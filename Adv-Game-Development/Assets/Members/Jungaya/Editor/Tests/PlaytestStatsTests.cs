using NUnit.Framework;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#63 分位点（Excel PERCENTILE.INC と同じ線形補間）。</summary>
    public class PlaytestStatsTests
    {
        [Test]
        public void 五つの値の中央値_p75_p95()
        {
            double[] v = { 5, 1, 4, 2, 3 };
            Assert.AreEqual(3.0, PlaytestStats.Percentile(v, 0.50).Value, 1e-9);
            Assert.AreEqual(4.0, PlaytestStats.Percentile(v, 0.75).Value, 1e-9);
            Assert.AreEqual(4.8, PlaytestStats.Percentile(v, 0.95).Value, 1e-9);
            Assert.AreEqual(1.0, PlaytestStats.Percentile(v, 0.0).Value, 1e-9);
            Assert.AreEqual(5.0, PlaytestStats.Percentile(v, 1.0).Value, 1e-9);
        }

        [Test]
        public void 二つの値は線形補間()
        {
            double[] v = { 10, 20 };
            Assert.AreEqual(15.0, PlaytestStats.Median(v).Value, 1e-9);
            Assert.AreEqual(19.5, PlaytestStats.Percentile(v, 0.95).Value, 1e-9);
        }

        [Test]
        public void 値が無ければnull_NaNは除く_1つならその値()
        {
            Assert.IsNull(PlaytestStats.Percentile(new double[0], 0.5));
            Assert.IsNull(PlaytestStats.Percentile(new[] { double.NaN }, 0.5));
            Assert.AreEqual(7.0, PlaytestStats.Percentile(new[] { double.NaN, 7.0 }, 0.95).Value, 1e-9);
        }
    }
}
