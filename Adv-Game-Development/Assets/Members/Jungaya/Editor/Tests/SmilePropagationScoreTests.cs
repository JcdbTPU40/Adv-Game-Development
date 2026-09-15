using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 笑顔の伝播 +20 が縁に乗るところ — Issue #56（企画書 v8 7章 縁の計算式／付録B B-2・PROPAGATE）
    ///
    ///   伝播得点 = round(20 × 救済時に保存した福の連なり倍率 × 救済時に保存したご加護倍率)
    ///
    /// 伝播は「救済」ではないので、福の連なり C を伸ばさず、途切れさせもしない。
    /// 加点の数値は <see cref="ScoreBonusTable"/>（付録B B-2 の写し）から引けることもここで確かめる。
    /// </summary>
    public class SmilePropagationScoreTests
    {
        GameObject _go;
        ScoreManager _score;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ScoreManagerUnderTest");
            _score = _go.AddComponent<ScoreManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        /// <summary>付録B B-2 の写しを作る（伝播の数値だけ指定する）。</summary>
        static ScoreBonusTable BonusTable(int smilePropagationBonus, int maxTargets)
        {
            var table = ScriptableObject.CreateInstance<ScoreBonusTable>();
            SetPrivate(table, "smilePropagationBonus", smilePropagationBonus);
            SetPrivate(table, "smilePropagationMaxTargets", maxTargets);
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
            int gain = _score.RegisterSmilePropagation(SmileMultiplierSnapshot.Purification);

            Assert.AreEqual(20, gain);
            Assert.AreEqual(20, _score.En);
            Assert.AreEqual(20, _score.PropagationEn);
            Assert.AreEqual(1, _score.PropagationCount);
            Assert.AreEqual(20, _score.LastPropagationGain);
        }

        [Test]
        public void 伝播は福の連なりを伸ばさない()
        {
            // 救済 1 件で連なり 1。そのあと伝播が 3 回起きても連なりは 1 のまま（v8 7章）。
            _score.RegisterCorrectHit(HitZone.Center, rescued: true, rescueBaseScore: 100);
            int comboAfterRescue = _score.Combo;

            for (int i = 0; i < 3; i++)
                _score.RegisterSmilePropagation(new SmileMultiplierSnapshot(_score.Multiplier, 1f));

            Assert.AreEqual(comboAfterRescue, _score.Combo, "伝播で連なりが伸びている");
            Assert.AreEqual(3, _score.PropagationCount);
        }

        [Test]
        public void 伝播は福の連なりを途切れさせない()
        {
            _score.RegisterCorrectHit(HitZone.Center, rescued: true, rescueBaseScore: 100);
            _score.RegisterCorrectHit(HitZone.Center, rescued: true, rescueBaseScore: 100);
            int combo = _score.Combo;

            _score.RegisterSmilePropagation(SmileMultiplierSnapshot.Purification);

            Assert.AreEqual(combo, _score.Combo, "伝播で連なりが切れている");
        }

        [Test]
        public void 救済時の倍率で計算し伝播時点の倍率へ差し替えない()
        {
            // 救済した時点のスナップショット（連なり ×1.2 / ご加護 ×1.0）。
            var snapshot = new SmileMultiplierSnapshot(1.2f, 1f);

            // 伝播が起きるまでの 3 秒で倍率が上がっても、入るのは保存した値のまま。
            _score.SetGokagoMultiplier(2f);
            int gain = _score.RegisterSmilePropagation(snapshot);

            Assert.AreEqual(24, gain, "伝播時点の倍率へ差し替わっている");
            Assert.AreEqual(24, _score.En);
        }

        [Test]
        public void 一回の救済から入る伝播の縁は最大八十()
        {
            // 遠方客の「基礎200 ＋ 伝播最大 +80」（付録B B-2）。
            Assert.AreEqual(20, _score.SmilePropagationBonus);
            Assert.AreEqual(4, _score.SmilePropagationMaxTargets);
            Assert.AreEqual(80, _score.MaxSmilePropagationBonusPerRescue);

            var rules = new SmilePropagation(1, SmileMultiplierSnapshot.Purification,
                _score.SmilePropagationBonus, _score.SmilePropagationMaxTargets);

            int total = 0;
            for (int id = 2; id <= 9; id++)
            {
                if (rules.TryPropagate(id, true, 0f) == SmilePropagationResult.Applied)
                    total += _score.RegisterSmilePropagation(rules.Snapshot);
            }

            Assert.AreEqual(80, total);
            Assert.AreEqual(80, _score.En);
            Assert.AreEqual(4, _score.PropagationCount);
        }

        [Test]
        public void 数値表を割り当てるとその値を使う()
        {
            SetPrivate(_score, "bonusTable", BonusTable(30, 2));

            Assert.AreEqual(30, _score.SmilePropagationBonus);
            Assert.AreEqual(2, _score.SmilePropagationMaxTargets);
            Assert.AreEqual(60, _score.MaxSmilePropagationBonusPerRescue);
            Assert.AreEqual(30, _score.RegisterSmilePropagation(SmileMultiplierSnapshot.Purification));
        }

        [Test]
        public void リセットで伝播の集計も戻る()
        {
            _score.RegisterSmilePropagation(SmileMultiplierSnapshot.Purification);
            _score.ResetAll();

            Assert.AreEqual(0, _score.En);
            Assert.AreEqual(0, _score.PropagationEn);
            Assert.AreEqual(0, _score.PropagationCount);
            Assert.AreEqual(0, _score.LastPropagationGain);
        }
    }
}
