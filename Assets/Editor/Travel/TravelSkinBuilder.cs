using BlockPet.UI.Travel;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Travel
{
    /// <summary>
    /// 自動建立／更新 Assets/Resources/UI/TravelSkin.asset：旅行的素材從 Assets/Art/UI/Travel/Sprites 依檔名填進去
    /// （檔名都是 xxx@3x.png；匯入設定見 TopBarSpriteImporter）。編譯完、或那個資料夾的圖重新匯入時都會跑一次。
    /// </summary>
    public class TravelSkinBuilder : AssetPostprocessor
    {
        const string SpriteFolder = "Assets/Art/UI/Travel/Sprites/";
        const string AssetPath = "Assets/Resources/UI/TravelSkin.asset";
        const string FontPath = "Assets/Art/Fonts/NotoSansCJKtc-Black SDF.asset";
        const string PetPath = "Assets/Art/UI/Mainroom_UI/Zapup.png";
        const string CampPath = "Assets/Art/UI/Mainroom_UI/forest1.jpeg";

        [InitializeOnLoadMethod]
        static void AutoBuild() => EditorApplication.delayCall += () => Build(false);

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.StartsWith(SpriteFolder)) { EditorApplication.delayCall += () => Build(false); return; }
        }

        [MenuItem("BlockPet/Travel/Build Travel Skin")]
        static void BuildFromMenu() => Build(true);

        public static TravelSkin Build(bool log)
        {
            if (!AssetDatabase.IsValidFolder(SpriteFolder.TrimEnd('/'))) return null;

            var skin = AssetDatabase.LoadAssetAtPath<TravelSkin>(AssetPath);
            bool created = skin == null;
            if (created)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");
                skin = ScriptableObject.CreateInstance<TravelSkin>();
            }

            bool changed = false;
            if (skin.font == null) { skin.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath); changed |= skin.font != null; }
            var pet = LoadSprite(PetPath);
            if (pet != null && skin.pet != pet) { skin.pet = pet; changed = true; }
            if (skin.campBackground == null) { skin.campBackground = AssetDatabase.LoadAssetAtPath<Texture>(CampPath); changed |= skin.campBackground != null; }

            changed |= Set(ref skin.seaWave, "map_sea_wave_tile");
            changed |= Set(ref skin.landDisc, "map_land_disc");
            changed |= Set(ref skin.pinFrame, "map_pin_frame");
            changed |= Set(ref skin.pinMask, "map_pin_mask");
            changed |= Set(ref skin.homeFrame, "map_home_frame");
            changed |= Set(ref skin.homeMask, "map_home_mask");
            changed |= Set(ref skin.labelBg, "map_label_bg");
            changed |= Set(ref skin.homeLabel, "map_home_label");
            changed |= Set(ref skin.back, "travel_btn_back");
            changed |= Set(ref skin.stampBadge, "map_stamp_badge");
            changed |= Set(ref skin.propBench, "map_prop_bench");
            changed |= Set(ref skin.propSign, "map_prop_sign");
            changed |= Set(ref skin.propFlowerbed, "map_prop_flowerbed");
            changed |= Set(ref skin.propLamp, "map_prop_lamp");
            changed |= Set(ref skin.headerBg, "travel_header_bg");
            changed |= Set(ref skin.inviteButton, "travel_btn_invite");
            changed |= Set(ref skin.redBadge, "travel_badge_red");
            changed |= Set(ref skin.addButton, "travel_btn_add");
            changed |= Set(ref skin.passportButton, "travel_btn_passport");
            changed |= Set(ref skin.statusCard, "travel_status_card");
            changed |= Set(ref skin.paperPlane, "icon_paper_plane");
            changed |= Set(ref skin.backpack, "pet_backpack");
            changed |= Set(ref skin.tagPill, "map_tag_pill");
            changed |= Set(ref skin.glassBar, "travel_glass_bar");
            changed |= Set(ref skin.backSmall, "travel_btn_back_sm");
            changed |= Set(ref skin.tagTraveling, "travel_tag_traveling");
            changed |= Set(ref skin.bubbleLeft, "bubble_left_tail");
            changed |= Set(ref skin.bubbleRight, "bubble_right_tail");
            changed |= Set(ref skin.awaySign, "away_sign");
            changed |= Set(ref skin.popupCard, "travel_popup_card");
            changed |= Set(ref skin.avatarBg, "travel_avatar_bg");
            changed |= Set(ref skin.infoCell, "travel_info_cell");
            changed |= Set(ref skin.photoMask, "travel_photo_mask");
            changed |= Set(ref skin.passportBack, "passport_btn_back");
            changed |= Set(ref skin.passportPage, "passport_page");
            changed |= Set(ref skin.passportFold, "passport_fold");
            changed |= Set(ref skin.stampRing, "passport_stamp_ring");
            changed |= Set(ref skin.stampEmpty, "passport_stamp_empty");
            changed |= Set(ref skin.timesBadge, "passport_times_badge");

            if (created) AssetDatabase.CreateAsset(skin, AssetPath);
            else if (changed) EditorUtility.SetDirty(skin);
            if (created || changed) AssetDatabase.SaveAssets();
            if (log || created) Debug.Log("[Travel] 旅行素材已更新：" + AssetPath);
            return skin;
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
