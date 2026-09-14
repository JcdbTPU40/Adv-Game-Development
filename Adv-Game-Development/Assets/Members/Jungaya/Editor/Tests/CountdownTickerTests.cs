using System.Collections.Generic;
using NUnit.Framework;

namespace Toufuku.Feedback.Tests
{
    /// <summary>#64 終了カウントダウンは残り 10〜1 秒の各 1 回だけ鳴る。</summary>
    public class CountdownTickerTests
    {
        [Test]
        public void 残り10秒から1秒まで各1回ずつ鳴る()
        {
            var ticker = new CountdownTicker { From = 10 };
            var ticks = new List<int>();
            for (float remaining = 12f; remaining > -0.5f; remaining -= 1f / 60f)
            {
                int n = ticker.Advance(remaining);
                if (n != 0) ticks.Add(n);
            }
            CollectionAssert.AreEqual(new[] { 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }, ticks);
        }

        [Test]
        public void 残りがN秒以下になった最初のフレームで鳴る()
        {
            var ticker = new CountdownTicker { From = 3 };
            Assert.AreEqual(0, ticker.Advance(3.01f));
            Assert.AreEqual(3, ticker.Advance(3.0f));
            Assert.AreEqual(0, ticker.Advance(2.5f));
            Assert.AreEqual(2, ticker.Advance(1.99f));
        }

        [Test]
        public void フレームが飛んだら最新の数字だけ鳴らす()
        {
            var ticker = new CountdownTicker { From = 10 };
            Assert.AreEqual(8, ticker.Advance(7.9f));
            Assert.AreEqual(5, ticker.Advance(4.2f));
        }

        [Test]
        public void 残り0以下では鳴らさない_Resetで最初から()
        {
            var ticker = new CountdownTicker { From = 10 };
            Assert.AreEqual(0, ticker.Advance(0f));
            Assert.AreEqual(1, ticker.Advance(0.5f));
            Assert.AreEqual(0, ticker.Advance(0.2f));

            ticker.Reset();
            Assert.AreEqual(10, ticker.Advance(9.99f));
        }
    }
}
