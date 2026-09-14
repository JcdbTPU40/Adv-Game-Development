using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 3 分間の発射と着弾 — Issue #53
    ///
    /// ・投数 = 有効スイング確定（発射）の数。1 分ごとに数える。
    /// ・命中率 = 的に当たった着弾 ÷ 着弾（#63 と同じ定義）。着弾は<b>発射した分</b>へ入れる
    ///   （0:59.8 に投げて 1:00.3 に着いた弾は 1 分目）。3:00 直前に投げた弾も最後の分で数える。
    /// ・実操作周期 = 発射と発射の間隔（#63 の <c>throw_interval_sec</c> と同じ定義）。
    /// 時刻は試技開始からの秒で渡す（MonoBehaviour 非依存）。
    /// </summary>
    public sealed class EnduranceThrowLog
    {
        readonly List<double> _fires = new List<double>();
        readonly List<double> _landingLaunches = new List<double>();
        readonly List<bool> _landingHits = new List<bool>();

        public int Throws => _fires.Count;
        public int Landings => _landingLaunches.Count;

        public int Hits
        {
            get
            {
                int n = 0;
                foreach (bool hit in _landingHits)
                {
                    if (hit) n++;
                }
                return n;
            }
        }

        /// <summary>クールダウン中の振り（参考値。合否には使わない）。</summary>
        public int Rejected { get; private set; }

        public void AddFire(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0.0) return;
            _fires.Add(seconds);
        }

        public void AddRejected() => Rejected++;

        /// <param name="landedAt">着弾した時刻（試技開始からの秒）。</param>
        /// <param name="flightSeconds">実際の飛翔秒。発射した時刻 = 着弾 − 飛翔。</param>
        public void AddLanding(double landedAt, double flightSeconds, bool hit)
        {
            double launch = landedAt - Math.Max(0.0, flightSeconds);
            if (double.IsNaN(launch) || launch < 0.0) return; // 試技の前に投げた弾
            _landingLaunches.Add(launch);
            _landingHits.Add(hit);
        }

        public int ThrowsIn(int minute)
        {
            int n = 0;
            foreach (double t in _fires)
            {
                if (EnduranceTestPlan.MinuteOf(t) == minute) n++;
            }
            return n;
        }

        public int LandingsIn(int minute)
        {
            int n = 0;
            foreach (double t in _landingLaunches)
            {
                if (EnduranceTestPlan.MinuteOf(t) == minute) n++;
            }
            return n;
        }

        public int HitsIn(int minute)
        {
            int n = 0;
            for (int i = 0; i < _landingLaunches.Count; i++)
            {
                if (_landingHits[i] && EnduranceTestPlan.MinuteOf(_landingLaunches[i]) == minute) n++;
            }
            return n;
        }

        /// <summary>発射と発射の間隔（秒）。発射が 1 回以下なら空。</summary>
        public List<double> Intervals()
        {
            var sorted = new List<double>(_fires);
            sorted.Sort();
            var intervals = new List<double>(Math.Max(0, sorted.Count - 1));
            for (int i = 1; i < sorted.Count; i++)
            {
                double d = sorted[i] - sorted[i - 1];
                if (d > 0.0) intervals.Add(d);
            }
            return intervals;
        }

        public double? CycleMedian => PlaytestStats.Percentile(Intervals(), 0.50);
        public double? CycleP75 => PlaytestStats.Percentile(Intervals(), 0.75);

        public void Clear()
        {
            _fires.Clear();
            _landingLaunches.Clear();
            _landingHits.Clear();
            Rejected = 0;
        }
    }
}
