using System.Collections.Generic;

namespace Toufuku.Playtest
{
    // 大きい負荷ウェーブの人数の案1つぶんの、処理の大変さ
    public readonly struct WaveCandidate
    {
        // 基準の上限に足す人数（+3 / +4 / +5）
        public readonly int Added;
        // 同時にいていい人数の合計（基準の10人 + 足した人数）
        public readonly int TotalCap;
        // ずっと同じ状態のときに必要な、1秒あたりの救済の数
        public readonly double RequiredRescuesPerSecond;
        // ずっと同じ状態のときに必要な、1秒あたりの命中の数
        public readonly double RequiredHitsPerSecond;
        // 全部当たって待ち時間もないときに許される1投の間かく（秒）。これより遅いと追いつかない
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

    /*
        8章「大負荷ウェーブ候補の処理負荷比較」の計算（#53 / 企画書 v8 8章・17章 T0-3M）

        計算の手順:
        ・必要な救済/秒 = 同時にいていい人数の合計 ÷ D が 100 になるまでの加重平均の秒（20.94 秒くらい）
        ・必要な命中/秒 = 必要な救済/秒 × 1人救うのに必要な平均の命中数（1.094 発くらい）
        ・許される1投の間かく = 1 ÷ 必要な命中/秒

        20.94 秒と 1.094 発は、T3 のわりあい（ボス OFF・負荷ウェーブ中は 通常 75% / 移動 6.25% / 遠方 9.375% / 欲張り 9.375%）から
        企画書で出した値。付録B のわりあいや秒数が変わったらここも合わせる
        全部当たって待ち時間もない、ずっと同じ状態を考えた計算なので、「できる」と証明するものじゃなくて「無理」と判断するための上限として使う
        MonoBehaviour は使っていない
    */
    public static class WaveLoadArithmetic
    {
        // MVP の基準の同時にいていい人数（人）
        public const int BaseCap = 10;
        // D が 100 になるまでの加重平均の秒
        public const double SecondsToDanger100 = 20.94;
        // 1人救うのに必要な平均の命中数
        public const double HitsPerRescue = 1.094;

        // T3 でくらべる、大きい負荷ウェーブで足す人数
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
