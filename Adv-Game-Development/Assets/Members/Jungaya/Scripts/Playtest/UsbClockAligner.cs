using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /// <summary>
    /// コントローラ側の時計（ESP32 の millis）を Unity の時計へ合わせる — Issue #52
    ///
    /// 受信行ごとに「Unity 受信時刻 − コントローラ時刻」を溜める。この差は
    /// 「時計の原点の差 ＋ その行が送られてから読まれるまでの遅れ」なので、<b>近くの行の最小値</b>を時計の差とみなす
    /// （いちばん待たされなかった行の遅れを 0 と置く）。水晶のずれ（数十 ppm）で 3 分に数 ms 動くので、
    /// 全体の最小ではなく前後 <see cref="UsbGatePlan.ClockWindowSeconds"/> 秒の最小を使う。
    ///
    /// ・USB の転送そのもの（最小でも数 ms）は 0 と置くので、推定した入力遅延は<b>実際より短めに出る</b>。
    ///   その差は外部動画で数投だけ確かめる（Docs/52 §5）。
    /// ・受信時刻は昇順に届く前提（Time.realtimeSinceStartupAsDouble）。逆行した行は捨てる。
    /// MonoBehaviour 非依存。
    /// </summary>
    public sealed class UsbClockAligner
    {
        readonly List<double> _receive = new List<double>(20000);
        readonly List<double> _offset = new List<double>(20000);

        public int Count => _receive.Count;

        public void Clear()
        {
            _receive.Clear();
            _offset.Clear();
        }

        /// <summary>受信 1 行ぶん。コントローラ時刻が無い（NaN）行は無視する。</summary>
        public void Add(double receiveTime, double deviceTime)
        {
            if (double.IsNaN(receiveTime) || double.IsNaN(deviceTime)) return;
            if (_receive.Count > 0 && receiveTime < _receive[_receive.Count - 1]) return;
            _receive.Add(receiveTime);
            _offset.Add(receiveTime - deviceTime);
        }

        /// <summary>受信時刻 receiveTime の前後 window 秒にある行から、時計の差（Unity − コントローラ）を推定する。無ければ null。</summary>
        public double? OffsetAt(double receiveTime, double window = UsbGatePlan.ClockWindowSeconds)
        {
            if (_receive.Count == 0 || double.IsNaN(receiveTime)) return null;

            int i = LowerBound(receiveTime - window);
            double best = double.PositiveInfinity;
            for (; i < _receive.Count && _receive[i] <= receiveTime + window; i++)
                best = Math.Min(best, _offset[i]);
            return double.IsPositiveInfinity(best) ? (double?)null : best;
        }

        /// <summary>コントローラ時刻 deviceTime（受信は receiveTime）を Unity の時計へ直す。</summary>
        public double? ToUnity(double deviceTime, double receiveTime, double window = UsbGatePlan.ClockWindowSeconds)
        {
            if (double.IsNaN(deviceTime)) return null;
            double? offset = OffsetAt(receiveTime, window);
            return offset.HasValue ? deviceTime + offset.Value : (double?)null;
        }

        int LowerBound(double value)
        {
            int lo = 0, hi = _receive.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_receive[mid] < value) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
}
