using System;
using UnityEngine;

namespace BlockPet.Feed
{
    /// <summary>一次餵食的結算結果（給寵物反應、台詞用）。</summary>
    public struct FeedResult
    {
        public string category;   // 照片辨識的類別（辨識失敗是 null）
        public string want;       // 現在想吃（每 2 小時換）
        public bool hit;
        public int coins;
        public float multiplier;  // 愛心乘數（用餵之後的愛心算）
        public float heartsAfter; // 餵之後的愛心
        public bool dead;         // 寵物昏倒中（愛心 0）：不能餵（規格 3.2 暫用值），萬一還是進來了不給金幣、不回心
    }

    /// <summary>
    /// 出牌餵食的結算（開發順序第 3 步）：手牌出牌、相機拍照餵食都會走 SaveManager.SavePhoto，
    /// 這裡在照片存好時（OnPhotoSavedMeta，只有自己餵的照片，雲端同步下來的不會觸發）一起結算：
    /// - 規則全部在 <see cref="EconomyRules"/>（docs/design/blockpet-economy-spec.md）：
    ///   先回復愛心（命中 +1、沒命中 +0.5），再用回復後的愛心算金幣（命中 +5 × 乘數、沒命中 +1）。
    /// - 辨識信心低也照樣用最高的那類判定。
    /// - 進化能量不另外存：照片本身記著類別，進化條直接從相簿照片算（見 PetCollectionManager.EnergyByCategory）。
    /// 金幣、愛心都進房間（FirebaseManager），帳本會記是誰賺的。
    /// </summary>
    public static class FeedSettlement
    {
        /// <summary>結算完成（主執行緒）。</summary>
        public static event Action<FeedResult> OnSettled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            SaveManager.OnPhotoSavedMeta -= Settle;
            SaveManager.OnPhotoSavedMeta += Settle;
        }

        static void Settle(PhotoMeta meta)
        {
            if (meta == null) return;
            var health = UnityEngine.Object.FindObjectOfType<PetHealthManager>();
            float hearts = health != null ? health.Hearts : PetHearts.Max; // 餵之前的愛心

            var r = new FeedResult { category = meta.category, want = TodayWant.Current() };
            r.hit = !string.IsNullOrEmpty(r.category) && r.category == r.want;
            r.dead = !EconomyRules.CanFeed(hearts);
            if (r.dead)
            {
                // 入口已經擋住昏倒時餵食；萬一還是進來（例：剛好在這一刻掉到 0）就不給金幣、不回心
                r.heartsAfter = hearts;
                r.multiplier = EconomyRules.Multiplier(hearts);
                r.coins = 0;
                Debug.Log($"[FeedSettlement] 寵物昏倒中，不結算");
                OnSettled?.Invoke(r);
                return;
            }

            // 先回復愛心，再用回復之後的愛心算金幣（規格 1.3）
            r.heartsAfter = EconomyRules.HeartsAfterFeed(hearts, r.hit);
            r.multiplier = EconomyRules.Multiplier(r.heartsAfter);
            r.coins = EconomyRules.FeedCoins(r.heartsAfter, r.hit);

            if (health != null) health.AddHearts(EconomyRules.Heal(r.hit));
            FirebaseManager.Instance?.AddRoomCoins(r.coins, r.hit ? "photo_hit" : "photo");

            Debug.Log($"[FeedSettlement] {r.category ?? "未辨識"} / 想吃 {r.want ?? "-"} → {(r.hit ? "命中" : "沒命中")} 愛心 {hearts:0.#}→{r.heartsAfter:0.#}，+{r.coins} 金幣（×{r.multiplier:0.0}）");
            OnSettled?.Invoke(r);
        }
    }
}
