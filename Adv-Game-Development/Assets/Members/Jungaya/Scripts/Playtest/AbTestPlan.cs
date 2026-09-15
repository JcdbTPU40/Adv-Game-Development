using System;

namespace Toufuku.Playtest
{
    /*
        T0-A/B の順番の割りふり・時間・合格ラインの数値（#49 / 企画書 v8 17章）

        ・対象の10人を5人ずつ BA と AB に分ける（順番のえいきょうを打ち消すため）
        ・1つの案は45秒、案と案の間に60秒休けい
        ・合格ラインは「10人中何人」のわりあいで持っておいて、人数が変わっても同じわりあいで判定する
        MonoBehaviour は使っていない
    */
    public static class AbTestPlan
    {
        public const int DefaultParticipants = 10;
        public const float TrialSeconds = 45f;
        public const float RestSeconds = 60f;

        // 選んだ案の手ごたえが4以上であってほしい人数のわりあい（8/10）
        public const double FeelRatio = 0.8;
        // 「すぐもう一回振りたい」を選んでほしい人数のわりあい（8/10）
        public const double AgainRatio = 0.8;
        // 同じ案を選んでほしい人数のわりあい（7/10）
        public const double MajorityRatio = 0.7;
        // 映像・音・振動のずれを指摘してもいい人数のわりあい（2/10 まで）
        public const double SyncComplaintRatio = 0.2;
        // 手ごたえ5段階のうち、合格にする一番下の値
        public const int MinFeel = 4;

        const double Epsilon = 1e-9;

        // 参加者の番号（1から）から、見せる順番を決める。奇数は AB、偶数は BA なので、10人なら5人ずつになる
        public static AbOrder OrderOf(int participantNumber) =>
            participantNumber % 2 != 0 ? AbOrder.AB : AbOrder.BA;

        // その順番で trialIndex 番目（0 か 1）に見せる案
        public static VariantId VariantAt(AbOrder order, int trialIndex)
        {
            bool first = trialIndex <= 0;
            if (order == AbOrder.AB) return first ? VariantId.A : VariantId.B;
            return first ? VariantId.B : VariantId.A;
        }

        // participants 人のうち、その順番になる人数
        public static int CountOf(int participants, AbOrder order)
        {
            if (participants <= 0) return 0;
            int ab = (participants + 1) / 2;
            return order == AbOrder.AB ? ab : participants - ab;
        }

        // 「8/10 以上」みたいな、一番少なくていい人数（切り上げ）
        public static int RequiredCount(int participants, double ratio)
        {
            if (participants <= 0) return 0;
            return (int)Math.Ceiling(participants * ratio - Epsilon);
        }

        // 「2/10 以下」みたいな、一番多くていい人数（切り捨て）
        public static int AllowedCount(int participants, double ratio)
        {
            if (participants <= 0) return 0;
            return (int)Math.Floor(participants * ratio + Epsilon);
        }

        // 1人あたりにかかる時間（秒）。アンケートの時間は入れていない
        public static float SecondsPerParticipant(float trialSeconds = TrialSeconds, float restSeconds = RestSeconds) =>
            trialSeconds * 2f + restSeconds;

        public static string LabelOf(VariantId id) => id == VariantId.A ? "案A" : "案B";

        public static string LabelOf(AbOrder order) => order == AbOrder.AB ? "A→B" : "B→A";
    }
}
