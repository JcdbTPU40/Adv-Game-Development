using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /// <summary>
    /// T0-CD の割り付け・母数・合格ラインの数値 — Issue #50（仕様書 v8 17章・付録B INPUT.CD）
    ///
    /// ・比べるのは 0.40 / 0.50 / 0.60 / 0.65 秒の 4 条件（付録B の候補値）。
    /// ・操作経験の異なる 8 人を 4×4 のラテン方格（<see cref="OrderTable"/>）の各順序へ 2 人ずつ割り付ける。
    /// ・判定は人数ではなく <b>投数</b>。1 値あたり合計で 単発 100 回 と「自分の最速で 2 回振る」50 組。
    /// ・合格ラインは「誤発射 ≤2%」「意図的連投の欠落 ≤2%」の両方を満たす <b>最小値</b>。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class CooldownTestPlan
    {
        /// <summary>比べる 4 条件。並びは秒数の小さい順（採用は「満たす最小値」なのでこの順で探す）。</summary>
        public static readonly CooldownPreset[] Conditions =
        {
            CooldownPreset.Sec040,
            CooldownPreset.Sec050,
            CooldownPreset.Sec060,
            CooldownPreset.Sec065
        };

        public const int ConditionCount = 4;

        /// <summary>操作経験の異なる 8 人（4 順序 × 2 人）。</summary>
        public const int DefaultParticipants = 8;

        /// <summary>1 値あたりの単発の合計回数。</summary>
        public const int SinglesPerCondition = 100;
        /// <summary>1 値あたりの「自分の最速で 2 回振る」の合計組数。</summary>
        public const int PairsPerCondition = 50;

        /// <summary>誤発射（余分な発射）の上限。</summary>
        public const double MaxMisfireRate = 0.02;
        /// <summary>意図的連投の 2 発目欠落の上限。</summary>
        public const double MaxMissedSecondRate = 0.02;

        /// <summary>ブロックの間の休憩（秒）。</summary>
        public const float BlockRestSeconds = 20f;
        /// <summary>条件と条件の間の休憩（秒）。値が変わったことに慣れる時間も兼ねる。</summary>
        public const float ConditionRestSeconds = 60f;

        /// <summary>
        /// 4 条件のラテン方格（ウィリアムズ計画）。行 = 順序、列 = 何番目に試すか、値 = <see cref="Conditions"/> の添字。
        ///
        /// 各列に 4 条件が 1 回ずつ出る（位置の効果を打ち消す）うえに、
        /// 「A の次に B」という並びも 12 通りすべてが 1 回ずつになる（直前の条件の引きずりも打ち消す）。
        /// </summary>
        public static readonly int[][] OrderTable =
        {
            new[] { 0, 1, 3, 2 },
            new[] { 1, 2, 0, 3 },
            new[] { 2, 3, 1, 0 },
            new[] { 3, 0, 2, 1 }
        };

        /// <summary>参加者番号（1 始まり）→ 順序の行番号（0〜3）。8 人なら各行 2 人ずつになる。</summary>
        public static int OrderIndexOf(int participantNumber)
        {
            int zeroBased = participantNumber - 1;
            if (zeroBased < 0) zeroBased = 0;
            return zeroBased % ConditionCount;
        }

        /// <summary>その順序で trialIndex 番目（0〜3）に試す条件。</summary>
        public static CooldownPreset ConditionAt(int orderIndex, int trialIndex)
        {
            int[] row = OrderTable[Wrap(orderIndex)];
            return Conditions[row[Wrap(trialIndex)]];
        }

        /// <summary>participants 人のうち、その順序に割り付く人数。</summary>
        public static int CountOf(int participants, int orderIndex)
        {
            if (participants <= 0) return 0;
            int full = participants / ConditionCount;
            int remainder = participants % ConditionCount;
            return full + (Wrap(orderIndex) < remainder ? 1 : 0);
        }

        /// <summary>
        /// 合計 total 回を participants 人へ分ける。余りは若い番号から 1 回ずつ足す。
        /// 100 回を 8 人なら 13,13,13,13,12,12,12,12（合計 100）。
        /// </summary>
        public static int ShareOf(int total, int participants, int participantIndex)
        {
            if (participants <= 0 || total <= 0) return 0;
            if (participantIndex < 0) participantIndex = 0;
            participantIndex %= participants;

            int full = total / participants;
            int remainder = total % participants;
            return full + (participantIndex < remainder ? 1 : 0);
        }

        /// <summary>参加者番号（1 始まり）から、その人が 1 条件で行う単発の回数。</summary>
        public static int SinglesFor(int participantNumber, int participants = DefaultParticipants) =>
            ShareOf(SinglesPerCondition, participants, participantNumber - 1);

        /// <summary>参加者番号（1 始まり）から、その人が 1 条件で行う連投の組数。</summary>
        public static int PairsFor(int participantNumber, int participants = DefaultParticipants) =>
            ShareOf(PairsPerCondition, participants, participantNumber - 1);

        /// <summary>1 値あたりの「意図した振り」の合計（誤発射率の母数）。単発 1 回 + 連投 1 組 = 2 回。</summary>
        public const int IntendedSwingsPerCondition = SinglesPerCondition + PairsPerCondition * 2;

        public static float SecondsOf(CooldownPreset preset)
        {
            switch (preset)
            {
                case CooldownPreset.Sec040: return 0.40f;
                case CooldownPreset.Sec050: return 0.50f;
                case CooldownPreset.Sec060: return 0.60f;
                case CooldownPreset.Sec065: return 0.65f;
                default: return 0f;
            }
        }

        public static string LabelOf(CooldownPreset preset) =>
            preset == CooldownPreset.Custom ? "任意" : $"{SecondsOf(preset):0.00}秒";

        /// <summary>秒数から条件を引く（CSV の読み戻し用）。候補にない秒数は <see cref="CooldownPreset.Custom"/>。</summary>
        public static CooldownPreset PresetOf(float seconds)
        {
            for (int i = 0; i < Conditions.Length; i++)
            {
                if (UnityEngine.Mathf.Abs(SecondsOf(Conditions[i]) - seconds) < 0.005f) return Conditions[i];
            }
            return CooldownPreset.Custom;
        }

        /// <summary>1 人あたりにかかるおおよその時間（秒）。振っている時間は含まない（休憩だけの目安）。</summary>
        public static float RestSecondsPerParticipant(
            float blockRest = BlockRestSeconds, float conditionRest = ConditionRestSeconds) =>
            (blockRest + conditionRest) * ConditionCount;

        static int Wrap(int index)
        {
            index %= ConditionCount;
            return index < 0 ? index + ConditionCount : index;
        }
    }
}
