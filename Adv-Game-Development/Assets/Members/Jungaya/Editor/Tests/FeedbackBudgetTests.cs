using NUnit.Framework;

namespace Toufuku.Feedback.Tests
{
    /// <summary>#64 1 投のフィードバック予算（スイング 80ms／命中 50ms／救済 250ms）。</summary>
    public class FeedbackBudgetTests
    {
        [Test]
        public void 予算の値()
        {
            Assert.AreEqual(0.080, FeedbackBudget.LimitOf(FeedbackCategory.Swing), 1e-9);
            Assert.AreEqual(0.050, FeedbackBudget.LimitOf(FeedbackCategory.Hit), 1e-9);
            Assert.AreEqual(0.250, FeedbackBudget.LimitOf(FeedbackCategory.Rescue), 1e-9);
        }

        [Test]
        public void 境界ちょうどは予算内_超えたら超過を数える()
        {
            var b = new FeedbackBudget();
            Assert.IsTrue(b.Record(FeedbackCategory.Hit, 0.050));
            Assert.IsFalse(b.Record(FeedbackCategory.Hit, 0.051));
            Assert.AreEqual(2, b.SampleCount(FeedbackCategory.Hit));
            Assert.AreEqual(1, b.ViolationCount(FeedbackCategory.Hit));
            Assert.AreEqual(0, b.ViolationCount(FeedbackCategory.Swing));
        }

        [Test]
        public void 区分ごとに直近と最大を持つ()
        {
            var b = new FeedbackBudget();
            b.Record(FeedbackCategory.Swing, 0.030);
            b.Record(FeedbackCategory.Swing, 0.010);
            b.Record(FeedbackCategory.Rescue, 0.200);

            Assert.AreEqual(0.010, b.LastSeconds(FeedbackCategory.Swing), 1e-9);
            Assert.AreEqual(0.030, b.MaxSeconds(FeedbackCategory.Swing), 1e-9);
            Assert.AreEqual(0.200, b.MaxSeconds(FeedbackCategory.Rescue), 1e-9);
            Assert.AreEqual(0, b.SampleCount(FeedbackCategory.Hit));
        }

        [Test]
        public void 負の遅延は0とみなす()
        {
            var b = new FeedbackBudget();
            Assert.IsTrue(b.Record(FeedbackCategory.Hit, -0.004));
            Assert.AreEqual(0.0, b.LastSeconds(FeedbackCategory.Hit));
        }

        [Test]
        public void Resetで全区分を消す()
        {
            var b = new FeedbackBudget();
            b.Record(FeedbackCategory.Swing, 0.5);
            b.Reset();
            Assert.AreEqual(0, b.SampleCount(FeedbackCategory.Swing));
            Assert.AreEqual(0, b.ViolationCount(FeedbackCategory.Swing));
            Assert.AreEqual(0.0, b.MaxSeconds(FeedbackCategory.Swing));
        }
    }
}
