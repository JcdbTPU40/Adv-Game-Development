using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /*
        コントローラー側の時計（ESP32 の millis）を Unity の時計に合わせるクラス（#52）

        受け取った行ごとに「Unity が受け取った時刻 − コントローラーの時刻」をためておく。この差は
        「時計のスタート地点の差 ＋ その行が送られてから読まれるまでの遅れ」なので、近くの行のいちばん小さい値を時計の差とする
        （いちばん待たされなかった行の遅れを 0 とする）。水晶のずれ（数十 ppm）で3分に数 ms 動くので、
        全体のいちばん小さい値じゃなくて、前後 UsbGatePlan.ClockWindowSeconds 秒のいちばん小さい値を使う

        ・USB で送る時間そのもの（少なくても数 ms）は 0 にしてしまうので、出てくる入力の遅れは実際より短めになる
          その差は外で撮った動画で何投かだけ確かめる（Docs/52 §5）
        ・受け取った時刻は小さい順に届くことにしている（Time.realtimeSinceStartupAsDouble）。時刻がもどっている行はすてる
        MonoBehaviour は使っていない
    */
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

        // 受け取った1行ぶん。コントローラーの時刻がない（NaN）行は無視する
        public void Add(double receiveTime, double deviceTime)
        {
            if (double.IsNaN(receiveTime) || double.IsNaN(deviceTime)) return;
            if (_receive.Count > 0 && receiveTime < _receive[_receive.Count - 1]) return;
            _receive.Add(receiveTime);
            _offset.Add(receiveTime - deviceTime);
        }

        // 受け取った時刻 receiveTime の前後 window 秒にある行から、時計の差（Unity − コントローラー）を出す。なければ null
        public double? OffsetAt(double receiveTime, double window = UsbGatePlan.ClockWindowSeconds)
        {
            if (_receive.Count == 0 || double.IsNaN(receiveTime)) return null;

            int i = LowerBound(receiveTime - window);
            double best = double.PositiveInfinity;
            for (; i < _receive.Count && _receive[i] <= receiveTime + window; i++)
                best = Math.Min(best, _offset[i]);
            return double.IsPositiveInfinity(best) ? (double?)null : best;
        }

        // コントローラーの時刻 deviceTime（受け取ったのは receiveTime）を Unity の時計に直す
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
