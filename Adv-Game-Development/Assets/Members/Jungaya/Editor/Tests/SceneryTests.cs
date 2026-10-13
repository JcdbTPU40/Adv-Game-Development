using NUnit.Framework;

namespace Toufuku.Scenery.Tests
{
    /// <summary>
    /// 日周表現のキーの探し方と、篝火3つの灯す数（企画書 v8 8章「時間」「進行の可視化」）
    /// </summary>
    public class SceneryTests
    {
        const float Eps = 0.0001f;
        static readonly float[] Keys = { 0f, 0.2f, 0.42f, 0.68f };   // 朝・昼・夕・夜

        [Test]
        public void キーちょうどの時刻は_そのキーから始まる()
        {
            DayCycleController.FindSegment(Keys, 0.2f, out int prev, out int next, out float s);
            Assert.AreEqual(1, prev);
            Assert.AreEqual(2, next);
            Assert.AreEqual(0f, s, Eps);
        }

        [Test]
        public void キーのあいだは_直線の割合になる()
        {
            DayCycleController.FindSegment(Keys, 0.31f, out int prev, out int next, out float s);
            Assert.AreEqual(1, prev);
            Assert.AreEqual(2, next);
            Assert.AreEqual(0.5f, s, Eps);
        }

        [Test]
        public void 夜のあとは_翌月の朝へつながる()
        {
            DayCycleController.FindSegment(Keys, 0.84f, out int prev, out int next, out float s);
            Assert.AreEqual(3, prev);
            Assert.AreEqual(0, next);
            Assert.AreEqual(0.5f, s, Eps);
        }

        [Test]
        public void 最初のキーが0でないときは_それより前を夜の続きとして扱う()
        {
            float[] keys = { 0.1f, 0.5f };
            DayCycleController.FindSegment(keys, 0.05f, out int prev, out int next, out float s);
            Assert.AreEqual(1, prev);
            Assert.AreEqual(0, next);
            Assert.AreEqual(0.55f / 0.6f, s, Eps);
        }

        [Test]
        public void キーが1つだけでも_止まらない()
        {
            DayCycleController.FindSegment(new[] { 0.3f }, 0.7f, out int prev, out int next, out float s);
            Assert.AreEqual(0, prev);
            Assert.AreEqual(0, next);
            Assert.AreEqual(0.4f, s, Eps);
        }

        [TestCase(false, 1, 0)]
        [TestCase(true, 1, 1)]
        [TestCase(true, 2, 2)]
        [TestCase(true, 3, 3)]
        [TestCase(true, 4, 3)]
        public void 篝火は_月が変わるごとに1つずつ灯る(bool started, int month, int expected)
        {
            Assert.AreEqual(expected, BonfireProgress.LitCountFor(started, month, 3));
        }
    }
}
