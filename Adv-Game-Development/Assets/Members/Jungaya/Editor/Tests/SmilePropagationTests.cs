using NUnit.Framework;

namespace Toufuku.Rescue.Tests
{
    /*
        笑顔の伝播のルール（#56 / 企画書 v8 6章「笑顔の伝播」・7章・付録B PROPAGATE）

        #56 の完了条件のうち、PlayMode を使わなくても確かめられるものをここで固定する
        ・同じ組で2回加点されない（同じ向きの組は1回）
        ・1回の救済から伝わるのが4人をこえない（＝伝播の縁は最大 +80）
        ・黒客がまざっても得点が増えない
        ・3:00 をまたぐ伝播のあつかいが1つに決まっている
        ・保存した倍率の値（救えたときの値を使って、あとで倍率が変わっても差しかえない）
    */
    public class SmilePropagationTests
    {
        const int Rescuer = 10;

        // 倍率 ×1.0（福の連なり1・ご加護なし）の救済客
        static SmilePropagation Plain()
        {
            return new SmilePropagation(Rescuer, new EnMultiplierSnapshot(1f, 1f));
        }

        // ---- 同じ向きの組は1回 ----

        [Test]
        public void 同じ相手への二度目は成立しない()
        {
            SmilePropagation rules = Plain();

            Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(1, true, true));
            Assert.AreEqual(SmilePropagationResult.AlreadyPropagated, rules.TryPropagate(1, true, true));

            Assert.AreEqual(1, rules.Count, "人数が二重に数えられている");
            Assert.AreEqual(20, rules.TotalScore, "同じ組で二重に加点されている");
        }

        [Test]
        public void 当たり判定が何フレーム重なっても一度しか成立しない()
        {
            SmilePropagation rules = Plain();

            for (int frame = 0; frame < 30; frame++)
                rules.TryPropagate(1, true, true);

            Assert.AreEqual(1, rules.Count);
            Assert.AreEqual(20, rules.TotalScore);
        }

        [Test]
        public void 別々の救済客からは同じ相手へ伝播できる()
        {
            // 向きのある組（A→B）ごとの制限なので、B は A からも C からも受け取れる（v8 6章）
            var fromA = new SmilePropagation(10, new EnMultiplierSnapshot(1f, 1f));
            var fromC = new SmilePropagation(11, new EnMultiplierSnapshot(1f, 1f));

            Assert.AreEqual(SmilePropagationResult.Applied, fromA.TryPropagate(1, true, true));
            Assert.AreEqual(SmilePropagationResult.Applied, fromC.TryPropagate(1, true, true));
        }

        [Test]
        public void 自分自身には伝播しない()
        {
            Assert.AreEqual(SmilePropagationResult.NotEligible, Plain().TryPropagate(Rescuer, true, true));
        }

        // ---- 上限4人 ----

        [Test]
        public void 五人目は上限で成立しない()
        {
            SmilePropagation rules = Plain();

            for (int id = 1; id <= 4; id++)
                Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(id, true, true), $"{id}人目");

