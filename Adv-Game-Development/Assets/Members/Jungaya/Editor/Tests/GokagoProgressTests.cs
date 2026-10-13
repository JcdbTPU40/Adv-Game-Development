using NUnit.Framework;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// ご加護専用の進捗 G — Issue #29（仕様書 v8 7章「ご加護タイム」／付録B BLESSING.G）
    /// </summary>
    public class GokagoProgressTests
    {
        [Test]
        public void 付録Bの値()
        {
            Assert.AreEqual(10, GokagoProgress.DefaultThreshold);
            Assert.AreEqual(12f, GokagoProgress.DefaultDurationSeconds);
            Assert.AreEqual(1.25f, GokagoProgress.DefaultMultiplier);
            Assert.AreEqual(0.8f, GokagoProgress.CutInSeconds);
        }

        [Test]
        public void 通常弾の救済10人目で発動してGは0にもどる()
        {
            var g = new GokagoProgress();
            for (int i = 1; i <= 9; i++)
            {
                Assert.IsFalse(g.AddRescue(false), $"{i}人目");
                Assert.AreEqual(i, g.Count);
            }
            Assert.IsTrue(g.AddRescue(false));
            Assert.AreEqual(0, g.Count);
        }

        [Test]
        public void ご加護中の救済ではGをためず再発動も予約しない()
        {
            var g = new GokagoProgress();
            for (int i = 0; i < 15; i++)
                Assert.IsFalse(g.AddRescue(true));
            Assert.AreEqual(0, g.Count);

            // 終わったあとは G=0 から 10人で再発動
            for (int i = 0; i < 9; i++) Assert.IsFalse(g.AddRescue(false));
            Assert.IsTrue(g.AddRescue(false));
        }

        [Test]
        public void 切れたら0から数えなおす()
        {
            var g = new GokagoProgress();
            for (int i = 0; i < 9; i++) g.AddRescue(false);
            g.Break();
            Assert.AreEqual(0, g.Count);
            Assert.IsFalse(g.AddRescue(false));
            Assert.AreEqual(1, g.Count);
        }

        [Test]
        public void しきい値を変えられる()
        {
            var g = new GokagoProgress();
            Assert.IsFalse(g.AddRescue(false, 2));
            Assert.IsTrue(g.AddRescue(false, 2));
        }
    }
}
