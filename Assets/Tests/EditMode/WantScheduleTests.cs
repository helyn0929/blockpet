using System;
using BlockPet.Feed;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>
    /// 「現在想吃」時段測試（docs/design/blockpet-economy-spec.md 1.3：每 2 小時換一次，以伺服器時間為準；
    /// 2026-11-05 定：台灣時間偶數整點換、不和上一個時段一樣）。
    /// </summary>
    public class WantScheduleTests
    {
        static long Taipei(int y, int mo, int d, int h, int mi = 0, int s = 0) =>
            new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();

        [Test]
        public void 同一個兩小時內是同一個時段()
        {
            Assert.AreEqual(WantSchedule.SlotOf(Taipei(2026, 11, 5, 14, 0)), WantSchedule.SlotOf(Taipei(2026, 11, 5, 15, 59, 59)));
        }

        [Test]
        public void 台灣時間偶數整點換時段()
        {
            Assert.AreEqual(WantSchedule.SlotOf(Taipei(2026, 11, 5, 13, 59, 59)) + 1, WantSchedule.SlotOf(Taipei(2026, 11, 5, 14, 0)));
            Assert.AreEqual(WantSchedule.SlotOf(Taipei(2026, 11, 5, 23, 59, 59)) + 1, WantSchedule.SlotOf(Taipei(2026, 11, 6, 0, 0)));
            // 奇數整點不換
            Assert.AreEqual(WantSchedule.SlotOf(Taipei(2026, 11, 5, 14, 59, 59)), WantSchedule.SlotOf(Taipei(2026, 11, 5, 15, 0)));
        }

        [Test]
        public void 一天有十二個時段()
        {
            Assert.AreEqual(12, WantSchedule.SlotOf(Taipei(2026, 11, 6, 0)) - WantSchedule.SlotOf(Taipei(2026, 11, 5, 0)));
        }

        [Test]
        public void 時段開始時間是偶數整點()
        {
            long slot = WantSchedule.SlotOf(Taipei(2026, 11, 5, 15, 30));
            Assert.AreEqual(Taipei(2026, 11, 5, 14, 0), WantSchedule.SlotStartUtcMs(slot));
        }

        [Test]
        public void 下一次換的倒數()
        {
            Assert.AreEqual(TimeSpan.FromMinutes(30), WantSchedule.UntilNextChange(Taipei(2026, 11, 5, 15, 30)));
            Assert.AreEqual(TimeSpan.FromHours(2), WantSchedule.UntilNextChange(Taipei(2026, 11, 5, 16, 0)));
        }

        [Test]
        public void 同一個時段內結果一樣()
        {
            Assert.AreEqual(WantSchedule.At("ROOM01", 0, Taipei(2026, 11, 5, 14, 0)), WantSchedule.At("ROOM01", 0, Taipei(2026, 11, 5, 15, 59)));
        }

        [Test]
        public void 結果一定是五大類之一()
        {
            long start = WantSchedule.SlotOf(Taipei(2026, 11, 1, 0));
            for (long s = start; s < start + 100; s++)
                CollectionAssert.Contains(PetPreferences.Categories, WantSchedule.For("ROOM01", 2, s));
        }

        [Test]
        public void 不和上一個時段一樣()
        {
            foreach (int pet in new[] { 0, 1, 5 })
            {
                long start = WantSchedule.SlotOf(Taipei(2026, 10, 1, 0));
                string prev = WantSchedule.For("ROOM01", pet, start);
                for (long s = start + 1; s < start + 24 * 12; s++)
                {
                    string cur = WantSchedule.For("ROOM01", pet, s);
                    Assert.AreNotEqual(prev, cur, $"寵物 {pet} 第 {s} 段");
                    prev = cur;
                }
            }
        }

        [Test]
        public void 先查後面再查前面結果一樣()
        {
            long a = WantSchedule.SlotOf(Taipei(2026, 11, 5, 10));
            long b = a + 30;
            string later = WantSchedule.For("ROOM02", 3, b);
            string earlier = WantSchedule.For("ROOM02", 3, a);
            Assert.AreEqual(later, WantSchedule.For("ROOM02", 3, b));
            Assert.AreEqual(earlier, WantSchedule.For("ROOM02", 3, a));
        }

        [Test]
        public void 一段時間內大多是偏愛類別()
        {
            // 偏愛類別合計 70%（扣掉不能和上一段一樣後會略有變動），抽 1200 段應該明顯過半
            var fav = PetPreferences.FavoritesOf(0);
            long start = WantSchedule.SlotOf(Taipei(2026, 1, 1, 0));
            int favHits = 0, n = 1200;
            for (long s = start; s < start + n; s++)
                if (Array.IndexOf(fav, WantSchedule.For("ROOM03", 0, s)) >= 0) favHits++;
            Assert.That(favHits / (float)n, Is.InRange(0.5f, 0.85f));
        }
    }
}
