import WidgetKit
import SwiftUI

// BlockPet 手機桌面小工具（設計文件 5.22；設計稿 WidgetC、WidgetS）。
// 資料由 Unity App 寫進 App Group 共用資料夾的 widget/（Assets/Scripts/Widget/WidgetSync.cs）：state.json＋latest.jpg。
// 長方形 systemMedium（WidgetC）＝營地底圖＋寵物＋左上愛心＋進化條＋右邊營地最新照片（拍立得）；沒有照片時寵物回到正中間。
// 正方形 systemSmall（WidgetS）＝滿版營地最新照片＋左下小小的寵物；沒有照片時改成營地底圖＋寵物置中。
// 尺寸以設計稿的 pt 為準：長方形 338 × 158、正方形 158 × 158；實際大小依機型不同，位置照比例換算。

// MARK: - 資料

/// App Group（跟 Unity 的 WidgetSync.AppGroupId、兩邊的 entitlements 一樣）
let appGroupId = "group.com.linnTech.blockpet"

struct PetEntry: TimelineEntry {
    let date: Date
    /// 愛心 0～5（半顆為單位）
    let hearts: Double
    /// 進化條各色張數，順序：美食、自然、城市、生活人物、寵物
    let evolution: [Double]
    /// 營地最新照片（nil＝營地還沒有照片）
    let photo: LatestPhoto?

    /// 小工具選單裡的預覽、App 還沒寫過資料時的示意（設計稿的示範照片）
    static let sample = PetEntry(date: Date(), hearts: 4, evolution: [28, 14, 8, 12, 6],
                                 photo: LatestPhoto(image: Image("LatestPhoto"), who: "小美", initial: "美", ago: "3 分鐘前", color: Color(hex: 0xE88AA8)))
}

/// 營地最新照片
struct LatestPhoto {
    let image: Image
    let who: String
    let initial: String
    let ago: String
    let color: Color
}

/// App 寫的 state.json
struct SharedState: Decodable {
    struct Photo: Decodable {
        let file: String
        let who: String
        let initial: String
        let color: String
        let takenAt: Int64
    }
    let hearts: Double
    let nextDropAt: Int64      // 下一次掉半顆的時間（毫秒）；0＝不知道
    let dropHours: Double
    let evolution: [Double]
    let photo: Photo?

    static func load() -> (SharedState, URL)? {
        guard let root = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroupId) else { return nil }
        let dir = root.appendingPathComponent("widget", isDirectory: true)
        guard let data = try? Data(contentsOf: dir.appendingPathComponent("state.json")),
              let state = try? JSONDecoder().decode(SharedState.self, from: data) else { return nil }
        return (state, dir)
    }

    /// 某個時間點的愛心：過了下一次掉心的時間，每 dropHours 小時再掉半顆
    func hearts(at date: Date) -> Double {
        guard nextDropAt > 0 else { return hearts }
        let next = Date(timeIntervalSince1970: Double(nextDropAt) / 1000)
        guard date >= next else { return hearts }
        let steps = 1 + floor(date.timeIntervalSince(next) / (max(dropHours, 0.1) * 3600))
        return max(0, hearts - 0.5 * steps)
    }
}

/// 「剛剛／3 分鐘前／2 小時前／昨天／4 天前」（同分割畫面）
func agoText(_ taken: Date, at now: Date) -> String {
    let s = now.timeIntervalSince(taken)
    if s < 60 { return "剛剛" }
    if s < 3600 { return "\(Int(s / 60)) 分鐘前" }
    if s < 86400 { return "\(Int(s / 3600)) 小時前" }
    if s < 172800 { return "昨天" }
    return "\(Int(s / 86400)) 天前"
}

extension Color {
    init(hexString: String) {
        let v = UInt32(hexString.trimmingCharacters(in: CharacterSet(charactersIn: "#")), radix: 16) ?? 0x8A7A66
        self.init(hex: v)
    }
}

struct PetProvider: TimelineProvider {
    func placeholder(in context: Context) -> PetEntry { .sample }

