using System.IO;
using BlockPet.Hand;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockPet.EditorTools.TopBar
{
    /// <summary>
    /// 選單「BlockPet/Build Hand Skin」：
    /// 1. 重新匯入 Assets/Art/UI/Hand/Sprites（設計稿切出的手牌素材，套用 9-slice 等設定）
    /// 2. 產生／更新 Assets/Data/HandSkin.asset（素材＋Noto Sans TC Black 字型＋標籤陰影材質）
    /// 3. 接到目前場景所有 HandController 的 Skin 欄位
    /// 素材要更新時：python3 docs/hand/Tools/make_hand_sprites.py，再按一次這個選單。
    /// </summary>
    public static class HandSkinBuilder
    {
        const string SkinPath = "Assets/Data/HandSkin.asset";

        [MenuItem("BlockPet/Build Hand Skin")]
        public static void Build()
        {
            TopBarSpriteImporter.ReimportFolder(TopBarSpriteImporter.HandFolder);

            var skin = AssetDatabase.LoadAssetAtPath<HandSkin>(SkinPath);
            if (skin == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SkinPath));
                skin = ScriptableObject.CreateInstance<HandSkin>();
                AssetDatabase.CreateAsset(skin, SkinPath);
            }

            Sprite S(string n)
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(TopBarSpriteImporter.HandFolder + n + ".png");
                if (s == null) Debug.LogWarning("[Hand] 找不到素材 " + n + "（先執行 docs/hand/Tools/make_hand_sprites.py）");
                return s;
            }
            skin.card = S("ui_hand_card");
            skin.cardShadow = S("ui_hand_card_shadow");
            skin.cardCamera = S("ui_hand_card_camera");
            skin.iconCamera = S("ui_icon_camera");
            skin.maskR6 = S("ui_mask_r6");
            skin.maskR10 = S("ui_mask_r10");
            skin.maskR3 = S("ui_mask_r3");
            skin.popup = S("ui_popup");
            skin.popupShadow = S("ui_popup_shadow");
            skin.buttonBeige = S("ui_btn_beige");
            skin.buttonYellow = S("ui_btn_yellow");
            skin.dockTile = S("ui_dock_tile");
            skin.dockCamera = S("ui_dock_camera");
            skin.iconChat = S("ui_icon_chat");
            skin.albumMinis = S("ui_album_minis");
            skin.latestCard = S("ui_latest_card");
            skin.latestBackShadow = S("ui_latest_back_shadow");
            skin.latestGlowLo = S("ui_latest_glow_lo");
            skin.latestGlowHi = S("ui_latest_glow_hi");
            skin.badgeNew = S("ui_badge_new");
            skin.iconEye = S("ui_icon_eye");
            skin.circle = S("ui_circle");
            skin.glassPill = S("ui_glass_pill");
            skin.pillYellow = S("ui_pill_yellow");

            // 與頂部狀態欄同一套字型（設計稿 Noto Sans TC Black）
            skin.font = TopBarBuilder.LoadOrCreateFont();
            skin.labelShadowMaterial = TopBarBuilder.LoadOrCreateLabelMaterial(skin.font);
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var hand in Object.FindObjectsOfType<HandController>(true))
            {
                var so = new SerializedObject(hand);
                so.FindProperty("skin").objectReferenceValue = skin;
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(hand.gameObject.scene);
                n++;
            }
            Selection.activeObject = skin;
            EditorGUIUtility.PingObject(skin);
            Debug.Log($"[Hand] 完成：{SkinPath}，已接到場景中 {n} 個 HandController");
            if (n == 0) Debug.LogWarning("[Hand] 目前場景沒有 HandController，請到 Hand 物件的 Inspector 手動把 Skin 設成 HandSkin");
        }
    }
}
