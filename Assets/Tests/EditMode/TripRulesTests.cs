using BlockPet.Travel;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>派寵物旅行（設計文件 5.21 ③；travel_spec.json travel_rules）：路程、階段、時間顯示。</summary>
    public class TripRulesTests
    {
        const long Min = 60_000L;

        [Test]
        public void 單程依距離_15到75分_取5的倍數()
        {
            var home = new MapPoint(195, 380);
            Assert.AreEqual(15, TripRules.OneWayMinutes(home, new MapPoint(195, 380 + 30)));   // 很近：最少 15
            Assert.AreEqual(75, TripRules.OneWayMinutes(home, new MapPoint(195, 380 + 2000))); // 很遠：最多 75
            // 營地 A 第一個好友（286.2134, 460.7767）：距離 121.9 ÷ 3.5 ÷ 5 ≈ 6.97 → 35 分
            Assert.AreEqual(35, TripRules.OneWayMinutes(home, new MapPoint(286.2134, 460.7767)));
        }

        [Test]
        public void 旅行的階段()
        {
            long d = 1_000_000L;
            Assert.AreEqual(TripPhase.None, TripRules.Phase(d, 0, 30));
            Assert.AreEqual(TripPhase.Going, TripRules.Phase(d + 10 * Min, d, 30));
            Assert.AreEqual(TripPhase.There, TripRules.Phase(d + 30 * Min, d, 30));
            Assert.AreEqual(TripPhase.There, TripRules.Phase(d + 59 * Min, d, 30));
            Assert.AreEqual(TripPhase.Returning, TripRules.Phase(d + 60 * Min, d, 30));
            Assert.AreEqual(TripPhase.Done, TripRules.Phase(d + 90 * Min, d, 30));
            Assert.AreEqual(d + 90 * Min, TripRules.HomeAt(d, 30));
        }

        [Test]
        public void 來回總共約1到3小時()
        {
            Assert.AreEqual(60, (TripRules.HomeAt(0, 15) - 0) / Min);   // 15＋30＋15
            Assert.AreEqual(180, (TripRules.HomeAt(0, 75) - 0) / Min);  // 75＋30＋75
        }

        [Test]
        public void 寵物在路上的位置()
        {
            long d = 1_000_000L; // 出發時間 0 代表「沒有旅行」，測試要用真的時間
            Assert.AreEqual(0, TripRules.RoadT(d, d, 30), 1e-9);
            Assert.AreEqual(0.5, TripRules.RoadT(d + 15 * Min, d, 30), 1e-9);
            Assert.AreEqual(1, TripRules.RoadT(d + 45 * Min, d, 30), 1e-9);   // 在那邊
            Assert.AreEqual(0.5, TripRules.RoadT(d + 75 * Min, d, 30), 1e-9); // 回程一半
            Assert.AreEqual(0, TripRules.RoadT(d + 100 * Min, d, 30), 1e-9);  // 到家
        }

        [Test]
        public void 時間顯示()
        {
            Assert.AreEqual("35 分", TripRules.FormatMinutes(35));
            Assert.AreEqual("1 小時", TripRules.FormatMinutes(60));
            Assert.AreEqual("1 小時 35 分", TripRules.FormatMinutes(95));
            Assert.AreEqual(1, TripRules.MinutesUntil(0, 10_000)); // 不到 1 分也算 1 分
            Assert.AreEqual(0, TripRules.MinutesUntil(10, 5));
        }

        [Test]
        public void 章的顏色和角度固定()
        {
            Assert.AreEqual(TripRules.StampColor("ROOMX"), TripRules.StampColor("ROOMX"));
            CollectionAssert.Contains(TripRules.StampColors, TripRules.StampColor("ROOMY"));
            Assert.That(TripRules.StampTilt("ROOMZ"), Is.InRange(-8f, 10f));
        }
    }
}
