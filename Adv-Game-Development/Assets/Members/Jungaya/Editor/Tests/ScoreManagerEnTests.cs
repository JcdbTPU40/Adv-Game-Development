using NUnit.Framework;
using UnityEngine;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// ScoreManager の縁の集計 — Issue #61（仕様書 v8 7章「縁の計算式」「福の連なりの判定」「3:00境界の処理順」）
    /// 5秒タイマーは Clock を差しかえて時刻を進める。
    /// </summary>
    public class ScoreManagerEnTests
    {
        GameObject _go;
        ScoreManager _score;
        float _now;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ScoreManagerUnderTest");
            _score = _go.AddComponent<ScoreManager>();
            _now = 0f;
            _score.Clock = () => _now;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        void Rescue(int baseScore = 100, HitZone zone = HitZone.Outer)
        {
            _score.RegisterCorrectHit(zone, rescued: true, rescueBaseScore: baseScore);
        }

        // ── 福の連なりと倍率 ─────────────────────────────

        [Test]
        public void 救済3人目から連なり倍率が乗る()
        {
            Rescue(); Assert.AreEqual(100, _score.LastGain);
            Rescue(); Assert.AreEqual(100, _score.LastGain);
            Rescue(); Assert.AreEqual(110, _score.LastGain, "+1 したあとの C=3 の倍率 ×1.10 をその救済に使う");

            Assert.AreEqual(310, _score.En);
            Assert.AreEqual(3, _score.Combo);
            Assert.AreEqual(1.10f, _score.Multiplier);
        }

        [Test]
        public void 連なり10以上は1点30倍で止まる()
        {
            for (int i = 0; i < 12; i++) Rescue();
            Assert.AreEqual(1.30f, _score.Multiplier);
            Assert.AreEqual(130, _score.LastGain);
        }

        [Test]
        public void 欲張り客は途中点なしで救済完了時に300を一括加算()
        {
            _score.RegisterCorrectHit(HitZone.Center, rescued: false, rescueBaseScore: 300);
            Assert.AreEqual(0, _score.En, "1発目は画面上も内部スコアも 0");
            Assert.AreEqual(0, _score.LastGain);
            Assert.AreEqual(0, _score.Combo, "途中命中では C を増やさない");

            _score.RegisterCorrectHit(HitZone.Inner, rescued: true, rescueBaseScore: 300);
            Assert.AreEqual(320, _score.En, "最終弾の精度（中間 +20）だけを足して 300 を一括");
            Assert.AreEqual(1, _score.Combo);
        }

        [Test]
        public void 途中命中はCを保つ()
        {
            Rescue(); Rescue();
            _score.RegisterCorrectHit(HitZone.Center, rescued: false, rescueBaseScore: 300);
            Assert.AreEqual(2, _score.Combo);
        }

        [Test]
        public void 誤投擲でCが0に戻る()
        {
            Rescue(); Rescue(); Rescue();
            _score.RegisterMiss();

            Assert.AreEqual(0, _score.Combo);
            Assert.AreEqual(3, _score.MaxCombo, "リザルト用の最大値は残る");

            Rescue();
            Assert.AreEqual(100, _score.LastGain, "切れたあとは ×1.0 から");
        }

        [Test]
        public void 地面への外れではCは切れない()
        {
            Rescue(); Rescue(); Rescue();
            _score.RegisterGroundMiss();
            Assert.AreEqual(3, _score.Combo);
        }

        [Test]
        public void 正しい色を5秒当てないとCが0に戻る()
        {
            Rescue(); Rescue(); Rescue();

            _now = 4.9f;
            Assert.IsFalse(_score.TickChainTimeout(_now));
            Assert.AreEqual(3, _score.Combo);

            _now = 5f;
            Assert.IsTrue(_score.TickChainTimeout(_now));
            Assert.AreEqual(0, _score.Combo);
        }

        [Test]
        public void 途中命中で5秒タイマーが戻る()
        {
            Rescue();
            _now = 4f;
            _score.RegisterCorrectHit(HitZone.Outer, rescued: false, rescueBaseScore: 300);

            _now = 8f;
            Assert.IsFalse(_score.TickChainTimeout(_now));
            _now = 9f;
            Assert.IsTrue(_score.TickChainTimeout(_now));
        }

        // ── ご加護倍率と伝播 ─────────────────────────────

        [Test]
        public void ご加護倍率は発射時に保存した値を使う()
        {
            // 発射したときはご加護中（×1.25）、着弾したときには終わっていた
            _score.SetGokagoMultiplier(1f);
            _score.RegisterCorrectHit(HitZone.Outer, rescued: true, rescueBaseScore: 100, blessingMultiplier: 1.25f);
            Assert.AreEqual(125, _score.LastGain);

            // 発射したときはご加護の前、着弾したときにはご加護中
            _score.SetGokagoMultiplier(1.25f);
            _score.RegisterCorrectHit(HitZone.Outer, rescued: true, rescueBaseScore: 100, blessingMultiplier: 1f);
            Assert.AreEqual(100, _score.LastGain);
        }

        [Test]
        public void 救済時の倍率を保存して伝播得点に使う()
        {
            Rescue(); Rescue(); Rescue();
            EnMultiplierSnapshot snapshot = _score.LastRescueSnapshot;
            Assert.IsTrue(snapshot.IsValid);
            Assert.AreEqual(1.10f, snapshot.ChainMultiplier);

            // 救済のあと、伝播が起きる前に C が切れても、救済時の倍率で計算する
            _score.RegisterMiss();
            int gained = _score.RegisterPropagation(snapshot);

            Assert.AreEqual(22, gained, "20 × 1.10");
            Assert.AreEqual(310 + 22, _score.En);
        }

        [Test]
        public void 救済で作った値でなければ伝播は入らない()
        {
            Assert.AreEqual(0, _score.RegisterPropagation(EnMultiplierSnapshot.None));
            Assert.AreEqual(0, _score.En);
        }

        [Test]
        public void 途中命中では倍率を保存しない()
        {
            _score.RegisterCorrectHit(HitZone.Center, rescued: false, rescueBaseScore: 300);
            Assert.IsFalse(_score.LastRescueSnapshot.IsValid);
        }

        // ── 3:00 のスコア固定 ─────────────────────────────

        [Test]
        public void 固定したあとは縁もCも動かない()
        {
            int lockedEn = -1;
            _score.onScoreLocked += en => lockedEn = en;

            Rescue();
            EnMultiplierSnapshot snapshot = _score.LastRescueSnapshot;
            _score.LockScore();
            Assert.AreEqual(100, lockedEn);

            Rescue();
            _score.RegisterMiss();
            Assert.AreEqual(0, _score.RegisterPropagation(snapshot), "3:00 以後の伝播は入らない");
            Assert.IsFalse(_score.TickChainTimeout(100f));

            Assert.AreEqual(100, _score.En);
            Assert.AreEqual(1, _score.Combo);
        }

        [Test]
        public void リセットで固定が外れる()
        {
            Rescue();
            _score.LockScore();
            _score.ResetAll();

            Assert.IsFalse(_score.IsLocked);
            Assert.AreEqual(0, _score.En);
            Assert.AreEqual(0, _score.MaxCombo);
            Rescue();
            Assert.AreEqual(100, _score.En);
        }

        [Test]
        public void ランクは倍率に入らない()
        {
            Rescue(); Rescue(); Rescue();
            Assert.AreEqual(_score.Multiplier * _score.GokagoMultiplier, _score.TotalMultiplier);
        }
    }
}
