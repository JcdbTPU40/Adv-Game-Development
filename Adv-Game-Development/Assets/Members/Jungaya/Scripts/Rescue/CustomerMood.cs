using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 参拝客の「不満（モヤモヤ）ゲージ」＋ステートの所有者 — Issue #11
    ///
    /// 仕様（企画書 6章「コアメカニクス ── 救済（不満ゲージ）」）:
    ///   ・参拝客は不満ゲージ（モヤモヤゲージ）を持つ。満ちる＝悪い / 0＝救済。全客で統一。
    ///   ・このゲージ1本に救済の三段階が収まっている:
    ///       抵抗(Resisting) … ゲージが満ちようとする（時間で自然上昇）
    ///       突破(Breakingthrough) … 正しいお守りでゲージを削る（命中の瞬間）
    ///       解消(Resolved) … ゲージを0にする（救済成功）
    ///   ・ゲージ満タン → 怒る(Angry)＝救済失敗。
    ///   ・色は赤系、0になると光る演出（onGlow）。
    ///   ・せっかち客は「向き」は同じで naturalRiseRate（満ちる速度）だけが速い。
    ///
    /// 役割分担:
    ///   このコンポーネントが “ゲージ値とステート” を一元管理する（#11 が正本）。
    ///   相性◯/✗の判定や正解お守りの保持は CustomerRescue(#13) 側の担当で、
    ///   命中時に ReduceGauge(...) を呼んでゲージを削るだけ。
    ///   表示は CustomerGaugeHud（テスト用）/ 本番ワールドUI が各イベントを購読する。
    /// </summary>
    public class CustomerMood : MonoBehaviour
    {
        /// <summary>不満ゲージの救済ステート（企画書6章の三段階＋失敗）。</summary>
        public enum MoodState
        {
            Resisting,      // 抵抗：ゲージが満ちようとしている通常状態
            Breakingthrough,// 突破：正しいお守りが命中しゲージを削っている瞬間（一時的）
            Resolved,       // 解消：ゲージ0＝救済成功
            Angry           // 怒り：ゲージ満タン＝救済失敗
        }

        [Header("不満ゲージ")]
        [Tooltip("ゲージ最大値。これに達すると失敗（怒る）。")]
        [SerializeField] private float maxGauge = 100f;
        [Tooltip("開始時のゲージ量。")]
        [SerializeField] private float startGauge = 50f;
        [Tooltip("1秒あたりの自然上昇量（抵抗）。せっかち客はこれだけを大きくする。")]
        [SerializeField] private float naturalRiseRate = 5f;

        [Header("演出タイミング")]
        [Tooltip("突破ステートを表示し続ける時間（秒）。命中後この間だけ Breakingthrough になる。")]
        [SerializeField] private float breakthroughDuration = 0.25f;
        [Tooltip("解消/怒りが確定してから退場（Destroy）するまでの余韻（秒）。0で光る演出を見せる猶予。")]
        [SerializeField] private float resolveLingerTime = 0.6f;

        [Header("結末確定時の当たり判定")]
        [Tooltip("結末確定（解消/怒り）と同時に enabled=false にする Collider。未設定（空 or null）なら子階層から自動収集する。")]
        [SerializeField] private Collider[] hitColliders;

        [Header("イベント（VFX/SE/HUD/スコア接続用）")]
        public UnityEvent<float> onGaugeChanged;   // 引数: 0〜1 の正規化ゲージ（HUD用）
        public UnityEvent<MoodState> onStateChanged;// ステートが変わるたびに発火
        public UnityEvent onBreakthrough;          // 突破（正しいお守りが命中した瞬間）
        public UnityEvent onResolved;              // 解消（救済成功）
        public UnityEvent onAngry;                 // 怒り（救済失敗）
        public UnityEvent onGlow;                  // ゲージ0で光る演出のトリガ

        private float _gauge;
        private MoodState _state = MoodState.Resisting;
        private float _breakthroughTimer;

        public float Gauge => _gauge;
        public float GaugeNormalized => maxGauge > 0f ? _gauge / maxGauge : 0f;
        public MoodState State => _state;

        /// <summary>救済成功＝解消。</summary>
        public bool IsResolved => _state == MoodState.Resolved;
        /// <summary>救済失敗＝怒り。</summary>
        public bool IsAngry => _state == MoodState.Angry;
        /// <summary>結末が確定済み（解消 or 怒り）。判定対象外。</summary>
        public bool IsFinished => _state == MoodState.Resolved || _state == MoodState.Angry;

        private void Awake()
        {
            _gauge = Mathf.Clamp(startGauge, 0f, maxGauge);
        }

        private void Start()
        {
            onGaugeChanged?.Invoke(GaugeNormalized);
            onStateChanged?.Invoke(_state);
        }

        private void Update()
        {
            if (IsFinished) return;

            // 突破ステートは一時的。一定時間で抵抗へ戻す。
            if (_state == MoodState.Breakingthrough)
            {
                _breakthroughTimer -= Time.deltaTime;
                if (_breakthroughTimer <= 0f)
                    SetState(MoodState.Resisting);
            }

            // 抵抗：時間とともに不満が満ちていく。
            ModifyGauge(naturalRiseRate * Time.deltaTime);
        }

        /// <summary>
        /// お守り命中などでゲージを削る。CustomerRescue(#13) から呼ぶ。
        /// </summary>
        /// <param name="amount">削る量（正の値）。0以下は無視。</param>
        /// <param name="isBreakthrough">正しいお守り命中なら true（突破ステート＆突破イベント）。</param>
        public void ReduceGauge(float amount, bool isBreakthrough)
        {
            if (IsFinished || amount <= 0f) return;

            if (isBreakthrough)
            {
                _breakthroughTimer = breakthroughDuration;
                SetState(MoodState.Breakingthrough);
                onBreakthrough?.Invoke();
            }

            ModifyGauge(-amount);
        }

        /// <summary>外部からゲージを増やしたい場合（デバッグ等）。</summary>
        public void AddGauge(float amount)
        {
            if (IsFinished || amount <= 0f) return;
            ModifyGauge(amount);
        }

        /// <summary>
        /// ゲージを delta だけ動かし、境界に達したら結末（解消/怒り）を確定する。
        /// </summary>
        private void ModifyGauge(float delta)
        {
            _gauge = Mathf.Clamp(_gauge + delta, 0f, maxGauge);
            onGaugeChanged?.Invoke(GaugeNormalized);

            if (_gauge <= 0f)
                Finish(MoodState.Resolved);
            else if (_gauge >= maxGauge)
                Finish(MoodState.Angry);
        }

        private void SetState(MoodState next)
        {
            if (_state == next) return;
            _state = next;
            onStateChanged?.Invoke(_state);
        }

        private void Finish(MoodState result)
        {
            if (IsFinished) return;
            SetState(result);

            if (result == MoodState.Resolved)
            {
                Debug.Log($"[Mood] 解消（救済成功）！ ({name})", this);
                onResolved?.Invoke();
                onGlow?.Invoke(); // 0で光る演出
            }
            else // Angry
            {
                Debug.Log($"[Mood] 怒り（救済失敗）… ({name})", this);
                onAngry?.Invoke();
            }

            // 企画書 v3 §16【B】：救済成功後の再ヒットを不可能にするため、
            // 結末確定（解消/怒りのどちらも）と同時に当たり判定を消す。
            // 以後の弾は客をすり抜けて地面に当たり、外し（RegisterMiss）扱いになる。
            DisableHitDetection();

            // 余韻（光る演出/怒り演出）を見せてから退場。
            StartCoroutine(LingerThenDespawn());
        }

        /// <summary>
        /// 当たり判定の無効化。企画書 v3 §16【B】：救済成功後の再ヒットを不可能にするため、
        /// 結末確定と同時に当たり判定を消す（解消済みの客に弾を当て続けて縁を稼ぐ抜け道を塞ぐ）。
        /// Destroy ではなく enabled=false（余韻演出中も見た目は残す）。
        /// </summary>
        private void DisableHitDetection()
        {
            // Inspector 未設定（空 or null）でも動くよう、子階層から自動収集する（非アクティブ含む）。
            if (hitColliders == null || hitColliders.Length == 0)
                hitColliders = GetComponentsInChildren<Collider>(true);

            // Rigidbody が付いている場合の落下対策：Collider を切る前に isKinematic にして、
            // 余韻中に床をすり抜けて落ちるのを防ぐ。
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            for (int i = 0; i < hitColliders.Length; i++)
            {
                if (hitColliders[i] != null)
                    hitColliders[i].enabled = false;
            }
        }

        private IEnumerator LingerThenDespawn()
        {
            if (resolveLingerTime > 0f)
                yield return new WaitForSeconds(resolveLingerTime);

            // TODO: 退場アニメ（解消＝昇天/笑顔、怒り＝走り去る）に差し替え予定。
            //       いまは余韻のあと即破棄。
            Destroy(gameObject);
        }
    }
}
