using System;
using System.Collections.Generic;

namespace BlockPet.Travel
{
    /// <summary>
    /// 寄明信片的設定值（設計文件 5.21 ⑥ 待定的數字先放這裡；遊戲裡從 Resources/Postcard/postcard_settings.json 讀，可以直接改檔案）。
    /// </summary>
    [Serializable]
    public class PostcardSettings
    {
        /// <summary>每個營地每天免費抽幾次（台灣時間 0 點換日，伺服器時間）。</summary>
        public int drawFreePerDay = 1;
        /// <summary>免費次數用完後，抽一次幾金幣（設計稿 50，價格待定）。</summary>
        public int drawPrice = 50;
        /// <summary>好友上限（地圖上的好友位置數）。</summary>
        public int friendLimit = 10;
        /// <summary>好友位置解鎖：一次幾金幣、加幾格（2026-11-08 使用者暫定 100 金幣 +1 格）。</summary>
        public int unlockPrice = 100;
        public int unlockSlots = 1;
        /// <summary>按「加好友」後，對方多久沒確認就失效（小時，暫定 24）。</summary>
        public int confirmExpireHours = 24;
    }

    /// <summary>抽好友時看得到的營地資料（設計文件 5.21 ①：只有營地名稱和寵物，不顯示真名、照片、帳號）。</summary>
    [Serializable]
    public class CampProfile
    {
        public string roomId;
        public string name;
        public string pet;
        public string breed;
        public int members;
        /// <summary>最常拍的類別（美食／自然／城市／生活人物／寵物；還沒有照片是空字串）。</summary>
        public string top;
        /// <summary>國家代碼（手機的地區設定，例 TW）。</summary>
        public string country;
        /// <summary>最後一次更新（伺服器時間 ms）。</summary>
        public long at;
    }

    /// <summary>
    /// 寄明信片的規則（設計文件 5.21，2026-11-08；docs/postcard/postcard_spec.json）：
    /// 抽好友的次數與價格、好友上限與解鎖、配對（最常拍的類別一樣優先、國家隨機、已是好友／封鎖過的不會再抽到）、
    /// 等確認的期限、寵物回家帶回風景明信片、郵票與郵戳的字。全部用伺服器時間算。
    /// </summary>
    public static class PostcardRules
    {
        const long HourMs = 3600L * 1000L;
        static readonly TimeSpan Taiwan = TimeSpan.FromHours(8);

        /// <summary>五大類別的順序（最常拍的類別一樣多時，照這個順序取）。</summary>
        public static readonly string[] Categories = { "美食", "自然", "城市", "生活人物", "寵物" };

        // ─── 抽好友 ────────────────────────────────────────────────────

        /// <summary>台灣時間的日期（yyyyMMdd）：每天 0 點換日（2026-11-08 使用者決定，和「現在想吃」一樣用台灣時間）。</summary>
        public static string DayKey(long serverMs) =>
            DateTimeOffset.FromUnixTimeMilliseconds(serverMs).ToOffset(Taiwan).ToString("yyyyMMdd");

        /// <summary>今天已經抽了幾次（存的是哪一天的次數；換日就歸零）。</summary>
        public static int DrawsToday(string storedDay, int storedCount, string today) =>
            storedDay == today ? Math.Max(0, storedCount) : 0;

        /// <summary>再抽一次要幾金幣：免費次數還有是 0。</summary>
        public static int DrawCost(int drawsToday, PostcardSettings s) =>
            drawsToday < Math.Max(0, s.drawFreePerDay) ? 0 : Math.Max(0, s.drawPrice);

        /// <summary>今天還有幾次免費。</summary>
        public static int FreeLeft(int drawsToday, PostcardSettings s) => Math.Max(0, s.drawFreePerDay - drawsToday);

        /// <summary>好友上限（基本＋解鎖過的格子）。</summary>
        public static int FriendCapacity(int unlocks, PostcardSettings s) =>
            Math.Max(0, s.friendLimit) + Math.Max(0, unlocks) * Math.Max(0, s.unlockSlots);

        public static bool IsFull(int friends, int unlocks, PostcardSettings s) => friends >= FriendCapacity(unlocks, s);

