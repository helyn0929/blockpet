using UnityEngine;

namespace BlockPet.UI.TopBar
{
    /// <summary>
    /// 佈置模式（右上選單「佈置」進入）時要隱藏哪些首頁介面。
    /// 設計文件 5.7 的建議是全部隱藏、把畫面留給家具，但尚未定案，所以預設全部不隱藏，
    /// 決定後在 Assets/Data/DecorModeSettings.asset 勾選即可。
    /// </summary>
    [CreateAssetMenu(fileName = "DecorModeSettings", menuName = "BlockPet/Decor Mode Settings")]
    public class DecorModeSettings : ScriptableObject
    {
        [Tooltip("頂部狀態欄（頭像、愛心、進化條、金幣）")]
        public bool hideTopBar;
        [Tooltip("左上 Home 鍵")]
        public bool hideHomeButton;
        [Tooltip("右上選單鈕（佈置／旅行／圖鑑／設定）")]
        public bool hideMenuButton;
        [Tooltip("今日題目（第 9 步實作後生效）")]
        public bool hideTodayTheme;
        [Tooltip("手牌、營地最新照片卡、出牌後的相簿／拍照／聊天")]
        public bool hideHand;

        /// <summary>目前是否在佈置模式。</summary>
        public static bool IsDecorating => BlockPet.UI.Home.RoomEditorController.AnyEditModeActive;
    }
}
