using NUnit.Framework;
using Toufuku.Playtest;

namespace Toufuku.Playtest.Tests
{
    /// <summary>#52 記録 CSV（1 イベント 1 行・17 列）の書き出しと読み戻し。</summary>
    public class UsbGateCsvTests
    {
        [Test]
        public void 見出しと行の列数が同じ()
        {
            Assert.AreEqual(UsbGateCsv.ColumnCount, UsbGateCsv.Header.Split(',').Length);
            string row = UsbGateCsv.RowOf(new UsbGateEvent { Event = UsbGateEventType.Cue });
            Assert.AreEqual(UsbGateCsv.ColumnCount, AbTestCsv.SplitRow(row).Length);
        }

        [Test]
        public void 書いて読み戻すと同じ()
        {
            var e = new UsbGateEvent
            {
                TestId = "T6-USB-1",
                Date = "2026-09-15",
                Run = 3,
                Section = UsbGatePlan.KeyOf(UsbGateSection.Throws100),
                Participant = 0,
                Seq = 42,
                Event = UsbGateEventType.Fire,
                T = 85.4321,
                InputTime = 1234.567,
                InputUnity = 3456.789012,
                ReceiveTime = 3456.801234,
                FireTime = 3456.801500,
                VisibleTime = 3456.835000,
                Value = 512.25,
                Label = "near",
                Flag = true,
                Detail = "Normal"
            };

            Assert.IsTrue(UsbGateCsv.TryParseRow(UsbGateCsv.RowOf(e), out UsbGateEvent r));
            Assert.AreEqual(e.TestId, r.TestId);
            Assert.AreEqual(e.Date, r.Date);
            Assert.AreEqual(3, r.Run);
            Assert.AreEqual(e.Section, r.Section);
            Assert.AreEqual(42, r.Seq);
            Assert.AreEqual(e.Event, r.Event);
            Assert.AreEqual(85.432, r.T, 1e-9);
            Assert.AreEqual(1234.567, r.InputTime.Value, 1e-6);
            Assert.AreEqual(3456.789012, r.InputUnity.Value, 1e-6);
            Assert.AreEqual(3456.801234, r.ReceiveTime.Value, 1e-6);
            Assert.AreEqual(3456.8015, r.FireTime.Value, 1e-6);
            Assert.AreEqual(3456.835, r.VisibleTime.Value, 1e-6);
            Assert.AreEqual(512.25, r.Value.Value, 1e-9);
            Assert.AreEqual("near", r.Label);
            Assert.IsTrue(r.Flag);
            Assert.AreEqual("Normal", r.Detail);
        }

        [Test]
        public void 空欄は欠測のまま読み戻す()
        {
            var e = new UsbGateEvent { Run = 1, Event = UsbGateEventType.Fire, ReceiveTime = 10.0 };
            Assert.IsTrue(UsbGateCsv.TryParseRow(UsbGateCsv.RowOf(e), out UsbGateEvent r));
            Assert.IsNull(r.InputTime);
            Assert.IsNull(r.InputUnity);
            Assert.IsNull(r.VisibleTime);
            Assert.IsNull(r.Value);
            Assert.AreEqual(10.0, r.ReceiveTime.Value, 1e-9);
            Assert.IsFalse(r.Flag);
        }

        [Test]
        public void 所見のカンマと引用符は残し_改行は空白にして一行に収める()
        {
            var e = new UsbGateEvent { Run = 1, Event = UsbGateEventType.Env, Label = "note", Detail = "ケーブル, 交換\n\"2 本目\"" };
            string row = UsbGateCsv.RowOf(e);
            StringAssert.DoesNotContain("\n", row, "1 イベント 1 行（行ごとに読み戻す）を保つ");
            Assert.IsTrue(UsbGateCsv.TryParseRow(row, out UsbGateEvent r));
            Assert.AreEqual("ケーブル, 交換 \"2 本目\"", r.Detail);
        }

        [Test]
        public void 見出し行と壊れた行は読まない()
        {
            Assert.IsFalse(UsbGateCsv.TryParseRow(UsbGateCsv.Header, out _));
            Assert.IsFalse(UsbGateCsv.TryParseRow("", out _));
            Assert.IsFalse(UsbGateCsv.TryParseRow("a,b,c", out _));
        }

        [Test]
        public void 区間の名前は往復できる()
        {
            foreach (UsbGateSection s in (UsbGateSection[])System.Enum.GetValues(typeof(UsbGateSection)))
            {
                Assert.IsTrue(UsbGatePlan.TryParseSection(UsbGatePlan.KeyOf(s), out UsbGateSection back));
                Assert.AreEqual(s, back);
            }
            Assert.IsFalse(UsbGatePlan.TryParseSection("unknown", out _));
        }
    }
}
