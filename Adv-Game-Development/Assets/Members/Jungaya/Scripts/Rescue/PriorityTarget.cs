using System;
using System.Collections.Generic;

namespace Toufuku.Rescue
{
    // 優先救済の候補1人ぶん
    public readonly struct PriorityCandidate
    {
        // 客の生成ID（出てきた順の通し番号。CustomerSpawnId）
        public readonly int Id;
        // 危険度 D（0〜100）
        public readonly float Danger;
        // プレイヤー（照準の基準点）からの距離
        public readonly float Distance;
        // active になった時刻（秒）。小さいほど早く定位置に着いた客
        public readonly float ActiveSince;

        public PriorityCandidate(int id, float danger, float distance, float activeSince = 0f)
        {
            Id = id;
            Danger = danger;
            Distance = distance;
            ActiveSince = activeSince;
        }
    }

    /*
        優先の相手（二重円の客）を1人に決めるクラス（#55 / 企画書 v8 6章「最危険マーク＝優先救済候補」、7章 得点表）

        ・候補の集まりは、呼ぶ側が「画面の中にいる、active で、まだ救われていない・黒客じゃない・R>0 の客」にしぼって渡す
          （PriorityRescueDirector）。入ってくる途中・帰っている途中・黒客は候補に入れない
        ・同じ値のときの順番は仕様どおり「D がいちばん大きい → 遠いほう → active になった時刻が早いほう → 生成IDが小さいほう」
          最後が生成ID（かぶらない）なので、同じ時刻に同じ D の客が何人いても必ず1人に決まる
        ・「遠いほう」を先にしているのは、同じ危険度なら当てにくいほうを考えてほしいから（v8 7章の選ばせ方の考え）
        ・しきい値はない（付録B PRIORITY.MARK: 相手は1人、D のしきい値なし）。D が 50 より小さくても必ず1人に出るので、
          「D を上げてから救うと得」という、待ったほうが得になることがしくみとして起きない

        MonoBehaviour は使っていない。順番のパターンはぜんぶエディタのテストで確かめる（PriorityTargetTests）
    */
    public static class PriorityTarget
    {
        public const float DangerEpsilon = 1e-3f;
        public const float DistanceEpsilon = 1e-3f;
        public const float TimeEpsilon = 1e-4f;

        // 優先の相手のID。候補がいなければ null
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

        // a が b より優先されるか（D が大きい → 遠い → active になったのが早い → IDが小さい）
        public static bool IsBetter(PriorityCandidate a, PriorityCandidate b)
        {
            if (Math.Abs(a.Danger - b.Danger) > DangerEpsilon) return a.Danger > b.Danger;
            if (Math.Abs(a.Distance - b.Distance) > DistanceEpsilon) return a.Distance > b.Distance;
            if (Math.Abs(a.ActiveSince - b.ActiveSince) > TimeEpsilon) return a.ActiveSince < b.ActiveSince;
            return a.Id < b.Id;
        }
    }
}
