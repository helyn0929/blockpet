using BlockPet.Hand;
using BlockPet.UI.Auth;
using BlockPet.UI.Room;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlockPet.EditorTools.TopBar
{
    /// <summary>
    /// 選單「BlockPet/Build Auth Screen」：把設計稿「帳號登入／註冊」接到場景的登入頁。
    /// 1. 舊登入頁（LoginScreenController 所在物件）加上 AuthScreenController，接好字型、背景、寵物圖、LoginUIHandler
    /// 2. 關掉舊的 UI Toolkit 登入頁（LoginScreenController＋UIDocument），不刪除，要換回來時重新勾選即可
    /// 寵物圖沿用 RoomRingController（或舊選房頁 RoomUIController）的 petSprites。
    /// </summary>
    public static class AuthScreenBuilder
    {
        const string SkinPath = "Assets/Data/HandSkin.asset";
        const string BackgroundPath = "Assets/Art/UI/Mainroom_UI/forest1.jpeg";

        [MenuItem("BlockPet/Build Auth Screen")]
        public static void Build()
        {
            var old = Object.FindObjectOfType<LoginScreenController>(true);
            if (old == null)
            {
                EditorUtility.DisplayDialog("Build Auth Screen", "目前場景找不到 LoginScreenController（舊登入頁）。請先打開 Feed 場景。", "OK");
                return;
            }
            var host = old.gameObject;

            var auth = host.GetComponent<AuthScreenController>();
            if (auth == null) auth = Undo.AddComponent<AuthScreenController>(host);

            var skin = AssetDatabase.LoadAssetAtPath<HandSkin>(SkinPath);
            if (skin == null) Debug.LogWarning("[Auth] 找不到 " + SkinPath + "，先執行 BlockPet/Build Hand Skin");
            var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            if (bg == null) Debug.LogWarning("[Auth] 找不到背景圖 " + BackgroundPath);

            var so = new SerializedObject(auth);
            so.FindProperty("skin").objectReferenceValue = skin;
            so.FindProperty("background").objectReferenceValue = bg;
            var handler = new SerializedObject(old).FindProperty("loginUIHandler").objectReferenceValue
                          ?? Object.FindObjectOfType<LoginUIHandler>(true);
            so.FindProperty("loginUIHandler").objectReferenceValue = handler;

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
            else Debug.LogWarning("[Auth] 找不到寵物圖來源，請在 AuthScreenController 的 Pet Sprites 手動指定");
            so.ApplyModifiedProperties();

            Undo.RecordObject(old, "Disable old login screen");
            old.enabled = false;
            var doc = host.GetComponent<UIDocument>();
            if (doc != null)
            {
                Undo.RecordObject(doc, "Disable old login screen");
                doc.enabled = false;
            }

            EditorSceneManager.MarkSceneDirty(host.scene);
            Selection.activeGameObject = host;
            EditorGUIUtility.PingObject(host);
            Debug.Log("[Auth] 完成：" + host.name + " 已改用新版登入／註冊頁（舊的 LoginScreenController／UIDocument 已關閉）。記得存檔場景。");
        }
    }
}
