using System;
using System.Collections.Generic;
using System.Text;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 参加者 1 人 = 1 行の CSV — Issue #49（19章のテスト記録へそのまま貼れる形）
    ///
    /// ・1 レコード 1 行に収めるため、自由記述の改行は空白へ置き換える。
    /// ・二択は「はい / いいえ / 未回答」、手応えは 1〜5（未回答は空）。
    /// ・Excel で開けるよう、書き出し側（<see cref="AbTestCsvFile"/>）は UTF-8 BOM を付ける。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class AbTestCsv
    {
        public const string Header =
            "テストID,日付,参加者番号,責任者,提示順," +
            "案A自発投数,案B自発投数,案A却下数,案B却下数," +
            "案A手応え,案B手応え,案Aもう一度,案Bもう一度," +
            "採用案,同期ずれ指摘,痛み,恐怖,ストラップ逸脱,理由,表情・声";

        public const int ColumnCount = 20;

        const string Yes = "はい";
        const string No = "いいえ";
        const string Unanswered = "未回答";

        public static string RowOf(AbParticipantRecord r)
        {
            if (r == null) return string.Empty;

            var sb = new StringBuilder();
            Add(sb, r.testId);
            Add(sb, r.date);
            Add(sb, r.participantNo.ToString());
            Add(sb, r.owner);
            Add(sb, AbTestPlan.LabelOf(r.order));
            Add(sb, r.throwsA.ToString());
            Add(sb, r.throwsB.ToString());
            Add(sb, r.rejectedA.ToString());
            Add(sb, r.rejectedB.ToString());
            Add(sb, FeelText(r.feelA));
            Add(sb, FeelText(r.feelB));
            Add(sb, BoolText(r.againA, r.againAnsweredA));
            Add(sb, BoolText(r.againB, r.againAnsweredB));
            Add(sb, r.adoptedAnswered ? AbTestPlan.LabelOf(r.adopted) : Unanswered);
            Add(sb, BoolText(r.syncComplaint, true));
            Add(sb, BoolText(r.pain, true));
            Add(sb, BoolText(r.fear, true));
            Add(sb, BoolText(r.strapDeviation, true));
            Add(sb, r.reason);
            Add(sb, r.note);
            return sb.ToString();
        }

        public static bool TryParseRow(string line, out AbParticipantRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.StartsWith("テストID", StringComparison.Ordinal)) return false; // 見出し行

            string[] f = SplitRow(line);
            if (f.Length < ColumnCount) return false;
            if (!int.TryParse(f[2], out int participantNo)) return false;

            var r = new AbParticipantRecord
            {
                testId = f[0],
                date = f[1],
                participantNo = participantNo,
                owner = f[3],
                order = f[4] == AbTestPlan.LabelOf(AbOrder.BA) ? AbOrder.BA : AbOrder.AB,
                throwsA = ParseInt(f[5]),
                throwsB = ParseInt(f[6]),
                rejectedA = ParseInt(f[7]),
                rejectedB = ParseInt(f[8]),
                feelA = ParseInt(f[9]),
                feelB = ParseInt(f[10]),
                syncComplaint = ParseBool(f[14]),
                pain = ParseBool(f[15]),
                fear = ParseBool(f[16]),
                strapDeviation = ParseBool(f[17]),
                reason = f[18],
                note = f[19]
            };

            if (f[11] != Unanswered) r.SetAgain(VariantId.A, ParseBool(f[11]));
            if (f[12] != Unanswered) r.SetAgain(VariantId.B, ParseBool(f[12]));
            if (f[13] != Unanswered) r.SetAdopted(f[13] == AbTestPlan.LabelOf(VariantId.B) ? VariantId.B : VariantId.A);

            record = r;
            return true;
        }

        static string FeelText(int feel) => AbParticipantRecord.IsValidFeel(feel) ? feel.ToString() : string.Empty;

        static string BoolText(bool value, bool answered) => !answered ? Unanswered : value ? Yes : No;

        static int ParseInt(string text) => int.TryParse(text, out int value) ? value : 0;

        static bool ParseBool(string text) => text == Yes;

        static void Add(StringBuilder sb, string field)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(Escape(field));
        }

        /// <summary>カンマ・引用符を含む自由記述を包む。改行は 1 行に収めるため空白にする。</summary>
        public static string Escape(string field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;

            string flat = field.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            if (flat.IndexOf(',') < 0 && flat.IndexOf('"') < 0) return flat;
            return "\"" + flat.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>1 行を列へ分ける（引用符の中のカンマは区切りにしない）。</summary>
        public static string[] SplitRow(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') { sb.Append(c); continue; }
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; continue; }
                    quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }
    }
}
