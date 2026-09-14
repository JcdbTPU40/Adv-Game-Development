using System.Collections.Generic;
using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>
    /// #49 完了条件: 8/10 が採用案を手応え 4 以上、8/10 が「すぐもう一度振りたい」、
    /// 7/10 以上が同じ案、同期ずれの指摘 2/10 以下、痛み・恐怖・ストラップ逸脱 0 件。
    /// </summary>
    public class AbTestSummaryTests
    {
        /// <summary>採用案に満足している 1 人分。</summary>
        static AbParticipantRecord Happy(int no, VariantId adopted, int feel = 5, bool again = true)
        {
            var r = new AbParticipantRecord
            {
                participantNo = no,
                order = AbTestPlan.OrderOf(no)
            };
            r.SetFeel(VariantId.A, adopted == VariantId.A ? feel : 2);
            r.SetFeel(VariantId.B, adopted == VariantId.B ? feel : 2);
            r.SetAgain(VariantId.A, adopted == VariantId.A && again);
            r.SetAgain(VariantId.B, adopted == VariantId.B && again);
            r.SetAdopted(adopted);
            return r;
        }

        /// <summary>案A を 8 人、案B を 2 人が選び、全員が満足している 10 人分。</summary>
        static List<AbParticipantRecord> TenPassing()
        {
            var list = new List<AbParticipantRecord>();
            for (int i = 1; i <= 10; i++)
                list.Add(Happy(i, i <= 8 ? VariantId.A : VariantId.B));
            return list;
        }

        [Test]
        public void 十人そろって満たせば合格()
        {
            AbTestSummary s = AbTestSummary.Of(TenPassing());

            Assert.AreEqual(10, s.Count);
            Assert.AreEqual(8, s.AdoptedA);
            Assert.AreEqual(2, s.AdoptedB);
            Assert.AreEqual(VariantId.A, s.Majority);
            Assert.AreEqual(8, s.MajorityCount);
            Assert.IsTrue(s.Passed, s.FailureSummary());
            Assert.AreEqual("-", s.FailureSummary());
        }

        [Test]
        public void 人数が足りなければ合格にしない()
        {
            List<AbParticipantRecord> records = TenPassing();
            records.RemoveAt(9);

            AbTestSummary s = AbTestSummary.Of(records);
            Assert.AreEqual(9, s.Count);
            Assert.IsFalse(s.Passed);
            StringAssert.Contains("記録人数", s.FailureSummary());
        }

        [Test]
        public void 採用案の手応えが四未満の人が三人いると不合格()
        {
            List<AbParticipantRecord> records = TenPassing();
            for (int i = 0; i < 3; i++) records[i].SetFeel(records[i].adopted, 3);

            AbTestSummary s = AbTestSummary.Of(records);
            Assert.AreEqual(7, s.FeelOk);
            Assert.IsFalse(s.Passed);
            StringAssert.Contains("手応え", s.FailureSummary());
        }

        [Test]
        public void もう一度振りたいが七人なら不合格()
        {
            List<AbParticipantRecord> records = TenPassing();
            for (int i = 0; i < 3; i++) records[i].SetAgain(records[i].adopted, false);

            AbTestSummary s = AbTestSummary.Of(records);
            Assert.AreEqual(7, s.AgainOk);
            Assert.IsFalse(s.Passed);
        }

        [Test]
        public void 選ばれた案が割れると不合格()
        {
            var records = new List<AbParticipantRecord>();
            for (int i = 1; i <= 10; i++)
                records.Add(Happy(i, i <= 6 ? VariantId.A : VariantId.B));

            AbTestSummary s = AbTestSummary.Of(records);
            Assert.AreEqual(6, s.MajorityCount);
            Assert.IsFalse(s.Passed);
            StringAssert.Contains("同じ案", s.FailureSummary());
        }

        [Test]
        public void 同期ずれの指摘は二人までなら合格()
        {
            List<AbParticipantRecord> records = TenPassing();
            records[0].syncComplaint = true;
            records[1].syncComplaint = true;
            Assert.IsTrue(AbTestSummary.Of(records).Passed);

            records[2].syncComplaint = true;
            AbTestSummary s = AbTestSummary.Of(records);
            Assert.AreEqual(3, s.SyncComplaints);
            Assert.IsFalse(s.Passed);
        }

        [Test]
        public void 痛みや恐怖やストラップ逸脱が一件でもあれば不合格()
        {
            List<AbParticipantRecord> records = TenPassing();
            records[4].fear = true;

            AbTestSummary s = AbTestSummary.Of(records);
            Assert.AreEqual(1, s.SafetyIncidents);
            Assert.IsFalse(s.Passed);
            StringAssert.Contains("ストラップ", s.FailureSummary());
        }

        [Test]
        public void 記入が足りない記録は数に入れない()
        {
            List<AbParticipantRecord> records = TenPassing();
            records.Add(new AbParticipantRecord { participantNo = 11 });

            AbTestSummary s = AbTestSummary.Of(records);
            Assert.AreEqual(10, s.Count);
            Assert.AreEqual(1, s.Incomplete);
            Assert.IsTrue(s.Passed, s.FailureSummary());
        }

        [Test]
        public void 空でも落ちない()
        {
            AbTestSummary s = AbTestSummary.Of(null);
            Assert.AreEqual(0, s.Count);
            Assert.IsFalse(s.Passed);
        }
    }
}
