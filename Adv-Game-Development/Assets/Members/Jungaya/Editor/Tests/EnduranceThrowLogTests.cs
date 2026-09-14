using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#53 1 分ごとの投数と命中率、実操作周期の中央値と p75。</summary>
    public class EnduranceThrowLogTests
    {
        [Test]
        public void 投数は一分ごとに数える()
        {
            var log = new EnduranceThrowLog();
            foreach (double t in new[] { 0.5, 59.9, 60.0, 119.0, 179.9 }) log.AddFire(t);

            Assert.AreEqual(5, log.Throws);
            Assert.AreEqual(2, log.ThrowsIn(0));
            Assert.AreEqual(2, log.ThrowsIn(1), "60.0 秒ちょうどは 2 分目");
            Assert.AreEqual(1, log.ThrowsIn(2));
        }

        [Test]
        public void 着弾は発射した分で数える()
        {
            var log = new EnduranceThrowLog();
            log.AddLanding(60.3, 0.5, true);   // 59.8 秒に投げた → 1 分目
            log.AddLanding(180.4, 0.6, false); // 179.8 秒に投げた → 3 分目
            log.AddLanding(90.0, 0.4, true);

            Assert.AreEqual(1, log.LandingsIn(0));
            Assert.AreEqual(1, log.HitsIn(0));
            Assert.AreEqual(1, log.LandingsIn(1));
            Assert.AreEqual(1, log.LandingsIn(2));
            Assert.AreEqual(0, log.HitsIn(2));
            Assert.AreEqual(3, log.Landings);
            Assert.AreEqual(2, log.Hits);
        }

        [Test]
        public void 試技の前に投げた弾は数えない()
        {
            var log = new EnduranceThrowLog();
            log.AddLanding(0.2, 0.5, true);
            log.AddFire(-0.1);

            Assert.AreEqual(0, log.Landings);
            Assert.AreEqual(0, log.Throws);
        }

        [Test]
        public void 実操作周期は発射間隔の中央値とp75()
        {
            var log = new EnduranceThrowLog();
            // 間隔 1.0 / 1.1 / 1.3 / 1.4（入れる順はばらばらでよい）
            foreach (double t in new[] { 3.4, 0.0, 4.8, 1.0, 2.1 }) log.AddFire(t);

            Assert.AreEqual(4, log.Intervals().Count);
            Assert.AreEqual(1.2, log.CycleMedian.Value, 1e-9);
            Assert.AreEqual(1.325, log.CycleP75.Value, 1e-9, "Excel PERCENTILE.INC と同じ線形補間");
        }

        [Test]
        public void 発射が一回以下なら周期は欠測()
        {
            var log = new EnduranceThrowLog();
            Assert.IsNull(log.CycleMedian);

            log.AddFire(3.0);
            Assert.IsNull(log.CycleMedian);
            Assert.IsNull(log.CycleP75);
        }

        [Test]
        public void クリアで空に戻る()
        {
            var log = new EnduranceThrowLog();
            log.AddFire(1.0);
            log.AddFire(2.0);
            log.AddRejected();
            log.AddLanding(2.5, 0.5, true);

            log.Clear();
            Assert.AreEqual(0, log.Throws);
            Assert.AreEqual(0, log.Landings);
            Assert.AreEqual(0, log.Rejected);
        }
    }
}
