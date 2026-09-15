using NUnit.Framework;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 笑顔の伝播の規則 — Issue #56（企画書 v8 6章「笑顔の伝播」／7章／付録B PROPAGATE）
    ///
    /// #56 の完了条件のうち、PlayMode を使わずに確かめられるものをここで固定する。
    ///   ・同じペアで二重加点されない（有向ペア1回）
    ///   ・1回の救済からの伝播が4人を超えない（＝伝播の縁は最大 +80）
    ///   ・黒客が絡んでも得点が増えない
    ///   ・3:00境界をまたぐ伝播の扱いが一意に決まっている
    ///   ・倍率スナップショットの値（救済時の値を使い、あとで倍率が変わっても差し替えない）
    /// </summary>
    public class SmilePropagationTests
    {
        const int Rescuer = 10;

        /// <summary>倍率 ×1.0（福の連なり1・ご加護なし）の救済客。境界を試さない場合は時刻 0 で呼ぶ。</summary>
        static SmilePropagation Plain()
        {
            return new SmilePropagation(Rescuer, new SmileMultiplierSnapshot(1f, 1f));
        }

        // ---- 有向ペア1回 ----

        [Test]
        public void 同じ相手への二度目は成立しない()
        {
            SmilePropagation rules = Plain();

            Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(1, true, 0f));
            Assert.AreEqual(SmilePropagationResult.AlreadyPropagated, rules.TryPropagate(1, true, 0f));

            Assert.AreEqual(1, rules.Count, "人数が二重に数えられている");
            Assert.AreEqual(20, rules.TotalScore, "同じペアで二重加点されている");
        }

        [Test]
        public void 当たり判定が何フレーム重なっても一度しか成立しない()
        {
            SmilePropagation rules = Plain();

            for (int frame = 0; frame < 30; frame++)
                rules.TryPropagate(1, true, frame * 0.016f);

            Assert.AreEqual(1, rules.Count);
            Assert.AreEqual(20, rules.TotalScore);
        }

        [Test]
        public void 別々の救済客からは同じ相手へ伝播できる()
        {
            // 有向ペア（A→B）ごとの制限なので、B は A からも C からも受け取れる（v8 6章）。
            var fromA = new SmilePropagation(10, new SmileMultiplierSnapshot(1f, 1f));
            var fromC = new SmilePropagation(11, new SmileMultiplierSnapshot(1f, 1f));

            Assert.AreEqual(SmilePropagationResult.Applied, fromA.TryPropagate(1, true, 0f));
            Assert.AreEqual(SmilePropagationResult.Applied, fromC.TryPropagate(1, true, 0f));
        }

        [Test]
        public void 自分自身には伝播しない()
        {
            Assert.AreEqual(SmilePropagationResult.NotEligible, Plain().TryPropagate(Rescuer, true, 0f));
        }

        // ---- 上限4人 ----

        [Test]
        public void 五人目は上限で成立しない()
        {
            SmilePropagation rules = Plain();

            for (int id = 1; id <= 4; id++)
                Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(id, true, 0f), $"{id}人目");

            Assert.IsTrue(rules.IsFull);
            Assert.AreEqual(SmilePropagationResult.LimitReached, rules.TryPropagate(5, true, 0f));
            Assert.AreEqual(4, rules.Count);
        }

        [Test]
        public void 一回の救済から入る伝播の縁は最大八十()
        {
            // 遠方客の「基礎200 ＋ 伝播最大 +80」の後半（付録B B-2）。
            SmilePropagation rules = Plain();

            for (int id = 1; id <= 8; id++) rules.TryPropagate(id, true, 0f);

            Assert.AreEqual(80, rules.MaxTotalScore);
            Assert.AreEqual(80, rules.TotalScore);
        }

        // ---- 黒客・対象外 ----

        [Test]
        public void 対象条件を満たさない客は成立しない()
        {
            // 黒客・入場中・救済済み・R=0 はすべて CustomerState.IsRescueTarget が false になる。
            Assert.AreEqual(SmilePropagationResult.NotEligible, Plain().TryPropagate(1, false, 0f));
        }

        [Test]
        public void 黒客とすれ違っても上限枠も得点も減らない()
        {
            // 「黒客が多いほど縁が増える」抜け道を塞ぐ（v8変更点10）。枠を食うこともない。
            SmilePropagation rules = Plain();

            for (int id = 100; id < 110; id++)
                Assert.AreEqual(SmilePropagationResult.NotEligible, rules.TryPropagate(id, false, 0f));

            Assert.AreEqual(0, rules.Count);
            Assert.AreEqual(0, rules.TotalScore);

            for (int id = 1; id <= 4; id++)
                Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(id, true, 0f), $"{id}人目");
            Assert.AreEqual(80, rules.TotalScore, "黒客が枠を食っている");
        }

        [Test]
        public void ID未設定の客には伝播しない()
        {
            Assert.AreEqual(SmilePropagationResult.NotEligible, Plain().TryPropagate(0, true, 0f));
        }

        // ---- 3:00 境界 ----

        [Test]
        public void 三分未満の接触は有効で三分以後は無効()
        {
            SmilePropagation rules = Plain();

            Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(1, true, 179.999f));
            Assert.AreEqual(SmilePropagationResult.AfterDeadline, rules.TryPropagate(2, true, 180f));
            Assert.AreEqual(SmilePropagationResult.AfterDeadline, rules.TryPropagate(3, true, 180.5f));

            Assert.AreEqual(1, rules.Count);
            Assert.AreEqual(20, rules.TotalScore, "3:00 以後の接触で縁が増えている");
        }

        [Test]
        public void 境界後は対象外の判定より先に打ち切る()
        {
            // 「3:00 をまたぐ伝播の扱いが一意に決まっている」：時刻で先に落とすので、
            // 相手が誰であっても結果は AfterDeadline に定まる。
            SmilePropagation rules = Plain();

            Assert.AreEqual(SmilePropagationResult.AfterDeadline, rules.TryPropagate(0, false, 200f));
            Assert.AreEqual(SmilePropagationResult.AfterDeadline, rules.TryPropagate(1, true, 200f));
        }

        [Test]
        public void 境界なしを指定すればいつ接触しても有効()
        {
            // セッションを置かない確認シーン・テスト用（SmileCarrier が GameSession 不在時に使う指定）。
            var rules = new SmilePropagation(Rescuer, new SmileMultiplierSnapshot(1f, 1f),
                deadlineSeconds: float.PositiveInfinity);

            Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(1, true, 9999f));
        }

        // ---- 倍率スナップショット ----

        [Test]
        public void 得点は救済時の倍率で計算する()
        {
            // round(20 × 1.10 × 1.25) = round(27.5) = 28（四捨五入）
            var rules = new SmilePropagation(Rescuer, new SmileMultiplierSnapshot(1.1f, 1.25f));

            Assert.AreEqual(28, rules.ScorePerPropagation);
            Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(1, true, 0f));
            Assert.AreEqual(28, rules.TotalScore);
        }

        [Test]
        public void 四捨五入は絶対値の大きい方へ寄せる()
        {
            // v8 7章「小数を掛けた最後に四捨五入して整数化する」。
            // round(20 × 1.30 × 1.25) = round(32.5) = 33（銀行丸めなら 32 になってしまう）
            var rules = new SmilePropagation(Rescuer, new SmileMultiplierSnapshot(1.3f, 1.25f));

            Assert.AreEqual(33, rules.ScorePerPropagation);
            Assert.AreEqual(25, SmilePropagation.Round(24.5));
            Assert.AreEqual(26, SmilePropagation.Round(25.5));
        }

        [Test]
        public void スナップショットは作ったあと変わらない()
        {
            // 3秒後に倍率がどう動いていても、保存した値で計算し続ける（v8変更点6）。
            var snapshot = new SmileMultiplierSnapshot(1.2f, 1f);
            var rules = new SmilePropagation(Rescuer, snapshot);

            rules.TryPropagate(1, true, 0.5f);
            rules.TryPropagate(2, true, 2.9f);

            Assert.AreEqual(1.2f, rules.Snapshot.FukuChain, 0.0001f);
            Assert.AreEqual(1f, rules.Snapshot.Gokago, 0.0001f);
            Assert.AreEqual(24, rules.ScorePerPropagation);
            Assert.AreEqual(48, rules.TotalScore);
        }

        [Test]
        public void 大祓の救済客は両方の倍率が一倍()
        {
            SmileMultiplierSnapshot snapshot = SmileMultiplierSnapshot.Purification;

            Assert.AreEqual(1f, snapshot.FukuChain, 0.0001f);
            Assert.AreEqual(1f, snapshot.Gokago, 0.0001f);
            Assert.AreEqual(20, snapshot.ScoreOf(SmilePropagation.DefaultScorePerContact));
        }

        [Test]
        public void 倍率は一倍より下がらない()
        {
            var snapshot = new SmileMultiplierSnapshot(0f, -3f);

            Assert.AreEqual(1f, snapshot.Product, 0.0001f);
            Assert.AreEqual(20, snapshot.ScoreOf(20));
        }

        // ---- 数値表の差し替え ----

        [Test]
        public void 数値は付録Bの表から差し替えられる()
        {
            // 付録B を直したら、コードではなく ScoreBonusTable の数字を入れ替えれば追従する。
            var rules = new SmilePropagation(Rescuer, new SmileMultiplierSnapshot(1f, 1f),
                scorePerContact: 30, maxTargets: 2);

            Assert.AreEqual(60, rules.MaxTotalScore);
            rules.TryPropagate(1, true, 0f);
            rules.TryPropagate(2, true, 0f);
            Assert.AreEqual(SmilePropagationResult.LimitReached, rules.TryPropagate(3, true, 0f));
            Assert.AreEqual(60, rules.TotalScore);
        }

        [Test]
        public void 既定値は付録Bのとおり()
        {
            Assert.AreEqual(4, SmilePropagation.DefaultMaxTargets);
            Assert.AreEqual(20, SmilePropagation.DefaultScorePerContact);
            Assert.AreEqual(180f, SmilePropagation.DefaultDeadlineSeconds, 0.0001f);
            Assert.AreEqual(5f, CustomerStateMachine.SmileDangerRelief, 0.0001f);
        }
    }
}
