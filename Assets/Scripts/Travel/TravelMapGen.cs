using System;
using System.Collections.Generic;

namespace BlockPet.Travel
{
    /// <summary>地圖上的一個點（設計座標 390 × 772，原點左上、y 向下）。</summary>
    public struct MapPoint
    {
        public double x, y;
        public MapPoint(double x, double y) { this.x = x; this.y = y; }
        public static double Dist(MapPoint a, MapPoint b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
    }

    /// <summary>陸地的一個圓：三層（淺水 r+20、沙灘 r+7、草地 r）。</summary>
    public struct LandCircle
    {
        public MapPoint c;
        public double r;
    }

    /// <summary>第 i 個好友營地的位置（加好友時依序用，放好就固定）。</summary>
    public struct FriendSlot
    {
        public MapPoint pos;
        /// <summary>路的彎曲程度（-0.275～0.275）。</summary>
        public double bend;
        /// <summary>營地旁那塊地的方向與半徑。</summary>
        public double blobA, blobR;
    }

    /// <summary>家 → 好友的一條路（二次貝茲曲線）。</summary>
    public struct MapRoad
    {
        public MapPoint from, ctrl, to;
        public int trips;
        /// <summary>去過＝實線白路；沒去過＝細的白色虛線。</summary>
        public bool Visited => trips > 0;
        /// <summary>白路寬：去過 6 + min(趟數, 4)；沒去過 3.5（虛線）。</summary>
        public double Width => Visited ? 6 + Math.Min(trips, 4) : 3.5;
        /// <summary>外框寬（#DCEBC8）：去過 11 + min(趟數, 4)；沒去過沒有外框。</summary>
        public double EdgeWidth => Visited ? 11 + Math.Min(trips, 4) : 0;

        public MapPoint At(double t) => TravelMapGen.Bezier(from, ctrl, to, t);
    }

    /// <summary>橢圓（湖、小山丘）。</summary>
    public struct MapEllipse
    {
        public MapPoint c;
        public double rx, ry;
    }

    /// <summary>樹：圓形樹冠（深色 #5A9548 或一般 #6BAA55），左上亮點、下方淡影。</summary>
    public struct MapTree
    {
        public MapPoint c;
        public double r;
        public bool dark;
    }

    /// <summary>小花：顏色 0 黃 #F6C945、1 粉 #F28FA6、2 白。</summary>
    public struct MapFlower
    {
        public MapPoint c;
        public int color;
    }

    /// <summary>路邊小物：0 長椅、1 路牌、2 花圃、3 路燈。</summary>
    public struct MapProp
    {
        public MapPoint c;
        public int kind;
    }

    /// <summary>生成好的地圖：陸地、家、好友營地、路、地形裝飾、路邊小物、下一塊地的雲霧。</summary>
    public class TravelMap
    {
        public MapPoint home;
        public List<LandCircle> land = new List<LandCircle>();
        public List<FriendSlot> slots = new List<FriendSlot>();
        /// <summary>目前好友各自用的格子（和 friends、roads 同順序）。</summary>
        public List<int> slotIndices = new List<int>();
        /// <summary>目前好友營地的位置（大頭針；x 夾在 52～338，設計稿 nodes）。</summary>
        public List<MapPoint> friends = new List<MapPoint>();
        public List<MapRoad> roads = new List<MapRoad>();
        public List<MapEllipse> lakes = new List<MapEllipse>();
        public List<MapEllipse> hills = new List<MapEllipse>();
        /// <summary>樹（依 y 由上往下排，下面的蓋住上面的）。</summary>
        public List<MapTree> trees = new List<MapTree>();
        public List<MapFlower> flowers = new List<MapFlower>();
        public List<MapProp> props = new List<MapProp>();
        /// <summary>下一個好友的位置有沒有雲霧（好友滿 10 個就沒有）。</summary>
        public bool hasNext;
        public MapPoint next;
        /// <summary>雲霧的白色圓（最後一個是中間那團）。</summary>
        public List<LandCircle> fog = new List<LandCircle>();
    }

    /// <summary>
    /// 旅行地圖生成（設計文件 5.21 ②；docs/travel/travel_spec.json 的 map_generation；照設計稿 TravelGrow 的程式移植）：
    /// 營地代碼做 FNV-1a 32bit hash 當種子，每種東西用不同 salt 開一條 mulberry32 亂數，所以加好友時舊的東西不會亂跳。
    /// 同一個營地代碼每次生成都一樣。
    /// </summary>
    public static class TravelMapGen
    {
        public const double CX = 195, CY = 380;
        /// <summary>好友位置先算好的數量（設計稿 MAX = 10，位置表多算一格給下一塊雲霧用）。</summary>
        public const int MaxFriends = 10;

        // salt（和設計稿一致）
        const uint SaltSlots0 = 1, SaltBlobs = 2, SaltSlot = 100;

