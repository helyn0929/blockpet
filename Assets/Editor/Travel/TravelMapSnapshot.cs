using System.IO;
using BlockPet.UI.Travel;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Travel
{
    /// <summary>
    /// 旅行地圖截圖（不用按 Play）：在畫面外建一張 390 × 772 的地圖（@3x），存成 PNG。
    /// 選單 BlockPet/Travel/Snapshot Map，或 batchmode：
    /// Unity -batchmode -projectPath . -executeMethod BlockPet.EditorTools.Travel.TravelMapSnapshot.CaptureBatch -snapshotOut 路徑.png -quit
    /// </summary>
    public static class TravelMapSnapshot
    {
        const int Scale = 3;

        [MenuItem("BlockPet/Travel/Snapshot Map")]
        static void CaptureMenu()
        {
            string path = Path.Combine(Application.dataPath, "../Logs/travel_map.png");
            Capture(path);
            EditorUtility.RevealInFinder(path);
        }

        public static void CaptureBatch()
        {
            string outPath = Path.Combine(Application.dataPath, "../Logs/travel_map.png");
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-snapshotOut") outPath = args[i + 1];
            TravelSkinBuilder.Build(false);
            Capture(outPath);
        }

        public static void Capture(string outPath)
        {
            int w = 390 * Scale, h = 772 * Scale;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); // 不開 MSAA：URP 的 batchmode 會找不到 resolve 目標
            var camGo = new GameObject("TravelSnapshotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            cam.targetTexture = rt;

            var canvasGo = new GameObject("TravelSnapshotCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            try
            {
                Canvas.ForceUpdateCanvases();
                TravelScreen.BuildForCapture(canvas);
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
                Debug.Log("[Travel] 地圖截圖：" + outPath);
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(canvasGo);
                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
