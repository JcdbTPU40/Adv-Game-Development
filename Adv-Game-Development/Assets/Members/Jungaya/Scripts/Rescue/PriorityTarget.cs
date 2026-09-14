using System;
using System.Collections.Generic;

namespace Toufuku.Rescue
{
    /// <summary>優先救済の候補 1 人ぶん。</summary>
    public readonly struct PriorityCandidate
    {
        /// <summary>客の生成ID（スポーン順の通し番号。<c>CustomerSpawnId</c>）。</summary>
        public readonly int Id;
        /// <summary>危険度 D（0〜100）。</summary>
        public readonly float Danger;
        /// <summary>プレイヤー（照準の基準点）からの距離。</summary>
        public readonly float Distance;
        /// <summary>active になった時刻（秒）。小さいほど早く定位置に着いた客。</summary>
        public readonly float ActiveSince;

        public PriorityCandidate(int id, float danger, float distance, float activeSince = 0f)
        {
            Id = id;
            Danger = danger;
            Distance = distance;
            ActiveSince = activeSince;
        }
    }

    /// <summary>
    /// 優先対象（二重円の客）を 1 人に決める — Issue #55（仕様書 v8 6章「最危険マーク＝優先救済候補」／7章 得点表）
    ///
    /// ・候補集合は呼び出し側が「画面内の、active かつ 未救済・非黒客・R&gt;0 の客」に絞って渡す
    ///   （<see cref="PriorityRescueDirector"/>）。入場中・退場中・黒客は候補に入れない。
    /// ・同値順は仕様どおり <b>D が最大 → 遠い方 → active になった時刻が早い方 → 生成IDが小さい方</b>。
    ///   最後が生成ID（重複しない）なので、同時刻に同じ D の客が何人いても必ず 1 人に定まる。
    /// ・「遠い方」を先に取るのは、同じ危険度なら当てにくい方を読ませたいから（v8 7章の選択の設計）。
    /// ・閾値は無い（付録B PRIORITY.MARK：対象数 1 人／D閾値なし）。D&lt;50 でも必ず 1 人に出るので、
    ///   「D を上げてから救うと得」という待ちの利益が構造的に生まれない。
    ///
    /// MonoBehaviour 非依存。順序の全ケースはエディタテストで検証する（PriorityTargetTests）。
    /// </summary>
    public static class PriorityTarget
    {
        public const float DangerEpsilon = 1e-3f;
        public const float DistanceEpsilon = 1e-3f;
        public const float TimeEpsilon = 1e-4f;

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

        /// <summary>a が b より優先されるか（D 最大 → 遠い → active 化が早い → ID 昇順）。</summary>
        public static bool IsBetter(PriorityCandidate a, PriorityCandidate b)
        {
            if (Math.Abs(a.Danger - b.Danger) > DangerEpsilon) return a.Danger > b.Danger;
            if (Math.Abs(a.Distance - b.Distance) > DistanceEpsilon) return a.Distance > b.Distance;
            if (Math.Abs(a.ActiveSince - b.ActiveSince) > TimeEpsilon) return a.ActiveSince < b.ActiveSince;
            return a.Id < b.Id;
        }
    }
}
