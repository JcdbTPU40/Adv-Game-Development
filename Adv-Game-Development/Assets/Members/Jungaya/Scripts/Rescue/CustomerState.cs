using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 参拝客の状態（危険度 D ／ 残り必要発数 R）の所有者 — Issue #54（旧 CustomerMood / #11 の不満ゲージを置き換え）
    ///
    /// 仕様（企画書 v8 6章「コアメカニクス ── 救済と輪郭発光」／付録B B-1）:
    ///   ・状態は<b>2変数</b>で管理する。同じゲージとして増減させない（v8変更点1）。
    ///       危険度 D（0〜100） … 残り時間。activeの間だけ時間で増え、100で黒客化。
    ///       残り必要発数 R（1〜3）… 救済までの手数。正色命中で1減り、0で救済完了。
    ///   ・誤色命中は D も R も変えない（v8変更点2）。福の連なり C とご加護進捗 G だけを切る。
    ///   ・笑顔の伝播を受けると D を5減らす。対象は active・未救済・非黒客・R&gt;0 だけ。
    ///   ・rescued / black は終端状態。退場中に再び active へ戻さない。
    ///
    /// 遷移そのものは MonoBehaviour 非依存の <see cref="CustomerStateMachine"/> が持ち、
    /// ここは Unity 側の入れ物（時間を進める／イベントを配る／当たり判定と退場を扱う）に徹する。
    ///
    /// 同時刻の解決順（v8 6章）:
    ///   Update で D を進め、LateUpdate で黒客化を確定する。こうするとフレーム内で着弾した
    ///   正色命中・笑顔の伝播が先に解決され、「間一髪の救済」が一意に優先される。
    ///
    /// 役割分担:
    ///   ・相性◯/✗の判定と正解お守りの保持は <see cref="CustomerRescue"/>。命中時にここの
    ///     <see cref="ApplyCorrectColorHit"/> / <see cref="ApplyWrongColorHit"/> を呼ぶ。
    ///   ・得点（救済完了時の基礎点）は ScoreManager、評価は ShrineRatingHook が各イベントから行う。
    ///   ・数値（初期R・D満タン秒数・基礎点・評価増減）は <see cref="CustomerKindTable"/>（付録B B-1）が正本。
    /// </summary>
    [DisallowMultipleComponent]
    public class CustomerState : MonoBehaviour
    {
        [Header("客種（付録B B-1）")]
        [Tooltip("この客の客種。初期R・D満タン秒数・基礎点・評価増減はこの客種で数値表から引く。")]
        [SerializeField] private CustomerKind customerKind = CustomerKind.Normal;
        [Tooltip("客種ごとの数値表（付録B B-1 の写し）。未設定なら下のフォールバック値を使う。")]
        [SerializeField] private CustomerKindTable kindTable;

        [Header("上書き（モック・検証シーン用。0 なら使わない）")]
        [Tooltip("0より大きいとき、数値表・フォールバックより優先して危険度Dの満タン秒数をこの値にする。" +
                 "視認性モック(#44)のように「並べた状態を保ちたい」検証シーンで使う。本番の客では 0 のままにすること。")]
        [Min(0f)]
        [SerializeField] private float dangerFullSecondsOverride = 0f;

        [Header("フォールバック（数値表 未設定時のみ使う）")]
        [Tooltip("初期R（残り必要発数）。")]
        [Min(1)]
        [SerializeField] private int fallbackInitialRemaining = 1;
        [Tooltip("危険度Dが0→100になるまでの秒数。")]
        [Min(0.1f)]
        [SerializeField] private float fallbackDangerFullSeconds = 20f;
        [Tooltip("救済完了（R=0）時の基礎点。")]
        [SerializeField] private int fallbackRescueBaseScore = 100;

        [Header("開始状態")]
        [Tooltip("ON なら最初から active（定位置に置いた客／テストシーン用）。OFF なら入場中（予告中）で始まり、Arrive() で active になる。")]
        [SerializeField] private bool startActive = true;

        [Header("退場（企画書 v8 6章「救済成功／失敗の演出」）")]
        [Tooltip("救済成功から退場（Destroy）までの秒数。")]
        [SerializeField] private float rescuedExitSeconds = 3f;
        [Tooltip("黒客化から退場（Destroy）までの秒数。")]
        [SerializeField] private float blackExitSeconds = 4f;

        [Header("当たり判定")]
        [Tooltip("救済成功と同時に enabled=false にする Collider。未設定（空 or null）なら子階層から自動収集する。" +
                 "黒客の当たり判定は仕様どおり残す（邪魔になる）。")]
        [SerializeField] private Collider[] hitColliders;

        [Header("イベント（VFX/SE/HUD/スコア接続用）")]
        [Tooltip("危険度Dが変化した（引数: 0〜1 の正規化D。足元円の表示用）。")]
        public UnityEvent<float> onDangerChanged;
        [Tooltip("残り必要発数Rが変化した（引数: 変化後のR。頭上ゲージ用）。")]
        public UnityEvent<int> onRemainingChanged;
        [Tooltip("状態が変化した（引数: 変化後の状態）。")]
        public UnityEvent<CustomerPhase> onPhaseChanged;
        [Tooltip("正色が命中した（Rが1減った瞬間。救済完了かどうかは問わない）。")]
        public UnityEvent onCorrectColorHit;
        [Tooltip("誤色が命中した（DもRも変わらない。渋るリアクション用）。")]
        public UnityEvent onWrongColorHit;
        [Tooltip("救済完了（R=0）。")]
        public UnityEvent onRescued;
        [Tooltip("黒客化（D=100）。")]
        public UnityEvent onBlack;
        [Tooltip("救済完了で光る演出のトリガ。")]
        public UnityEvent onGlow;

        /// <summary>どの客でも終端（救済成功 / 黒客化）が確定したら発火する（#64 失敗SE・#63 計測ログなど、客ごとに結線しない購読者用）。</summary>
        public static event System.Action<CustomerState, CustomerPhase> AnyFinished;

        CustomerStateMachine _machine;
        CustomerKindEntry _entry;

        /// <summary>
        /// 遷移の本体。読み取り専用で使う（値を変えるのはこのコンポーネント経由）。
        /// Awake 前や、エディタ上（Awake が呼ばれない）で触られても落ちないよう、必要なら遅延生成する。
        /// </summary>
        public CustomerStateMachine Machine
        {
            get
            {
                if (_machine == null) BuildMachine(null);
                return _machine;
            }
        }

        /// <summary>この客の客種。</summary>
        public CustomerKind Kind => customerKind;
        /// <summary>現在の状態。</summary>
        public CustomerPhase Phase => Machine.Phase;
        /// <summary>危険度 D（0〜100）。</summary>
        public float Danger => Machine.Danger;
        /// <summary>危険度 D の 0〜1 正規化（HUD用）。</summary>
        public float DangerNormalized => Machine.DangerNormalized;
        /// <summary>残り必要発数 R。</summary>
        public int Remaining => Machine.Remaining;
        /// <summary>救済待ち（active）。</summary>
        public bool IsActive => Machine.IsActive;
        /// <summary>救済成功。</summary>
        public bool IsRescued => Machine.IsRescued;
        /// <summary>黒客（救済失敗）。</summary>
        public bool IsBlack => Machine.IsBlack;
        /// <summary>終端状態（救済成功 or 黒客）。判定対象外。</summary>
        public bool IsFinished => Machine.IsFinished;
        /// <summary>笑顔の伝播・優先救済（二重円）の対象になれるか（active・未救済・非黒客・R&gt;0）。</summary>
        public bool IsRescueTarget => Machine.IsRescueTarget;

        /// <summary>救済完了（R=0）時の基礎点（付録B B-1）。途中命中では入らない。</summary>
        public int RescueBaseScore => _entry != null ? _entry.rescueBaseScore : fallbackRescueBaseScore;
        /// <summary>救済成功時の神社評価の増分（付録B B-1）。</summary>
        public int RatingGainOnRescue => _entry != null ? _entry.ratingGainOnRescue : 10;
        /// <summary>黒客化時の神社評価の減分（正の値。付録B B-1）。</summary>
        public int RatingLossOnBlack => _entry != null ? _entry.ratingLossOnBlack : 20;

        private void Awake()
        {
            BuildMachine(null);
        }

        private void Start()
        {
            onDangerChanged?.Invoke(DangerNormalized);
            onRemainingChanged?.Invoke(Remaining);
            onPhaseChanged?.Invoke(Phase);
        }

        /// <summary>
        /// 客種と数値表を差し込む（スポーン側から生成直後に呼ぶ）。D・R は差し込んだ客種で作り直す。
        /// </summary>
        /// <param name="kind">客種。</param>
        /// <param name="table">数値表（付録B B-1）。null ならこのコンポーネントの設定を使う。</param>
        /// <param name="dangerSecondsRandom">D満タン秒数に幅がある客種（通常客 15〜25秒）で使う 0〜1 の乱数。</param>
        public void Setup(CustomerKind kind, CustomerKindTable table = null, float dangerSecondsRandom = 0.5f)
        {
            customerKind = kind;
            if (table != null) kindTable = table;
            BuildMachine(dangerSecondsRandom);

            onDangerChanged?.Invoke(DangerNormalized);
            onRemainingChanged?.Invoke(Remaining);
            onPhaseChanged?.Invoke(Phase);
        }

        void BuildMachine(float? dangerSecondsRandom)
        {
            _entry = kindTable != null ? kindTable.Get(customerKind) : null;

            int initialRemaining = _entry != null ? _entry.initialRemaining : fallbackInitialRemaining;
            float fullSeconds = dangerFullSecondsOverride > 0f
                ? dangerFullSecondsOverride                                   // 検証シーン用の上書き
                : (_entry != null
                    ? _entry.PickDangerFullSeconds(dangerSecondsRandom ?? 0.5f)
                    : fallbackDangerFullSeconds);

            _machine = new CustomerStateMachine(initialRemaining, fullSeconds, startActive);
            _machine.DangerChanged += _ => onDangerChanged?.Invoke(DangerNormalized);
            _machine.RemainingChanged += r => onRemainingChanged?.Invoke(r);
            _machine.PhaseChanged += phase => onPhaseChanged?.Invoke(phase);
        }

        private void Update()
        {
            // active 中だけ D が進む（入場中・終端状態では進まない）。黒客化は LateUpdate で確定する。
            Machine.TickDanger(Time.deltaTime);
        }

        private void LateUpdate()
        {
            // 同じイベント時刻では、正色着弾と有効な笑顔伝播を先に解決 → R と D を更新 → R=0 の救済判定
            // → なお active で D≥100 なら黒客化（企画書 v8 6章）。フレーム内の着弾はこの時点で解決済み。
            if (Machine.ResolveBlackout())
                Finish(CustomerPhase.Black);
        }

        /// <summary>入場中 → 定位置へ到着（active 開始）。スポーン／歩行側から呼ぶ。</summary>
        public void Arrive() => Machine.Arrive();

        /// <summary>
        /// 現在要求中の正しい色が命中した。R を1減らし D は変えない。R=0 なら救済完了。
        /// <see cref="CustomerRescue"/> から呼ぶ。
        /// </summary>
        public CorrectHitResult ApplyCorrectColorHit()
        {
            CorrectHitResult result = Machine.HitCorrectColor();
            if (result == CorrectHitResult.Ignored) return result;

            onCorrectColorHit?.Invoke();

            if (result == CorrectHitResult.Rescued)
                Finish(CustomerPhase.Rescued);
            else
                Debug.Log($"[State] 正色命中：R {Remaining + 1} → {Remaining}（D は変えない） ({name})", this);

            return result;
        }

        /// <summary>
        /// 違う色が命中した。D も R も変えない。福の連なり C とご加護進捗 G のリセットは呼び出し側で行う。
        /// </summary>
        /// <returns>active で受け付けたら true。</returns>
        public bool ApplyWrongColorHit()
        {
            if (!Machine.HitWrongColor()) return false;
            onWrongColorHit?.Invoke();
            return true;
        }

        /// <summary>
        /// 笑顔の伝播を受けた。対象条件（active・未救済・非黒客・R&gt;0）を満たすときだけ D を5減らす。
        /// </summary>
        /// <returns>伝播が成立したら true（縁+20 を数えてよい）。</returns>
        public bool ReceiveSmilePropagation() => Machine.ReceiveSmile();

        /// <summary>危険度 D を直接設定する（モック・検証シーン用。正規のゲーム進行では使わない）。</summary>
        public void SetDangerForDebug(float danger) => Machine.SetDangerForDebug(danger);

        void Finish(CustomerPhase result)
        {
            if (result == CustomerPhase.Rescued)
            {
                Debug.Log($"[State] 救済完了！ ({name})", this);
                onRescued?.Invoke();
                onGlow?.Invoke();

                // 企画書 v8 6章：成功時は当たり判定を消す（救済済みの客に当て続けて縁を稼ぐ抜け道を塞ぐ）。
                DisableHitDetection();
            }
            else
            {
                Debug.Log($"[State] 黒客化（救済失敗）… ({name})", this);
                onBlack?.Invoke();

                // 企画書 v8 6章：失敗時（黒客）の当たり判定は<b>残す</b>（邪魔になる）。
                // 黒客への通常弾は福の連なりを切る罰として成立させる必要があるため、ここでは消さない。
            }

            AnyFinished?.Invoke(this, result);

            StartCoroutine(ExitThenDespawn(result == CustomerPhase.Rescued ? rescuedExitSeconds : blackExitSeconds));
        }

        /// <summary>救済成功時の当たり判定の無効化。Destroy ではなく enabled=false（退場演出中も見た目は残す）。</summary>
        void DisableHitDetection()
        {
            // Inspector 未設定（空 or null）でも動くよう、子階層から自動収集する（非アクティブ含む）。
            if (hitColliders == null || hitColliders.Length == 0)
                hitColliders = GetComponentsInChildren<Collider>(true);

            // Rigidbody が付いている場合の落下対策：Collider を切る前に isKinematic にして、
            // 退場中に床をすり抜けて落ちるのを防ぐ。
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;

            for (int i = 0; i < hitColliders.Length; i++)
            {
                if (hitColliders[i] != null)
                    hitColliders[i].enabled = false;
            }
        }

        IEnumerator ExitThenDespawn(float seconds)
        {
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);

            // TODO: 参道を歩いて退場するアニメ（成功3秒／黒客4秒）に差し替える。いまは時間だけ待って破棄。
            Destroy(gameObject);
        }
    }
}
