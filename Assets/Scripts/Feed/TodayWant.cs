using System;
using System.Collections.Generic;

namespace BlockPet.Feed
{
    /// <summary>
    /// 「今天想吃」（2026-10-24 定案：依寵物喜好＋隨機）：
    /// - 全房間同一個：用房間 ID＋日期算出來，每台手機算的都一樣，不用存在伺服器。
    /// - 偏愛類別（<see cref="PetPreferences"/>）合計 70%、其他類別平分 30%；不會和昨天一樣。
    /// - 台灣時間 00:00 換日（目前用手機時間換算，手機時間錯了會看到別天的）。
    /// 「不和昨天一樣」要知道昨天真正的結果，所以從固定起點一天一天往後算（一年三百多次，很快），結果快取。
    /// </summary>
    public static class TodayWant
    {
        static readonly DateTime Epoch = new DateTime(2026, 1, 1);
        static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        /// <summary>台灣時間的今天。</summary>
        public static DateTime Today => DateTime.UtcNow.AddHours(8).Date;

        /// <summary>目前房間、目前寵物今天想吃的類別；沒有房間時回傳 null。</summary>
        public static string Current()
        {
            string room = FirebaseManager.Instance != null ? FirebaseManager.Instance.RoomId : null;
            if (string.IsNullOrEmpty(room)) return null;
            int pet = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.CurrentPetIndex : 0;
            return For(room, pet, Today);
        }

        public static string For(string roomId, int petIndex, DateTime date)
        {
            date = date.Date;
            string key = roomId + "|" + petIndex + "|" + date.ToString("yyyyMMdd");
            if (Cache.TryGetValue(key, out var hit)) return hit;

            var fav = PetPreferences.FavoritesOf(petIndex);
            string prev = null;
            var d = date < Epoch ? date : Epoch;
            for (; d <= date; d = d.AddDays(1)) prev = Pick(roomId, d, fav, prev);
            Cache[key] = prev;
            return prev;
        }

        static string Pick(string roomId, DateTime day, string[] fav, string exclude)
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
            float r = Random01(roomId + "|" + day.ToString("yyyyMMdd")) * total;
            for (int i = 0; i < cats.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                if (r < weights[i]) return cats[i];
                r -= weights[i];
            }
            for (int i = cats.Length - 1; i >= 0; i--) if (weights[i] > 0f) return cats[i];
            return cats[0];
        }

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
