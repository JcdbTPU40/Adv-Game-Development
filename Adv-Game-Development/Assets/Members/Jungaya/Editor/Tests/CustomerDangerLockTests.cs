using NUnit.Framework;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 段階学習の危険度Dのスクリプト固定 — Issue #58（仕様書 v8 18章「0:00〜0:18はDが増えない」「1人だけD=65をスクリプトで固定」「この30秒は黒客化しない」）
    /// </summary>
    public class CustomerDangerLockTests
    {
        static CustomerStateMachine Normal() =>
            new CustomerStateMachine(initialRemaining: 1, dangerFullSeconds: 20f, startActive: true);

        [Test]
        public void 固定中は時間がたってもDが進まず黒客にならない()
        {
            CustomerStateMachine m = Normal();
            m.LockDanger(65f);

            m.TickDanger(100f);

            Assert.IsTrue(m.IsDangerLocked);
            Assert.AreEqual(65f, m.Danger, 0.0001f);
            Assert.IsFalse(m.ResolveBlackout());
            Assert.AreEqual(CustomerPhase.Active, m.Phase);
        }

        [Test]
        public void 固定中は笑顔が伝わってもDは変わらないが伝わったことは返す()
        {
            CustomerStateMachine m = Normal();
            m.LockDanger(65f);

            Assert.IsTrue(m.ReceiveSmile());
            Assert.AreEqual(65f, m.Danger, 0.0001f);
        }

        [Test]
        public void 固定中はデバッグ用のD設定を受け付けない()
        {
            CustomerStateMachine m = Normal();
            m.LockDanger(0f);

            m.SetDangerForDebug(80f);

            Assert.AreEqual(0f, m.Danger, 0.0001f);
        }

        [Test]
        public void Dに100以上を渡しても100未満で固定して黒客にしない()
        {
            CustomerStateMachine m = Normal();
            m.LockDanger(150f);

            Assert.Less(m.Danger, CustomerStateMachine.MaxDanger);
            Assert.IsFalse(m.ResolveBlackout());
        }

        [Test]
        public void 固定中も正しい色で救える()
        {
            CustomerStateMachine m = Normal();
            m.LockDanger(65f);

            Assert.AreEqual(CorrectHitResult.Rescued, m.HitCorrectColor());
            Assert.IsTrue(m.IsRescued);
        }

        [Test]
        public void 固定を外すと渡したDから時間で進みはじめる()
        {
            CustomerStateMachine m = Normal();
            m.LockDanger(65f);

            m.UnlockDanger(0f);
            Assert.IsFalse(m.IsDangerLocked);
            Assert.AreEqual(0f, m.Danger, 0.0001f);

            m.TickDanger(10f);
            Assert.AreEqual(50f, m.Danger, 0.0001f, "満タン20秒の客は10秒で D=50");
        }

        [Test]
        public void Dを渡さずに外すと今のDから進む()
        {
            CustomerStateMachine m = Normal();
            m.LockDanger(65f);

            m.UnlockDanger();
            m.TickDanger(2f);

            Assert.AreEqual(75f, m.Danger, 0.0001f);
        }
    }
}
