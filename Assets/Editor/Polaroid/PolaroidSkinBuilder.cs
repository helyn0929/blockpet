using BlockPet.UI.Polaroid;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Polaroid
{
    /// <summary>
    /// 自動建立／更新 Assets/Resources/UI/PolaroidSkin.asset：拍立得分割畫面（設計文件 5.26）的素材從
    /// Assets/Art/UI/Polaroid/Sprites 依檔名填進去（xxx@3x.png；匯入設定見 TopBarSpriteImporter）。編譯完、或圖重新匯入時都會跑一次。
    /// </summary>
    public class PolaroidSkinBuilder : AssetPostprocessor
    {
        const string SpriteFolder = "Assets/Art/UI/Polaroid/Sprites/";
        const string AssetPath = "Assets/Resources/UI/PolaroidSkin.asset";
        const string FontPath = "Assets/Art/Fonts/NotoSansCJKtc-Black SDF.asset";
        const string PetPath = "Assets/Art/UI/Mainroom_UI/Zapup.png"; // 同旅行、圖鑑的寵物圖
        const string CampPath = "Assets/Art/UI/Mainroom_UI/forest1.jpeg"; // 營地底圖（同原本分割畫面）
        const string TopBarPath = "Assets/Data/TopBarConfig.asset";

        [InitializeOnLoadMethod]
        static void AutoBuild() => EditorApplication.delayCall += () => Build(false);

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.StartsWith(SpriteFolder)) { EditorApplication.delayCall += () => Build(false); return; }
        }

        [MenuItem("BlockPet/Polaroid/Build Polaroid Skin")]
        static void BuildFromMenu() => Build(true);

        public static void Build(bool log)
        {
            if (!AssetDatabase.IsValidFolder(SpriteFolder.TrimEnd('/'))) return;
            var skin = AssetDatabase.LoadAssetAtPath<PolaroidSkin>(AssetPath);
            bool created = skin == null;
            if (created)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");
                skin = ScriptableObject.CreateInstance<PolaroidSkin>();
            }

            bool changed = false;
            if (skin.font == null) { skin.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath); changed |= skin.font != null; }
            var pet = LoadSprite(PetPath);
            if (pet != null && skin.pet != pet) { skin.pet = pet; changed = true; }
            var camp = AssetDatabase.LoadAssetAtPath<Texture>(CampPath);
            if (camp != null && skin.camp != camp) { skin.camp = camp; changed = true; }
            var topBar = AssetDatabase.LoadAssetAtPath<BlockPet.UI.TopBar.TopBarConfig>(TopBarPath);
            if (topBar != null && skin.topBar != topBar) { skin.topBar = topBar; changed = true; }

            changed |= Set(ref skin.iconBack, "pl_icon_back");
            changed |= Set(ref skin.iconDownload, "pl_icon_download");
            changed |= Set(ref skin.iconShare, "pl_icon_share");
            changed |= Set(ref skin.iconEdit, "pl_icon_edit");
            changed |= Set(ref skin.iconPencil, "pl_icon_pencil");
            changed |= Set(ref skin.iconSend, "pl_icon_send");
            changed |= Set(ref skin.iconCheck, "pl_icon_check");
            changed |= Set(ref skin.iconX, "pl_icon_x");
            changed |= Set(ref skin.btnSend, "pl_btn_send");
            changed |= Set(ref skin.btnPeel, "pl_btn_peel");
            changed |= Set(ref skin.dtabOn, "pl_dtab_on");
            changed |= Set(ref skin.dtabOff, "pl_dtab_off");
            changed |= Set(ref skin.cellAlt, "pl_cell_alt");
            changed |= Set(ref skin.dotYellow, "pl_dot_yellow");
            changed |= Set(ref skin.pageOn, "pl_page_on");
            changed |= Set(ref skin.pageOff, "pl_page_off");
            changed |= Set(ref skin.petFrame, "pl_pet_frame");
            changed |= Set(ref skin.panelMask, "pl_panel_mask");
            changed |= Set(ref skin.statusPill, "pl_status_pill");
            changed |= Set(ref skin.inputBar, "pl_input_bar");
            changed |= Set(ref skin.cell, "pl_cell");
            changed |= Set(ref skin.cellPet, "pl_cell_pet");
            changed |= Set(ref skin.cellRing, "pl_cell_ring");
            changed |= Set(ref skin.filterMask, "pl_filter_mask");
            changed |= Set(ref skin.selDash, "pl_sel_dash");
            changed |= Set(ref skin.tagDark, "pl_tag_dark");
            changed |= Set(ref skin.toast, "pl_toast");
            changed |= Set(ref skin.pillComment, "pl_pill_comment");
            changed |= Set(ref skin.tagCat, "pl_tag_cat");
            changed |= Set(ref skin.catDot, "pl_cat_dot");
            changed |= Set(ref skin.scrollTrack, "pl_scroll_track");
            changed |= Set(ref skin.scrollThumb, "pl_scroll_thumb");
            changed |= Set(ref skin.photoMask, "pl_photo_mask");
            changed |= Set(ref skin.frameGingham, "pl_frame_gingham");
            changed |= Set(ref skin.frameDots, "pl_frame_dots");
            changed |= Set(ref skin.frameStripe, "pl_frame_stripe");
            changed |= Set(ref skin.stkStar, "pl_stk_star");
            changed |= Set(ref skin.stkHeart, "pl_stk_heart");
            changed |= Set(ref skin.stkSpark, "pl_stk_spark");
            changed |= Set(ref skin.stkCloud, "pl_stk_cloud");
            changed |= Set(ref skin.stkPaw, "pl_stk_paw");
            changed |= Set(ref skin.stkBolt, "pl_stk_bolt");
            changed |= Set(ref skin.petOvHeart, "pl_pet_ov_heart");
            changed |= Set(ref skin.petOvNote, "pl_pet_ov_note");
            changed |= Set(ref skin.petOvZz, "pl_pet_ov_zz");
            changed |= Set(ref skin.petOvDrop, "pl_pet_ov_drop");

            if (created) AssetDatabase.CreateAsset(skin, AssetPath);
            else if (changed) EditorUtility.SetDirty(skin);
            if (created || changed) AssetDatabase.SaveAssets();
            if (log || created) Debug.Log("[Polaroid] 拍立得素材已更新：" + AssetPath);
        }

        static bool Set(ref Sprite field, string file)
        {
            var s = LoadSprite(SpriteFolder + file + "@3x.png");
            if (s == null || s == field) return false;
            field = s;
            return true;
        }

        static Sprite LoadSprite(string path)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Sprite sp) return sp;
            return null;
        }
    }
}
