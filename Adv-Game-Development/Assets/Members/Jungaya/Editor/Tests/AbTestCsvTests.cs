using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#49 1 人 = 1 行。自由記述にカンマや改行が入っても列がずれない。</summary>
    public class AbTestCsvTests
    {
        static AbParticipantRecord Sample()
        {
            var r = new AbParticipantRecord
            {
                testId = "T0-AB-1",
                date = "2026-09-15",
                owner = "純ヶ谷",
                participantNo = 3,
                order = AbTestPlan.OrderOf(3),
                note = "笑った",
                reason = "音が先で気持ちいい"
            };
            r.SetThrows(VariantId.A, 21, 4);
            r.SetThrows(VariantId.B, 17, 2);
            r.SetFeel(VariantId.A, 5);
            r.SetFeel(VariantId.B, 3);
            r.SetAgain(VariantId.A, true);
            r.SetAgain(VariantId.B, false);
            r.SetAdopted(VariantId.A);
            return r;
        }

        [Test]
        public void 見出しと行の列数がそろう()
        {
            Assert.AreEqual(AbTestCsv.ColumnCount, AbTestCsv.SplitRow(AbTestCsv.Header).Length);
            Assert.AreEqual(AbTestCsv.ColumnCount, AbTestCsv.SplitRow(AbTestCsv.RowOf(Sample())).Length);
        }

        [Test]
        public void 書いて読み戻すと同じ内容になる()
        {
            AbParticipantRecord source = Sample();
            Assert.IsTrue(AbTestCsv.TryParseRow(AbTestCsv.RowOf(source), out AbParticipantRecord back));

            Assert.AreEqual(source.testId, back.testId);
            Assert.AreEqual(source.date, back.date);
            Assert.AreEqual(source.owner, back.owner);
            Assert.AreEqual(source.participantNo, back.participantNo);
            Assert.AreEqual(source.order, back.order);
            Assert.AreEqual(source.throwsA, back.throwsA);
            Assert.AreEqual(source.throwsB, back.throwsB);
            Assert.AreEqual(source.rejectedA, back.rejectedA);
            Assert.AreEqual(source.rejectedB, back.rejectedB);
            Assert.AreEqual(source.feelA, back.feelA);
            Assert.AreEqual(source.feelB, back.feelB);
            Assert.AreEqual(source.againA, back.againA);
            Assert.AreEqual(source.againB, back.againB);
            Assert.AreEqual(source.adopted, back.adopted);
            Assert.AreEqual(source.reason, back.reason);
            Assert.AreEqual(source.note, back.note);
            Assert.IsTrue(back.IsComplete);
        }

        [Test]
        public void カンマと引用符と改行を含む理由でも列がずれない()
        {
            AbParticipantRecord r = Sample();
            r.reason = "速い、軽い\n「振った」感じが\"強い\"";

            string row = AbTestCsv.RowOf(r);
            Assert.AreEqual(AbTestCsv.ColumnCount, AbTestCsv.SplitRow(row).Length);

            Assert.IsTrue(AbTestCsv.TryParseRow(row, out AbParticipantRecord back));
            // 1 行に収めるため改行は空白になる
            Assert.AreEqual("速い、軽い 「振った」感じが\"強い\"", back.reason);
            Assert.AreEqual(r.note, back.note);
        }

        [Test]
        public void 未回答は未回答のまま残る()
        {
            var r = new AbParticipantRecord { participantNo = 1, date = "2026-09-15" };
            string row = AbTestCsv.RowOf(r);
            StringAssert.Contains("未回答", row);

            Assert.IsTrue(AbTestCsv.TryParseRow(row, out AbParticipantRecord back));
            Assert.IsFalse(back.againAnsweredA);
            Assert.IsFalse(back.againAnsweredB);
            Assert.IsFalse(back.adoptedAnswered);
            Assert.IsFalse(back.IsComplete);
            StringAssert.Contains("採用案", back.MissingFields());
        }

        [Test]
        public void 見出し行は記録として読まない()
        {
            Assert.IsFalse(AbTestCsv.TryParseRow(AbTestCsv.Header, out _));
            Assert.IsFalse(AbTestCsv.TryParseRow("", out _));
            Assert.IsFalse(AbTestCsv.TryParseRow("こわれた行", out _));
        }

        [Test]
        public void 安全と同期ずれも往復する()
        {
            AbParticipantRecord r = Sample();
            r.syncComplaint = true;
            r.pain = true;
            r.strapDeviation = true;

            Assert.IsTrue(AbTestCsv.TryParseRow(AbTestCsv.RowOf(r), out AbParticipantRecord back));
            Assert.IsTrue(back.syncComplaint);
            Assert.IsTrue(back.pain);
            Assert.IsFalse(back.fear);
            Assert.IsTrue(back.strapDeviation);
            Assert.IsTrue(back.HasSafetyIncident);
        }
    }
}
