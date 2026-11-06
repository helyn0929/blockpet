using BlockPet.UI.Home;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Home
{
    /// <summary>
    /// 自動建立／更新 Assets/Resources/UI/Home9Skin.asset：首頁 v9 的素材從 Assets/Art/UI/Home9/Sprites 依檔名填進去
    /// （檔名 xxx@3x.png；匯入設定見 TopBarSpriteImporter）。編譯完、或那個資料夾的圖重新匯入時都會跑一次。
    /// </summary>
    public class Home9SkinBuilder : AssetPostprocessor
    {
        const string SpriteFolder = "Assets/Art/UI/Home9/Sprites/";
        const string AssetPath = "Assets/Resources/UI/Home9Skin.asset";
        const string FontPath = "Assets/Art/Fonts/NotoSansCJKtc-Black SDF.asset";

        [InitializeOnLoadMethod]
        static void AutoBuild() => EditorApplication.delayCall += () => Build(false);

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.StartsWith(SpriteFolder)) { EditorApplication.delayCall += () => Build(false); return; }
        }

        [MenuItem("BlockPet/Home/Build Home9 Skin")]
        static void BuildFromMenu() => Build(true);

        static void Build(bool log)
        {
            if (!AssetDatabase.IsValidFolder(SpriteFolder.TrimEnd('/'))) return;

            var skin = AssetDatabase.LoadAssetAtPath<Home9Skin>(AssetPath);
            bool created = skin == null;
            if (created)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");
                skin = ScriptableObject.CreateInstance<Home9Skin>();
            }

            bool changed = false;
            if (skin.font == null) { skin.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath); changed |= skin.font != null; }
            changed |= Set(ref skin.iconHome, "icon_home_line");
            changed |= Set(ref skin.iconMenuChevron, "icon_menu_chevron");
            changed |= Set(ref skin.buttonRound, "home_btn_round");
            changed |= Set(ref skin.buttonCamera, "home_btn_camera");
            changed |= Set(ref skin.iconAlbum, "icon_album_line");
            changed |= Set(ref skin.iconChat, "icon_chat_line");
            changed |= Set(ref skin.badgeRed, "badge_red");
            changed |= Set(ref skin.stickerFrame, "sticker_frame");
            changed |= Set(ref skin.stickerPhotoMask, "sticker_photo_mask");
            changed |= Set(ref skin.stickerLatestFrame, "sticker_latest_frame");
            changed |= Set(ref skin.stickerLatestMask, "sticker_latest_mask");
            changed |= Set(ref skin.avatarRing, "sticker_avatar_ring");
            changed |= Set(ref skin.tagLatest, "tag_latest");
            changed |= Set(ref skin.tagMore, "tag_more");
            changed |= Set(ref skin.petBubble, "pet_bubble");

            if (created) AssetDatabase.CreateAsset(skin, AssetPath);
            else if (changed) EditorUtility.SetDirty(skin);
            if (created || changed) AssetDatabase.SaveAssets();
            if (log || created) Debug.Log("[Home9] 首頁 v9 素材已更新：" + AssetPath);
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
