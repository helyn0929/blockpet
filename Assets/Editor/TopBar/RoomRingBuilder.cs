using BlockPet.Hand;
using BlockPet.UI.Room;
using BlockPet.UI.TopBar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlockPet.EditorTools.TopBar
{
    /// <summary>
    /// 選單「BlockPet/Build Room Ring」：把設計稿「選擇寵物空間 E 角落轉盤」接到場景的 RoomPage。
    /// 1. RoomPage 加上 RoomRingController，接好字型（HandSkin）、愛心與進化色（TopBarConfig）、營地背景、寵物圖
    /// 2. 關掉舊的 UI Toolkit 選房頁（RoomUIController＋UIDocument），不刪除，要換回來時重新勾選即可
    /// 寵物圖沿用舊選房頁 RoomUIController 的 petSprites。
    /// </summary>
    public static class RoomRingBuilder
    {
        const string SkinPath = "Assets/Data/HandSkin.asset";
        const string ConfigPath = "Assets/Data/TopBarConfig.asset";
        const string BackgroundPath = "Assets/Art/UI/Mainroom_UI/forest1.jpeg";

        [MenuItem("BlockPet/Build Room Ring")]
        public static void Build()
        {
            var page = FindRoomPage();
            if (page == null)
            {
                EditorUtility.DisplayDialog("Build Room Ring", "目前場景找不到 RoomPage（PageManager 的 Room Page 或名為 RoomPage 的物件）。請先打開 Feed 場景。", "OK");
                return;
            }

            var ring = page.GetComponent<RoomRingController>();
            if (ring == null) ring = Undo.AddComponent<RoomRingController>(page);

            var skin = AssetDatabase.LoadAssetAtPath<HandSkin>(SkinPath);
            if (skin == null) Debug.LogWarning("[RoomRing] 找不到 " + SkinPath + "，先執行 BlockPet/Build Hand Skin");
            var config = AssetDatabase.LoadAssetAtPath<TopBarConfig>(ConfigPath);
            if (config == null) Debug.LogWarning("[RoomRing] 找不到 " + ConfigPath + "，先執行 BlockPet/Build TopBar");
            var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            if (bg == null) Debug.LogWarning("[RoomRing] 找不到背景圖 " + BackgroundPath);

            var so = new SerializedObject(ring);
            so.FindProperty("skin").objectReferenceValue = skin;
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("background").objectReferenceValue = bg;
            var pm = Object.FindObjectOfType<PageManager>(true);
            if (pm != null) so.FindProperty("pageManager").objectReferenceValue = pm;

            var old = page.GetComponent<RoomUIController>();
            if (old != null)
            {
                var src = new SerializedObject(old).FindProperty("petSprites");
                var dst = so.FindProperty("petSprites");
                dst.arraySize = src.arraySize;
                for (int i = 0; i < src.arraySize; i++)
                    dst.GetArrayElementAtIndex(i).objectReferenceValue = src.GetArrayElementAtIndex(i).objectReferenceValue;
                Undo.RecordObject(old, "Disable old room page");
                old.enabled = false;
            }
            so.ApplyModifiedProperties();

            var doc = page.GetComponent<UIDocument>();
            if (doc != null)
            {
                Undo.RecordObject(doc, "Disable old room page");
                doc.enabled = false;
            }

            EditorSceneManager.MarkSceneDirty(page.scene);
            Selection.activeGameObject = page;
            EditorGUIUtility.PingObject(page);
            Debug.Log("[RoomRing] 完成：" + page.name + " 已改用角落轉盤（舊的 RoomUIController／UIDocument 已關閉）。記得存檔場景。");
        }

        static GameObject FindRoomPage()
        {
            var pm = Object.FindObjectOfType<PageManager>(true);
            if (pm != null)
            {
                var prop = new SerializedObject(pm).FindProperty("roomPage");
                if (prop != null && prop.objectReferenceValue is GameObject go) return go;
            }
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.name == "RoomPage" && t.gameObject.scene.IsValid()) return t.gameObject;
            return null;
        }
    }
}
