using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Aim
{
    // 着弾判定の候補（客1人ぶん）
    public readonly struct LandingCandidate
    {
        // 判定の中心（高さは見ない）
        public readonly Vector3 Center;
        // 判定の半径（ワールド単位）
        public readonly float Radius;
        // 当たり判定が生きているか。救済の演出中（結果が決まったあと）は false
        public readonly bool Hittable;

        public LandingCandidate(Vector3 center, float radius, bool hittable)
        {
            Center = center;
            Radius = radius;
            Hittable = hittable;
        }
    }

    /*
        弾が落ちた場所で、だれに当たったかを決めるクラス（#60）

        ・弾はとちゅうの客にさえぎられず、必ず着弾目標点で判定する（予測点と実際に落ちた場所を同じにするため）
        ・当たり判定が消えている客（救済の演出中）は候補から外す。弾は通りぬけて、うしろの客で判定する
        ・判定の円が重なっていたら、中心にいちばん近い客（命中精度がいちばん高くなる客）を選ぶ
    */
    public static class LandingJudge
    {
        /*
            当たった候補の番号を返す。だれにも当たらなかったら -1
            normalizedDistance は、中心からの距離が判定半径の何割か（HitAccuracy に渡す値）
        */
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
