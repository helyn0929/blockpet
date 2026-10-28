using System;
using UnityEngine;

namespace BlockPet.Feed
{
    /// <summary>
    /// 愛心規則（開發順序第 6 步）：0～5 顆、半顆為單位；每 5 小時沒人餵掉半顆（以伺服器時間計算）；
    /// 房間任何成員餵一張回半顆，並重新開始計時；0 顆＝死亡，要喝一瓶復活藥水（商城買）才會醒來（完全恢復）。
    /// 房間存的是「上次有人餵（或復活）時的愛心」＋「那時的伺服器時間」，現在的愛心每台手機自己算，不用一直寫伺服器。
    /// 5 顆滿 → 45 小時剩半顆 → 50 小時死亡。
    /// 舊房間（24 小時秒數制、沒有 heartsAt）第一次載入時直接補成 5 顆滿（FirebaseManager.MigratePetHearts）。
    /// </summary>
    public static class PetHearts
    {
        public const float Max = 5f;
        public const float Step = 0.5f;
        public const double DecayHours = 5.0;
        /// <summary>復活藥水的商品 id（Rooms/{room}/shop/item_revive 是空間有幾瓶）。2026-10-28 定案：復活要喝藥水，藥水在商城用金幣買（價格見 MarketCatalog）。</summary>
        public const string PotionId = "item_revive";

        const double DecayMs = DecayHours * 3600.0 * 1000.0;

        /// <summary>從上次餵食時的愛心與時間，算出 nowMs 時的愛心。</summary>
        public static float At(float hearts, long sinceMs, long nowMs)
        {
            hearts = Snap(hearts);
            if (hearts <= 0f || sinceMs <= 0 || nowMs <= sinceMs) return hearts;
            double steps = Math.Floor((nowMs - sinceMs) / DecayMs);
            return Mathf.Max(0f, hearts - (float)steps * Step);
        }

        /// <summary>下一次掉半顆還要多久（給狀態顯示用）；已經死亡回傳 TimeSpan.Zero。</summary>
        public static TimeSpan UntilNextDrop(float hearts, long sinceMs, long nowMs)
        {
            if (At(hearts, sinceMs, nowMs) <= 0f || sinceMs <= 0) return TimeSpan.Zero;
            double elapsed = Math.Max(0, nowMs - sinceMs) % DecayMs;
            return TimeSpan.FromMilliseconds(DecayMs - elapsed);
        }

        /// <summary>愛心對應的星等 ★1～★5（有幾顆就是幾星，半顆進位，最少 ★1）。</summary>
        public static int Stars(float hearts) => Mathf.Clamp(Mathf.CeilToInt(hearts - 0.001f), 1, 5);

        /// <summary>對齊到半顆。</summary>
        public static float Snap(float hearts) => Mathf.Clamp(Mathf.Round(hearts / Step) * Step, 0f, Max);
    }
}
