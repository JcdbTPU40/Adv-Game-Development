using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /// <summary>完了条件 1 つ分の判定。</summary>
    public readonly struct AbCriterion
    {
        public readonly string Name;
        /// <summary>実測（例: "8 人"）。</summary>
        public readonly string Actual;
        /// <summary>合格ライン（例: "8 人以上"）。</summary>
        public readonly string Required;
        public readonly bool Passed;

        public AbCriterion(string name, string actual, string required, bool passed)
        {
            Name = name;
            Actual = actual;
            Required = required;
            Passed = passed;
        }

        public override string ToString() => $"{(Passed ? "○" : "×")} {Name}: {Actual}（{Required}）";
    }

    /// <summary>
    /// 1 回分（対象層 10 人）の合否 — Issue #49（仕様書 v8 17章）
    ///
    /// | 完了条件 | 判定 |
    /// |---|---|
    /// | 8/10 が採用案を手応え 4/5 以上 | 各自が選んだ採用案の手応えで数える |
    /// | 8/10 が「すぐもう一度振りたい」 | 同じく採用案への回答で数える |
    /// | 7/10 以上が同じ案を選ぶ | 採用案の多いほうの人数 |
    /// | 同期ずれの指摘が 2/10 以下 | 指摘した人数 |
    /// | 痛み・恐怖・ストラップ逸脱 0 件 | 1 件でも出たら不合格 |
    ///
    /// 分母は「予定人数（既定 10 人）」で、途中まででも同じラインで見る（足りなければ不合格のまま）。
    /// 未記入の記録（<see cref="AbParticipantRecord.IsComplete"/> が false）は数に入れず <see cref="Incomplete"/> に出す。
    /// 「別日・別対象者で 2 回連続合格」は 1 回分では判定できないので、19章のテスト記録で 2 回並べて確認する。
    /// MonoBehaviour 非依存。
    /// </summary>
    public sealed class AbTestSummary
    {
        public int PlannedParticipants { get; private set; }
        /// <summary>集計に入れた人数。</summary>
        public int Count { get; private set; }
        /// <summary>記入が足りず数えなかった人数。</summary>
        public int Incomplete { get; private set; }
        public int AdoptedA { get; private set; }
        public int AdoptedB { get; private set; }
        /// <summary>採用案の手応えが 4 以上だった人数。</summary>
        public int FeelOk { get; private set; }
        /// <summary>採用案で「すぐもう一度振りたい」を選んだ人数。</summary>
        public int AgainOk { get; private set; }
        public int SyncComplaints { get; private set; }
        public int SafetyIncidents { get; private set; }

        /// <summary>多いほうの案を選んだ人数。</summary>
        public int MajorityCount => AdoptedA >= AdoptedB ? AdoptedA : AdoptedB;
        /// <summary>多いほうの案（同数なら案A）。</summary>
        public VariantId Majority => AdoptedA >= AdoptedB ? VariantId.A : VariantId.B;

        public IReadOnlyList<AbCriterion> Criteria { get; private set; } = new AbCriterion[0];

        /// <summary>すべての完了条件を満たしたか。</summary>
        public bool Passed { get; private set; }

        /// <summary>不合格の項目だけを並べた 1 行（"-" なら合格）。</summary>
        public string FailureSummary()
        {
            var parts = new List<string>();
            foreach (AbCriterion c in Criteria)
            {
                if (!c.Passed) parts.Add($"{c.Name} {c.Actual}");
            }
            return parts.Count == 0 ? "-" : string.Join(" / ", parts);
        }

        public static AbTestSummary Of(IReadOnlyList<AbParticipantRecord> records,
            int plannedParticipants = AbTestPlan.DefaultParticipants)
        {
            var s = new AbTestSummary { PlannedParticipants = plannedParticipants };

            if (records != null)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    AbParticipantRecord r = records[i];
                    if (r == null) continue;
                    if (!r.IsComplete) { s.Incomplete++; continue; }

                    s.Count++;
                    if (r.adopted == VariantId.A) s.AdoptedA++; else s.AdoptedB++;
                    if (r.AdoptedFeelOk) s.FeelOk++;
                    if (r.AdoptedAgain) s.AgainOk++;
                    if (r.syncComplaint) s.SyncComplaints++;
                    if (r.HasSafetyIncident) s.SafetyIncidents++;
                }
            }

            int n = plannedParticipants;
            int feelNeeded = AbTestPlan.RequiredCount(n, AbTestPlan.FeelRatio);
            int againNeeded = AbTestPlan.RequiredCount(n, AbTestPlan.AgainRatio);
            int majorityNeeded = AbTestPlan.RequiredCount(n, AbTestPlan.MajorityRatio);
            int syncAllowed = AbTestPlan.AllowedCount(n, AbTestPlan.SyncComplaintRatio);

            var criteria = new List<AbCriterion>
            {
                new AbCriterion("記録人数", $"{s.Count} 人", $"{n} 人", s.Count >= n),
                new AbCriterion($"採用案の手応え {AbTestPlan.MinFeel} 以上",
                    $"{s.FeelOk} 人", $"{feelNeeded} 人以上", s.FeelOk >= feelNeeded),
                new AbCriterion("すぐもう一度振りたい",
                    $"{s.AgainOk} 人", $"{againNeeded} 人以上", s.AgainOk >= againNeeded),
                new AbCriterion("同じ案を選んだ人数",
                    $"{AbTestPlan.LabelOf(s.Majority)} {s.MajorityCount} 人", $"{majorityNeeded} 人以上",
                    s.MajorityCount >= majorityNeeded),
                new AbCriterion("同期ずれの指摘",
                    $"{s.SyncComplaints} 人", $"{syncAllowed} 人以下", s.SyncComplaints <= syncAllowed),
                new AbCriterion("痛み・恐怖・ストラップ逸脱",
                    $"{s.SafetyIncidents} 件", "0 件", s.SafetyIncidents == 0)
            };

            bool passed = true;
            foreach (AbCriterion c in criteria)
            {
                if (!c.Passed) passed = false;
            }

            s.Criteria = criteria;
            s.Passed = passed;
            return s;
        }
    }
}
