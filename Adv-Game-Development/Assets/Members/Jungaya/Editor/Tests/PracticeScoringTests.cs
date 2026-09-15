using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// 段階学習の練習中は競技の得点・評価に入れない — Issue #58
    /// （仕様書 v8 18章「学習中は競技用の得点・評価・ランクを加算しない」「誤投擲は『色が違う』短表示だけで罰を与えない」、
    ///  8章「0:30.000で全競技カウンタを0へ初期化」）
    /// </summary>
    public class PracticeScoringTests
    {
        GameObject _go;
        ScoreManager _score;
        ShrineRating _rating;
        float _now;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("PracticeScoringUnderTest");
            _score = _go.AddComponent<ScoreManager>();
            _now = 0f;
            _score.Clock = () => _now;

            _rating = _go.AddComponent<ShrineRating>();
            if (_rating.onRatingChanged == null) _rating.onRatingChanged = new UnityEvent<float>();
            if (_rating.onRankChanged == null) _rating.onRankChanged = new UnityEvent<ShrineRank>();
            if (_rating.onMaxRankChanged == null) _rating.onMaxRankChanged = new UnityEvent<ShrineRank>();
            _rating.ResetAll();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        void Rescue() => _score.RegisterCorrectHit(HitZone.Outer, rescued: true, rescueBaseScore: 100);

        [Test]
        public void ふだんは練習中ではない()
        {
            Assert.IsFalse(_score.IsPractice);
            Assert.IsFalse(_rating.IsPractice);
        }

        [Test]
        public void 練習中は救済しても縁_福の連なり_救済数が増えない()
        {
            _score.SetPractice(true);
            for (int i = 0; i < 5; i++) Rescue();

            Assert.AreEqual(0, _score.En);
            Assert.AreEqual(0, _score.Combo);
            Assert.AreEqual(0, _score.MaxCombo);
            Assert.AreEqual(0, _score.RescueCount);
            Assert.AreEqual(0, _score.LastGain);
        }

        [Test]
        public void 練習中の誤投擲は福の連なりが切れる合図を出さない()
        {
            int misses = 0;
            _score.onMiss += () => misses++;
            _score.SetPractice(true);

            _score.RegisterCorrectHit(HitZone.Miss, rescued: false, rescueBaseScore: 100);
            _score.RegisterMiss();

            Assert.AreEqual(0, misses);
        }

        [Test]
        public void 練習中は笑顔の伝播の縁が入らない()
        {
            _score.SetPractice(true);
            Assert.AreEqual(0, _score.RegisterPropagation(new EnMultiplierSnapshot(1f, 1f)));
            Assert.AreEqual(0, _score.En);
        }

        [Test]
        public void 練習をやめてResetAllすると0から数える()
        {
            _score.SetPractice(true);
            Rescue();
            Rescue();

            _score.SetPractice(false);
            _score.ResetAll();
            Rescue();

            Assert.AreEqual(100, _score.En);
            Assert.AreEqual(1, _score.Combo);
            Assert.AreEqual(1, _score.RescueCount);
        }

        [Test]
        public void ResetAllは練習中かどうかを変えない()
        {
            _score.SetPractice(true);
            _rating.SetPractice(true);
            _score.ResetAll();
            _rating.ResetAll();

            Assert.IsTrue(_score.IsPractice);
            Assert.IsTrue(_rating.IsPractice);
        }

        [Test]
        public void 練習中は評価_ランク_最高ランクが動かない()
        {
            _rating.SetPractice(true);
            for (int i = 0; i < 30; i++) _rating.RegisterResolved(25f);
            _rating.RegisterAngry(20f);
            _rating.RegisterBlackShot();

            Assert.AreEqual(0f, _rating.Rating);
            Assert.AreEqual(ShrineRank.C, _rating.Rank);
            Assert.AreEqual(ShrineRank.C, _rating.MaxRank);
        }

        [Test]
        public void 練習をやめたら評価が動く()
        {
            _rating.SetPractice(true);
            _rating.RegisterResolved(10f);
            _rating.SetPractice(false);
            _rating.RegisterResolved(10f);

            Assert.AreEqual(10f, _rating.Rating);
        }
    }
}
