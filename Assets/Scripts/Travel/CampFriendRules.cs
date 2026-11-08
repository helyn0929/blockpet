using System.Collections.Generic;

namespace BlockPet.Travel
{
    /// <summary>
    /// 營地好友的地圖格子（設計文件 5.21）：好友放好就固定、刪掉（封鎖）空出來的格子會再被用。
    /// 2026-11-08 起好友全部用抽的（PostcardRules）；營地代碼加好友、邀請信封已刪除。
    /// </summary>
    public static class CampFriendRules
    {
        /// <summary>地圖上的好友位置數（超過的好友要解鎖才加得進來，但地圖目前只畫得出這麼多塊地）。</summary>
        public const int MaxFriends = TravelMapGen.MaxFriends;

        /// <summary>新好友放在哪一格：還沒用過的格子裡編號最小的（放好就固定；刪掉好友空出來的格子會再被用）。滿了回傳 -1。</summary>
        public static int NextFreeSlot(IEnumerable<int> usedSlots, int capacity = MaxFriends)
        {
            var used = new HashSet<int>();
            if (usedSlots != null) foreach (var s in usedSlots) if (s >= 0) used.Add(s);
            for (int i = 0; i < capacity; i++) if (!used.Contains(i)) return i;
            return -1;
        }
    }
}
