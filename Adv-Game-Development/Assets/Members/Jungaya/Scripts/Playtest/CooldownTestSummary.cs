using System.Collections.Generic;
using System.Text;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    // クールダウンの値1つぶんの集計
    public sealed class CooldownValueResult
    {
        public CooldownPreset Preset { get; internal set; }
        public float Seconds { get; internal set; }

        // 集計に入れた行の数（参加者 × この条件）
        public int Rows { get; internal set; }
        // 動画側がまだ入っていなくて数えなかった行の数
        public int Incomplete { get; internal set; }

        // 振ろうとした回数の合計（まちがい発射のわりあいのわる数）
        public int IntendedSwings { get; internal set; }
        // よけいな発射（動画の振りに合わない発射）
        public int ExtraFires { get; internal set; }
        // わざと連投した組の数（抜けのわりあいのわる数）
        public int PairSets { get; internal set; }
        // 2発目が出なかった組の数
        public int MissedSecond { get; internal set; }

        public int SafetyIncidents { get; internal set; }

        // 実際の連投の間かくのうち、いちばん短かったもの（秒）。0 は記録なし
        public float FastestInterval { get; internal set; }

        public RateWithInterval Misfire { get; internal set; }
        public RateWithInterval MissedRate { get; internal set; }

        public bool HasEnoughSwings => IntendedSwings >= CooldownTestPlan.IntendedSwingsPerCondition;
        public bool HasEnoughPairs => PairSets >= CooldownTestPlan.PairsPerCondition;
        // 投げた数がそろっているか（わる数の完了条件）
        public bool HasEnoughData => HasEnoughSwings && HasEnoughPairs;

        public bool MisfireOk => Misfire.HasData && Misfire.Rate <= CooldownTestPlan.MaxMisfireRate;
        public bool MissedOk => MissedRate.HasData && MissedRate.Rate <= CooldownTestPlan.MaxMissedSecondRate;
        // まちがい発射と抜けの両方が 2% 以下か（選ぶ条件）
        public bool BothOk => MisfireOk && MissedOk;

        // 選ぶ候補として数えていいか（投げた数がそろっていて、両方 2% 以下）
        public bool Adoptable => HasEnoughData && BothOk;

        public string Label => CooldownTestPlan.LabelOf(Preset);

        // やる人のパネルに出す1行
        public string Describe()
        {
            string mark = Adoptable ? "○" : BothOk ? "△" : "×";
            string interval = FastestInterval > 0f ? $" 最速連投 {FastestInterval:0.000}s" : "";
            return $"{mark} {Label}  誤発射 {Misfire.Describe()}  欠落 {MissedRate.Describe()}{interval}";
        }
    }

    /*
        T0-CD の合格・不合格を出すクラス（#50 / 企画書 v8 17章）

        完了条件と判定のしかた:
        ・まちがい発射 2%以下 と わざと連投したときの抜け 2%以下 を両方満たす、いちばん小さい値を選ぶ
          → 4つの値を秒が小さい順に見て、最初に両方満たした値
        ・ぜんぶの値でストラップが外れた・本体にぶつかったが0件 → 1件でも出たら不合格
        ・わる数と 95% 信頼区間を記録する → どの値も1回振り100回・連投50組に届いているか
        ・両方を満たす値がなければ、しきい値・ピークの見つけ方・ヒステリシスを変える → NeedsDetectorChange

        合格かどうかは「件数 ÷ わる数」の値そのもので見る。信頼区間は記録用で、合格かどうかには使わない
        連投50組だと0件でも 95% の上限が 7% くらいまでしか下がらなくて、区間で「2%以下」と言いきれないから
        （まちがい発射のほうはわる数が200なので、0件なら上限は 1.9%）

        「別の日に同じ投げた数で同じ結果になる」は1回ぶんでは判定できないので、19章のテスト記録に2回ならべて確認する
        MonoBehaviour は使っていない
    */
    public sealed class CooldownTestSummary
    {
        public IReadOnlyList<CooldownValueResult> Values { get; private set; } = new CooldownValueResult[0];
        public IReadOnlyList<AbCriterion> Criteria { get; private set; } = new AbCriterion[0];

        // 集計に入れた行の数
        public int Rows { get; private set; }
        // 動画側がまだ入っていなくて数えなかった行の数
        public int Incomplete { get; private set; }
        public int SafetyIncidents { get; private set; }

        // 選んだ値（両方 2% 以下を満たす、いちばん小さい値）。なければ CooldownPreset.Custom
        public CooldownPreset Adopted { get; private set; } = CooldownPreset.Custom;
        public bool HasAdopted { get; private set; }
        public float AdoptedSeconds => HasAdopted ? CooldownTestPlan.SecondsOf(Adopted) : 0f;

        /*
            どの値も両方を満たさなかった。片方だけ優先して値を決めたりしないで、
            角度のしきい値・ピークの見つけ方・ヒステリシス（SwingPeakDetector）を変えてやりなおす
        */
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

        // 不合格の項目だけをならべた1行（"-" なら合格）
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
                    if (v == null) continue; // 候補にない秒数（Custom）は集計しない

                    // 安全のことは動画側が入るのを待たずに数える（1件でも出たらその場で止めるため）
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

            // 秒が小さい順に見て、最初に両方を満たした値を選ぶ
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
