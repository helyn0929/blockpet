using BlockPet.UI.Decorate;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Decorate
{
    /// <summary>
    /// 自動建立／更新 Assets/Resources/UI/DecorateSkin.asset：佈置營地（設計文件 5.28）的素材從 Assets/Art/UI/Decorate/Sprites
    /// 依檔名填進去（xxx@3x.png；匯入設定見 TopBarSpriteImporter）。編譯完、或圖重新匯入時都會跑一次。
    /// </summary>
    public class DecorateSkinBuilder : AssetPostprocessor
    {
        const string SpriteFolder = "Assets/Art/UI/Decorate/Sprites/";
        const string AssetPath = "Assets/Resources/UI/DecorateSkin.asset";
        const string FontPath = "Assets/Art/Fonts/NotoSansCJKtc-Black SDF.asset";

        [InitializeOnLoadMethod]
        static void AutoBuild() => EditorApplication.delayCall += () => Build(false);

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.StartsWith(SpriteFolder)) { EditorApplication.delayCall += () => Build(false); return; }
        }

        [MenuItem("BlockPet/Decorate/Build Decorate Skin")]
        static void BuildFromMenu() => Build(true);

        public static void Build(bool log)
        {
            if (!AssetDatabase.IsValidFolder(SpriteFolder.TrimEnd('/'))) return;
            var skin = AssetDatabase.LoadAssetAtPath<DecorateSkin>(AssetPath);
            bool created = skin == null;
            if (created)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder("Assets/Resources/UI")) AssetDatabase.CreateFolder("Assets/Resources", "UI");
                skin = ScriptableObject.CreateInstance<DecorateSkin>();
            }
            bool changed = false;
            if (skin.font == null) { skin.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath); changed |= skin.font != null; }
            changed |= Set(ref skin.tileNormal, "deco_tile_normal");
            changed |= Set(ref skin.tileOk, "deco_tile_ok");
            changed |= Set(ref skin.tileTarget, "deco_tile_target");
            changed |= Set(ref skin.tileOccupied, "deco_tile_occupied");
            changed |= Set(ref skin.tileSelected, "deco_tile_selected");
            changed |= Set(ref skin.tileBlocked, "deco_tile_blocked");
            changed |= Set(ref skin.dim, "deco_dim");
            changed |= Set(ref skin.iconBuild, "deco_icon_build");
            changed |= Set(ref skin.btnPill, "deco_btn_pill");
            changed |= Set(ref skin.btnDone, "deco_btn_done");
            changed |= Set(ref skin.topbar, "deco_topbar");
            changed |= Set(ref skin.panel, "deco_panel");
            changed |= Set(ref skin.card, "deco_card");
            changed |= Set(ref skin.cardRing, "deco_card_ring");
            changed |= Set(ref skin.tagDark, "deco_tag_dark");
            changed |= Set(ref skin.shadow, "deco_shadow");
            if (created) AssetDatabase.CreateAsset(skin, AssetPath);
            else if (changed) EditorUtility.SetDirty(skin);
            if (created || changed) AssetDatabase.SaveAssets();
            if (log || created) Debug.Log("[Decorate] 佈置素材已更新：" + AssetPath);
        }

        static bool Set(ref Sprite field, string file)
        {
            Sprite s = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(SpriteFolder + file + "@3x.png")) if (o is Sprite sp) s = sp;
            if (s == null || s == field) return false;
            field = s;
            return true;
        }
    }
}
