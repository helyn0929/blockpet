using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.UI.Album;
using BlockPet.UI.Chat;
using BlockPet.UI.Room;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlockPet.EditorTools.TopBar
{
    /// <summary>
    /// 選單「BlockPet/Build Chat And Album」：把設計稿「聊天室與相簿」接到場景的 ChatPage／AlbumPage。
    /// 1. 重新匯入 emoji 圖（Assets/Art/UI/Chat/Sprites，docs/chat/Tools/make_emoji_sprites.py 產生）
    /// 2. ChatPage 加 ChatScreenController、AlbumPage 加 AlbumScreenController，接好字型、背景、寵物圖、emoji
    /// 3. 關掉舊的：聊天室的 ChatUIController＋UIDocument（ChatUIHandler 保留）、相簿的 AlbumPanel（AlbumUIManager 保留，點照片看大圖還是用它）
    /// </summary>
    public static class ChatAlbumBuilder
    {
        const string SkinPath = "Assets/Data/HandSkin.asset";
        const string BackgroundPath = "Assets/Art/UI/Mainroom_UI/forest1.jpeg";

        [MenuItem("BlockPet/Build Chat And Album")]
        public static void Build()
        {
            var pm = Object.FindObjectOfType<PageManager>(true);
            if (pm == null)
            {
                EditorUtility.DisplayDialog("Build Chat And Album", "目前場景找不到 PageManager。請先打開 Feed 場景。", "OK");
                return;
            }
            var pso = new SerializedObject(pm);
            var chatPage = pso.FindProperty("chatPage").objectReferenceValue as GameObject;
            var albumPage = pso.FindProperty("albumPage").objectReferenceValue as GameObject;
            if (chatPage == null || albumPage == null)
            {
                EditorUtility.DisplayDialog("Build Chat And Album", "PageManager 的 Chat Page 或 Album Page 沒有設定。", "OK");
                return;
            }

            TopBarSpriteImporter.ReimportFolder(TopBarSpriteImporter.ChatFolder);
            var emoji = new List<Sprite>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { TopBarSpriteImporter.ChatFolder.TrimEnd('/') }))
            {
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null && s.name.StartsWith("emoji_")) emoji.Add(s);
            }
            if (emoji.Count == 0) Debug.LogWarning("[ChatAlbum] 找不到 emoji 圖，先執行 python3 docs/chat/Tools/make_emoji_sprites.py");

            var skin = AssetDatabase.LoadAssetAtPath<HandSkin>(SkinPath);
            var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);

            // 聊天室
            var chat = chatPage.GetComponent<ChatScreenController>();
            if (chat == null) chat = Undo.AddComponent<ChatScreenController>(chatPage);
            var cso = new SerializedObject(chat);
            cso.FindProperty("skin").objectReferenceValue = skin;
            cso.FindProperty("background").objectReferenceValue = bg;
            cso.FindProperty("pageManager").objectReferenceValue = pm;
            SetList(cso.FindProperty("emojiSprites"), emoji);
            CopyPetSprites(cso.FindProperty("petSprites"));
            cso.ApplyModifiedProperties();
            foreach (var old in chatPage.GetComponentsInChildren<ChatUIController>(true))
            {
                Undo.RecordObject(old, "Disable old chat");
                old.enabled = false;
                var doc = old.GetComponent<UIDocument>();
                if (doc != null) { Undo.RecordObject(doc, "Disable old chat"); doc.enabled = false; }
            }

            // 相簿
            var album = albumPage.GetComponent<AlbumScreenController>();
            if (album == null) album = Undo.AddComponent<AlbumScreenController>(albumPage);
            var aso = new SerializedObject(album);
            aso.FindProperty("skin").objectReferenceValue = skin;
            aso.FindProperty("background").objectReferenceValue = bg;
            aso.FindProperty("pageManager").objectReferenceValue = pm;
            SetList(aso.FindProperty("emojiSprites"), emoji);
            aso.ApplyModifiedProperties();
            foreach (var old in albumPage.GetComponentsInChildren<AlbumUI>(true))
            {
                if (old.gameObject == albumPage) { Undo.RecordObject(old, "Disable old album"); old.enabled = false; continue; }
                Undo.RecordObject(old.gameObject, "Disable old album");
                old.gameObject.SetActive(false);
            }

            EditorSceneManager.MarkSceneDirty(chatPage.scene);
            Selection.activeGameObject = chatPage;
            Debug.Log($"[ChatAlbum] 完成：ChatPage／AlbumPage 已改用新版聊天室與相簿（{emoji.Count} 個 emoji）。舊的 ChatUIController／AlbumPanel 已關閉。記得存檔場景。");
        }

        static void SetList(SerializedProperty prop, List<Sprite> sprites)
        {
            prop.arraySize = sprites.Count;
            for (int i = 0; i < sprites.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        }

        static void CopyPetSprites(SerializedProperty dst)
        {
            SerializedProperty src = null;
            var ring = Object.FindObjectOfType<RoomRingController>(true);
            if (ring != null) src = new SerializedObject(ring).FindProperty("petSprites");
            if (src == null || src.arraySize == 0)
            {
                var roomUi = Object.FindObjectOfType<RoomUIController>(true);
                if (roomUi != null) src = new SerializedObject(roomUi).FindProperty("petSprites");
            }
            if (src == null) return;
            dst.arraySize = src.arraySize;
            for (int i = 0; i < src.arraySize; i++)
                dst.GetArrayElementAtIndex(i).objectReferenceValue = src.GetArrayElementAtIndex(i).objectReferenceValue;
        }
    }
}
