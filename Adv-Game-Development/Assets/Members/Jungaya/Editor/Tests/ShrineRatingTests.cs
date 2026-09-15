using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// 神社の評価・ランク・称号 — Issue #61（仕様書 v8 7章「神社の評価・ランク表示」「称号は最高ランクで出す」／付録B RANK.*）
    /// </summary>
    public class ShrineRatingTests
    {
        GameObject _go;
        ShrineRating _rating;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("ShrineRatingUnderTest");
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

        void Rescue(int times, float gain = 10f)
        {
            for (int i = 0; i < times; i++) _rating.RegisterResolved(gain);
        }

        [Test]
        public void 評価値0ランクCから始まる()
        {
            Assert.AreEqual(0f, _rating.Rating);
            Assert.AreEqual(300f, _rating.RatingMax);
            Assert.AreEqual(ShrineRank.C, _rating.Rank);
            Assert.AreEqual(ShrineRank.C, _rating.MaxRank);
        }

        [Test]
        public void 救済を重ねて60でBに上がる()
        {
            Rescue(5);
            Assert.AreEqual(ShrineRank.C, _rating.Rank);
            Rescue(1);
            Assert.AreEqual(60f, _rating.Rating);
            Assert.AreEqual(ShrineRank.B, _rating.Rank);
            Assert.AreEqual(ShrineRank.B, _rating.MaxRank);
        }

        [Test]
        public void 評価が下がっても称号はプレイ中の最高ランクのまま()
        {
            Rescue(15);                          // 150 → A
            Assert.AreEqual(ShrineRank.A, _rating.Rank);

            // 終盤の負荷ウェーブで黒客化が続く
            for (int i = 0; i < 4; i++) _rating.RegisterAngry(30f); // 150 → 30

            Assert.AreEqual(30f, _rating.Rating);
            Assert.AreEqual(ShrineRank.C, _rating.Rank, "HUD は今のランク");
            Assert.AreEqual(ShrineRank.A, _rating.MaxRank, "リザルトの称号は最高ランク");
        }

        [Test]
        public void 境界の近くではランクがちらつかない()
        {
            Rescue(6);                           // 60 → B
            _rating.RegisterAngry(15f);          // 45 → B のまま（降格は 40 未満）
            Assert.AreEqual(ShrineRank.B, _rating.Rank);
            _rating.RegisterAngry(10f);          // 35 → C
            Assert.AreEqual(ShrineRank.C, _rating.Rank);
            Rescue(2);                           // 55 → C のまま（昇格は 60 以上）
            Assert.AreEqual(ShrineRank.C, _rating.Rank);
        }

        [Test]
        public void 黒客に通常弾を当てるとマイナス15()
        {
            Rescue(3);
            _rating.RegisterBlackShot();
            Assert.AreEqual(15f, _rating.Rating);
        }

        [Test]
        public void 評価値は0から300で止まる()
        {
            _rating.RegisterAngry(20f);
            Assert.AreEqual(0f, _rating.Rating);

            Rescue(40);
            Assert.AreEqual(300f, _rating.Rating);
            Assert.AreEqual(ShrineRank.S, _rating.Rank);
        }

        [Test]
        public void 固定したあとは評価もランクも動かない()
        {
            Rescue(6);
            _rating.Lock();
            Rescue(20);
            _rating.RegisterAngry(30f);

            Assert.AreEqual(60f, _rating.Rating);
            Assert.AreEqual(ShrineRank.B, _rating.MaxRank);
        }

        [Test]
        public void リセットで最高ランクもCに戻り固定も外れる()
        {
            Rescue(25);
            _rating.Lock();
            _rating.ResetAll();

            Assert.IsFalse(_rating.IsLocked);
            Assert.AreEqual(ShrineRank.C, _rating.Rank);
            Assert.AreEqual(ShrineRank.C, _rating.MaxRank);
            Rescue(1);
            Assert.AreEqual(10f, _rating.Rating);
        }

        [Test]
        public void 最高ランクが上がったときだけ通知する()
        {
            int count = 0;
            _rating.onMaxRankChanged.AddListener(_ => count++);

            Rescue(6);                       // B
            _rating.RegisterAngry(30f);      // C（最高ランクは B のまま）
            Rescue(3);                       // 60 → B（最高ランクは変わらない）
            Assert.AreEqual(1, count);
        }
    }
}
