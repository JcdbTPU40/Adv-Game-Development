using System;
using System.Globalization;
using System.Text;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 参加者 1 人 × 1 条件 = 1 行の CSV — Issue #50（19章のテスト記録へそのまま貼れる形）
    ///
    /// ・ゲーム側の列は実施中に埋まる。<b>動画側の 4 列は空で書き出す</b>ので、
    ///   録画を見返しながら Excel で埋めるか、実施者パネルの集計モードで入れる。
    /// ・空欄は「未入力」であって 0 ではない（<see cref="CooldownConditionRecord.NotEntered"/>）。
    ///   0 と書けば「0 件を確認した」という意味になる。
    /// ・区切りと引用の扱いは #49 と同じ（<see cref="AbTestCsv.Escape"/> / <see cref="AbTestCsv.SplitRow"/>）。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class CooldownCsv
    {
        public const string Header =
            "テストID,日付,参加者番号,責任者,順序,試行順,クールダウン秒," +
            "単発予定,単発受理,単発CD却下," +
            "連投予定組,連投受理,連投CD却下,連投成立組," +
            "間隔件数,間隔最小,間隔中央,間隔p75," +
            "動画_単発振り数,動画_連投組数,動画_余分な発射,動画_2発目欠落," +
            "ストラップ逸脱,筐体接触,所見";

        public const int ColumnCount = 25;

        static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static string RowOf(CooldownConditionRecord r)
        {
            if (r == null) return string.Empty;

            var sb = new StringBuilder();
            Add(sb, r.testId);
            Add(sb, r.date);
            Add(sb, r.participantNo.ToString(Culture));
            Add(sb, r.owner);
            Add(sb, (r.orderIndex + 1).ToString(Culture));
            Add(sb, (r.trialIndex + 1).ToString(Culture));
            Add(sb, r.cooldownSeconds.ToString("0.00", Culture));

            Add(sb, r.singleTarget.ToString(Culture));
            Add(sb, r.singleAccepted.ToString(Culture));
            Add(sb, r.singleRejected.ToString(Culture));

            Add(sb, r.pairTarget.ToString(Culture));
            Add(sb, r.pairAccepted.ToString(Culture));
            Add(sb, r.pairRejected.ToString(Culture));
            Add(sb, r.pairCompleted.ToString(Culture));

            Add(sb, r.intervalCount.ToString(Culture));
            Add(sb, Seconds(r.intervalMin));
            Add(sb, Seconds(r.intervalMedian));
            Add(sb, Seconds(r.intervalP75));

            Add(sb, Optional(r.videoSingleSwings));
            Add(sb, Optional(r.videoPairSets));
            Add(sb, Optional(r.videoExtraFires));
            Add(sb, Optional(r.videoMissedSecond));

            Add(sb, r.strapDeviation.ToString(Culture));
            Add(sb, r.caseContact.ToString(Culture));
            Add(sb, r.note);
            return sb.ToString();
        }

        public static bool TryParseRow(string line, out CooldownConditionRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.StartsWith("テストID", StringComparison.Ordinal)) return false; // 見出し行

            string[] f = AbTestCsv.SplitRow(line);
            if (f.Length < ColumnCount) return false;
            if (!int.TryParse(f[2], NumberStyles.Integer, Culture, out int participantNo)) return false;

            float seconds = ParseFloat(f[6]);
            var r = new CooldownConditionRecord
            {
                testId = f[0],
                date = f[1],
                participantNo = participantNo,
                owner = f[3],
                orderIndex = Math.Max(0, ParseInt(f[4]) - 1),
                trialIndex = Math.Max(0, ParseInt(f[5]) - 1),
                cooldownSeconds = seconds,
                preset = CooldownTestPlan.PresetOf(seconds),

                singleTarget = ParseInt(f[7]),
                singleAccepted = ParseInt(f[8]),
                singleRejected = ParseInt(f[9]),

                pairTarget = ParseInt(f[10]),
                pairAccepted = ParseInt(f[11]),
                pairRejected = ParseInt(f[12]),
                pairCompleted = ParseInt(f[13]),

                intervalCount = ParseInt(f[14]),
                intervalMin = ParseFloat(f[15]),
                intervalMedian = ParseFloat(f[16]),
                intervalP75 = ParseFloat(f[17]),

                videoSingleSwings = ParseOptional(f[18]),
                videoPairSets = ParseOptional(f[19]),
                videoExtraFires = ParseOptional(f[20]),
                videoMissedSecond = ParseOptional(f[21]),

                strapDeviation = ParseInt(f[22]),
                caseContact = ParseInt(f[23]),
                note = f[24]
            };

            record = r;
            return true;
        }

        /// <summary>未入力は空欄で書く（0 と区別する）。</summary>
        static string Optional(int value) =>
            value <= CooldownConditionRecord.NotEntered ? string.Empty : value.ToString(Culture);

        static int ParseOptional(string text) =>
            string.IsNullOrWhiteSpace(text)
                ? CooldownConditionRecord.NotEntered
                : int.TryParse(text, NumberStyles.Integer, Culture, out int value)
                    ? value
                    : CooldownConditionRecord.NotEntered;

        static string Seconds(float value) => value.ToString("0.000", Culture);

        static int ParseInt(string text) =>
            int.TryParse(text, NumberStyles.Integer, Culture, out int value) ? value : 0;

        static float ParseFloat(string text) =>
            float.TryParse(text, NumberStyles.Float, Culture, out float value) ? value : 0f;

        static void Add(StringBuilder sb, string field)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(AbTestCsv.Escape(field));
        }
    }
}
