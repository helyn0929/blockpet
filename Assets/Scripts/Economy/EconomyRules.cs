using UnityEngine;

namespace BlockPet.Feed
{
    /// <summary>
    /// 經濟規則（依據：docs/design/blockpet-economy-spec.md，本檔是程式裡唯一定義這些數字的地方）。
    /// - 1.1 愛心乘數：1～5 顆 → ×0.6／0.8／1.0／1.2／1.4；半顆心無條件捨去。餵食金幣和寵物移動速度共用這張表。
    /// - 1.2 愛心回復：命中 +1 顆、沒命中 +0.5 顆，最多 5 顆。
    /// - 1.3 餵食金幣：沒命中 +1（不乘乘數）；命中 +5 × 乘數。先回復愛心，再用回復後的愛心算金幣。
    /// - 1.4 移動速度倍率＝愛心乘數。
    /// 【未定】的規則用暫用值，也集中在這裡（規格第 3 節）。
    /// </summary>
    public static class EconomyRules
    {
        // ─── 1.1 愛心乘數【定案】────────────────────────────────────────
        /// <summary>愛心 1～5 顆的乘數（索引 0＝1 顆）。</summary>
        static readonly float[] HeartMultipliers = { 0.6f, 0.8f, 1.0f, 1.2f, 1.4f };

        // ─── 1.2 愛心回復【定案】────────────────────────────────────────
        public const float HitHeal = 1f;
        public const float MissHeal = 0.5f;

        // ─── 1.3 餵食金幣【定案】────────────────────────────────────────
        public const int HitCoins = 5;
        public const int MissCoins = 1;

        // ─── 3. 未定（暫用值，改規則只改這裡）───────────────────────────
        /// <summary>3.2【未定】暫用值：0 顆心（昏倒）時不能餵，要先復活。</summary>
        public const bool CanFeedWhenDead = false;
        /// <summary>3.2【未定】暫用值：0 顆心（昏倒）時寵物不移動。</summary>
        public const bool DeadPetMoves = false;

        /// <summary>
        /// 愛心對應的等級 1～5：半顆心無條件捨去（1.5 顆＝1、4.5 顆＝4）。
        /// 3.1【未定】暫用值：不到 1 顆（0.5 顆、0 顆）當作 1。
        /// </summary>
        public static int HeartLevel(float hearts) => Mathf.Clamp(Mathf.FloorToInt(hearts + 0.001f), 1, HeartMultipliers.Length);

        /// <summary>等級 1～5 的乘數（超出範圍會夾到 1～5）。</summary>
        public static float LevelMultiplier(int level) => HeartMultipliers[Mathf.Clamp(level, 1, HeartMultipliers.Length) - 1];

        /// <summary>愛心乘數（金幣、移動速度共用）。</summary>
        public static float Multiplier(float hearts) => LevelMultiplier(HeartLevel(hearts));

        /// <summary>寵物移動速度倍率（1.4：和愛心乘數同一張表）。</summary>
        public static float SpeedMultiplier(float hearts) => Multiplier(hearts);

        /// <summary>這張照片回幾顆心（命中 +1、沒命中 +0.5）。</summary>
        public static float Heal(bool hit) => hit ? HitHeal : MissHeal;

        /// <summary>餵一張之後的愛心：加上回復量，最多 5 顆；昏倒（0 顆）時不回心。</summary>
        public static float HeartsAfterFeed(float hearts, bool hit)
        {
            if (hearts <= 0f) return 0f;
            return Mathf.Min(PetHearts.Max, hearts + Heal(hit));
        }

        /// <summary>餵一張拿到的金幣；heartsAfter 是回復之後的愛心。</summary>
        public static int FeedCoins(float heartsAfter, bool hit) =>
            hit ? Mathf.RoundToInt(HitCoins * Multiplier(heartsAfter)) : MissCoins;

        /// <summary>現在能不能餵（3.2 暫用值：昏倒時不能）。</summary>
        public static bool CanFeed(float hearts) => hearts > 0f || CanFeedWhenDead;
    }
}
