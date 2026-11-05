using BlockPet.UI.TopBar;
using UnityEngine;

namespace BlockPet.Feed
{
    /// <summary>
    /// 餵食入口的檢查（規格 3.2【未定】暫用值：寵物昏倒時不能餵，要先復活）。
    /// 手牌出牌、首頁相機、分割畫面快門開始前呼叫 <see cref="Check"/>；不能餵時打開復活視窗。
    /// </summary>
    public static class FeedGate
    {
        /// <summary>可以餵回傳 true；昏倒中回傳 false，並打開復活視窗。</summary>
        public static bool Check()
        {
            var health = Object.FindObjectOfType<PetHealthManager>();
            if (health == null || EconomyRules.CanFeed(health.Hearts)) return true;
            var top = Object.FindObjectOfType<TopBarView>();
            if (top != null && top.IsDead) top.ClickWantChip();
            else ReviveDialog.Show(null, "寵物");
            return false;
        }
    }
}
