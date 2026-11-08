using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BlockPet.Polaroid
{
    /// <summary>照片上的一張貼紙（Firebase：Rooms/{room}/polaroid/{照片鍵}/stickers/{id}）。x、y 是卡片座標（左上，354 × 431）。</summary>
    [Serializable]
    public class PolaroidSticker
    {
        public string id;
        public string by;       // 貼的人 uid
        public string byName;   // 貼的人暱稱（「小美貼的」）
        public string kind;     // pet:happy … 或 star、heart …（PolaroidRules.Tray）
        public float x, y, rot;
        public long at;
    }

    /// <summary>拍照的人設定的畫框、濾鏡、標題（Rooms/{room}/polaroid/{照片鍵}/style）。</summary>
    [Serializable]
    public class PolaroidStyle
    {
        public string frame = "white";
        public string filter = "";       // 空字串＝沒有濾鏡
        public string caption = "";
        public string by;
        public long at;
    }

    public struct PolaroidFrame
    {
        public string key, name;
        public Color bg, ink;
        public string tile;     // 平鋪花樣素材（格紋、星點、條紋），其他是純色
    }

    /// <summary>一種濾鏡：依序套用 CSS filter（sepia／saturate／contrast／brightness／grayscale／hue-rotate），再疊一層顏色（tint）、暗角。</summary>
    public struct PolaroidFilter
    {
        public string key, name;
        public FilterOp[] ops;
        public Color tint;      // a = 0 表示沒有
        public float vignette;  // 暗角：四角最暗的不透明度（0＝沒有），從中心 55% 開始變暗
        public bool dateStamp;  // 照片右下角橘色日期戳（Y2K CCD）
    }

    public enum FilterKind { Sepia, Saturate, Contrast, Brightness, Grayscale, HueRotate }

    public struct FilterOp
    {
        public FilterKind kind;
        public float amount;
        public FilterOp(FilterKind k, float a) { kind = k; amount = a; }
    }

    public struct SpawnSpot
    {
        public Vector2 pos;
        public float rot;
    }

    /// <summary>
    /// 拍立得分割畫面的規則（設計文件 5.26；docs/polaroid/polaroid_spec.json）。
    /// 待決 35 的事先用暫定值：每人每張貼紙上限 3、標題最多 16 字；濾鏡 2026-11-08 改成 3 種、預設不用；
    /// 拍照的人撕別人貼紙（長按）之後再畫，現在只有貼的人自己能撕。
    /// </summary>
    public static class PolaroidRules
    {
        public const float CardW = 354f, CardH = 431f;
        public const float PhotoX = 18f, PhotoY = 24f, PhotoSize = 318f;
        public const int StickerLimit = 3;          // 每人每張照片（暫定，待決 35）
        public const int CaptionMax = 16;           // 標題字數（暫定，待決 35）
        public const float PetStickerSize = 92f, DrawStickerSize = 66f;
        public const string DefaultFrame = "white";
        public const string DefaultFilter = "";     // 預設不用濾鏡（2026-11-08）

        /// <summary>貼紙列：前 6 個寵物貼紙、後 6 個手畫貼紙（5.26 ③）。</summary>
        public static readonly string[] Tray =
        {
            "pet:happy", "pet:tilt", "pet:sleep", "pet:drool", "pet:rainbow", "pet:extreme",
            "star", "heart", "spark", "cloud", "paw", "bolt",
        };

        static readonly Vector2[] Spots = { new Vector2(196, 30), new Vector2(40, 120), new Vector2(150, 170) };
        static readonly float[] SpotRot = { 8f, -8f };

        public static readonly PolaroidFrame[] Frames =
        {
            F("white", "白", 0xFFFFFF, 0x3A2E22),
            F("cream", "奶油", 0xFFF4DC, 0x3A2E22),
            F("pink", "粉紅", 0xF9D9E2, 0x8A3F5C),
            F("mint", "薄荷", 0xD6EEDC, 0x2F5E3A),
            F("sky", "天空", 0xD8E6FA, 0x2F4C8A),
            F("black", "黑", 0x2B2622, 0xFFF8EC),
            F("gingham", "格紋", 0xFFFFFF, 0x8A3F5C, "gingham"),
            F("dots", "星點", 0xFFFFFF, 0x3A2E22, "dots"),
            F("food", "美食色", 0xFFE3C7, 0xA4511B),
            F("stripe", "條紋", 0xFFFFFF, 0x2F4C8A, "stripe"),
        };

        /// <summary>沒有濾鏡（預設；再點一次選到的濾鏡也會回到這裡）。</summary>
        public static readonly PolaroidFilter None = new PolaroidFilter { key = "", name = "", ops = new FilterOp[0] };

        // 2026-11-08 改成 3 種；數值照 polaroid_spec.json 的 filters_css_reference（CSS filter 依寫的順序套用）
        public static readonly PolaroidFilter[] Filters =
        {
            new PolaroidFilter
            {
                key = "leica", name = "萊卡", vignette = 0.38f,
                ops = new[] { new FilterOp(FilterKind.Contrast, 1.25f), new FilterOp(FilterKind.Saturate, 0.85f),
                              new FilterOp(FilterKind.Brightness, 0.96f), new FilterOp(FilterKind.Sepia, 0.08f) },
            },
            new PolaroidFilter
            {
                key = "fuji", name = "富士底片", tint = Rgba(120, 200, 170, 0.12f),
                ops = new[] { new FilterOp(FilterKind.Contrast, 0.92f), new FilterOp(FilterKind.Saturate, 1.1f),
                              new FilterOp(FilterKind.Brightness, 1.05f), new FilterOp(FilterKind.HueRotate, -6f) },
            },
            new PolaroidFilter
            {
                key = "ccd", name = "Y2K（CCD）", tint = Rgba(150, 180, 255, 0.14f), dateStamp = true,
                ops = new[] { new FilterOp(FilterKind.Brightness, 1.12f), new FilterOp(FilterKind.Contrast, 1.05f),
                              new FilterOp(FilterKind.Saturate, 1.25f), new FilterOp(FilterKind.HueRotate, 8f) },
            },
        };

        public static PolaroidFrame Frame(string key)
        {
            foreach (var f in Frames) if (f.key == key) return f;
            return Frames[0];
        }

        /// <summary>找濾鏡；空字串、找不到（例如第一版拿掉的濾鏡）都當作沒有濾鏡。</summary>
        public static PolaroidFilter Filter(string key)
        {
            if (!string.IsNullOrEmpty(key)) foreach (var f in Filters) if (f.key == key) return f;
            return None;
        }

        public static bool HasFilter(string key) => Filter(key).key.Length > 0;

        /// <summary>點濾鏡：選到另一個就換；再點一次已選的就取消（不另外放「原圖」）。</summary>
        public static string ToggleFilter(string current, string tapped) =>
            HasFilter(current) && current == tapped ? DefaultFilter : (HasFilter(tapped) ? tapped : DefaultFilter);

        /// <summary>Y2K 日期戳「'YY MM DD」（用照片拍的那天）。</summary>
        public static string DateStamp(DateTime t) => $"'{t.Year % 100:00} {t.Month:00} {t.Day:00}";

        /// <summary>標題只有拍照的人能改（舊照片沒記拍照的人：誰都不能改）。</summary>
        public static bool CanStyle(string photoOwner, string me) =>
            !string.IsNullOrEmpty(photoOwner) && !string.IsNullOrEmpty(me) && photoOwner == me;

        /// <summary>畫框、濾鏡營地成員都能換（2026-11-08 使用者決定；原本只有拍照的人能換）。</summary>
        public static bool CanFrameFilter(string me) => !string.IsNullOrEmpty(me);

        /// <summary>自己的貼紙才能拖、撕。</summary>
        public static bool CanEditSticker(PolaroidSticker s, string me) =>
            s != null && !string.IsNullOrEmpty(me) && s.by == me;

        public static int MyCount(IEnumerable<PolaroidSticker> stickers, string me)
        {
            int n = 0;
            if (stickers != null && !string.IsNullOrEmpty(me))
                foreach (var s in stickers) if (s != null && s.by == me) n++;
            return n;
        }

        public static int StickersLeft(IEnumerable<PolaroidSticker> stickers, string me) =>
            Math.Max(0, StickerLimit - MyCount(stickers, me));

        public static bool CanAddSticker(IEnumerable<PolaroidSticker> stickers, string me) =>
            !string.IsNullOrEmpty(me) && StickersLeft(stickers, me) > 0;

        /// <summary>第 n 個（從 0）自己貼的貼紙放哪：(196,30)、(40,120)、(150,170) 依序輪，左右各轉 8°。</summary>
        public static SpawnSpot SpawnSpot(int n)
        {
            int i = ((n % Spots.Length) + Spots.Length) % Spots.Length;
            return new SpawnSpot { pos = Spots[i], rot = SpotRot[i % SpotRot.Length] };
        }

        public static bool IsPetSticker(string kind) => kind != null && kind.StartsWith("pet:");

        public static float StickerSize(string kind) => IsPetSticker(kind) ? PetStickerSize : DrawStickerSize;

        /// <summary>標題：去掉換行與前後空白，最多 16 字（以字為單位，emoji 不會被切一半）。</summary>
        public static string CleanCaption(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = text.Replace("\r", "").Replace("\n", "").Trim();
            var e = System.Globalization.StringInfo.GetTextElementEnumerator(s);
            var sb = new StringBuilder();
            int n = 0;
            while (e.MoveNext() && n < CaptionMax) { sb.Append(e.GetTextElement()); n++; }
            return sb.ToString();
        }

        /// <summary>
        /// 這張照片在拍立得畫面裝飾過嗎（寄明信片 5.21：裝飾過的直接用、縮圖右下有小拍立得記號）：
        /// 有貼紙、換過畫框、套了濾鏡、或寫了標題。
        /// </summary>
        public static bool IsDecorated(PolaroidStyle style, int stickerCount)
        {
            if (stickerCount > 0) return true;
            if (style == null) return false;
            return (!string.IsNullOrEmpty(style.frame) && style.frame != DefaultFrame)
                || HasFilter(style.filter)
                || !string.IsNullOrWhiteSpace(style.caption);
        }

        /// <summary>照片檔名 → Firebase 可以用的鍵（. # $ [ ] / 換成 _xx 十六進位，不會兩個檔名撞在一起）。</summary>
        public static string PhotoKey(string fileName)
        {
            var sb = new StringBuilder();
            foreach (char c in fileName ?? "")
            {
                if (c == '.' || c == '#' || c == '$' || c == '[' || c == ']' || c == '/' || c == '_')
                    sb.Append('_').Append(((int)c).ToString("x2"));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static PolaroidFrame F(string key, string name, int bg, int ink, string tile = null) =>
            new PolaroidFrame { key = key, name = name, bg = Hex(bg), ink = Hex(ink), tile = tile };

        static Color Rgba(int r, int g, int b, float a) => new Color(r / 255f, g / 255f, b / 255f, a);

        public static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }

    /// <summary>
    /// 濾鏡（CSS filter 的算法，W3C Filter Effects 規格的矩陣）：在手機上直接改像素，顯示和下載用同一套，看起來一樣。
    /// </summary>
    public static class PolaroidFilters
    {
        /// <summary>w、h＝圖的寬高（暗角要知道每個像素離中心多遠）。</summary>
        public static void Apply(Color32[] px, PolaroidFilter f, int w, int h)
        {
            if (px == null || ((f.ops == null || f.ops.Length == 0) && f.tint.a <= 0f && f.vignette <= 0f)) return;
            var ops = f.ops ?? new FilterOp[0];
            float ta = f.tint.a, tr = f.tint.r * ta, tg = f.tint.g * ta, tb = f.tint.b * ta, keep = 1f - ta;
            float hw = Mathf.Max(1f, w / 2f), hh = Mathf.Max(1f, h / 2f);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                foreach (var op in ops) Step(op, ref r, ref g, ref b);
                if (ta > 0f) { r = r * keep + tr; g = g * keep + tg; b = b * keep + tb; }
                if (f.vignette > 0f && w > 0)
                {
                    // CSS radial-gradient(ellipse, transparent 55%, rgba(0,0,0,a) 100%)：橢圓到四角＝100%
                    float nx = ((i % w) + 0.5f - hw) / hw, ny = ((i / w) + 0.5f - hh) / hh;
                    float d = Mathf.Sqrt(nx * nx + ny * ny) / 1.41421356f;
                    float a = f.vignette * Mathf.Clamp01((d - 0.55f) / 0.45f);
                    r *= 1f - a; g *= 1f - a; b *= 1f - a;
                }
                px[i] = new Color32(To8(r), To8(g), To8(b), c.a);
            }
        }

        static void Step(FilterOp op, ref float r, ref float g, ref float b)
        {
            float a = op.amount;
            switch (op.kind)
            {
                case FilterKind.Brightness: r *= a; g *= a; b *= a; break;
                case FilterKind.Contrast:
                    r = (r - 0.5f) * a + 0.5f; g = (g - 0.5f) * a + 0.5f; b = (b - 0.5f) * a + 0.5f; break;
                case FilterKind.Saturate:
                    Mat(ref r, ref g, ref b,
                        0.213f + 0.787f * a, 0.715f - 0.715f * a, 0.072f - 0.072f * a,
                        0.213f - 0.213f * a, 0.715f + 0.285f * a, 0.072f - 0.072f * a,
                        0.213f - 0.213f * a, 0.715f - 0.715f * a, 0.072f + 0.928f * a);
                    break;
                case FilterKind.Grayscale:
                    {
                        float k = 1f - Mathf.Clamp01(a);
                        Mat(ref r, ref g, ref b,
                            0.2126f + 0.7874f * k, 0.7152f - 0.7152f * k, 0.0722f - 0.0722f * k,
                            0.2126f - 0.2126f * k, 0.7152f + 0.2848f * k, 0.0722f - 0.0722f * k,
                            0.2126f - 0.2126f * k, 0.7152f - 0.7152f * k, 0.0722f + 0.9278f * k);
                        break;
                    }
                case FilterKind.HueRotate:
                    {
                        float rad = a * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
                        Mat(ref r, ref g, ref b,
                            0.213f + cos * 0.787f - sin * 0.213f, 0.715f - cos * 0.715f - sin * 0.715f, 0.072f - cos * 0.072f + sin * 0.928f,
                            0.213f - cos * 0.213f + sin * 0.143f, 0.715f + cos * 0.285f + sin * 0.140f, 0.072f - cos * 0.072f - sin * 0.283f,
                            0.213f - cos * 0.213f - sin * 0.787f, 0.715f - cos * 0.715f + sin * 0.715f, 0.072f + cos * 0.928f + sin * 0.072f);
                        break;
                    }
                case FilterKind.Sepia:
                    {
                        float k = 1f - Mathf.Clamp01(a);
                        Mat(ref r, ref g, ref b,
                            0.393f + 0.607f * k, 0.769f - 0.769f * k, 0.189f - 0.189f * k,
                            0.349f - 0.349f * k, 0.686f + 0.314f * k, 0.168f - 0.168f * k,
                            0.272f - 0.272f * k, 0.534f - 0.534f * k, 0.131f + 0.869f * k);
                        break;
                    }
            }
            // CSS 每一步之後都會夾在 0～1
            r = Mathf.Clamp01(r); g = Mathf.Clamp01(g); b = Mathf.Clamp01(b);
        }

        static void Mat(ref float r, ref float g, ref float b,
                        float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22)
        {
            float nr = m00 * r + m01 * g + m02 * b;
            float ng = m10 * r + m11 * g + m12 * b;
            float nb = m20 * r + m21 * g + m22 * b;
            r = nr; g = ng; b = nb;
        }

        static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
