using NUnit.Framework;

namespace BlockPet.Tests
{
    /// <summary>範例測試：一定會通過，用來確認 scripts/run-tests.sh 整條流程能動。</summary>
    public class SampleTest
    {
        [Test]
        public void OnePlusOneIsTwo()
        {
            Assert.AreEqual(2, 1 + 1);
        }
    }
}
