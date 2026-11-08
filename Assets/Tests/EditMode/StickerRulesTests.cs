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
        public void 三種尺寸_避開狀態欄和底部按鈕()
        {
            var count = new Dictionary<float, int> { { 66f, 0 }, { 88f, 0 }, { 106f, 0 } };
            const int N = 3000;
            for (int i = 0; i < N; i++)
            {
                var s = StickerRules.Spot("photo_" + i + ".jpg");
                Assert.That(count.ContainsKey(s.size), $"尺寸只能是 66／88／106，拿到 {s.size}");
                count[s.size]++;
                Assert.That(s.y, Is.GreaterThanOrEqualTo(StickerRules.TopLimit));
                Assert.That(s.y + s.size, Is.LessThanOrEqualTo(StickerRules.BottomLimit));
                Assert.That(s.x, Is.GreaterThanOrEqualTo(StickerRules.SideMargin));
                Assert.That(s.x + s.size, Is.LessThanOrEqualTo(StickerRules.DesignW - StickerRules.SideMargin));
                Assert.That(s.rot, Is.InRange(StickerRules.MinRot, StickerRules.MaxRot));
            }
            // 小的多、大的少：66 約 50%、88 約 30%、106 約 20%（2026-11-06 使用者決定）
            Assert.That(count[66f] / (float)N, Is.InRange(0.45f, 0.55f));
            Assert.That(count[88f] / (float)N, Is.InRange(0.25f, 0.35f));
            Assert.That(count[106f] / (float)N, Is.InRange(0.15f, 0.25f));
        }

        static List<string> Ids(List<StickerPhoto> list)
        {
            var ids = new List<string>();
            foreach (var p in list) ids.Add(p.id);
            return ids;
        }
    }
}
