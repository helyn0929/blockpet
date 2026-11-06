using BlockPet.Feed;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>
    /// 圖鑑規則（設計文件 5.23）與進化三種型的判斷（5.17 ②）：
    /// 每系列 3 格（彩虹型、極致型、進化型，沒有稀有）、同一格收好幾次只算一格、新格判斷、系列順序、獎勵暫用值。
    /// </summary>
    public class DexRulesTests
    {
        static DexEntry E(int gen) => new DexEntry { gen = gen, evolvedAt = 1000L * gen, days = 30 };

        // ─── 格子 ───────────────────────────────────────────────────

        [Test]
        public void 每系列三格_彩虹極致進化()
        {
            Assert.AreEqual(3, DexRules.CellsPerSeries);
            Assert.AreEqual(3, DexRules.Forms.Length);
            Assert.AreEqual("彩虹型", DexRules.Forms[0].name);
            Assert.AreEqual("極致型", DexRules.Forms[1].name);
            Assert.AreEqual("進化型", DexRules.Forms[2].name);
            foreach (var f in DexRules.Forms) Assert.IsNotEmpty(f.how, f.name);
        }

        // ─── 進化三種型（5.17 ②，依序看）────────────────────────────

        [TestCase(new[] { 51, 20, 10, 10, 9 }, "extreme")]  // 主色超過 50%
        [TestCase(new[] { 50, 20, 10, 10, 10 }, "evolved")] // 剛好 50% 不算超過
        [TestCase(new[] { 0, 100, 0, 0, 0 }, "extreme")]
        [TestCase(new[] { 29, 20, 20, 16, 15 }, "rainbow")] // 五色都有、主色不到 30%
        [TestCase(new[] { 30, 20, 20, 15, 15 }, "evolved")] // 剛好 30% 不算不到
        [TestCase(new[] { 28, 28, 28, 16, 0 }, "evolved")]  // 少一色就不是彩虹
        [TestCase(new[] { 40, 35, 10, 10, 5 }, "evolved")]
        public void 進化型的判斷(int[] palette, string expected)
        {
            Assert.AreEqual(expected, DexRules.TypeFor(palette));
        }

        [Test]
        public void 用張數或百分比判斷都一樣()
        {
            // 100 張裡 46／20／14／12／8 和 23／10／7／6／4（一半）是同一個比例
            Assert.AreEqual(DexRules.TypeFor(new[] { 46, 20, 14, 12, 8 }), DexRules.TypeFor(new[] { 23, 10, 7, 6, 4 }));
            Assert.AreEqual(DexRules.Extreme, DexRules.TypeFor(new[] { 26, 10, 7, 5, 2 })); // 26/50 超過一半
        }

        [Test]
        public void 沒有資料算進化型()
        {
            Assert.AreEqual(DexRules.Evolved, DexRules.TypeFor(null));
            Assert.AreEqual(DexRules.Evolved, DexRules.TypeFor(new int[5]));
        }

        // ─── 圖鑑內容 ───────────────────────────────────────────────

        [Test]
        public void 空的圖鑑()
        {
            var book = new DexBook();
            Assert.AreEqual(0, book.TotalGot());
            Assert.AreEqual(DexRules.Series.Length * 3, DexBook.TotalMax);
            Assert.IsTrue(book.IsNewCell("bowbow", DexRules.Extreme));
        }

        [Test]
        public void 同一格收兩次只算一格()
        {
            var book = new DexBook();
            book.Add("bowbow", DexRules.Evolved, E(2));
            book.Add("bowbow", DexRules.Evolved, E(4));
            book.Add("bowbow", DexRules.Extreme, E(1));
            Assert.AreEqual(2, book.Got("bowbow"));
            Assert.AreEqual(2, book.Count("bowbow", DexRules.Evolved));
            Assert.AreEqual(3, book.TotalEntries());
            Assert.IsFalse(book.IsNewCell("bowbow", DexRules.Evolved));
            Assert.IsTrue(book.IsNewCell("bowbow", DexRules.Rainbow));
        }

        [Test]
        public void 舊的七格資料不算進三格()
        {
            var book = new DexBook();
            book.Add("bowbow", "forest", E(1)); // 11-05 以前的外型
            Assert.AreEqual(0, book.Got("bowbow"));
        }

        [Test]
        public void 每一代按代數排()
        {
            var book = new DexBook();
            book.Add("bowbow", DexRules.Evolved, E(4));
            book.Add("bowbow", DexRules.Evolved, E(2));
            var list = book.Entries("bowbow", DexRules.Evolved);
            Assert.AreEqual(2, list[0].gen);
            Assert.AreEqual(4, list[1].gen);
        }

        [Test]
        public void 收滿三格()
        {
            var book = new DexBook();
            for (int i = 0; i < DexRules.Forms.Length; i++) book.Add("cat", DexRules.Forms[i].key, E(i + 1));
            Assert.IsTrue(book.IsComplete("cat"));
            Assert.AreEqual(3, book.TotalGot());
            Assert.IsFalse(book.IsComplete("bowbow"));
        }

        [Test]
        public void 營地有的品種排前面_其他照目錄順序()
        {
            var book = new DexBook();
            book.Add("rabbit", DexRules.Extreme, E(1));
            var order = book.OrderedSeries();
            Assert.AreEqual("bowbow", order[0].id);
            Assert.AreEqual("rabbit", order[1].id);
            Assert.AreEqual("cat", order[2].id);
            Assert.AreEqual("halloween", order[3].id);
        }

        [Test]
        public void 現在的寵物都算BowBow_一開始就有BowBow系列()
        {
            var book = new DexBook();
            Assert.IsTrue(book.Owns("bowbow"));
            Assert.AreEqual(0, book.Got("bowbow"));
            Assert.IsFalse(book.Owns("cat"));
            book.Add("cat", DexRules.Evolved, E(1));
            Assert.IsTrue(book.Owns("cat"));
        }

        [Test]
        public void 系列說明()
        {
            var bow = DexRules.FindSeries("bowbow");
            var rabbit = DexRules.FindSeries("rabbit");
            var halloween = DexRules.FindSeries("halloween");
            Assert.AreEqual("收滿 3 格送免費抽一次寵物蛋", DexRules.Note(bow, true, false));
            Assert.AreEqual("收滿了！已經送過免費抽一次寵物蛋", DexRules.Note(bow, true, true));
            Assert.AreEqual("還沒有兔子：抽寵物蛋有機會抽到", DexRules.Note(rabbit, false, false));
            Assert.AreEqual("10/15～11/15 才抽得到萬聖節蛋", DexRules.Note(halloween, false, false));
            Assert.AreEqual("去商店抽寵物蛋", DexRules.ShopCta(rabbit));
            Assert.AreEqual("去商店抽萬聖節蛋", DexRules.ShopCta(halloween));
        }

        [Test]
        public void 不認得的系列或型不記()
        {
            var book = new DexBook();
            book.Add("", DexRules.Evolved, E(1));
            book.Add("bowbow", "", E(1));
            book.Add("bowbow", DexRules.Evolved, null);
            Assert.AreEqual(0, book.TotalEntries());
            Assert.IsNull(DexRules.Form("forest"));
            Assert.IsNull(DexRules.FindSeries("nope"));
        }

        [Test]
        public void 暫用_新格獎勵一百金幣()
        {
            Assert.AreEqual(100, EconomyRules.DexNewCellCoins);
        }
    }
}
