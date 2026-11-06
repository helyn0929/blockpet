using TMPro;
using UnityEngine;

namespace BlockPet.UI.Dex
{
    /// <summary>
    /// 圖鑑（設計文件 5.23）用的素材。放在 Resources/UI/DexSkin，執行時自己讀，不用在場景裡接線。
    /// 由 Editor 的 DexSkinBuilder 自動從 Assets/Art/UI/Dex/Sprites 填好（素材重新匯入時也會更新）。
    /// 9-slice 邊界見 docs/dex/dex_assets.json（匯入設定在 TopBarSpriteImporter）。
    /// </summary>
    public class DexSkin : ScriptableObject
    {
        public const string ResourcePath = "UI/DexSkin";

        [Header("字型與圖")]
        public TMP_FontAsset font;
        [Tooltip("寵物圖（各外型、彩虹、極致的圖還沒畫，先用一張改色；設計稿也是用 BowBow 改色）")]
        public Sprite petPlaceholder;
        [Tooltip("詳細大圖的營地底圖")]
        public Texture campBackground;

        [Header("主頁")]
        public Sprite headerBg, back; // dex_tab、dex_tab_on 不再使用（系列分頁刪除，11-05）
        public Sprite seriesCard, seriesIconBg, egg, eggZigzag, progressTrack, progressFill, tagSeason;
        public Sprite badgeCount, shopCell, shopBag; // 格子不要方框（設計文件 5.23 ③ 11-05）：dex_cell 系列不再使用

        [Header("詳細、還沒收到")]
        public Sprite sheetPanel, sheetGrabber, close, photoMask, genPill, arrow, infoBox, paletteMask, dot, pageDot, pageDotOn;
        public Sprite popupCard, buttonPrimary;

        [Header("收到新的一格")]
        public Sprite dim, newRays, newTag, rewardPill, coin, buttonPrimaryLarge;

        static DexSkin _loaded;

        public static DexSkin Load()
        {
            if (_loaded == null) _loaded = Resources.Load<DexSkin>(ResourcePath);
            return _loaded;
        }
    }
}
