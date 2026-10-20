using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Room
{
    /// <summary>
    /// 圓環扇形（設計稿「選擇寵物空間 E 角落轉盤」）。
    /// 所有數值都用設計座標：x 向右、y 向下、角度順時針（與 CSS／SVG 相同）；
    /// RectTransform 要蓋滿 390×772 的設計根、pivot 在左上，設計點 (x, y) 對應本地座標 (x, −y)。
    /// - 實心：可貼照片（以「未旋轉時的扇形外框」做填滿裁切，旋轉時照片跟著轉，與設計稿一致）。
    /// - 外框：stroke &gt; 0 時只畫邊線（兩段圓弧＋兩條半徑）。
    /// - 視窗：只畫 [clipMin, clipMax] 角度內的部分（轉盤外的格子被裁掉）。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class RingSectorGraphic : MaskableGraphic, ICanvasRaycastFilter
    {
        public Vector2 center = new Vector2(390f, 772f);
        public float r1 = 172f, r2 = 352f;
        [Tooltip("未旋轉時的起訖角度（度）")]
        public float a0, a1 = 90f;
        [Tooltip("整體旋轉（度），照片跟著轉")]
        public float rotation;
        public float clipMin = -9999f, clipMax = 9999f;
        [Tooltip("0 = 實心；> 0 = 外框粗細")]
        public float stroke;
        public Texture texture;

        public override Texture mainTexture => texture != null ? texture : s_WhiteTexture;

        public void Set(float startDeg, float endDeg, float rotationDeg)
        {
            a0 = startDeg; a1 = endDeg; rotation = rotationDeg;
            SetVerticesDirty();
        }

        public void SetTexture(Texture tex)
        {
            if (texture == tex) return;
            texture = tex;
            SetMaterialDirty();
            SetVerticesDirty();
        }

        static Vector2 Pt(Vector2 c, float r, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector2(c.x + r * Mathf.Cos(a), c.y + r * Mathf.Sin(a));
        }

        static Vector2 Local(Vector2 design) => new Vector2(design.x, -design.y);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (stroke > 0f) { PopulateStroke(vh); return; }

            float s = Mathf.Max(a0 + rotation, clipMin);
            float e = Mathf.Min(a1 + rotation, clipMax);
            if (e <= s || r2 <= r1) return;

            // 照片：用未旋轉的扇形外框做填滿裁切
            Rect box = default, uv = new Rect(0, 0, 1, 1);
            bool hasTex = texture != null;
            if (hasTex)
            {
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
                for (int t = 0; t <= 8; t++)
                {
                    float aa = Mathf.Lerp(a0, a1, t / 8f);
                    foreach (float r in new[] { r1, r2 })
                    {
                        var p = Pt(center, r, aa);
                        min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                    }
                }
                box = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                uv = AspectFill(texture.width, texture.height, box.width, box.height);
            }

            int steps = Mathf.Max(2, Mathf.CeilToInt((e - s) / 2f));
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(s, e, i / (float)steps);
                foreach (float r in new[] { r1, r2 })
                {
                    var p = Pt(center, r, a);
                    Vector2 tc = Vector2.zero;
                    if (hasTex)
                    {
                        var p0 = Pt(center, r, a - rotation);
                        float u = (p0.x - box.xMin) / box.width;
                        float v = 1f - (p0.y - box.yMin) / box.height;
                        tc = new Vector2(uv.x + u * uv.width, uv.y + v * uv.height);
                    }
                    vh.AddVert(Local(p), color, tc);
                }
                if (i > 0)
                {
                    int b = (i - 1) * 2;
                    vh.AddTriangle(b, b + 1, b + 3);
                    vh.AddTriangle(b, b + 3, b + 2);
                }
            }
        }

        void PopulateStroke(VertexHelper vh)
        {
            float s = a0 + rotation, e = a1 + rotation, h = stroke / 2f;
            // 圓弧往兩端多延伸半個線寬，蓋住轉角
            Arc(vh, r2 - h, r2 + h, s - h / r2 * Mathf.Rad2Deg, e + h / r2 * Mathf.Rad2Deg);
            Arc(vh, r1 - h, r1 + h, s - h / r1 * Mathf.Rad2Deg, e + h / r1 * Mathf.Rad2Deg);
            Radial(vh, s, r1 - h, r2 + h, h);
            Radial(vh, e, r1 - h, r2 + h, h);
        }

        void Arc(VertexHelper vh, float ri, float ro, float s, float e)
        {
            int start = vh.currentVertCount;
            int steps = Mathf.Max(2, Mathf.CeilToInt((e - s) / 2f));
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(s, e, i / (float)steps);
                vh.AddVert(Local(Pt(center, ri, a)), color, Vector2.zero);
                vh.AddVert(Local(Pt(center, ro, a)), color, Vector2.zero);
                if (i > 0)
                {
                    int b = start + (i - 1) * 2;
                    vh.AddTriangle(b, b + 1, b + 3);
                    vh.AddTriangle(b, b + 3, b + 2);
                }
            }
        }

        void Radial(VertexHelper vh, float deg, float ri, float ro, float h)
        {
            float a = deg * Mathf.Deg2Rad;
            var n = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * h;
            Vector2 pi = Pt(center, ri, deg), po = Pt(center, ro, deg);
            int b = vh.currentVertCount;
            vh.AddVert(Local(pi - n), color, Vector2.zero);
            vh.AddVert(Local(pi + n), color, Vector2.zero);
            vh.AddVert(Local(po + n), color, Vector2.zero);
            vh.AddVert(Local(po - n), color, Vector2.zero);
            vh.AddTriangle(b, b + 1, b + 2);
            vh.AddTriangle(b, b + 2, b + 3);
        }

        /// <summary>點擊只算落在扇形（且在視窗內）的位置。</summary>
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out var local))
                return false;
            return Contains(new Vector2(local.x, -local.y));
        }

        public bool Contains(Vector2 design)
        {
            var d = design - center;
            float r = d.magnitude;
            if (r < r1 || r > r2) return false;
            float a = AngleOf(design);
            return a >= Mathf.Max(a0 + rotation, clipMin) && a <= Mathf.Min(a1 + rotation, clipMax);
        }

        /// <summary>設計座標點相對圓心的角度（0～360，順時針）。</summary>
        public float AngleOf(Vector2 design)
        {
            var d = design - center;
            float a = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            return a < 0f ? a + 360f : a;
        }

        static Rect AspectFill(float texW, float texH, float boxW, float boxH)
        {
            if (texW <= 0 || texH <= 0 || boxW <= 0 || boxH <= 0) return new Rect(0, 0, 1, 1);
            float texAspect = texW / texH, boxAspect = boxW / boxH;
            if (texAspect > boxAspect)
            {
                float w = boxAspect / texAspect;
                return new Rect((1f - w) / 2f, 0f, w, 1f);
            }
            float hgt = texAspect / boxAspect;
            return new Rect(0f, (1f - hgt) / 2f, 1f, hgt);
        }
    }
}
