using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Home
{
    /// <summary>
    /// 首頁 v9 的 Home 鍵和選單鈕（設計文件 5.25 ②）：拿掉毛玻璃膠囊底，改成沒有底的線條圖示＋一圈淡白光。
    /// - Home：點擊範圍 44×44，(8, 76)；小房子 28。
    /// - 選單：點擊範圍 44×44，右 10、y 76；向下箭頭 26，展開轉 180°（MenuToggle 照舊轉）。展開的四顆維持 5.7 樣式。
    /// 白光（drop-shadow 0 0 1.5 rgba(255,252,244,0.95)＋0 0 4 rgba(255,252,244,0.75)）是執行時用圖示的形狀模糊算出來的白色圖，
    /// 墊在圖示後面（圖示素材要可讀，匯入設定在 TopBarSpriteImporter）。
    /// 場景裡的按鈕是 Build TopBar 建的，這裡在執行時換掉樣式，不用重建場景。
    /// </summary>
    public static class Home9TopIcons
    {
        const float Scale = 3f;   // 素材 @3x：1 pt = 3 px
        const int Pad = 16;       // 光暈往外留的 px

        static readonly Dictionary<Sprite, Sprite> _glows = new Dictionary<Sprite, Sprite>();

        public static void Apply(RectTransform home, RectTransform menu)
        {
            var skin = Home9Skin.Load();
            if (skin == null) return;

            if (home != null && skin.iconHome != null)
            {
                home.sizeDelta = new Vector2(44f, 44f);
                home.anchoredPosition = new Vector2(8f, -76f);
                HideBg(home);
                Icon(home.Find("Icon") as RectTransform, skin.iconHome, 28f);
            }

            if (menu != null && skin.iconMenuChevron != null)
            {
                menu.sizeDelta = new Vector2(44f, 44f);
                menu.anchoredPosition = new Vector2(-10f, -76f);
                HideBg(menu);
                Icon(menu.Find("Chevron") as RectTransform, skin.iconMenuChevron, 26f);
            }
        }

        static void HideBg(RectTransform button)
        {
            var bg = button.Find("Bg");
            if (bg != null) bg.gameObject.SetActive(false);
        }

        static void Icon(RectTransform rt, Sprite sprite, float size)
        {
            if (rt == null) return;
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.GetComponent<Image>();
            if (img == null) img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.preserveAspect = false;
            img.color = Color.white;
            img.raycastTarget = false;

            // 光暈：放在圖示底下（圖示的子物件會畫在圖示上面，所以用 Mask 以外的方式：放成兄弟、排在前面）
            if (rt.parent == null || rt.parent.Find(rt.name + "Glow") != null) return;
            var glow = Glow(sprite);
            if (glow == null) return;
            var g = new GameObject(rt.name + "Glow", typeof(RectTransform)).GetComponent<RectTransform>();
            g.SetParent(rt.parent, false);
            g.SetSiblingIndex(rt.GetSiblingIndex());
            g.anchorMin = rt.anchorMin;
            g.anchorMax = rt.anchorMax;
            g.pivot = rt.pivot;
            g.anchoredPosition = rt.anchoredPosition;
            float k = size / sprite.rect.width;
            g.sizeDelta = new Vector2(glow.rect.width * k, glow.rect.height * k);
            var gi = g.gameObject.AddComponent<Image>();
            gi.sprite = glow;
            gi.raycastTarget = false;
            // 箭頭會轉，光暈跟著一起轉
            if (rt.name == "Chevron") g.gameObject.AddComponent<FollowRotation>().target = rt;
        }

        /// <summary>依圖示形狀做白色光暈：近的一層模糊 1.5pt×0.95，遠的一層 4pt×0.75（聊天室相簿鈕也用）。圖要可讀，不然回傳 null。</summary>
        public static Sprite Glow(Sprite src)
        {
            if (_glows.TryGetValue(src, out var cached)) return cached;
            var tex = src.texture;
            if (tex == null || !tex.isReadable) { _glows[src] = null; return null; }

            var r = src.rect;
            int w = (int)r.width + Pad * 2, h = (int)r.height + Pad * 2;
            var px = tex.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height);
            var a = new float[w * h];
            for (int y = 0; y < (int)r.height; y++)
                for (int x = 0; x < (int)r.width; x++)
                    a[(y + Pad) * w + x + Pad] = px[y * (int)r.width + x].a;

            var near = Blur(a, w, h, Mathf.RoundToInt(1.5f * Scale));
            var far = Blur(a, w, h, Mathf.RoundToInt(4f * Scale));
            var outPx = new Color32[w * h];
            for (int i = 0; i < a.Length; i++)
            {
                // drop-shadow 的強度大約是模糊後 alpha 的兩倍（線條很細），夾到 1
                float g1 = Mathf.Clamp01(near[i] * 2.2f) * 0.95f;
                float g2 = Mathf.Clamp01(far[i] * 2.2f) * 0.75f;
                float alpha = 1f - (1f - g1) * (1f - g2);
                outPx[i] = new Color32(255, 252, 244, (byte)Mathf.RoundToInt(alpha * 255f));
            }
            var glowTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            glowTex.SetPixels32(outPx);
            glowTex.Apply(false, true);
            var s = Sprite.Create(glowTex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), src.pixelsPerUnit);
            _glows[src] = s;
            return s;
        }

        /// <summary>三次方框模糊（近似高斯，radius 約等於 CSS 的 blur 半徑）。</summary>
        static float[] Blur(float[] src, int w, int h, int radius)
        {
            int box = Mathf.Max(1, Mathf.RoundToInt(radius * 0.6f));
            var a = (float[])src.Clone();
            var tmp = new float[a.Length];
            for (int pass = 0; pass < 3; pass++)
            {
                BoxH(a, tmp, w, h, box);
                BoxV(tmp, a, w, h, box);
            }
            return a;
        }

        static void BoxH(float[] s, float[] d, int w, int h, int r)
        {
            float n = r * 2 + 1;
            for (int y = 0; y < h; y++)
            {
                float sum = 0f;
                for (int x = -r; x <= r; x++) sum += s[y * w + Mathf.Clamp(x, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    d[y * w + x] = sum / n;
                    sum += s[y * w + Mathf.Min(x + r + 1, w - 1)] - s[y * w + Mathf.Max(x - r, 0)];
                }
            }
        }

        static void BoxV(float[] s, float[] d, int w, int h, int r)
        {
            float n = r * 2 + 1;
            for (int x = 0; x < w; x++)
            {
                float sum = 0f;
                for (int y = -r; y <= r; y++) sum += s[Mathf.Clamp(y, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    d[y * w + x] = sum / n;
                    sum += s[Mathf.Min(y + r + 1, h - 1) * w + x] - s[Mathf.Max(y - r, 0) * w + x];
                }
            }
        }

        /// <summary>光暈跟著箭頭轉。</summary>
        class FollowRotation : MonoBehaviour
        {
            public Transform target;
            void LateUpdate() { if (target != null) transform.localRotation = target.localRotation; }
        }
    }
}
