using System.Collections.Generic;
using BlockPet.Feed;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>進化（設計文件 5.17 ②④⑤，2026-11-14）。</summary>
    public class EvolutionRulesTests
    {
        [Test]
        public void Palette_CountsFirst100FromStart()
        {
            var cats = new List<string>();
            for (int i = 0; i < 110; i++) cats.Add(i < 5 ? "城市" : i % 2 == 0 ? "自然" : null);
            var p = EvolutionRules.Palette(cats, 5);
            int sum = 0;
            foreach (var v in p) sum += v;
            Assert.AreEqual(100, sum);
            Assert.AreEqual(0, p[2], "start 之前的城市不算");
            Assert.AreEqual(50, p[0], "沒辨識的照片算美食");
            Assert.AreEqual(50, p[1]);
        }

        [Test]
        public void LastFed_IsTheLastCountedPhoto()
        {
            var cats = new List<string> { "美食", "自然", "寵物" };
            Assert.AreEqual(4, EvolutionRules.LastFedCategory(cats, 0));
            Assert.AreEqual(-1, EvolutionRules.LastFedCategory(new List<string>(), 0));
        }

        [Test]
        public void Decide_ExtremeWhenMainOver50()
        {
            var r = EvolutionRules.Decide(new[] { 60, 10, 10, 10, 10 }, 2);
            Assert.AreEqual(DexRules.Extreme, r.type);
            Assert.AreEqual(0, r.main);
            Assert.AreEqual(1, r.sub);
        }

        [Test]
        public void Decide_RainbowWhenAllPresentAndMainUnder30()
        {
            var r = EvolutionRules.Decide(new[] { 20, 20, 20, 20, 20 }, 3);
            Assert.AreEqual(DexRules.Rainbow, r.type);
            Assert.AreEqual(3, r.main, "一樣多：取最後餵的那一類");
        }

        [Test]
        public void Decide_TieUsesLastFed()
        {
            var r = EvolutionRules.Decide(new[] { 40, 40, 10, 5, 5 }, 1);
            Assert.AreEqual(1, r.main);
            Assert.AreEqual(0, r.sub);
        }

        [Test]
        public void Decide_EvolvedOtherwise()
        {
            var r = EvolutionRules.Decide(new[] { 45, 40, 15, 0, 0 }, 0);
            Assert.AreEqual(DexRules.Evolved, r.type);
            Assert.AreEqual(0, r.main);
            Assert.AreEqual(1, r.sub);
        }

        [Test]
        public void Decide_SameInputSameResult()
        {
            var a = EvolutionRules.Decide(new[] { 30, 30, 20, 10, 10 }, 1);
            var b = EvolutionRules.Decide(new[] { 30, 30, 20, 10, 10 }, 1);
            Assert.AreEqual(a.type, b.type);
            Assert.AreEqual(a.main, b.main);
            Assert.AreEqual(a.sub, b.sub);
        }

        [Test]
        public void Preview_Thresholds()
        {
            Assert.IsFalse(EvolutionRules.ShowPreview(79));
            Assert.IsTrue(EvolutionRules.ShowPreview(80));
            Assert.IsTrue(EvolutionRules.ShowPreview(99));
            Assert.IsFalse(EvolutionRules.ShowPreview(100));
            Assert.IsFalse(EvolutionRules.CanEvolve(99));
            Assert.IsTrue(EvolutionRules.CanEvolve(100));
        }

        [Test]
        public void PreviewLine_Texts()
        {
            Assert.AreEqual("全部都是美食耶！好像要變成極致型…", EvolutionRules.PreviewLine(new[] { 70, 5, 5, 0, 0 }, 0));
            Assert.AreEqual("顏色好平均，說不定會變彩虹…", EvolutionRules.PreviewLine(new[] { 16, 16, 16, 16, 16 }, 0));
            Assert.AreEqual("好像會變成綠色的…", EvolutionRules.PreviewLine(new[] { 20, 35, 25, 0, 0 }, 1));
        }

        [Test]
        public void ResultTitle_AndColorWords()
        {
            Assert.AreEqual("變成極致型了！", EvolutionRules.ResultTitle(DexRules.Extreme));
            Assert.AreEqual("變成彩虹型了！", EvolutionRules.ResultTitle(DexRules.Rainbow));
            Assert.AreEqual("變成進化型了！", EvolutionRules.ResultTitle(DexRules.Evolved));
            Assert.AreEqual("橘色", EvolutionRules.ColorWord(0));
            Assert.AreEqual("粉紫色", EvolutionRules.ColorWord(3));
            Assert.AreEqual("黃色", EvolutionRules.ColorWord(4));
        }
    }
}
