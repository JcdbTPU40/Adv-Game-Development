using System;
using System.Globalization;
using System.Text;

namespace Toufuku.Playtest
{
    /*
        参加者1人を1行にした CSV（#53。19章のテスト記録にそのまま貼れる形）

        ・届いていない分（とちゅうで終わった）の投げた数・着弾・命中・命中率は空欄（データなし）で書く。0回とは分ける
        ・疲れと、もう一回やるかの答えがないときも空欄。あとで Excel でうめて L で読みなおせる
        ・命中率・投げた数の下がり方・間かくの中央値と p75 は、見やすくするために計算して足した列。読みもどすときは元の列から計算しなおす
        ・最後の「間かくの列」は発射の間かく（秒）を空白で区切ってならべたもの。全員ぶんをまとめて中央値と p75 を出すのに使う
        ・区切りと引用符のあつかいは #49 と同じ（AbTestCsv.Escape / AbTestCsv.SplitRow）
        MonoBehaviour は使っていない
    */
    public static class EnduranceCsv
    {
        public const string Header =
            "テストID,日付,参加者番号,責任者,クールダウン秒,フィードバック案,予定秒,実施秒,完走,終了希望," +
            "0-60s投数,60-120s投数,120-180s投数," +
            "0-60s着弾,60-120s着弾,120-180s着弾," +
            "0-60s命中,60-120s命中,120-180s命中," +
            "0-60s命中率,60-120s命中率,120-180s命中率," +
            "投数低下率,CD却下,周期件数,周期中央,周期p75," +
            "持ち替え,腕の下がり,腕の下がり初回秒," +
            "疲労,痛み,恐怖,ストラップ逸脱,再挑戦,所見,周期列";

        public const int ColumnCount = 37;

        public const string RetryYes = "再挑戦";
        public const string RetryNo = "別の遊び";

        static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static string RowOf(EnduranceRecord r)
        {
            if (r == null) return string.Empty;
            r.EnsureArrays();

            var sb = new StringBuilder();
            Add(sb, r.testId);
            Add(sb, r.date);
            Add(sb, r.participantNo.ToString(Culture));
            Add(sb, r.owner);
            Add(sb, r.cooldownSeconds.ToString("0.00", Culture));
            Add(sb, r.feedbackLabel);
            Add(sb, r.plannedSeconds.ToString("0.#", Culture));
            Add(sb, r.endSeconds.ToString("0.0", Culture));
            Add(sb, Bool(r.completed));
            Add(sb, Bool(r.stopRequested));

            for (int k = 0; k < EnduranceTestPlan.MinuteCount; k++) Add(sb, Int(r.ThrowsOf(k)));
            for (int k = 0; k < EnduranceTestPlan.MinuteCount; k++) Add(sb, Int(r.LandingsOf(k)));
            for (int k = 0; k < EnduranceTestPlan.MinuteCount; k++) Add(sb, Int(r.HitsOf(k)));
            for (int k = 0; k < EnduranceTestPlan.MinuteCount; k++) Add(sb, Num(r.HitRateOf(k)));

            Add(sb, Num(r.ThrowsDropRatio));
            Add(sb, r.rejected.ToString(Culture));
            Add(sb, r.CycleCount.ToString(Culture));
            Add(sb, Num(r.CycleMedian));
            Add(sb, Num(r.CycleP75));

            Add(sb, r.gripChanges.ToString(Culture));
            Add(sb, r.armDrops.ToString(Culture));
            Add(sb, r.firstArmDropSec < 0f ? "" : r.firstArmDropSec.ToString("0.0", Culture));

            Add(sb, r.FatigueAnswered ? r.fatigue.ToString(Culture) : "");
            Add(sb, Bool(r.pain));
            Add(sb, Bool(r.fear));
            Add(sb, Bool(r.strapDeviation));
            Add(sb, r.retryAnswered ? (r.retry ? RetryYes : RetryNo) : "");
            Add(sb, r.note);
            Add(sb, CyclesText(r));
            return sb.ToString();
        }

        public static bool TryParseRow(string line, out EnduranceRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line.StartsWith("テストID", StringComparison.Ordinal)) return false; // 見出しの行

            string[] f = AbTestCsv.SplitRow(line);
            if (f.Length < ColumnCount) return false;
            if (!int.TryParse(f[2], NumberStyles.Integer, Culture, out int participantNo)) return false;

            var r = new EnduranceRecord
            {
                testId = f[0],
                date = f[1],
                participantNo = participantNo,
                owner = f[3],
                cooldownSeconds = ParseFloat(f[4]),
                feedbackLabel = f[5],
                plannedSeconds = ParseFloat(f[6]),
                endSeconds = ParseFloat(f[7]),
                completed = ParseBool(f[8]),
                stopRequested = ParseBool(f[9]),
                rejected = ParseInt(f[23]),
                gripChanges = ParseInt(f[27]),
                armDrops = ParseInt(f[28]),
                firstArmDropSec = string.IsNullOrWhiteSpace(f[29]) ? -1f : ParseFloat(f[29]),
                fatigue = ParseInt(f[30]),
                pain = ParseBool(f[31]),
                fear = ParseBool(f[32]),
                strapDeviation = ParseBool(f[33]),
                note = f[35]
            };
            r.EnsureArrays();

            int n = EnduranceTestPlan.MinuteCount;
            for (int k = 0; k < n; k++)
            {
                r.throwsPerMinute[k] = ParseInt(f[10 + k]);
                r.landingsPerMinute[k] = ParseInt(f[10 + n + k]);
                r.hitsPerMinute[k] = ParseInt(f[10 + n * 2 + k]);
            }

            string retry = f[34].Trim();
            if (retry == RetryYes || retry == "1") r.SetRetry(true);
            else if (retry == RetryNo || retry == "0") r.SetRetry(false);

            foreach (string part in f[36].Split(new[] { ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (float.TryParse(part, NumberStyles.Float, Culture, out float value) && value > 0f) r.cycles.Add(value);
            }

            record = r;
            return true;
        }

        public static string CyclesText(EnduranceRecord r)
        {
            if (r?.cycles == null || r.cycles.Count == 0) return string.Empty;
            var sb = new StringBuilder(r.cycles.Count * 6);
            foreach (float c in r.cycles)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(c.ToString("0.000", Culture));
            }
            return sb.ToString();
        }

        static string Bool(bool value) => value ? "1" : "0";

        static bool ParseBool(string text)
        {
            text = text?.Trim();
            return text == "1" || text == "はい" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        static string Int(int? value) => value.HasValue ? value.Value.ToString(Culture) : string.Empty;

        static string Num(double? value) => value.HasValue ? value.Value.ToString("0.000", Culture) : string.Empty;

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
