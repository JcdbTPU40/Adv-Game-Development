using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Rescue.Tests
{
    /// <summary>
    /// 優先救済 +50 が縁に乗るところ — Issue #55（仕様書 v8 7章 縁の計算式／付録B B-2）
    ///
    ///   救済得点 = round((基礎点 + 命中精度加点 + 優先救済加点) × 福の連なり倍率 × ご加護倍率)
    ///
    /// 加点の数値は <see cref="ScoreBonusTable"/>（付録B B-2 の写し）から引く。
    /// 「T2 で支配的なら +30 へ」をコード変更なしでできること（#55 完了条件）もここで確かめる。
    /// </summary>
    public class PriorityRescueScoreTests
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

        /// <summary>付録B B-2 の写しを作る（優先救済の加点だけ指定する）。</summary>
        static ScoreBonusTable BonusTable(int priorityRescueBonus)
        {
            var table = ScriptableObject.CreateInstance<ScoreBonusTable>();
            SetPrivate(table, "priorityRescueBonus", priorityRescueBonus);
            return table;
        }

        static void SetPrivate(Object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{field} が見つかりません");
            info.SetValue(target, value);
        }

        [Test]
        public void 二重円の客を救済したら加点が乗る()
        {
            // 通常客 100 + 中心 50 + 優先救済 50 = 200（連なり 1 なので倍率 ×1.0）
            _score.RegisterCorrectHit(HitZone.Center, rescued: true, rescueBaseScore: 100, priorityRescue: true);

            Assert.AreEqual(50, _score.LastPriorityBonus);
            Assert.AreEqual(200, _score.LastGain);
            Assert.AreEqual(200, _score.En);
        }

        [Test]
        public void 二重円でない客を救済しても加点は乗らない()
        {
            _score.RegisterCorrectHit(HitZone.Center, rescued: true, rescueBaseScore: 100, priorityRescue: false);

            Assert.AreEqual(0, _score.LastPriorityBonus);
            Assert.AreEqual(150, _score.LastGain);
        }

        [Test]
        public void 救済完了していない途中命中では縁も加点も入らない()
        {
            // 欲張り客の1発目（付録B B-2「部分点なし」）。福の連なりは増やさないで保つ（#61 / 7章）。
            _score.RegisterCorrectHit(HitZone.Center, rescued: false, rescueBaseScore: 300, priorityRescue: true);

            Assert.AreEqual(0, _score.LastGain);
            Assert.AreEqual(0, _score.LastPriorityBonus);
            Assert.AreEqual(0, _score.En);
            Assert.AreEqual(0, _score.Combo, "途中命中では福の連なりは増えない（救済完了でだけ +1）");
        }

        [Test]
        public void 加点の値は数値表から引く_コードを変えずに30へ下げられる()
        {
            // #55 完了条件「T2 で支配戦略になった場合に、コード変更なしで +50 → +30 へ下げられる」。
            ScoreBonusTable table = BonusTable(30);
            SetPrivate(_score, "bonusTable", table);

            Assert.AreEqual(30, _score.PriorityRescueBonus);

            _score.RegisterCorrectHit(HitZone.Center, rescued: true, rescueBaseScore: 100, priorityRescue: true);

            Assert.AreEqual(30, _score.LastPriorityBonus);
            Assert.AreEqual(180, _score.LastGain, "100 + 精度50 + 優先30");

            Object.DestroyImmediate(table);
        }

        [Test]
        public void 数値表が未割り当てなら付録Bの既定値を使う()
        {
            Assert.AreEqual(50, _score.PriorityRescueBonus);
        }

        [Test]
        public void 倍率は加点を足したあとに掛ける()
        {
            // v8 7章：round((基礎点 + 精度 + 優先救済) × 倍率)。加点に先に倍率を掛けない。
            _score.SetGokagoMultiplier(1.25f);
            _score.RegisterCorrectHit(HitZone.Center, rescued: true, rescueBaseScore: 100, priorityRescue: true);

            Assert.AreEqual(Mathf.RoundToInt((100 + 50 + 50) * 1.25f), _score.LastGain);
        }
    }
}
