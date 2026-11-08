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
        /// <summary>相簿照片檢視（設計文件 5.14 ⑥；docs/photoview/photo_shutter_assets.json）</summary>
        public const string PhotoViewFolder = "Assets/Art/UI/PhotoView/Sprites/";
        /// <summary>分割畫面快門（設計文件 5.16 ⑤；同上 json）</summary>
        public const string ShutterFolder = "Assets/Art/UI/Shutter/Sprites/";
        /// <summary>圖鑑（設計文件 5.23；docs/dex/dex_assets.json）</summary>
        public const string DexFolder = "Assets/Art/UI/Dex/Sprites/";
        /// <summary>旅行（設計文件 5.21；docs/travel/travel_assets.json）</summary>
        public const string TravelFolder = "Assets/Art/UI/Travel/Sprites/";
        /// <summary>首頁 v9（設計文件 5.25；docs/home9/home9_assets.json）</summary>
        public const string Home9Folder = "Assets/Art/UI/Home9/Sprites/";
        /// <summary>拍立得分割畫面（設計文件 5.26；docs/polaroid/polaroid_assets.json）</summary>
        public const string PolaroidFolder = "Assets/Art/UI/Polaroid/Sprites/";
        /// <summary>佈置營地（設計文件 5.28；docs/decorate/decorate_assets.json）</summary>
        public const string DecorateFolder = "Assets/Art/UI/Decorate/Sprites/";
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
            // 照片檢視、快門（docs/photoview/photo_shutter_assets.json 的 9slice_px_at3x_LTRB）
            { "photo_polaroid_frame", new Vector4(24, 24, 24, 24) },
            { "photo_mask", new Vector4(12, 12, 12, 12) },
            { "photo_dot_on", new Vector4(9, 9, 9, 9) },
            { "photo_input_bar", new Vector4(6, 6, 6, 6) },
            { "chat_input", new Vector4(63, 63, 63, 63) },
            { "cam_toast_card", new Vector4(42, 42, 42, 42) },
            // 圖鑑（docs/dex/dex_assets.json 的 9slice_px_at3x_LTRB，換成 Unity 的 L,B,R,T）
            { "dex_header_bg", new Vector4(6, 6, 6, 6) },
            { "dex_tab", new Vector4(42, 42, 42, 42) },
            { "dex_tab_on", new Vector4(42, 42, 42, 42) },
            { "dex_series_card", new Vector4(66, 66, 66, 66) },
            { "dex_progress_track", new Vector4(9, 9, 9, 9) },
            { "dex_progress_fill", new Vector4(9, 9, 9, 9) },
            { "dex_tag_season", new Vector4(24, 24, 24, 24) },
            { "dex_cell", new Vector4(48, 48, 48, 48) },
            { "dex_cell_rare_empty", new Vector4(48, 48, 48, 48) },
            { "dex_cell_extreme", new Vector4(48, 48, 48, 48) },
            { "dex_badge_count", new Vector4(24, 24, 24, 24) },
            { "dex_shop_cell", new Vector4(48, 48, 48, 48) },
            { "dex_sheet_panel", new Vector4(84, 0, 84, 84) },
            { "dex_photo_mask", new Vector4(60, 60, 60, 60) },
            { "dex_gen_pill", new Vector4(30, 30, 30, 30) },
            { "dex_info_box", new Vector4(42, 42, 42, 42) },
            { "dex_palette_mask", new Vector4(18, 18, 18, 18) },
            { "dex_page_dot_on", new Vector4(9, 9, 9, 9) },
            { "dex_popup_card", new Vector4(72, 72, 72, 72) },
            { "btn_primary", new Vector4(48, 57, 54, 48) },
            { "btn_primary_lg", new Vector4(57, 66, 63, 57) },
            { "dex_new_tag", new Vector4(36, 42, 36, 36) },
            { "dex_reward_pill", new Vector4(48, 48, 48, 48) },
            // 旅行（docs/travel/travel_assets.json；檔名 xxx@3x.png，查表時去掉 @3x）
            { "map_label_bg", new Vector4(24, 24, 24, 24) },
            { "map_home_label", new Vector4(27, 30, 27, 27) },
            { "map_tag_pill", new Vector4(24, 24, 24, 24) },
            { "travel_header_bg", new Vector4(6, 6, 6, 6) },
            { "travel_badge_red_wide", new Vector4(27, 27, 27, 27) },
            { "travel_btn_add", new Vector4(57, 66, 57, 57) },
            { "travel_status_card", new Vector4(54, 54, 54, 54) },
            { "travel_progress_track", new Vector4(9, 9, 9, 9) },
            { "travel_progress_fill", new Vector4(9, 9, 9, 9) },
            { "travel_sheet_panel", new Vector4(84, 0, 84, 84) },
            { "travel_photo_mask", new Vector4(54, 54, 54, 54) },
            { "travel_info_cell", new Vector4(42, 42, 42, 42) },
            { "travel_btn_go", new Vector4(54, 66, 54, 54) },
            { "travel_glass_bar", new Vector4(78, 78, 78, 78) },
            { "travel_tag_traveling", new Vector4(27, 27, 27, 27) },
            { "bubble_left_tail", new Vector4(42, 42, 42, 42) },
            { "bubble_right_tail", new Vector4(42, 42, 42, 42) },
            { "travel_popup_card", new Vector4(60, 60, 60, 60) },
            { "passport_page", new Vector4(36, 36, 36, 36) },
            { "passport_times_badge", new Vector4(24, 24, 24, 24) },
            { "travel_btn_primary", new Vector4(45, 54, 45, 45) },
            { "friend_input_box", new Vector4(45, 45, 45, 45) },
            { "friend_btn_disabled", new Vector4(45, 45, 45, 45) },
            { "friend_invite_card", new Vector4(54, 54, 54, 54) },
            { "friend_btn_no", new Vector4(42, 42, 42, 42) },
            { "friend_btn_yes", new Vector4(42, 51, 42, 42) },
            { "camp_code_card", new Vector4(54, 54, 54, 54) },
            { "camp_code_btn_copy", new Vector4(42, 42, 42, 42) },
            { "camp_code_btn_share", new Vector4(42, 51, 42, 42) },
            // 首頁 v9（docs/home9/home9_assets.json 的 LTRB，換成 L,B,R,T）
            { "tag_latest", new Vector4(24, 30, 24, 24) },
            { "tag_more", new Vector4(33, 33, 33, 33) },
            { "pet_bubble", new Vector4(48, 48, 48, 48) },
            // 拍立得（docs/polaroid/polaroid_assets.json 的 LTRB，四邊都一樣）
            { "pl_dtab_on", new Vector4(42, 42, 42, 42) },
            { "pl_dtab_off", new Vector4(42, 42, 42, 42) },
            { "pl_cell_alt", new Vector4(42, 42, 42, 42) },
            { "pl_pet_frame", new Vector4(63, 63, 63, 63) },
            { "pl_panel_mask", new Vector4(63, 63, 63, 63) },
            { "pl_status_pill", new Vector4(42, 42, 42, 42) },
            { "pl_input_bar", new Vector4(69, 69, 69, 69) },
            { "pl_cell", new Vector4(48, 48, 48, 48) },
            { "pl_cell_pet", new Vector4(48, 48, 48, 48) },
            { "pl_cell_ring", new Vector4(60, 60, 60, 60) },
            { "pl_sel_dash", new Vector4(45, 45, 45, 45) },
            { "pl_tag_dark", new Vector4(27, 27, 27, 27) },
            { "pl_toast", new Vector4(51, 51, 51, 51) },
            { "pl_pill_comment", new Vector4(45, 45, 45, 45) },
            { "pl_tag_cat", new Vector4(42, 42, 42, 42) },
            // 佈置營地（decorate_assets.json：佈置鈕 69、完成 57、面板上 66 左右 66）
            { "deco_btn_pill", new Vector4(69, 69, 69, 69) },
            { "deco_btn_done", new Vector4(57, 57, 57, 57) },
            { "deco_panel", new Vector4(66, 0, 66, 66) },
            { "deco_tag_dark", new Vector4(27, 27, 27, 27) },
        };

        // 改了設定就把版本 +1，Unity 會自動重新匯入
        public override uint GetVersion() => 15;

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!(path.StartsWith(Folder) || path.StartsWith(HandFolder) || path.StartsWith(IconsFolder) || path.StartsWith(MarketFolder) || path.StartsWith(ChatFolder)
                   || path.StartsWith(PhotoViewFolder) || path.StartsWith(ShutterFolder) || path.StartsWith(DexFolder) || path.StartsWith(TravelFolder) || path.StartsWith(Home9Folder)
                   || path.StartsWith(PolaroidFolder) || path.StartsWith(DecorateFolder))
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

            string name = Path.GetFileNameWithoutExtension(path).Replace("@3x", "");
            importer.spriteBorder = Borders.TryGetValue(name, out var b) ? b : Vector4.zero;
            // 首頁 v9 的 Home、選單箭頭：執行時要讀像素做白色光暈（Home9TopIcons）
            // 拍立得的線條圖示（深色）：執行時做白色版本，才能上畫框字色、做白光（PUI.White）
            importer.isReadable = name == "icon_home_line" || name == "icon_menu_chevron" || name == "icon_album_line"
                || name == "pl_icon_edit" || name == "pl_icon_pencil" || name == "pl_icon_back" || name == "pl_icon_share" || name == "pl_icon_download";
        }

        [MenuItem("BlockPet/TopBar/Reimport Sprites")]
        public static void ReimportAll()
        {
            ReimportFolder(Folder);
            ReimportFolder(IconsFolder);
            ReimportFolder(HandFolder);
            ReimportFolder(PhotoViewFolder);
            ReimportFolder(ShutterFolder);
            ReimportFolder(DexFolder);
            ReimportFolder(TravelFolder);
            ReimportFolder(Home9Folder);
            ReimportFolder(PolaroidFolder);
            ReimportFolder(DecorateFolder);
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
