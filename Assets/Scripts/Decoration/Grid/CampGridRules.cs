using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockPet.Decoration
{
    /// <summary>斜角格子的一格：i 往右下、j 往左下（設計文件 5.28 ②）。</summary>
    [Serializable]
    public struct Cell : IEquatable<Cell>
    {
        public int i, j;
        public Cell(int i, int j) { this.i = i; this.j = j; }
        public bool Equals(Cell o) => i == o.i && j == o.j;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => (i * 397) ^ j;
        public override string ToString() => $"({i},{j})";
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
    }

    /// <summary>家具圖片放在哪裡（設計座標，y 向下）：底部 y、水平中心、寬度。</summary>
    public struct ImagePlace
    {
        public float bottom, centerX, width;
    }

    /// <summary>
    /// 斜角格子的數學（設計文件 5.28；docs/decorate/decorate_spec.json），座標都是設計座標（@1x、y 向下）：
    /// - 一格菱形寬 60、高 30；格子中心＝(OX + (i − j) × 30, OY + (i + j + 1) × 15)。
    /// - 家具底座：沿 i 佔 w 格（寬）、沿 j 佔 d 格（深）；存的是底座最上面那格 anchor＝(i0, j0)，錨點（底座中心）由此算出。
    /// - 能不能放：底座蓋到的每一格都存在、不是不能放、沒被別的家具佔（自己原本的位置可以）。
    /// - 前後：i + j + w + d 越大越前面（等於底座最下角越低越前面，寵物用腳的位置排，結果一樣）。
    /// - 家具圖：底部對齊底座最下角往上 6；寬約 (w + d) × 30 × 0.86；水平置中在底座中心。
    /// </summary>
    public static class GridMath
    {
        public const float TileW = 60f, TileH = 30f;
        public const float ImageLift = 6f, ImageWidthFactor = 0.86f;

        public static Vector2 Center(Cell c, Vector2 origin) =>
            new Vector2(origin.x + (c.i - c.j) * TileW / 2f, origin.y + (c.i + c.j + 1) * TileH / 2f);

        /// <summary>設計座標的點在哪一格（菱形在 (i, j) 空間裡是以整數為中心的正方形，四捨五入就好）。</summary>
        public static Cell CellAt(Vector2 p, Vector2 origin)
        {
            float a = (p.x - origin.x) / (TileW / 2f);          // i − j
            float b = (p.y - origin.y) / (TileH / 2f) - 1f;     // i + j
            return new Cell(Mathf.RoundToInt((a + b) / 2f), Mathf.RoundToInt((b - a) / 2f));
        }

        public static List<Cell> Footprint(Cell anchor, int w, int d)
        {
            var list = new List<Cell>(Mathf.Max(1, w * d));
            for (int di = 0; di < Mathf.Max(1, w); di++)
                for (int dj = 0; dj < Mathf.Max(1, d); dj++)
                    list.Add(new Cell(anchor.i + di, anchor.j + dj));
            return list;
        }

        /// <summary>
        /// 放得下嗎：底座每一格都存在、不在 blocked、沒被別人佔（taken：格子 → 佔用者 id；self＝這件家具自己的 id，自己佔的可以）。
        /// </summary>
        public static bool Fits(Cell anchor, int w, int d, ICollection<Cell> exists, ICollection<Cell> blocked,
                                IReadOnlyDictionary<Cell, string> taken, string self)
        {
            foreach (var c in Footprint(anchor, w, d))
            {
                if (exists == null || !exists.Contains(c)) return false;
                if (blocked != null && blocked.Contains(c)) return false;
                if (taken != null && taken.TryGetValue(c, out var who) && who != self) return false;
            }
            return true;
        }

        public static int DepthKey(Cell anchor, int w, int d) => anchor.i + anchor.j + Mathf.Max(1, w) + Mathf.Max(1, d);

        public static ImagePlace ImagePlacement(Cell anchor, int w, int d, Vector2 origin)
        {
            w = Mathf.Max(1, w); d = Mathf.Max(1, d);
            // 底座最下角＝格子 (i0+w−1, j0+d−1) 的下頂點
            float bottomY = origin.y + (anchor.i + anchor.j + w + d) * TileH / 2f;
            float centerX = origin.x + (anchor.i - anchor.j + (w - d) / 2f) * TileW / 2f;
            return new ImagePlace { bottom = bottomY - ImageLift, centerX = centerX, width = (w + d) * TileW / 2f * ImageWidthFactor };
        }

        /// <summary>底座中心（錨點，設計座標）。</summary>
        public static Vector2 AnchorPoint(Cell anchor, int w, int d, Vector2 origin)
        {
            w = Mathf.Max(1, w); d = Mathf.Max(1, d);
            return new Vector2(origin.x + (anchor.i - anchor.j + (w - d) / 2f) * TileW / 2f,
                               origin.y + (anchor.i + anchor.j + (w + d) / 2f) * TileH / 2f);
        }

        /// <summary>選了底座中心在 p 附近時，anchor 該放哪一格（點哪一格就讓底座中心靠近那裡）。</summary>
        public static Cell AnchorForTap(Cell tapped, int w, int d) =>
            new Cell(tapped.i - (Mathf.Max(1, w) - 1) / 2, tapped.j - (Mathf.Max(1, d) - 1) / 2);

        public static int UsedCells(IEnumerable<(Cell anchor, int w, int d)> placed)
        {
            int n = 0;
            if (placed != null) foreach (var p in placed) n += Mathf.Max(1, p.w) * Mathf.Max(1, p.d);
            return n;
        }
    }
}
