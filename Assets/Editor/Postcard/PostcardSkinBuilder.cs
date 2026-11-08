using BlockPet.UI.Travel;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Postcard
{
    /// <summary>
    /// 自動建立／更新 Assets/Resources/UI/PostcardSkin.asset：寄明信片（設計文件 5.21）的素材從 Assets/Art/UI/Postcard/Sprites
    /// 依檔名填進去（pc_xxx@3x.png；匯入設定見 TopBarSpriteImporter）。編譯完、或圖重新匯入時都會跑一次。
    /// </summary>
    public class PostcardSkinBuilder : AssetPostprocessor
    {
        const string SpriteFolder = "Assets/Art/UI/Postcard/Sprites/";
        const string AssetPath = "Assets/Resources/UI/PostcardSkin.asset";
        const string FontPath = "Assets/Art/Fonts/NotoSansCJKtc-Black SDF.asset";

        [InitializeOnLoadMethod]
        static void AutoBuild() => EditorApplication.delayCall += () => Build(false);

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.StartsWith(SpriteFolder)) { EditorApplication.delayCall += () => Build(false); return; }
        }

        [MenuItem("BlockPet/Postcard/Build Postcard Skin")]
        static void BuildFromMenu() => Build(true);

        public static void Build(bool log)
        {
            if (!AssetDatabase.IsValidFolder(SpriteFolder.TrimEnd('/'))) return;
            var skin = AssetDatabase.LoadAssetAtPath<PostcardSkin>(AssetPath);
            bool created = skin == null;
            if (created)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");
                skin = ScriptableObject.CreateInstance<PostcardSkin>();
            }
            bool changed = false;
            if (skin.font == null) { skin.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath); changed |= skin.font != null; }
            foreach (var (field, file) in PostcardSkin.Map)
            {
                Sprite s = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(SpriteFolder + file + "@3x.png")) if (o is Sprite sp) s = sp;
                var fi = typeof(PostcardSkin).GetField(field);
                if (s == null || fi == null || (Sprite)fi.GetValue(skin) == s) continue;
                fi.SetValue(skin, s);
                changed = true;
            }
            if (created) AssetDatabase.CreateAsset(skin, AssetPath);
            else if (changed) EditorUtility.SetDirty(skin);
            if (created || changed) AssetDatabase.SaveAssets();
            if (log || created) Debug.Log("[Postcard] 明信片素材已更新：" + AssetPath);
        }
    }
}
