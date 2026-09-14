using UnityEngine;

namespace Toufuku.Feedback
{
    /// <summary>
    /// 福の連なり（連続命中）→ 命中音の音階と和音 — Issue #64（仕様書 v8 15章）
    ///
    /// ・連なりが 1 伸びるごとに音階を 1 段上げる。音階はメジャーペンタトニック（ド・レ・ミ・ソ・ラ）で、
    ///   どこで途切れても不協和にならず、数字を読まなくても「上がっている」ことが分かる。
    /// ・上がりすぎて耳に痛くならないよう、<c>maxStep</c> 段で頭打ちにする。
    /// ・3・6・10 連続の節目では根音に和音を足す（節目ごとに厚くする）。
    /// </summary>
    public static class ComboScale
    {
        /// <summary>1 オクターブ内の音階（根音からの半音数）。</summary>
        static readonly int[] s_degrees = { 0, 2, 4, 7, 9 };

        /// <summary>既定の頭打ち段数（10 段 = 2 オクターブ）。</summary>
        public const int DefaultMaxStep = 10;

        /// <summary>既定の節目。</summary>
        public static readonly int[] DefaultMilestones = { 3, 6, 10 };

        /// <summary>節目ごとに足す和音（根音からの半音数）。1 つ目の節目 = 5 度、2 つ目 = 3 度＋5 度、3 つ目以降 = 3 度＋5 度＋オクターブ。</summary>
        static readonly int[][] s_chords =
        {
            new[] { 7 },
            new[] { 4, 7 },
            new[] { 4, 7, 12 }
        };

        static readonly int[] s_noChord = new int[0];

        /// <summary>連なり数 → 音階の段（1 連なり目 = 0 段）。</summary>
        public static int StepOf(int combo, int maxStep = DefaultMaxStep)
        {
            return Mathf.Clamp(combo - 1, 0, Mathf.Max(0, maxStep));
        }

        /// <summary>音階の段 → 根音からの半音数。</summary>
        public static int SemitoneOfStep(int step)
        {
            if (step < 0) step = 0;
            return 12 * (step / s_degrees.Length) + s_degrees[step % s_degrees.Length];
        }

        /// <summary>連なり数 → 根音からの半音数。</summary>
        public static int SemitoneOf(int combo, int maxStep = DefaultMaxStep)
        {
            return SemitoneOfStep(StepOf(combo, maxStep));
        }

        /// <summary>半音数 → AudioSource.pitch に渡す再生速度。</summary>
        public static float PitchOf(int semitones)
        {
            return Mathf.Pow(2f, semitones / 12f);
        }

        /// <summary>
        /// 連なり数がちょうど節目なら、何番目の節目か（1 始まり）。節目でなければ 0。
        /// </summary>
        public static int MilestoneLevelOf(int combo, int[] milestones = null)
        {
            milestones = milestones ?? DefaultMilestones;
            for (int i = 0; i < milestones.Length; i++)
            {
                if (milestones[i] == combo) return i + 1;
            }
            return 0;
        }

        /// <summary>
        /// この連なり数で根音に足す和音（根音からの半音数）。節目でなければ空。
        /// </summary>
        public static int[] ChordOf(int combo, int[] milestones = null)
        {
            int level = MilestoneLevelOf(combo, milestones);
            if (level <= 0) return s_noChord;
            return s_chords[Mathf.Min(level, s_chords.Length) - 1];
        }
    }
}
