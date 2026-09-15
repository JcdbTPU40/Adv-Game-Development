using UnityEngine;

namespace Toufuku.Feedback
{
    /*
        福の連なり（連続で当てた数）から、命中音の音の高さと和音を決めるクラス（#64 / 企画書 v8 15章）

        ・連なりが1つのびるごとに、音を1段上げる。音階はメジャーペンタトニック（ド・レ・ミ・ソ・ラ）なので、
          どこで止まっても変な音にならないし、数字を見なくても「上がってる」のがわかる
        ・上がりすぎて耳が痛くならないように、maxStep 段で止める
        ・3・6・10 連続のきりのいいところでは、和音を足して音を厚くする
    */
    public static class ComboScale
    {
        // 1オクターブの中の音階（根音から何半音か）
        static readonly int[] s_degrees = { 0, 2, 4, 7, 9 };

        // ふつうの上限の段数（10段 = 2オクターブ）
        public const int DefaultMaxStep = 10;

        // ふつうのきりのいいところ
        public static readonly int[] DefaultMilestones = { 3, 6, 10 };

        // きりのいいところで足す和音（根音から何半音か）。1つ目は5度、2つ目は3度と5度、3つ目からは3度と5度とオクターブ
        static readonly int[][] s_chords =
        {
            new[] { 7 },
            new[] { 4, 7 },
            new[] { 4, 7, 12 }
        };

        static readonly int[] s_noChord = new int[0];

        // 連なりの数から音の段を出す（1つ目の連なりは0段）
        public static int StepOf(int combo, int maxStep = DefaultMaxStep)
        {
            return Mathf.Clamp(combo - 1, 0, Mathf.Max(0, maxStep));
        }

        // 音の段から、根音から何半音かを出す
        public static int SemitoneOfStep(int step)
        {
            if (step < 0) step = 0;
            return 12 * (step / s_degrees.Length) + s_degrees[step % s_degrees.Length];
        }

        // 連なりの数から、根音から何半音かを出す
        public static int SemitoneOf(int combo, int maxStep = DefaultMaxStep)
        {
            return SemitoneOfStep(StepOf(combo, maxStep));
        }

        // 半音の数から、AudioSource.pitch に入れる再生速度を出す
        public static float PitchOf(int semitones)
        {
            return Mathf.Pow(2f, semitones / 12f);
        }

        // 連なりの数がちょうどきりのいいところなら、何番目か（1から数える）を返す。ちがったら 0
        public static int MilestoneLevelOf(int combo, int[] milestones = null)
        {
            milestones = milestones ?? DefaultMilestones;
            for (int i = 0; i < milestones.Length; i++)
            {
                if (milestones[i] == combo) return i + 1;
            }
            return 0;
        }

        // この連なりの数で根音に足す和音（根音から何半音か）を返す。きりのいいところじゃなければ空
        public static int[] ChordOf(int combo, int[] milestones = null)
        {
            int level = MilestoneLevelOf(combo, milestones);
            if (level <= 0) return s_noChord;
            return s_chords[Mathf.Min(level, s_chords.Length) - 1];
        }
    }
}
