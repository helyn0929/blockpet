using BlockPet.Travel;
using BlockPet.UI.Travel;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Travel
{
    /// <summary>
    /// 寄明信片測試用（Play 時才能用）：不用真的等 1～3 小時、不用第二個帳號。會真的寫進目前空間的 Firebase。
    /// 去好友營地＝送明信片（5.21），所以只有：加測試好友、寄一張、快轉到送到／回家（回家會自動帶回風景明信片）、
    /// 假裝別的營地來加好友或寄明信片過來、重看彈出。
    /// </summary>
    static class TravelDebugMenu
    {
        const string Root = "BlockPet/Debug/寄明信片/";
        const long Min = 60L * 1000L;

        static FirebaseManager Fb => FirebaseManager.Instance;

        [MenuItem(Root + "加一個測試好友營地", false, 0)]
        static void AddFriend() { Fb.StartCampFriends(); Fb.DebugFriend(false); }

        [MenuItem(Root + "清掉測試好友營地", false, 1)]
        static void ClearFriends() => Fb.DebugFriend(true);

        [MenuItem(Root + "寄一張明信片給第一個好友營地", false, 20)]
        static void Send()
        {
            Fb.StartCampFriends();
            if (Fb.CampFriends.Count == 0) { Debug.LogWarning("[Postcard] 還沒有營地好友：先用「加一個測試好友營地」，或在明信片地圖抽好友"); return; }
            Fb.SendPostcard(Fb.CampFriends[0], 15, TestImage(), "測試明信片", err => Debug.Log("[Postcard] 寄出：" + (err ?? "成功")));
        }

        [MenuItem(Root + "快轉到送到", false, 21)]
        static void ToThere() => ShiftTo(t => TripRules.ArriveAt(t.departAt, t.oneWayMin) + Min);

        [MenuItem(Root + "快轉到回家", false, 22)]
        static void ToHome() => ShiftTo(t => t.HomeAt + Min);

        [MenuItem(Root + "假裝有營地想跟我們當好友", false, 40)]
        static void MatchIn() { Fb.StartCampFriends(); Fb.DebugMatchIn(); }

        [MenuItem(Root + "假裝好友寄來一張明信片", false, 41)]
        static void PostcardIn() { Fb.StartCampFriends(); Fb.DebugPostcardIn(TestImage(), FirebaseManager.PostcardKindPhoto); }

        [MenuItem(Root + "重看明信片彈出", false, 42)]
        static void ResetSeen()
        {
            PlayerPrefs.DeleteKey("BlockPet.Postcard.Seen." + Fb.RoomId + "." + Fb.GetUserId());
            PlayerPrefs.Save();
        }

        /// <summary>測試用的明信片圖：用風景明信片的畫法畫一張。</summary>
        static byte[] TestImage() => PostcardArt.RenderScenery(new FirebaseManager.SentPostcard { toName = "（測試）海邊小屋", toSlot = 1 });

        /// <summary>把出發時間往前推，讓現在剛好到 target（以目前出發時間算）。</summary>
        static void ShiftTo(System.Func<FirebaseManager.TravelTrip, long> target)
        {
            var t = Fb.CurrentTrip;
            if (t == null) { Debug.LogWarning("[Postcard] 寵物現在沒有出門"); return; }
            long shift = target(t) - Fb.ServerNowMs;
            if (shift > 0) Fb.DebugShiftTrip(shift);
        }

        [MenuItem(Root + "加一個測試好友營地", true)]
        [MenuItem(Root + "清掉測試好友營地", true)]
        [MenuItem(Root + "寄一張明信片給第一個好友營地", true)]
        [MenuItem(Root + "快轉到送到", true)]
        [MenuItem(Root + "快轉到回家", true)]
        [MenuItem(Root + "假裝有營地想跟我們當好友", true)]
        [MenuItem(Root + "假裝好友寄來一張明信片", true)]
        [MenuItem(Root + "重看明信片彈出", true)]
        static bool CanRun() => Application.isPlaying && Fb != null && !string.IsNullOrEmpty(Fb.RoomId);
    }
}
