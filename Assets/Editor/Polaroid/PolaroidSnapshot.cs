using System.Collections.Generic;
using System.IO;
using BlockPet.Polaroid;
using BlockPet.UI.Polaroid;
using UnityEditor;
using UnityEngine;

namespace BlockPet.EditorTools.Polaroid
{
    /// <summary>
    /// 拍立得分割畫面截圖（設計文件 5.26，不用按 Play）：用測試照片、假的貼紙和留言畫出整個畫面，
    /// 另外輸出一張下載用的整張拍立得。輸出到 Logs/polaroid_*.png。
    /// 選單 BlockPet/Debug/拍立得截圖，或 batchmode：
    /// Unity -batchmode -projectPath . -executeMethod BlockPet.EditorTools.Polaroid.PolaroidSnapshot.CaptureBatch -quit
    /// </summary>
    public static class PolaroidSnapshot
    {
        const int Scale = 3;
        const int Layer = 31;
        const string PhotoPath = "Assets/Art/Test/photo.jpg";

        [MenuItem("BlockPet/Debug/拍立得截圖")]
        static void CaptureMenu() => EditorUtility.RevealInFinder(CaptureAll());

        public static void CaptureBatch() => CaptureAll();

        static string CaptureAll()
        {
            PolaroidSkinBuilder.Build(false);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
            var photo = LoadReadable(PhotoPath);
            var meta = new PhotoMeta("demo.jpg") { takerUserId = "amy", takerName = "小美", category = "美食", timestamp = System.DateTime.Now.AddMinutes(-3).ToString("yyyy-MM-dd HH:mm:ss") };
            var comments = new List<ChatEntry>
            {
                Comment("媽媽", "mom", "下次帶我去吃"),
                Comment("阿哲", "zhe", "看起來好好吃"),
            };

            // ① 別人的照片、寵物頁（預設）：想吃美食 → 跳；照片上的留言、大家貼的貼紙
            var style1 = new PolaroidStyle { frame = "white", filter = "", caption = "今天的拉麵" };
            var stickers1 = new List<PolaroidSticker>
            {
                S("a", "zhe", "阿哲", "pet:happy", 196, 30, 8),
                S("b", "me", "我", "heart", 40, 120, -8),
            };
            Capture(Path.Combine(dir, "polaroid_open.png"), meta, photo, style1, stickers1, comments, "me", 0, 0, null);

            // ② 我的照片、裝飾頁・畫框（粉紅），選取自己的貼紙
            var mine = new PhotoMeta("mine.jpg") { takerUserId = "me", takerName = "我", category = "美食", timestamp = meta.timestamp };
            var style2 = new PolaroidStyle { frame = "pink", filter = "", caption = "第一次自己煮" };
            var stickers2 = new List<PolaroidSticker>
            {
                S("c", "me", "我", "pet:tilt", 196, 30, 8),
                S("d", "amy", "小美", "star", 40, 120, -8),
            };
            Capture(Path.Combine(dir, "polaroid_frame.png"), mine, photo, style2, stickers2, comments, "me", 1, 1, "c");

            // ③ 我的照片（拍的時候開了魚眼）、裝飾頁・濾鏡（Y2K CCD，右下日期戳）：魚眼和顏色濾鏡疊在一起
            var mineFish = new PhotoMeta("mine_fish.jpg") { takerUserId = "me", takerName = "我", category = "美食", timestamp = meta.timestamp, fisheye = true };
            var style3 = new PolaroidStyle { frame = "gingham", filter = "ccd", caption = "" };
            Capture(Path.Combine(dir, "polaroid_filter.png"), mineFish, photo, style3, new List<PolaroidSticker>(), new List<ChatEntry>(), "me", 1, 2, null);

            // ④ 萊卡（暗角）＋裝飾頁・貼紙，別人的照片、沒想吃 → 歪頭（寵物頁看不到，只看卡片）
            var other = new PhotoMeta("other.jpg") { takerUserId = "amy", takerName = "小美", category = "自然", timestamp = meta.timestamp };
            var style4 = new PolaroidStyle { frame = "black", filter = "leica", caption = "週末的山" };
            Capture(Path.Combine(dir, "polaroid_stickers.png"), other, photo, style4, stickers1, comments, "me", 1, 0, null);

            // 下載的圖（整張拍立得，不含留言）
            var sq = PolaroidPhoto.Square(photo, PolaroidPhoto.PhotoPx);
            var filtered = PolaroidPhoto.Filtered(sq, "");
            var skin = AssetDatabase.LoadAssetAtPath<PolaroidSkin>("Assets/Resources/UI/PolaroidSkin.asset");
            var style5 = new PolaroidStyle { frame = "white", filter = "ccd", caption = "今天的拉麵" };
            Object.DestroyImmediate(filtered);
            filtered = PolaroidPhoto.Filtered(sq, style5.filter);
            var jpg = PolaroidExport.Render(filtered, style5, stickers1, skin, skin != null ? skin.font : null, System.DateTime.Now, true);
            File.WriteAllBytes(Path.Combine(dir, "polaroid_export.jpg"), jpg);
            Debug.Log("[Polaroid] 下載圖：" + Path.Combine(dir, "polaroid_export.jpg"));
            Object.DestroyImmediate(sq);
            Object.DestroyImmediate(filtered);
            Object.DestroyImmediate(photo);
            return dir;
        }

        static void Capture(string outPath, PhotoMeta meta, Texture2D photo, PolaroidStyle style, List<PolaroidSticker> stickers,
                            List<ChatEntry> comments, string me, int page, int tab, string selectId)
        {
            int w = 390 * Scale, h = 772 * Scale;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("PolaroidSnapshotCam");
            camGo.transform.position = new Vector3(100000f, 100000f, -10f); // 遠離場景，只畫截圖用的圖層
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            cam.cullingMask = 1 << Layer;
            cam.targetTexture = rt;
            var canvasGo = new GameObject("PolaroidSnapshotCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            try
            {
                Canvas.ForceUpdateCanvases();
                PolaroidScreen.BuildForCapture((RectTransform)canvasGo.transform, meta, photo, style, stickers, comments, me, page, tab, selectId, Scale,
                    4f, new[] { 30f, 16f, 10f, 7f, 5f }, "美食");
                SetLayer(canvasGo.transform, Layer);
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
                Debug.Log("[Polaroid] 截圖：" + outPath);
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

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        static PolaroidSticker S(string id, string by, string name, string kind, float x, float y, float rot) =>
            new PolaroidSticker { id = id, by = by, byName = name, kind = kind, x = x, y = y, rot = rot };

        static ChatEntry Comment(string name, string uid, string text) =>
            new ChatEntry { key = uid + text, msg = new ChatMessage(name, text) { senderId = uid } };

        /// <summary>測試照片轉成可讀的貼圖（不改匯入設定）。</summary>
        static Texture2D LoadReadable(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(Path.Combine(Application.dataPath, "..", path)));
            return tex;
        }
    }
}
