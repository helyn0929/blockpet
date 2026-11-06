using System.Collections.Generic;
using BlockPet.Travel;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>營地好友規則（設計文件 5.21 ①；2026-11-06 定：最多 10 個、錯誤提示、固定格子）。</summary>
    public class CampFriendRulesTests
    {
        [Test]
        public void 送出邀請前的檢查()
        {
            var friends = new List<string> { "ROOMB" };
            Assert.AreEqual(CampFriendRules.MsgNotFound, CampFriendRules.Check(null, "ROOMA", friends, false));
            Assert.AreEqual(CampFriendRules.MsgSelf, CampFriendRules.Check("ROOMA", "ROOMA", friends, false));
            Assert.AreEqual(CampFriendRules.MsgAlreadyFriend, CampFriendRules.Check("ROOMB", "ROOMA", friends, false));
            Assert.AreEqual(CampFriendRules.MsgAlreadyInvited, CampFriendRules.Check("ROOMC", "ROOMA", friends, true));
            Assert.IsNull(CampFriendRules.Check("ROOMC", "ROOMA", friends, false));
        }

        [Test]
        public void 好友滿十個不能再加()
        {
            var friends = new List<string>();
            for (int i = 0; i < 10; i++) friends.Add("F" + i);
            Assert.AreEqual(CampFriendRules.MsgFull, CampFriendRules.Check("NEW", "ME", friends, false));
            Assert.AreEqual(-1, CampFriendRules.NextFreeSlot(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
        }

        [Test]
        public void 新好友放在還沒用的最小格子()
        {
            Assert.AreEqual(0, CampFriendRules.NextFreeSlot(new int[0]));
            Assert.AreEqual(3, CampFriendRules.NextFreeSlot(new[] { 0, 1, 2 }));
            Assert.AreEqual(1, CampFriendRules.NextFreeSlot(new[] { 0, 2, 3 })); // 刪掉的格子會再被用
            Assert.AreEqual(0, CampFriendRules.NextFreeSlot(new[] { -1, -1 }));  // 還沒分格子的不算
        }

        [Test]
        public void 代碼整理()
        {
            Assert.AreEqual("K7R2PX", CampFriendRules.NormalizeCode(" k7r-2px "));
            Assert.AreEqual("ABCDEF", CampFriendRules.NormalizeCode("abcdefgh"));
            Assert.AreEqual("", CampFriendRules.NormalizeCode(null));
        }

        [Test]
        public void 刪掉中間的好友_其他人不移動()
        {
            var all = TravelMapGen.Generate("K7R2PX", new[] { 0, 1, 2 });
            var removed = TravelMapGen.Generate("K7R2PX", new[] { 0, 2 });
            Assert.AreEqual(all.friends[0].x, removed.friends[0].x, 1e-9);
            Assert.AreEqual(all.friends[2].x, removed.friends[1].x, 1e-9);
            Assert.AreEqual(all.friends[2].y, removed.friends[1].y, 1e-9);
            // 空出來的格子 1 變成下一塊地（雲霧）
            Assert.AreEqual(all.slots[1].pos.x, removed.next.x, 1e-9);
        }

        [Test]
        public void 照順序的格子和舊的生成一樣()
        {
            var a = TravelMapGen.Generate("M3Q8TA", 6);
            var b = TravelMapGen.Generate("M3Q8TA", new[] { 0, 1, 2, 3, 4, 5 });
            Assert.AreEqual(a.land.Count, b.land.Count);
            Assert.AreEqual(a.trees.Count, b.trees.Count);
            Assert.AreEqual(a.next.x, b.next.x, 1e-9);
        }
    }
}
