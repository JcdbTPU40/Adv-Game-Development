using System;
using System.Collections.Generic;

namespace Toufuku.Playtest
{
    /// <summary>優先救済の候補 1 人ぶん。</summary>
    public readonly struct PriorityCandidate
    {
        public readonly int Id;
        /// <summary>危険度 D（0〜100）。</summary>
        public readonly float Danger;
        /// <summary>プレイヤー（照準の基準点）からの距離。</summary>
        public readonly float Distance;

        public PriorityCandidate(int id, float danger, float distance)
        {
            Id = id;
            Danger = danger;
            Distance = distance;
        }
    }

    /// <summary>
    /// 優先対象（二重円の客）を 1 人に決める — Issue #63（#55 の候補集合と同値順）
    ///
    /// ・候補集合は呼び出し側が「画面内の生存客」に絞って渡す。
    /// ・D が最大 → 同値なら近い → 同距離なら ID（スポーン順）が小さい。同時刻に同じ D が複数いても必ず 1 人に定まる。
    /// ・#55 が実装されるまでは、ログの優先対象ID をこの規則と D = 不満ゲージ × 100 で出す（<see cref="PlaytestLog.PriorityTargetProvider"/> で差し替え可）。
    /// </summary>
    public static class PriorityTarget
    {
        public const float DangerEpsilon = 1e-3f;
        public const float DistanceEpsilon = 1e-3f;

        /// <summary>優先対象の ID。候補が無ければ null。</summary>
        public static int? Select(IReadOnlyList<PriorityCandidate> candidates)
        {
            if (candidates == null) return null;

            int best = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (best < 0 || IsBetter(candidates[i], candidates[best])) best = i;
            }
            return best >= 0 ? candidates[best].Id : (int?)null;
        }

        /// <summary>a が b より優先されるか。</summary>
        public static bool IsBetter(PriorityCandidate a, PriorityCandidate b)
        {
            if (Math.Abs(a.Danger - b.Danger) > DangerEpsilon) return a.Danger > b.Danger;
            if (Math.Abs(a.Distance - b.Distance) > DistanceEpsilon) return a.Distance < b.Distance;
            return a.Id < b.Id;
        }
    }
}
