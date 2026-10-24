using System.IO;
using BlockPet.Vision;
using Unity.Sentis;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockPet.EditorTools.Vision
{
    /// <summary>
    /// 照片辨識（第 3 步）的編輯器選單：
    /// - BlockPet/Build Photo Classifier：場景加一個 PhotoClassifier 物件，接好模型與類別向量
    /// - BlockPet/Vision/Test Editor Photos：用專案根目錄 EditorHandPhotos/ 的照片在 Unity 裡辨識一次，結果印在 Console
    ///   （可以和 python3 docs/vision/Tools/build_photo_classifier.py MODEL --eval EditorHandPhotos 的結果對照）
    /// 模型與向量由 docs/vision/Tools/build_photo_classifier.py 產生。
    /// </summary>
    public static class PhotoClassifierBuilder
    {
        const string ModelPath = "Assets/Models/photo_classifier_vision.onnx";
        const string CategoriesPath = "Assets/Data/Vision/photo_categories.json";

        [MenuItem("BlockPet/Build Photo Classifier")]
        public static void Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            var cats = AssetDatabase.LoadAssetAtPath<TextAsset>(CategoriesPath);
            if (model == null || cats == null)
            {
                EditorUtility.DisplayDialog("Build Photo Classifier", "找不到 " + ModelPath + " 或 " + CategoriesPath + "。\n先確認 Sentis 套件已安裝、檔案已匯入。", "OK");
                return;
            }
            var pc = Object.FindObjectOfType<PhotoClassifier>(true);
            if (pc == null)
            {
                var go = new GameObject("PhotoClassifier");
                Undo.RegisterCreatedObjectUndo(go, "Create PhotoClassifier");
                pc = go.AddComponent<PhotoClassifier>();
            }
            Assign(pc, model, cats);
            EditorSceneManager.MarkSceneDirty(pc.gameObject.scene);
            Selection.activeGameObject = pc.gameObject;
            Debug.Log("[Vision] 完成：場景的 PhotoClassifier 已接好模型與類別向量。記得存檔場景。");
        }

        [MenuItem("BlockPet/Vision/Test Editor Photos")]
        public static void TestEditorPhotos()
        {
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            var cats = AssetDatabase.LoadAssetAtPath<TextAsset>(CategoriesPath);
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "EditorHandPhotos");
            if (model == null || cats == null || !Directory.Exists(folder))
            {
                Debug.LogWarning("[Vision] 缺少模型、類別向量或 EditorHandPhotos 資料夾");
                return;
            }
            var go = new GameObject("PhotoClassifier (test)") { hideFlags = HideFlags.HideAndDontSave };
            var pc = go.AddComponent<PhotoClassifier>();
            Assign(pc, model, cats);
            try
            {
                foreach (var file in Directory.GetFiles(folder))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".jpg" && ext != ".jpeg" && ext != ".png") continue;
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(File.ReadAllBytes(file));
                    var r = pc.Classify(tex);
                    Debug.Log($"[Vision] {Path.GetFileName(file)} → {(r.Ok ? r.category : "失敗")} {r.confidence:0.00}");
                    Object.DestroyImmediate(tex);
                }
            }
            finally
            {
                pc.ReleaseWorker();
                Object.DestroyImmediate(go);
            }
        }

        static void Assign(PhotoClassifier pc, ModelAsset model, TextAsset cats)
        {
            var so = new SerializedObject(pc);
            so.FindProperty("model").objectReferenceValue = model;
            so.FindProperty("categories").objectReferenceValue = cats;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
