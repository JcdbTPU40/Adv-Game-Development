using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 実連投間隔の要約 — Issue #50
    ///
    /// 「自分の最速で 2 回振る」の 1 発目と 2 発目の間隔（秒）を集めて、件数・最小・中央値・p75 を出す。
    /// クールダウン値がこの分布のどこに来るかが、欠落が起きる／起きないの直接の理由になる
    /// （最小値よりクールダウンが長ければ、その組は必ず欠落する）。
    /// MonoBehaviour 非依存。
    /// </summary>
    public sealed class IntervalStats
    {
        readonly List<double> _values = new List<double>();
        bool _sorted = true;

        public int Count => _values.Count;

        public void Add(double seconds)
        {
            if (seconds <= 0.0) return;
            _values.Add(seconds);
            _sorted = false;
        }

        public void Clear()
        {
            _values.Clear();
            _sorted = true;
        }

        public double Min => Percentile(0.0);
        public double Median => Percentile(0.5);
        public double P75 => Percentile(0.75);

        /// <summary>0〜1 の位置の値（線形補間）。値が無ければ 0。</summary>
        public double Percentile(double fraction)
        {
            if (_values.Count == 0) return 0.0;
            Sort();

            if (fraction <= 0.0) return _values[0];
            if (fraction >= 1.0) return _values[_values.Count - 1];

            double position = fraction * (_values.Count - 1);
            int low = (int)position;
            int high = low + 1;
            if (high >= _values.Count) return _values[_values.Count - 1];

            double t = position - low;
            return _values[low] + (_values[high] - _values[low]) * t;
        }

        /// <summary>この間隔のうち、クールダウン秒数以上だった割合（＝欠落しなかったはずの割合）。</summary>
        public int CountAtLeast(double seconds)
        {
            int count = 0;
            for (int i = 0; i < _values.Count; i++)
            {
                if (_values[i] >= seconds) count++;
            }
            return count;
        }

        public string Describe()
        {
            if (_values.Count == 0) return "-";
            return $"n={_values.Count} 最小 {Min:0.000}s 中央 {Median:0.000}s p75 {P75:0.000}s";
        }

        void Sort()
        {
            if (_sorted) return;
            _values.Sort();
            _sorted = true;
        }
    }
}
