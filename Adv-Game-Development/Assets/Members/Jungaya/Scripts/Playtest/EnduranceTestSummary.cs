using System.Collections.Generic;
using System.Globalization;

namespace Toufuku.Playtest
{
    /// <summary>実操作周期と照らした大負荷ウェーブの人数案 1 つ分。</summary>
    public readonly struct WaveCandidateCheck
    {
        public readonly WaveCandidate Candidate;
        /// <summary>この案が許す 1 投周期が、実測の p75 より短い（＝ 4 投に 1 投以上は間に合わない）。</summary>
        public readonly bool RequiresFasterThanP75;
        /// <summary>T3 候補から外す（実操作周期が想定から外れていて、かつ p75 より短い周期を要求する）。</summary>
        public readonly bool Excluded;

        public WaveCandidateCheck(WaveCandidate candidate, bool requiresFasterThanP75, bool excluded)
        {
            Candidate = candidate;
            RequiresFasterThanP75 = requiresFasterThanP75;
            Excluded = excluded;
        }

        public string Describe(double? p75)
        {
            string p = p75.HasValue ? p75.Value.ToString("0.000", CultureInfo.InvariantCulture) + "s" : "-";
            string verdict = Excluded ? "T3 候補から外す"
                : RequiresFasterThanP75 ? "p75 より短い（周期が想定内なので参考）"
                : "残す";
            return $"{(Excluded ? "×" : "○")} {Candidate.Label} 許容 {Candidate.AllowedCycleSeconds:0.00}s / 実測 p75 {p} → {verdict}";
        }
    }

    /// <summary>
    /// 1 回分（対象層 10 人）の合否 — Issue #53（仕様書 v8 17章 T0-3M）
    ///
    /// | 完了条件 | 判定 |
    /// |---|---|
    /// | 9/10 が 3 分完走 | 3:00 まで続けた人数 |
    /// | 最終 1 分の投数低下が初分比 20% 以内 | 完走者それぞれの（1 − 最終分 ÷ 初分）の<b>中央値</b> |
    /// | 疲労中央値 2/5 以下 | 聞き取りの疲労 5 段階の中央値 |
    /// | 7/10 がもう一度を選択 | 実際に再挑戦を選んだ人数 |
    /// | 痛み・恐怖・ストラップ逸脱 0 件 | 1 件でも出たら不合格（記入途中の行でも数える） |
    ///
    /// 実操作周期は全員分の発射間隔を束ねて中央値と p75 を出す。想定 1.0〜1.4 秒から外れたら、
    /// p75 より短い周期を要求する大負荷ウェーブ案（<see cref="WaveLoadArithmetic"/>）を T3 候補から外す。
    /// 周期は合否の条件ではなく、8章の負荷算術を更新するための記録。
    /// 「別日・別対象者で 2 回連続合格」は 1 回分では判定できないので、19章のテスト記録で 2 回並べて確認する。
    /// MonoBehaviour 非依存。
    /// </summary>
    public sealed class EnduranceTestSummary
    {
        public int PlannedParticipants { get; private set; }
        /// <summary>集計に入れた人数（疲労と再挑戦が入っている行）。</summary>
        public int Count { get; private set; }
        /// <summary>聞き取りが足りず数えなかった行数。</summary>
        public int Incomplete { get; private set; }
        public int Completed { get; private set; }
        public int StoppedEarly { get; private set; }
        public int Retry { get; private set; }
        /// <summary>痛み・恐怖・ストラップ逸脱の件数（全行）。</summary>
        public int SafetyIncidents { get; private set; }

        /// <summary>完走者の投数低下率の中央値。</summary>
        public double? ThrowsDropMedian { get; private set; }
        public int ThrowsDropSamples { get; private set; }
        public double? FatigueMedian { get; private set; }

        /// <summary>完走者の 1 分ごとの投数の合計（参考）。</summary>
        public int[] ThrowsPerMinute { get; private set; } = new int[EnduranceTestPlan.MinuteCount];
        /// <summary>完走者の 1 分ごとの命中率（着弾を束ねた値。参考）。</summary>
        public double?[] HitRatePerMinute { get; private set; } = new double?[EnduranceTestPlan.MinuteCount];

        public int CycleCount { get; private set; }
        public double? CycleMedian { get; private set; }
        public double? CycleP75 { get; private set; }

        /// <summary>中央値・p75 とも想定 1.0〜1.4 秒の範囲内か（データが無ければ false）。</summary>
        public bool CycleWithinExpected { get; private set; }
        /// <summary>データがあって想定から外れた（8章の負荷算術を更新する）。</summary>
        public bool CycleOutOfExpected => CycleMedian.HasValue && !CycleWithinExpected;

        public IReadOnlyList<WaveCandidateCheck> Waves { get; private set; } = new WaveCandidateCheck[0];
        public IReadOnlyList<AbCriterion> Criteria { get; private set; } = new AbCriterion[0];
        public bool Passed { get; private set; }

        public string FailureSummary()
        {
            var parts = new List<string>();
            foreach (AbCriterion c in Criteria)
            {
                if (!c.Passed) parts.Add($"{c.Name} {c.Actual}");
            }
            return parts.Count == 0 ? "-" : string.Join(" / ", parts);
        }

