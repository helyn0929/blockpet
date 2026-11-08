using TMPro;
using UnityEngine;

namespace BlockPet.UI.Polaroid
{
    /// <summary>
    /// 拍立得分割畫面的素材（設計文件 5.26 ⑧；docs/polaroid/polaroid_assets.json）。
    /// 由 Editor 的 PolaroidSkinBuilder 從 Assets/Art/UI/Polaroid/Sprites 依檔名自動填，放在 Resources/UI/PolaroidSkin。
    /// 缺圖時畫面會用程式畫的形狀代替。
    /// </summary>
    public class PolaroidSkin : ScriptableObject
    {
        public TMP_FontAsset font;
        [Tooltip("寵物貼紙、寵物頁的寵物本體（現有 BowBow 圖；彩虹型／極致型之後換正式進化圖）")]
        public Sprite pet;
        [Tooltip("寵物頁的營地底圖（同首頁）")]
        public Texture camp;
        [Tooltip("寵物頁狀態膠囊的愛心圖、進化條顏色（沿用 5.5 狀態欄）")]
        public BlockPet.UI.TopBar.TopBarConfig topBar;

        [Header("線條圖示")]
        public Sprite iconBack, iconDownload, iconShare, iconEdit, iconPencil, iconSend, iconCheck, iconX;

        [Header("底")]
        public Sprite btnSend, btnPeel, dtabOn, dtabOff, inputBar, cell, cellPet, cellAlt, cellRing, filterMask,
            selDash, tagDark, toast, pillComment, tagCat, catDot, scrollTrack, scrollThumb, photoMask,
            dotYellow, pageOn, pageOff, petFrame, panelMask, statusPill;

        [Header("畫框花樣（平鋪）")]
        public Sprite frameGingham, frameDots, frameStripe;

        [Header("手畫貼紙")]
        public Sprite stkStar, stkHeart, stkSpark, stkCloud, stkPaw, stkBolt;

        [Header("寵物貼紙右上小符號")]
        public Sprite petOvHeart, petOvNote, petOvZz, petOvDrop;

        static PolaroidSkin _cached;

        public static PolaroidSkin Load()
        {
            if (_cached == null) _cached = Resources.Load<PolaroidSkin>("UI/PolaroidSkin");
            return _cached;
        }

        public Sprite DrawSticker(string kind)
        {
            switch (kind)
            {
                case "star": return stkStar;
                case "heart": return stkHeart;
                case "spark": return stkSpark;
                case "cloud": return stkCloud;
                case "paw": return stkPaw;
                case "bolt": return stkBolt;
            }
            return null;
        }

        public Sprite FrameTile(string tile)
        {
            switch (tile)
            {
                case "gingham": return frameGingham;
                case "dots": return frameDots;
                case "stripe": return frameStripe;
            }
            return null;
        }
    }
}
