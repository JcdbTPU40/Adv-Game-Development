using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 救済判定（相性◯/✗ → ゲージ削り）— Issue #13
    ///
    /// 仕様（企画書 6章）:
    ///   ・相性◯のお守りを当てると大きく削れる（突破）。
    ///   ・相性✗（誤投擲）でも少しだけ削れる ＋ コンボ途切れ（#14）。
    ///
    /// 役割分担（#11 マージ後）:
    ///   不満ゲージの「値・ステート（抵抗/突破/解消）・自然上昇・結末確定・退場」は
    ///   CustomerMood(#11) が一元管理する。ここは “相性判定だけ” に絞り、
    ///   命中時に CustomerMood.ReduceGauge(...) を呼ぶ。
    ///   ※ 同じ GameObject に CustomerMood が必要（RequireComponent）。
    /// </summary>
    [RequireComponent(typeof(CustomerMood))]
    public class CustomerRescue : MonoBehaviour
    {
        [Header("この客が求めているお守り（正解）")]
        [Tooltip("暫定。正式には #16 の客タイプ→正解お守りデータから設定する。")]
        [SerializeField] private OmamoriType correctOmamori = OmamoriType.Kenkou;

        [Header("お守り命中によるゲージ削り量（正の値）")]
        [Tooltip("相性◯（突破）のとき削る量。")]
        [SerializeField] private float goodHitReduce = 40f;
        [Tooltip("相性✗（誤投擲）のとき削る量。少しだけ。")]
        [SerializeField] private float badHitReduce = 5f;

        [Header("判定時イベント（SE/スコア接続用）")]
        public UnityEvent onGoodHit; // 相性◯ヒット（突破）
        public UnityEvent onBadHit;  // 相性✗ヒット（コンボ途切れは #14 でここに接続）

        private CustomerMood _mood;

        /// <summary>ゲージ・ステートは CustomerMood が正本。参照したい場合はこちらから。</summary>
        public CustomerMood Mood => _mood;
        public bool IsResolved => _mood != null && _mood.IsFinished;

        private void Awake()
        {
            _mood = GetComponent<CustomerMood>();
        }

        /// <summary>
        /// お守りが命中したときに OmamoriBullet から呼ぶ。
        /// 相性判定の結果を返すので、呼び出し側でコンボ/スコア処理に使える。
        /// </summary>
        /// <param name="hitType">当たったお守りの種類</param>
        /// <returns>相性◯/✗の判定結果</returns>
        public Affinity ApplyHit(OmamoriType hitType)
        {
            Affinity affinity = AffinityResolver.Resolve(correctOmamori, hitType);

            // 結末確定後はゲージを動かさない（二重判定防止）。
            if (_mood == null || _mood.IsFinished) return affinity;

            if (affinity == Affinity.Good)
            {
                _mood.ReduceGauge(goodHitReduce, isBreakthrough: true); // 突破
                onGoodHit?.Invoke();
            }
            else
            {
                _mood.ReduceGauge(badHitReduce, isBreakthrough: false); // 誤投擲でも少し削れる
                onBadHit?.Invoke();                                     // ← コンボ途切れ（#14）はここに繋ぐ
            }

            return affinity;
        }
    }
}