    func getSnapshot(in context: Context, completion: @escaping (PetEntry) -> Void) {
        // 小工具選單的預覽：有資料就用真的，沒有就用示意
        completion(entries(from: Date(), count: 1).first ?? .sample)
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<PetEntry>) -> Void) {
        // 每 15 分鐘一筆（更新「N 分鐘前」和愛心），共 2 小時；之後系統再來要新的。App 有新資料時會主動通知重畫。
        let now = Date()
        let list = entries(from: now, count: 8)
        if list.isEmpty {
            // App 還沒寫過資料（還沒登入、還沒打開過）：營地＋寵物，沒有照片
            let empty = PetEntry(date: now, hearts: 5, evolution: [], photo: nil)
            completion(Timeline(entries: [empty], policy: .after(now.addingTimeInterval(30 * 60))))
            return
        }
        completion(Timeline(entries: list, policy: .after(now.addingTimeInterval(2 * 3600))))
    }

    func entries(from start: Date, count: Int) -> [PetEntry] {
        guard let (state, dir) = SharedState.load() else { return [] }
        var image: Image? = nil
        if let p = state.photo, let ui = UIImage(contentsOfFile: dir.appendingPathComponent(p.file).path) {
            image = Image(uiImage: ui)
        }
        return (0..<count).map { i in
            let date = start.addingTimeInterval(Double(i) * 15 * 60)
            var photo: LatestPhoto? = nil
            if let p = state.photo, let image = image {
                let taken = Date(timeIntervalSince1970: Double(p.takenAt))
                photo = LatestPhoto(image: image, who: p.who, initial: p.initial,
                                    ago: p.takenAt > 0 ? agoText(taken, at: date) : "", color: Color(hexString: p.color))
            }
            return PetEntry(date: date, hearts: state.hearts(at: date), evolution: state.evolution, photo: photo)
        }
    }
}

// MARK: - 樣式

enum Palette {
    static let heartFull = Color(hex: 0xF06B5B)
    static let heartEmpty = Color(hex: 0xE6D9C3)
    /// 五色進化條（同 5.5）：美食、自然、城市、生活人物、寵物
    static let evolution: [Color] = [
        Color(hex: 0xF29A3A), Color(hex: 0x6FA45F), Color(hex: 0x6F8FE0), Color(hex: 0xD78AD0), Color(hex: 0xE8C33A),
    ]
    static let petShadow = Color(red: 30 / 255, green: 45 / 255, blue: 20 / 255).opacity(0.3)
}

extension Color {
    init(hex: UInt32) {
        self.init(red: Double((hex >> 16) & 0xFF) / 255, green: Double((hex >> 8) & 0xFF) / 255, blue: Double(hex & 0xFF) / 255)
    }
}

// MARK: - 元件

/// 愛心形狀（14 × 13）
struct HeartShape: Shape {
    func path(in r: CGRect) -> Path {
        func p(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: r.minX + x * r.width, y: r.minY + y * r.height) }
        var path = Path()
        path.move(to: p(0.5, 0.96))
        path.addCurve(to: p(0.04, 0.4), control1: p(0.3, 0.78), control2: p(0.04, 0.62))
        path.addCurve(to: p(0.5, 0.2), control1: p(0.04, 0.06), control2: p(0.42, 0.0))
        path.addCurve(to: p(0.96, 0.4), control1: p(0.58, 0.0), control2: p(0.96, 0.06))
        path.addCurve(to: p(0.5, 0.96), control1: p(0.96, 0.62), control2: p(0.7, 0.78))
        path.closeSubpath()
        return path
    }
}

/// 一顆愛心：fill 0～1（半顆時左半邊是滿的），白邊 1.2
struct HeartView: View {
    let fill: Double

    var body: some View {
        ZStack {
            HeartShape().fill(Palette.heartEmpty)
            HeartShape().fill(Palette.heartFull)
                .mask(
                    HStack(spacing: 0) {
                        Rectangle().frame(width: 14 * CGFloat(fill))
                        Spacer(minLength: 0)
                    }
                )
            HeartShape().stroke(Color.white, lineWidth: 1.2)
        }
        .frame(width: 14, height: 13)
    }
}

/// 5 顆愛心，間距 2；沒有底框，整組陰影 0 1 2 rgba(0,0,0,0.35)
struct HeartsRow: View {
    let hearts: Double

    var body: some View {
        HStack(spacing: 2) {
            ForEach(0..<5, id: \.self) { i in
                HeartView(fill: min(1, max(0, hearts - Double(i))))
            }
        }
        .compositingGroup()
        .shadow(color: Color.black.opacity(0.35), radius: 1, x: 0, y: 1)
    }
}

