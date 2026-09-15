using NUnit.Framework;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// ランクC停滞タイマー — Issue #65（仕様書 v8 11章「競技開始の0:30.000から計時し、B以上へ上がった瞬間に0へ戻す。
    /// 学習中、ポーズ、リザルト、通信復帰中は加算しない」）
    /// 学習中・ポーズ・リザルト・通信復帰中を引く部分は GameSession.CompetitionDeltaSeconds（SessionClockTests で確認）。
    /// </summary>
    public class RankCStallTimerTests
    {
        [Test]
        public void ランクCのままの秒だけ数える()
        {
            var timer = new RankCStallTimer();
            timer.Tick(10.0, ShrineRank.C);
            timer.Tick(5.0, ShrineRank.C);
            Assert.AreEqual(15.0, timer.Seconds, 1e-9);
        }

        [Test]
        public void 数えてよい秒が0なら足さない()
        {
            var timer = new RankCStallTimer();
            timer.Tick(3.0, ShrineRank.C);
            timer.Tick(0.0, ShrineRank.C);
            timer.Tick(-1.0, ShrineRank.C);
            Assert.AreEqual(3.0, timer.Seconds, 1e-9);
        }

        [Test]
        public void B以上に上がった瞬間に0へもどりCに落ちたら0から数えなおす()
        {
            var timer = new RankCStallTimer();
            timer.Tick(20.0, ShrineRank.C);

            timer.OnRankChanged(ShrineRank.B);
            Assert.AreEqual(0.0, timer.Seconds);
            timer.Tick(5.0, ShrineRank.B);
            Assert.AreEqual(0.0, timer.Seconds);

            timer.OnRankChanged(ShrineRank.C);
            timer.Tick(3.0, ShrineRank.C);
            Assert.AreEqual(3.0, timer.Seconds, 1e-9);
        }

        [Test]
        public void 停滞は30秒でとみなす()
        {
            var timer = new RankCStallTimer();
            timer.Tick(29.999, ShrineRank.C);
            Assert.IsFalse(timer.IsStalled());
            timer.Tick(0.001, ShrineRank.C);
            Assert.IsTrue(timer.IsStalled());
        }

        [Test]
        public void リセットで0にもどる()
        {
            var timer = new RankCStallTimer();
            timer.Tick(40.0, ShrineRank.C);
            timer.Reset();
            Assert.AreEqual(0.0, timer.Seconds);
            Assert.IsFalse(timer.IsStalled());
        }
    }
}
