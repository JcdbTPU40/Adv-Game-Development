using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#49 45 秒 → 60 秒休憩 → 45 秒 → 聞き取り。フレームが飛んでもフェーズは 1 つずつ進む。</summary>
    public class AbSessionClockTests
    {
        AbSessionClock _clock;

        [SetUp]
        public void SetUp()
        {
            _clock = new AbSessionClock { TrialSeconds = 45.0, RestSeconds = 60.0 };
        }

        [Test]
        public void 開始前は準備で試技ではない()
        {
            Assert.AreEqual(AbPhase.Ready, _clock.Phase);
            Assert.IsFalse(_clock.IsTrial);
            Assert.AreEqual(-1, _clock.TrialIndex);
        }

        [Test]
        public void 四十五秒で休憩_さらに六十秒で二案目_四十五秒で聞き取り()
        {
            _clock.Begin(100.0);
            Assert.AreEqual(AbPhase.FirstTrial, _clock.Phase);
            Assert.AreEqual(0, _clock.TrialIndex);

            Assert.IsFalse(_clock.Advance(144.9));
            Assert.AreEqual(AbPhase.FirstTrial, _clock.Phase);

            Assert.IsTrue(_clock.Advance(145.0));
            Assert.AreEqual(AbPhase.Rest, _clock.Phase);
            Assert.IsFalse(_clock.IsTrial);

            Assert.IsTrue(_clock.Advance(205.0));
            Assert.AreEqual(AbPhase.SecondTrial, _clock.Phase);
            Assert.AreEqual(1, _clock.TrialIndex);

            Assert.IsTrue(_clock.Advance(250.0));
            Assert.AreEqual(AbPhase.Survey, _clock.Phase);

            // 聞き取りは時間で終わらない
            Assert.IsFalse(_clock.Advance(100000.0));
            Assert.AreEqual(AbPhase.Survey, _clock.Phase);
        }

        [Test]
        public void 残り秒数はフェーズの長さから減る()
        {
            _clock.Begin(0.0);
            Assert.AreEqual(45.0, _clock.RemainingSeconds(0.0), 0.001);
            Assert.AreEqual(15.0, _clock.RemainingSeconds(30.0), 0.001);
            Assert.AreEqual(0.0, _clock.RemainingSeconds(60.0), 0.001);
        }

        [Test]
        public void 長いフレーム落ちでも一つずつ進んで聞き取りで止まる()
        {
            _clock.Begin(0.0);
            Assert.IsTrue(_clock.Advance(1000.0));
            Assert.AreEqual(AbPhase.Survey, _clock.Phase);
        }

        [Test]
        public void 飛ばすと次のフェーズへ移り時間も測り直す()
        {
            _clock.Begin(0.0);
            Assert.IsTrue(_clock.Skip(10.0));
            Assert.AreEqual(AbPhase.Rest, _clock.Phase);
            Assert.AreEqual(60.0, _clock.RemainingSeconds(10.0), 0.001);

            Assert.IsFalse(_clock.Advance(69.0));
            Assert.IsTrue(_clock.Advance(70.0));
            Assert.AreEqual(AbPhase.SecondTrial, _clock.Phase);
        }

        [Test]
        public void 準備で飛ばすと一案目が始まる()
        {
            Assert.IsTrue(_clock.Skip(5.0));
            Assert.AreEqual(AbPhase.FirstTrial, _clock.Phase);
            Assert.AreEqual(45.0, _clock.RemainingSeconds(5.0), 0.001);
        }

        [Test]
        public void 記録し終えたら終了_次の参加者で準備に戻る()
        {
            _clock.Begin(0.0);
            _clock.Advance(1000.0);
            _clock.FinishSurvey();
            Assert.AreEqual(AbPhase.Done, _clock.Phase);
            Assert.IsFalse(_clock.Skip(1001.0));

            _clock.Reset();
            Assert.AreEqual(AbPhase.Ready, _clock.Phase);
        }
    }
}
