using TMPro;
using UnityEngine;

namespace BlockPet.UI.Travel
{
    /// <summary>
    /// 寄明信片的素材（設計文件 5.21，2026-11-08；docs/postcard/postcard_assets.json）。由 Editor 的 PostcardSkinBuilder 從
    /// Assets/Art/UI/Postcard/Sprites 依檔名（pc_xxx@3x.png → 欄位）自動填，放在 Resources/UI/PostcardSkin。缺圖時用程式畫的形狀代替。
    /// </summary>
    public class PostcardSkin : ScriptableObject
    {
        public TMP_FontAsset font;
        [Header("抽好友")]
        public Sprite iconDraw, btnDraw, chipFree, dotRed, card, btnYes, btnNo, btnWait, spinner, tagCountry, tagSame;
        [Header("選照片")]
        public Sprite iconNoface, nofaceBar, badgeDeco, check, sheet, handle;
        [Header("明信片、護照")]
        public Sprite stamp, postmark, postmarkS, note, passportBg, passportPage, miniPolaroid, emptySlot;

        /// <summary>欄位 ↔ 檔名（PostcardSkinBuilder 用）。</summary>
        public static readonly (string field, string file)[] Map =
        {
            ("iconDraw", "pc_icon_draw"), ("btnDraw", "pc_btn_draw"), ("chipFree", "pc_chip_free"), ("dotRed", "pc_dot_red"),
            ("card", "pc_card"), ("btnYes", "pc_btn_yes"), ("btnNo", "pc_btn_no"), ("btnWait", "pc_btn_wait"), ("spinner", "pc_spinner"),
            ("tagCountry", "pc_tag_country"), ("tagSame", "pc_tag_same"), ("iconNoface", "pc_icon_noface"), ("nofaceBar", "pc_noface_bar"),
            ("badgeDeco", "pc_badge_deco"), ("check", "pc_check"), ("sheet", "pc_sheet"), ("handle", "pc_handle"), ("stamp", "pc_stamp"),
            ("postmark", "pc_postmark"), ("postmarkS", "pc_postmark_s"), ("note", "pc_note"), ("passportBg", "pc_passport_bg"),
            ("passportPage", "pc_passport_page"), ("miniPolaroid", "pc_mini_polaroid"), ("emptySlot", "pc_empty_slot"),
        };

        static PostcardSkin _cached;
        public static PostcardSkin Load()
        {
            if (_cached == null) _cached = Resources.Load<PostcardSkin>("UI/PostcardSkin");
            return _cached;
        }
    }
}
