using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#53 準備 → 3 分 → 着弾待ち → 記録。途中終了は完走に数えない。</summary>
    public class EnduranceTrialClockTests
    {
        [Test]
        public void 開始前は準備で経過はゼロ()
        {
            var clock = new EnduranceTrialClock();

            Assert.AreEqual(EndurancePhase.Ready, clock.Phase);
            Assert.AreEqual(0.0, clock.TrialElapsed(50.0), 1e-9);
            Assert.IsFalse(clock.Completed);
            Assert.IsFalse(clock.Advance(1000.0), "開始するまでは時間で進まない");
        }

        [Test]
        public void 三分で締め切り着弾を待ってから記録へ移る()
        {
            var clock = new EnduranceTrialClock();
            clock.Begin(10.0);

            Assert.IsFalse(clock.Advance(189.9));
            Assert.AreEqual(EndurancePhase.Trial, clock.Phase);
            Assert.AreEqual(0.1, clock.RemainingSeconds(189.9), 1e-6);

            Assert.IsTrue(clock.Advance(190.0));
            Assert.AreEqual(EndurancePhase.Settle, clock.Phase);
            Assert.IsTrue(clock.Completed);
            Assert.AreEqual(180.0, clock.EndSeconds, 1e-9);
            Assert.AreEqual(180.5, clock.SinceTrialStart(190.5), 1e-9, "着弾待ち中も試技開始からの秒は進む");

            Assert.IsTrue(clock.Advance(191.0));
            Assert.AreEqual(EndurancePhase.Record, clock.Phase);
        }

        [Test]
        public void フレームが飛んでも一度で記録まで追いつく()
        {
            var clock = new EnduranceTrialClock();
            clock.Begin(0.0);

            Assert.IsTrue(clock.Advance(500.0));
            Assert.AreEqual(EndurancePhase.Record, clock.Phase);
            Assert.AreEqual(180.0, clock.EndSeconds, 1e-9);
            Assert.IsTrue(clock.Completed);
        }

        [Test]
        public void 終了希望で途中終了すると完走にならない()
        {
            var clock = new EnduranceTrialClock();
            clock.Begin(0.0);

            Assert.IsTrue(clock.StopEarly(95.0));
            Assert.AreEqual(EndurancePhase.Settle, clock.Phase);
            Assert.IsTrue(clock.StoppedEarly);
            Assert.IsFalse(clock.Completed);
            Assert.AreEqual(95.0, clock.EndSeconds, 1e-9);
            Assert.AreEqual(95.0, clock.TrialElapsed(120.0), 1e-9, "終わったあとは振っていた秒のまま");

            clock.Advance(96.0);
            Assert.AreEqual(EndurancePhase.Record, clock.Phase);
            Assert.IsFalse(clock.StopEarly(97.0), "試技中でなければ何もしない");
        }

        [Test]
        public void 三分を過ぎてからの終了希望は完走のまま()
        {
            var clock = new EnduranceTrialClock();
            clock.Begin(0.0);

            clock.StopEarly(180.2); // Advance より先に押された
            Assert.IsFalse(clock.StoppedEarly);
            Assert.IsTrue(clock.Completed);
            Assert.AreEqual(180.0, clock.EndSeconds, 1e-9);
        }

        [Test]
        public void 記録を終えると終了しリセットで準備に戻る()
        {
            var clock = new EnduranceTrialClock();
            clock.Begin(0.0);
            clock.Advance(200.0);

            clock.FinishRecord();
            Assert.AreEqual(EndurancePhase.Done, clock.Phase);

            clock.Reset();
            Assert.AreEqual(EndurancePhase.Ready, clock.Phase);
            Assert.IsFalse(clock.StoppedEarly);
            Assert.AreEqual(0.0, clock.EndSeconds, 1e-9);
        }
    }
}
