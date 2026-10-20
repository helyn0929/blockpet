using UnityEngine;
using UnityEngine.UI;

namespace BlockPet.UI.Room
{
    /// <summary>凸多邊形（三角形箭頭、播放鍵）。點用設計座標，相對 RectTransform 左上角。</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIPolygon : MaskableGraphic
    {
        public Vector2[] points;

        public void SetPoints(params Vector2[] pts)
        {
            points = pts;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points == null || points.Length < 3) return;
            var r = rectTransform.rect;
            foreach (var p in points)
                vh.AddVert(new Vector2(r.xMin + p.x, r.yMax - p.y), color, Vector2.zero);
            for (int i = 1; i < points.Length - 1; i++)
                vh.AddTriangle(0, i, i + 1);
        }
    }
}
