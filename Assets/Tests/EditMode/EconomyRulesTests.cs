using BlockPet.Feed;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>
    /// 經濟規則測試：docs/design/blockpet-economy-spec.md 第 2 節的驗收條件（乘數表、愛心回復、餵食金幣、移動速度）。
    /// 需要 Firebase 的條件（成員 A、B 帳本、帳戶與花費）不在這裡。
    /// </summary>
    public class EconomyRulesTests
    {
        // ─── 乘數表 ─────────────────────────────────────────────────

        [TestCase(1f, 0.6f)]
        [TestCase(2f, 0.8f)]
        [TestCase(3f, 1.0f)]
        [TestCase(4f, 1.2f)]
        [TestCase(5f, 1.4f)]
        public void 整數愛心的乘數(float hearts, float expected)
        {
            Assert.AreEqual(expected, EconomyRules.Multiplier(hearts), 1e-5f);
        }

        [TestCase(1.5f, 0.6f)]
        [TestCase(2.5f, 0.8f)]
        [TestCase(4.5f, 1.2f)]
        public void 半顆心無條件捨去(float hearts, float expected)
        {
            Assert.AreEqual(expected, EconomyRules.Multiplier(hearts), 1e-5f);
        }

        [Test]
        public void 金幣和移動速度讀同一張表()
        {
            for (float h = 0f; h <= 5f; h += 0.5f)
                Assert.AreEqual(EconomyRules.Multiplier(h), EconomyRules.SpeedMultiplier(h), 0f, $"愛心 {h}");
        }

        [Test]
        public void 星等和乘數等級一致()
        {
            for (float h = 0f; h <= 5f; h += 0.5f)
                Assert.AreEqual(EconomyRules.HeartLevel(h), PetHearts.Stars(h), $"愛心 {h}");
        }

        // ─── 愛心回復 ───────────────────────────────────────────────

        [TestCase(3f, true, 4f)]
        [TestCase(3f, false, 3.5f)]
        [TestCase(4.5f, true, 5f)]
        [TestCase(5f, false, 5f)]
        public void 餵食回復愛心(float hearts, bool hit, float expected)
        {
            Assert.AreEqual(expected, EconomyRules.HeartsAfterFeed(hearts, hit), 1e-5f);
        }

        // ─── 餵食金幣（先回復愛心，再算金幣）───────────────────────

        [TestCase(5f, true, 5f, 7)]
        [TestCase(4f, true, 5f, 7)]
        [TestCase(3.5f, true, 4.5f, 6)]
        [TestCase(3f, true, 4f, 6)]
        [TestCase(2f, true, 3f, 5)]
        [TestCase(1f, true, 2f, 4)]
        [TestCase(0.5f, true, 1.5f, 3)]
        [TestCase(5f, false, 5f, 1)]
        [TestCase(1f, false, 1.5f, 1)]
        public void 餵一張的愛心和金幣(float hearts, bool hit, float expectedHearts, int expectedCoins)
        {
            float after = EconomyRules.HeartsAfterFeed(hearts, hit);
            Assert.AreEqual(expectedHearts, after, 1e-5f, "愛心");
            Assert.AreEqual(expectedCoins, EconomyRules.FeedCoins(after, hit), "金幣");
        }

        [Test]
        public void 兩顆心連續命中三張()
        {
            float hearts = 2f;
            int total = 0;
            var seen = new float[3];
            for (int i = 0; i < 3; i++)
            {
                hearts = EconomyRules.HeartsAfterFeed(hearts, true);
                seen[i] = hearts;
                total += EconomyRules.FeedCoins(hearts, true);
            }
            CollectionAssert.AreEqual(new[] { 3f, 4f, 5f }, seen);
            Assert.AreEqual(18, total);
        }

        // ─── 移動速度 ───────────────────────────────────────────────

        [TestCase(5f, 1.4f)]
        [TestCase(3f, 1.0f)]
        [TestCase(2.5f, 0.8f)]
        [TestCase(1f, 0.6f)]
        public void 移動速度倍率(float hearts, float expected)
        {
            Assert.AreEqual(expected, EconomyRules.SpeedMultiplier(hearts), 1e-5f);
        }

        [Test]
        public void 速度不會超出範圍()
        {
            for (float h = -1f; h <= 6f; h += 0.25f)
            {
                float m = EconomyRules.SpeedMultiplier(h);
                Assert.That(m, Is.InRange(0.6f, 1.4f), $"愛心 {h}");
            }
        }

        // ─── 未定（暫用值）──────────────────────────────────────────

        [Test]
        public void 暫用_半顆心的速度當作一顆()
        {
            Assert.AreEqual(0.6f, EconomyRules.SpeedMultiplier(0.5f), 1e-5f);
        }

        [Test]
        public void 暫用_昏倒時不能餵()
        {
            Assert.IsFalse(EconomyRules.CanFeed(0f));
            Assert.IsTrue(EconomyRules.CanFeed(0.5f));
        }
    }
}
