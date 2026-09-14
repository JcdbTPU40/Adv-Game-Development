using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#50 実連投間隔を別に記録する。最小値がクールダウンより短ければ、その組は必ず欠落する。</summary>
    public class IntervalStatsTests
    {
        static IntervalStats Of(params double[] values)
        {
            var stats = new IntervalStats();
            foreach (double value in values) stats.Add(value);
            return stats;
        }

        [Test]
        public void 値が無ければすべてゼロ()
        {
            var stats = new IntervalStats();
            Assert.AreEqual(0, stats.Count);
            Assert.AreEqual(0.0, stats.Min, 1e-9);
            Assert.AreEqual(0.0, stats.Median, 1e-9);
            Assert.AreEqual("-", stats.Describe());
        }

        [Test]
        public void 入れた順に関係なく最小と中央が出る()
        {
            IntervalStats stats = Of(0.72, 0.41, 0.95, 0.58, 0.63);

            Assert.AreEqual(5, stats.Count);
            Assert.AreEqual(0.41, stats.Min, 1e-9);
            Assert.AreEqual(0.63, stats.Median, 1e-9);
        }

        [Test]
        public void 偶数個の中央値は両隣の真ん中()
        {
            IntervalStats stats = Of(0.40, 0.60);
            Assert.AreEqual(0.50, stats.Median, 1e-9);
        }

        [Test]
        public void p75は上から四分の一の位置()
        {
            IntervalStats stats = Of(0.1, 0.2, 0.3, 0.4, 0.5);
            // 位置 = 0.75 × 4 = 3.0 → 4 番目の値
            Assert.AreEqual(0.4, stats.P75, 1e-9);
        }

        [Test]
        public void ゼロ以下の間隔は入れない()
        {
            IntervalStats stats = Of(0.5, 0.0, -1.0);
            Assert.AreEqual(1, stats.Count);
        }

        [Test]
        public void クールダウン以上だった件数を数えられる()
        {
            IntervalStats stats = Of(0.42, 0.51, 0.55, 0.68, 0.72);

            // 0.60 秒のクールダウンなら 5 組のうち 2 組しか通らない＝残り 3 組は欠落する
            Assert.AreEqual(2, stats.CountAtLeast(0.60));
            Assert.AreEqual(5, stats.CountAtLeast(0.40));
            Assert.AreEqual(0, stats.CountAtLeast(1.00));
        }

        [Test]
        public void 追加後も並べ直される()
        {
            var stats = new IntervalStats();
            stats.Add(0.80);
            Assert.AreEqual(0.80, stats.Min, 1e-9);

            stats.Add(0.30);
            Assert.AreEqual(0.30, stats.Min, 1e-9);
        }

        [Test]
        public void 初期化すると空に戻る()
        {
            IntervalStats stats = Of(0.5, 0.6);
            stats.Clear();
            Assert.AreEqual(0, stats.Count);
            Assert.AreEqual(0.0, stats.Median, 1e-9);
        }
    }
}
