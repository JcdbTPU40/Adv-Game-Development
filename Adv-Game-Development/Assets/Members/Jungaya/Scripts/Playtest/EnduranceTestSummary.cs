using System.Collections.Generic;
using System.Globalization;

namespace Toufuku.Playtest
{
    // 実際に振る間かくと照らし合わせた、大きい負荷ウェーブの人数の案1つぶん
    public readonly struct WaveCandidateCheck
    {
        public readonly WaveCandidate Candidate;
        // この案が許す1投の間かくが、実際に測った p75 より短い（＝4投に1投以上は間に合わない）
        public readonly bool RequiresFasterThanP75;
        // T3 の候補から外す（実際の間かくが思っていたのとちがっていて、しかも p75 より短い間かくが必要になる）
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

    /*
        1回ぶん（対象の10人）の合格・不合格を出すクラス（#53 / 企画書 v8 17章 T0-3M）

        完了条件と判定のしかた:
        ・10人中9人が3分最後までやる → 3:00 まで続けた人数
        ・最後の1分で投げた数が、最初の分の 20% 以内しか下がらない → 最後までやった人それぞれの（1 − 最後の分 ÷ 最初の分）の中央値
        ・疲れの中央値が 2/5 以下 → 聞き取りの疲れ5段階の中央値
        ・10人中7人がもう一回を選ぶ → 本当にもう一回を選んだ人数
        ・痛い・こわい・ストラップが外れた、が0件 → 1件でも出たら不合格（書きかけの行でも数える）

        実際に振った間かくは、全員ぶんの発射の間かくをまとめて中央値と p75 を出す。思っていた 1.0〜1.4 秒から外れたら、
        p75 より短い間かくが必要になる大きい負荷ウェーブの案（WaveLoadArithmetic）を T3 の候補から外す
        間かくは合格の条件じゃなくて、8章の負荷の計算を直すための記録
        「別の日・別の人で2回連続合格」は1回ぶんでは判定できないので、19章のテスト記録に2回ならべて確認する
        MonoBehaviour は使っていない
    */
    public sealed class EnduranceTestSummary
    {
        public int PlannedParticipants { get; private set; }
        // 集計に入れた人数（疲れともう一回が入っている行）
        public int Count { get; private set; }
        // 聞き取りが足りなくて数えなかった行の数
        public int Incomplete { get; private set; }
        public int Completed { get; private set; }
        public int StoppedEarly { get; private set; }
        public int Retry { get; private set; }
        // 痛い・こわい・ストラップが外れた、の件数（ぜんぶの行）
        public int SafetyIncidents { get; private set; }

        // 最後までやった人の、投げた数の下がり方の中央値
        public double? ThrowsDropMedian { get; private set; }
        public int ThrowsDropSamples { get; private set; }
        public double? FatigueMedian { get; private set; }

        // 最後までやった人の、1分ごとの投げた数の合計（参考）
        public int[] ThrowsPerMinute { get; private set; } = new int[EnduranceTestPlan.MinuteCount];
        // 最後までやった人の、1分ごとの命中率（着弾をまとめた値。参考）
        public double?[] HitRatePerMinute { get; private set; } = new double?[EnduranceTestPlan.MinuteCount];

        public int CycleCount { get; private set; }
        public double? CycleMedian { get; private set; }
        public double? CycleP75 { get; private set; }

        // 中央値と p75 がどっちも思っていた 1.0〜1.4 秒に入っているか（データがなければ false）
        public bool CycleWithinExpected { get; private set; }
        // データがあって、思っていたのから外れた（8章の負荷の計算を直す）
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

                    // 安全のことは、聞き取りの入力を待たずに数える（1件でも出たらその場で止めるため）
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
