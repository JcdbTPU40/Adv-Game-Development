using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#53 8章「大負荷ウェーブ候補の処理負荷比較」の表と同じ値が出る。</summary>
    public class WaveLoadArithmeticTests
    {
        [TestCase(3, 13, 0.62, 0.68, 1.47)]
        [TestCase(4, 14, 0.67, 0.73, 1.37)]
        [TestCase(5, 15, 0.72, 0.78, 1.28)]
        public void 仕様書の表と一致する(int added, int totalCap, double rescues, double hits, double cycle)
        {
            WaveCandidate c = WaveLoadArithmetic.Of(added);

            Assert.AreEqual(totalCap, c.TotalCap);
            Assert.AreEqual(rescues, c.RequiredRescuesPerSecond, 0.005);
            Assert.AreEqual(hits, c.RequiredHitsPerSecond, 0.005);
            Assert.AreEqual(cycle, c.AllowedCycleSeconds, 0.005);
        }

        [Test]
        public void 人数が増えるほど許容される周期は短くなる()
        {
            var list = WaveLoadArithmetic.FinalCandidates();

            Assert.AreEqual(3, list.Count);
            Assert.Greater(list[0].AllowedCycleSeconds, list[1].AllowedCycleSeconds);
            Assert.Greater(list[1].AllowedCycleSeconds, list[2].AllowedCycleSeconds);
        }
    }
}
