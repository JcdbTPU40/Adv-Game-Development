using System;

namespace Toufuku.Feedback
{
    // 1投のフィードバックの時間の目標（予算）の区分
    public enum FeedbackCategory
    {
        Swing = 0,  // 振りのピーク → 投げる音
        Hit = 1,    // 落ちる予定の時刻 → 命中音
        Rescue = 2  // 落ちる予定の時刻 → 救済音
    }

    /*
        1投のフィードバックの遅れを測るクラス（#64 / 企画書 v8 1章）

        目標の時間はこうなっている:
        ・Swing: 振りピークのデータを受け取った時刻（SwingAccepted.Time）から投げる音を鳴らすまで 80ms
        ・Hit: 弾が落ちる予定の時刻（発射時刻＋飛ぶ時間）から命中音を鳴らすまで 50ms
        ・Rescue: 同じく落ちる予定の時刻から救済音を鳴らすまで 250ms

        オーディオの出力バッファのぶんの遅れは入れていない（HUD に別で出す）。MonoBehaviour は使っていない
    */
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

        // 遅れを記録する。目標内なら true。マイナスの遅れ（時計の丸め）は 0 としてあつかう
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
