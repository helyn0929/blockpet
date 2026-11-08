using System;
using System.Collections.Generic;
using BlockPet.Outfit;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>穿搭（設計文件 5.11 ⑧、第 7 節）：部位、同部位只能一件、限定月份、商店狀態。</summary>
    public class OutfitRulesTests
    {
        const string Json = @"{ ""items"": [
            { ""id"": ""outfit_strawhat"", ""name"": ""小草帽"", ""slot"": ""head"", ""price"": 300 },
            { ""id"": ""outfit_scarf"", ""name"": ""紅色領巾"", ""slot"": ""neck"", ""price"": 220 },
            { ""id"": ""outfit_glasses"", ""name"": ""圓框眼鏡"", ""slot"": ""face"", ""price"": 180 },
            { ""id"": ""outfit_beanie"", ""name"": ""毛線帽"", ""slot"": ""head"", ""price"": 280 },
            { ""id"": ""outfit_bow"", ""name"": ""蝴蝶結"", ""slot"": ""side"", ""price"": 150 },
            { ""id"": ""outfit_santa"", ""name"": ""聖誕帽"", ""slot"": ""head"", ""price"": 350, ""months"": [12], ""lockText"": ""12 月限定"" }
        ] }";

        static List<OutfitItem> Items() => OutfitRules.Parse(Json);
        static OutfitItem Get(string id) => Items().Find(i => i.id == id);

        [Test]
        public void 讀設定檔_每件都有部位和價格()
        {
            var items = Items();
            Assert.AreEqual(6, items.Count);
            Assert.AreEqual("head", Get("outfit_strawhat").slot);
            Assert.AreEqual(300, Get("outfit_strawhat").price);
            Assert.IsTrue(OutfitRules.IsValidSlot("side"));
            Assert.IsFalse(OutfitRules.IsValidSlot("body"), "身體長相不能買");
            Assert.AreEqual(0, OutfitRules.Parse("{ \"items\": [ { \"id\": \"x\", \"slot\": \"body\", \"price\": 1 } ] }").Count, "部位不對的品項不收");
        }

        [Test]
        public void 同一個部位只能穿一件_不同部位可以同時穿()
        {
            var worn = new Dictionary<string, string>();
            worn = OutfitRules.Wear(worn, Get("outfit_strawhat"));
            worn = OutfitRules.Wear(worn, Get("outfit_scarf"));
            worn = OutfitRules.Wear(worn, Get("outfit_glasses"));
            Assert.AreEqual(3, worn.Count);
            worn = OutfitRules.Wear(worn, Get("outfit_beanie"));
            Assert.AreEqual("outfit_beanie", worn["head"], "換上同部位的就把舊的換掉");
            Assert.AreEqual(3, worn.Count);
            Assert.IsTrue(OutfitRules.IsWearing(worn, Get("outfit_beanie")));
            Assert.IsFalse(OutfitRules.IsWearing(worn, Get("outfit_strawhat")));
            worn = OutfitRules.TakeOff(worn, Get("outfit_scarf"));
            Assert.IsFalse(worn.ContainsKey("neck"));
            worn = OutfitRules.TakeOff(worn, Get("outfit_strawhat"));
            Assert.AreEqual("outfit_beanie", worn["head"], "脫下沒穿的那件不會影響正在穿的");
        }

        [Test]
        public void 限定月份才開賣()
        {
            var santa = Get("outfit_santa");
            Assert.IsFalse(OutfitRules.IsOnSale(santa, new DateTime(2026, 11, 8)));
            Assert.IsTrue(OutfitRules.IsOnSale(santa, new DateTime(2026, 12, 1)));
            Assert.IsTrue(OutfitRules.IsOnSale(Get("outfit_bow"), new DateTime(2026, 11, 8)), "沒寫月份就一直賣");
        }

        [Test]
        public void 商店狀態_穿著優先_買過就算不在賣也能穿()
        {
            var nov = new DateTime(2026, 11, 8);
            var none = new Dictionary<string, string>();
            var santa = Get("outfit_santa");
            Assert.AreEqual(OutfitState.NotOnSale, OutfitRules.StateOf(santa, false, none, nov));
            Assert.AreEqual(OutfitState.Owned, OutfitRules.StateOf(santa, true, none, nov));
            var worn = OutfitRules.Wear(none, santa);
            Assert.AreEqual(OutfitState.Wearing, OutfitRules.StateOf(santa, true, worn, nov));
            Assert.AreEqual(OutfitState.Buyable, OutfitRules.StateOf(Get("outfit_bow"), false, none, nov));
        }
    }
}
