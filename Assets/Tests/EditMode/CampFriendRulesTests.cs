using System.Collections.Generic;
using BlockPet.Travel;
using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>營地好友的地圖格子（設計文件 5.21；放好就固定）。</summary>
    public class CampFriendRulesTests
    {
        [Test]
        public void 格子滿了回傳負一_解鎖後可以用第十一格()
        {
            Assert.AreEqual(-1, CampFriendRules.NextFreeSlot(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }));
            Assert.AreEqual(10, CampFriendRules.NextFreeSlot(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 11));
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
