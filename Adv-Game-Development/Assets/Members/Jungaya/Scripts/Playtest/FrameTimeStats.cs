using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /*
        フレームの時間から平均の fps と 1% low を出すクラス（#52。T6-USB の30人の負荷）

        ・平均 fps = フレーム数 ÷ 合計の秒（1フレームごとの fps の平均じゃない。遅いフレームをちゃんと重く数えるため）
        ・1% low = 遅い順に上から 1%（切り上げ・最低1フレーム）のフレームの時間の平均を fps にしたもの。#45 の OutlinePerfHud と同じ決め方
        MonoBehaviour は使っていない
    */
    public sealed class FrameTimeStats
    {
        readonly List<double> _ms = new List<double>(4096);

        public int Frames => _ms.Count;
        public double TotalSeconds { get; private set; }

        // 1フレームの時間（秒）を足す。0 以下と NaN は無視する
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

        // 1% にあたるフレーム数（切り上げ・最低1）。小数の丸めで1フレーム増えないように、整数で計算する
        public static int WorstCount(int frames) => frames <= 0 ? 0 : Math.Max(1, (frames + 99) / 100);
    }
}
