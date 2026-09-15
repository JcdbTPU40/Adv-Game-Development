using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /// <summary>
    /// フレーム時間から平均 fps と 1% low を出す — Issue #52（T6-USB の 30 体負荷）
    ///
    /// ・平均 fps = フレーム数 ÷ 合計秒（1 フレームごとの fps の平均ではない。遅いフレームを正しく重く数える）。
    /// ・1% low = 遅い順に上位 1%（切り上げ・最低 1 フレーム）のフレーム時間の平均 → fps。#45 <c>OutlinePerfHud</c> と同じ定義。
    /// MonoBehaviour 非依存。
    /// </summary>
    public sealed class FrameTimeStats
    {
        readonly List<double> _ms = new List<double>(4096);

        public int Frames => _ms.Count;
        public double TotalSeconds { get; private set; }

        /// <summary>1 フレームの時間（秒）を足す。0 以下・NaN は無視する。</summary>
        public void Add(double seconds)
        {
            if (double.IsNaN(seconds) || seconds <= 0.0) return;
            _ms.Add(seconds * 1000.0);
            TotalSeconds += seconds;
        }

        public void Clear()
        {
            _ms.Clear();
            TotalSeconds = 0.0;
        }

        public double? AverageFps => Frames > 0 && TotalSeconds > 0.0 ? Frames / TotalSeconds : (double?)null;

        public double? OnePercentLowFps
        {
            get
            {
                if (Frames == 0) return null;
                var sorted = new List<double>(_ms);
                sorted.Sort();
                int worst = WorstCount(sorted.Count);
                double sum = 0.0;
                for (int i = sorted.Count - worst; i < sorted.Count; i++) sum += sorted[i];
                double avgMs = sum / worst;
                return avgMs > 1e-6 ? 1000.0 / avgMs : (double?)null;
            }
        }

        public double? WorstFrameMs
        {
            get
            {
                if (Frames == 0) return null;
                double max = 0.0;
                foreach (double ms in _ms) max = Math.Max(max, ms);
                return max;
            }
        }

        /// <summary>1% に当たるフレーム数（切り上げ・最低 1）。浮動小数の丸めで 1 フレーム増えないよう整数で計算する。</summary>
        public static int WorstCount(int frames) => frames <= 0 ? 0 : Math.Max(1, (frames + 99) / 100);
    }
}
