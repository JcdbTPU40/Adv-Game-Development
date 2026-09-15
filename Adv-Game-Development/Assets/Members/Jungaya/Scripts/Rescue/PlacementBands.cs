using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 近／中／遠の距離帯の割り当て（企画書 v8 8章「空間配置の比率」／付録B PLACEMENT）— Issue #62
    ///
    ///   ・比率 40／40／20% は「新規1体ごとの抽選率」ではなく、定位置にいる非黒客の<b>目標占有率</b>。
    ///   ・現在上限に比率を掛けて最大剰余法で整数枠へ丸め（<see cref="Quotas"/>）、
    ///     補充時は不足率が最大の帯を優先する（<see cref="Choose"/>）。
    ///   ・遠方客は必ず遠へ置く。遠に空きがなくて置けないときだけ、次に不足する帯へ回す。
    ///
    /// MonoBehaviour 非依存。境界はエディタテストで検証する（PlacementBandsTests）。
    /// </summary>
    public static class PlacementBands
    {
        /// <summary>
        /// 最大剰余法で上限を帯ごとの整数枠へ分ける。端数が同じなら添字の小さい帯（手前）へ回す。
        /// </summary>
        /// <param name="capacity">現在の上限人数。</param>
        /// <param name="shares">帯ごとの比率（合計が1や100でなくてよい）。</param>
        public static int[] Quotas(int capacity, IReadOnlyList<float> shares)
        {
            int n = shares != null ? shares.Count : 0;
            var quotas = new int[n];
            if (capacity <= 0 || n == 0) return quotas;

            float total = 0f;
            for (int i = 0; i < n; i++) total += Mathf.Max(0f, shares[i]);
            if (total <= 0f) return quotas;

            var remainders = new double[n];
            int assigned = 0;
            for (int i = 0; i < n; i++)
            {
                double exact = (double)capacity * Mathf.Max(0f, shares[i]) / total;
                quotas[i] = (int)System.Math.Floor(exact);
                remainders[i] = exact - quotas[i];
                assigned += quotas[i];
            }

            for (int left = capacity - assigned; left > 0; left--)
            {
                int best = 0;
                for (int i = 1; i < n; i++)
                {
                    if (remainders[i] > remainders[best] + 1e-9) best = i;
                }
                quotas[best]++;
                remainders[best] = -1.0;   // 同じ帯に2つ目の端数を回さない
            }
            return quotas;
        }

        /// <summary>
        /// 補充する帯を選ぶ。
        ///   1) <paramref name="requiredBand"/> が指定され、そこに空きがあればそこ（遠方客＝遠）
        ///   2) それ以外は、空きがある帯のうち不足率（(枠−占有) ÷ 枠）が最大の帯。同率なら手前（添字が小さい方）
        /// </summary>
        /// <param name="quotas">帯ごとの目標枠（<see cref="Quotas"/>）。</param>
        /// <param name="occupied">帯ごとの、定位置にいる（向かっている）非黒客の数。</param>
        /// <param name="hasFree">帯ごとに空き定位置があるか。</param>
        /// <param name="requiredBand">置く帯が決まっている客種の帯。無ければ -1。</param>
        /// <param name="fellBack">指定の帯に空きがなく、別の帯へ回したら true（ログに残す）。</param>
        /// <returns>帯の添字。どこにも空きがなければ -1。</returns>
        public static int Choose(IReadOnlyList<int> quotas, IReadOnlyList<int> occupied, IReadOnlyList<bool> hasFree,
                                 int requiredBand, out bool fellBack)
        {
            fellBack = false;
            int n = hasFree != null ? hasFree.Count : 0;

            if (requiredBand >= 0 && requiredBand < n)
            {
                if (hasFree[requiredBand]) return requiredBand;
                fellBack = true;
            }

            int best = -1;
            double bestRate = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                if (!hasFree[i]) continue;
                double rate = DeficitRate(At(quotas, i), At(occupied, i));
                if (best < 0 || rate > bestRate + 1e-9)
                {
                    best = i;
                    bestRate = rate;
                }
            }
            return best;
        }

        /// <summary>不足率。枠が0の帯は、占有が増えるほど後回しになる負の値にする。</summary>
        public static double DeficitRate(int quota, int occupied)
        {
            if (quota <= 0) return -1.0 - occupied;
            return (double)(quota - occupied) / quota;
        }

        /// <summary>
        /// 基準点から水平距離 <paramref name="distance"/> にあり、左右に <paramref name="lateral"/> ずれた点の、
        /// 奥行き方向の距離。左右のずれが距離を超えるときは 0。
        /// </summary>
        public static float DepthAtDistance(float distance, float lateral)
        {
            float d2 = distance * distance - lateral * lateral;
            return d2 > 0f ? Mathf.Sqrt(d2) : 0f;
        }

        static int At(IReadOnlyList<int> list, int i) => list != null && i < list.Count ? list[i] : 0;
    }
}
