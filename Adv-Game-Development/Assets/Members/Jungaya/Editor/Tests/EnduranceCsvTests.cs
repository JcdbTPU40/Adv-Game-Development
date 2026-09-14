using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#53 1 人 1 行の CSV。未到達の分・未回答は空欄で、0 と区別する。</summary>
    public class EnduranceCsvTests
    {
        static EnduranceRecord Completed()
        {
            var r = new EnduranceRecord
            {
                testId = "T0-3M-1",
                date = "2026-09-20",
                participantNo = 3,
                owner = "Jungaya",
                cooldownSeconds = 0.5f,
                feedbackLabel = "案A",
                endSeconds = 180f,
                completed = true,
                rejected = 4,
                gripChanges = 2,
                fatigue = 2,
                note = "笑顔"
            };
            r.throwsPerMinute = new[] { 50, 48, 45 };
            r.landingsPerMinute = new[] { 50, 48, 45 };
            r.hitsPerMinute = new[] { 40, 36, 30 };
            r.cycles.AddRange(new[] { 1.1f, 1.25f, 1.3f });
            r.MarkArmDrop(150.4);
            r.SetRetry(true);
            return r;
        }

        [Test]
        public void 見出しと行の列数が一致する()
        {
            Assert.AreEqual(EnduranceCsv.ColumnCount, AbTestCsv.SplitRow(EnduranceCsv.Header).Length);
            Assert.AreEqual(EnduranceCsv.ColumnCount, AbTestCsv.SplitRow(EnduranceCsv.RowOf(Completed())).Length);
        }

        [Test]
        public void 書いて読み戻すと同じ記録になる()
        {
            EnduranceRecord original = Completed();
            Assert.IsTrue(EnduranceCsv.TryParseRow(EnduranceCsv.RowOf(original), out EnduranceRecord r));

            Assert.AreEqual(original.testId, r.testId);
            Assert.AreEqual(original.date, r.date);
            Assert.AreEqual(3, r.participantNo);
            Assert.AreEqual("Jungaya", r.owner);
            Assert.AreEqual(0.5f, r.cooldownSeconds, 1e-4f);
            Assert.AreEqual("案A", r.feedbackLabel);
            Assert.AreEqual(180f, r.endSeconds, 1e-4f);
            Assert.IsTrue(r.completed);
            Assert.IsFalse(r.stopRequested);
            CollectionAssert.AreEqual(original.throwsPerMinute, r.throwsPerMinute);
            CollectionAssert.AreEqual(original.landingsPerMinute, r.landingsPerMinute);
            CollectionAssert.AreEqual(original.hitsPerMinute, r.hitsPerMinute);
            Assert.AreEqual(4, r.rejected);
            Assert.AreEqual(3, r.CycleCount);
            Assert.AreEqual(original.CycleP75.Value, r.CycleP75.Value, 1e-3);
            Assert.AreEqual(2, r.gripChanges);
            Assert.AreEqual(1, r.armDrops);
            Assert.AreEqual(150.4f, r.firstArmDropSec, 0.051f);
            Assert.AreEqual(2, r.fatigue);
            Assert.IsTrue(r.retryAnswered);
            Assert.IsTrue(r.retry);
            Assert.AreEqual("笑顔", r.note);
            Assert.AreEqual(0.1, r.ThrowsDropRatio.Value, 1e-9);
        }

        [Test]
        public void 到達していない分は空欄で書き欠測として読む()
        {
            var r = new EnduranceRecord { participantNo = 1, endSeconds = 95f, completed = false, stopRequested = true };
            r.throwsPerMinute = new[] { 40, 30, 0 };
            r.landingsPerMinute = new[] { 40, 30, 0 };
            r.hitsPerMinute = new[] { 30, 20, 0 };

            string[] f = AbTestCsv.SplitRow(EnduranceCsv.RowOf(r));
            Assert.AreEqual("30", f[11]);
            Assert.AreEqual("", f[12], "3 分目の投数は 0 ではなく空欄");
            Assert.AreEqual("", f[21], "3 分目の命中率も空欄");
            Assert.AreEqual("", f[22], "完走していないので投数低下率は欠測");

            Assert.IsTrue(EnduranceCsv.TryParseRow(EnduranceCsv.RowOf(r), out EnduranceRecord back));
            Assert.IsNull(back.ThrowsOf(2));
            Assert.IsTrue(back.stopRequested);
            Assert.IsFalse(back.completed);
        }

        [Test]
        public void 最終分に届いていなければ完走でも投数低下は欠測()
        {
            // 試技秒を 60 秒に縮めた確認プレイ（完走扱いだが 3 分目に入っていない）
            var r = new EnduranceRecord { participantNo = 1, endSeconds = 60f, completed = true };
            r.throwsPerMinute = new[] { 40, 0, 0 };

            Assert.IsNull(r.ThrowsDropRatio);
            Assert.AreEqual("", AbTestCsv.SplitRow(EnduranceCsv.RowOf(r))[22]);
        }

        [Test]
        public void 未回答は空欄で書き別の遊びと区別する()
        {
            var r = new EnduranceRecord { participantNo = 2, endSeconds = 180f, completed = true };
            string[] f = AbTestCsv.SplitRow(EnduranceCsv.RowOf(r));
            Assert.AreEqual("", f[30], "疲労");
            Assert.AreEqual("", f[34], "再挑戦");

            Assert.IsTrue(EnduranceCsv.TryParseRow(EnduranceCsv.RowOf(r), out EnduranceRecord unanswered));
            Assert.IsFalse(unanswered.retryAnswered);
            Assert.IsFalse(unanswered.IsComplete);

            r.SetRetry(false);
            r.fatigue = 4;
            Assert.IsTrue(EnduranceCsv.TryParseRow(EnduranceCsv.RowOf(r), out EnduranceRecord declined));
            Assert.IsTrue(declined.retryAnswered);
            Assert.IsFalse(declined.retry);
            Assert.IsTrue(declined.IsComplete);
        }

        [Test]
        public void 所見にカンマや改行があっても列がずれない()
        {
            EnduranceRecord r = Completed();
            r.note = "腕が重い, と言った\n\"もう無理\"";

            string row = EnduranceCsv.RowOf(r);
            Assert.AreEqual(EnduranceCsv.ColumnCount, AbTestCsv.SplitRow(row).Length);
            Assert.IsTrue(EnduranceCsv.TryParseRow(row, out EnduranceRecord back));
            Assert.AreEqual("腕が重い, と言った \"もう無理\"", back.note);
            Assert.AreEqual(3, back.CycleCount);
        }

        [Test]
        public void 見出し行は記録として読まない()
        {
            Assert.IsFalse(EnduranceCsv.TryParseRow(EnduranceCsv.Header, out _));
            Assert.IsFalse(EnduranceCsv.TryParseRow("", out _));
        }
    }
}
