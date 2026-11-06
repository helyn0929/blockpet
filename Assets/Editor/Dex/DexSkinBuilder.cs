using BlockPet.UI.Dex;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Dex
{
    /// <summary>
    /// 自動建立／更新 Assets/Resources/UI/DexSkin.asset：圖鑑的素材從 Assets/Art/UI/Dex/Sprites 依檔名填進去
    /// （素材匯入設定見 TopBarSpriteImporter）。編譯完、或那個資料夾的圖重新匯入時都會跑一次；也可以從選單手動執行。
    /// </summary>
    public class DexSkinBuilder : AssetPostprocessor
    {
        const string SpriteFolder = "Assets/Art/UI/Dex/Sprites/";
        const string AssetPath = "Assets/Resources/UI/DexSkin.asset";
        const string FontPath = "Assets/Art/Fonts/NotoSansCJKtc-Black SDF.asset";
        /// <summary>寵物圖與剪影先用營地寵物 Zapup（2026-11-05；各外型的圖還沒畫）。</summary>
        const string PetPath = "Assets/Art/UI/Mainroom_UI/Zapup.png";
        const string CampPath = "Assets/Art/UI/Mainroom_UI/forest1.jpeg";

        [InitializeOnLoadMethod]
        static void AutoBuild() => EditorApplication.delayCall += () => Build(false);

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.StartsWith(SpriteFolder)) { EditorApplication.delayCall += () => Build(false); return; }
        }

        [MenuItem("BlockPet/Dex/Build Dex Skin")]
        static void BuildFromMenu() => Build(true);

        static void Build(bool log)
        {
            if (!AssetDatabase.IsValidFolder(SpriteFolder.TrimEnd('/'))) return;

            var skin = AssetDatabase.LoadAssetAtPath<DexSkin>(AssetPath);
            bool created = skin == null;
            if (created)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");
                skin = ScriptableObject.CreateInstance<DexSkin>();
            }

            bool changed = false;
            if (skin.font == null) { skin.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath); changed |= skin.font != null; }
            var pet = LoadSprite(PetPath);
            if (pet != null && skin.petPlaceholder != pet) { skin.petPlaceholder = pet; changed = true; }
            if (skin.campBackground == null) { skin.campBackground = AssetDatabase.LoadAssetAtPath<Texture>(CampPath); changed |= skin.campBackground != null; }

            changed |= Set(ref skin.headerBg, "dex_header_bg");
            changed |= Set(ref skin.back, "dex_btn_back");
            changed |= Set(ref skin.seriesCard, "dex_series_card");
            changed |= Set(ref skin.seriesIconBg, "dex_series_icon_bg");
            changed |= Set(ref skin.egg, "dex_egg");
            changed |= Set(ref skin.eggZigzag, "dex_egg_zigzag");
            changed |= Set(ref skin.progressTrack, "dex_progress_track");
            changed |= Set(ref skin.progressFill, "dex_progress_fill");
            changed |= Set(ref skin.tagSeason, "dex_tag_season");
            changed |= Set(ref skin.badgeCount, "dex_badge_count");
            changed |= Set(ref skin.shopCell, "dex_shop_cell");
            changed |= Set(ref skin.shopBag, "icon_shop_bag");
            changed |= Set(ref skin.sheetPanel, "dex_sheet_panel");
            changed |= Set(ref skin.sheetGrabber, "dex_sheet_grabber");
            changed |= Set(ref skin.close, "dex_btn_close");
            changed |= Set(ref skin.photoMask, "dex_photo_mask");
            changed |= Set(ref skin.genPill, "dex_gen_pill");
            changed |= Set(ref skin.arrow, "dex_arrow_btn");
            changed |= Set(ref skin.infoBox, "dex_info_box");
            changed |= Set(ref skin.paletteMask, "dex_palette_mask");
            changed |= Set(ref skin.dot, "dex_dot");
            changed |= Set(ref skin.pageDot, "dex_page_dot");
            changed |= Set(ref skin.pageDotOn, "dex_page_dot_on");
            changed |= Set(ref skin.popupCard, "dex_popup_card");
            changed |= Set(ref skin.buttonPrimary, "btn_primary");
            changed |= Set(ref skin.dim, "dim");
            changed |= Set(ref skin.newRays, "dex_new_rays");
            changed |= Set(ref skin.newTag, "dex_new_tag");
            changed |= Set(ref skin.rewardPill, "dex_reward_pill");
            changed |= Set(ref skin.coin, "icon_coin");
            changed |= Set(ref skin.buttonPrimaryLarge, "btn_primary_lg");

            if (created) AssetDatabase.CreateAsset(skin, AssetPath);
            else if (changed) EditorUtility.SetDirty(skin);
            if (created || changed) AssetDatabase.SaveAssets();
            if (log || created) Debug.Log("[Dex] 圖鑑素材已更新：" + AssetPath);
        }

        static bool Set(ref Sprite field, string file)
        {
            var s = LoadSprite(SpriteFolder + file + ".png");
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
