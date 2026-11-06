using BlockPet.Travel;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>
    /// 旅行地圖生成（設計文件 5.21 ②；travel_spec.json map_generation）。
    /// 期望值是用設計稿 TravelGrow 的原始 JavaScript 算出來的，確認移植後每個營地的地圖和設計稿一模一樣。
    /// </summary>
    public class TravelMapGenTests
    {
        const double Eps = 0.001;

        [Test]
        public void 營地代碼的種子()
        {
            Assert.AreEqual(785080343u, TravelMapGen.Hash("K7R2PX"));
            Assert.AreEqual(383982287u, TravelMapGen.Hash("M3Q8TA"));
        }

        [Test]
        public void 營地A的好友位置和設計稿一樣()
        {
            var slots = TravelMapGen.Slots(TravelMapGen.Hash("K7R2PX"));
            Assert.AreEqual(TravelMapGen.MaxFriends + 1, slots.Count);
            AssertPoint(286.2134, 460.7767, slots[0].pos);
            AssertPoint(138.9650, 514.3790, slots[1].pos);
            AssertPoint(149.6229, 226.9080, slots[2].pos);
        }

        [Test]
        public void 營地B十個好友也和設計稿一樣_含夾邊()
        {
            var slots = TravelMapGen.Slots(TravelMapGen.Hash("M3Q8TA"));
            AssertPoint(248.9417, 271.6371, slots[0].pos);
            AssertPoint(58, 244.8223, slots[6].pos);   // x 夾在 58
            AssertPoint(332, 168, slots[9].pos);       // x、y 都夾邊
            Assert.AreEqual(-0.2725, slots[0].bend, 0.0001);
        }

        [Test]
        public void 營地A三個好友的陸地()
        {
            var map = TravelMapGen.Generate("K7R2PX", 3);
            // 家 1 塊＋家旁 3 塊＋每個好友 5 塊
            Assert.AreEqual(1 + 3 + 3 * 5, map.land.Count);
            Assert.AreEqual(112, map.land[0].r, Eps);
            AssertPoint(218.3408, 301.3772, map.land[1].c);
            Assert.AreEqual(62.8338, map.land[1].r, 0.0001);
            AssertPoint(212.7431, 451.4832, map.land[3].c);
            Assert.AreEqual(78.2658, map.land[3].r, 0.0001);
        }

        [Test]
        public void 營地B十個好友的陸地數量()
        {
            Assert.AreEqual(54, TravelMapGen.Generate("M3Q8TA", 10).land.Count);
        }

        [Test]
        public void 路的控制點()
        {
            var map = TravelMapGen.Generate("K7R2PX", 3);
            AssertPoint(235.8551, 425.7539, map.roads[0].ctrl);
            AssertPoint(195, 380, map.roads[0].At(0));
            AssertPoint(286.2134, 460.7767, map.roads[0].At(1));
        }

        [Test]
        public void 同一個代碼每次一樣_加好友時舊的不動()
        {
            var a = TravelMapGen.Generate("K7R2PX", 3);
            var b = TravelMapGen.Generate("K7R2PX", 4);
            for (int i = 0; i < 3; i++) AssertPoint(a.friends[i].x, a.friends[i].y, b.friends[i]);
            for (int i = 0; i < a.land.Count; i++) AssertPoint(a.land[i].c.x, a.land[i].c.y, b.land[i].c);
        }

        [Test]
        public void 不同代碼地圖不一樣()
        {
            var a = TravelMapGen.Generate("K7R2PX", 3);
            var b = TravelMapGen.Generate("Z5W1HD", 3);
            Assert.AreNotEqual(a.friends[0].x, b.friends[0].x);
        }

        [Test]
        public void 好友位置在範圍內_離家和彼此夠遠()
        {
            foreach (var code in new[] { "K7R2PX", "M3Q8TA", "Z5W1HD", "AAAAAA" })
            {
                var slots = TravelMapGen.Slots(TravelMapGen.Hash(code));
                foreach (var s in slots)
                {
                    Assert.That(s.pos.x, Is.InRange(58, 332), code);
                    Assert.That(s.pos.y, Is.InRange(168, 600), code);
                    Assert.That(s.bend, Is.InRange(-0.275, 0.275), code);
                }
            }
        }

        [Test]
        public void 示範假資料_營地A三個好友()
        {
            TravelMapGen.DemoFriends("K7R2PX", 3, 1, out var names, out var trips);
            CollectionAssert.AreEqual(new[] { "小黑的辦公室營地", "海邊小屋", "山上的家" }, names);
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, trips);
        }

        [Test]
        public void 路的樣式_去過越多次越寬_最多四次()
        {
            var r = new MapRoad { trips = 0 };
            Assert.IsFalse(r.Visited);
            Assert.AreEqual(3.5, r.Width, Eps);
            Assert.AreEqual(0, r.EdgeWidth, Eps);
            r.trips = 2;
            Assert.AreEqual(8, r.Width, Eps);
            Assert.AreEqual(13, r.EdgeWidth, Eps);
            r.trips = 9;
            Assert.AreEqual(10, r.Width, Eps);
            Assert.AreEqual(15, r.EdgeWidth, Eps);
        }

        [Test]
        public void 營地A三個好友的裝飾和設計稿一樣()
        {
            TravelMapGen.DemoFriends("K7R2PX", 3, 1, out _, out var trips);
            var m = TravelMapGen.Generate("K7R2PX", 3, trips);
            Assert.AreEqual(1, m.lakes.Count);
            AssertPoint(236.903, 327.498, m.lakes[0].c);
            Assert.AreEqual(20.145, m.lakes[0].rx, Eps);
            Assert.AreEqual(2, m.hills.Count);
            AssertPoint(205.163, 507.812, m.hills[0].c);
            Assert.AreEqual(79, m.trees.Count);
            AssertPoint(181.521, 185.080, m.trees[0].c);
            Assert.IsTrue(m.trees[0].dark);
            Assert.AreEqual(22, m.flowers.Count);
            AssertPoint(237.390, 468.445, m.flowers[0].c);
            Assert.AreEqual(2, m.flowers[0].color);
            // 去過 1＋2 趟 → 路邊 3 樣
            Assert.AreEqual(3, m.props.Count);
            AssertPoint(231.814, 398.082, m.props[0].c);
            Assert.AreEqual(1, m.props[0].kind);
            Assert.IsTrue(m.hasNext);
            Assert.AreEqual(10, m.fog.Count);
            AssertPoint(286.245, 560.520, m.fog[0].c);
        }

        [Test]
        public void 營地B十個好友_沒有雲霧()
        {
            TravelMapGen.DemoFriends("M3Q8TA", 10, 3, out _, out var trips);
            CollectionAssert.AreEqual(new[] { 1, 3, 1, 0, 2, 2, 2, 0, 0, 0 }, trips);
            var m = TravelMapGen.Generate("M3Q8TA", 10, trips);
            Assert.IsFalse(m.hasNext);
            Assert.AreEqual(0, m.fog.Count);
            Assert.AreEqual(0, m.lakes.Count);
            Assert.AreEqual(1, m.hills.Count);
            Assert.AreEqual(96, m.trees.Count);
            Assert.AreEqual(38, m.flowers.Count);
            AssertPoint(185.952, 335.076, m.props[0].c);
            Assert.AreEqual(3, m.props[0].kind);
        }

        [Test]
        public void 樹由上往下排()
        {
            var m = TravelMapGen.Generate("Z5W1HD", 6);
            for (int i = 1; i < m.trees.Count; i++) Assert.LessOrEqual(m.trees[i - 1].c.y, m.trees[i].c.y);
        }

        [Test]
        public void 成長階段()
        {
            Assert.AreEqual(0, TravelMapGen.Stage(0));
            Assert.AreEqual(0, TravelMapGen.Stage(2));
            Assert.AreEqual(1, TravelMapGen.Stage(3));
            Assert.AreEqual(2, TravelMapGen.Stage(6));
            Assert.AreEqual(3, TravelMapGen.Stage(10));
        }

        static void AssertPoint(double x, double y, MapPoint p)
        {
            Assert.AreEqual(x, p.x, Eps, "x");
            Assert.AreEqual(y, p.y, Eps, "y");
        }
    }
}