/// 進化條 96 × 8：圓角 4、軌道白 75%、五色分段（段間隔 1），長度＝已餵張數／100，不寫數字
struct EvolutionBar: View {
    let counts: [Double]
    static let width: CGFloat = 96
    static let target: Double = 100

    var body: some View {
        let total = counts.reduce(0, +)
        let filled = EvolutionBar.width * CGFloat(min(total, EvolutionBar.target) / EvolutionBar.target)
        let used = counts.indices.filter { counts[$0] > 0 }
        let gaps = CGFloat(max(0, used.count - 1))
        let available = max(0, filled - gaps)

        return ZStack(alignment: .leading) {
            RoundedRectangle(cornerRadius: 4).fill(Color.white.opacity(0.75))
            HStack(spacing: 1) {
                ForEach(used, id: \.self) { i in
                    Rectangle()
                        .fill(Palette.evolution[i % Palette.evolution.count])
                        .frame(width: total > 0 ? available * CGFloat(counts[i]) / CGFloat(total) : 0)
                }
            }
        }
        .frame(width: EvolutionBar.width, height: 8)
        .clipShape(RoundedRectangle(cornerRadius: 4))
    }
}

/// 寵物＋腳下半透明橢圓陰影
struct PetView: View {
    let size: CGSize
    let shadow: CGSize

    var body: some View {
        ZStack(alignment: .bottom) {
            Ellipse()
                .fill(Palette.petShadow)
                .frame(width: shadow.width, height: shadow.height)
                .offset(y: shadow.height / 2 - 2)
            Image("Pet")
                .resizable()
                .scaledToFit()
                .frame(width: size.width, height: size.height)
        }
        .frame(width: size.width, height: size.height)
    }
}

/// 滿版營地底圖：寬度撐滿，往上偏移看到地面（設計稿 WidgetC：338 寬時圖高 668、往上 340）
struct CampBackground: View {
    var body: some View {
        GeometryReader { g in
            let w = g.size.width
            let h = w * 1364 / 690 // 底圖比例
            Image("CampBackground")
                .resizable()
                .frame(width: w, height: h)
                .offset(y: -h * 340 / 668)
                .frame(width: w, height: g.size.height, alignment: .top)
                .clipped()
        }
    }
}

/// 長方形右邊的拍立得：寬 104、內距 6 6 7、圓角 4、陰影 0 4 12 rgba(0,0,0,0.3)、轉 4°；
/// 照片 92 × 92（圓角 2）；下面「頭像 13 圓＋小美・3 分鐘前」9 Black #8A6A4E
struct PolaroidView: View {
    let photo: LatestPhoto

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            photo.image
                .resizable()
                .scaledToFill()
                .frame(width: 92, height: 92)
                .clipShape(RoundedRectangle(cornerRadius: 2))
            HStack(spacing: 4) {
                Circle()
                    .fill(photo.color)
                    .frame(width: 13, height: 13)
                    .overlay(Text(photo.initial).font(.system(size: 8, weight: .black)).foregroundColor(.white))
                Text(photo.ago.isEmpty ? photo.who : "\(photo.who)・\(photo.ago)")
                    .font(.system(size: 9, weight: .black))
                    .foregroundColor(Color(hex: 0x8A6A4E))
                    .lineLimit(1)
            }
        }
        .padding(EdgeInsets(top: 6, leading: 6, bottom: 7, trailing: 6))
        .frame(width: 104, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 4).fill(Color(hex: 0xFFFCF4)))
        .shadow(color: Color.black.opacity(0.3), radius: 6, x: 0, y: 4)
        .rotationEffect(.degrees(4))
    }
}

/// 正方形：滿版最新照片＋下方 64 漸層變暗（讓寵物看得清楚）
struct PhotoBackground: View {
    let photo: LatestPhoto

    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .bottom) {
                photo.image
                    .resizable()
                    .scaledToFill()
                    .frame(width: g.size.width, height: g.size.height)
                    .clipped()
                LinearGradient(colors: [Color.black.opacity(0), Color.black.opacity(0.35)], startPoint: .top, endPoint: .bottom)
                    .frame(height: 64 * g.size.height / 158)
            }
        }
    }
}

// MARK: - 畫面

struct BlockPetWidgetView: View {
    @Environment(\.widgetFamily) var family
    let entry: PetEntry

