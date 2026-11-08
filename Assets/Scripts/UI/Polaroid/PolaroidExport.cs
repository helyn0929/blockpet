using System.Collections.Generic;
using BlockPet.Polaroid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Polaroid
{
    /// <summary>
    /// 下載／分享的圖（5.26 ⑥）：整張拍立得 @3x 1062 × 1293——照片（含濾鏡）、畫框、標題、所有人的貼紙（超出卡片的切掉，寵物貼紙靜止）；
    /// 不含留言、誰拍的、類別標籤。在畫面外用一台暫時的相機把卡片畫進 RenderTexture，再轉成 JPG。
    /// </summary>
    public static class PolaroidExport
    {
        const int Scale = 3;
        const int Layer = 31;          // 只畫這一層，不會畫到營地或其他畫面
        const int JpgQuality = 92;

        public static byte[] Render(Texture photo, PolaroidStyle style, IEnumerable<PolaroidSticker> stickers, PolaroidSkin skin, TMP_FontAsset font, System.DateTime photoTime, bool fisheye = false)
        {
            int w = Mathf.RoundToInt(PolaroidRules.CardW * Scale), h = Mathf.RoundToInt(PolaroidRules.CardH * Scale);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("PolaroidExportCam");
            camGo.transform.position = new Vector3(100000f, 100000f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.orthographic = true;
            cam.cullingMask = 1 << Layer;
            cam.targetTexture = rt;

            var canvasGo = new GameObject("PolaroidExportCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            try
            {
                Canvas.ForceUpdateCanvases();
                var holder = PUI.Stretch(canvasGo.transform, "Holder");
                var card = new PolaroidCard(holder, skin, font, false);
                card.root.anchorMin = card.root.anchorMax = new Vector2(0.5f, 0.5f);
                card.root.pivot = new Vector2(0.5f, 0.5f);
                card.root.anchoredPosition = Vector2.zero;
                card.root.localScale = Vector3.one * Scale;
                card.ApplyStyle(style, false, photoTime);
                card.SetPhoto(photo, fisheye); // 魚眼一起套進下載的圖
                card.RebuildStickers(stickers);
                SetLayer(canvasGo.transform, Layer);

                Canvas.ForceUpdateCanvases();
                foreach (var t in canvasGo.GetComponentsInChildren<TMP_Text>(true)) t.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                var jpg = tex.EncodeToJPG(JpgQuality);
                Kill(tex);
                return jpg;
            }
            finally
            {
                cam.targetTexture = null;
                Kill(canvasGo);
                Kill(camGo);
                rt.Release();
                Kill(rt);
            }
        }

        /// <summary>Play 中用 Destroy；Editor 截圖（沒按 Play）要用 DestroyImmediate。</summary>
        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }
    }
}
