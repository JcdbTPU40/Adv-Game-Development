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
        [Header("相性テーブル（#12）")]
        [Tooltip("お守り5種×客タイプの相性テーブル(ScriptableObject)。割り当てると下の客タイプで相性を判定する。未設定なら従来どおり correctOmamori との一致で判定。")]
        [SerializeField] private OmamoriAffinityTable affinityTable;
        [Tooltip("この客のタイプ。affinityTable 設定時に使う。正式には #16 のスポーン側から設定する想定。")]
        [SerializeField] private CustomerType customerType = CustomerType.Kenkou;

        [Header("この客が求めているお守り（正解／フォールバック）")]
        [Tooltip("相性テーブル未設定のときに使う正解お守り。暫定。正式には #16 の客タイプ→正解お守りデータから設定する。")]
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
        /// スポーン時にこの客のタイプと相性テーブルを差し込む用（#16 のスポーン側から呼ぶ想定）。
        /// table を渡すとテーブル判定に切り替わる。
        /// </summary>
        public void Setup(CustomerType type, OmamoriAffinityTable table = null)
        {
            customerType = type;
            if (table != null) affinityTable = table;
        }

        /// <summary>
        /// 客タイプ定義(#16)を丸ごと差し込む版。スポーン側はカタログから引いた
        /// <see cref="CustomerProfile"/> を渡すだけで、客タイプ・正解お守り（フォールバック）が
        /// まとめて設定される。table を渡せば相性テーブル判定に切り替わる。
        /// 見た目(Sprite/Prefab/色)の適用はスポーン側の担当（このコンポーネントは判定のみ）。
        /// </summary>
        public void Setup(CustomerProfile profile, OmamoriAffinityTable table = null)
        {
            if (profile == null) return;
            customerType = profile.CustomerType;
            correctOmamori = profile.CorrectOmamori;
            if (table != null) affinityTable = table;
        }

        /// <summary>
        /// お守りが命中したときに OmamoriBullet から呼ぶ。
        /// 相性判定の結果を返すので、呼び出し側でコンボ/スコア処理に使える。
        /// </summary>
        /// <param name="hitType">当たったお守りの種類</param>
        /// <returns>相性◯/✗の判定結果</returns>
        public Affinity ApplyHit(OmamoriType hitType)
        {
            // 相性テーブル(#12)があれば客タイプで判定。無ければ従来の正解一致で判定。
            Affinity affinity = affinityTable != null
                ? AffinityResolver.Resolve(affinityTable, customerType, hitType)
                : AffinityResolver.Resolve(correctOmamori, hitType);

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
