using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockPet.Decoration
{
    /// <summary>擴建（花田、湖畔…）：買了 unlockItemId 之後多開的一塊格子（設計文件 5.28 ②；介面先留好）。</summary>
    [Serializable]
    public class GridExtension
    {
        [Tooltip("商店的環境 id（例 exp_lake）；營地買過才開這塊")]
        public string unlockItemId;
        public List<Cell> cells = new List<Cell>();
        public List<Cell> blocked = new List<Cell>();
    }

    /// <summary>
    /// 一個營地場景的格子表（設計文件 5.28 ②）：哪些格子存在（鋪滿草地）、哪些不能放（小路、樹叢、高草叢、石頭）。
    /// 在 Unity 裡標：選到這個檔案時 Scene 畫面會畫出格子，用滑鼠點選（CampGridEditor）。
    /// 對齊營地底圖：worldOrigin＝格子 (0,0) 上頂點的世界座標、tileWorldWidth＝一格的世界寬度（設計 60）；
    /// 編輯器有「對齊營地底圖」按鈕，照設計稿 OX 205、OY 370 從底圖算出來，再手動微調。
    /// 執行時放在 Resources/Camp/CampGrid_{場景}.asset。
    /// </summary>
    [CreateAssetMenu(fileName = "CampGrid_forest", menuName = "BlockPet/Camp Grid")]
    public class CampGrid : ScriptableObject
    {
        public string sceneId = "forest";
        [Tooltip("格子 (0,0) 上頂點的世界座標（設計稿的 OX, OY）")]
        public Vector2 worldOrigin;
        [Tooltip("一格的世界寬度（設計 60 @1x）；高度是一半")]
        public float tileWorldWidth = 1f;
        [Tooltip("存在的格子（鋪滿整片草地）")]
        public List<Cell> cells = new List<Cell>();
        [Tooltip("不能放的格子（小路、樹叢、高草叢、石頭），佈置時顯示淡紅色")]
        public List<Cell> blocked = new List<Cell>();
        [Tooltip("擴建多開的格子（買了環境才開）")]
        public List<GridExtension> extensions = new List<GridExtension>();

        /// <summary>每個設計 px 是多少世界單位。</summary>
        public float WorldPerPx => tileWorldWidth / GridMath.TileW;

        /// <summary>設計座標（以格子原點為 0、y 向下）→ 世界座標。</summary>
        public Vector3 ToWorld(Vector2 design) =>
            new Vector3(worldOrigin.x + design.x * WorldPerPx, worldOrigin.y - design.y * WorldPerPx, 0f);

        public Vector2 ToDesign(Vector3 world) =>
            new Vector2((world.x - worldOrigin.x) / WorldPerPx, (worldOrigin.y - world.y) / WorldPerPx);

        public Vector3 CellCenterWorld(Cell c) => ToWorld(GridMath.Center(c, Vector2.zero));

        public Cell CellAtWorld(Vector3 world) => GridMath.CellAt(ToDesign(world), Vector2.zero);

        /// <summary>現在開放的格子（基本＋已解鎖的擴建）。owned＝營地買過的商品 id。</summary>
        public HashSet<Cell> ActiveCells(ICollection<string> owned)
        {
            var set = new HashSet<Cell>(cells);
            foreach (var e in extensions)
                if (e != null && owned != null && owned.Contains(e.unlockItemId)) set.UnionWith(e.cells);
            return set;
        }

        public HashSet<Cell> ActiveBlocked(ICollection<string> owned)
        {
            var set = new HashSet<Cell>(blocked);
            foreach (var e in extensions)
                if (e != null && owned != null && owned.Contains(e.unlockItemId)) set.UnionWith(e.blocked);
            return set;
        }

        /// <summary>可以放的格子數（存在、不是不能放）：「已用 N / M 格」的 M。</summary>
        public int PlaceableCount(ICollection<string> owned)
        {
            var c = ActiveCells(owned);
            c.ExceptWith(ActiveBlocked(owned));
            return c.Count;
        }

        public static CampGrid Load(string sceneId) => Resources.Load<CampGrid>("Camp/CampGrid_" + (string.IsNullOrEmpty(sceneId) ? "forest" : sceneId));
    }
}
