using BlockPet.Hand;
using BlockPet.UI.Room;
using BlockPet.UI.TopBar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockPet.EditorTools.TopBar
{
    /// <summary>
    /// 選單「BlockPet/Build Split View」：把設計稿首頁 v8 的分割畫面（Camp8Photo）掛到手牌物件上。
    /// 手牌最右邊的營地最新照片卡點下去就會打開（HandController.OnLatestClicked）。
    /// </summary>
    public static class SplitViewBuilder
    {
        const string SkinPath = "Assets/Data/HandSkin.asset";
        const string ConfigPath = "Assets/Data/TopBarConfig.asset";
        const string BackgroundPath = "Assets/Art/UI/Mainroom_UI/forest1.jpeg";

        [MenuItem("BlockPet/Build Split View")]
        public static void Build()
        {
            var hand = Object.FindObjectOfType<HandController>(true);
            if (hand == null)
            {
                EditorUtility.DisplayDialog("Build Split View", "目前場景找不到 HandController（手牌）。請先打開 Feed 場景。", "OK");
                return;
            }
            var view = hand.GetComponent<PhotoSplitView>();
            if (view == null) view = Undo.AddComponent<PhotoSplitView>(hand.gameObject);
            var so = new SerializedObject(view);
            so.FindProperty("skin").objectReferenceValue = AssetDatabase.LoadAssetAtPath<HandSkin>(SkinPath);
            so.FindProperty("config").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TopBarConfig>(ConfigPath);
            so.FindProperty("background").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            var ring = Object.FindObjectOfType<RoomRingController>(true);
            if (ring != null)
            {
                var src = new SerializedObject(ring).FindProperty("petSprites");
                var dst = so.FindProperty("petSprites");
                dst.arraySize = src.arraySize;
                for (int i = 0; i < src.arraySize; i++)
                    dst.GetArrayElementAtIndex(i).objectReferenceValue = src.GetArrayElementAtIndex(i).objectReferenceValue;
            }
            else Debug.LogWarning("[SplitView] 找不到 RoomRingController，寵物圖請在 PhotoSplitView 手動指定");
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(hand.gameObject.scene);
            Selection.activeGameObject = hand.gameObject;
            Debug.Log("[SplitView] 完成：已在 " + hand.name + " 加上 PhotoSplitView。記得存檔場景。");
        }
    }
}
