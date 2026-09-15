using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#52 静止判定（角速度 30deg/s 未満が 0.5 秒）— 3章のキャリブレーション受理条件と同じ。</summary>
    public class UsbStillnessDetectorTests
    {
        [Test]
        public void 止まって零点五秒で静止()
        {
            var d = new UsbStillnessDetector();
            for (int i = 0; i <= 24; i++) d.Add(10f, 0f, i * 0.02);
            Assert.IsFalse(d.IsStill, "0.48 秒では足りない");
            d.Add(10f, 0f, 0.50);
            Assert.IsTrue(d.IsStill);
        }

        [Test]
        public void 動くと数え直す()
        {
            var d = new UsbStillnessDetector();
            for (int i = 0; i <= 30; i++) d.Add(10f, 0f, i * 0.02);
            Assert.IsTrue(d.IsStill);

            d.Add(11f, 0f, 0.62); // 50deg/s
            Assert.IsFalse(d.IsStill);
            // 0.62 の標本から止まっている → 0.62 + 0.5 = 1.12 で静止
            for (int i = 32; i <= 55; i++) d.Add(11f, 0f, i * 0.02);
            Assert.IsFalse(d.IsStill);
            d.Add(11f, 0f, 1.12);
            Assert.IsTrue(d.IsStill);
        }

        [Test]
        public void ゆっくりの漂いは静止のまま_三百六十度の折り返しも近い角度()
        {
            var d = new UsbStillnessDetector();
            float yaw = 359.8f;
            for (int i = 0; i <= 40; i++)
            {
                d.Add(yaw % 360f, 0f, i * 0.02);
                yaw += 0.2f; // 10deg/s
            }
            Assert.IsTrue(d.IsStill);
            Assert.AreEqual(2.0, UsbStillnessDetector.DeltaAngle(359.0, 1.0), 1e-9);
            Assert.AreEqual(-2.0, UsbStillnessDetector.DeltaAngle(1.0, 359.0), 1e-9);
        }
    }
}
