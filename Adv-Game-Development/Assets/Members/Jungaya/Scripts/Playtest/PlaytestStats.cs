using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /*
        分位点を出すクラス（#63）

        あいだを直線でうめるやり方（Excel の PERCENTILE.INC や numpy.percentile のふつうの設定と同じ）
        集計のスクリプト（Tools/playtest_aggregate.py）も同じやり方で計算する
    */
    public static class PlaytestStats
    {
        // p（0〜1）の分位点。値がなければ null。NaN はのぞく
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

        // 小さい順にならんだ値の p の分位点
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
