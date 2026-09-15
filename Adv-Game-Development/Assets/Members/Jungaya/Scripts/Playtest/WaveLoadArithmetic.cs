using System;
using System.Collections.Generic;
using Toufuku.Rescue;
using UnityEngine;

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

    // 客の種類のわりあいから出した、客1人ぶんの平均（8章の 20.94 秒と 1.094 発）（#57）
    public readonly struct KindMix
    {
        // D が 100 になるまでの加重平均の秒
        public readonly double SecondsToDanger100;
        // 1人救うのに必要な平均の命中数
        public readonly double HitsPerRescue;

        public KindMix(double secondsToDanger100, double hitsPerRescue)
        {
            SecondsToDanger100 = secondsToDanger100;
            HitsPerRescue = hitsPerRescue;
        }
    }

    // T0-3M で測った1投の間かくで、その人数の案に追いつけるか（#53 5.1 の判定）
    public enum CycleVerdict
    {
        // p75 の間かくでも追いつく。T3 の候補に残す
        Keep,
        // 中央値なら追いつくけど、p75（遅いほうの4人に1人）は追いつかない。T3 の候補から外す
        DropSlowQuarter,
        // 中央値でも追いつかない。T3 の候補から外す
        DropMedian
    }

    /*
        8章「大負荷ウェーブ候補の処理負荷比較」の計算（#53 / 企画書 v8 8章・17章 T0-3M）

        計算の手順:
        ・必要な救済/秒 = 同時にいていい人数の合計 ÷ D が 100 になるまでの加重平均の秒（20.94 秒くらい）
        ・必要な命中/秒 = 必要な救済/秒 × 1人救うのに必要な平均の命中数（1.094 発くらい）
        ・許される1投の間かく = 1 ÷ 必要な命中/秒

        20.94 秒と 1.094 発は、T3 のわりあい（ボス OFF・負荷ウェーブ中は 通常 75% / 移動 6.25% / 遠方 9.375% / 欲張り 9.375%）から
        企画書で出した値。#57 で、わりあいと客種の数値表から同じ値を出す MixOf を足した（小・中の負荷ウェーブもこれで計算する）
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
            return Of(added, BaseCap + added, new KindMix(SecondsToDanger100, HitsPerRescue));
        }

        // 足す人数・同時にいていい人数の合計・客1人ぶんの平均から計算する
        public static WaveCandidate Of(int added, int totalCap, KindMix mix)
        {
            double rescues = mix.SecondsToDanger100 > 0.0 ? totalCap / mix.SecondsToDanger100 : double.PositiveInfinity;
            double hits = rescues * mix.HitsPerRescue;
            double cycle = hits > 0.0 ? 1.0 / hits : double.PositiveInfinity;
            return new WaveCandidate(added, totalCap, rescues, hits, cycle);
        }

        public static List<WaveCandidate> FinalCandidates()
        {
            var list = new List<WaveCandidate>(FinalWaveAdds.Length);
            foreach (int added in FinalWaveAdds) list.Add(Of(added));
            return list;
        }

        /*
            客の種類のわりあい（合計が100じゃなくてもいい）と数値表（付録B B-1）から、客1人ぶんの平均を出す
              D が 100 になるまでの秒 = Σ わりあい × その種類の満タン秒数（通常客の 15〜25秒 は真ん中の 20秒）
              救うのに必要な命中数   = Σ わりあい × その種類の最初の R
            table が null なら、数値表の初期値（付録B B-1 を写したもの）を使う
        */
        public static KindMix MixOf(CustomerKindWeights weights, CustomerKindTable table)
        {
            double total = weights.Total;
            if (total <= 0.0) return new KindMix(0.0, 0.0);

            CustomerKindTable source = table != null ? table : DefaultTable;
            double seconds = 0.0;
            double hits = 0.0;
            foreach (CustomerKind kind in (CustomerKind[])Enum.GetValues(typeof(CustomerKind)))
            {
                double share = weights.Get(kind) / total;
                if (share <= 0.0) continue;

                CustomerKindEntry entry = source.Get(kind);
                if (entry == null)
                {
                    Debug.LogWarning($"[WaveLoad] 数値表に客種 {kind} の行がないので、算術から外します。");
                    continue;
                }

                seconds += share * (entry.dangerFullSecondsMin + entry.dangerFullSecondsMax) * 0.5;
                hits += share * entry.initialRemaining;
            }
            return new KindMix(seconds, hits);
        }

        /*
            T0-3M で測った1投の間かく（中央値・p75）で判定する（#53 5.1）
            許される間かくが p75 より短い案は T3 の候補から外す
        */
        public static CycleVerdict Judge(WaveCandidate candidate, double medianCycle, double p75Cycle)
        {
            if (candidate.AllowedCycleSeconds >= p75Cycle) return CycleVerdict.Keep;
            if (candidate.AllowedCycleSeconds >= medianCycle) return CycleVerdict.DropSlowQuarter;
            return CycleVerdict.DropMedian;
        }

        /*
            判定の文言
            t3Candidate: T3 でくらべる人数の案（大負荷ウェーブ）なら true。小・中の負荷ウェーブは T3 の候補じゃないので、
                         外すかわりに「加算人数を見直す」と出す
        */
        public static string VerdictLabel(CycleVerdict verdict, bool t3Candidate = true)
        {
            string drop = t3Candidate ? "T3 候補から外す" : "追いつかない → 加算人数を見直す";
            switch (verdict)
            {
                case CycleVerdict.Keep:            return t3Candidate ? "○ 残す（p75 でも追いつく）" : "○ 追いつく（p75 でも）";
                case CycleVerdict.DropSlowQuarter: return $"× {drop}（p75 が許容周期より遅い）";
                default:                           return $"× {drop}（中央値でも追いつかない）";
            }
        }

        static CustomerKindTable s_defaultTable;

        // 数値表が渡されなかったときに使う、初期値だけの表（アセットにはしない）
        static CustomerKindTable DefaultTable
        {
            get
            {
                if (s_defaultTable == null)
                {
                    s_defaultTable = ScriptableObject.CreateInstance<CustomerKindTable>();
                    s_defaultTable.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_defaultTable;
            }
        }
    }
}
