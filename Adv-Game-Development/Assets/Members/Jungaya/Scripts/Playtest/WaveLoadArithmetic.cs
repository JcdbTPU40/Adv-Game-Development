using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /// <summary>大負荷ウェーブの人数案 1 つ分の処理負荷。</summary>
    public readonly struct WaveCandidate
    {
        /// <summary>基準上限への加算人数（+3 / +4 / +5）。</summary>
        public readonly int Added;
        /// <summary>総同時上限（基準 10 人 + 加算）。</summary>
        public readonly int TotalCap;
        /// <summary>定常状態で必要な救済数／秒。</summary>
        public readonly double RequiredRescuesPerSecond;
        /// <summary>定常状態で必要な命中数／秒。</summary>
        public readonly double RequiredHitsPerSecond;
        /// <summary>全命中・待ち時間なしのとき許される 1 投周期（秒）。これより遅いと追いつかない。</summary>
        public readonly double AllowedCycleSeconds;

        public WaveCandidate(int added, int totalCap, double rescuesPerSecond, double hitsPerSecond, double allowedCycleSeconds)
        {
            Added = added;
            TotalCap = totalCap;
            RequiredRescuesPerSecond = rescuesPerSecond;
            RequiredHitsPerSecond = hitsPerSecond;
            AllowedCycleSeconds = allowedCycleSeconds;
        }

        public string Label => $"+{Added}（総上限{TotalCap}人）";
    }

    /// <summary>
    /// 8章「大負荷ウェーブ候補の処理負荷比較」の算術 — Issue #53（仕様書 v8 8章・17章 T0-3M）
    ///
    /// | 手順 | 式 |
    /// |---|---|
    /// | 必要救済/秒 | 総同時上限 ÷ D が 100 になる加重平均秒（約 20.94 秒） |
    /// | 必要命中/秒 | 必要救済/秒 × 救済 1 人に必要な平均命中数（約 1.094 発） |
    /// | 許容される 1 投周期 | 1 ÷ 必要命中/秒 |
    ///
    /// 20.94 秒と 1.094 発は、T3 の比率（ボス OFF・負荷ウェーブ中 通常 75% / 移動 6.25% / 遠方 9.375% / 欲張り 9.375%）から
    /// 仕様書が出した値。付録B の比率・秒数が変わったらここも合わせる。
    /// 全命中・待ち時間なしの定常近似なので、成立の証明ではなく<b>棄却のための上限</b>として使う。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class WaveLoadArithmetic
    {
        /// <summary>MVP の基準同時上限（人）。</summary>
        public const int BaseCap = 10;
        /// <summary>D が 100 になるまでの加重平均秒。</summary>
        public const double SecondsToDanger100 = 20.94;
        /// <summary>救済 1 人に必要な平均命中数。</summary>
        public const double HitsPerRescue = 1.094;

        /// <summary>T3 で比べる大負荷ウェーブの加算人数。</summary>
        public static readonly int[] FinalWaveAdds = { 3, 4, 5 };

        public static WaveCandidate Of(int added)
        {
            int total = BaseCap + added;
            double rescues = total / SecondsToDanger100;
            double hits = rescues * HitsPerRescue;
            double cycle = hits > 0.0 ? 1.0 / hits : double.PositiveInfinity;
            return new WaveCandidate(added, total, rescues, hits, cycle);
        }

        public static List<WaveCandidate> FinalCandidates()
        {
            var list = new List<WaveCandidate>(FinalWaveAdds.Length);
            foreach (int added in FinalWaveAdds) list.Add(Of(added));
            return list;
        }
    }
}
