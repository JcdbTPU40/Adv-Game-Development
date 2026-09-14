using System;

namespace Toufuku.Playtest
{
    /// <summary>
    /// T0-A/B の割り付け・時間・合格ラインの数値 — Issue #49（仕様書 v8 17章）
    ///
    /// ・対象層 10 人を 5 人ずつ BA / AB へ割り付ける（順番の効果を打ち消す）。
    /// ・1 案 45 秒、案と案の間に 60 秒休憩。
    /// ・合格ラインは「10 人中」の割合で持ち、人数が変わっても同じ割合で判定する。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class AbTestPlan
    {
        public const int DefaultParticipants = 10;
        public const float TrialSeconds = 45f;
        public const float RestSeconds = 60f;

        /// <summary>採用案の手応えが 4 以上であるべき人数の割合（8/10）。</summary>
        public const double FeelRatio = 0.8;
        /// <summary>「すぐもう一度振りたい」を選ぶべき人数の割合（8/10）。</summary>
        public const double AgainRatio = 0.8;
        /// <summary>同じ案を選ぶべき人数の割合（7/10）。</summary>
        public const double MajorityRatio = 0.7;
        /// <summary>映像・音・振動の同期ずれを指摘してよい人数の割合（2/10 まで）。</summary>
        public const double SyncComplaintRatio = 0.2;
        /// <summary>手応え 5 段階のうち、合格とみなす下限。</summary>
        public const int MinFeel = 4;

        const double Epsilon = 1e-9;

        /// <summary>参加者番号（1 始まり）→ 提示順。奇数が AB、偶数が BA なので 10 人なら 5 人ずつになる。</summary>
        public static AbOrder OrderOf(int participantNumber) =>
            participantNumber % 2 != 0 ? AbOrder.AB : AbOrder.BA;

        /// <summary>その順番で trialIndex 番目（0 or 1）に見せる案。</summary>
        public static VariantId VariantAt(AbOrder order, int trialIndex)
        {
            bool first = trialIndex <= 0;
            if (order == AbOrder.AB) return first ? VariantId.A : VariantId.B;
            return first ? VariantId.B : VariantId.A;
        }

        /// <summary>participants 人のうち、その順番に割り付く人数。</summary>
        public static int CountOf(int participants, AbOrder order)
        {
            if (participants <= 0) return 0;
            int ab = (participants + 1) / 2;
            return order == AbOrder.AB ? ab : participants - ab;
        }

        /// <summary>「8/10 以上」のような下限の人数（切り上げ）。</summary>
        public static int RequiredCount(int participants, double ratio)
        {
            if (participants <= 0) return 0;
            return (int)Math.Ceiling(participants * ratio - Epsilon);
        }

        /// <summary>「2/10 以下」のような上限の人数（切り捨て）。</summary>
        public static int AllowedCount(int participants, double ratio)
        {
            if (participants <= 0) return 0;
            return (int)Math.Floor(participants * ratio + Epsilon);
        }

        /// <summary>1 人あたりにかかる時間（秒）。アンケートの時間は含まない。</summary>
        public static float SecondsPerParticipant(float trialSeconds = TrialSeconds, float restSeconds = RestSeconds) =>
            trialSeconds * 2f + restSeconds;

        public static string LabelOf(VariantId id) => id == VariantId.A ? "案A" : "案B";

        public static string LabelOf(AbOrder order) => order == AbOrder.AB ? "A→B" : "B→A";
    }
}
