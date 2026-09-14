using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 分位点 — Issue #63
    ///
    /// 線形補間（Excel の PERCENTILE.INC、numpy.percentile の既定と同じ）。
    /// 集計スクリプト（Tools/playtest_aggregate.py）も同じ定義で計算する。
    /// </summary>
    public static class PlaytestStats
    {
        /// <summary>p（0〜1）の分位点。値が無ければ null。NaN は除く。</summary>
        public static double? Percentile(IEnumerable<double> values, double p)
        {
            var sorted = new List<double>();
            foreach (double v in values)
            {
                if (!double.IsNaN(v)) sorted.Add(v);
            }
            if (sorted.Count == 0) return null;
            sorted.Sort();
            return PercentileSorted(sorted, p);
        }

        /// <summary>昇順に並んだ値の p 分位点。</summary>
        public static double PercentileSorted(IReadOnlyList<double> sorted, double p)
        {
            if (sorted == null || sorted.Count == 0) throw new ArgumentException("値がありません", nameof(sorted));

            p = Math.Max(0.0, Math.Min(1.0, p));
            double h = (sorted.Count - 1) * p;
            int lo = (int)Math.Floor(h);
            int hi = Math.Min(lo + 1, sorted.Count - 1);
            return sorted[lo] + (h - lo) * (sorted[hi] - sorted[lo]);
        }

        public static double? Median(IEnumerable<double> values)
        {
            return Percentile(values, 0.5);
        }
    }
}
