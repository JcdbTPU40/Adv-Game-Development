using System;
using System.Globalization;
using System.Text;

namespace Toufuku.Playtest
{
    /// <summary>
    /// T6-USB の記録 CSV（1 イベント 1 行）— Issue #52
    ///
    /// ・時刻（input_time 〜 visible_time）は秒。受信・発射・表示は Unity の <c>Time.realtimeSinceStartupAsDouble</c>、
    ///   input_time だけはコントローラ側の時計（原点が違う）。input_unity はそれを Unity の時計へ合わせた推定。
    /// ・空欄は欠測。区切りと引用の扱いは #49 と同じ（<see cref="AbTestCsv.Escape"/> / <see cref="AbTestCsv.SplitRow"/>）。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class UsbGateCsv
    {
        public const string Header =
            "test_id,date,run,section,participant,seq,event,t," +
            "input_time,input_unity,receive_time,fire_time,visible_time," +
            "value,label,flag,detail";

        public const int ColumnCount = 17;

        static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static string RowOf(UsbGateEvent e)
        {
            if (e == null) return string.Empty;

            var sb = new StringBuilder(160);
            Add(sb, e.TestId);
            Add(sb, e.Date);
            Add(sb, e.Run.ToString(Culture));
            Add(sb, e.Section);
            Add(sb, e.Participant.ToString(Culture));
            Add(sb, e.Seq.ToString(Culture));
            Add(sb, e.Event);
            Add(sb, e.T.ToString("0.000", Culture));
            Add(sb, Time(e.InputTime));
            Add(sb, Time(e.InputUnity));
            Add(sb, Time(e.ReceiveTime));
            Add(sb, Time(e.FireTime));
            Add(sb, Time(e.VisibleTime));
            Add(sb, e.Value.HasValue ? e.Value.Value.ToString("0.######", Culture) : "");
            Add(sb, e.Label);
            Add(sb, e.Flag ? "1" : "0");
            Add(sb, e.Detail);
            return sb.ToString();
        }

        public static bool TryParseRow(string line, out UsbGateEvent e)
        {
            e = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.StartsWith("test_id", StringComparison.Ordinal)) return false; // 見出し行

            string[] f = AbTestCsv.SplitRow(line);
            if (f.Length < ColumnCount) return false;
            if (!int.TryParse(f[2], NumberStyles.Integer, Culture, out int run)) return false;
            if (string.IsNullOrEmpty(f[6])) return false;

            e = new UsbGateEvent
            {
                TestId = f[0],
                Date = f[1],
                Run = run,
                Section = f[3],
                Participant = ParseInt(f[4]),
                Seq = ParseInt(f[5]),
                Event = f[6],
                T = ParseDouble(f[7]) ?? 0.0,
                InputTime = ParseDouble(f[8]),
                InputUnity = ParseDouble(f[9]),
                ReceiveTime = ParseDouble(f[10]),
                FireTime = ParseDouble(f[11]),
                VisibleTime = ParseDouble(f[12]),
                Value = ParseDouble(f[13]),
                Label = f[14],
                Flag = f[15].Trim() == "1",
                Detail = f[16]
            };
            return true;
        }

        static string Time(double? value) => value.HasValue ? value.Value.ToString("0.000000", Culture) : "";

        static int ParseInt(string text) =>
            int.TryParse(text, NumberStyles.Integer, Culture, out int value) ? value : 0;

        static double? ParseDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return double.TryParse(text, NumberStyles.Float, Culture, out double value) ? value : (double?)null;
        }

        static void Add(StringBuilder sb, string field)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(AbTestCsv.Escape(field));
        }
    }
}
