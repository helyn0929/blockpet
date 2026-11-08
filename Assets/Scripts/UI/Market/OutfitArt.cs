using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.Outfit;
using BlockPet.UI.Room;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Market
{
    /// <summary>
    /// 穿搭的佔位圖（設計文件 5.11 ⑧：正式美術還沒出）：用圓角形狀和三角形在執行時畫，商品卡、試穿預覽、營地寵物共用。
    /// 之後有正式圖時，換掉 Draw 裡面每一件的畫法就好（或改成讀 Sprite）。
    /// </summary>
    public static class OutfitArt
    {
        static readonly Dictionary<string, OutfitItem> _items = new Dictionary<string, OutfitItem>();
        static List<OutfitItem> _list;

        /// <summary>設定檔 Resources/Shop/outfits.json 的全部品項（讀一次）。</summary>
        public static List<OutfitItem> Items
        {
            get
            {
                if (_list != null) return _list;
                var file = Resources.Load<TextAsset>("Shop/outfits");
                _list = OutfitRules.Parse(file != null ? file.text : null);
                if (file == null) Debug.LogWarning("[Outfit] 找不到 Resources/Shop/outfits.json");
                foreach (var i in _list) _items[i.id] = i;
                return _list;
            }
        }

        public static OutfitItem Get(string id)
        {
            var _ = Items;
            return id != null && _items.TryGetValue(id, out var i) ? i : null;
        }

        // 每個部位在寵物圖上的位置（寵物圖框的比例：中心 x、上緣 y、寬度；高度依每件的比例）
        struct Anchor { public float cx, top, w; }
        static Anchor AnchorOf(string slot)
        {
            switch (slot)
            {
                // BowBow 的臉朝右：頭、眼睛在圖的右半邊（寵物翻面時 Dress 會左右對調）
                case OutfitRules.Head: return new Anchor { cx = 0.6f, top = -0.12f, w = 0.5f };
                case OutfitRules.Face: return new Anchor { cx = 0.67f, top = 0.27f, w = 0.4f };
                case OutfitRules.Neck: return new Anchor { cx = 0.6f, top = 0.56f, w = 0.44f };
                case OutfitRules.Side: return new Anchor { cx = 0.84f, top = 0.02f, w = 0.22f };
            }
            return new Anchor { cx = 0.5f, top = 0f, w = 0.4f };
        }

        /// <summary>每件的高寬比。</summary>
        static float Ratio(string id)
        {
            switch (id)
            {
                case "outfit_glasses": return 0.4f;
                case "outfit_scarf": return 0.6f;
                case "outfit_bow": return 0.7f;
                case "outfit_party":
                case "outfit_santa": return 0.95f;
                default: return 0.6f;
            }
        }

        /// <summary>單獨一件，放在 box（w × h）中間（商品卡、購買列）。</summary>
        public static RectTransform Thumbnail(RectTransform box, string id, float maxW, float maxH)
        {
            float r = Ratio(id);
            float w = Mathf.Min(maxW, maxH / r), h = w * r;
            var rt = Place(box, "Outfit_" + id, (box.rect.width - w) / 2f, (box.rect.height - h) / 2f, w, h);
            Draw(rt, id, w, h);
            return rt;
        }

        /// <summary>
        /// 把 worn（部位 → 穿搭 id）穿在寵物身上：layer 是一個和寵物圖同大小（w × h）、左上為原點的框。
        /// flip＝寵物左右翻面時，位置也跟著翻。會先清掉 layer 裡原本的穿搭。
        /// </summary>
        public static void Dress(RectTransform layer, IReadOnlyDictionary<string, string> worn, float w, float h, bool flip)
        {
            for (int i = layer.childCount - 1; i >= 0; i--) Kill(layer.GetChild(i).gameObject);
            if (worn == null) return;
            // 由下往上畫：脖子、臉、側邊、頭（帽子蓋在最上面）
            foreach (var slot in new[] { OutfitRules.Neck, OutfitRules.Face, OutfitRules.Side, OutfitRules.Head })
            {
                if (!worn.TryGetValue(slot, out var id) || Get(id) == null) continue;
                var a = AnchorOf(slot);
                float pw = w * a.w, ph = pw * Ratio(id);
                float cx = flip ? 1f - a.cx : a.cx;
                var rt = Place(layer, "Wear_" + slot, w * cx - pw / 2f, h * a.top, pw, ph);
                if (flip) { rt.pivot = new Vector2(0.5f, 1f); rt.anchoredPosition = new Vector2(w * cx, -h * a.top); rt.localScale = new Vector3(-1f, 1f, 1f); }
                Draw(rt, id, pw, ph);
            }
        }

        // ─── 每一件的佔位畫法（座標是這件的框，左上為原點）─────────────────

        static void Draw(RectTransform rt, string id, float w, float h)
        {
            switch (id)
            {
                case "outfit_strawhat":
                    Box(rt, 0.22f * w, 0.05f * h, 0.56f * w, 0.7f * h, Hex(0xEFD38E), 0.25f * w);
                    Box(rt, 0.22f * w, 0.48f * h, 0.56f * w, 0.16f * h, Hex(0xD9534F), 0f);
                    Box(rt, 0f, 0.62f * h, w, 0.32f * h, Hex(0xE3BE6A), 0.16f * h);
                    break;
                case "outfit_beanie":
                    Box(rt, 0.1f * w, 0.18f * h, 0.8f * w, 0.82f * h, Hex(0x6FA45F), 0.4f * w);
                    Box(rt, 0.08f * w, 0.72f * h, 0.84f * w, 0.28f * h, Hex(0x4A7A3F), 0.12f * h);
                    Box(rt, 0.39f * w, 0f, 0.22f * w, 0.22f * w, Hex(0xFFFCF4), 0.11f * w);
                    break;
                case "outfit_raincap":
                    Box(rt, 0.15f * w, 0.08f * h, 0.7f * w, 0.8f * h, Hex(0xF6C945), 0.35f * w);
                    Box(rt, 0f, 0.68f * h, w, 0.3f * h, Hex(0xD9A92C), 0.15f * h);
                    break;
                case "outfit_party":
                    Tri(rt, w, h, Hex(0x6F8FE0), new Vector2(0.5f, 0.12f), new Vector2(0.84f, 1f), new Vector2(0.16f, 1f));
                    Box(rt, 0.3f * w, 0.6f * h, 0.4f * w, 0.1f * h, Hex(0xF6C945), 0f);
                    Box(rt, 0.4f * w, 0f, 0.2f * w, 0.2f * w, Hex(0xF6C945), 0.1f * w);
                    break;
                case "outfit_santa":
                    Tri(rt, w, h, Hex(0xD9534F), new Vector2(0.68f, 0.08f), new Vector2(0.92f, 0.82f), new Vector2(0.08f, 0.82f));
                    Box(rt, 0f, 0.74f * h, w, 0.26f * h, Hex(0xFFFCF4), 0.13f * h);
                    Box(rt, 0.58f * w, 0f, 0.22f * w, 0.22f * w, Hex(0xFFFCF4), 0.11f * w);
                    break;
                case "outfit_glasses":
                    Ring(rt, 0.02f * w, 0f, 0.42f * w, h, Hex(0x3A2E22));
                    Ring(rt, 0.56f * w, 0f, 0.42f * w, h, Hex(0x3A2E22));
                    Box(rt, 0.42f * w, 0.38f * h, 0.16f * w, 0.14f * h, Hex(0x3A2E22), 0f);
                    break;
                case "outfit_scarf":
                    Tri(rt, w, h, Hex(0xB44A33), new Vector2(0.52f, 0.3f), new Vector2(0.86f, 0.3f), new Vector2(0.62f, 1f));
                    Box(rt, 0f, 0f, w, 0.42f * h, Hex(0xD9534F), 0.21f * h);
                    break;
                case "outfit_bow":
                    Tri(rt, w, h, Hex(0xE88AA8), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 1f));
                    Tri(rt, w, h, Hex(0xE88AA8), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
                    Box(rt, 0.36f * w, 0.5f * h - 0.14f * w, 0.28f * w, 0.28f * w, Hex(0xC2577A), 0.14f * w);
                    break;
                default:
                    Box(rt, 0f, 0f, w, h, Hex(0xC9BFAF), 0.2f * Mathf.Min(w, h));
                    break;
            }
        }

        static void Box(RectTransform parent, float x, float y, float w, float h, Color c, float radius)
        {
            var img = radius > 0f
                ? UIShapes.AddRounded(Place(parent, "Box", x, y, w, h).gameObject, c, radius)
                : Place(parent, "Box", x, y, w, h).gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
        }

        static void Ring(RectTransform parent, float x, float y, float w, float h, Color c)
        {
            UIShapes.AddRing(Place(parent, "Ring", x, y, w, h).gameObject, c, Mathf.Min(w, h) / 2f).raycastTarget = false;
        }

        /// <summary>三角形；點是這件框的比例（0～1，左上為原點）。</summary>
        static void Tri(RectTransform parent, float w, float h, Color c, Vector2 a, Vector2 b, Vector2 d)
        {
            var rt = Place(parent, "Tri", 0, 0, w, h);
            rt.gameObject.AddComponent<CanvasRenderer>();
            var poly = rt.gameObject.AddComponent<UIPolygon>();
            poly.color = c;
            poly.raycastTarget = false;
            poly.SetPoints(new Vector2(a.x * w, a.y * h), new Vector2(b.x * w, b.y * h), new Vector2(d.x * w, d.y * h));
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

        static void Kill(GameObject go)
        {
            if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
        }

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        public static Color Parse(string hex, Color fallback) =>
            ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
    }
}
