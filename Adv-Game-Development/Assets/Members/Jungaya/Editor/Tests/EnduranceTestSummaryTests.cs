using System.Collections.Generic;
using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#53 9/10 完走・投数低下 20% 以内・疲労中央値 2 以下・7/10 再挑戦・安全事象 0、と実操作周期による人数案の除外。</summary>
    public class EnduranceTestSummaryTests
    {
        static readonly float[] TypicalCycles = { 1.1f, 1.2f, 1.2f, 1.3f };

        static EnduranceRecord Row(int no, bool completed = true, int first = 60, int last = 54,
            int fatigue = 2, bool retry = true, float[] cycles = null)
        {
            var r = new EnduranceRecord
            {
                participantNo = no,
                completed = completed,
                stopRequested = !completed,
                endSeconds = completed ? 180f : 100f,
                fatigue = fatigue
            };
            r.throwsPerMinute = new[] { first, (first + last) / 2, completed ? last : 0 };
            r.landingsPerMinute = (int[])r.throwsPerMinute.Clone();
            r.hitsPerMinute = new[] { first * 8 / 10, first * 7 / 10, completed ? last * 6 / 10 : 0 };
            r.cycles.AddRange(cycles ?? TypicalCycles);
            r.SetRetry(retry);
            return r;
        }

        /// <summary>全員完走・低下 10%・疲労 2・再挑戦 8 人。</summary>
        static List<EnduranceRecord> TypicalRecords()
        {
            var list = new List<EnduranceRecord>();
            for (int i = 1; i <= 10; i++) list.Add(Row(i, retry: i <= 8));
            return list;
        }

        [Test]
        public void 典型的な十人は合格する()
        {
            EnduranceTestSummary s = EnduranceTestSummary.Of(TypicalRecords());

            Assert.AreEqual(10, s.Count);
            Assert.AreEqual(10, s.Completed);
            Assert.AreEqual(0.1, s.ThrowsDropMedian.Value, 1e-9);
            Assert.AreEqual(2.0, s.FatigueMedian.Value, 1e-9);
            Assert.AreEqual(8, s.Retry);
            Assert.AreEqual(600, s.ThrowsPerMinute[0]);
            Assert.AreEqual(0.8, s.HitRatePerMinute[0].Value, 1e-9);
            Assert.IsTrue(s.Passed, s.FailureSummary());
        }

        [Test]
        public void 完走が八人だと不合格()
        {
            var records = TypicalRecords();
            records[0] = Row(1, completed: false);
            records[1] = Row(2, completed: false);

            EnduranceTestSummary s = EnduranceTestSummary.Of(records);
            Assert.AreEqual(8, s.Completed);
            Assert.AreEqual(2, s.StoppedEarly);
            Assert.AreEqual(8, s.ThrowsDropSamples, "途中終了の人は投数低下に入れない");
            Assert.IsFalse(s.Passed);
            StringAssert.Contains("3 分完走", s.FailureSummary());
        }

        [Test]
        public void 投数低下は二十パーセントちょうどまで合格()
        {
            var records = new List<EnduranceRecord>();
            for (int i = 1; i <= 10; i++) records.Add(Row(i, first: 50, last: 40));

            EnduranceTestSummary s = EnduranceTestSummary.Of(records);
            Assert.AreEqual(0.2, s.ThrowsDropMedian.Value, 1e-9);
            Assert.IsTrue(s.Passed, s.FailureSummary());

            records[0] = Row(1, first: 50, last: 39);
            records[1] = Row(2, first: 50, last: 39);
            records[2] = Row(3, first: 50, last: 39);
            records[3] = Row(4, first: 50, last: 39);
            records[4] = Row(5, first: 50, last: 39);
            records[5] = Row(6, first: 50, last: 39);
            Assert.IsFalse(EnduranceTestSummary.Of(records).Passed, "過半数が 22% 低下なら中央値も超える");
        }

        [Test]
        public void 疲労の中央値が二点五なら不合格()
        {
            var records = new List<EnduranceRecord>();
            for (int i = 1; i <= 10; i++) records.Add(Row(i, fatigue: i <= 5 ? 2 : 3));

            EnduranceTestSummary s = EnduranceTestSummary.Of(records);
            Assert.AreEqual(2.5, s.FatigueMedian.Value, 1e-9);
            Assert.IsFalse(s.Passed);
            StringAssert.Contains("疲労", s.FailureSummary());
        }

        [Test]
        public void 再挑戦が六人なら不合格()
        {
            var records = new List<EnduranceRecord>();
            for (int i = 1; i <= 10; i++) records.Add(Row(i, retry: i <= 6));

            Assert.IsFalse(EnduranceTestSummary.Of(records).Passed);
        }

        [Test]
        public void 安全事象は記入途中の行でも数える()
        {
            var records = TypicalRecords();
            EnduranceRecord partial = Row(11, fatigue: 0);
            partial.pain = true;
            records.Add(partial);

            EnduranceTestSummary s = EnduranceTestSummary.Of(records);
            Assert.AreEqual(1, s.Incomplete);
            Assert.AreEqual(10, s.Count);
            Assert.AreEqual(1, s.SafetyIncidents);
            Assert.IsFalse(s.Passed);
        }

        [Test]
        public void 記録人数が足りなければ不合格()
        {
            var records = TypicalRecords();
            records.RemoveAt(9);
            Assert.IsFalse(EnduranceTestSummary.Of(records).Passed);
        }

        [Test]
        public void 周期が想定内なら人数案を外さない()
        {
            EnduranceTestSummary s = EnduranceTestSummary.Of(TypicalRecords());

            Assert.AreEqual(40, s.CycleCount, "全員分の発射間隔を束ねる");
            Assert.AreEqual(1.2, s.CycleMedian.Value, 1e-6);
            Assert.AreEqual(1.225, s.CycleP75.Value, 1e-6);
            Assert.IsTrue(s.CycleWithinExpected);
            foreach (WaveCandidateCheck w in s.Waves) Assert.IsFalse(w.Excluded, w.Describe(s.CycleP75));
        }

        [Test]
        public void 周期が想定より遅ければp75より短い周期を要求する案を外す()
        {
            var records = new List<EnduranceRecord>();
            float[] slow = { 1.30f, 1.35f, 1.40f, 1.45f, 1.50f };
            for (int i = 1; i <= 10; i++) records.Add(Row(i, cycles: slow));

            EnduranceTestSummary s = EnduranceTestSummary.Of(records);
            Assert.AreEqual(1.45, s.CycleP75.Value, 1e-6);
            Assert.IsTrue(s.CycleOutOfExpected, "p75 が 1.4 秒を超えた");

            Assert.AreEqual(3, s.Waves[0].Candidate.Added);
            Assert.IsFalse(s.Waves[0].Excluded, "+3 は 1.47 秒まで許すので残る");
            Assert.IsTrue(s.Waves[1].Excluded, "+4 は 1.37 秒を要求する");
            Assert.IsTrue(s.Waves[2].Excluded, "+5 は 1.28 秒を要求する");
            Assert.IsTrue(s.Passed, "周期は合否の条件ではない");
        }

        [Test]
        public void 周期のデータが無ければ判定しない()
        {
            var records = new List<EnduranceRecord>();
            for (int i = 1; i <= 10; i++) records.Add(Row(i, cycles: new float[0]));

            EnduranceTestSummary s = EnduranceTestSummary.Of(records);
            Assert.IsNull(s.CycleMedian);
            Assert.IsFalse(s.CycleWithinExpected);
            Assert.IsFalse(s.CycleOutOfExpected);
            foreach (WaveCandidateCheck w in s.Waves) Assert.IsFalse(w.Excluded);
        }
    }
}
