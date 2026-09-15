using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#52 平均 fps（フレーム数 ÷ 合計秒）と 1% low（遅い 1% の平均）。#45 と同じ定義。</summary>
    public class FrameTimeStatsTests
    {
        [Test]
        public void 一定の六十fpsは平均も一パーセントローも六十()
        {
            var s = new FrameTimeStats();
            for (int i = 0; i < 600; i++) s.Add(1.0 / 60.0);

            Assert.AreEqual(600, s.Frames);
            Assert.AreEqual(60.0, s.AverageFps.Value, 1e-6);
            Assert.AreEqual(60.0, s.OnePercentLowFps.Value, 1e-6);
        }

        [Test]
        public void 百フレームに一回の遅いフレームが一パーセントローになる()
        {
            var s = new FrameTimeStats();
            for (int i = 0; i < 99; i++) s.Add(0.010);
            s.Add(0.100);

            Assert.AreEqual(10.0, s.OnePercentLowFps.Value, 1e-6);
            Assert.AreEqual(100.0, s.WorstFrameMs.Value, 1e-6);
            // 平均は 1 フレームごとの fps の平均ではなく、フレーム数 ÷ 合計秒（1.09 秒で 100 フレーム）
            Assert.AreEqual(100.0 / 1.09, s.AverageFps.Value, 1e-6);
        }

        [Test]
        public void 一パーセントのフレーム数は切り上げで最低一()
        {
            Assert.AreEqual(0, FrameTimeStats.WorstCount(0));
            Assert.AreEqual(1, FrameTimeStats.WorstCount(1));
            Assert.AreEqual(1, FrameTimeStats.WorstCount(100));
            Assert.AreEqual(2, FrameTimeStats.WorstCount(101));
            Assert.AreEqual(3, FrameTimeStats.WorstCount(300));
            Assert.AreEqual(36, FrameTimeStats.WorstCount(3600));
        }

        [Test]
        public void 百一フレームなら遅い二フレームの平均()
        {
            var s = new FrameTimeStats();
            for (int i = 0; i < 99; i++) s.Add(0.010);
            s.Add(0.020);
            s.Add(0.030);

            Assert.AreEqual(40.0, s.OnePercentLowFps.Value, 1e-6);
        }

        [Test]
        public void データが無ければ欠測で不正な値は無視する()
        {
            var s = new FrameTimeStats();
            s.Add(0.0);
            s.Add(-1.0);
            s.Add(double.NaN);

            Assert.AreEqual(0, s.Frames);
            Assert.IsNull(s.AverageFps);
            Assert.IsNull(s.OnePercentLowFps);
            Assert.IsNull(s.WorstFrameMs);
        }
    }
}
