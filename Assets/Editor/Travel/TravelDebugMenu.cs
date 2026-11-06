using BlockPet.Travel;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Travel
{
    /// <summary>
    /// 旅行測試用（Play 時才能用）：不用真的等 1～3 小時。會真的寫進目前空間的 Firebase。
    /// </summary>
    static class TravelDebugMenu
    {
        const string Root = "BlockPet/Debug/旅行/";
        const long Min = 60L * 1000L;

        static FirebaseManager Fb => FirebaseManager.Instance;

        [MenuItem(Root + "加一個測試好友營地", false, 0)]
        static void AddFriend() { Fb.StartCampFriends(); Fb.DebugFriend(false); }

        [MenuItem(Root + "清掉測試好友營地", false, 1)]
        static void ClearFriends() => Fb.DebugFriend(true);

        [MenuItem(Root + "派寵物去第一個好友營地", false, 20)]
        static void Send()
        {
            Fb.StartCampFriends();
            if (Fb.CampFriends.Count == 0) { Debug.LogWarning("[Travel] 還沒有營地好友：先用「加一個測試好友營地」，或在旅行地圖加好友"); return; }
            Fb.SendPetOnTrip(Fb.CampFriends[0], 15, err => Debug.Log("[Travel] 派出發：" + (err ?? "成功")));
        }

        [MenuItem(Root + "快轉 10 分鐘", false, 21)]
        static void Ten() => Fb.DebugShiftTrip(10 * Min);

        [MenuItem(Root + "快轉到抵達（在對方營地）", false, 22)]
        static void ToThere() => ShiftTo(t => TripRules.ArriveAt(t.departAt, t.oneWayMin) + Min);

        [MenuItem(Root + "快轉到回程", false, 23)]
        static void ToReturn() => ShiftTo(t => TripRules.LeaveAt(t.departAt, t.oneWayMin, t.stayMin) + Min);

        [MenuItem(Root + "快轉到回家（蓋章）", false, 24)]
        static void ToHome() => ShiftTo(t => t.HomeAt + Min);

        [MenuItem(Root + "假裝有客人來玩", false, 40)]
        static void Guest() => Fb.DebugGuest(false);

        [MenuItem(Root + "清掉假客人", false, 41)]
        static void ClearGuest() => Fb.DebugGuest(true);

        [MenuItem(Root + "重看回家蓋章卡", false, 42)]
        static void ResetSeen()
        {
            PlayerPrefs.DeleteKey("BlockPet.Travel.SeenReturn." + Fb.RoomId + "." + Fb.GetUserId());
            PlayerPrefs.Save();
        }

        /// <summary>把出發時間往前推，讓現在剛好到 target（以目前出發時間算）。</summary>
        static void ShiftTo(System.Func<FirebaseManager.TravelTrip, long> target)
        {
            var t = Fb.CurrentTrip;
            if (t == null) { Debug.LogWarning("[Travel] 目前沒有在旅行"); return; }
            long shift = target(t) - Fb.ServerNowMs;
            if (shift > 0) Fb.DebugShiftTrip(shift);
        }

        [MenuItem(Root + "加一個測試好友營地", true)]
        [MenuItem(Root + "清掉測試好友營地", true)]
        [MenuItem(Root + "派寵物去第一個好友營地", true)]
        [MenuItem(Root + "快轉 10 分鐘", true)]
        [MenuItem(Root + "快轉到抵達（在對方營地）", true)]
        [MenuItem(Root + "快轉到回程", true)]
        [MenuItem(Root + "快轉到回家（蓋章）", true)]
        [MenuItem(Root + "假裝有客人來玩", true)]
        [MenuItem(Root + "清掉假客人", true)]
        [MenuItem(Root + "重看回家蓋章卡", true)]
        static bool CanRun() => Application.isPlaying && Fb != null && !string.IsNullOrEmpty(Fb.RoomId);
    }
}
