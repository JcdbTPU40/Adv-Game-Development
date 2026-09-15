using System;
using NUnit.Framework;

namespace Toufuku.Scoring.Tests
{
    /// <summary>
    /// 今日のベストスコア — Issue #61（仕様書 v8 7章「HUDに出すもの」／13章「観戦体験の設計」）
    /// PlayerPrefs にさわらないように、メモリの保存先を使う。
    /// </summary>
    public class DailyBestScoreTests
    {
        class MemoryStore : IBestScoreStore
        {
            public string Date = "";
            public int Best;
            public int SaveCount;
            public string LoadDate() => Date;
            public int LoadBest() => Best;
            public void Save(string date, int best) { Date = date; Best = best; SaveCount++; }
        }

        DateTime _now;
        MemoryStore _store;
        DailyBestScore _best;

        [SetUp]
        public void SetUp()
        {
            _now = new DateTime(2027, 1, 23, 10, 0, 0);
            _store = new MemoryStore();
            _best = new DailyBestScore(_store, () => _now);
        }

        [Test]
        public void 最初は0()
        {
            Assert.AreEqual(0, _best.TodayBest);
        }

        [Test]
        public void ベストより大きいときだけ更新する()
        {
            Assert.IsTrue(_best.Submit(12000));
            Assert.AreEqual(12000, _best.TodayBest);

            Assert.IsFalse(_best.Submit(9000), "低いスコアでは更新しない");
            Assert.IsFalse(_best.Submit(12000), "同点では更新しない");
            Assert.AreEqual(12000, _best.TodayBest);
            Assert.AreEqual(1, _store.SaveCount);

            Assert.IsTrue(_best.Submit(12001));
            Assert.AreEqual(12001, _best.TodayBest);
        }

        [Test]
        public void 日付が変わったら0から数えなおす()
        {
            _best.Submit(18000);
            _now = _now.AddDays(1).Date.AddHours(9);

            Assert.AreEqual(0, _best.TodayBest);
            Assert.IsTrue(_best.Submit(5000), "前の日のベストより低くても、今日の最初の記録なので更新");
            Assert.AreEqual(5000, _best.TodayBest);
        }

        [Test]
        public void 同じ日なら時刻がちがっても同じベスト()
        {
            _best.Submit(7000);
            _now = _now.Date.AddHours(23).AddMinutes(59);
            Assert.AreEqual(7000, _best.TodayBest);
        }

        [Test]
        public void 零点は登録しない()
        {
            Assert.IsFalse(_best.Submit(0));
            Assert.AreEqual(0, _store.SaveCount);
        }

        [Test]
        public void 日付のキー()
        {
            Assert.AreEqual("2027-01-23", DailyBestScore.DateKeyOf(_now));
        }
    }
}
