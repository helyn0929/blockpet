using System;
using System.Collections.Generic;
using BlockPet.Travel;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>寄明信片的規則（設計文件 5.21，2026-11-08；docs/postcard/postcard_spec.json）。</summary>
    public class PostcardRulesTests
    {
        static readonly PostcardSettings S = new PostcardSettings();
        const long H = 3600L * 1000L;

        static long Ms(int y, int mo, int d, int h, int mi) =>
            new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        [Test]
        public void 換日用台灣時間0點()
        {
            // UTC 15:59 = 台灣 23:59；UTC 16:00 = 台灣隔天 0:00
            Assert.AreEqual("20261108", PostcardRules.DayKey(Ms(2026, 11, 8, 15, 59)));
            Assert.AreEqual("20261109", PostcardRules.DayKey(Ms(2026, 11, 8, 16, 0)));
        }

        [Test]
        public void 每天免費1次_之後用金幣()
        {
            Assert.AreEqual(0, PostcardRules.DrawCost(0, S));
            Assert.AreEqual(50, PostcardRules.DrawCost(1, S));
            Assert.AreEqual(1, PostcardRules.FreeLeft(0, S));
            Assert.AreEqual(0, PostcardRules.FreeLeft(3, S));
            // 存的是昨天的次數：今天歸零
            Assert.AreEqual(0, PostcardRules.DrawsToday("20261107", 4, "20261108"));
            Assert.AreEqual(4, PostcardRules.DrawsToday("20261108", 4, "20261108"));
        }

        [Test]
        public void 好友上限10個_解鎖一次加1格()
        {
            Assert.AreEqual(10, PostcardRules.FriendCapacity(0, S));
            Assert.AreEqual(12, PostcardRules.FriendCapacity(2, S));
            Assert.IsTrue(PostcardRules.IsFull(10, 0, S));
            Assert.IsFalse(PostcardRules.IsFull(10, 1, S));
        }

        static CampProfile P(string id, string top) => new CampProfile { roomId = id, top = top };

        [Test]
        public void 配對_最常拍的類別一樣優先()
        {
            var list = new List<CampProfile> { P("A", "城市"), P("B", "美食"), P("C", "自然"), P("D", "美食") };
            for (int seed = 0; seed < 20; seed++)
            {
                var pick = PostcardRules.Pick(list, "ME", "美食", null, new Random(seed));
                Assert.AreEqual("美食", pick.top);
            }
        }

        [Test]
        public void 配對_沒有一樣的就從其他營地抽()
        {
            var list = new List<CampProfile> { P("A", "城市"), P("C", "自然") };
            Assert.IsNotNull(PostcardRules.Pick(list, "ME", "美食", null, new Random(1)));
        }

        [Test]
        public void 配對_不會抽到自己_好友_封鎖過_等確認中的()
        {
            var list = new List<CampProfile> { P("ME", "美食"), P("F", "美食"), P("X", "美食"), P("OK", "城市") };
            var exclude = new HashSet<string> { "F", "X" };
            for (int seed = 0; seed < 20; seed++)
                Assert.AreEqual("OK", PostcardRules.Pick(list, "ME", "美食", exclude, new Random(seed)).roomId);
            Assert.IsNull(PostcardRules.Pick(new List<CampProfile> { P("ME", "美食") }, "ME", "美食", null, new Random(1)));
        }

        [Test]
        public void 最常拍的類別()
        {
            Assert.AreEqual("", PostcardRules.TopCategory(new Dictionary<string, int>()));
            Assert.AreEqual("城市", PostcardRules.TopCategory(new Dictionary<string, int> { { "美食", 2 }, { "城市", 5 } }));
            // 一樣多：照類別順序（美食在前）
            Assert.AreEqual("美食", PostcardRules.TopCategory(new Dictionary<string, int> { { "城市", 3 }, { "美食", 3 } }));
            Assert.IsTrue(PostcardRules.SameTop("美食", "美食"));
            Assert.IsFalse(PostcardRules.SameTop("", ""));
        }

        [Test]
        public void 等對方確認24小時後失效()
        {
            long t0 = Ms(2026, 11, 8, 0, 0);
            Assert.IsFalse(PostcardRules.MatchExpired(t0, t0 + 23 * H, S));
            Assert.IsTrue(PostcardRules.MatchExpired(t0, t0 + 24 * H, S));
        }

        [Test]
        public void 寵物回到家就帶回一張風景明信片_只帶一次()
        {
            long home = Ms(2026, 11, 8, 3, 0);
            Assert.IsFalse(PostcardRules.SceneryDue(home, home - 1, false)); // 還在路上
            Assert.IsTrue(PostcardRules.SceneryDue(home, home, false));
            Assert.IsFalse(PostcardRules.SceneryDue(home, home + 10 * H, true)); // 已經帶回過
            Assert.IsFalse(PostcardRules.SceneryDue(0, home, false));
        }

        [Test]
        public void 郵票郵戳的字()
        {
            Assert.AreEqual("", PostcardRules.StampLabel(1));
            Assert.AreEqual("×2", PostcardRules.StampLabel(2));
            Assert.AreEqual("2026.11.09", PostcardRules.PostmarkDate(Ms(2026, 11, 8, 16, 0)));
            Assert.AreEqual("11.09", PostcardRules.PostmarkShort(Ms(2026, 11, 8, 16, 0)));
            for (int i = 0; i < 30; i++)
            {
                int t = PostcardRules.MiniTilt("card" + i);
                Assert.That(t, Is.InRange(-4, 3));
            }
            Assert.AreEqual(PostcardRules.MiniTilt("abc"), PostcardRules.MiniTilt("abc"));
            Assert.AreEqual("台灣", PostcardRules.CountryName("tw"));
            Assert.AreEqual("BR", PostcardRules.CountryName("BR"));
        }
    }
}
