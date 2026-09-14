using NUnit.Framework;
using Toufuku.GameInput;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#50 1 条件 = 1 行。動画側の 4 列は空欄で書き出し、空欄は 0 ではなく未入力として読む。</summary>
    public class CooldownCsvTests
    {
        static CooldownConditionRecord NewRecord()
        {
            return new CooldownConditionRecord
            {
                testId = "T0-CD-1",
                date = "2026-09-15",
                owner = "ジュンガヤ",
                participantNo = 3,
                orderIndex = 2,
                trialIndex = 1,
                preset = CooldownPreset.Sec060,
                cooldownSeconds = 0.60f,
                singleTarget = 13,
                singleAccepted = 13,
                singleRejected = 1,
                pairTarget = 7,
                pairAccepted = 13,
                pairRejected = 2,
                pairCompleted = 6,
                intervalCount = 6,
                intervalMin = 0.612f,
                intervalMedian = 0.688f,
                intervalP75 = 0.731f,
                strapDeviation = 0,
                caseContact = 0,
                note = "最後の 2 組で腕が下がった"
            };
        }

        [Test]
        public void 見出しと行の列数が合う()
        {
            string[] header = AbTestCsv.SplitRow(CooldownCsv.Header);
            string[] row = AbTestCsv.SplitRow(CooldownCsv.RowOf(NewRecord()));

            Assert.AreEqual(CooldownCsv.ColumnCount, header.Length);
            Assert.AreEqual(CooldownCsv.ColumnCount, row.Length);
        }

        [Test]
        public void 書いて読み戻しても同じ()
        {
            CooldownConditionRecord written = NewRecord();
            written.videoSingleSwings = 13;
            written.videoPairSets = 7;
            written.videoExtraFires = 1;
            written.videoMissedSecond = 0;

            Assert.IsTrue(CooldownCsv.TryParseRow(CooldownCsv.RowOf(written), out CooldownConditionRecord read));

            Assert.AreEqual(written.testId, read.testId);
            Assert.AreEqual(written.date, read.date);
            Assert.AreEqual(written.owner, read.owner);
            Assert.AreEqual(written.participantNo, read.participantNo);
            Assert.AreEqual(written.orderIndex, read.orderIndex);
            Assert.AreEqual(written.trialIndex, read.trialIndex);
            Assert.AreEqual(written.preset, read.preset);
            Assert.AreEqual(written.cooldownSeconds, read.cooldownSeconds, 0.001f);
            Assert.AreEqual(written.singleAccepted, read.singleAccepted);
            Assert.AreEqual(written.singleRejected, read.singleRejected);
            Assert.AreEqual(written.pairCompleted, read.pairCompleted);
            Assert.AreEqual(written.intervalMin, read.intervalMin, 0.001f);
            Assert.AreEqual(written.intervalP75, read.intervalP75, 0.001f);
            Assert.AreEqual(written.videoExtraFires, read.videoExtraFires);
            Assert.AreEqual(written.videoMissedSecond, read.videoMissedSecond);
            Assert.AreEqual(written.note, read.note);
        }

        [Test]
        public void 動画側は空欄で書き出される()
        {
            string[] row = AbTestCsv.SplitRow(CooldownCsv.RowOf(NewRecord()));

            Assert.AreEqual("", row[18], "動画_単発振り数");
            Assert.AreEqual("", row[19], "動画_連投組数");
            Assert.AreEqual("", row[20], "動画_余分な発射");
            Assert.AreEqual("", row[21], "動画_2発目欠落");
        }

        [Test]
        public void 空欄はゼロではなく未入力として読む()
        {
            Assert.IsTrue(CooldownCsv.TryParseRow(CooldownCsv.RowOf(NewRecord()), out CooldownConditionRecord read));

            Assert.AreEqual(CooldownConditionRecord.NotEntered, read.videoExtraFires);
            Assert.IsFalse(read.IsComplete);
            StringAssert.Contains("動画", read.MissingFields());
        }

        [Test]
        public void ゼロと書けば確認済みになる()
        {
            CooldownConditionRecord record = NewRecord();
            record.videoSingleSwings = 13;
            record.videoPairSets = 7;
            record.videoExtraFires = 0;
            record.videoMissedSecond = 0;

            Assert.IsTrue(CooldownCsv.TryParseRow(CooldownCsv.RowOf(record), out CooldownConditionRecord read));
            Assert.AreEqual(0, read.videoExtraFires);
            Assert.IsTrue(read.IsComplete);
            Assert.AreEqual("", read.MissingFields());
            Assert.AreEqual(13 + 7 * 2, read.IntendedSwings);
        }

        [Test]
        public void カンマや引用符や改行が入っても列がずれない()
        {
            CooldownConditionRecord record = NewRecord();
            record.note = "1 回目は空振り, 2 回目は\"強め\"\n腕が下がった";

            string[] row = AbTestCsv.SplitRow(CooldownCsv.RowOf(record));
            Assert.AreEqual(CooldownCsv.ColumnCount, row.Length);

            Assert.IsTrue(CooldownCsv.TryParseRow(CooldownCsv.RowOf(record), out CooldownConditionRecord read));
            StringAssert.Contains("強め", read.note);
            Assert.IsFalse(read.note.Contains("\n"), "改行は空白へ置き換える");
        }

        [Test]
        public void 見出し行は記録として読まない()
        {
            Assert.IsFalse(CooldownCsv.TryParseRow(CooldownCsv.Header, out _));
            Assert.IsFalse(CooldownCsv.TryParseRow("", out _));
        }

        [Test]
        public void 暫定値はゲーム側の予定と受理の差で出る()
        {
            CooldownConditionRecord record = NewRecord();
            // 予定 13 + 7×2 = 27 回に対し 13 + 13 = 26 発。余分は 0、欠落は 7-6 = 1 組
            Assert.AreEqual(0, record.ProvisionalExtraFires);
            Assert.AreEqual(1, record.ProvisionalMissedSecond);

            record.singleAccepted = 15;
            Assert.AreEqual(1, record.ProvisionalExtraFires);
        }
    }
}
