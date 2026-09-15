using System;
using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        客の種類ごとの出てくるわりあい（企画書 v8 10章「出現比率」、付録B SPAWN.TYPE.*）（#62）

        わりあいは「補充が必要になった瞬間に、客の種類を決めるくじのわりあい」。合計が100じゃなくても、くじを引くときに残りで100にしなおす
        解禁スケジュールでわりあいを切りかえる（6月の3段階など）のは #57 の担当で、ここは1組のわりあいを持つだけ
    */
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

        // 11月（付録B SPAWN.TYPE.11）: 通常45、移動30、遠方15、欲張り10、ボス0。MVP の4種類がぜんぶ出るただ1つの月
        public static CustomerKindWeights November => new CustomerKindWeights
        {
            normal = 45f, moving = 30f, distant = 15f, greedy = 10f, boss = 0f
        };

        // 客の種類のわりあいを取る
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

    /*
        客の種類をくじで決めるクラス（企画書 v8 10章）（#62）

          ・同時の上限に届いた種類（欲張り客2人、ボス客1人）はくじから外して、残りで100%にしなおす
          ・ぜんぶ0（またはぜんぶ上限）なら通常客にする
          ・乱数は呼ぶ側が「客ID × 使いみち」の決まったシードの乱数（#63）から 0〜1 を1つ渡す。ここは乱数を持っていないので、
            同じ値を渡せば必ず同じ種類になる

        MonoBehaviour は使っていない。さかい目はエディタのテストで確かめる（CustomerKindPickerTests）
    */
    public static class CustomerKindPicker
    {
        // 欲張り客の同時の上限（企画書 v8 10章）
        public const int GreedyCap = 2;
        // ボス客の同時の上限（企画書 v8 10章）
        public const int BossCap = 1;

        // くじのならび。ならべかえると同じシードでも結果が変わるので、順番を変えないこと
        static readonly CustomerKind[] Order =
        {
            CustomerKind.Normal, CustomerKind.Moving, CustomerKind.Distant, CustomerKind.Greedy, CustomerKind.Boss
        };

        /*
            weights: 客の種類ごとのわりあい
            unitRandom: 0〜1 の乱数
            greedyAlive: 境内にいる（まだ終わっていない）欲張り客の数
            bossAlive: 境内にいる（まだ終わっていない）ボス客の数
        */
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
            return last;   // unitRandom=1 や丸めの誤差で最後をこえたとき
        }

        // 上限を考えたわりあい。上限に届いた種類は 0
        public static float EffectiveWeight(CustomerKindWeights weights, CustomerKind kind, int greedyAlive, int bossAlive,
                                            int greedyCap = GreedyCap, int bossCap = BossCap)
        {
            if (kind == CustomerKind.Greedy && greedyAlive >= greedyCap) return 0f;
            if (kind == CustomerKind.Boss && bossAlive >= bossCap) return 0f;
            return Mathf.Max(0f, weights.Get(kind));
        }
    }
}
