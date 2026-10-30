using BlockPet.UI.Album;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Album
{
    /// <summary>
    /// 自動建立／更新 Assets/Resources/UI/PhotoViewSkin.asset：照片檢視的素材都從
    /// Assets/Art/UI/PhotoView/Sprites 依檔名填進去（素材匯入設定見 TopBarSpriteImporter）。
    /// 編譯完、或那個資料夾的圖重新匯入時都會跑一次；也可以從選單手動執行。
    /// </summary>
    public class PhotoViewSkinBuilder : AssetPostprocessor
    {
        const string SpriteFolder = "Assets/Art/UI/PhotoView/Sprites/";
        const string AssetPath = "Assets/Resources/UI/PhotoViewSkin.asset";

        [InitializeOnLoadMethod]
        static void AutoBuild() => EditorApplication.delayCall += () => Build(false);

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.StartsWith(SpriteFolder)) { EditorApplication.delayCall += () => Build(false); return; }
        }

        [MenuItem("BlockPet/Album/Build Photo View Skin")]
        static void BuildFromMenu() => Build(true);

        static void Build(bool log)
        {
            if (!AssetDatabase.IsValidFolder(SpriteFolder.TrimEnd('/'))) return;

            var skin = AssetDatabase.LoadAssetAtPath<PhotoViewSkin>(AssetPath);
            bool created = skin == null;
            if (created)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");
                skin = ScriptableObject.CreateInstance<PhotoViewSkin>();
            }

            bool changed = false;
            changed |= Set(ref skin.close, "photo_btn_close");
            changed |= Set(ref skin.polaroidFrame, "photo_polaroid_frame");
            changed |= Set(ref skin.photoMask, "photo_mask");
            changed |= Set(ref skin.tape, "photo_tape");
            changed |= Set(ref skin.dot, "photo_dot");
            changed |= Set(ref skin.dotOn, "photo_dot_on");
            changed |= Set(ref skin.avatar, "photo_avatar");
            changed |= Set(ref skin.inputBar, "photo_input_bar");
            changed |= Set(ref skin.chatInput, "chat_input");
            changed |= Set(ref skin.sendOn, "btn_send_on");
            changed |= Set(ref skin.sendOff, "btn_send_off");

            if (created) AssetDatabase.CreateAsset(skin, AssetPath);
            else if (changed) EditorUtility.SetDirty(skin);
            if (created || changed) AssetDatabase.SaveAssets();
            if (log || created) Debug.Log("[PhotoView] 照片檢視素材已更新：" + AssetPath);
        }

        static bool Set(ref Sprite field, string name)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + name + ".png");
            if (s == field) return false;
            field = s;
            return true;
        }
    }
}
