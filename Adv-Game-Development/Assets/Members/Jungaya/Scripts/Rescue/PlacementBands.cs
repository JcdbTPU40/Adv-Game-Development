using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.Rescue
{
    /*
        近い・中・遠いの距離の帯を割りふるクラス（企画書 v8 8章「空間配置の比率」、付録B PLACEMENT）（#62）

          ・わりあい 40/40/20% は「新しく1人出すごとのくじのわりあい」じゃなくて、定位置にいる黒客じゃない客の「目標のわりあい」
          ・今の上限にわりあいをかけて、最大剰余法で整数の枠にする（Quotas）
            補充するときは、いちばん足りない帯を優先する（Choose）
          ・遠方客は必ず遠い帯に置く。遠い帯に空きがなくて置けないときだけ、次に足りない帯に回す

        MonoBehaviour は使っていない。さかい目はエディタのテストで確かめる（PlacementBandsTests）
    */
    public static class PlacementBands
    {
        /*
            最大剰余法で、上限を帯ごとの整数の枠に分ける。はんぱが同じなら番号が小さい帯（手前）に回す
            capacity: 今の上限の人数
            shares: 帯ごとのわりあい（合計が1や100じゃなくてもいい）
        */
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
                remainders[best] = -1.0;   // 同じ帯に2つ目のはんぱを回さない
            }
            return quotas;
        }

        /*
            補充する帯を選ぶ
              1) requiredBand が決まっていて、そこに空きがあればそこ（遠方客＝遠い帯）
              2) それ以外は、空きがある帯のうち足りないわりあい（(枠−いる人数) ÷ 枠）がいちばん大きい帯。同じなら手前（番号が小さいほう）
            quotas: 帯ごとの目標の枠（Quotas）
            occupied: 帯ごとの、定位置にいる（向かっている）黒客じゃない客の数
            hasFree: 帯ごとに空いている定位置があるか
            requiredBand: 置く帯が決まっている種類の客の帯。なければ -1
            fellBack: 決まった帯に空きがなくて、別の帯に回したら true（ログに残す）
            返す値: 帯の番号。どこにも空きがなければ -1
        */
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

        // 足りないわりあい。枠が0の帯は、いる人数が増えるほどあと回しになるマイナスの値にする
        public static double DeficitRate(int quota, int occupied)
        {
            if (quota <= 0) return -1.0 - occupied;
            return (double)(quota - occupied) / quota;
        }

        /*
            基準点から水平距離 distance のところにあって、左右に lateral ずれた点の、
            奥行きの方向の距離。左右のずれが距離より大きいときは 0
        */
        public static float DepthAtDistance(float distance, float lateral)
        {
            float d2 = distance * distance - lateral * lateral;
            return d2 > 0f ? Mathf.Sqrt(d2) : 0f;
        }

        static int At(IReadOnlyList<int> list, int i) => list != null && i < list.Count ? list[i] : 0;
    }
}
