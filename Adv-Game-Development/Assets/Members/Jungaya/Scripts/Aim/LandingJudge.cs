using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Aim
{
    /// <summary>着弾判定の候補（参拝客 1 人ぶん）。</summary>
    public readonly struct LandingCandidate
    {
        /// <summary>判定の中心（高さは無視する）。</summary>
        public readonly Vector3 Center;
        /// <summary>判定半径（ワールド単位）。</summary>
        public readonly float Radius;
        /// <summary>当たり判定が生きているか。救済演出中（結末確定後）は false。</summary>
        public readonly bool Hittable;

        public LandingCandidate(Vector3 center, float radius, bool hittable)
        {
            Center = center;
            Radius = radius;
            Hittable = hittable;
        }
    }

    /// <summary>
    /// 着弾点で誰に当たったかを決める — Issue #60
    ///
    /// ・弾は途中の客に遮られず、必ず着弾目標点で判定する（予測点と実着弾点を一致させるため）。
    /// ・当たり判定が消えている客（救済演出中）は候補から外す＝弾は通過し、後ろにいる客で判定する。
    /// ・判定円が重なっていれば、中心に最も近い客（命中精度が最も高くなる客）を選ぶ。
    /// </summary>
    public static class LandingJudge
    {
        /// <summary>
        /// 当たった候補の添字を返す。誰にも当たらなければ -1。
        /// normalizedDistance は判定半径に対する中心からの距離の割合（<see cref="HitAccuracy"/> に渡す値）。
        /// </summary>
        public static int FindBest(IReadOnlyList<LandingCandidate> candidates, Vector3 point, out float normalizedDistance)
        {
            int best = -1;
            normalizedDistance = float.PositiveInfinity;
            if (candidates == null) return best;

            for (int i = 0; i < candidates.Count; i++)
            {
                LandingCandidate c = candidates[i];
                if (!c.Hittable) continue;

                float n = HitAccuracy.NormalizedDistance(c.Center, point, c.Radius);
                if (HitAccuracy.ZoneOf(n) == HitZone.Miss) continue;
                if (n < normalizedDistance)
                {
                    normalizedDistance = n;
                    best = i;
                }
            }
            return best;
        }
    }
}
