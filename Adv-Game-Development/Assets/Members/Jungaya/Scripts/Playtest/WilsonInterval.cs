using System;

namespace Toufuku.Playtest
{
    // わりあいと、その 95% 信頼区間
    public readonly struct RateWithInterval
    {
        // 起きた数（まちがい発射の件数など）
        public readonly int Count;
        // わる数（振ろうとした回数や組の数）
        public readonly int Total;
        // わりあいそのもの（Count / Total）。わる数が 0 なら 0
        public readonly double Rate;
        public readonly double Lower;
        public readonly double Upper;

        public RateWithInterval(int count, int total, double rate, double lower, double upper)
        {
            Count = count;
            Total = total;
            Rate = rate;
            Lower = lower;
            Upper = upper;
        }

        public bool HasData => Total > 0;

        // "3/200 = 1.5%（95%CI 0.5〜4.3%）" みたいな形の文字
        public string Describe()
        {
            if (!HasData) return "-";
            return $"{Count}/{Total} = {Rate * 100.0:0.0}%（95%CI {Lower * 100.0:0.0}〜{Upper * 100.0:0.0}%）";
        }

        public override string ToString() => Describe();
    }

    /*
        わりあいの 95% 信頼区間（ウィルソンの得点区間）を出すクラス（#50）

        T0-CD は「まちがい発射 2%以下」みたいに 0 に近いわりあいを見るので、
        ふつうの正規分布での近似（p ± z√(p(1-p)/n)）だと 0件のときに区間のはばが 0 になってしまって使えない
        ウィルソンの区間なら 0件でも上限が出るので、「0件だったけど、わる数がいくつあれば言いきれるか」が残せる

        例: 0/200 → 上限 1.9%（2%以下と言える）、0/50 → 上限 7.1%（わる数が足りなくて言いきれない）
        MonoBehaviour は使っていない
    */
    public static class WilsonInterval
    {
        // 95% 両側の z の値
        public const double Z95 = 1.959963984540054;

        public static RateWithInterval Of(int count, int total, double z = Z95)
        {
            if (total <= 0) return new RateWithInterval(count < 0 ? 0 : count, 0, 0.0, 0.0, 0.0);
            if (count < 0) count = 0;
            if (count > total) count = total;

            double n = total;
            double p = count / n;
            double z2 = z * z;
            double denominator = 1.0 + z2 / n;
            double center = (p + z2 / (2.0 * n)) / denominator;
            double half = z / denominator * Math.Sqrt(p * (1.0 - p) / n + z2 / (4.0 * n * n));

            double lower = center - half;
            double upper = center + half;
            if (lower < 0.0) lower = 0.0;
            if (upper > 1.0) upper = 1.0;

            return new RateWithInterval(count, total, p, lower, upper);
        }
    }
}
