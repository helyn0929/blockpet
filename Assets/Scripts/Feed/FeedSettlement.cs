using System;
using UnityEngine;

namespace BlockPet.Feed
{
    /// <summary>一次餵食的結算結果（給寵物反應、台詞用）。</summary>
    public struct FeedResult
    {
        public string category;   // 照片辨識的類別（辨識失敗是 null）
        public string want;       // 今天想吃
        public bool hit;
        public int coins;
        public float multiplier;  // 愛心乘數
        public bool dead;         // 寵物昏倒中（愛心 0）：照樣有金幣，但不回心
    }

    /// <summary>
    /// 出牌餵食的結算（開發順序第 3 步）：手牌出牌、相機拍照餵食都會走 SaveManager.SavePhoto，
    /// 這裡在照片存好時（OnPhotoSavedMeta，只有自己餵的照片，雲端同步下來的不會觸發）一起結算：
    /// - 命中「今天想吃」：+5 × 愛心乘數 金幣；沒命中 +1（辨識信心低也照樣用最高的那類判定）。
    /// - 每餵一張回半顆心（寵物死亡時不回心，要先用金幣復活）。
    /// - 進化能量不另外存：照片本身記著類別，進化條直接從相簿照片算（見 PetCollectionManager.EnergyByCategory）。
    /// 金幣、愛心都進房間（FirebaseManager），帳本會記是誰賺的。
    /// </summary>
    public static class FeedSettlement
    {
        /// <summary>愛心等級 ★1～★5 的收益乘數。</summary>
        static readonly float[] Multipliers = { 0.6f, 0.8f, 1.0f, 1.2f, 1.4f };
        const int MissCoins = 1;
        const int HitCoins = 5;

        /// <summary>結算完成（主執行緒）。</summary>
        public static event Action<FeedResult> OnSettled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveManager.OnPhotoSavedMeta -= Settle;
            SaveManager.OnPhotoSavedMeta += Settle;
        }

        /// <summary>愛心（0～5 顆）對應的乘數：有幾顆（半顆進位）就是幾星，最少 ★1。</summary>
        public static float Multiplier(float hearts) => Multipliers[PetHearts.Stars(hearts) - 1];

        static void Settle(PhotoMeta meta)
        {
            if (meta == null) return;
            var health = UnityEngine.Object.FindObjectOfType<PetHealthManager>();
            float hearts = health != null ? health.Hearts : 5f; // 用餵食前的愛心算乘數

            var r = new FeedResult { category = meta.category, want = TodayWant.Current() };
            r.hit = !string.IsNullOrEmpty(r.category) && r.category == r.want;
            r.multiplier = Multiplier(hearts);
            r.dead = hearts <= 0f;
            r.coins = r.hit ? Mathf.RoundToInt(HitCoins * r.multiplier) : MissCoins;

            FirebaseManager.Instance?.AddRoomCoins(r.coins, r.hit ? "photo_hit" : "photo");
            if (health != null && !r.dead) health.AddHalfHeart();

            Debug.Log($"[FeedSettlement] {r.category ?? "未辨識"} / 想吃 {r.want ?? "-"} → {(r.hit ? "命中" : "沒命中")} +{r.coins} 金幣（×{r.multiplier:0.0}）{(r.dead ? "，寵物昏倒中不回心" : "，+半顆心")}");
            OnSettled?.Invoke(r);
        }
    }
}
