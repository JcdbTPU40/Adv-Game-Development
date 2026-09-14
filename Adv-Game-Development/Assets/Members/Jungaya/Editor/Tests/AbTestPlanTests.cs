using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#49 対象層 10 人を 5 人ずつ BA / AB へ割り付け、合格ラインは 8/10・7/10・2/10。</summary>
    public class AbTestPlanTests
    {
        [Test]
        public void 奇数はAB_偶数はBA()
        {
            Assert.AreEqual(AbOrder.AB, AbTestPlan.OrderOf(1));
            Assert.AreEqual(AbOrder.BA, AbTestPlan.OrderOf(2));
            Assert.AreEqual(AbOrder.AB, AbTestPlan.OrderOf(9));
            Assert.AreEqual(AbOrder.BA, AbTestPlan.OrderOf(10));
        }

        [Test]
        public void 十人なら五人ずつに割り付く()
        {
            Assert.AreEqual(5, AbTestPlan.CountOf(10, AbOrder.AB));
            Assert.AreEqual(5, AbTestPlan.CountOf(10, AbOrder.BA));
        }

        [Test]
        public void 提示順どおりに案が出る()
        {
            Assert.AreEqual(VariantId.A, AbTestPlan.VariantAt(AbOrder.AB, 0));
            Assert.AreEqual(VariantId.B, AbTestPlan.VariantAt(AbOrder.AB, 1));
            Assert.AreEqual(VariantId.B, AbTestPlan.VariantAt(AbOrder.BA, 0));
            Assert.AreEqual(VariantId.A, AbTestPlan.VariantAt(AbOrder.BA, 1));
        }

        [Test]
        public void 十人の合格ラインは八人_七人_二人()
        {
            Assert.AreEqual(8, AbTestPlan.RequiredCount(10, AbTestPlan.FeelRatio));
            Assert.AreEqual(8, AbTestPlan.RequiredCount(10, AbTestPlan.AgainRatio));
            Assert.AreEqual(7, AbTestPlan.RequiredCount(10, AbTestPlan.MajorityRatio));
            Assert.AreEqual(2, AbTestPlan.AllowedCount(10, AbTestPlan.SyncComplaintRatio));
        }

        [Test]
        public void 人数が変わっても同じ割合で判定する()
        {
            // 8 人なら 8割 = 6.4 → 7 人以上、7割 = 5.6 → 6 人以上、2割 = 1.6 → 1 人まで
            Assert.AreEqual(7, AbTestPlan.RequiredCount(8, AbTestPlan.FeelRatio));
            Assert.AreEqual(6, AbTestPlan.RequiredCount(8, AbTestPlan.MajorityRatio));
            Assert.AreEqual(1, AbTestPlan.AllowedCount(8, AbTestPlan.SyncComplaintRatio));
        }

        [Test]
        public void 一人あたり四十五秒と六十秒休憩で百五十秒()
        {
            Assert.AreEqual(150f, AbTestPlan.SecondsPerParticipant(), 0.001f);
        }

        [Test]
        public void 案の違いの数を数える()
        {
            FeedbackVariant a = FeedbackVariant.DefaultA();
            Assert.IsTrue(a.IsSimultaneous);
            Assert.AreEqual(0, FeedbackVariant.CountDifferences(a, a.Clone()));

            var oneChanged = new FeedbackVariant("軌跡だけ遅らせる", 0f, 45f, 0f);
            Assert.AreEqual(1, FeedbackVariant.CountDifferences(a, oneChanged));
            Assert.IsFalse(oneChanged.IsSimultaneous);

            Assert.AreEqual(2, FeedbackVariant.CountDifferences(a, FeedbackVariant.DefaultB()));
        }

        [Test]
        public void 時刻差は出口ごとに引ける()
        {
            var v = new FeedbackVariant("試し", 10f, 45f, 20f);
            Assert.AreEqual(10f, v.DelayMsOf(FeedbackChannel.ThrowSe));
            Assert.AreEqual(45f, v.DelayMsOf(FeedbackChannel.Trail));
            Assert.AreEqual(20f, v.DelayMsOf(FeedbackChannel.Haptic));
            Assert.AreEqual(45f, v.MaxDelayMs);
        }
    }
}