        public string CycleDescribe()
        {
            if (!CycleMedian.HasValue) return "実操作周期: -";
            string range = CycleWithinExpected ? "想定内" : "想定 1.0〜1.4s から外れた → 8章の負荷算術を更新";
            return $"実操作周期: n={CycleCount} 中央 {CycleMedian.Value:0.000}s p75 {CycleP75.Value:0.000}s（{range}）";
        }

        public static EnduranceTestSummary Of(IReadOnlyList<EnduranceRecord> records,
            int plannedParticipants = EnduranceTestPlan.DefaultParticipants)
        {
            var s = new EnduranceTestSummary { PlannedParticipants = plannedParticipants };
            int minutes = EnduranceTestPlan.MinuteCount;
            var drops = new List<double>();
            var fatigues = new List<double>();
            var cycles = new List<double>();
            var landings = new int[minutes];
            var hits = new int[minutes];

            if (records != null)
            {
                foreach (EnduranceRecord r in records)
                {
                    if (r == null) continue;
                    r.EnsureArrays();

                    // 安全事象は聞き取りの入力を待たずに数える（1 件でも出たらその場で止めるため）
                    s.SafetyIncidents += r.SafetyIncidents;

                    if (!r.IsComplete) { s.Incomplete++; continue; }

                    s.Count++;
                    if (r.completed) s.Completed++; else s.StoppedEarly++;
                    if (r.retry) s.Retry++;
                    fatigues.Add(r.fatigue);
                    cycles.AddRange(r.CyclesAsDouble());

                    double? drop = r.ThrowsDropRatio;
                    if (drop.HasValue) drops.Add(drop.Value);

                    if (!r.completed) continue;
                    for (int k = 0; k < minutes; k++)
                    {
                        s.ThrowsPerMinute[k] += r.throwsPerMinute[k];
                        landings[k] += r.landingsPerMinute[k];
                        hits[k] += r.hitsPerMinute[k];
                    }
                }
            }

            for (int k = 0; k < minutes; k++)
                s.HitRatePerMinute[k] = landings[k] > 0 ? (double)hits[k] / landings[k] : (double?)null;

            s.ThrowsDropMedian = PlaytestStats.Median(drops);
            s.ThrowsDropSamples = drops.Count;
            s.FatigueMedian = PlaytestStats.Median(fatigues);

            s.CycleCount = cycles.Count;
            s.CycleMedian = PlaytestStats.Percentile(cycles, 0.50);
            s.CycleP75 = PlaytestStats.Percentile(cycles, 0.75);
            s.CycleWithinExpected = s.CycleMedian.HasValue &&
                                    EnduranceTestPlan.CycleWithinExpected(s.CycleMedian.Value) &&
                                    EnduranceTestPlan.CycleWithinExpected(s.CycleP75.Value);

            var waves = new List<WaveCandidateCheck>();
            foreach (WaveCandidate candidate in WaveLoadArithmetic.FinalCandidates())
            {
                bool faster = s.CycleP75.HasValue && candidate.AllowedCycleSeconds < s.CycleP75.Value;
                waves.Add(new WaveCandidateCheck(candidate, faster, faster && s.CycleOutOfExpected));
            }
            s.Waves = waves;

            s.Criteria = BuildCriteria(s);
            bool passed = true;
            foreach (AbCriterion c in s.Criteria)
            {
                if (!c.Passed) passed = false;
            }
            s.Passed = passed;
            return s;
        }

        static IReadOnlyList<AbCriterion> BuildCriteria(EnduranceTestSummary s)
        {
            int n = s.PlannedParticipants;
            int completeNeeded = EnduranceTestPlan.RequiredCount(n, EnduranceTestPlan.CompletionRatio);
            int retryNeeded = EnduranceTestPlan.RequiredCount(n, EnduranceTestPlan.RetryRatio);

            string drop = s.ThrowsDropMedian.HasValue
                ? $"中央値 {s.ThrowsDropMedian.Value * 100.0:0.#}%（{s.ThrowsDropSamples} 人）"
                : "データなし";
            string fatigue = s.FatigueMedian.HasValue ? $"中央値 {s.FatigueMedian.Value:0.#}" : "データなし";

            return new List<AbCriterion>
            {
                new AbCriterion("記録人数", $"{s.Count} 人", $"{n} 人", s.Count >= n),
                new AbCriterion("3 分完走", $"{s.Completed} 人", $"{completeNeeded} 人以上", s.Completed >= completeNeeded),
                new AbCriterion("最終 1 分の投数低下（初分比）", drop,
                    $"{EnduranceTestPlan.MaxThrowsDropRatio * 100.0:0}% 以内",
                    s.ThrowsDropMedian.HasValue && EnduranceTestPlan.DropWithinLimit(s.ThrowsDropMedian.Value)),
                new AbCriterion("疲労", fatigue, $"中央値 {EnduranceTestPlan.MaxFatigueMedian:0}/5 以下",
                    s.FatigueMedian.HasValue && s.FatigueMedian.Value <= EnduranceTestPlan.MaxFatigueMedian + 1e-9),
                new AbCriterion("もう一度を選択", $"{s.Retry} 人", $"{retryNeeded} 人以上", s.Retry >= retryNeeded),
                new AbCriterion("痛み・恐怖・ストラップ逸脱", $"{s.SafetyIncidents} 件", "0 件", s.SafetyIncidents == 0)
            };
        }
    }
}
