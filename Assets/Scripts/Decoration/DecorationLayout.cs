using UnityEngine;

namespace BlockPet.Decoration
{
    /// <summary>
    /// 家具擺在格子上的樣子（設計文件 5.28 ③；規則在 GridMath）：
    /// - 圖片底部對齊底座最下角往上 6，寬約 (寬 + 深) × 30 × 0.86，水平置中在底座中心。
    /// - 前後：和寵物同一套（AIController.SortOrderForY，用底座最下角的高度），i + j + 寬 + 深 越大越前面。
    /// - 腳下的影子（deco_shadow，寬度跟底座）。
    /// 沒有格子座標的（舊的自由擺放）先藏起來，佈置時列在「還沒擺出來的家具」；沒有格子表時照舊的 x、y 擺。
    /// </summary>
    public static class DecorationLayout
    {
        public static void Apply(DecorationObject obj, DecorationItem item, CampGrid grid)
        {
            if (obj == null || item == null) return;
            if (grid == null || grid.tileWorldWidth <= 0f)
            {
                obj.gameObject.SetActive(true);
                obj.transform.localScale = Vector3.one * item.worldScale;
                return;
            }
            bool show = obj.HasGrid && !item.hangPoint;
            obj.gameObject.SetActive(show);
            if (show) ApplyAt(obj, item, grid, obj.Anchor);
        }

        /// <summary>把家具畫在某個 anchor（拖曳時跟著手指一格一格吸附；不改資料）。</summary>
        public static void ApplyAt(DecorationObject obj, DecorationItem item, CampGrid grid, Cell anchor)
        {
            var sr = obj.Renderer;
            obj.transform.rotation = Quaternion.identity;
            float scale = PlaceSprite(obj.transform, sr != null ? sr.sprite : null, item, grid, anchor, item.worldScale);
            var foot = FootWorld(item, anchor, grid);
            if (sr != null) sr.sortingOrder = AIController.SortOrderForY(foot.y);
            Shadow(obj, item, grid, foot, scale);
        }

        /// <summary>把一張家具圖放到 anchor 上（底部對齊、寬度照底座），回傳縮放；拖曳預覽也用這個。</summary>
        public static float PlaceSprite(Transform t, Sprite sprite, DecorationItem item, CampGrid grid, Cell anchor, float fallbackScale = 1f)
        {
            int w = Mathf.Max(1, item.footprintW), d = Mathf.Max(1, item.footprintD);
            var place = GridMath.ImagePlacement(anchor, w, d, Vector2.zero);
            var bottom = grid.ToWorld(new Vector2(place.centerX, place.bottom));
            float targetW = place.width * grid.WorldPerPx;
            float scale = sprite != null && sprite.bounds.size.x > 0f ? targetW / sprite.bounds.size.x : fallbackScale;
            t.localScale = Vector3.one * scale;
            var local = sprite != null ? new Vector3(sprite.bounds.center.x, sprite.bounds.min.y, 0f) : Vector3.zero;
            t.position = bottom - local * scale;
            return scale;
        }

        /// <summary>底座中心的世界座標（拖曳時手指抓的點）。</summary>
        public static Vector3 CenterWorld(DecorationItem item, Cell anchor, CampGrid grid) =>
            grid.ToWorld(GridMath.AnchorPoint(anchor, Mathf.Max(1, item.footprintW), Mathf.Max(1, item.footprintD), Vector2.zero));

        /// <summary>底座最下角的世界座標（寵物的腳用同一個高度比前後）。</summary>
        public static Vector3 FootWorld(DecorationItem item, Cell anchor, CampGrid grid)
        {
            int w = item != null ? Mathf.Max(1, item.footprintW) : 1, d = item != null ? Mathf.Max(1, item.footprintD) : 1;
            var place = GridMath.ImagePlacement(anchor, w, d, Vector2.zero);
            return grid.ToWorld(new Vector2(place.centerX, place.bottom + GridMath.ImageLift));
        }

        /// <summary>腳下的影子：寬度跟底座，畫在家具後面。</summary>
        static void Shadow(DecorationObject obj, DecorationItem item, CampGrid grid, Vector3 foot, float objScale)
        {
            var skin = BlockPet.UI.Decorate.DecorateSkin.Load();
            if (skin == null || skin.shadow == null) return;
            var t = obj.transform.Find("Shadow");
            SpriteRenderer sr;
            if (t == null)
            {
                var go = new GameObject("Shadow");
                go.transform.SetParent(obj.transform, false);
                sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = skin.shadow;
                sr.sortingLayerName = obj.Renderer.sortingLayerName;
            }
            else sr = t.GetComponent<SpriteRenderer>();
            float w = (Mathf.Max(1, item.footprintW) + Mathf.Max(1, item.footprintD)) * GridMath.TileW / 2f * 0.9f * grid.WorldPerPx;
            float s = skin.shadow.bounds.size.x > 0f ? w / skin.shadow.bounds.size.x : 1f;
            sr.transform.localScale = Vector3.one * (s / Mathf.Max(0.0001f, objScale));
            sr.transform.position = foot + new Vector3(0f, grid.WorldPerPx * 8f, 0f);
            sr.sortingOrder = obj.Renderer.sortingOrder - 1;
        }
    }
}
