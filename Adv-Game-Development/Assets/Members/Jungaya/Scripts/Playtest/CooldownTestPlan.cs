using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /*
        T0-CD の割りふり・わる数・合格ラインの数値（#50 / 企画書 v8 17章・付録B INPUT.CD）

        ・くらべるのは 0.40 / 0.50 / 0.60 / 0.65 秒の4つの条件（付録B の候補の値）
        ・操作になれている度合いがちがう8人を、4×4 のラテン方格（OrderTable）のそれぞれの順番に2人ずつ割りふる
        ・判定は人数じゃなくて投げた数。1つの値につき、合計で1回振り100回と「自分の最速で2回振る」50組
        ・合格ラインは「まちがい発射 2%以下」と「わざと連投したときの抜け 2%以下」の両方を満たす、いちばん小さい値
        MonoBehaviour は使っていない
    */
    public static class CooldownTestPlan
    {
        // くらべる4つの条件。秒数が小さい順（「満たすいちばん小さい値」を選ぶので、この順でさがす）
        public static readonly CooldownPreset[] Conditions =
        {
            CooldownPreset.Sec040,
            CooldownPreset.Sec050,
            CooldownPreset.Sec060,
            CooldownPreset.Sec065
        };

        public const int ConditionCount = 4;

        // 操作になれている度合いがちがう8人（4つの順番 × 2人）
        public const int DefaultParticipants = 8;

        // 1つの値につき、1回振りの合計の回数
        public const int SinglesPerCondition = 100;
        // 1つの値につき、「自分の最速で2回振る」の合計の組の数
        public const int PairsPerCondition = 50;

        // まちがい発射（よけいな発射）の上限
        public const double MaxMisfireRate = 0.02;
        // わざと連投したときの2発目の抜けの上限
        public const double MaxMissedSecondRate = 0.02;

        // ブロックとブロックの間の休けい（秒）
        public const float BlockRestSeconds = 20f;
        // 条件と条件の間の休けい（秒）。値が変わったのになれる時間でもある
        public const float ConditionRestSeconds = 60f;

        /*
            4つの条件のラテン方格（ウィリアムズ計画）。行 = 順番、列 = 何番目にためすか、値 = Conditions の番号

            どの列にも4つの条件が1回ずつ出る（何番目にやるかのえいきょうを打ち消す）うえに、
            「Aの次にB」というならびも12通りぜんぶが1回ずつになる（直前の条件を引きずるのも打ち消す）
        */
        public static readonly int[][] OrderTable =
        {
            new[] { 0, 1, 3, 2 },
            new[] { 1, 2, 0, 3 },
            new[] { 2, 3, 1, 0 },
            new[] { 3, 0, 2, 1 }
        };

        // 参加者の番号（1から）から、順番の行の番号（0〜3）を出す。8人ならどの行も2人ずつになる
        public static int OrderIndexOf(int participantNumber)
        {
            int zeroBased = participantNumber - 1;
            if (zeroBased < 0) zeroBased = 0;
            return zeroBased % ConditionCount;
        }

        // その順番で trialIndex 番目（0〜3）にためす条件
        public static CooldownPreset ConditionAt(int orderIndex, int trialIndex)
        {
            int[] row = OrderTable[Wrap(orderIndex)];
            return Conditions[row[Wrap(trialIndex)]];
        }

        // participants 人のうち、その順番になる人数
        public static int CountOf(int participants, int orderIndex)
        {
            if (participants <= 0) return 0;
            int full = participants / ConditionCount;
            int remainder = participants % ConditionCount;
            return full + (Wrap(orderIndex) < remainder ? 1 : 0);
        }

        /*
            合計 total 回を participants 人に分ける。余りは若い番号の人から1回ずつ足す
            100回を8人なら 13,13,13,13,12,12,12,12（合計100）
        */
        public static int ShareOf(int total, int participants, int participantIndex)
        {
            if (participants <= 0 || total <= 0) return 0;
            if (participantIndex < 0) participantIndex = 0;
            participantIndex %= participants;

            int full = total / participants;
            int remainder = total % participants;
            return full + (participantIndex < remainder ? 1 : 0);
        }

        // 参加者の番号（1から）から、その人が1つの条件でやる1回振りの回数を出す
        public static int SinglesFor(int participantNumber, int participants = DefaultParticipants) =>
            ShareOf(SinglesPerCondition, participants, participantNumber - 1);

        // 参加者の番号（1から）から、その人が1つの条件でやる連投の組の数を出す
        public static int PairsFor(int participantNumber, int participants = DefaultParticipants) =>
            ShareOf(PairsPerCondition, participants, participantNumber - 1);

        // 1つの値につき「振ろうとした回数」の合計（まちがい発射のわりあいのわる数）。1回振りは1回、連投1組は2回
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

        // 秒数から条件をさがす（CSV を読みもどすとき用）。候補にない秒数は CooldownPreset.Custom
        public static CooldownPreset PresetOf(float seconds)
        {
            for (int i = 0; i < Conditions.Length; i++)
            {
                if (UnityEngine.Mathf.Abs(SecondsOf(Conditions[i]) - seconds) < 0.005f) return Conditions[i];
            }
            return CooldownPreset.Custom;
        }

        // 1人あたりにかかるだいたいの時間（秒）。振っている時間は入れていない（休けいだけの目安）
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
