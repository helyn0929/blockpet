using System;

namespace BlockPet.Travel
{
    /// <summary>旅行的階段（全部用伺服器時間算，每台手機算出來一樣）。</summary>
    public enum TripPhase { None, Going, There, Returning, Done }

    /// <summary>
    /// 派寵物旅行的規則（設計文件 5.21 ①③；travel_spec.json travel_rules）：
    /// 單程依地圖距離 15～75 分（距離 ÷ 3.5，取 5 的倍數）、在對方營地玩 30 分、一次只能去一個地方。
    /// 旅行中愛心照常（2026-11-06 定）；對方營地同時有好幾隻客人時只顯示最早到的（2026-11-06 定）。
    /// </summary>
    public static class TripRules
    {
        public const int StayMinutes = 30;
        const long MinMs = 60L * 1000L;

        /// <summary>單程幾分：家到好友營地的距離 ÷ 3.5，取 5 的倍數，夾在 15～75（照設計稿 TravelGrow）。</summary>
        public static int OneWayMinutes(MapPoint home, MapPoint friend)
        {
            double d = MapPoint.Dist(home, friend);
            int m = (int)Math.Floor(d / 3.5 / 5 + 0.5) * 5; // JS Math.round
            return Math.Max(15, Math.Min(75, m));
        }

        public static long ArriveAt(long departAt, int oneWay) => departAt + oneWay * MinMs;
        public static long LeaveAt(long departAt, int oneWay, int stay = StayMinutes) => ArriveAt(departAt, oneWay) + stay * MinMs;
        public static long HomeAt(long departAt, int oneWay, int stay = StayMinutes) => LeaveAt(departAt, oneWay, stay) + oneWay * MinMs;

        public static TripPhase Phase(long now, long departAt, int oneWay, int stay = StayMinutes)
        {
            if (departAt <= 0) return TripPhase.None;
            if (now < ArriveAt(departAt, oneWay)) return TripPhase.Going;
            if (now < LeaveAt(departAt, oneWay, stay)) return TripPhase.There;
            if (now < HomeAt(departAt, oneWay, stay)) return TripPhase.Returning;
            return TripPhase.Done;
        }

        /// <summary>寵物在路上的位置（0＝家、1＝好友營地）：去程 0→1、在那邊 1、回程 1→0。</summary>
        public static double RoadT(long now, long departAt, int oneWay, int stay = StayMinutes)
        {
            switch (Phase(now, departAt, oneWay, stay))
            {
                case TripPhase.Going: return Clamp01((now - departAt) / (double)(oneWay * MinMs));
                case TripPhase.There: return 1;
                case TripPhase.Returning: return 1 - Clamp01((now - LeaveAt(departAt, oneWay, stay)) / (double)(oneWay * MinMs));
                default: return 0;
            }
        }

        /// <summary>整趟的進度 0～1（狀態卡的進度條）。</summary>
        public static double Progress(long now, long departAt, int oneWay, int stay = StayMinutes)
        {
            long total = HomeAt(departAt, oneWay, stay) - departAt;
            return total <= 0 ? 1 : Clamp01((now - departAt) / (double)total);
        }

        /// <summary>到某個時間還要幾分（無條件進位，最少 1 分）。</summary>
        public static int MinutesUntil(long now, long at)
        {
            if (at <= now) return 0;
            return (int)Math.Max(1, Math.Ceiling((at - now) / (double)MinMs));
        }

        /// <summary>「1 小時 15 分」「35 分」（照設計稿 fmt）。</summary>
        public static string FormatMinutes(int m)
        {
            m = Math.Max(0, m);
            int h = m / 60, mm = m % 60;
            string s = (h > 0 ? h + " 小時" : "") + (h > 0 && mm > 0 ? " " : "") + (mm > 0 ? mm + " 分" : "");
            return s.Length == 0 ? "0 分" : s;
        }

        /// <summary>護照的章顏色（每個營地固定一色，依營地 id）。</summary>
        public static readonly string[] StampColors = { "#C2452B", "#4F8F45", "#3F5BA8", "#B8891F" };

        public static string StampColor(string roomId) => StampColors[TravelMapGen.Hash(roomId) % (uint)StampColors.Length];

        /// <summary>章歪的角度 -8～10°（依營地 id 固定）。</summary>
        public static float StampTilt(string roomId) => -8f + TravelMapGen.Hash(roomId + "#tilt") % 19;

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
