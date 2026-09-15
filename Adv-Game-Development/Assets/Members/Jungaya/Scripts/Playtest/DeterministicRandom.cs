namespace Toufuku.Playtest
{
    /*
        決まったシードから作る乱数（#63 / 企画書 v8 17章）

        ・UnityEngine.Random（全体で1つを使いまわしている）とは別の乱数。ほかの処理が乱数を使っても、こっちの順番はずれない
        ・SplitMix64 を使っている。同じシードなら、OS や Unity のバージョンがちがっても同じ数字がならぶ
        ・Derive で「シード × 使いみち × 客ID」ごとに別の乱数を作る。n 人目の客のくじは、
          それまでに何発当てたかや、どの客が先に帰ったかで変わらない
        ・MonoBehaviour も UnityEngine も使っていない（EditMode テストで確かめる）
    */
    public sealed class DeterministicRandom
    {
        const ulong Golden = 0x9E3779B97F4A7C15UL;

        ulong _state;

        public DeterministicRandom(ulong seed)
        {
            _state = seed;
        }

        // シード・使いみち・番号（客IDなど）から、別々の乱数を作る
        public static DeterministicRandom Derive(int seed, int stream, int index)
        {
            return new DeterministicRandom(Mix(Mix(Mix((uint)seed) ^ (uint)stream) ^ (uint)index));
        }

        // シードから、使いみちごとの子どものシードを作る（定位置をしきつめるときみたいに、番号がないくじ用）
        public static int DeriveSeed(int seed, int stream)
        {
            return (int)(Mix(Mix((uint)seed) ^ (uint)stream) >> 32);
        }

        // SplitMix64 のかきまぜる関数
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

        // 0 以上 1 未満の小数
        public double NextDouble()
        {
            return (NextULong() >> 11) * (1.0 / (1UL << 53));
        }

        // 0 以上 1 未満の小数（float）
        public float Value()
        {
            float f = (float)NextDouble();
            return f >= 1f ? 0.99999994f : f;
        }

        // min 以上 max 未満の整数。max が min 以下なら min（UnityEngine.Random.Range(int, int) と同じ）
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            long span = (long)maxExclusive - minInclusive;
            return (int)(minInclusive + (long)(NextDouble() * span));
        }

        // min〜max の小数
        public float Range(float min, float max)
        {
            return min + (max - min) * (float)NextDouble();
        }
    }
}
