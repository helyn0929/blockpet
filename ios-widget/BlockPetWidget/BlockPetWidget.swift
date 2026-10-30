import WidgetKit
import SwiftUI

// BlockPet 手機桌面小工具（設計文件 5.22）：靜態版，先用固定的假資料，還沒接 App 的資料（App Group 之後再做）。
// 長方形 systemMedium（①，還沒放右邊的最新照片，寵物在正中間）＝營地底圖＋寵物＋左上愛心＋進化條；
// 正方形 systemSmall（②）＝營地底圖＋寵物＋左上愛心。
// 尺寸以設計稿的 pt 為準：長方形 338 × 158、正方形 158 × 158；實際大小依機型不同，位置照比例換算。

// MARK: - 資料

struct PetEntry: TimelineEntry {
    let date: Date
    /// 愛心 0～5（半顆為單位）
    let hearts: Double
    /// 進化條各色張數，順序：美食、自然、城市、生活人物、寵物
    let evolution: [Int]

    static let sample = PetEntry(date: Date(), hearts: 4, evolution: [28, 14, 8, 12, 6])
}

struct PetProvider: TimelineProvider {
    func placeholder(in context: Context) -> PetEntry { .sample }

    func getSnapshot(in context: Context, completion: @escaping (PetEntry) -> Void) {
        completion(.sample)
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<PetEntry>) -> Void) {
        // 靜態版：固定一筆，不排更新
        completion(Timeline(entries: [.sample], policy: .never))
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
    let counts: [Int]
    static let width: CGFloat = 96
    static let target = 100

    var body: some View {
        let total = counts.reduce(0, +)
        let filled = EvolutionBar.width * CGFloat(min(total, EvolutionBar.target)) / CGFloat(EvolutionBar.target)
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

/// 滿版營地底圖：寬度撐滿，往上偏移看到地面（同分割畫面的寵物框）
struct CampBackground: View {
    var body: some View {
        GeometryReader { g in
            let w = g.size.width
            let h = w * 1364 / 690 // 底圖比例
            Image("CampBackground")
                .resizable()
                .frame(width: w, height: h)
                .offset(y: -h * 0.41)
                .frame(width: w, height: g.size.height, alignment: .top)
                .clipped()
        }
    }
}

// MARK: - 畫面

struct BlockPetWidgetView: View {
    @Environment(\.widgetFamily) var family
    let entry: PetEntry

    var body: some View {
        FullBleed {
            GeometryReader { g in
                let medium = family == .systemMedium
                // 設計稿：長方形寵物 84 × 74、上緣 y 62（158 高）；正方形 88 × 76、上緣 y 46
                let pet = medium ? CGSize(width: 84, height: 74) : CGSize(width: 88, height: 76)
                let shadow = medium ? CGSize(width: 74, height: 11) : CGSize(width: 72, height: 11)
                let top = (medium ? 62 : 46) * g.size.height / 158

                ZStack(alignment: .topLeading) {
                    PetView(size: pet, shadow: shadow)
                        .position(x: g.size.width / 2, y: top + pet.height / 2)

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
