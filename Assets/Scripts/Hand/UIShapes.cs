using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.Hand
{
    /// <summary>
    /// 執行時產生的圓角、外框、柔和陰影 9-slice sprite，讓程式建立的 UI 不需要美術素材也有圓角。
    /// 圓角大小用 Image.pixelsPerUnitMultiplier 控制（單位：所在座標系的 px）。
    /// </summary>
    public static class UIShapes
    {
        const int Radius = 32;        // 圓角 sprite 的半徑（貼圖像素）
        const int RingRadius = 21;    // 外框 sprite 的半徑
        const int RingThickness = 6;  // 外框粗細（貼圖像素）
        const int ThinRingRadius = 32;    // 細外框 sprite（快門圈圈用）
        const int ThinRingThickness = 4;
        const int ShadowRadius = 16;
        const int ShadowBlur = 16;

        static Sprite _rounded, _ring, _thinRing, _shadow;

        static Sprite Rounded => _rounded != null ? _rounded : (_rounded = Make(Radius, 0, 0));
        static Sprite Ring => _ring != null ? _ring : (_ring = Make(RingRadius, RingThickness, 0));
        static Sprite ThinRing => _thinRing != null ? _thinRing : (_thinRing = Make(ThinRingRadius, ThinRingThickness, 0));
        static Sprite Shadow => _shadow != null ? _shadow : (_shadow = Make(ShadowRadius, 0, ShadowBlur));

        /// <summary>圓角實心矩形；radius 大於邊長一半時會自動變成膠囊／圓形。</summary>
        public static Image AddRounded(GameObject go, Color color, float radius)
        {
            var img = go.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = (Radius + 1f) / Mathf.Max(0.5f, radius);
            img.color = color;
            return img;
        }

        /// <summary>圓角外框（中空），粗細約為 radius 的 2/7。</summary>
        public static Image AddRing(GameObject go, Color color, float radius)
        {
            var img = go.AddComponent<Image>();
            img.sprite = Ring;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = (RingRadius + 1f) / Mathf.Max(0.5f, radius);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>細的圓角外框（中空），粗細約為 radius 的 1/8。</summary>
        public static Image AddThinRing(GameObject go, Color color, float radius)
        {
            var img = go.AddComponent<Image>();
            img.sprite = ThinRing;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = (ThinRingRadius + 1f) / Mathf.Max(0.5f, radius);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// 柔和陰影：在 parent 底下建立一個撐滿、往外擴 blur、往下偏移 offsetY 的陰影。
        /// 子物件會畫在父物件的圖片上面，所以呼叫端要讓陰影是第一個子物件、底圖放在陰影之後。
        /// </summary>
        public static Image AddShadow(RectTransform parent, Color color, float radius, float blur, float offsetY)
        {
            var go = new GameObject("Shadow", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-blur, -blur - offsetY);
            rt.offsetMax = new Vector2(blur, blur - offsetY);
            var img = go.AddComponent<Image>();
            img.sprite = Shadow;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = (ShadowRadius + ShadowBlur + 1f) / Mathf.Max(0.5f, radius + blur);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static Sprite Make(int radius, int ring, int blur)
        {
            int edge = radius + blur;
            int size = edge * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[size * size];
            float c = size / 2f;
            const float core = 1f; // 中間平直區的半寬；圖形半寬 = core + radius，外圍再留 blur
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 圓角矩形的 signed distance（內部為負）
                    float qx = Mathf.Abs(x + 0.5f - c) - core;
                    float qy = Mathf.Abs(y + 0.5f - c) - core;
                    float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
                    float d = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;

                    float a;
                    if (blur > 0) a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-blur * 0.5f, blur, d));
                    else if (ring > 0) a = Mathf.Clamp01(0.5f - (Mathf.Abs(d + ring / 2f) - ring / 2f));
                    else a = Mathf.Clamp01(0.5f - d);

                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);

            float border = edge + 1;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
