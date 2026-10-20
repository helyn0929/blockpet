using BlockPet.Hand;
using BlockPet.UI.Room;
using BlockPet.UI.Settings;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlockPet.EditorTools.TopBar
{
    /// <summary>
    /// 選單「BlockPet/Build Settings Screen」：把設計稿「設定」接到場景的設定頁。
    /// 1. 舊設定頁（SettingsUIController 所在物件，PageManager 的 Settings Page）加上 SettingsScreenController，
    ///    接好字型、背景、寵物圖、PageManager、LoginUIHandler
    /// 2. 關掉舊的 UI Toolkit 設定頁（SettingsUIController＋UIDocument），不刪除，要換回來時重新勾選即可
    /// </summary>
    public static class SettingsScreenBuilder
    {
        const string SkinPath = "Assets/Data/HandSkin.asset";
        const string BackgroundPath = "Assets/Art/UI/Mainroom_UI/forest1.jpeg";

        [MenuItem("BlockPet/Build Settings Screen")]
        public static void Build()
        {
            var old = Object.FindObjectOfType<SettingsUIController>(true);
            if (old == null)
            {
                EditorUtility.DisplayDialog("Build Settings Screen", "目前場景找不到 SettingsUIController（舊設定頁）。請先打開 Feed 場景。", "OK");
                return;
            }
            var host = old.gameObject;

            var screen = host.GetComponent<SettingsScreenController>();
            if (screen == null) screen = Undo.AddComponent<SettingsScreenController>(host);

            var skin = AssetDatabase.LoadAssetAtPath<HandSkin>(SkinPath);
            if (skin == null) Debug.LogWarning("[Settings] 找不到 " + SkinPath + "，先執行 BlockPet/Build Hand Skin");
            var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            if (bg == null) Debug.LogWarning("[Settings] 找不到背景圖 " + BackgroundPath);

            var so = new SerializedObject(screen);
            var oldSo = new SerializedObject(old);
            so.FindProperty("skin").objectReferenceValue = skin;
            so.FindProperty("background").objectReferenceValue = bg;
            so.FindProperty("pageManager").objectReferenceValue =
                oldSo.FindProperty("pageManager").objectReferenceValue ?? Object.FindObjectOfType<PageManager>(true);
            so.FindProperty("loginUIHandler").objectReferenceValue =
                oldSo.FindProperty("loginUIHandler").objectReferenceValue ?? Object.FindObjectOfType<LoginUIHandler>(true);

            SerializedProperty src = null;
            var ring = Object.FindObjectOfType<RoomRingController>(true);
            if (ring != null) src = new SerializedObject(ring).FindProperty("petSprites");
            if (src == null || src.arraySize == 0)
            {
                var roomUi = Object.FindObjectOfType<RoomUIController>(true);
                if (roomUi != null) src = new SerializedObject(roomUi).FindProperty("petSprites");
            }
            if (src != null)
            {
                var dst = so.FindProperty("petSprites");
                dst.arraySize = src.arraySize;
                for (int i = 0; i < src.arraySize; i++)
                    dst.GetArrayElementAtIndex(i).objectReferenceValue = src.GetArrayElementAtIndex(i).objectReferenceValue;
            }
            so.ApplyModifiedProperties();

            Undo.RecordObject(old, "Disable old settings screen");
            old.enabled = false;
            var doc = host.GetComponent<UIDocument>();
            if (doc != null)
            {
                Undo.RecordObject(doc, "Disable old settings screen");
                doc.enabled = false;
            }

            EditorSceneManager.MarkSceneDirty(host.scene);
            Selection.activeGameObject = host;
            EditorGUIUtility.PingObject(host);
            Debug.Log("[Settings] 完成：" + host.name + " 已改用新版設定頁（舊的 SettingsUIController／UIDocument 已關閉）。記得存檔場景。");
        }
    }
}
