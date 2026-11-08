using System.Collections.Generic;
using BlockPet.Decoration;
using NUnit.Framework;
using UnityEngine;

namespace BlockPet.Tests
{
    /// <summary>佈置營地的斜角格子（設計文件 5.28；docs/decorate/decorate_spec.json）。</summary>
    public class CampGridRulesTests
    {
        static readonly Vector2 O = new Vector2(205, 370); // 設計稿 OX, OY

        [Test]
        public void 格子中心_照公式()
        {
            Assert.AreEqual(new Vector2(205, 385), GridMath.Center(new Cell(0, 0), O));
            Assert.AreEqual(new Vector2(235, 400), GridMath.Center(new Cell(1, 0), O), "i 往右下");
            Assert.AreEqual(new Vector2(175, 400), GridMath.Center(new Cell(0, 1), O), "j 往左下");
        }

        [Test]
        public void 點到哪一格_菱形裡面都算那一格()
        {
            var c = GridMath.Center(new Cell(3, 2), O);
            Assert.AreEqual(new Cell(3, 2), GridMath.CellAt(c, O));
            Assert.AreEqual(new Cell(3, 2), GridMath.CellAt(c + new Vector2(25, 0), O), "靠右但還在菱形裡");
            Assert.AreEqual(new Cell(3, 2), GridMath.CellAt(c + new Vector2(0, -12), O), "靠上但還在菱形裡");
            Assert.AreEqual(new Cell(4, 2), GridMath.CellAt(c + new Vector2(30, 15), O), "右下那格");
        }

        [Test]
        public void 底座佔的格子_寬沿i_深沿j()
        {
            var cells = GridMath.Footprint(new Cell(2, 5), 2, 1);
            CollectionAssert.AreEquivalent(new[] { new Cell(2, 5), new Cell(3, 5) }, cells);
            Assert.AreEqual(4, GridMath.Footprint(new Cell(0, 0), 2, 2).Count);
        }

        [Test]
        public void 能不能放_要存在_不是不能放_沒被別人佔()
        {
            var exists = new HashSet<Cell> { new Cell(0, 0), new Cell(1, 0), new Cell(0, 1), new Cell(1, 1), new Cell(2, 0) };
            var blocked = new HashSet<Cell> { new Cell(2, 0) };
            var taken = new Dictionary<Cell, string> { { new Cell(0, 1), "tent" } };

            Assert.IsTrue(GridMath.Fits(new Cell(0, 0), 2, 1, exists, blocked, taken, null));
            Assert.IsFalse(GridMath.Fits(new Cell(1, 0), 2, 1, exists, blocked, taken, null), "蓋到不能放的格子");
            Assert.IsFalse(GridMath.Fits(new Cell(0, 0), 2, 2, exists, blocked, taken, null), "蓋到別人的家具");
            Assert.IsTrue(GridMath.Fits(new Cell(0, 0), 2, 2, exists, blocked, taken, "tent"), "自己原本的位置可以");
            Assert.IsFalse(GridMath.Fits(new Cell(1, 1), 1, 2, exists, blocked, taken, null), "蓋到不存在的格子");
        }

        [Test]
        public void 前後順序_i加j加寬加深_越大越前面()
        {
            Assert.AreEqual(2 + 3 + 2 + 1, GridMath.DepthKey(new Cell(2, 3), 2, 1));
            Assert.Greater(GridMath.DepthKey(new Cell(3, 3), 1, 1), GridMath.DepthKey(new Cell(2, 3), 1, 1));
        }

        [Test]
        public void 家具圖_底部在底座最下角往上6_寬約寬加深乘30乘0點86()
        {
            var place = GridMath.ImagePlacement(new Cell(0, 0), 2, 1, O);
            // 最下角：格子 (1,0) 的下頂點 → y = OY + (1+0+1)*15 + 15 = 415
            Assert.AreEqual(415f - 6f, place.bottom, 0.01f);
            Assert.AreEqual((2 + 1) * 30f * 0.86f, place.width, 0.01f);
            // 水平置中在底座中心
            Assert.AreEqual(205f + (0 - 0 + (2 - 1) / 2f) * 30f, place.centerX, 0.01f);
        }

        [Test]
        public void 已用格子數()
        {
            var placed = new List<(Cell anchor, int w, int d)> { (new Cell(0, 0), 2, 2), (new Cell(5, 5), 1, 1) };
            Assert.AreEqual(5, GridMath.UsedCells(placed));
        }
    }
}
