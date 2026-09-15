using NUnit.Framework;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// 福の連なり C — Issue #61（仕様書 v8 7章「福の連なりの判定」／付録B B-2・CHAIN.TIMEOUT）
    /// </summary>
    public class FukuChainTests
    {
        [TestCase(0, 1.00f)]
        [TestCase(1, 1.00f)]
        [TestCase(2, 1.00f)]
        [TestCase(3, 1.10f)]
        [TestCase(5, 1.10f)]
        [TestCase(6, 1.20f)]
        [TestCase(9, 1.20f)]
        [TestCase(10, 1.30f)]
        [TestCase(30, 1.30f)]
        public void Cの段で倍率が決まる(int chain, float expected)
        {
            Assert.AreEqual(expected, FukuChain.MultiplierOf(chain));
        }

        [Test]
        public void 救済でCが1ずつ増える()
        {
            var c = new FukuChainCounter();
            Assert.AreEqual(1, c.AddRescue(0f));
            Assert.AreEqual(2, c.AddRescue(1f));
            Assert.AreEqual(3, c.AddRescue(2f));
            Assert.AreEqual(3, c.Max);
        }

        [Test]
        public void 途中命中はCを保ってタイマーだけ戻す()
        {
            var c = new FukuChainCounter();
            c.AddRescue(0f);
            c.KeepAlive(4f);

            Assert.AreEqual(1, c.Count, "途中命中では増やさない");
            Assert.IsFalse(c.TickTimeout(8.9f, 5f), "途中命中から5秒たっていない");
            Assert.IsTrue(c.TickTimeout(9f, 5f), "途中命中から5秒で途切れる");
            Assert.AreEqual(0, c.Count);
        }

        [Test]
        public void 正しい色を5秒当てないと0に戻る()
        {
            var c = new FukuChainCounter();
            c.AddRescue(10f);

            Assert.IsFalse(c.TickTimeout(14.99f, 5f));
            Assert.AreEqual(1, c.Count);
            Assert.IsTrue(c.TickTimeout(15f, 5f));
            Assert.AreEqual(0, c.Count);
        }

        [Test]
        public void Cが0ならタイムアウトは何もしない()
        {
            var c = new FukuChainCounter();
            Assert.IsFalse(c.TickTimeout(100f, 5f));
        }

        [Test]
        public void 誤投擲や黒客への通常弾で0に戻り最大値は残る()
        {
            var c = new FukuChainCounter();
            for (int i = 0; i < 4; i++) c.AddRescue(i);

            Assert.IsTrue(c.Break());
            Assert.AreEqual(0, c.Count);
            Assert.AreEqual(4, c.Max, "リザルト用の最大値は切れても残す");
            Assert.IsFalse(c.Break(), "0 のときにもう一度切っても変化なし");
        }

        [Test]
        public void リセットで最大値も消える()
        {
            var c = new FukuChainCounter();
            c.AddRescue(0f);
            c.Reset();
            Assert.AreEqual(0, c.Count);
            Assert.AreEqual(0, c.Max);
        }
    }
}
