using System.Collections.Generic;
using System.Text;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /// <summary>クールダウン値 1 つ分の集計。</summary>
    public sealed class CooldownValueResult
    {
        public CooldownPreset Preset { get; internal set; }
        public float Seconds { get; internal set; }

        /// <summary>集計に入れた行数（参加者 × この条件）。</summary>
        public int Rows { get; internal set; }
        /// <summary>動画側が未入力で数えなかった行数。</summary>
        public int Incomplete { get; internal set; }

        /// <summary>意図した振りの合計（誤発射率の母数）。</summary>
        public int IntendedSwings { get; internal set; }
        /// <summary>余分な発射（動画の振りに対応しない発射）。</summary>
        public int ExtraFires { get; internal set; }
        /// <summary>意図的連投の組数（欠落率の母数）。</summary>
        public int PairSets { get; internal set; }
        /// <summary>2 発目が出なかった組数。</summary>
        public int MissedSecond { get; internal set; }

        public int SafetyIncidents { get; internal set; }

        /// <summary>実連投間隔のうち最も短かったもの（秒）。0 は記録なし。</summary>
        public float FastestInterval { get; internal set; }

        public RateWithInterval Misfire { get; internal set; }
        public RateWithInterval MissedRate { get; internal set; }

        public bool HasEnoughSwings => IntendedSwings >= CooldownTestPlan.IntendedSwingsPerCondition;
        public bool HasEnoughPairs => PairSets >= CooldownTestPlan.PairsPerCondition;
        /// <summary>投数がそろっているか（母数の完了条件）。</summary>
        public bool HasEnoughData => HasEnoughSwings && HasEnoughPairs;

        public bool MisfireOk => Misfire.HasData && Misfire.Rate <= CooldownTestPlan.MaxMisfireRate;
        public bool MissedOk => MissedRate.HasData && MissedRate.Rate <= CooldownTestPlan.MaxMissedSecondRate;
        /// <summary>誤発射・欠落の両方が 2% 以下か（採用の条件）。</summary>
        public bool BothOk => MisfireOk && MissedOk;

        /// <summary>採用候補として数えられるか（投数がそろっていて両方 2% 以下）。</summary>
        public bool Adoptable => HasEnoughData && BothOk;

        public string Label => CooldownTestPlan.LabelOf(Preset);

        /// <summary>実施者パネルの 1 行。</summary>
        public string Describe()
        {
            string mark = Adoptable ? "○" : BothOk ? "△" : "×";
            string interval = FastestInterval > 0f ? $" 最速連投 {FastestInterval:0.000}s" : "";
            return $"{mark} {Label}  誤発射 {Misfire.Describe()}  欠落 {MissedRate.Describe()}{interval}";
        }
    }

    /// <summary>
    /// T0-CD の合否 — Issue #50（仕様書 v8 17章）
    ///
    /// | 完了条件 | 判定 |
    /// |---|---|
    /// | 誤発射 ≤2% かつ 意図的連投の欠落 ≤2% を両方満たす最小値を採用 | 4 値を秒の小さい順に見て最初に両方満たした値 |
    /// | 全値でストラップ逸脱・筐体接触 0 件 | 1 件でも出たら不合格 |
    /// | 母数と 95% 信頼区間を記録 | 各値 単発 100 回・連投 50 組に達しているか |
    /// | 両方を満たす値が無ければ閾値・ピーク検出・ヒステリシスを変更 | <see cref="NeedsDetectorChange"/> |
    ///
    /// 合否は<b>点推定</b>（件数 ÷ 母数）で見る。信頼区間は記録用で、合否には使わない。
    /// 連投 50 組では 0 件でも 95% 上限が約 7% までしか下がらず、区間で 2% 以下を言い切れないため
    /// （誤発射側は母数 200 なので 0 件なら上限 1.9%）。
    ///
    /// 「別日・同じ投数で再現できる」は 1 回分では判定できないので、19章のテスト記録に 2 回並べて確認する。
    /// MonoBehaviour 非依存。
    /// </summary>
    public sealed class CooldownTestSummary
    {
        public IReadOnlyList<CooldownValueResult> Values { get; private set; } = new CooldownValueResult[0];
        public IReadOnlyList<AbCriterion> Criteria { get; private set; } = new AbCriterion[0];

        /// <summary>集計に入れた行数。</summary>
        public int Rows { get; private set; }
        /// <summary>動画側が未入力で数えなかった行数。</summary>
        public int Incomplete { get; private set; }
        public int SafetyIncidents { get; private set; }

        /// <summary>採用値（両方 2% 以下を満たす最小値）。無ければ <see cref="CooldownPreset.Custom"/>。</summary>
        public CooldownPreset Adopted { get; private set; } = CooldownPreset.Custom;
        public bool HasAdopted { get; private set; }
        public float AdoptedSeconds => HasAdopted ? CooldownTestPlan.SecondsOf(Adopted) : 0f;

        /// <summary>
        /// どの値も両方を満たさなかった。片方だけを優先して値を決めず、
        /// 角度閾値・ピーク検出・ヒステリシス（<see cref="SwingPeakDetector"/>）を変えてやり直す。
        /// </summary>
        public bool NeedsDetectorChange { get; private set; }

        public bool Passed { get; private set; }

        public CooldownValueResult ValueOf(CooldownPreset preset)
        {
            for (int i = 0; i < Values.Count; i++)
            {
                if (Values[i].Preset == preset) return Values[i];
            }
            return null;
        }

        /// <summary>不合格の項目だけを並べた 1 行（"-" なら合格）。</summary>
        public string FailureSummary()
        {
            var parts = new List<string>();
            for (int i = 0; i < Criteria.Count; i++)
            {
                if (!Criteria[i].Passed) parts.Add($"{Criteria[i].Name} {Criteria[i].Actual}");
            }
            return parts.Count == 0 ? "-" : string.Join(" / ", parts);
        }

        public static CooldownTestSummary Of(IReadOnlyList<CooldownConditionRecord> records)
        {
            var summary = new CooldownTestSummary();

            var values = new CooldownValueResult[CooldownTestPlan.ConditionCount];
            for (int i = 0; i < values.Length; i++)
            {
                CooldownPreset preset = CooldownTestPlan.Conditions[i];
                values[i] = new CooldownValueResult
                {
                    Preset = preset,
                    Seconds = CooldownTestPlan.SecondsOf(preset)
                };
            }

            if (records != null)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    CooldownConditionRecord r = records[i];
                    if (r == null) continue;

                    CooldownValueResult v = Find(values, r.preset);
                    if (v == null) continue; // 候補外の秒数（Custom）は集計しない

                    // 安全事象は動画側の入力を待たずに数える（1 件でも出たらその場で止めるため）
                    int incidents = r.strapDeviation + r.caseContact;
                    v.SafetyIncidents += incidents;
                    summary.SafetyIncidents += incidents;

                    if (r.intervalCount > 0 && r.intervalMin > 0f
                        && (v.FastestInterval <= 0f || r.intervalMin < v.FastestInterval))
                        v.FastestInterval = r.intervalMin;

                    if (!r.IsComplete)
                    {
                        v.Incomplete++;
                        summary.Incomplete++;
                        continue;
                    }

                    v.Rows++;
                    summary.Rows++;
                    v.IntendedSwings += r.IntendedSwings;
                    v.ExtraFires += r.videoExtraFires;
                    v.PairSets += r.videoPairSets;
                    v.MissedSecond += r.videoMissedSecond;
                }
            }

            for (int i = 0; i < values.Length; i++)
            {
                values[i].Misfire = WilsonInterval.Of(values[i].ExtraFires, values[i].IntendedSwings);
                values[i].MissedRate = WilsonInterval.Of(values[i].MissedSecond, values[i].PairSets);
            }

            // 秒の小さい順に見て、最初に両方を満たした値が採用値
            for (int i = 0; i < values.Length; i++)
            {
                if (!values[i].Adoptable) continue;
                summary.Adopted = values[i].Preset;
                summary.HasAdopted = true;
                break;
            }

            summary.Values = values;
            summary.NeedsDetectorChange = !summary.HasAdopted && AllHaveEnoughData(values);
            summary.Criteria = BuildCriteria(summary, values);

            bool passed = true;
            for (int i = 0; i < summary.Criteria.Count; i++)
            {
                if (!summary.Criteria[i].Passed) passed = false;
            }
            summary.Passed = passed;
            return summary;
        }

        static IReadOnlyList<AbCriterion> BuildCriteria(CooldownTestSummary summary, CooldownValueResult[] values)
        {
            int enough = 0;
            int misfireOk = 0;
            int missedOk = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].HasEnoughData) enough++;
                if (values[i].MisfireOk) misfireOk++;
                if (values[i].MissedOk) missedOk++;
            }

            return new List<AbCriterion>
            {
                new AbCriterion(
                    $"母数（各値 単発{CooldownTestPlan.SinglesPerCondition}回・連投{CooldownTestPlan.PairsPerCondition}組）",
                    $"{enough}/{values.Length} 値", $"{values.Length} 値", enough == values.Length),
                new AbCriterion("動画との突き合わせ",
                    summary.Incomplete == 0 ? "済" : $"未入力 {summary.Incomplete} 行", "全行", summary.Incomplete == 0),
                new AbCriterion($"誤発射 ≤{CooldownTestPlan.MaxMisfireRate * 100.0:0}%",
                    ListOf(values, true), "1 値以上", misfireOk > 0),
                new AbCriterion($"意図的連投の欠落 ≤{CooldownTestPlan.MaxMissedSecondRate * 100.0:0}%",
                    ListOf(values, false), "1 値以上", missedOk > 0),
                new AbCriterion("両方を満たす最小値",
                    summary.HasAdopted ? CooldownTestPlan.LabelOf(summary.Adopted) : "なし",
                    "1 値以上", summary.HasAdopted),
                new AbCriterion("ストラップ逸脱・筐体接触",
                    $"{summary.SafetyIncidents} 件", "0 件", summary.SafetyIncidents == 0)
            };
        }

        static string ListOf(CooldownValueResult[] values, bool misfire)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (misfire ? !values[i].MisfireOk : !values[i].MissedOk) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(values[i].Label);
            }
            return sb.Length == 0 ? "なし" : sb.ToString();
        }

        static bool AllHaveEnoughData(CooldownValueResult[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!values[i].HasEnoughData) return false;
            }
            return true;
        }

        static CooldownValueResult Find(CooldownValueResult[] values, CooldownPreset preset)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].Preset == preset) return values[i];
            }
            return null;
        }
    }
}