        /// <summary>FNV-1a 32bit。</summary>
        public static uint Hash(string code)
        {
            uint h = 2166136261;
            foreach (char ch in code ?? "") { h ^= ch; h = unchecked(h * 16777619u); }
            return h;
        }

        /// <summary>mulberry32：每次回傳 0～1。</summary>
        public sealed class Rng
        {
            uint _a;
            public Rng(uint hash, uint salt) { _a = hash ^ unchecked((salt + 1u) * 2654435761u); }
            public double Next()
            {
                unchecked
                {
                    _a += 0x6D2B79F5u;
                    uint t = _a;
                    t = (t ^ (t >> 15)) * (t | 1u);
                    t ^= t + (t ^ (t >> 7)) * (t | 61u);
                    return (t ^ (t >> 14)) / 4294967296.0;
                }
            }
        }

        public static MapPoint Bezier(MapPoint p0, MapPoint c, MapPoint p1, double t)
        {
            double u = 1 - t;
            return new MapPoint(u * u * p0.x + 2 * u * t * c.x + t * t * p1.x, u * u * p0.y + 2 * u * t * c.y + t * t * p1.y);
        }

        /// <summary>好友營地的位置表（0～MaxFriends，共 11 格），只看營地代碼。</summary>
        public static List<FriendSlot> Slots(uint hash)
        {
            var home = new MapPoint(CX, CY);
            var r0 = new Rng(hash, SaltSlots0);
            double a0 = r0.Next() * Math.PI * 2, kx = 0.6 + r0.Next() * 0.08;
            var slots = new List<FriendSlot>();
            for (int i = 0; i <= MaxFriends; i++)
            {
                var r = new Rng(hash, SaltSlot + (uint)i);
                double a = a0 + i * 2.39996 + (r.Next() - 0.5) * 0.7;
                double R = 135 + i * 12 + r.Next() * 22;
                var q = new MapPoint();
                for (int tr = 0; tr < 10; tr++)
                {
                    q = new MapPoint(Clamp(CX + Math.Cos(a) * R * kx, 58, 332), Clamp(CY + Math.Sin(a) * R, 168, 600));
                    bool ok = MapPoint.Dist(q, home) > 100;
                    foreach (var s in slots) if (MapPoint.Dist(q, s.pos) < 86) ok = false;
                    if (ok) break;
                    a += 0.4; R += 10;
                }
                slots.Add(new FriendSlot { pos = q, bend = (r.Next() - 0.5) * 0.55, blobA = r.Next() * Math.PI * 2, blobR = 38 + r.Next() * 14 });
            }
            return slots;
        }

        /// <summary>路的控制點＝中點＋垂直方向 × bend。</summary>
        public static MapPoint Ctrl(FriendSlot s)
        {
            double dx = s.pos.x - CX, dy = s.pos.y - CY;
            return new MapPoint((CX + s.pos.x) / 2 - dy * s.bend, (CY + s.pos.y) / 2 + dx * s.bend);
        }

        /// <summary>地圖的成長階段（設計稿 0／3／6／10 個好友＝階段 0～3），決定湖、小山丘、小花的數量。</summary>
        public static int Stage(int friendCount) => friendCount >= 10 ? 3 : friendCount >= 6 ? 2 : friendCount >= 3 ? 1 : 0;

        /// <summary>
        /// 生成地圖。trips[i]＝第 i 個好友去過幾趟（長度不足當 0）。
        /// 陸地：家 r112＋家旁 3 塊 r58～80＋每個好友：營地 r70、旁邊一塊 r38～52、路上 t=0.3／0.55／0.8 各一塊 r46。
        /// 裝飾（湖 1～2、小山丘 3＋階段、樹叢 60 叢、小花 14＋階段×8）只放在草地內、避開營地和路；路邊小物每條去過的路最多 3 樣。
        /// </summary>
        public static TravelMap Generate(string campCode, int friendCount, IList<int> trips = null)
        {
            int n = Math.Max(0, Math.Min(MaxFriends, friendCount));
            var slotIdx = new int[n];
            for (int i = 0; i < n; i++) slotIdx[i] = i;
            return Generate(campCode, slotIdx, trips);
        }

