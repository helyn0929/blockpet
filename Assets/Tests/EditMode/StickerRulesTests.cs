using System.Collections.Generic;
using BlockPet.Home;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>首頁照片貼紙（設計文件 5.25 ④）。</summary>
    public class StickerRulesTests
    {
        const string Me = "me";

        static StickerPhoto P(string id, string author, long time) => new StickerPhoto(id, author, time);

        [Test]
        public void 只貼別人發的_沒看過的_更新之後的()
        {
            var photos = new[]
            {
                P("a", "mom", 5),    // 更新之前 → 當作看過
                P("b", Me, 20),      // 自己的
                P("c", "mom", 21),   // 撕過了
                P("d", "dad", 22),   // ✔
                P("e", "mom", 23),   // 現在的最新貼紙
                P("f", null, 24),    // 舊資料沒有拍照人 → 算別人
            };
            var list = StickerRules.Visible(photos, Me, new HashSet<string> { "c" }, 10, "e", out int more);
            CollectionAssert.AreEqual(new[] { "d", "f" }, Ids(list));
            Assert.AreEqual(0, more);
        }

        [Test]
        public void 最多八張_貼最新的_舊的算加N()
        {
            var photos = new List<StickerPhoto>();
            for (int i = 11; i >= 0; i--) photos.Add(P("p" + i, "mom", 100 + i));
            var list = StickerRules.Visible(photos, Me, null, 0, null, out int more);
            Assert.AreEqual(StickerRules.MaxStickers, list.Count);
            Assert.AreEqual("p4", list[0].id);   // 舊到新排，新的疊在上面
            Assert.AreEqual("p11", list[7].id);
            Assert.AreEqual(4, more);
        }

        [Test]
        public void 新照片進來擠掉最舊的()
        {
            var photos = new List<StickerPhoto>();
            for (int i = 0; i < 8; i++) photos.Add(P("p" + i, "mom", 100 + i));
            photos.Add(P("new", "dad", 500));
            var list = StickerRules.Visible(photos, Me, null, 0, null, out int more);
            CollectionAssert.Contains(Ids(list), "new");
            CollectionAssert.DoesNotContain(Ids(list), "p0");
            Assert.AreEqual(1, more);
        }

        [Test]
        public void 撕掉一張就補上下一張()
        {
            var photos = new List<StickerPhoto>();
            for (int i = 0; i < 10; i++) photos.Add(P("p" + i, "mom", 100 + i));
            var list = StickerRules.Visible(photos, Me, new HashSet<string> { "p5" }, 0, null, out int more);
            CollectionAssert.Contains(Ids(list), "p1");
            CollectionAssert.DoesNotContain(Ids(list), "p5");
            Assert.AreEqual(1, more);
        }

        [Test]
        public void 重複的照片只算一次()
        {
            var list = StickerRules.Visible(new[] { P("a", "mom", 50), P("a", "mom", 50) }, Me, null, 0, null, out _);
            Assert.AreEqual(1, list.Count);
        }

        [Test]
        public void 同一張照片位置固定()
        {
            var a = StickerRules.Spot("IMG_0012.jpg");
            var b = StickerRules.Spot("IMG_0012.jpg");
            Assert.AreEqual(a.x, b.x);
            Assert.AreEqual(a.y, b.y);
            Assert.AreEqual(a.rot, b.rot);
            Assert.AreEqual(a.size, b.size);
            Assert.AreNotEqual(a.x + a.y * 1000, StickerRules.Spot("IMG_0013.jpg").x + StickerRules.Spot("IMG_0013.jpg").y * 1000);
        }

        [Test]
        public void 有大有小_避開狀態欄和底部按鈕()
        {
            float min = 999, max = 0;
            for (int i = 0; i < 500; i++)
            {
                var s = StickerRules.Spot("photo_" + i + ".jpg");
                Assert.That(s.size, Is.InRange(StickerRules.MinSize, StickerRules.MaxSize));
                min = System.Math.Min(min, s.size);
                max = System.Math.Max(max, s.size);
                Assert.That(s.y, Is.GreaterThanOrEqualTo(StickerRules.TopLimit));
                Assert.That(s.y + s.size, Is.LessThanOrEqualTo(StickerRules.BottomLimit));
                Assert.That(s.x, Is.GreaterThanOrEqualTo(StickerRules.SideMargin));
                Assert.That(s.x + s.size, Is.LessThanOrEqualTo(StickerRules.DesignW - StickerRules.SideMargin));
                Assert.That(s.rot, Is.InRange(StickerRules.MinRot, StickerRules.MaxRot));
            }
            Assert.Less(min, 62f, "要有小的");
            Assert.Greater(max, 90f, "要有大的");
        }

        static List<string> Ids(List<StickerPhoto> list)
        {
            var ids = new List<string>();
            foreach (var p in list) ids.Add(p.id);
            return ids;
        }
    }
}
