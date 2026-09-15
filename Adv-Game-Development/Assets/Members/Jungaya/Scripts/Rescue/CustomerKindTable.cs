using System;
using UnityEngine;

namespace Toufuku.Rescue
{
    // 客の種類1つぶんの数値（企画書 v8 付録B B-1 の1行）
    [Serializable]
    public class CustomerKindEntry
    {
        [Tooltip("この行が表す客種。")]
        public CustomerKind kind = CustomerKind.Normal;

        [Header("救済までの手数")]
        [Tooltip("初期R（残り必要発数）。通常/移動/遠方=1、欲張り=2、ボス=3。")]
        [Min(1)] public int initialRemaining = 1;

        [Header("危険度Dが100になるまでの秒数")]
        [Tooltip("下限（秒）。上限と同値なら固定値。通常客だけ 15〜25 の幅を持つ。")]
        [Min(0.1f)] public float dangerFullSecondsMin = 15f;
        [Tooltip("上限（秒）。")]
        [Min(0.1f)] public float dangerFullSecondsMax = 25f;

        [Header("得点・評価")]
        [Tooltip("救済完了（R=0）時の基礎点。途中命中では1点も入らない。")]
        public int rescueBaseScore = 100;
        [Tooltip("救済成功時の神社評価の増分。")]
        public int ratingGainOnRescue = 10;
        [Tooltip("黒客化（救済失敗）時の神社評価の減分（正の値で書く）。")]
        public int ratingLossOnBlack = 20;

        // この種類の D が満タンになる秒数を、rng（0〜1）で1つ決める。はばがなければ決まった値
        public float PickDangerFullSeconds(float unitRandom)
        {
            float lo = Mathf.Min(dangerFullSecondsMin, dangerFullSecondsMax);
            float hi = Mathf.Max(dangerFullSecondsMin, dangerFullSecondsMax);
            if (hi - lo <= 0.0001f) return Mathf.Max(0.1f, lo);
            return Mathf.Max(0.1f, Mathf.Lerp(lo, hi, Mathf.Clamp01(unitRandom)));
        }
    }

    /*
        客の種類ごとの、プレイ中に使う数値の表（企画書 v8 付録B B-1 を写したもの）（#54）

        付録B が Phase 1 でただ1つの数値の元なので、コードに数値を直接書かないで
        この ScriptableObject 1つにまとめる。値を変えるときは付録B（またはうつしたあとのシート）と
        ここを必ずいっしょに直す

        使い方:
          Project で右クリック → Create → Toufuku → 客種数値表 (CustomerKindTable)
          シーンのスポナーや客プレハブの CustomerState に入れる
    */
    [CreateAssetMenu(
        fileName = "CustomerKindTable",
        menuName = "Toufuku/客種数値表 (CustomerKindTable)",
        order = 2)]
    public class CustomerKindTable : ScriptableObject
    {
        [Tooltip("付録B B-1 の各行。客種1種につき1行。")]
        [SerializeField] private CustomerKindEntry[] entries =
        {
            new CustomerKindEntry { kind = CustomerKind.Normal,  initialRemaining = 1, dangerFullSecondsMin = 15f, dangerFullSecondsMax = 25f, rescueBaseScore = 100, ratingGainOnRescue = 10, ratingLossOnBlack = 20 },
            new CustomerKindEntry { kind = CustomerKind.Moving,  initialRemaining = 1, dangerFullSecondsMin = 20f, dangerFullSecondsMax = 20f, rescueBaseScore = 150, ratingGainOnRescue = 10, ratingLossOnBlack = 20 },
            new CustomerKindEntry { kind = CustomerKind.Distant, initialRemaining = 1, dangerFullSecondsMin = 22f, dangerFullSecondsMax = 22f, rescueBaseScore = 200, ratingGainOnRescue = 15, ratingLossOnBlack = 30 },
            new CustomerKindEntry { kind = CustomerKind.Greedy,  initialRemaining = 2, dangerFullSecondsMin = 28f, dangerFullSecondsMax = 28f, rescueBaseScore = 300, ratingGainOnRescue = 15, ratingLossOnBlack = 20 },
            new CustomerKindEntry { kind = CustomerKind.Boss,    initialRemaining = 3, dangerFullSecondsMin = 30f, dangerFullSecondsMax = 30f, rescueBaseScore = 500, ratingGainOnRescue = 25, ratingLossOnBlack = 30 },
        };

        // 客の種類の行を取る。登録されていなければ null
        public CustomerKindEntry Get(CustomerKind kind)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].kind == kind) return entries[i];
            }
            return null;
        }

        // 登録してあるぜんぶの行（読むだけ用）
        public CustomerKindEntry[] Entries => entries;

#if UNITY_EDITOR
        // 付録B の行が足りない、または重なっているときに気づけるようにする（エディタだけ）
        private void OnValidate()
        {
            if (entries == null) return;

            foreach (CustomerKind kind in (CustomerKind[])Enum.GetValues(typeof(CustomerKind)))
            {
                int count = 0;
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i] != null && entries[i].kind == kind) count++;
                }

                if (count == 0)
                    Debug.LogWarning($"[CustomerKindTable] {name}: 客種 {kind} の行がありません（付録B B-1）。", this);
                else if (count > 1)
                    Debug.LogWarning($"[CustomerKindTable] {name}: 客種 {kind} の行が {count} 行あります。先頭の行だけが使われます。", this);
            }
        }
#endif
    }
}
