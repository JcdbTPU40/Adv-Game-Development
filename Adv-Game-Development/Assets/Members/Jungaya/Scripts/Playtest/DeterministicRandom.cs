namespace Toufuku.Playtest
{
    /// <summary>
    /// 固定シードの乱数列 — Issue #63（仕様書 v8 17章）
    ///
    /// ・UnityEngine.Random（全体で 1 本を共有）とは独立した列。ほかの処理が乱数を引いても列がずれない。
    /// ・SplitMix64。同じシードからは OS・Unity のバージョンによらず同じ列になる。
    /// ・<see cref="Derive"/> で「シード × 用途 × 客ID」ごとに別の列を作る。n 体目の客の抽選は、
    ///   それまでに何発当てたか・どの客が先に退場したかに左右されない。
    /// ・MonoBehaviour・UnityEngine に依存しない（EditMode テストで検証する）。
    /// </summary>
    public sealed class DeterministicRandom
    {
        const ulong Golden = 0x9E3779B97F4A7C15UL;

        ulong _state;

        public DeterministicRandom(ulong seed)
        {
            _state = seed;
        }

        /// <summary>シード・用途・番号（客ID など）から独立した列を作る。</summary>
        public static DeterministicRandom Derive(int seed, int stream, int index)
        {
            return new DeterministicRandom(Mix(Mix(Mix((uint)seed) ^ (uint)stream) ^ (uint)index));
        }

        /// <summary>シードから用途ごとの子シードを作る（定位置の敷き詰めなど、番号を持たない抽選用）。</summary>
        public static int DeriveSeed(int seed, int stream)
        {
            return (int)(Mix(Mix((uint)seed) ^ (uint)stream) >> 32);
        }

        /// <summary>SplitMix64 の攪拌関数。</summary>
        public static ulong Mix(ulong z)
        {
            z += Golden;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            _state += Golden;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>[0, 1) の実数。</summary>
        public double NextDouble()
        {
            return (NextULong() >> 11) * (1.0 / (1UL << 53));
        }

        /// <summary>[0, 1) の実数（float）。</summary>
        public float Value()
        {
            float f = (float)NextDouble();
            return f >= 1f ? 0.99999994f : f;
        }

        /// <summary>[min, max) の整数。max ≤ min なら min（UnityEngine.Random.Range(int, int) と同じ）。</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            long span = (long)maxExclusive - minInclusive;
            return (int)(minInclusive + (long)(NextDouble() * span));
        }

        /// <summary>min〜max の実数。</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * (float)NextDouble();
        }
    }
}
