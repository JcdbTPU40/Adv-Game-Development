using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 救済判定（減少 / 成功 / 失敗）— Issue #13
    ///
    /// 仕様（企画書 6章）:
    ///   ・客は不満ゲージ（モヤモヤゲージ）を持つ。満ちる＝悪い / 0＝救済。
    ///   ・相性◯のお守りを当てると大きく減る。
    ///   ・相性✗（誤投擲）でも少しだけ減る ＋ コンボ途切れ（#14）。
    ///   ・ゲージは時間で自然に増える（抵抗）。せっかち客は rise 速度が速いだけ。
    ///   ・ゲージ 0   → 救済成功（解消して退場、縁＋・評価＋）。
    ///   ・ゲージ満タン → 失敗（怒って退場、評価−）。
    ///
    /// このコンポーネントを客プレハブに付け、OmamoriBullet 命中時に
    /// ApplyHit(...) を呼ぶことで判定が回ります。
    ///
    /// ※ ゲージ変数・ステートは #11 と重複し得る部分です。#11 がマージされたら
    ///   そちらの実装に寄せて、ここは「判定ロジック」だけに絞ってもOK。
    /// </summary>
    public class CustomerRescue : MonoBehaviour
    {
        public enum RescueState
        {
            Active,  // 救済中（判定対象）
            Rescued, // 救済成功（解消）
            Angry    // 救済失敗（怒り）
        }

        [Header("この客が求めているお守り（正解）")]
        [Tooltip("暫定。正式には #16 の客タイプ→正解お守りデータから設定する。")]
        [SerializeField] private OmamoriType correctOmamori = OmamoriType.Kenkou;

        [Header("不満ゲージ")]
        [Tooltip("ゲージ最大値。これに達すると失敗（怒る）。")]
        [SerializeField] private float maxGauge = 100f;
        [Tooltip("開始時のゲージ量。")]
        [SerializeField] private float startGauge = 50f;
        [Tooltip("1秒あたりの自然上昇量（抵抗）。せっかち客はこれを大きくする。")]
        [SerializeField] private float naturalRiseRate = 5f;

        [Header("お守り命中によるゲージ変化（マイナス＝減少）")]
        [Tooltip("相性◯のとき減らす量。")]
        [SerializeField] private float goodHitReduce = 40f;
        [Tooltip("相性✗（誤投擲）のとき減らす量。少しだけ。")]
        [SerializeField] private float badHitReduce = 5f;

        [Header("判定時イベント（VFX/SE/スコア接続用）")]
        public UnityEvent onRescued;          // 救済成功
        public UnityEvent onAngry;            // 救済失敗
        public UnityEvent onGoodHit;          // 相性◯ヒット
        public UnityEvent onBadHit;           // 相性✗ヒット（コンボ途切れは #14 でここに接続）
        public UnityEvent<float> onGaugeChanged; // 引数: 0〜1 の正規化ゲージ（HUD用）

        private float _gauge;
        private RescueState _state = RescueState.Active;

        public RescueState State => _state;
        public float Gauge => _gauge;
        public float GaugeNormalized => maxGauge > 0f ? _gauge / maxGauge : 0f;
        public bool IsResolved => _state != RescueState.Active;

        private void Awake()
        {
            _gauge = Mathf.Clamp(startGauge, 0f, maxGauge);
        }

        private void Start()
        {
            onGaugeChanged?.Invoke(GaugeNormalized);
        }

        private void Update()
        {
            if (_state != RescueState.Active) return;

            // 抵抗：時間とともに不満が満ちていく
            ModifyGauge(naturalRiseRate * Time.deltaTime);
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

            if (_state != RescueState.Active) return affinity; // 確定後はゲージを動かさない（二重判定防止）

            if (affinity == Affinity.Good)
            {
                ModifyGauge(-goodHitReduce);
                onGoodHit?.Invoke();
            }
            else
            {
                ModifyGauge(-badHitReduce); // 誤投擲でも少しは削れる
                onBadHit?.Invoke();         // ← コンボ途切れ（#14）はここに繋ぐ
            }

            return affinity;
        }

        /// <summary>
        /// ゲージを delta だけ動かし、境界に達したら救済判定を確定する。
        /// </summary>
        private void ModifyGauge(float delta)
        {
            _gauge = Mathf.Clamp(_gauge + delta, 0f, maxGauge);
            onGaugeChanged?.Invoke(GaugeNormalized);

            if (_gauge <= 0f)
            {
                Resolve(RescueState.Rescued);
            }
            else if (_gauge >= maxGauge)
            {
                Resolve(RescueState.Angry);
            }
        }

        private void Resolve(RescueState result)
        {
            if (_state != RescueState.Active) return;
            _state = result;

            if (result == RescueState.Rescued)
            {
                Debug.Log($"[Rescue] 救済成功！ ({name})", this);
                onRescued?.Invoke();
            }
            else // Angry
            {
                Debug.Log($"[Rescue] 救済失敗…怒った ({name})", this);
                onAngry?.Invoke();
            }

            // TODO: 退場アニメ/演出が終わってから Destroy する形に差し替え予定。
            //       いまは検証用に即時破棄。
            Destroy(gameObject);
        }
    }
}
