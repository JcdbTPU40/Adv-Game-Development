using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#52 受信の間隔 0.5 秒以上で切断 1 回。途絶の間は 1 回だけ数える。</summary>
    public class UsbLinkMonitorTests
    {
        [Test]
        public void 二十ミリ秒ごとに届いていれば切断なし()
        {
            var m = new UsbLinkMonitor();
            m.Begin(10.0, true);
            for (int i = 1; i <= 500; i++)
            {
                double t = 10.0 + i * 0.02;
                Assert.IsFalse(m.Poll(t));
                m.AddSample(t, out _);
            }
            m.End(20.0);

            Assert.AreEqual(0, m.Disconnects);
            Assert.AreEqual(500, m.Samples);
            Assert.AreEqual(0.02, m.MaxGapSeconds, 1e-9);
            Assert.AreEqual(50.0, m.SampleRateHz.Value, 1e-9);
        }

        [Test]
        public void 行が届いた時点で途絶に気づいても一回()
        {
            var m = new UsbLinkMonitor();
            m.Begin(0.0, true);
            m.AddSample(0.02, out _);
            bool closed = m.AddSample(0.62, out double gap);

            Assert.IsTrue(closed);
            Assert.AreEqual(0.6, gap, 1e-9);
            Assert.AreEqual(1, m.Disconnects);
        }

        [Test]
        public void 途絶中に気づいてから戻っても一回()
        {
            var m = new UsbLinkMonitor();
            m.Begin(0.0, true);
            m.AddSample(0.02, out _);

            Assert.IsFalse(m.Poll(0.30));
            Assert.IsTrue(m.Poll(0.52));
            Assert.IsFalse(m.Poll(1.00));
            Assert.AreEqual(0.02, m.GapStart, 1e-9);
            m.AddSample(1.50, out double gap);

            Assert.AreEqual(1, m.Disconnects);
            Assert.AreEqual(1.48, gap, 1e-9);

            // 戻ったあとの次の途絶は別に数える
            Assert.IsTrue(m.Poll(2.10));
            Assert.AreEqual(2, m.Disconnects);
        }

        [Test]
        public void ちょうど零点五秒は切断()
        {
            var m = new UsbLinkMonitor();
            m.Begin(0.0, true);
            m.AddSample(1.0, out _);
            m.AddSample(1.5, out _);
            Assert.AreEqual(2, m.Disconnects); // 開始から 1.0 秒来なかった分と、1.0→1.5
        }

        [Test]
        public void つながっていた区間で一度も来なければ切断()
        {
            var m = new UsbLinkMonitor();
            m.Begin(0.0, true);
            Assert.IsTrue(m.Poll(0.6));
            m.End(3.0);

            Assert.AreEqual(1, m.Disconnects);
            Assert.AreEqual(3.0, m.MaxGapSeconds, 1e-9);
        }

        [Test]
        public void 未接続で始めた区間は最初の行まで数えない()
        {
            var m = new UsbLinkMonitor();
            m.Begin(0.0, false);
            Assert.IsFalse(m.Poll(5.0));
            m.AddSample(5.0, out _);
            m.AddSample(5.02, out _);
            Assert.AreEqual(0, m.Disconnects);
        }
    }
}
