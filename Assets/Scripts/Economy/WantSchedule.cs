using System;
using System.Collections.Generic;

namespace BlockPet.Feed
{
    /// <summary>
    /// 「現在想吃」的時段與抽選（依據：docs/design/blockpet-economy-spec.md 1.3「現在想吃每 2 小時換一次，以伺服器時間為準」）。
    /// - 時段：台灣時間偶數整點換（00:00、02:00…22:00），所有空間同時換（2026-11-05 定）。
    /// - 全空間同一個：用空間 ID＋時段編號算出來，每台手機算的都一樣，不用存在伺服器。
    /// - 偏愛類別（<see cref="PetPreferences"/>）合計 70%、其他類別平分 30%；不會和上一個時段一樣（2026-11-05 定）。
    /// 「不和上一個時段一樣」要知道上一個時段真正的結果，所以從固定起點一段一段往後算，算過的記住、下次接著算。
    /// </summary>
    public static class WantSchedule
    {
        public const int SlotHours = 2;
        const int TaipeiOffsetHours = 8;
        const long HourMs = 3600L * 1000L;
        const long SlotMs = SlotHours * HourMs;
        const long OffsetMs = TaipeiOffsetHours * HourMs;

        /// <summary>起點：2026-01-01 00:00 台灣時間所在的時段。</summary>
        static readonly long EpochSlot = SlotOf(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(TaipeiOffsetHours)).ToUnixTimeMilliseconds());

        // 每個「空間｜寵物」算到哪個時段、那時段的結果
        static readonly Dictionary<string, KeyValuePair<long, string>> Progress = new Dictionary<string, KeyValuePair<long, string>>();

        /// <summary>某個時間（UTC 毫秒）屬於第幾個時段。</summary>
        public static long SlotOf(long utcMs) => FloorDiv(utcMs + OffsetMs, SlotMs);

        /// <summary>時段開始的時間（UTC 毫秒）。</summary>
        public static long SlotStartUtcMs(long slot) => slot * SlotMs - OffsetMs;

        /// <summary>離下一次換還有多久。</summary>
        public static TimeSpan UntilNextChange(long utcMs) => TimeSpan.FromMilliseconds(SlotStartUtcMs(SlotOf(utcMs) + 1) - utcMs);

        /// <summary>某空間、某寵物在 utcMs 那一刻想吃的類別。</summary>
        public static string At(string roomId, int petIndex, long utcMs) => For(roomId, petIndex, SlotOf(utcMs));

        /// <summary>某空間、某寵物在第 slot 個時段想吃的類別。</summary>
        public static string For(string roomId, int petIndex, long slot)
        {
            string key = roomId + "|" + petIndex;
            var fav = PetPreferences.FavoritesOf(petIndex);

            long s;
            string prev;
            if (Progress.TryGetValue(key, out var p) && p.Key <= slot)
            {
                if (p.Key == slot) return p.Value;
                s = p.Key + 1;
                prev = p.Value;
            }
            else
            {
                // 沒算過，或要查更早的時段：從起點重算（起點之前的時段直接從那段開始）
                s = slot < EpochSlot ? slot : EpochSlot;
                prev = null;
            }
            for (; s <= slot; s++) prev = Pick(roomId, s, fav, prev);
            Progress[key] = new KeyValuePair<long, string>(slot, prev);
            return prev;
        }

        static string Pick(string roomId, long slot, string[] fav, string exclude)
        {
            var cats = PetPreferences.Categories;
            var weights = new float[cats.Length];
            int favCount = 0, otherCount = 0;
            foreach (var c in cats)
            {
                if (c == exclude) continue;
                if (Array.IndexOf(fav, c) >= 0) favCount++; else otherCount++;
            }
            float total = 0f;
            for (int i = 0; i < cats.Length; i++)
            {
                if (cats[i] == exclude) continue;
                bool isFav = Array.IndexOf(fav, cats[i]) >= 0;
                // 某一邊沒有可選的類別時，另一邊拿全部機率
                if (isFav) weights[i] = otherCount == 0 ? 1f / favCount : PetPreferences.FavoriteWeight / favCount;
                else weights[i] = favCount == 0 ? 1f / otherCount : (1f - PetPreferences.FavoriteWeight) / otherCount;
                total += weights[i];
            }
            float r = Random01(roomId + "|" + slot) * total;
            for (int i = 0; i < cats.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                if (r < weights[i]) return cats[i];
                r -= weights[i];
            }
            for (int i = cats.Length - 1; i >= 0; i--) if (weights[i] > 0f) return cats[i];
            return cats[0];
        }

        static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>同一個字串在每台裝置都得到同一個 0～1 的數（FNV-1a；不用 string.GetHashCode，各平台不保證一樣）。</summary>
        static float Random01(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s) { h ^= ch; h *= 16777619; }
            h ^= h >> 15; h *= 0x2C1B3C6D; h ^= h >> 12;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
