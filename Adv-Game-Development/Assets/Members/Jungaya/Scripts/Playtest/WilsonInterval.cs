using System;

namespace Toufuku.Playtest
{
    /// <summary>割合とその 95% 信頼区間。</summary>
    public readonly struct RateWithInterval
    {
        /// <summary>事象の数（誤発射の件数など）。</summary>
        public readonly int Count;
        /// <summary>母数（意図した振り数・組数）。</summary>
        public readonly int Total;
        /// <summary>点推定（Count / Total）。母数 0 なら 0。</summary>
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

        /// <summary>"3/200 = 1.5%（95%CI 0.5〜4.3%）" の形。</summary>
        public string Describe()
        {
            if (!HasData) return "-";
            return $"{Count}/{Total} = {Rate * 100.0:0.0}%（95%CI {Lower * 100.0:0.0}〜{Upper * 100.0:0.0}%）";
        }

        public override string ToString() => Describe();
    }

    /// <summary>
    /// 割合の 95% 信頼区間（ウィルソン得点区間）— Issue #50
    ///
    /// T0-CD は「誤発射 ≤2%」のように 0 に近い割合を見るので、
    /// 正規近似（p ± z√(p(1-p)/n)）だと 0 件のとき区間が 0 幅になってしまい使えない。
    /// ウィルソン区間は 0 件でも上限が出るので、「0 件だったが母数がいくつまでなら言い切れるか」が残せる。
    ///
    /// 例: 0/200 → 上限 1.9%（2% 以下と言える）／0/50 → 上限 7.1%（母数が足りず言い切れない）。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class WilsonInterval
    {
        /// <summary>95% 両側の z 値。</summary>
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
