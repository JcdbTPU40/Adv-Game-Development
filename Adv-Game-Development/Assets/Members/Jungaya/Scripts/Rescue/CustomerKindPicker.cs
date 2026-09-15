using System;
using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 客種ごとの出現比率（企画書 v8 10章「出現比率」／付録B SPAWN.TYPE.*）— Issue #62
    ///
    /// 比率は「補充が必要になった瞬間に行う客種の抽選率」。合計が100でなくても、抽選時に残りで再正規化する。
    /// 解禁スケジュールによる比率の切り替え（6月の3段階など）は #57 の担当で、ここは1組の比率を持つだけ。
    /// </summary>
    [Serializable]
    public struct CustomerKindWeights
    {
        [Tooltip("通常客の比率。")]
        [Min(0f)] public float normal;
        [Tooltip("移動客の比率。")]
        [Min(0f)] public float moving;
        [Tooltip("遠方客の比率。")]
        [Min(0f)] public float distant;
        [Tooltip("欲張り客の比率。")]
        [Min(0f)] public float greedy;
        [Tooltip("ボス客の比率（追加要素。MVP は 0）。")]
        [Min(0f)] public float boss;

        /// <summary>11月（付録B SPAWN.TYPE.11）：通常45／移動30／遠方15／欲張り10／ボス0。MVP の4種がすべて出る唯一の月。</summary>
        public static CustomerKindWeights November => new CustomerKindWeights
        {
            normal = 45f, moving = 30f, distant = 15f, greedy = 10f, boss = 0f
        };

        /// <summary>客種の比率を引く。</summary>
        public float Get(CustomerKind kind)
        {
            switch (kind)
            {
                case CustomerKind.Normal:  return normal;
                case CustomerKind.Moving:  return moving;
                case CustomerKind.Distant: return distant;
                case CustomerKind.Greedy:  return greedy;
                case CustomerKind.Boss:    return boss;
                default:                   return 0f;
            }
        }
    }

    /// <summary>
    /// 客種の抽選（企画書 v8 10章）— Issue #62
    ///
    ///   ・同時上限に達した客種（欲張り客2人・ボス客1人）は抽選から外し、残りを100%へ再正規化する。
    ///   ・全部が0（または全部が上限）なら通常客にする。
    ///   ・乱数は呼び出し側が「客ID × 用途」の固定シード列（#63）から 0〜1 を1つ渡す。ここは乱数を持たないので、
    ///     同じ値を渡せば必ず同じ客種になる。
    ///
    /// MonoBehaviour 非依存。境界はエディタテストで検証する（CustomerKindPickerTests）。
    /// </summary>
    public static class CustomerKindPicker
    {
        /// <summary>欲張り客の同時上限（企画書 v8 10章）。</summary>
        public const int GreedyCap = 2;
        /// <summary>ボス客の同時上限（企画書 v8 10章）。</summary>
        public const int BossCap = 1;

        // 抽選の並び。値を並べ替えると同じシードでも結果が変わるので、順番を変えないこと。
        static readonly CustomerKind[] Order =
        {
            CustomerKind.Normal, CustomerKind.Moving, CustomerKind.Distant, CustomerKind.Greedy, CustomerKind.Boss
        };

        /// <param name="weights">客種ごとの比率。</param>
        /// <param name="unitRandom">0〜1 の乱数。</param>
        /// <param name="greedyAlive">境内にいる（終端状態でない）欲張り客の数。</param>
        /// <param name="bossAlive">境内にいる（終端状態でない）ボス客の数。</param>
        public static CustomerKind Pick(CustomerKindWeights weights, float unitRandom, int greedyAlive, int bossAlive,
                                        int greedyCap = GreedyCap, int bossCap = BossCap)
        {
            float total = 0f;
            for (int i = 0; i < Order.Length; i++)
                total += EffectiveWeight(weights, Order[i], greedyAlive, bossAlive, greedyCap, bossCap);

            if (total <= 0f) return CustomerKind.Normal;

            float target = Mathf.Clamp01(unitRandom) * total;
            float accumulated = 0f;
            CustomerKind last = CustomerKind.Normal;
            for (int i = 0; i < Order.Length; i++)
            {
                float w = EffectiveWeight(weights, Order[i], greedyAlive, bossAlive, greedyCap, bossCap);
                if (w <= 0f) continue;
                accumulated += w;
                last = Order[i];
                if (target < accumulated) return Order[i];
            }
            return last;   // unitRandom=1 や丸め誤差で末尾を越えたとき
        }

        /// <summary>上限を反映した比率。上限に達した客種は 0。</summary>
        public static float EffectiveWeight(CustomerKindWeights weights, CustomerKind kind, int greedyAlive, int bossAlive,
                                            int greedyCap = GreedyCap, int bossCap = BossCap)
        {
            if (kind == CustomerKind.Greedy && greedyAlive >= greedyCap) return 0f;
            if (kind == CustomerKind.Boss && bossAlive >= bossCap) return 0f;
            return Mathf.Max(0f, weights.Get(kind));
        }
    }
}
