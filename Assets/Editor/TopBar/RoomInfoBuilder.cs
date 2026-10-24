using BlockPet.Hand;
using BlockPet.UI.Room;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlockPet.EditorTools.TopBar
{
    /// <summary>
    /// 選單「BlockPet/Build Room Info」：把設計稿「空間資訊」（取代 Pet Settings）接到場景的 PetSettingPage。
    /// 1. PetSettingPage 加上 RoomInfoScreenController，接好字型、背景、寵物圖、PageManager
    /// 2. 關掉舊的 UI Toolkit 頁面（PetSettingPageController＋UIDocument），不刪除
    /// 打開方式不變：點狀態欄的寵物頭像（PageManager.ShowPetSettingPage）。
    /// </summary>
    public static class RoomInfoBuilder
    {
        const string SkinPath = "Assets/Data/HandSkin.asset";
        const string BackgroundPath = "Assets/Art/UI/Mainroom_UI/forest1.jpeg";

        [MenuItem("BlockPet/Build Room Info")]
        public static void Build()
        {
            var pm = Object.FindObjectOfType<PageManager>(true);
            var page = pm != null ? new SerializedObject(pm).FindProperty("petSettingPage").objectReferenceValue as GameObject : null;
            if (page == null)
            {
                var old0 = Object.FindObjectOfType<PetSettingPageController>(true);
                if (old0 != null) page = old0.gameObject;
            }
            if (page == null)
            {
                EditorUtility.DisplayDialog("Build Room Info", "目前場景找不到 PetSettingPage（PageManager 的 Pet Setting Page）。請先打開 Feed 場景。", "OK");
                return;
            }

            var screen = page.GetComponent<RoomInfoScreenController>();
            if (screen == null) screen = Undo.AddComponent<RoomInfoScreenController>(page);
            var so = new SerializedObject(screen);
            so.FindProperty("skin").objectReferenceValue = AssetDatabase.LoadAssetAtPath<HandSkin>(SkinPath);
            so.FindProperty("background").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            so.FindProperty("pageManager").objectReferenceValue = pm;

            SerializedProperty src = null;
            var ring = Object.FindObjectOfType<RoomRingController>(true);
            if (ring != null) src = new SerializedObject(ring).FindProperty("petSprites");
            if (src == null || src.arraySize == 0)
            {
                var oldPage = page.GetComponentInChildren<PetSettingPageController>(true);
                if (oldPage != null) src = new SerializedObject(oldPage).FindProperty("petSprites");
            }
            if (src != null)
            {
                var dst = so.FindProperty("petSprites");
                dst.arraySize = src.arraySize;
                for (int i = 0; i < src.arraySize; i++)
                    dst.GetArrayElementAtIndex(i).objectReferenceValue = src.GetArrayElementAtIndex(i).objectReferenceValue;
            }
            so.ApplyModifiedProperties();

            foreach (var old in page.GetComponentsInChildren<PetSettingPageController>(true))
            {
                Undo.RecordObject(old, "Disable old pet settings");
                old.enabled = false;
                var doc = old.GetComponent<UIDocument>();
                if (doc != null) { Undo.RecordObject(doc, "Disable old pet settings"); doc.enabled = false; }
            }

            EditorSceneManager.MarkSceneDirty(page.scene);
            Selection.activeGameObject = page;
            EditorGUIUtility.PingObject(page);
            Debug.Log("[RoomInfo] 完成：" + page.name + " 已改用新版空間資訊（舊的 PetSettingPageController／UIDocument 已關閉）。記得存檔場景。");
        }
    }
}
