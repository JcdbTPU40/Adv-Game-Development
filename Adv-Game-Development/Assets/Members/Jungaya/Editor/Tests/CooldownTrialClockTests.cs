using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#50 4 条件 ×（単発 → 休憩 → 連投 → 記録 → 休憩）。ブロックは合図で、休憩は時間で終わる。</summary>
    public class CooldownTrialClockTests
    {
        static CooldownTrialClock NewClock() =>
            new CooldownTrialClock { BlockRestSeconds = 20.0, ConditionRestSeconds = 60.0 };

        [Test]
        public void 開始前は準備で待つ()
        {
            var clock = NewClock();
            Assert.AreEqual(CooldownPhase.Ready, clock.Phase);
            Assert.IsFalse(clock.IsBlock);

            clock.Next(0.0);
            Assert.AreEqual(CooldownPhase.SingleBlock, clock.Phase);
            Assert.AreEqual(0, clock.TrialIndex);
        }

        [Test]
        public void 一条件は単発_休憩_連投_記録の順に進む()
        {
            var clock = NewClock();
            clock.Begin(0.0);

            Assert.AreEqual(CooldownBlock.Single, clock.Block);
            clock.Next(10.0);
            Assert.AreEqual(CooldownPhase.BlockRest, clock.Phase);
            clock.Next(11.0);
            Assert.AreEqual(CooldownPhase.PairBlock, clock.Phase);
            Assert.AreEqual(CooldownBlock.Pair, clock.Block);
            clock.Next(30.0);
            Assert.AreEqual(CooldownPhase.Record, clock.Phase);
            Assert.IsFalse(clock.IsBlock);
        }

        [Test]
        public void 記録のあとは休憩をはさんで次の条件へ()
        {
            var clock = NewClock();
            clock.Begin(0.0);
            clock.Next(1.0); // BlockRest
            clock.Next(2.0); // PairBlock
            clock.Next(3.0); // Record
            clock.Next(4.0); // ConditionRest

            Assert.AreEqual(CooldownPhase.ConditionRest, clock.Phase);
            Assert.AreEqual(0, clock.TrialIndex, "休憩の間はまだ前の条件のまま");

            clock.Next(5.0);
            Assert.AreEqual(CooldownPhase.SingleBlock, clock.Phase);
            Assert.AreEqual(1, clock.TrialIndex);
        }

        [Test]
        public void 四条件目の記録で終わる()
        {
            var clock = NewClock();
            clock.Begin(0.0);

            double t = 0.0;
            for (int condition = 0; condition < CooldownTestPlan.ConditionCount; condition++)
            {
                Assert.AreEqual(CooldownPhase.SingleBlock, clock.Phase);
                Assert.AreEqual(condition, clock.TrialIndex);

                clock.Next(++t); // BlockRest
                clock.Next(++t); // PairBlock
                clock.Next(++t); // Record
                clock.Next(++t); // ConditionRest か Done

                if (condition < CooldownTestPlan.ConditionCount - 1)
                {
                    Assert.AreEqual(CooldownPhase.ConditionRest, clock.Phase);
                    clock.Next(++t); // 次の条件の単発へ
                }
            }

            Assert.AreEqual(CooldownPhase.Done, clock.Phase);
            Assert.IsFalse(clock.Next(100.0), "終了後は進まない");
        }

        [Test]
        public void 休憩だけは時間で自動的に進む()
        {
            var clock = NewClock();
            clock.Begin(0.0);
            clock.Next(1.0); // BlockRest（20 秒）

            Assert.IsFalse(clock.Advance(10.0), "途中では進まない");
            Assert.AreEqual(CooldownPhase.BlockRest, clock.Phase);
            Assert.AreEqual(10.0, clock.RemainingSeconds(11.0), 1e-6);

            Assert.IsTrue(clock.Advance(21.0));
            Assert.AreEqual(CooldownPhase.PairBlock, clock.Phase);
        }

        [Test]
        public void ブロックは時間では終わらない()
        {
            var clock = NewClock();
            clock.Begin(0.0);

            Assert.IsFalse(clock.Advance(9999.0), "単発ブロックは時間では終わらない");
            Assert.AreEqual(CooldownPhase.SingleBlock, clock.Phase);
            Assert.AreEqual(0.0, clock.RemainingSeconds(9999.0), 1e-6);
        }

        [Test]
        public void フレームが飛んでも休憩は一つずつ進む()
        {
            var clock = NewClock();
            clock.Begin(0.0);
            clock.Next(1.0); // BlockRest（20 秒）

            // 休憩の次は連投ブロック（時間で終わらない）なので、どれだけ飛んでもそこで止まる
            Assert.IsTrue(clock.Advance(1000.0));
            Assert.AreEqual(CooldownPhase.PairBlock, clock.Phase);
        }

        [Test]
        public void 途中の条件から再開できる()
        {
            var clock = NewClock();
            clock.Begin(0.0, 2);

            Assert.AreEqual(CooldownPhase.SingleBlock, clock.Phase);
            Assert.AreEqual(2, clock.TrialIndex);
            Assert.IsFalse(clock.IsLastCondition);

            clock.Begin(0.0, 99);
            Assert.AreEqual(CooldownTestPlan.ConditionCount - 1, clock.TrialIndex, "範囲外は最後の条件へ丸める");
            Assert.IsTrue(clock.IsLastCondition);
        }

        [Test]
        public void 次の参加者で初期化される()
        {
            var clock = NewClock();
            clock.Begin(0.0, 3);
            clock.Reset();

            Assert.AreEqual(CooldownPhase.Ready, clock.Phase);
            Assert.AreEqual(0, clock.TrialIndex);
        }
    }
}
