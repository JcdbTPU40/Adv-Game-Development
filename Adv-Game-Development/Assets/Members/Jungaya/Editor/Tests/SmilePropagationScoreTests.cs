using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Rescue.Tests
{
    /*
        笑顔の伝播 +20 が縁に乗るところ（#56 / 企画書 v8 7章 縁の計算式・付録B B-2・PROPAGATE）

          伝播得点 = round(20 × 救済時に保存した福の連なり倍率 × 救済時に保存したご加護倍率)

        計算そのものは #61 の EnFormula / ScoreManager.RegisterPropagation が持っている。
        ここで確かめるのは「#56 のルールで数えた回数ぶんが、正しく縁になるか」と
        「伝播が福の連なり C を動かさないか」「上限4人ぶん＝最大 +80 か」
    */
    public class SmilePropagationScoreTests
    {
        GameObject _go;
        ScoreManager _score;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ScoreManagerUnderTest");
            _score = _go.AddComponent<ScoreManager>();
            // 福の連なりの5秒タイマーはテスト内で時間を進めないので、時計は止めておく
            _score.Clock = () => 0f;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        // 付録B B-2 の写しを作る（伝播の数値だけ指定する）
        static ScoreBonusTable BonusTable(int points, int maxTargets)
        {
            var table = ScriptableObject.CreateInstance<ScoreBonusTable>();
            SetPrivate(table, "propagationPoints", points);
            SetPrivate(table, "propagationMaxTargets", maxTargets);
            return table;
        }

        static void SetPrivate(Object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{field} が見つかりません");
            info.SetValue(target, value);
        }

        [Test]
        public void 伝播一回で縁が二十入る()
        {
            int gain = _score.RegisterPropagation(new EnMultiplierSnapshot(1f, 1f));

            Assert.AreEqual(20, gain);
            Assert.AreEqual(20, _score.En);
        }

        [Test]
        public void 伝播は福の連なりを伸ばさない()
        {
            // 救済1件で連なり1。そのあと伝播が3回起きても連なりは1のまま（v8 7章）
            _score.RegisterCorrectHit(HitZone.Center, rescued: true, rescueBaseScore: 100);
            int comboAfterRescue = _score.Combo;

            for (int i = 0; i < 3; i++)
                _score.RegisterPropagation(_score.LastRescueSnapshot);

            Assert.AreEqual(comboAfterRescue, _score.Combo, "伝播で連なりが伸びている");
        }

        [Test]
        public void 伝播は福の連なりを途切れさせない()
        {
            _score.RegisterCorrectHit(HitZone.Center, rescued: true, rescueBaseScore: 100);
            _score.RegisterCorrectHit(HitZone.Center, rescued: true, rescueBaseScore: 100);
            int combo = _score.Combo;

            _score.RegisterPropagation(_score.LastRescueSnapshot);

            Assert.AreEqual(combo, _score.Combo, "伝播で連なりが切れている");
        }

        [Test]
        public void 救済時のスナップショットで計算し伝播時点の倍率へ差し替えない()
        {
            // 救えた時点の倍率（連なり ×1.2 / ご加護 ×1.0）
            var snapshot = new EnMultiplierSnapshot(1.2f, 1f);

            // 伝播が起きるまでの3秒で倍率が上がっても、入るのは保存した値のまま
            _score.SetGokagoMultiplier(2f);
            int gain = _score.RegisterPropagation(snapshot);

            Assert.AreEqual(24, gain, "伝播時点の倍率へ差しかわっている");
            Assert.AreEqual(24, _score.En);
        }

        [Test]
        public void 救済していないスナップショットでは入らない()
        {
            Assert.AreEqual(0, _score.RegisterPropagation(EnMultiplierSnapshot.None));
            Assert.AreEqual(0, _score.En);
        }

        [Test]
        public void 一回の救済から入る伝播の縁は最大八十()
        {
            // 遠方客の「基礎200 ＋ 伝播最大 +80」（付録B B-2）
            Assert.AreEqual(20, _score.PropagationPoints);
            Assert.AreEqual(4, _score.PropagationMaxTargets);
            Assert.AreEqual(80, _score.MaxPropagationEnPerRescue);

            var rules = new SmilePropagation(1, new EnMultiplierSnapshot(1f, 1f),
                _score.PropagationPoints, _score.PropagationMaxTargets);

            int total = 0;
            for (int id = 2; id <= 9; id++)
            {
                if (rules.TryPropagate(id, true, true) == SmilePropagationResult.Applied)
                    total += _score.RegisterPropagation(rules.Snapshot);
            }

            Assert.AreEqual(4, rules.Count);
            Assert.AreEqual(80, total);
            Assert.AreEqual(80, _score.En);
        }

        [Test]
        public void 数値表を割り当てるとその値を使う()
        {
            SetPrivate(_score, "bonusTable", BonusTable(30, 2));

            Assert.AreEqual(30, _score.PropagationPoints);
            Assert.AreEqual(2, _score.PropagationMaxTargets);
            Assert.AreEqual(60, _score.MaxPropagationEnPerRescue);
            Assert.AreEqual(30, _score.RegisterPropagation(new EnMultiplierSnapshot(1f, 1f)));
        }

        [Test]
        public void スコアを固定したあとは伝播で増えない()
        {
            // 3:00 のあと受理ずみの弾がぜんぶ落ちたら GameSession が LockScore を呼ぶ（#61 / #65）
            _score.RegisterPropagation(new EnMultiplierSnapshot(1f, 1f));
            _score.LockScore();

            Assert.AreEqual(0, _score.RegisterPropagation(new EnMultiplierSnapshot(1f, 1f)));
            Assert.AreEqual(20, _score.En);
        }
    }
}