    var body: some View {
        if family == .systemSmall, let photo = entry.photo {
            smallWithPhoto(photo)
        } else {
            camp
        }
    }

    /// 營地畫面：長方形（有照片時寵物在 (87, 62)、右邊拍立得；沒有照片寵物置中），或沒有照片的正方形
    var camp: some View {
        FullBleed {
            GeometryReader { g in
                let medium = family == .systemMedium
                let photo = medium ? entry.photo : nil
                // 設計稿：長方形寵物 84 × 74、上緣 y 62、左 87（有照片時；338 × 158）；正方形 88 × 76、上緣 y 46、置中
                let pet = medium ? CGSize(width: 84, height: 74) : CGSize(width: 88, height: 76)
                let shadow = medium ? CGSize(width: 74, height: 11) : CGSize(width: 72, height: 11)
                let sx = g.size.width / (medium ? 338 : 158)
                let sy = g.size.height / 158
                let top = (medium ? 62 : 46) * sy
                let petX = photo != nil ? (87 + pet.width / 2) * sx : g.size.width / 2

                ZStack(alignment: .topLeading) {
                    PetView(size: pet, shadow: shadow)
                        .position(x: petX, y: top + pet.height / 2)

                    if let photo = photo {
                        // 拍立得左上 (218, 22)，寬 104；高度約 92 + 6 + 7 + 4 + 13 = 122
                        PolaroidView(photo: photo)
                            .position(x: (218 + 52) * sx, y: (22 + 61) * sy)
                    }

                    VStack(alignment: .leading, spacing: 5) {
                        HeartsRow(hearts: entry.hearts)
                        if medium { EvolutionBar(counts: entry.evolution) }
                    }
                    .padding(.leading, 12)
                    .padding(.top, 12)
                }
                .frame(width: g.size.width, height: g.size.height, alignment: .topLeading)
            }
        }
        .widgetBackground { CampBackground() }
    }

    /// 正方形（WidgetS）：滿版營地最新照片＋左下小小的寵物（48 × 41，左 8、下 12；腳下陰影 40 × 7，左 12、下 9）
    func smallWithPhoto(_ photo: LatestPhoto) -> some View {
        FullBleed {
            GeometryReader { g in
                ZStack(alignment: .bottomLeading) {
                    Color.clear
                    Ellipse()
                        .fill(Color.black.opacity(0.3))
                        .frame(width: 40, height: 7)
                        .padding(.leading, 12)
                        .padding(.bottom, 9)
                    Image("Pet")
                        .resizable()
                        .scaledToFit()
                        .frame(width: 48, height: 41)
                        .shadow(color: Color.black.opacity(0.3), radius: 1, x: 0, y: 1)
                        .padding(.leading, 8)
                        .padding(.bottom, 12)
                }
                .frame(width: g.size.width, height: g.size.height)
            }
        }
        .widgetBackground { PhotoBackground(photo: photo) }
    }
}

// MARK: - iOS 15～16／17 相容

extension View {
    /// iOS 17 以上用 containerBackground（系統規定的寫法）；15～16 用一般背景。
    @ViewBuilder
    func widgetBackground<B: View>(@ViewBuilder _ background: () -> B) -> some View {
        if #available(iOSApplicationExtension 17.0, *) {
            containerBackground(for: .widget) { background() }
        } else {
            self.background(background())
        }
    }
}

/// iOS 17 起小工具內容有系統內距（content margins），這裡抵掉，讓位置照設計稿從邊緣算起；15～16 沒有內距。
struct FullBleed<Content: View>: View {
    @ViewBuilder let content: () -> Content

    var body: some View {
        if #available(iOSApplicationExtension 17.0, *) {
            MarginCancel(content: content)
        } else {
            content()
        }
    }
}

@available(iOSApplicationExtension 17.0, *)
private struct MarginCancel<Content: View>: View {
    @Environment(\.widgetContentMargins) var margins
    let content: () -> Content

    var body: some View {
        content().padding(EdgeInsets(top: -margins.top, leading: -margins.leading, bottom: -margins.bottom, trailing: -margins.trailing))
    }
}

// MARK: - Widget

@main
struct BlockPetWidget: Widget {
    let kind = "BlockPetWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: PetProvider()) { entry in
            BlockPetWidgetView(entry: entry)
        }
        .configurationDisplayName("BlockPet")
        .description("在桌面看看你的寵物和營地。")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}
