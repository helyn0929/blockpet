using System.Collections.Generic;

namespace BlockPet.Travel
{
    /// <summary>
    /// 營地好友的規則（設計文件 5.21 ①；2026-11-06 定：最多 10 個、輸入代碼的錯誤提示、被拒絕不通知）。
    /// </summary>
    public static class CampFriendRules
    {
        /// <summary>營地好友上限（和地圖的好友位置數一樣）。</summary>
        public const int MaxFriends = TravelMapGen.MaxFriends;

        public const string MsgNotFound = "找不到這個營地代碼，再確認一下";
        public const string MsgSelf = "這是你們自己營地的代碼";
        public const string MsgAlreadyFriend = "已經是營地好友了";
        public const string MsgAlreadyInvited = "已經送過邀請，等對方同意";
        public const string MsgFull = "營地好友已滿（最多 10 個）";

        /// <summary>
        /// 送出邀請前檢查；可以送回傳 null，不行回傳要顯示的提示。
        /// targetRoom＝用代碼找到的營地（找不到是 null）。
        /// </summary>
        public static string Check(string targetRoom, string myRoom, ICollection<string> myFriends, bool alreadyInvited)
        {
            if (string.IsNullOrEmpty(targetRoom)) return MsgNotFound;
            if (targetRoom == myRoom) return MsgSelf;
            if (myFriends != null && myFriends.Contains(targetRoom)) return MsgAlreadyFriend;
            if (myFriends != null && myFriends.Count >= MaxFriends) return MsgFull;
            if (alreadyInvited) return MsgAlreadyInvited;
            return null;
        }

        /// <summary>新好友放在地圖的哪一格：還沒用過的格子裡編號最小的（放好就固定；刪掉好友空出來的格子會再被用）。滿了回傳 -1。</summary>
        public static int NextFreeSlot(IEnumerable<int> usedSlots)
        {
            var used = new HashSet<int>();
            if (usedSlots != null) foreach (var s in usedSlots) if (s >= 0) used.Add(s);
            for (int i = 0; i < MaxFriends; i++) if (!used.Contains(i)) return i;
            return -1;
        }

        /// <summary>代碼輸入整理：大寫、只留英文數字、最多 6 碼。</summary>
        public static string NormalizeCode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder(6);
            foreach (char ch in raw.ToUpperInvariant())
            {
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
                if (sb.Length >= 6) break;
            }
            return sb.ToString();
        }
    }
}
