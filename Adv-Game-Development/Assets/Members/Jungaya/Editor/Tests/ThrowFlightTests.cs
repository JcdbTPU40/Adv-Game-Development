using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Aim.Tests
{
    /// <summary>#60 飛翔時間 0.25〜0.65 秒・強さは時間と太さだけ・スプラインの終点＝着弾目標点。</summary>
    public class ThrowFlightTests
    {
        const float Slow = 180f;
        const float Fast = 720f;

        [TestCase(-1000f)]
        [TestCase(0f)]
        [TestCase(1f)]
        [TestCase(180f)]
        [TestCase(450f)]
        [TestCase(720f)]
        [TestCase(100000f)]
        public void どの強さでも飛翔時間は025から065秒(float strength)
        {
            float s = ThrowFlight.FlightSeconds(ThrowFlight.Strength01(strength, Slow, Fast));
            Assert.GreaterOrEqual(s, 0.25f);
            Assert.LessOrEqual(s, 0.65f);
        }

        [Test]
        public void 弱い振りは065秒_強い振りは025秒()
        {
            Assert.AreEqual(0.65f, ThrowFlight.FlightSeconds(ThrowFlight.Strength01(1f, Slow, Fast)), 1e-5f);
            Assert.AreEqual(0.25f, ThrowFlight.FlightSeconds(ThrowFlight.Strength01(720f, Slow, Fast)), 1e-5f);
        }

        [Test]
        public void 強く振るほど飛翔時間は短く軌跡は太い()
        {
            float weak = ThrowFlight.Strength01(250f, Slow, Fast);
            float strong = ThrowFlight.Strength01(600f, Slow, Fast);
            Assert.Greater(ThrowFlight.FlightSeconds(weak), ThrowFlight.FlightSeconds(strong));
            Assert.Less(ThrowFlight.TrailWidth(weak, 0.06f, 0.22f), ThrowFlight.TrailWidth(strong, 0.06f, 0.22f));
        }

        [Test]
        public void 設定値が範囲外でも025から065秒に収める()
        {
            Assert.AreEqual(0.65f, ThrowFlight.FlightSeconds(0f, 2f, 0.01f), 1e-5f);
            Assert.AreEqual(0.25f, ThrowFlight.FlightSeconds(1f, 2f, 0.01f), 1e-5f);
        }

        [Test]
        public void NaNの強さは最も遅い扱い()
        {
            Assert.AreEqual(0f, ThrowFlight.Strength01(float.NaN, Slow, Fast));
        }

        [TestCase(0f)]
        [TestCase(0.8f)]
        [TestCase(2f)]
        public void 軌跡の終点は弧の高さに依らず着弾目標点と一致する(float arc)
        {
            Vector3 start = new Vector3(0f, 1.4943f, 38.7296f);
            Vector3 end = new Vector3(-3.217f, 0f, 22.113f);
            Vector3 p = ThrowFlight.Evaluate(start, end, arc, 1f);
            Assert.AreEqual(end.x, p.x);
            Assert.AreEqual(end.y, p.y);
            Assert.AreEqual(end.z, p.z);
        }

        [Test]
        public void 時間を超えても着弾目標点を通り過ぎない()
        {
            Vector3 end = new Vector3(2f, 0f, 20f);
            Assert.AreEqual(end, ThrowFlight.Evaluate(Vector3.up, end, 1f, 1.7f));
        }

        [Test]
        public void 始点から始まり中間で弧の高さだけ持ち上がる()
        {
            Vector3 start = new Vector3(0f, 2f, 0f);
            Vector3 end = new Vector3(0f, 0f, 10f);
            Assert.AreEqual(start, ThrowFlight.Evaluate(start, end, 1.5f, 0f));

            Vector3 mid = ThrowFlight.Evaluate(start, end, 1.5f, 0.5f);
            Assert.AreEqual(5f, mid.z, 1e-4f);
            Assert.AreEqual(1f + 1.5f, mid.y, 1e-4f);
        }

        [Test]
        public void 弧の高さは距離に比例して上限で止まる()
        {
            Assert.AreEqual(1.2f, ThrowFlight.ArcHeight(10f, 0.12f, 2f), 1e-5f);
            Assert.AreEqual(2f, ThrowFlight.ArcHeight(30f, 0.12f, 2f), 1e-5f);
        }
    }
}
