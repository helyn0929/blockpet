using TMPro;
using UnityEngine;

namespace BlockPet.UI.Decorate
{
    /// <summary>
    /// 佈置營地的素材（設計文件 5.28；docs/decorate/decorate_assets.json）。由 Editor 的 DecorateSkinBuilder 從
    /// Assets/Art/UI/Decorate/Sprites 依檔名自動填，放在 Resources/UI/DecorateSkin。缺圖時用程式畫的形狀代替。
    /// </summary>
    public class DecorateSkin : ScriptableObject
    {
        public TMP_FontAsset font;
        [Header("格子（菱形 60 × 30）")]
        public Sprite tileNormal, tileOk, tileTarget, tileOccupied, tileSelected, tileBlocked;
        [Header("畫面")]
        public Sprite dim, iconBuild, btnPill, btnDone, topbar, panel, card, cardRing, tagDark, shadow;

        static DecorateSkin _cached;
        public static DecorateSkin Load()
        {
            if (_cached == null) _cached = Resources.Load<DecorateSkin>("UI/DecorateSkin");
            return _cached;
        }
    }
}
