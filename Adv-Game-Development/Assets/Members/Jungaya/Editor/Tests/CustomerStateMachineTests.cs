using NUnit.Framework;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 危険度D／残り必要発数Rの状態遷移（企画書 v8 6章の表）— Issue #54
    ///
    /// 表の行を1テスト1行で網羅する。ここが落ちたら仕様と実装がずれている。
    /// </summary>
    public class CustomerStateMachineTests
    {
        static CustomerStateMachine Normal(bool startActive = true) =>
            new CustomerStateMachine(initialRemaining: 1, dangerFullSeconds: 20f, startActive: startActive);

        static CustomerStateMachine Greedy(bool startActive = true) =>
            new CustomerStateMachine(initialRemaining: 2, dangerFullSeconds: 28f, startActive: startActive);

        // ── 入場中 → 定位置へ到着 ──────────────────────────────

        [Test]
        public void 入場中はDが進まない_Rは初期値のまま()
        {
            CustomerStateMachine m = Greedy(startActive: false);

            m.TickDanger(10f);

            Assert.AreEqual(CustomerPhase.Entering, m.Phase);
            Assert.AreEqual(0f, m.Danger, 0.0001f, "入場中は D=0 で停止する");
            Assert.AreEqual(2, m.Remaining, "初期Rは客種の値を維持する");
        }

        [Test]
        public void 定位置へ到着するとactiveになりDが0から進み始める()
        {
            CustomerStateMachine m = Normal(startActive: false);

            Assert.IsTrue(m.Arrive());
            Assert.AreEqual(CustomerPhase.Active, m.Phase);
            Assert.AreEqual(0f, m.Danger, 0.0001f);

            m.TickDanger(5f);
            Assert.AreEqual(25f, m.Danger, 0.0001f, "D満タン20秒なら5秒で25");
        }

        [Test]
        public void 入場中は弾も伝播も受け付けない()
        {
            CustomerStateMachine m = Normal(startActive: false);

            Assert.AreEqual(CorrectHitResult.Ignored, m.HitCorrectColor());
            Assert.IsFalse(m.HitWrongColor());
            Assert.IsFalse(m.ReceiveSmile(), "入場中は笑顔の伝播の対象外");
            Assert.AreEqual(1, m.Remaining);
        }

        // ── active中の時間経過 ────────────────────────────────

        [Test]
        public void Dは満タン秒数に比例して進み100を超えない()
        {
            CustomerStateMachine m = Normal();

            m.TickDanger(10f);
            Assert.AreEqual(50f, m.Danger, 0.0001f);

            m.TickDanger(30f);
            Assert.AreEqual(100f, m.Danger, 0.0001f, "Dは0〜100に制限される");
        }

        // ── 正色命中 ──────────────────────────────────────────

        [Test]
        public void 正色命中はRを1減らしDを変えない()
        {
            CustomerStateMachine m = Greedy();
            m.TickDanger(14f);            // D = 50
            float before = m.Danger;

            Assert.AreEqual(CorrectHitResult.Progressed, m.HitCorrectColor());

            Assert.AreEqual(1, m.Remaining);
            Assert.AreEqual(before, m.Danger, 0.0001f, "正色命中で危険度は動かない");
            Assert.AreEqual(CustomerPhase.Active, m.Phase, "R>0 なので active 継続");
        }

        [Test]
        public void Rが0になったらrescuedでDは据え置き()
        {
            CustomerStateMachine m = Greedy();
            m.TickDanger(14f);            // D = 50

            m.HitCorrectColor();
            Assert.AreEqual(CorrectHitResult.Rescued, m.HitCorrectColor());

            Assert.AreEqual(0, m.Remaining);
            Assert.AreEqual(CustomerPhase.Rescued, m.Phase);
            Assert.AreEqual(50f, m.Danger, 0.0001f);
        }

        [Test]
        public void 救済後は終端でDも進まず再命中も受け付けない()
        {
            CustomerStateMachine m = Normal();
            m.HitCorrectColor();
            Assert.AreEqual(CustomerPhase.Rescued, m.Phase);

            m.TickDanger(100f);
            Assert.AreEqual(0f, m.Danger, 0.0001f, "救済済みの客のDは進まない");
            Assert.AreEqual(CorrectHitResult.Ignored, m.HitCorrectColor());
            Assert.IsFalse(m.ResolveBlackout(), "rescued から black へは戻らない");
        }

        // ── 誤色命中（v8変更点2） ──────────────────────────────

        [Test]
        public void 誤色命中はDもRも変えない()
        {
            CustomerStateMachine m = Greedy();
            m.TickDanger(14f);            // D = 50

            Assert.IsTrue(m.HitWrongColor());

            Assert.AreEqual(50f, m.Danger, 0.0001f);
            Assert.AreEqual(2, m.Remaining);
            Assert.AreEqual(CustomerPhase.Active, m.Phase);
        }

        [Test]
        public void 誤色を連打してもDの上昇速度は変わらない()
        {
            CustomerStateMachine clean = Normal();
            CustomerStateMachine spammed = Normal();

            // 20秒ぶんを 0.1 秒刻みで進める。片方は毎回誤色を当てる。
            for (int i = 0; i < 200; i++)
            {
                clean.TickDanger(0.1f);
                spammed.TickDanger(0.1f);
                spammed.HitWrongColor();
            }

            Assert.AreEqual(clean.Danger, spammed.Danger, 0.0001f,
                "誤色連打で黒客化を遅らせられてはいけない（v8変更点2）");
            Assert.IsTrue(clean.ResolveBlackout());
            Assert.IsTrue(spammed.ResolveBlackout(), "誤色を連打しても同じ時刻に黒客化する");
        }

        // ── 笑顔の伝播 ────────────────────────────────────────

        [Test]
        public void 笑顔の伝播はDを5減らしRは変えない()
        {
            CustomerStateMachine m = Greedy();
            m.TickDanger(14f);            // D = 50

            Assert.IsTrue(m.ReceiveSmile());

            Assert.AreEqual(45f, m.Danger, 0.0001f);
            Assert.AreEqual(2, m.Remaining);
            Assert.AreEqual(CustomerPhase.Active, m.Phase);
        }

        [Test]
        public void 笑顔の伝播でDは0未満にならない()
        {
            CustomerStateMachine m = Normal();
            m.TickDanger(0.4f);           // D = 2

            Assert.IsTrue(m.ReceiveSmile());
            Assert.AreEqual(0f, m.Danger, 0.0001f);
        }

        [Test]
        public void 黒客と救済済みは笑顔の伝播の対象外()
        {
            CustomerStateMachine black = Normal();
            black.TickDanger(20f);
            Assert.IsTrue(black.ResolveBlackout());

            CustomerStateMachine rescued = Normal();
            rescued.TickDanger(5f);
            rescued.HitCorrectColor();

            CustomerStateMachine entering = Normal(startActive: false);

            Assert.IsFalse(black.IsRescueTarget, "黒客は対象外（黒客が多いほど縁が増える抜け道を塞ぐ）");
            Assert.IsFalse(black.ReceiveSmile());
            Assert.AreEqual(100f, black.Danger, 0.0001f, "黒客のDは動かない");

            Assert.IsFalse(rescued.IsRescueTarget, "退場中の救済客は対象外");
            Assert.IsFalse(rescued.ReceiveSmile());

            Assert.IsFalse(entering.IsRescueTarget, "入場中は対象外");
            Assert.IsFalse(entering.ReceiveSmile());

            CustomerStateMachine active = Normal();
            Assert.IsTrue(active.IsRescueTarget, "active・未救済・非黒客・R>0 だけが対象");
        }

        // ── 黒客化と同時刻の解決順 ────────────────────────────

        [Test]
        public void Dが100に到達すると黒客化し救済へ戻らない()
        {
            CustomerStateMachine m = Normal();
            m.TickDanger(20f);

            Assert.AreEqual(CustomerPhase.Active, m.Phase, "TickDanger だけでは黒客化しない");
            Assert.IsTrue(m.ResolveBlackout());

            Assert.AreEqual(CustomerPhase.Black, m.Phase);
            Assert.AreEqual(100f, m.Danger, 0.0001f);
            Assert.AreEqual(CorrectHitResult.Ignored, m.HitCorrectColor(), "黒客は救済へ戻らない");
            Assert.AreEqual(CustomerPhase.Black, m.Phase);
        }

        [Test]
        public void 同時刻ではDが100でも先に解決した正色命中の救済が勝つ()
        {
            CustomerStateMachine m = Normal();
            m.TickDanger(20f);            // D = 100（まだ黒客化していない）

            Assert.AreEqual(CorrectHitResult.Rescued, m.HitCorrectColor());
            Assert.IsFalse(m.ResolveBlackout(), "救済済みなら黒客化しない");
            Assert.AreEqual(CustomerPhase.Rescued, m.Phase, "間一髪の救済が一意に優先される");
        }

        [Test]
        public void 同時刻の笑顔の伝播はDを下げて黒客化を防ぐ()
        {
            CustomerStateMachine m = Normal();
            m.TickDanger(20f);            // D = 100

            Assert.IsTrue(m.ReceiveSmile());
            Assert.IsFalse(m.ResolveBlackout());

            Assert.AreEqual(95f, m.Danger, 0.0001f);
            Assert.AreEqual(CustomerPhase.Active, m.Phase);
        }

        [Test]
        public void 複数発客は最後の1発まで救済されない()
        {
            CustomerStateMachine boss = new CustomerStateMachine(initialRemaining: 3, dangerFullSeconds: 30f, startActive: true);

            Assert.AreEqual(CorrectHitResult.Progressed, boss.HitCorrectColor());
            Assert.AreEqual(CorrectHitResult.Progressed, boss.HitCorrectColor());
            Assert.AreEqual(CustomerPhase.Active, boss.Phase);
            Assert.AreEqual(CorrectHitResult.Rescued, boss.HitCorrectColor());
            Assert.AreEqual(CustomerPhase.Rescued, boss.Phase);
        }
    }

    /// <summary>客種ごとの数値表（付録B B-1）が仕様どおりか。</summary>
    public class CustomerKindTableTests
    {
        static CustomerKindTable NewTable() => UnityEngine.ScriptableObject.CreateInstance<CustomerKindTable>();

        [TestCase(CustomerKind.Normal, 1, 100)]
        [TestCase(CustomerKind.Moving, 1, 150)]
        [TestCase(CustomerKind.Distant, 1, 200)]
        [TestCase(CustomerKind.Greedy, 2, 300)]
        [TestCase(CustomerKind.Boss, 3, 500)]
        public void 既定値は付録Bのとおり(CustomerKind kind, int initialRemaining, int baseScore)
        {
            CustomerKindTable table = NewTable();
            try
            {
                CustomerKindEntry entry = table.Get(kind);
                Assert.IsNotNull(entry, $"{kind} の行が無い");
                Assert.AreEqual(initialRemaining, entry.initialRemaining, "初期R");
                Assert.AreEqual(baseScore, entry.rescueBaseScore, "救済完了の基礎点");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void 通常客のD満タン秒数は15から25の幅を持つ()
        {
            CustomerKindTable table = NewTable();
            try
            {
                CustomerKindEntry entry = table.Get(CustomerKind.Normal);
                Assert.AreEqual(15f, entry.PickDangerFullSeconds(0f), 0.0001f);
                Assert.AreEqual(25f, entry.PickDangerFullSeconds(1f), 0.0001f);
                Assert.AreEqual(20f, entry.PickDangerFullSeconds(0.5f), 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(table);
            }
        }

        [Test]
        public void 幅の無い客種は固定値を返す()
        {
            CustomerKindTable table = NewTable();
            try
            {
                CustomerKindEntry entry = table.Get(CustomerKind.Greedy);
                Assert.AreEqual(28f, entry.PickDangerFullSeconds(0f), 0.0001f);
                Assert.AreEqual(28f, entry.PickDangerFullSeconds(1f), 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(table);
            }
        }
    }
}
