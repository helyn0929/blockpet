using System.Collections.Generic;
using System.IO;
using BlockPet.UI.Market;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.EditorTools.Market
{
    /// <summary>
    /// 穿搭佔位圖截圖（設計文件 5.11 ⑧，不用按 Play）：每一件單獨穿在寵物身上，最後一格是好幾個部位一起穿。
    /// 輸出 Logs/outfit_preview.png。選單 BlockPet/Debug/穿搭截圖，或 batchmode：
    /// Unity -batchmode -projectPath . -executeMethod BlockPet.EditorTools.Market.OutfitSnapshot.CaptureBatch -quit
    /// </summary>
    public static class OutfitSnapshot
    {
        const int Scale = 3, Cols = 3, Cell = 130, Layer = 31;
        const string PetPath = "Assets/Art/UI/Mainroom_UI/Zapup.png";

        [MenuItem("BlockPet/Debug/穿搭截圖")]
        static void CaptureMenu() => EditorUtility.RevealInFinder(Capture());

        public static void CaptureBatch() => Capture();

        static string Capture()
        {
            var sets = new List<(string label, Dictionary<string, string> worn)>();
            foreach (var o in OutfitArt.Items) sets.Add((o.name, new Dictionary<string, string> { { o.slot, o.id } }));
            sets.Add(("一起穿", new Dictionary<string, string>
            {
                { "head", "outfit_strawhat" }, { "face", "outfit_glasses" }, { "neck", "outfit_scarf" }, { "side", "outfit_bow" },
            }));
            sets.Add(("翻面", new Dictionary<string, string> { { "head", "outfit_santa" }, { "neck", "outfit_scarf" }, { "side", "outfit_bow" } }));

            int rows = Mathf.CeilToInt(sets.Count / (float)Cols);
            int w = Cols * Cell * Scale, h = rows * Cell * Scale;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("OutfitSnapshotCam");
            camGo.transform.position = new Vector3(100000f, 100000f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0x8F / 255f, 0xA8 / 255f, 0x68 / 255f);
            cam.orthographic = true;
            cam.cullingMask = 1 << Layer;
            cam.targetTexture = rt;
            var canvasGo = new GameObject("OutfitSnapshotCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/outfit_preview.png"));
            try
            {
                var design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
                design.SetParent(canvasGo.transform, false);
                design.anchorMin = design.anchorMax = new Vector2(0.5f, 0.5f);
                design.pivot = new Vector2(0.5f, 0.5f);
                design.sizeDelta = new Vector2(Cols * Cell, rows * Cell);
                design.localScale = Vector3.one * Scale;
                Sprite pet = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(PetPath)) if (o is Sprite s) pet = s;

                for (int i = 0; i < sets.Count; i++)
                {
                    var cell = Place(design, "Cell", (i % Cols) * Cell, (i / Cols) * Cell, Cell, Cell);
                    const float pw = 100f, ph = 86f;
                    var petRt = Place(cell, "Pet", (Cell - pw) / 2f, 26, pw, ph);
                    var img = petRt.gameObject.AddComponent<Image>();
                    img.sprite = pet;
                    img.preserveAspect = true;
                    bool flip = sets[i].label == "翻面";
                    if (flip) { petRt.pivot = new Vector2(0.5f, 1f); petRt.anchoredPosition = new Vector2(Cell / 2f, -26f); petRt.localScale = new Vector3(-1f, 1f, 1f); }
                    var wear = Place(cell, "Wear", (Cell - pw) / 2f, 26, pw, ph);
                    OutfitArt.Dress(wear, sets[i].worn, pw, ph, flip);
                    var label = Place(cell, "Label", 0, 4, Cell, 18).gameObject.AddComponent<TMPro.TextMeshProUGUI>();
                    label.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/Art/Fonts/NotoSansCJKtc-Black SDF.asset");
                    label.text = sets[i].label;
                    label.fontSize = 12;
                    label.color = Color.white;
                    label.alignment = TMPro.TextAlignmentOptions.Center;
                }
                SetLayer(canvasGo.transform);
                Canvas.ForceUpdateCanvases();
                foreach (var t in canvasGo.GetComponentsInChildren<TMPro.TMP_Text>(true)) t.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[Outfit] 截圖：" + outPath);
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(canvasGo);
                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            return outPath;
        }

        static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            foreach (Transform c in t) SetLayer(c);
        }

        static RectTransform Place(Transform parent, string name, float left, float top, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }
    }
}
