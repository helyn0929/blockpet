using System.Collections.Generic;
using BlockPet.Travel;
using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Travel
{
    /// <summary>
    /// 旅行地圖的路：用程式畫二次貝茲曲線（設計文件 5.21 ⑦「路用程式畫曲線，不用圖塊」）。
    /// 實線或虛線、圓頭；座標是設計座標（原點左上、y 向下），RectTransform 要蓋滿設計根（pivot 左上）。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class MapCurveGraphic : MaskableGraphic
    {
        public MapPoint from, ctrl, to;
        public float width = 4f;
        /// <summary>虛線：線段長、間隔（0 ＝ 實線）。SVG 的 stroke-dasharray。</summary>
        public float dash, gap;
        /// <summary>虛線往前流動的距離（SVG stroke-dashoffset 的負值；旅途中的紅線用）。</summary>
        public float flow;

        public void SetFlow(float f) { flow = f; SetVerticesDirty(); }

        const int Samples = 64;
        const int CircleSegments = 12;

        public void Set(MapPoint p0, MapPoint c, MapPoint p1, float w, Color col, float dashLen = 0f, float gapLen = 0f)
        {
            from = p0; ctrl = c; to = p1; width = w; color = col; dash = dashLen; gap = gapLen;
            raycastTarget = false;
            SetVerticesDirty();
        }

        static Vector2 V(MapPoint p) => new Vector2((float)p.x, -(float)p.y);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (width <= 0f) return;

            // 取樣＋累計長度
            var pts = new List<Vector2>(Samples + 1);
            var len = new List<float>(Samples + 1);
            float acc = 0f;
            for (int i = 0; i <= Samples; i++)
            {
                var p = V(TravelMapGen.Bezier(from, ctrl, to, i / (double)Samples));
                if (i > 0) acc += Vector2.Distance(pts[i - 1], p);
                pts.Add(p);
                len.Add(acc);
            }

            float r = width / 2f;
            if (dash <= 0f)
            {
                for (int i = 0; i < Samples; i++) Segment(vh, pts[i], pts[i + 1], r);
                foreach (var p in pts) Circle(vh, p, r); // 圓頭＋接縫
                return;
            }
            float period = dash + gap;
            float start = period > 0f ? (flow % period) - period : 0f;
            for (float s = start; s < acc; s += period)
            {
                float s0 = Mathf.Max(0f, s), s1 = Mathf.Min(acc, s + dash);
                if (s1 <= s0) continue;
                var a = PointAt(pts, len, s0);
                var b = PointAt(pts, len, s1);
                if ((b - a).sqrMagnitude > 0.0001f) Segment(vh, a, b, r);
                Circle(vh, a, r);
                Circle(vh, b, r);
            }
        }

        static Vector2 PointAt(List<Vector2> pts, List<float> len, float s)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                if (len[i] < s) continue;
                float seg = len[i] - len[i - 1];
                float t = seg > 0f ? (s - len[i - 1]) / seg : 0f;
                return Vector2.Lerp(pts[i - 1], pts[i], t);
            }
            return pts[pts.Count - 1];
        }

        void Segment(VertexHelper vh, Vector2 a, Vector2 b, float r)
        {
            var d = (b - a).normalized;
            var n = new Vector2(-d.y, d.x) * r;
            int start = vh.currentVertCount;
            vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero);
            vh.AddVert(b - n, color, Vector2.zero);
            vh.AddVert(a - n, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        void Circle(VertexHelper vh, Vector2 c, float r)
        {
            int start = vh.currentVertCount;
            vh.AddVert(c, color, Vector2.zero);
            for (int i = 0; i <= CircleSegments; i++)
            {
                float a = i / (float)CircleSegments * Mathf.PI * 2f;
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, color, Vector2.zero);
            }
            for (int i = 1; i <= CircleSegments; i++) vh.AddTriangle(start, start + i, start + i + 1);
        }
    }
}
