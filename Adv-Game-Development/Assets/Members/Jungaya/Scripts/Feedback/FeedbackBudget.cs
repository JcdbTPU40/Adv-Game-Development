using System;

namespace Toufuku.Feedback
{
    /// <summary>1 投のフィードバック予算の区分。</summary>
    public enum FeedbackCategory
    {
        Swing = 0,  // 振りピーク → 投擲SE
        Hit = 1,    // 着弾予定時刻 → 命中音
        Rescue = 2  // 着弾予定時刻 → 救済音
    }

    /// <summary>
    /// 1 投のフィードバック予算の計測 — Issue #64（仕様書 v8 1章）
    ///
    /// | 区分 | 起点 | 終点 | 予算 |
    /// |---|---|---|---|
    /// | Swing | 振りピークのサンプル受信時刻（SwingAccepted.Time） | 投擲SE の再生要求 | 80ms |
    /// | Hit | 弾の着弾予定時刻（発射時刻 ＋ 飛翔時間） | 命中音の再生要求 | 50ms |
    /// | Rescue | 同上 | 救済音の再生要求 | 250ms |
    ///
    /// オーディオ出力バッファ分の遅延は含まない（HUD に別途表示する）。MonoBehaviour 非依存。
    /// </summary>
    public sealed class FeedbackBudget
    {
        public const double SwingBudgetSeconds = 0.080;
        public const double HitBudgetSeconds = 0.050;
        public const double RescueBudgetSeconds = 0.250;

        static readonly int s_count = Enum.GetValues(typeof(FeedbackCategory)).Length;

        readonly double[] _last = new double[s_count];
        readonly double[] _max = new double[s_count];
        readonly int[] _samples = new int[s_count];
        readonly int[] _violations = new int[s_count];

        public static double LimitOf(FeedbackCategory category)
        {
            switch (category)
            {
                case FeedbackCategory.Swing: return SwingBudgetSeconds;
                case FeedbackCategory.Hit: return HitBudgetSeconds;
                default: return RescueBudgetSeconds;
            }
        }

        /// <summary>遅延を記録する。予算内なら true。負の遅延（時計の丸め）は 0 とみなす。</summary>
        public bool Record(FeedbackCategory category, double latencySeconds)
        {
            if (double.IsNaN(latencySeconds) || latencySeconds < 0.0) latencySeconds = 0.0;

            int i = (int)category;
            _last[i] = latencySeconds;
            if (_samples[i] == 0 || latencySeconds > _max[i]) _max[i] = latencySeconds;
            _samples[i]++;

            bool within = latencySeconds <= LimitOf(category);
            if (!within) _violations[i]++;
            return within;
        }

        public double LastSeconds(FeedbackCategory category) => _last[(int)category];
        public double MaxSeconds(FeedbackCategory category) => _max[(int)category];
        public int SampleCount(FeedbackCategory category) => _samples[(int)category];
        public int ViolationCount(FeedbackCategory category) => _violations[(int)category];

        public void Reset()
        {
            Array.Clear(_last, 0, s_count);
            Array.Clear(_max, 0, s_count);
            Array.Clear(_samples, 0, s_count);
            Array.Clear(_violations, 0, s_count);
        }
    }
}
