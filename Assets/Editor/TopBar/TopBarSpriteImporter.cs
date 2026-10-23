using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.TopBar
{
    /// <summary>
    /// 自動設定 Assets/Art/UI/TopBar/Sprites、TopBar/Icons、Hand/Sprites 與 Market/Sprites 的匯入（README「匯入設定」）：
    /// Sprite (2D and UI)、Pixels Per Unit 300（素材 @3x）、Full Rect、Bilinear、不壓縮、無 Mip Maps，
    /// 9-slice 邊界照 README 表格（單位 px，順序 L,B,R,T）。
    /// </summary>
    public class TopBarSpriteImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/Art/UI/TopBar/Sprites/";
        public const string HandFolder = "Assets/Art/UI/Hand/Sprites/";
        /// <summary>Home 鍵、選單「佈置」圖示（設計文件 5.7；來源 專案根目錄 Icons/*@3x.png）</summary>
        public const string IconsFolder = "Assets/Art/UI/TopBar/Icons/";
        /// <summary>商店的家具／裝飾／擴建／寵物蛋圖（docs/market/Tools/make_market_sprites.py 產生）</summary>
        public const string MarketFolder = "Assets/Art/UI/Market/Sprites/";
        /// <summary>聊天室 emoji（docs/chat/Tools/make_emoji_sprites.py 產生）</summary>
        public const string ChatFolder = "Assets/Art/UI/Chat/Sprites/";
        const float PixelsPerUnit = 300f;

        static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {
            { "ui_topbar_panel",        new Vector4(87, 87, 87, 87) },
            { "ui_topbar_panel_shadow", new Vector4(159, 159, 159, 159) },
            { "ui_lv_badge",            new Vector4(18, 18, 18, 18) },
            { "ui_pill_17",             new Vector4(25, 25, 25, 25) },
            { "ui_evo_track",           new Vector4(12, 12, 12, 12) },
            { "ui_evo_mask",            new Vector4(12, 12, 12, 12) },
            { "ui_evo_ready_glow",      new Vector4(54, 54, 54, 54) },
            { "ui_coin_pill",           new Vector4(45, 45, 45, 45) },
            // 手牌（docs/hand/sprites.json，由 docs/hand/Tools/make_hand_sprites.py 產生）
            { "ui_hand_card", new Vector4(30, 30, 30, 30) },
            { "ui_hand_card_shadow", new Vector4(102, 102, 102, 102) },
            { "ui_mask_r6", new Vector4(18, 18, 18, 18) },
            { "ui_mask_r10", new Vector4(30, 30, 30, 30) },
            { "ui_mask_r3", new Vector4(9, 9, 9, 9) },
            { "ui_popup", new Vector4(48, 48, 48, 48) },
            { "ui_popup_shadow", new Vector4(192, 192, 192, 192) },
            { "ui_btn_beige", new Vector4(39, 39, 39, 39) },
            { "ui_btn_yellow", new Vector4(39, 48, 39, 39) },
            { "ui_latest_card", new Vector4(36, 36, 36, 36) },
            { "ui_latest_back_shadow", new Vector4(108, 108, 108, 108) },
            { "ui_latest_glow_lo", new Vector4(132, 132, 132, 132) },
            { "ui_latest_glow_hi", new Vector4(132, 132, 132, 132) },
            { "ui_badge_new", new Vector4(24, 30, 24, 24) },
            { "ui_glass_pill", new Vector4(48, 48, 48, 48) },
            { "ui_pill_yellow", new Vector4(45, 45, 45, 45) },
        };

        // 改了設定就把版本 +1，Unity 會自動重新匯入
        public override uint GetVersion() => 5;

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!(path.StartsWith(Folder) || path.StartsWith(HandFolder) || path.StartsWith(IconsFolder) || path.StartsWith(MarketFolder) || path.StartsWith(ChatFolder))
                || !path.EndsWith(".png")) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);

            string name = Path.GetFileNameWithoutExtension(path);
            importer.spriteBorder = Borders.TryGetValue(name, out var b) ? b : Vector4.zero;
        }

        [MenuItem("BlockPet/TopBar/Reimport Sprites")]
        public static void ReimportAll()
        {
            ReimportFolder(Folder);
            ReimportFolder(IconsFolder);
            ReimportFolder(HandFolder);
        }

        public static void ReimportFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder.TrimEnd('/'))) return;
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder.TrimEnd('/') });
            foreach (var g in guids)
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
            Debug.Log($"[UI] 重新匯入 {guids.Length} 張素材：{folder}");
        }
    }
}
