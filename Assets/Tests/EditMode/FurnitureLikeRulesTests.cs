using System;
using System.Collections.Generic;
using System.IO;
using BlockPet.Feed;
using NUnit.Framework;
using UnityEngine;

namespace BlockPet.Tests
{
    /// <summary>家具愛好（設計文件 5.18 ⑤，2026-11-14）。</summary>
    public class FurnitureLikeRulesTests
    {
        /// <summary>每次都回傳同一個數的亂數（測機率用）。</summary>
        class FixedRandom : System.Random
        {
            readonly double _v;
            public FixedRandom(double v) { _v = v; }
            protected override double Sample() => _v;
        }

        static LikeEntry E(string item, string action, float weight = 1f) => new LikeEntry { item = item, action = action, weight = weight };

        static FurnitureLikeTable Table() => new FurnitureLikeTable
        {
            breeds = new[]
            {
                new BreedLikes
                {
                    breed = "bowbow",
                    sets = new[]
                    {
                        new LikeSet { key = "自然", likes = new[] { E("furn_hammock", "sleep"), E("furn_fire", "look") } },
                        new LikeSet { key = "美食", likes = new[] { E("furn_mat", "sit"), E("furn_lantern", "look") } },
                        new LikeSet { key = "rainbow", likes = new[] { E("furn_easel", "look") } },
                        new LikeSet { key = "城市", likes = new[] { E("a", "look"), E("b", "look"), E("c", "look"), E("d", "look"), E("e", "look"), E("f", "look"), E("g", "look") } },
                        new LikeSet { key = "juvenile", likes = new[] { E("furn_tent", "sleep"), E("furn_hammock", "sleep"), E("furn_mat", "sit"), E("furn_fire", "look"), E("furn_easel", "look"), E("furn_lantern", "look") } },
                    },
                },
            },
        };

        [Test]
        public void LikesFor_EvolvedUsesMainColor()
        {
            var likes = FurnitureLikeRules.LikesFor(Table(), "bowbow", DexRules.Evolved, "自然", 1);
            Assert.AreEqual(2, likes.Count);
            Assert.AreEqual("furn_hammock", likes[0].item);
            Assert.AreEqual("sleep", likes[0].action);
            Assert.AreEqual("furn_fire", likes[1].item);
        }

        [Test]
        public void LikesFor_RainbowKey()
        {
            var likes = FurnitureLikeRules.LikesFor(Table(), "bowbow", DexRules.Rainbow, "美食", 1);
            Assert.AreEqual(1, likes.Count);
            Assert.AreEqual("furn_easel", likes[0].item);
        }

        [Test]
        public void LikesFor_UnknownBreedFallsBackToBowbow()
        {
            var likes = FurnitureLikeRules.LikesFor(Table(), "fish", DexRules.Extreme, "美食", 1);
            Assert.AreEqual("furn_mat", likes[0].item);
        }

        [Test]
        public void LikesFor_JuvenileTwoFixedBySeed()
        {
            var a = FurnitureLikeRules.LikesFor(Table(), "bowbow", null, null, 42);
            var b = FurnitureLikeRules.LikesFor(Table(), "bowbow", "", null, 42);
            Assert.AreEqual(2, a.Count);
            Assert.AreNotEqual(a[0].item, a[1].item);
            Assert.AreEqual(a[0].item, b[0].item);
            Assert.AreEqual(a[1].item, b[1].item);
            var seen = new HashSet<string>();
            for (int s = 0; s < 30; s++)
                foreach (var e in FurnitureLikeRules.LikesFor(Table(), "bowbow", null, null, s)) seen.Add(e.item);
            Assert.Greater(seen.Count, 2, "不同寵物會喜歡不一樣的");
        }

        [Test]
        public void LikesFor_CapsAtMax()
        {
            Assert.AreEqual(5, FurnitureLikeRules.LikesFor(Table(), "bowbow", DexRules.Evolved, "城市", 1).Count);
        }

        [Test]
        public void PickVisit_IgnoresItemsNotPlaced()
        {
            var likes = new List<LikeEntry> { E("furn_hammock", "sleep"), E("furn_fire", "look") };
            var pick = FurnitureLikeRules.PickVisit(likes, id => id == "furn_fire", new Dictionary<string, float>(), 100f, Table(), new FixedRandom(0.1));
            Assert.AreEqual("furn_fire", pick.item);
            Assert.IsNull(FurnitureLikeRules.PickVisit(likes, id => false, new Dictionary<string, float>(), 100f, Table(), new FixedRandom(0.1)));
        }

        [Test]
        public void PickVisit_RespectsCooldown()
        {
            var likes = new List<LikeEntry> { E("furn_fire", "look") };
            var last = new Dictionary<string, float> { { "furn_fire", 10f } };
            Assert.IsNull(FurnitureLikeRules.PickVisit(likes, id => true, last, 50f, Table(), new FixedRandom(0.1)));
            Assert.AreEqual("furn_fire", FurnitureLikeRules.PickVisit(likes, id => true, last, 71f, Table(), new FixedRandom(0.1)).item);
        }

        [Test]
        public void PickVisit_RollAboveChanceReturnsNull()
        {
            var likes = new List<LikeEntry> { E("furn_fire", "look") };
            Assert.IsNull(FurnitureLikeRules.PickVisit(likes, id => true, new Dictionary<string, float>(), 100f, Table(), new FixedRandom(0.31)));
        }

        [Test]
        public void Likes_ChecksItem()
        {
            var likes = new List<LikeEntry> { E("furn_fire", "look") };
            Assert.IsTrue(FurnitureLikeRules.Likes(likes, "furn_fire"));
            Assert.IsFalse(FurnitureLikeRules.Likes(likes, "furn_mat"));
            Assert.IsFalse(FurnitureLikeRules.Likes(null, "furn_mat"));
        }

        [Test]
        public void RealTable_LoadsAndHasPlaceholderSets()
        {
            var json = File.ReadAllText("Assets/Resources/Pets/furniture_likes.json");
            var t = JsonUtility.FromJson<FurnitureLikeTable>(json);
            Assert.AreEqual(0.3f, t.visitChance, 1e-4);
            Assert.AreEqual(6f, t.staySeconds, 1e-4);
            Assert.AreEqual(60f, t.cooldownSeconds, 1e-4);
            var nature = FurnitureLikeRules.LikesFor(t, "bowbow", DexRules.Evolved, "自然", 1);
            Assert.AreEqual(2, nature.Count);
            Assert.AreEqual(2, FurnitureLikeRules.LikesFor(t, "bowbow", null, null, 7).Count);
        }
    }
}