        /// <summary>
        /// 用固定的好友位置生成（真的好友：每個好友放在加好友時分到的那一格，刪掉好友後其他人不會移動）。
        /// slotIndices[i]＝第 i 個好友的格子（0～9）；trips[i]＝去過幾趟。雲霧放在還沒用的最小格子。
        /// </summary>
        public static TravelMap Generate(string campCode, IList<int> slotIndices, IList<int> trips = null)
        {
            uint hash = Hash(campCode);
            var used = new List<int>();
            if (slotIndices != null) foreach (var si in slotIndices) if (si >= 0 && si < MaxFriends && !used.Contains(si) && used.Count < MaxFriends) used.Add(si);
            int n = used.Count;
            var map = new TravelMap { home = new MapPoint(CX, CY) };
            map.slotIndices = used;
            map.slots = Slots(hash);

            map.land.Add(new LandCircle { c = map.home, r = 112 });
            var rb = new Rng(hash, SaltBlobs);
            for (int b = 0; b < 3; b++)
            {
                double ang = rb.Next() * Math.PI * 2, d = 60 + rb.Next() * 40;
                double x = CX + Math.Cos(ang) * d * 0.8, y = CY + Math.Sin(ang) * d;
                map.land.Add(new LandCircle { c = new MapPoint(x, y), r = 58 + rb.Next() * 22 });
            }
            for (int m = 0; m < n; m++)
            {
                var s = map.slots[used[m]];
                var c = Ctrl(s);
                map.land.Add(new LandCircle { c = s.pos, r = 70 });
                map.land.Add(new LandCircle { c = new MapPoint(s.pos.x + Math.Cos(s.blobA) * 40, s.pos.y + Math.Sin(s.blobA) * 40), r = s.blobR });
                foreach (double t in new[] { 0.3, 0.55, 0.8 })
                    map.land.Add(new LandCircle { c = Bezier(map.home, c, s.pos, t), r = 46 });

                map.friends.Add(new MapPoint(Clamp(s.pos.x, 52, 338), s.pos.y));
                int tv = trips != null && m < trips.Count ? Math.Max(0, trips[m]) : 0;
                map.roads.Add(new MapRoad { from = map.home, ctrl = c, to = s.pos, trips = tv });
            }
            Decorate(map, hash, n, Stage(n));
            return map;
        }

        // ─── 裝飾（照設計稿 TravelGrow：各用自己的 salt，順序和條件都一樣）───────