        /// <summary>
        /// 配對：排除自己、已經是好友、封鎖過（雙向）、已經在等確認的營地；
        /// 最常拍的類別和我們一樣的優先（有就只在這些裡面抽），國家不限；從剩下的隨機挑一個。沒有可以抽的回傳 null。
        /// </summary>
        public static CampProfile Pick(IEnumerable<CampProfile> candidates, string myRoom, string myTop,
                                       ICollection<string> exclude, Random rnd)
        {
            var same = new List<CampProfile>();
            var other = new List<CampProfile>();
            var seen = new HashSet<string>();
            if (candidates != null)
                foreach (var c in candidates)
                {
                    if (c == null || string.IsNullOrEmpty(c.roomId) || c.roomId == myRoom) continue;
                    if (exclude != null && exclude.Contains(c.roomId)) continue;
                    if (!seen.Add(c.roomId)) continue;
                    if (!string.IsNullOrEmpty(myTop) && c.top == myTop) same.Add(c); else other.Add(c);
                }
            var pool = same.Count > 0 ? same : other;
            if (pool.Count == 0) return null;
            return pool[(rnd ?? new Random()).Next(pool.Count)];
        }

        /// <summary>兩個營地最常拍的類別一樣（卡片上「你們都愛拍美食！」）。</summary>
        public static bool SameTop(string a, string b) => !string.IsNullOrEmpty(a) && a == b;

        /// <summary>最常拍的類別（張數最多的；一樣多照類別順序）。沒有照片回傳空字串。</summary>
        public static string TopCategory(IDictionary<string, int> counts)
        {
            if (counts == null) return "";
            string best = "";
            int bestN = 0;
            foreach (var cat in Categories)
                if (counts.TryGetValue(cat, out int n) && n > bestN) { best = cat; bestN = n; }
            return best;
        }

        /// <summary>「加好友」送出後超過期限就失效（雙方都不顯示）。</summary>
        public static bool MatchExpired(long sentAt, long now, PostcardSettings s) =>
            sentAt <= 0 || now - sentAt >= Math.Max(1, s.confirmExpireHours) * HourMs;

        // ─── 明信片 ────────────────────────────────────────────────────

        /// <summary>
        /// 寵物回到家了、還沒帶回紀念：要帶回一張對方營地的風景明信片（2026-11-08 使用者決定拿掉回寄，各寄各的；
        /// 寵物每趟回家都帶一張對方營地的風景明信片當紀念）。
        /// </summary>
        public static bool SceneryDue(long homeAt, long now, bool alreadyBrought) =>
            !alreadyBrought && homeAt > 0 && now >= homeAt;

        /// <summary>地圖大頭針右上的小郵票：寄過一次只有郵票，兩次以上「×N」。</summary>
        public static string StampLabel(int times) => times > 1 ? "×" + times : "";

        /// <summary>郵戳下面的日期（台灣時間 yyyy.MM.dd）。</summary>
        public static string PostmarkDate(long ms) =>
            ms <= 0 ? "" : DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(Taiwan).ToString("yyyy.MM.dd");

        /// <summary>護照小拍立得上的小郵戳日期（MM.dd）。</summary>
        public static string PostmarkShort(long ms) =>
            ms <= 0 ? "" : DateTimeOffset.FromUnixTimeMilliseconds(ms).ToOffset(Taiwan).ToString("MM.dd");

        /// <summary>護照裡每張小拍立得微微歪斜：-4～3°（依明信片 id 固定）。</summary>
        public static int MiniTilt(string id) => -4 + (int)(TravelMapGen.Hash(id ?? "") % 8u);

        /// <summary>國家代碼 → 卡片上的國名；不認得的直接顯示代碼。</summary>
        public static string CountryName(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            switch (code.ToUpperInvariant())
            {
                case "TW": return "台灣";
                case "JP": return "日本";
                case "KR": return "韓國";
                case "HK": return "香港";
                case "MO": return "澳門";
                case "CN": return "中國";
                case "SG": return "新加坡";
                case "MY": return "馬來西亞";
                case "TH": return "泰國";
                case "VN": return "越南";
                case "PH": return "菲律賓";
                case "ID": return "印尼";
                case "US": return "美國";
                case "CA": return "加拿大";
                case "GB": return "英國";
                case "FR": return "法國";
                case "DE": return "德國";
                case "AU": return "澳洲";
                case "NZ": return "紐西蘭";
                default: return code.ToUpperInvariant();
            }
        }
    }
}
