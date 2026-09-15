using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// 縁の計算式 — Issue #61（仕様書 v8 7章「縁の計算式（通常弾）」／付録B B-2・PROPAGATE）
    ///
    ///   救済得点 = round((基礎点 + 最終弾の命中精度加点 + 優先救済加点) × 救済時の福の連なり倍率 × 発射時のご加護倍率)
    ///   伝播得点 = round(20 × 救済時に保存した福の連なり倍率 × 救済時に保存したご加護倍率)
    ///
    /// 小数を掛けた最後に四捨五入する。0.5 は切り上げる（Mathf.RoundToInt の偶数丸めではない）。
    /// </summary>
    public class EnFormulaTests
    {
        [Test]
        public void 通常客を中心で救済_倍率なし()
        {
            Assert.AreEqual(150, EnFormula.RescueScore(100, 50, 0, 1f, 1f));
        }

        [Test]
        public void 欲張り客を中間で救済_優先救済あり_連なり3()
        {
            // (300 + 20 + 50) × 1.10 = 407
            Assert.AreEqual(407, EnFormula.RescueScore(300, 20, 50, 1.10f, 1f));
        }

        [Test]
        public void 加点を足してから倍率を掛ける()
        {
            // (200 + 50 + 50) × 1.30 × 1.25 = 487.5 → 488
            Assert.AreEqual(488, EnFormula.RescueScore(200, 50, 50, 1.30f, 1.25f));
        }

        [Test]
        public void ちょうど0点5は切り上げる()
        {
            // (110 + 20) × 1.25 = 162.5 → 163（偶数丸めなら 162）
            Assert.AreEqual(163, EnFormula.RescueScore(110, 20, 0, 1f, 1.25f));
            Assert.AreEqual(162, Mathf.RoundToInt(162.5f), "Mathf.RoundToInt は偶数丸めになる（使わない理由）");
        }

        [Test]
        public void 伝播得点もちょうど0点5は切り上げる()
        {
            // 20 × 1.3 × 1.25 = 32.5 → 33（偶数丸めなら 32）
            Assert.AreEqual(33, EnFormula.PropagationScore(1.30f, 1.25f));
        }

        [Test]
        public void 倍率のfloatの端数を持ちこまない()
        {
            // 1.1f は float だと 1.10000002。decimal に直すと 1.1 になり、(300 + 50 + 50) × 1.1 × 1.25 = 550 ちょうど
            Assert.AreEqual(550, EnFormula.RescueScore(300, 50, 50, 1.10f, 1.25f));
        }

        [TestCase(1.00f, 20)]
        [TestCase(1.10f, 22)]
        [TestCase(1.20f, 24)]
        [TestCase(1.30f, 26)]
        public void 伝播得点は20に連なり倍率を掛ける(float chainMultiplier, int expected)
        {
            Assert.AreEqual(expected, EnFormula.PropagationScore(chainMultiplier, 1f));
        }

        [Test]
        public void 伝播の点数は表から渡せる()
        {
            Assert.AreEqual(33, EnFormula.PropagationScore(1.10f, 1f, points: 30));
        }

        [Test]
        public void 救済時の倍率を保存する値()
        {
            var snapshot = new EnMultiplierSnapshot(1.20f, 1.25f);
            Assert.IsTrue(snapshot.IsValid);
            Assert.AreEqual(1.20f, snapshot.ChainMultiplier);
            Assert.AreEqual(1.25f, snapshot.BlessingMultiplier);
            Assert.IsFalse(EnMultiplierSnapshot.None.IsValid);
        }
    }
}