        static void Decorate(TravelMap map, uint hash, int n, int stage)
        {
            var home = map.home;
            var land = map.land;
            bool InLand(MapPoint p, double margin)
            {
                foreach (var c in land) if (MapPoint.Dist(p, c.c) < c.r - margin) return true;
                return false;
            }
            var roadPts = new List<MapPoint>();
            foreach (var r in map.roads) for (int t = 0; t <= 20; t++) roadPts.Add(r.At(t / 20.0));
            bool Clear(MapPoint p, double nodeGap, double roadGap)
            {
                if (MapPoint.Dist(p, home) < nodeGap + 14) return false;
                foreach (int si in map.slotIndices) if (MapPoint.Dist(p, map.slots[si].pos) < nodeGap) return false;
                foreach (var q in roadPts) if (MapPoint.Dist(p, q) < roadGap) return false;
                return true;
            }
            MapPoint BboxPt(Rng g) { double x = 20 + g.Next() * 350; double y = 90 + g.Next() * 600; return new MapPoint(x, y); }
            bool AwayLake(MapPoint p, double gap)
            {
                foreach (var l in map.lakes) if (MapPoint.Dist(p, l.c) < l.rx + gap) return false;
                return true;
            }

            // 湖
            var rl = new Rng(hash, 700);
            for (int i = 0; i < 40 && map.lakes.Count < (stage >= 2 ? 2 : 1); i++)
            {
                var p = BboxPt(rl);
                double rx = 16 + rl.Next() * 12;
                if (InLand(p, rx + 4) && Clear(p, rx + 30, rx + 10)) map.lakes.Add(new MapEllipse { c = p, rx = rx, ry = rx * 0.62 });
            }
            // 小山丘
            var rh = new Rng(hash, 600);
            for (int i = 0; i < 40 && map.hills.Count < 3 + stage; i++)
            {
                var p = BboxPt(rh);
                double rx = 20 + rh.Next() * 14;
                if (InLand(p, rx * 0.7) && Clear(p, rx + 34, rx * 0.6 + 8) && AwayLake(p, rx + 6)) map.hills.Add(new MapEllipse { c = p, rx = rx, ry = rx * 0.5 });
            }
            // 樹叢：60 叢 × 3～8 棵
            var rt = new Rng(hash, 500);
            for (int ci = 0; ci < 60; ci++)
            {
                var cp = BboxPt(rt);
                int cnt = 3 + (int)Math.Floor(rt.Next() * 6);
                for (int ti = 0; ti < cnt; ti++)
                {
                    double x = cp.x + (rt.Next() - 0.5) * 44;
                    double y = cp.y + (rt.Next() - 0.5) * 34;
                    var p = new MapPoint(x, y);
                    double r = 5.5 + rt.Next() * 3.5;
                    if (!InLand(p, 10) || !Clear(p, 40, 12) || !AwayLake(p, 8)) continue;
                    map.trees.Add(new MapTree { c = p, r = r, dark = rt.Next() < 0.3 });
                }
            }
            // 依 y 排（JS 的 sort 是穩定排序）
            var sorted = new List<(MapTree t, int i)>();
            for (int i = 0; i < map.trees.Count; i++) sorted.Add((map.trees[i], i));
            sorted.Sort((a, b) => a.t.c.y != b.t.c.y ? a.t.c.y.CompareTo(b.t.c.y) : a.i.CompareTo(b.i));
            map.trees = sorted.ConvertAll(x => x.t);
            // 小花
            var rf = new Rng(hash, 800);
            for (int i = 0; i < 160 && map.flowers.Count < 14 + stage * 8; i++)
            {
                var p = BboxPt(rf);
                if (InLand(p, 8) && Clear(p, 36, 8) && AwayLake(p, 5)) map.flowers.Add(new MapFlower { c = p, color = (int)Math.Floor(rf.Next() * 3) });
            }
            // 路邊小物：每條去過的路最多 3 樣，t = 0.32／0.52／0.72，左右交錯偏 14
            double[] ts = { 0.32, 0.52, 0.72 };
            for (int i = 0; i < n; i++)
            {
                var rp = new Rng(hash, 400 + (uint)map.slotIndices[i]); // 每個格子自己的小物（和設計稿一樣用格子編號）
                var road = map.roads[i];
                int cnt = Math.Min(3, road.trips);
                for (int k = 0; k < cnt; k++)
                {
                    var p0 = road.At(ts[k]);
                    var p1 = road.At(ts[k] + 0.02);
                    double dx = p1.x - p0.x, dy = p1.y - p0.y, len = Math.Sqrt(dx * dx + dy * dy);
                    if (len == 0) len = 1;
                    int side = k % 2 == 1 ? 1 : -1;
                    map.props.Add(new MapProp { c = new MapPoint(p0.x - dy / len * 14 * side, p0.y + dx / len * 14 * side), kind = (int)Math.Floor(rp.Next() * 4) });
                }
            }
            // 下一塊地的雲霧
            // 下一塊地＝還沒用的最小格子（好友都照順序加時就是第 n 格，和設計稿一樣）
            int nextSlot = -1;
            for (int i = 0; i < MaxFriends; i++) if (!map.slotIndices.Contains(i)) { nextSlot = i; break; }
            map.hasNext = nextSlot >= 0;
            if (map.hasNext)
            {
                var nx = map.slots[nextSlot].pos;
                map.next = nx;
                var rg = new Rng(hash, 900 + (uint)nextSlot);
                for (int g = 0; g < 9; g++)
                {
                    double ga = g / 9.0 * Math.PI * 2;
                    double x = nx.x + Math.Cos(ga) * 26 + (rg.Next() - 0.5) * 10;
                    double y = nx.y + Math.Sin(ga) * 16 + (rg.Next() - 0.5) * 8;
                    map.fog.Add(new LandCircle { c = new MapPoint(x, y), r = 18 + rg.Next() * 10 });
                }
                map.fog.Add(new LandCircle { c = nx, r = 28 });
            }
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        // ─── 假資料（第一步測試用；之後換成 Firebase 的營地好友）────────────

        static readonly string[] DemoNames = { "小黑的辦公室營地", "團子的小窩", "海邊小屋", "阿嬤的菜園", "阿凱的宿舍", "咪咪咖啡店", "山上的家", "小雨的陽台", "公司茶水間", "外婆家" };

        /// <summary>
        /// 設計稿示範用的好友名字與趟數（同一個營地代碼每次一樣）。
        /// stage：設計稿的階段（1＝3 個好友、2＝6 個、3＝10 個），影響假的趟數。
        /// </summary>
        public static void DemoFriends(string campCode, int friendCount, int stage, out string[] names, out int[] trips)
        {
            uint hash = Hash(campCode);
            var list = (string[])DemoNames.Clone();
            var rn = new Rng(hash, 950);
            for (int s1 = list.Length - 1; s1 > 0; s1--)
            {
                int s2 = (int)Math.Floor(rn.Next() * (s1 + 1));
                (list[s1], list[s2]) = (list[s2], list[s1]);
            }
            // 設計稿營地 A（K7R2PX）把「小黑的辦公室營地」放第一個
            if (campCode == "K7R2PX")
            {
                int k = Array.IndexOf(list, "小黑的辦公室營地");
                list[k] = list[0];
                list[0] = "小黑的辦公室營地";
            }
            int n = Math.Max(0, Math.Min(MaxFriends, friendCount));
            names = new string[n];
            trips = new int[n];
            for (int i = 0; i < n; i++)
            {
                names[i] = list[i];
                var rt = new Rng(hash, 300 + (uint)i);
                trips[i] = Math.Max(0, (int)Math.Floor(rt.Next() * (stage * 1.6 + 0.6) - i * 0.35 + 0.5)); // JS Math.round
            }
        }
    }
}
