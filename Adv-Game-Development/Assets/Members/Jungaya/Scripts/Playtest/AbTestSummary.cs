using System.Collections.Generic;

namespace Toufuku.Playtest
{
    // 完了条件1つぶんの判定
    public readonly struct AbCriterion
    {
        public readonly string Name;
        // 実際の値（例: "8 人"）
        public readonly string Actual;
        // 合格ライン（例: "8 人以上"）
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

    /*
        1回ぶん（対象の10人）の合格・不合格を出すクラス（#49 / 企画書 v8 17章）

        完了条件と判定のしかた:
        ・10人中8人が、選んだ案の手ごたえを4/5以上にした → それぞれが選んだ案の手ごたえで数える
        ・10人中8人が「すぐもう一回振りたい」 → これも選んだ案への答えで数える
        ・10人中7人以上が同じ案を選ぶ → 多いほうの案を選んだ人数
        ・タイミングずれの指摘が10人中2人以下 → 指摘した人数
        ・痛い・こわい・ストラップが外れた、が0件 → 1件でも出たら不合格

        わる数は「予定の人数（ふつうは10人）」で、とちゅうでも同じラインで見る（足りなければ不合格のまま）
        書き終わっていない記録（AbParticipantRecord.IsComplete が false）は数に入れないで、Incomplete に出す
        「別の日・別の人で2回連続合格」は1回ぶんでは判定できないので、19章のテスト記録に2回ならべて確認する
        MonoBehaviour は使っていない
    */
    public sealed class AbTestSummary
    {
        public int PlannedParticipants { get; private set; }
        // 集計に入れた人数
        public int Count { get; private set; }
        // 書き足りなくて数えなかった人数
        public int Incomplete { get; private set; }
        public int AdoptedA { get; private set; }
        public int AdoptedB { get; private set; }
        // 選んだ案の手ごたえが4以上だった人数
        public int FeelOk { get; private set; }
        // 選んだ案で「すぐもう一回振りたい」を選んだ人数
        public int AgainOk { get; private set; }
        public int SyncComplaints { get; private set; }
        public int SafetyIncidents { get; private set; }

        // 多いほうの案を選んだ人数
        public int MajorityCount => AdoptedA >= AdoptedB ? AdoptedA : AdoptedB;
        // 多いほうの案（同じ数なら案A）
        public VariantId Majority => AdoptedA >= AdoptedB ? VariantId.A : VariantId.B;

        public IReadOnlyList<AbCriterion> Criteria { get; private set; } = new AbCriterion[0];

        // 完了条件をぜんぶ満たしたかどうか
        public bool Passed { get; private set; }

        // 不合格の項目だけをならべた1行（"-" なら合格）
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
