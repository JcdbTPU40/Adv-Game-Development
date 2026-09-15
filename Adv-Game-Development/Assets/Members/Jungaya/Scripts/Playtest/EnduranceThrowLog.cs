using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /*
        3分の間の発射と着弾を記録するクラス（#53）

        ・投げた数 = 有効スイングが決まった（発射した）数。1分ごとに数える
        ・命中率 = 的に当たった着弾 ÷ 着弾（#63 と同じ決め方）。着弾は発射した分のほうに入れる
          （0:59.8 に投げて 1:00.3 に落ちた弾は1分目）。3:00 の直前に投げた弾も最後の分で数える
        ・実際に振る間かく = 発射と発射の間（#63 の throw_interval_sec と同じ決め方）
        時刻はテストが始まってからの秒で渡す（MonoBehaviour は使っていない）
    */
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

        // クールダウン中の振り（参考の値。合格かどうかには使わない）
        public int Rejected { get; private set; }

        public void AddFire(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0.0) return;
            _fires.Add(seconds);
        }

        public void AddRejected() => Rejected++;

        /*
            landedAt: 落ちた時刻（テストが始まってからの秒）
            flightSeconds: 実際に飛んだ秒。発射した時刻 = 着弾 − 飛んだ秒
        */
        public void AddLanding(double landedAt, double flightSeconds, bool hit)
        {
            double launch = landedAt - Math.Max(0.0, flightSeconds);
            if (double.IsNaN(launch) || launch < 0.0) return; // テストの前に投げた弾
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

        // 発射と発射の間かく（秒）。発射が1回以下なら空
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
