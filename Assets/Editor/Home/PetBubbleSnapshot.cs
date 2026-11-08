using System.IO;
using BlockPet.Feed;
using BlockPet.Hand;
using BlockPet.UI.Travel;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.EditorTools.Home
{
    /// <summary>
    /// 餵完的寵物泡泡截圖（設計文件 5.25 ③，不用按 Play）：營地背景＋寵物＋泡泡，用 HandController 真的泡泡程式畫，
    /// 輸出命中、沒命中、辨識失敗三張 390 × 772（@3x）PNG 到 Logs/。
    /// 選單 BlockPet/Debug/寵物泡泡截圖，或 batchmode：
    /// Unity -batchmode -projectPath . -executeMethod BlockPet.EditorTools.Home.PetBubbleSnapshot.CaptureBatch -quit
    /// </summary>
    public static class PetBubbleSnapshot
    {
        const int Scale = 3;
        const float DesignW = 390f, DesignH = 772f;

        [MenuItem("BlockPet/Debug/寵物泡泡截圖")]
        static void CaptureMenu()
        {
            string dir = CaptureAll();
            EditorUtility.RevealInFinder(dir);
        }

        public static void CaptureBatch() => CaptureAll();

        static string CaptureAll()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs"));
            Capture(Path.Combine(dir, "pet_bubble_hit.png"), new FeedResult { category = "美食", want = "美食", hit = true, coins = 7 });
            Capture(Path.Combine(dir, "pet_bubble_miss.png"), new FeedResult { category = "寵物", want = "美食", hit = false, coins = 1 });
            Capture(Path.Combine(dir, "pet_bubble_unknown.png"), new FeedResult { category = null, want = "美食", hit = false, coins = 1 });
            return dir;
        }

        static void Capture(string outPath, FeedResult result)
        {
            int w = (int)DesignW * Scale, h = (int)DesignH * Scale;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); // 不開 MSAA：URP 的 batchmode 會找不到 resolve 目標
            var camGo = new GameObject("BubbleSnapshotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0x8F / 255f, 0xA8 / 255f, 0x68 / 255f);
            cam.orthographic = true;
            cam.targetTexture = rt;

            var canvasGo = new GameObject("BubbleSnapshotCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var hostGo = new GameObject("BubbleSnapshotHand");
            try
            {
                Canvas.ForceUpdateCanvases();
                var design = new GameObject("Design", typeof(RectTransform)).GetComponent<RectTransform>();
                design.SetParent(canvasGo.transform, false);
                design.anchorMin = design.anchorMax = new Vector2(0.5f, 0f);
                design.pivot = new Vector2(0.5f, 0f);
                design.sizeDelta = new Vector2(DesignW, DesignH);
                design.anchoredPosition = Vector2.zero;
                design.localScale = Vector3.one * Scale;

                // 營地背景與寵物（畫面上的寵物在遊戲裡是場景物件，這裡用旅行素材的同一張圖代替）
                var travel = TravelSkin.Load();
                if (travel != null && travel.campBackground != null)
                {
                    var bg = Stretch(design, "Camp").gameObject.AddComponent<RawImage>();
                    bg.texture = travel.campBackground;
                }
                var pet = HandController.CapturePetRect;
                if (travel != null && travel.pet != null)
                {
                    var p = new GameObject("Pet", typeof(RectTransform)).GetComponent<RectTransform>();
                    p.SetParent(design, false);
                    p.anchorMin = p.anchorMax = new Vector2(0.5f, 0f);
                    p.pivot = Vector2.zero;
                    p.anchoredPosition = new Vector2(pet.xMin, pet.yMin);
                    p.sizeDelta = new Vector2(pet.width, pet.height);
                    var img = p.gameObject.AddComponent<Image>();
                    img.sprite = travel.pet;
                    img.preserveAspect = true;
                }

                var skin = AssetDatabase.LoadAssetAtPath<HandSkin>("Assets/Data/HandSkin.asset");
                var hand = hostGo.AddComponent<HandController>();
                hand.CaptureReaction(design, skin, result);

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
                Debug.Log("[PetBubble] 截圖：" + outPath);
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(hostGo);
                Object.DestroyImmediate(canvasGo);
                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static RectTransform Stretch(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }
    }
}
