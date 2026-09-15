using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#52 コントローラの時計を、近くの受信行の「受信 − 送信」の最小値で Unity の時計へ合わせる。</summary>
    public class UsbClockAlignerTests
    {
        [Test]
        public void いちばん待たされなかった行の差を時計の差にする()
        {
            var a = new UsbClockAligner();
            // コントローラ時刻 0.00〜 を 20ms ごと、Unity 側の原点差 100 秒、読み込み待ち 1〜15ms
            double[] waits = { 0.015, 0.001, 0.008, 0.012, 0.004 };
            for (int i = 0; i < waits.Length; i++)
                a.Add(100.0 + i * 0.02 + waits[i], i * 0.02);

            Assert.AreEqual(100.001, a.OffsetAt(100.05).Value, 1e-9);
            // 入力時刻 0.06 → Unity では 100.061（待ち 1ms を 0 と置いた推定）
            Assert.AreEqual(100.061, a.ToUnity(0.06, 100.072).Value, 1e-9);
        }

        [Test]
        public void 時計のずれがあっても前後の窓の中だけで合わせる()
        {
            var a = new UsbClockAligner();
            // 最初の 10 秒は差 100.000、後ろの 10 秒は差 100.010（水晶のずれを誇張）
            for (int i = 0; i <= 500; i++) a.Add(100.0 + i * 0.02, i * 0.02);
            for (int i = 1000; i <= 1500; i++) a.Add(100.010 + i * 0.02, i * 0.02);

            Assert.AreEqual(100.000, a.OffsetAt(102.0, 5.0).Value, 1e-9);
            Assert.AreEqual(100.010, a.OffsetAt(128.0, 5.0).Value, 1e-9);
        }

        [Test]
        public void 窓に行が無い_入力時刻が無いなら欠測()
        {
            var a = new UsbClockAligner();
            Assert.IsNull(a.OffsetAt(10.0));

            a.Add(100.0, double.NaN);
            Assert.AreEqual(0, a.Count);

            a.Add(100.0, 0.0);
            Assert.IsNull(a.OffsetAt(200.0, 5.0));
            Assert.IsNull(a.ToUnity(double.NaN, 100.0));
        }

        [Test]
        public void 逆行した受信は捨てる()
        {
            var a = new UsbClockAligner();
            a.Add(100.0, 0.0);
            a.Add(99.0, 0.0);
            Assert.AreEqual(1, a.Count);
        }
    }
}
