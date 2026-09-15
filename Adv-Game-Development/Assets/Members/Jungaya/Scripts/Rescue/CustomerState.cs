using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Toufuku.Rescue
{
    /*
        客の状態（危険度 D と、残りの必要な発数 R）を持っているクラス（#54。前の CustomerMood / #11 の不満ゲージのかわり）

        仕様（企画書 v8 6章「コアメカニクス ── 救済と輪郭発光」、付録B B-1）:
          ・状態は2つの変数で管理する。1本のゲージとして増やしたり減らしたりしない（v8 の変更点1）
              危険度 D（0〜100）: 残り時間。active の間だけ時間で増えて、100 で黒客になる
              残りの必要な発数 R（1〜3）: 救うまでの手数。正しい色で当たると1減って、0 で救えた
          ・まちがった色で当たっても D も R も変えない（v8 の変更点2）。福の連なり C とご加護の進み G だけを切る
          ・笑顔が伝わってきたら D を5減らす。対象は active・まだ救われていない・黒客じゃない・R>0 の客だけ
          ・rescued と black は終わりの状態。帰っている途中にまた active にもどしたりしない

        状態の変わり方そのものは MonoBehaviour を使わない CustomerStateMachine が持っていて、
        ここは Unity 側の入れ物（時間を進める・イベントを配る・当たり判定と帰るのをあつかう）に集中する

        同じ時刻に起きたときの順番（v8 6章）:
          Update で D を進めて、LateUpdate で黒客になるのを決める。こうすると、そのフレームの中で当たった
          正しい色と笑顔の伝わりが先に処理されるので、「ギリギリで救えた」がちゃんと優先される

        役割分担:
          ・相性◯/✗の判定と正解のお守りを持つのは CustomerRescue。当たったときにここの
            ApplyCorrectColorHit / ApplyWrongColorHit を呼ぶ
          ・得点（救えたときの基礎点）は ScoreManager、評価は ShrineRatingHook がそれぞれのイベントから入れる
          ・数値（最初のR・D が満タンになる秒数・基礎点・評価の増減）は CustomerKindTable（付録B B-1）が正しい
    */
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

        /*
            D が満タンになる秒数の上書き（0 なら使わない）。次の Setup から効く
            #57: 検証シーンの客プレハブに残っている上書きを、時間割で動かすときだけ出す側が外すために使う
        */
        public float DangerFullSecondsOverride
        {
            get => dangerFullSecondsOverride;
            set => dangerFullSecondsOverride = Mathf.Max(0f, value);
        }

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

        // どの客でも終わり（救えた / 黒客になった）が決まったら呼ばれる（#64 の失敗の音や #63 の計測ログなど、客ごとにつながない受け取り手用）
        public static event System.Action<CustomerState, CustomerPhase> AnyFinished;

        CustomerStateMachine _machine;
        CustomerKindEntry _entry;

        /*
            状態の変わり方の本体。読むだけで使う（値を変えるのはこのコンポーネントを通す）
            Awake の前や、エディタの上（Awake が呼ばれない）でさわられても落ちないように、必要ならあとから作る
        */
        public CustomerStateMachine Machine
        {
            get
            {
                if (_machine == null) BuildMachine(null);
                return _machine;
            }
        }

        // この客の種類
        public CustomerKind Kind => customerKind;
        // 今の状態
        public CustomerPhase Phase => Machine.Phase;
        // 危険度 D（0〜100）
        public float Danger => Machine.Danger;
        // 危険度 D を 0〜1 に直したもの（HUD 用）
        public float DangerNormalized => Machine.DangerNormalized;
        // 残りの必要な発数 R
        public int Remaining => Machine.Remaining;
        // 救われるのを待っている（active）
        public bool IsActive => Machine.IsActive;
        // 救えた
        public bool IsRescued => Machine.IsRescued;
        // 黒客（救えなかった）
        public bool IsBlack => Machine.IsBlack;
        // 終わりの状態（救えた、または黒客）。判定しない
        public bool IsFinished => Machine.IsFinished;
        // 笑顔の伝わりや優先救済（二重円）の対象になれるか（active・まだ救われていない・黒客じゃない・R>0）
        public bool IsRescueTarget => Machine.IsRescueTarget;
        /*
            active になった時刻（Time.time の秒）。優先救済で同じ値のときの順番「active になったのが早いほう」で使う（#55）
            入ってくる途中（まだ定位置に着いていない）は 0
        */
        public float ActiveSinceTime { get; private set; }

        // 救えた（R=0）ときの基礎点（付録B B-1）。とちゅうで当たったときは入らない
        public int RescueBaseScore => _entry != null ? _entry.rescueBaseScore : fallbackRescueBaseScore;
        // 救えたときに神社の評価が増える量（付録B B-1）
        public int RatingGainOnRescue => _entry != null ? _entry.ratingGainOnRescue : 10;
        // 黒客になったときに神社の評価が減る量（プラスの値。付録B B-1）
        public int RatingLossOnBlack => _entry != null ? _entry.ratingLossOnBlack : 20;
        /*
            終わりの状態から帰る（Destroy）までの秒数（付録B EXIT.TIMING: 救済3秒、黒客4秒）
            歩いて帰る（#62 CustomerMotion）ときはこの秒数で出口まで歩ききる
        */
        public float ExitSecondsFor(CustomerPhase phase) =>
            phase == CustomerPhase.Black ? blackExitSeconds : rescuedExitSeconds;

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

        /*
            客の種類と数値の表を入れる（出す側が作った直後に呼ぶ）。D と R は入れた種類で作りなおす
            kind: 客の種類
            table: 数値の表（付録B B-1）。null ならこのコンポーネントの設定を使う
            dangerSecondsRandom: D が満タンになる秒数にはばがある種類（通常客は 15〜25秒）で使う 0〜1 の乱数
        */
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
                ? dangerFullSecondsOverride                                   // 検証のシーン用に上書きする値
                : (_entry != null
                    ? _entry.PickDangerFullSeconds(dangerSecondsRandom ?? 0.5f)
                    : fallbackDangerFullSeconds);

            _machine = new CustomerStateMachine(initialRemaining, fullSeconds, startActive);
            _machine.DangerChanged += _ => onDangerChanged?.Invoke(DangerNormalized);
            _machine.RemainingChanged += r => onRemainingChanged?.Invoke(r);
            _machine.PhaseChanged += HandlePhaseChanged;

            // 定位置に置いた客（startActive）はこの時点で active。#55 の同じ値のときの順番のために、着いた時刻をメモしておく
            ActiveSinceTime = _machine.IsActive ? Time.time : 0f;
        }

        // active になった時刻をメモしてから、外に向けたイベントを配る
        void HandlePhaseChanged(CustomerPhase phase)
        {
            if (phase == CustomerPhase.Active) ActiveSinceTime = Time.time;
            onPhaseChanged?.Invoke(phase);
        }

        private void Update()
        {
            // #61: 3:00 で危険度の進行を止める（7章「3:00境界の処理順」）。セッションがないシーンではいつも進める
            if (GameSession.Instance != null && !GameSession.Instance.IsPlaying) return;

            // active の間だけ D が進む（入ってくる途中や終わりの状態では進まない）。黒客になるのは LateUpdate で決める
            Machine.TickDanger(Time.deltaTime);
        }

        private void LateUpdate()
        {
            /*
                同じ時刻なら、正しい色の着弾と有効な笑顔の伝わりを先に処理 → R と D を更新 → R=0 なら救えた
                → それでも active で D が 100 以上なら黒客になる（企画書 v8 6章）。フレームの中の着弾はこの時点でもう処理ずみ
            */
            if (Machine.ResolveBlackout())
                Finish(CustomerPhase.Black);
        }

        // 入ってくる途中 → 定位置に着いた（active スタート）。出す側や歩かせる側から呼ぶ
        public void Arrive() => Machine.Arrive();

        /*
            今ほしがっている正しい色が当たった。R を1減らして、D は変えない。R=0 なら救えた
            CustomerRescue から呼ぶ
        */
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

        /*
            ちがう色が当たった。D も R も変えない。福の連なり C とご加護の進み G のリセットは呼ぶ側でやる
            返す値: active で受け付けたら true
        */
        public bool ApplyWrongColorHit()
        {
            if (!Machine.HitWrongColor()) return false;
            onWrongColorHit?.Invoke();
            return true;
        }

        /*
            笑顔が伝わってきた。対象の条件（active・まだ救われていない・黒客じゃない・R>0）を満たすときだけ D を5減らす
            返す値: 伝わったら true（縁+20 を数えていい）
        */
        public bool ReceiveSmilePropagation() => Machine.ReceiveSmile();

        // 危険度 D を直接決める（モックや検証のシーン用。ふつうのゲームの進み方では使わない）
        public void SetDangerForDebug(float danger) => Machine.SetDangerForDebug(danger);

        void Finish(CustomerPhase result)
        {
            if (result == CustomerPhase.Rescued)
            {
                Debug.Log($"[State] 救済完了！ ({name})", this);
                onRescued?.Invoke();
                onGlow?.Invoke();

                // 企画書 v8 6章: 救えたときは当たり判定を消す（救われた客に当てつづけて縁をかせぐずるをできなくする）
                DisableHitDetection();
            }
            else
            {
                Debug.Log($"[State] 黒客化（救済失敗）… ({name})", this);
                onBlack?.Invoke();

                /*
                    企画書 v8 6章: 救えなかったとき（黒客）の当たり判定は残す（じゃまになる）
                    黒客にふつうの弾を当てたら福の連なりが切れる、というペナルティにしたいので、ここでは消さない
                */
            }

            AnyFinished?.Invoke(this, result);

            StartCoroutine(ExitThenDespawn(result == CustomerPhase.Rescued ? rescuedExitSeconds : blackExitSeconds));
        }

        // 救えたときに当たり判定をオフにする。Destroy じゃなくて enabled=false にする（帰る演出の間も見た目は残す）
        void DisableHitDetection()
        {
            // Inspector で入れていない（空や null）ときでも動くように、子どもの階層から自動で集める（非アクティブのものも入れる）
            if (hitColliders == null || hitColliders.Length == 0)
                hitColliders = GetComponentsInChildren<Collider>(true);

            /*
                Rigidbody が付いているときに落ちないようにする: Collider を切る前に isKinematic にして、
                帰っている間に床をすりぬけて落ちるのを防ぐ
            */
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

            // TODO: 参道を歩いて帰るアニメ（成功3秒、黒客4秒）に入れかえる。今は時間だけ待って消している
            Destroy(gameObject);
        }
    }
}
