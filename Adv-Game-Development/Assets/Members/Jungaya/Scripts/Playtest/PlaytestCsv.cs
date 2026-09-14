using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 計測ログの CSV 書式 — Issue #63
    ///
    /// ・UTF-8（BOM 付き・CRLF）。Excel でそのまま開いても日本語が化けない。
    /// ・先頭に <c>#meta,キー,値</c> の行（テストID・日付・ビルド番号・シード値・パラメータ）を並べ、そのあとに列名の行とデータ行。
    ///   pandas なら <c>read_csv(path, comment='#')</c> でメタ行を飛ばせる。
    /// ・数値は小数点ピリオド固定（OS の地域設定に左右されない）。未設定の値は空欄、真偽は 1 / 0。
    /// </summary>
    public static class PlaytestCsv
    {
        public const string FormatVersion = "1";
        public const string MetaMarker = "#meta";
        public const string NewLine = "\r\n";

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly char[] s_special = { ',', '"', '\r', '\n' };

        /// <summary>イベントログの列。<see cref="PlaytestEvent"/> のフィールドと同じ並び。</summary>
        public static readonly string[] EventColumns =
        {
            "t", "realtime", "frame", "event", "throw_no", "target_id", "category", "color", "omamori", "black",
            "accuracy", "zone", "color_error", "gain", "en", "multiplier", "fuku_chain", "rescued", "priority_target_id",
            "input_time", "receive_time", "fire_time", "strength", "flight_sec", "distance_m", "pos_x", "pos_z",
            "danger", "rating", "rank", "stage", "value", "detail"
        };

        public static readonly string[] SummaryColumns = { "section", "metric", "value" };

        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(s_special) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string Num(double? value, string format = "0.######")
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value)) return "";
            return value.Value.ToString(format, Inv);
        }

        public static string Int(int? value)
        {
            return value.HasValue ? value.Value.ToString(Inv) : "";
        }

        public static string Bool(bool? value)
        {
            return value.HasValue ? (value.Value ? "1" : "0") : "";
        }

        public static string Line(params string[] cells)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(cells[i]));
            }
            return sb.ToString();
        }

        public static string EventRow(PlaytestEvent e)
        {
            return Line(
                Num(e.T, "0.000"), Num(e.Realtime, "0.00000"), e.Frame.ToString(Inv), e.Type,
                Int(e.ThrowNo), Int(e.TargetId), e.Category, e.Color, e.Omamori, Bool(e.IsBlack),
                Num(e.Accuracy, "0.000"), e.Zone, Bool(e.ColorError), Int(e.Gain), Int(e.En), Num(e.Multiplier, "0.###"),
                Int(e.FukuChain), Bool(e.Rescued), Int(e.PriorityTargetId),
                Num(e.InputTime, "0.00000"), Num(e.ReceiveTime, "0.00000"), Num(e.FireTime, "0.00000"),
                Num(e.Strength, "0.#"), Num(e.FlightSeconds, "0.000"), Num(e.Distance, "0.00"),
                Num(e.PosX, "0.00"), Num(e.PosZ, "0.00"), Num(e.Danger, "0.##"), Num(e.Rating, "0.##"), e.Rank,
                Int(e.Stage), Num(e.Value, "0.###"), e.Detail);
        }

        public static string BuildEvents(IReadOnlyList<KeyValuePair<string, string>> meta, IReadOnlyList<PlaytestEvent> events)
        {
            var sb = new StringBuilder();
            AppendMeta(sb, meta);
            sb.Append(Line(EventColumns)).Append(NewLine);
            if (events != null)
            {
                foreach (PlaytestEvent e in events)
                    sb.Append(EventRow(e)).Append(NewLine);
            }
            return sb.ToString();
        }

        public static string BuildSummary(IReadOnlyList<KeyValuePair<string, string>> meta, IReadOnlyList<PlaytestSummaryRow> rows)
        {
            var sb = new StringBuilder();
            AppendMeta(sb, meta);
            sb.Append(Line(SummaryColumns)).Append(NewLine);
            if (rows != null)
            {
                foreach (PlaytestSummaryRow row in rows)
                    sb.Append(Line(row.Section, row.Metric, row.Value)).Append(NewLine);
            }
            return sb.ToString();
        }

        static void AppendMeta(StringBuilder sb, IReadOnlyList<KeyValuePair<string, string>> meta)
        {
            if (meta == null) return;
            foreach (KeyValuePair<string, string> pair in meta)
                sb.Append(Line(MetaMarker, pair.Key, pair.Value)).Append(NewLine);
        }

        /// <summary>BOM 付き UTF-8 で書く。フォルダが無ければ作る。</summary>
        public static void WriteFile(string path, string content)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(path, content, new UTF8Encoding(true));
        }
    }
}
