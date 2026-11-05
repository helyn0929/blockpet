using System;

namespace BlockPet.Feed
{
    /// <summary>
    /// 「現在想吃」（原「今天想吃」）：每 2 小時換一次，以伺服器時間為準（docs/design/blockpet-economy-spec.md 1.3）。
    /// 時段與抽選規則在 <see cref="WantSchedule"/>；這裡只負責拿目前的空間、寵物、伺服器時間。
    /// </summary>
    public static class TodayWant
    {
        /// <summary>伺服器時間（UTC 毫秒）；還沒連上 Firebase 時用手機時間。</summary>
        static long NowMs => FirebaseManager.Instance != null
            ? FirebaseManager.Instance.ServerNowMs
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>目前空間、目前寵物現在想吃的類別；沒有空間時回傳 null。</summary>
        public static string Current()
        {
            string room = FirebaseManager.Instance != null ? FirebaseManager.Instance.RoomId : null;
            if (string.IsNullOrEmpty(room)) return null;
            int pet = PetCollectionManager.Instance != null ? PetCollectionManager.Instance.CurrentPetIndex : 0;
            return WantSchedule.At(room, pet, NowMs);
        }

        /// <summary>離下一次換還有多久（之後狀態欄倒數用）。</summary>
        public static TimeSpan UntilNextChange() => WantSchedule.UntilNextChange(NowMs);
    }
}
