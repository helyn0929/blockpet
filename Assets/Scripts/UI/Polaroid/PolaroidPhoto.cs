using BlockPet.Polaroid;
using UnityEngine;

namespace BlockPet.UI.Polaroid
{
    /// <summary>
    /// 拍立得的照片：置中裁成正方形（直式照片能不能自己調位置是待決 35，先置中），縮到需要的大小，再套濾鏡。
    /// 濾鏡用 CSS filter 的算法直接改像素（PolaroidFilters），畫面和下載的圖用同一份。
    /// </summary>
    public static class PolaroidPhoto
    {
        /// <summary>照片 318 × 318 的 @3x。</summary>
        public const int PhotoPx = 954;
        public const int ThumbPx = 252; // 濾鏡縮圖 84 寬的 @3x

        /// <summary>讀照片，置中裁正方形、縮成 size × size（可讀的 Texture2D）。找不到照片回傳 null。</summary>
        public static Texture2D LoadSquare(PhotoMeta meta, int size)
        {
            var full = SaveManager.Instance != null ? SaveManager.Instance.LoadPhoto(meta) : null;
            if (full == null) return null;
            try { return Square(full, size); }
            finally { Object.Destroy(full); }
        }

        public static Texture2D Square(Texture src, int size)
        {
            float side = Mathf.Min(src.width, src.height);
            var scale = new Vector2(side / src.width, side / src.height);
            var offset = new Vector2((1f - scale.x) / 2f, (1f - scale.y) / 2f);
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.filterMode = FilterMode.Bilinear;
            var prevFilter = src.filterMode;
            src.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, rt, scale, offset);
            src.filterMode = prevFilter;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        /// <summary>套濾鏡，回傳新的貼圖（原圖不動）。原圖濾鏡也會複製一份，呼叫端可以統一釋放。</summary>
        public static Texture2D Filtered(Texture2D square, string filterKey)
        {
            if (square == null) return null;
            var px = square.GetPixels32();
            PolaroidFilters.Apply(px, PolaroidRules.Filter(filterKey), square.width, square.height);
            var tex = new Texture2D(square.width, square.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
