using System.Collections.Generic;
using BlockPet.Hand;
using BlockPet.Polaroid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Polaroid
{
    /// <summary>設計座標的 UI 小工具（和其他新版頁面相同寫法：左上為原點、y 向下）。</summary>
    public static class PUI
    {
        public static RectTransform Place(Transform parent, string name, float left, float top, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(left, -top);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>以中心為軸放：left/top 是左上角（設計座標），旋轉、縮放以中心。</summary>
        public static RectTransform PlaceCentered(Transform parent, string name, float left, float top, float w, float h)
        {
            var rt = Place(parent, name, left, top, w, h);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(left + w / 2f, -(top + h / 2f));
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static TMP_Text Label(TMP_FontAsset font, RectTransform parent, float left, float top, float w, float h,
                                     string value, float size, Color color, TextAlignmentOptions align)
        {
            var t = Place(parent, "Text", left, top, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            t.richText = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        /// <summary>有圖用圖（sliced＝9-slice），沒有就用程式畫的圓角。</summary>
        public static Image Fill(RectTransform rt, Sprite sprite, bool sliced, Color fallback, float radius)
        {
            if (sprite == null) return UIShapes.AddRounded(rt.gameObject, fallback, radius);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            return img;
        }

        public static Image Icon(RectTransform parent, Sprite sprite, float left, float top, float size, Color color)
        {
            var img = Place(parent, "Icon", left, top, size, size).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.enabled = sprite != null;
            return img;
        }

        public static Button MakeButton(GameObject go, Graphic target, UnityEngine.Events.UnityAction onClick)
        {
            var b = go.AddComponent<Button>();
            b.targetGraphic = target;
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(onClick);
            return b;
        }

        public static Image Hit(RectTransform rt)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0);
            return img;
        }

        /// <summary>Play 中用 Destroy；Editor 截圖（沒按 Play）要用 DestroyImmediate。</summary>
        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        static readonly System.Collections.Generic.Dictionary<Sprite, Sprite> _white = new System.Collections.Generic.Dictionary<Sprite, Sprite>();

        /// <summary>
        /// 線條圖示的白色版本（素材是深色 #3A2E22，Unity 的上色只能變暗）：顏色換成白、保留透明度，之後就能上任何顏色。
        /// 圖要可讀（匯入設定見 TopBarSpriteImporter）；不可讀就回傳原圖。
        /// </summary>
        public static Sprite White(Sprite s)
        {
            if (s == null) return null;
            if (_white.TryGetValue(s, out var w) && w != null) return w;
            var src = s.texture;
            if (src == null || !src.isReadable) return s;
            var r = s.textureRect;
            var px = src.GetPixels32();
            int tw = src.width;
            var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var outPx = new Color32[tex.width * tex.height];
            for (int y = 0; y < tex.height; y++)
                for (int x = 0; x < tex.width; x++)
                {
                    var c = px[((int)r.y + y) * tw + (int)r.x + x];
                    outPx[y * tex.width + x] = new Color32(255, 255, 255, c.a);
                }
            tex.SetPixels32(outPx);
            tex.Apply();
            w = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), s.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            _white[s] = w;
            return w;
        }

        public static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    }

    /// <summary>
    /// 貼紙的樣子（拍立得上、貼紙列、下載的圖共用）：寵物貼紙＝寵物圖（翻面／轉／變色）＋右上小符號；手畫貼紙＝一張圖。
    /// </summary>
    public static class PolaroidStickerArt
    {
        // 彩虹型、極致型：濾色同圖鑑（DexScreen.FormTint）
        static readonly Color RainbowTint = new Color(1f, 0.9f, 0.95f);
        static readonly Color ExtremeTint = new Color(1f, 0.82f, 0.62f);

        /// <summary>建一張 size × size 的貼紙；pet 的話回傳的 body 是要輕晃的那層（手畫貼紙是 null）。</summary>
        public static RectTransform Create(RectTransform parent, string kind, float size, PolaroidSkin skin, out RectTransform body)
        {
            var root = PUI.Place(parent, "Sticker_" + kind, 0, 0, size, size);
            body = null;
            if (PolaroidRules.IsPetSticker(kind))
            {
                body = PUI.Stretch(root, "Body");
                body.pivot = new Vector2(0.5f, 0.5f);
                var petRt = PUI.Stretch(body, "Pet");
                petRt.pivot = new Vector2(0.5f, 0.5f);
                var img = petRt.gameObject.AddComponent<Image>();
                img.sprite = skin != null ? skin.pet : null;
                img.preserveAspect = true;
                img.raycastTarget = false;
                if (img.sprite == null) { img.enabled = false; UIShapes.AddRounded(PUI.Stretch(petRt, "Fallback").gameObject, PUI.Hex(0xF6C945), size / 2f).raycastTarget = false; }
                Sprite ov = null;
                switch (kind)
                {
                    case "pet:happy": ov = skin?.petOvHeart; break;
                    case "pet:tilt":
                        petRt.localScale = new Vector3(-1f, 1f, 1f);
                        petRt.localRotation = Quaternion.Euler(0f, 0f, 14f); // CSS rotate(-14deg)
                        ov = skin?.petOvNote;
                        break;
                    case "pet:sleep":
                        img.color = new Color(0.92f, 0.92f, 0.92f, 1f); // brightness(.92)
                        ov = skin?.petOvZz;
                        break;
                    case "pet:drool":
                        petRt.localScale = new Vector3(-1f, 1f, 1f);
                        ov = skin?.petOvDrop;
                        break;
                    case "pet:rainbow": img.color = RainbowTint; break;
                    case "pet:extreme": img.color = ExtremeTint; break;
                }
                if (ov != null)
                {
                    // 寵物圖的 40%，放在右 −6、上 −8
                    float os = size * 0.4f;
                    PUI.Icon(body, ov, size - os + 6f, -8f, os, Color.white);
                }
            }
            else
            {
                var sp = skin != null ? skin.DrawSticker(kind) : null;
                var img = PUI.Stretch(root, "Art").gameObject.AddComponent<Image>();
                img.sprite = sp;
                img.preserveAspect = true;
                img.raycastTarget = false;
                if (sp == null) img.color = PUI.Hex(0x3A2E22, 0.6f);
            }
            return root;
        }
    }

    /// <summary>
    /// 一張拍立得卡片（354 × 431；5.26 ②）：畫框底（純色或平鋪花樣）、照片 318 × 318、標題、貼紙層。
    /// 畫面上和下載的圖都用這個建，所以兩邊長得一樣。
    /// </summary>
    public class PolaroidCard
    {
        public RectTransform root, photoArea, commentLayer, stickerLayer;
        public RawImage photo;
        public TMP_Text caption, dateStamp;
        public Image pencil;
        Image _bg, _tile;
        readonly PolaroidSkin _skin;
        readonly TMP_FontAsset _font;

        public PolaroidCard(RectTransform parent, PolaroidSkin skin, TMP_FontAsset font, bool withShadow)
        {
            _skin = skin;
            _font = font;
            root = PUI.Place(parent, "Card", 0, 0, PolaroidRules.CardW, PolaroidRules.CardH);
            if (withShadow) UIShapes.AddShadow(root, new Color(60f / 255f, 45f / 255f, 25f / 255f, 0.22f), 4, 26, 12);
            _bg = PUI.Stretch(root, "Bg").gameObject.AddComponent<Image>();
            _bg.raycastTarget = false;
            _tile = PUI.Stretch(root, "Pattern").gameObject.AddComponent<Image>();
            _tile.type = Image.Type.Tiled;
            _tile.raycastTarget = false;
            _tile.enabled = false;

            // 照片：正方形、不圓角、裁切填滿
            photoArea = PUI.Place(root, "PhotoArea", PolaroidRules.PhotoX, PolaroidRules.PhotoY, PolaroidRules.PhotoSize, PolaroidRules.PhotoSize);
            var under = photoArea.gameObject.AddComponent<Image>();
            under.color = PUI.Hex(0xE9E2D6);
            under.raycastTarget = false;
            photo = PUI.Stretch(photoArea, "Photo").gameObject.AddComponent<RawImage>();
            photo.raycastTarget = false;
            photo.enabled = false;
            commentLayer = PUI.Stretch(photoArea, "Comments");

            // Y2K（CCD）濾鏡的橘色日期戳：照片右 12、下 10，15 粗體 #FF9A3C（下載的圖也有）
            dateStamp = PUI.Label(font, photoArea, 0, 0, PolaroidRules.PhotoSize - 12f, 24, "", 15, PUI.Hex(0xFF9A3C), TextAlignmentOptions.BottomRight);
            dateStamp.overflowMode = TextOverflowModes.Overflow;
            dateStamp.rectTransform.anchorMin = dateStamp.rectTransform.anchorMax = dateStamp.rectTransform.pivot = new Vector2(1f, 0f);
            dateStamp.rectTransform.anchoredPosition = new Vector2(-12f, 10f);
            dateStamp.characterSpacing = 4f;
            dateStamp.gameObject.SetActive(false);

            // 標題：(22, 352) 310 × 70，置中、轉 −2°（CSS），字暫用 Noto Sans Black（霞鶩文楷待決 35）
            var cap = PUI.PlaceCentered(root, "CaptionBox", 22, 352, 310, 70);
            cap.localRotation = Quaternion.Euler(0f, 0f, 2f);
            caption = PUI.Label(font, cap, 0, 0, 310, 70, "", 24, Color.black, TextAlignmentOptions.Center);
            pencil = PUI.Icon(cap, PUI.White(skin != null ? skin.iconPencil : null), 0, 27, 16, Color.white);
            pencil.enabled = false;

            stickerLayer = PUI.Stretch(root, "Stickers");
        }

        /// <summary>套畫框、標題、日期戳；photoTime＝照片拍的時間（Y2K 日期戳用）。</summary>
        public void ApplyStyle(PolaroidStyle style, bool showPencil, System.DateTime photoTime)
        {
            var filter = PolaroidRules.Filter(style?.filter);
            dateStamp.gameObject.SetActive(filter.dateStamp);
            if (filter.dateStamp) dateStamp.text = PolaroidRules.DateStamp(photoTime == default ? System.DateTime.Now : photoTime);

            var f = PolaroidRules.Frame(style?.frame);
            _bg.color = f.bg;
            var tile = _skin != null && f.tile != null ? _skin.FrameTile(f.tile) : null;
            _tile.sprite = tile;
            _tile.enabled = tile != null;
            caption.color = f.ink;
            caption.text = style?.caption ?? "";

            // 小鉛筆：標題右邊（沒寫標題時放在中間），字色 55%
            bool pen = showPencil && pencil.sprite != null;
            pencil.enabled = pen;
            if (pen)
            {
                pencil.color = new Color(f.ink.r, f.ink.g, f.ink.b, 0.55f);
                float tw = string.IsNullOrEmpty(caption.text) ? 0f : Mathf.Min(290f, caption.GetPreferredValues(caption.text).x);
                pencil.rectTransform.anchoredPosition = new Vector2(155f + tw / 2f + (tw > 0f ? 6f : -8f), -27f);
            }
        }

        /// <summary>照片（已裁成正方形、套好顏色濾鏡）；fisheye＝拍的時候開了魚眼，正方形裡再套魚眼（5.27 ③）。</summary>
        public void SetPhoto(Texture tex, bool fisheye)
        {
            photo.texture = tex;
            photo.enabled = tex != null;
            FisheyeImage.Set(photo, fisheye && tex != null);
        }

        /// <summary>貼紙放到卡片上（x、y＝左上，卡片座標；rot＝CSS 角度）。</summary>
        public RectTransform AddSticker(PolaroidSticker s, out RectTransform body)
        {
            float size = PolaroidRules.StickerSize(s.kind);
            var rt = PolaroidStickerArt.Create(stickerLayer, s.kind, size, _skin, out body);
            PlaceSticker(rt, s.x, s.y, s.rot, size);
            return rt;
        }

        public static void PlaceSticker(RectTransform rt, float x, float y, float rot, float size)
        {
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x + size / 2f, -(y + size / 2f));
            rt.localRotation = Quaternion.Euler(0f, 0f, -rot);
        }

        public void ClearStickers()
        {
            for (int i = stickerLayer.childCount - 1; i >= 0; i--) PUI.Kill(stickerLayer.GetChild(i).gameObject);
        }

        public void RebuildStickers(IEnumerable<PolaroidSticker> stickers)
        {
            ClearStickers();
            if (stickers == null) return;
            foreach (var s in stickers) AddSticker(s, out _);
        }
    }
}