            Assert.IsTrue(rules.IsFull);
            Assert.AreEqual(SmilePropagationResult.LimitReached, rules.TryPropagate(5, true, true));
            Assert.AreEqual(4, rules.Count);
        }

        [Test]
        public void 一回の救済から入る伝播の縁は最大八十()
        {
            // 遠方客の「基礎200 ＋ 伝播最大 +80」の後ろ半分（付録B B-2）
            SmilePropagation rules = Plain();

            for (int id = 1; id <= 8; id++) rules.TryPropagate(id, true, true);

            Assert.AreEqual(80, rules.MaxTotalScore);
            Assert.AreEqual(80, rules.TotalScore);
        }

        // ---- 黒客・相手にならない客 ----

        [Test]
        public void 条件を満たさない客には伝播しない()
        {
            // 黒客・入場中・救済ずみ・R=0 はどれも CustomerState.IsRescueTarget が false になる
            Assert.AreEqual(SmilePropagationResult.NotEligible, Plain().TryPropagate(1, false, true));
        }

        [Test]
        public void 黒客とすれ違っても上限枠も得点も減らない()
        {
            // 「黒客が多いほど縁が増える」ずるをできなくする（v8 変更点10）。枠を食うこともない
            SmilePropagation rules = Plain();

            for (int id = 100; id < 110; id++)
                Assert.AreEqual(SmilePropagationResult.NotEligible, rules.TryPropagate(id, false, true));

            Assert.AreEqual(0, rules.Count);
            Assert.AreEqual(0, rules.TotalScore);

            for (int id = 1; id <= 4; id++)
                Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(id, true, true), $"{id}人目");
            Assert.AreEqual(80, rules.TotalScore, "黒客が枠を食っている");
        }

        [Test]
        public void ID未設定の客には伝播しない()
        {
            Assert.AreEqual(SmilePropagationResult.NotEligible, Plain().TryPropagate(0, true, true));
        }

        // ---- 3:00 の境目 ----

        [Test]
        public void 三分に間に合えば有効で間に合わなければ無効()
        {
            SmilePropagation rules = Plain();

            Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(1, true, true));
            Assert.AreEqual(SmilePropagationResult.AfterDeadline, rules.TryPropagate(2, true, false));
            Assert.AreEqual(SmilePropagationResult.AfterDeadline, rules.TryPropagate(3, true, false));

            Assert.AreEqual(1, rules.Count);
            Assert.AreEqual(20, rules.TotalScore, "3:00 以後の接触で縁が増えている");
        }

        [Test]
        public void 境界後は相手の条件より先に打ち切る()
        {
            // 「3:00 をまたぐ伝播のあつかいが1つに決まっている」: 時刻で先に落とすので、
            // 相手がだれであっても結果は AfterDeadline になる
            SmilePropagation rules = Plain();

            Assert.AreEqual(SmilePropagationResult.AfterDeadline, rules.TryPropagate(0, false, false));
            Assert.AreEqual(SmilePropagationResult.AfterDeadline, rules.TryPropagate(1, true, false));
        }

        [Test]
        public void 境界で落ちた相手には後から伝播できる()
        {
            // 落ちたぶんは「もう伝えた」に入らないので、まだ 3:00 前だったフレームの判定と食いちがわない
            SmilePropagation rules = Plain();

            Assert.AreEqual(SmilePropagationResult.AfterDeadline, rules.TryPropagate(1, true, false));
            Assert.IsFalse(rules.HasReached(1));
            Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(1, true, true));
        }

        // ---- 倍率のスナップショット ----

        [Test]
        public void 得点は救済時の倍率で計算する()
        {
            // round(20 × 1.10 × 1.25) = round(27.5) = 28（四捨五入）
            var rules = new SmilePropagation(Rescuer, new EnMultiplierSnapshot(1.1f, 1.25f));

            Assert.AreEqual(28, rules.ScorePerPropagation);
            Assert.AreEqual(SmilePropagationResult.Applied, rules.TryPropagate(1, true, true));
            Assert.AreEqual(28, rules.TotalScore);
        }

        [Test]
        public void 四捨五入は絶対値の大きい方へ寄せる()
        {
            // v8 7章「小数を掛けた最後に四捨五入して整数化する」
            // round(20 × 1.30 × 1.25) = round(32.5) = 33（偶数丸めだと 32 になってしまう）
            var rules = new SmilePropagation(Rescuer, new EnMultiplierSnapshot(1.3f, 1.25f));

            Assert.AreEqual(33, rules.ScorePerPropagation);
        }

        [Test]
        public void スナップショットは作ったあと変わらない()
        {
            // 3秒後に倍率がどう動いていても、保存した値で計算しつづける（v8 変更点6）
            var rules = new SmilePropagation(Rescuer, new EnMultiplierSnapshot(1.2f, 1f));

            rules.TryPropagate(1, true, true);
            rules.TryPropagate(2, true, true);

            Assert.AreEqual(1.2f, rules.Snapshot.ChainMultiplier, 0.0001f);
            Assert.AreEqual(1f, rules.Snapshot.BlessingMultiplier, 0.0001f);
            Assert.AreEqual(24, rules.ScorePerPropagation);
            Assert.AreEqual(48, rules.TotalScore);
        }

        [Test]
        public void 大祓の救済客は両方の倍率が一倍()
        {
            // 大祓（追加T5）で救えた客は両方 ×1.0 で保存する（v8 7章）
            var rules = new SmilePropagation(Rescuer, new EnMultiplierSnapshot(1f, 1f));

            Assert.AreEqual(20, rules.ScorePerPropagation);
            Assert.AreEqual(80, rules.MaxTotalScore);
        }

        // ---- 数値表の差しかえ ----

        [Test]
        public void 数値は付録Bの表から差し替えられる()
        {
            // 付録B を直したら、コードじゃなくて ScoreBonusTable の数字を入れかえれば付いてくる
            var rules = new SmilePropagation(Rescuer, new EnMultiplierSnapshot(1f, 1f),
                pointsPerContact: 30, maxTargets: 2);

            Assert.AreEqual(60, rules.MaxTotalScore);
            rules.TryPropagate(1, true, true);
            rules.TryPropagate(2, true, true);
            Assert.AreEqual(SmilePropagationResult.LimitReached, rules.TryPropagate(3, true, true));
            Assert.AreEqual(60, rules.TotalScore);
        }

        [Test]
        public void 既定値は付録Bのとおり()
        {
            Assert.AreEqual(4, SmilePropagation.DefaultMaxTargets);
            Assert.AreEqual(20, EnFormula.DefaultPropagationPoints);
            Assert.AreEqual(5f, CustomerStateMachine.SmileDangerRelief, 0.0001f);
        }
    }
}
