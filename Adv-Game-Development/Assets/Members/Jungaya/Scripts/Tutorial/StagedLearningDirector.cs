using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Toufuku.Aim;
using Toufuku.GameInput;
using Toufuku.Playtest;
using Toufuku.Rescue;

namespace Toufuku.Tutorial
{
    /*
        開始30秒の段階学習をシーンで動かすクラス（#58 / 企画書 v8 8章「時間割」、18章「開始30秒の段階学習」、17章 T1、11章 ランクC停滞タイマー）

        進み方（段階・早期達成・時間切れ・ゴーストの合図）は StagedLearningPlan が決めて、ここはシーンとつなぐ:
          ・0:00: ScoreManager / ShrineRating を練習中にする（縁・福の連なり・評価・ランクを加算しない）。
                  客を出す側（ILearningCustomerSource）のふつうの補充を止めて、段階ごとの色の客を1人ずつ置く
          ・学習の客の D はスクリプトで固定する（0:00〜0:18 は D=0、教える3の1人だけ D=65。黒客にならない。18章）
          ・同期パルス: 教える1は対象の輪郭とボタンを同時に脈動、教える2は最初の1回だけ
          ・照準反応: 照準が乗った学習の客がこちらを向いて少し大きくなる
          ・ゴースト: 教える1で3秒止まったら大幣ゴーストを1回。見抜くが未達なら 0:30 から5秒の間に、足りない操作のゴーストを1回
          ・誤投擲は「色が違う」の短い表示だけ（罰なし）。二重円の客を救えたら「先に救えた！」と onPriorityRescued
          ・0:30.000（GameSession.CompetitionStarted）: 練習中をやめて、縁=0・評価=0・福の連なり C=0・ご加護・現在/最高ランク=C・
                  ランクC停滞タイマー=0 から始める（ScoreManager.ResetAll / ShrineRating.ResetAll）。学習の客は D の固定を外して D=0 から進める。
                  ふつうの補充を再開する（時間割どおり通常客から）。学習の達成状況にかかわらず、固定時刻に始める
          ・T1 の記録（19章）: 段階の開始・終わり（achieved / timeout）、自由練習の秒、0:30 のカウンタ初期化、ゴースト、
                  0:30 のあとのゴーストの介入、スタッフの介入（staffInterventionKey）を PlaytestLog に残す
                  初救済秒・色誤り・3秒停止は PlaytestLogger がふだんのイベントから集計する

        シーンに1つ置く。GameSession がないシーンでは何もしない
        GameSession（-300）が 0:30 を知らせたときにカウンタを初期化するので、そのフレームの着弾から競技として数える
        客の補充（MockCrowdDirector、0）のあとに動かして（10）、リトライで補充側が客を片付けたあとに学習の客を置く
    */
    [DefaultExecutionOrder(10)]
    [DisallowMultipleComponent]
    public class StagedLearningDirector : MonoBehaviour
    {
        sealed class LearningCustomer
        {
            public GameObject Go;
            public CustomerState State;
            public OmamoriType Color;
            public bool Priority;
            public Quaternion BaseRotation;
            public Vector3 BaseScale;
            public float Reaction;
        }

        [Header("段階学習の数値（企画書 v8 8章 時間割・18章）")]
        [SerializeField] StagedLearningSettings settings = new StagedLearningSettings();

        [Header("参照（未設定ならシーンから探す）")]
        [Tooltip("学習の客を置く側（ILearningCustomerSource を実装したコンポーネント。今は MockCrowdDirector）。")]
        [SerializeField] MonoBehaviour customerSource;
        [Tooltip("ボタンの列・ゴースト・短い表示。未設定なら同じ GameObject に付ける。")]
        [SerializeField] LearningCueView cueView;
        [SerializeField] OnusaAimController aim;
        [SerializeField] ThrowInputController input;

        [Header("客の置き方（帯: 0 近 / 1 中 / 2 遠）")]
        [Tooltip("教える1の客を置く帯。18章「近距離1人」。")]
        [SerializeField, Min(0)] int deliverBand = 0;
        [Tooltip("教える2で増える客を置く帯。")]
        [SerializeField, Min(0)] int chooseBand = 0;
        [Tooltip("教える3で増える客（二重円の客）を置く帯。")]
        [SerializeField, Min(0)] int discernBand = 1;
        [Tooltip("ON なら鳥居から歩いて入る。OFF なら定位置にすぐ置く（8秒・10秒の段階で歩く時間を使わない）。")]
        [SerializeField] bool walkIn;
        [Tooltip("救えた色の客を、次に置くまでの秒数（段階が変わったときと自由練習）。")]
        [SerializeField, Min(0f)] float respawnSeconds = 1.5f;

        [Header("同期パルス（対象の輪郭とボタンを同時に脈動）")]
        [SerializeField, Min(0.1f)] float pulsesPerSecond = 1.25f;
        [Tooltip("教える2で自動の脈動を出す回数（18章「ボタンの自動脈動は最初の1回だけ」）。")]
        [SerializeField, Min(0)] int chooseAutoPulseCount = 1;

        [Header("照準反応（照準が乗った学習の客がこちらを見る）")]
        [SerializeField] bool aimReaction = true;
        [Tooltip("照準が乗っているときの大きさ（倍）。")]
        [SerializeField, Range(1f, 1.3f)] float aimScale = 1.08f;
        [Tooltip("振り向く・もどるのにかける秒数。")]
        [SerializeField, Min(0.01f)] float reactionSeconds = 0.15f;

        [Header("記録（T1）")]
        [Tooltip("スタッフが操作を手伝ったときに押す（T1Intervention \"staff\"）。None で無効。")]
        [SerializeField] KeyCode staffInterventionKey = KeyCode.F10;
        [SerializeField] bool logToConsole = true;

        [Header("イベント")]
        [Tooltip("教える3で二重円の客を救えた（「先に救えた」のあと。笑顔の伝播の強調をここにつなぐ）。")]
        public UnityEvent onPriorityRescued;
        [Tooltip("0:30.000 に競技が始まった（学習の値を捨てたあと）。")]
        public UnityEvent onCompetitionStarted;

        // ボタン箱の LED 用（引数: 色 0〜4, 明るさ 0〜1）。学習中は毎フレーム配る
        public event Action<int, float> ButtonPulseChanged;

        readonly List<LearningCustomer> _customers = new List<LearningCustomer>();
        readonly HashSet<OmamoriType> _rescuedThisStage = new HashSet<OmamoriType>();
        readonly Dictionary<OmamoriType, double> _respawnAt = new Dictionary<OmamoriType, double>();
        readonly float[] _buttonPulse = new float[InputStateMachine.ColorCount];

        StagedLearningPlan _plan;
        GameSession _session;
        ILearningCustomerSource _source;
        bool _running;
        bool _pendingStart;
        int _startRequestFrame;
        bool _warnedSpawnFailure;

        LearningGhost _pendingGhost;
        string _pendingGhostReason;
        double _pendingGhostUntil;

        public StagedLearningPlan Plan => _plan;
        public StagedLearningSettings Settings => settings;
        public LearningStage Stage => _plan != null ? _plan.Stage : LearningStage.NotStarted;
        public bool IsRunning => _running;
        public bool IsLearning => _running && _plan.IsLearning;
        public int LearningCustomerCount => _customers.Count;
        public LearningCueView CueView => cueView;
        public float ButtonPulseOf(int color) => color >= 0 && color < _buttonPulse.Length ? _buttonPulse[color] : 0f;

        // 今いる学習の客（確認用）
        public IEnumerable<GameObject> LearningCustomers
        {
            get
            {
                foreach (LearningCustomer c in _customers)
                {
                    if (c.Go != null) yield return c.Go;
                }
            }
        }

        // 今の学習の客のうち、D を固定した二重円の客（いなければ null）
        public GameObject PriorityCustomer
        {
            get
            {
                foreach (LearningCustomer c in _customers)
                {
                    if (c.Priority && c.Go != null) return c.Go;
                }
                return null;
            }
        }

        void Awake()
        {
            _plan = new StagedLearningPlan(settings);
            _plan.StageStarted += HandleStageStarted;
            _plan.StageEnded += HandleStageEnded;
            _plan.FreePracticeEnded += HandleFreePracticeEnded;
            _plan.CompetitionStarted += HandlePlanCompetitionStarted;
            _plan.GhostRequested += HandleGhostRequested;

            if (cueView == null) cueView = GetComponent<LearningCueView>();
            if (cueView == null) cueView = gameObject.AddComponent<LearningCueView>();
        }

        void OnEnable()
        {
            CustomerState.AnyFinished += HandleCustomerFinished;
            OmamoriHitResolver.HitResolved += HandleHitResolved;
        }

        void OnDisable()
        {
            CustomerState.AnyFinished -= HandleCustomerFinished;
            OmamoriHitResolver.HitResolved -= HandleHitResolved;
            if (_running) StopLearning();
        }

        void Start()
        {
            _session = GameSession.Instance;

            if (customerSource == null) customerSource = FindCustomerSource();
            _source = customerSource as ILearningCustomerSource;
            if (_source == null)
                Debug.LogWarning("[Learning] 学習の客を置く側（ILearningCustomerSource）が見つかりません。客は置かれません。", this);

            if (aim == null) aim = FindAnyObjectByType<OnusaAimController>();
            if (input == null) input = FindAnyObjectByType<ThrowInputController>();
            if (input != null) input.SwingAccepted += HandleSwingAccepted;

            foreach (string problem in settings.Validate())
                Debug.LogWarning($"[Learning] 設定: {problem}", this);

            if (_session == null)
            {
                Debug.LogWarning("[Learning] GameSession がないので段階学習は動きません。", this);
                return;
            }

            _session.CompetitionStarted += HandleSessionCompetitionStarted;
            if (_session.onSessionStart != null) _session.onSessionStart.AddListener(RequestStart);
            if (_session.IsPlaying && !_session.HasCompetitionStarted)
            {
                // シーンの始まりは補充側が前のプレイの客を片付けることがないので、1フレーム待たずにこのフレームの Update から始める
                // （待つと、再生直後のロードの引っかかりで時計だけ進んで、最初の客が 1秒ほど遅れて出る）
                RequestStart();
                _startRequestFrame = Time.frameCount - 1;
            }
        }

        void OnDestroy()
        {
            if (input != null) input.SwingAccepted -= HandleSwingAccepted;
            if (_session != null)
            {
                _session.CompetitionStarted -= HandleSessionCompetitionStarted;
                if (_session.onSessionStart != null) _session.onSessionStart.RemoveListener(RequestStart);
            }
        }

        void Update()
        {
            if (_session == null) return;

            // リトライ（GameSession.Retry）のフレームには始めない。補充側が前のプレイの客を片付けてから、次のフレームで置く
            if (_pendingStart && _session.IsPlaying && Time.frameCount > _startRequestFrame) BeginLearning();
            if (!_running) return;

            if (staffInterventionKey != KeyCode.None && Input.GetKeyDown(staffInterventionKey)) RecordStaffIntervention();

            _plan.Advance(_session.ElapsedTime);
            TryPlayPendingGhost();

            if (!_plan.IsLearning) return;

            SweepCustomers();
            EnsureStageCustomers();
            UpdatePulses();
            UpdateAimReaction();
            if (cueView != null && input != null) cueView.SetSelectedColor(input.SelectedColor);
        }

        // ---------- 始まりと終わり ----------

        void RequestStart()
        {
            _pendingStart = true;
            _startRequestFrame = Time.frameCount;
        }

        void BeginLearning()
        {
            _pendingStart = false;

            ForgetCustomers();
            _rescuedThisStage.Clear();
            _respawnAt.Clear();
            _pendingGhost = LearningGhost.None;
            _warnedSpawnFailure = false;
            if (cueView != null) cueView.StopGhost();

            if (Math.Abs(settings.learningSeconds - _session.LearningSeconds) > 0.0001f)
            {
                Debug.Log($"[Learning] 学習の終わりを GameSession の learningSeconds {_session.LearningSeconds:0.###}秒 に合わせます（設定は {settings.learningSeconds:0.###}秒）。", this);
                settings.learningSeconds = _session.LearningSeconds;
            }

            _running = true;
            SetPractice(true);
            if (_source != null) _source.AutoSpawnSuspended = true;
            if (cueView != null) cueView.SetButtonRowVisible(true);

            Log("段階学習を始めます（競技は 0:30.000 から。学習中は縁・評価を加算しません）");
            _plan.Start(0.0);
            _plan.Advance(_session.ElapsedTime);
        }

        // 段階学習を外す（コンポーネントを無効にしたとき）。練習中をやめて、補充を再開して、D の固定を外す
        void StopLearning()
        {
            _running = false;
            _pendingGhost = LearningGhost.None;
            _plan.Stop();
            SetPractice(false);
            if (_source != null) _source.AutoSpawnSuspended = false;
            ReleaseCustomers();
            if (cueView != null)
            {
                cueView.StopGhost();
                cueView.SetButtonRowVisible(false);
            }
        }

        void HandleSessionCompetitionStarted(double learningSeconds)
        {
            if (!_running) return;
            _plan.Advance(learningSeconds);
            TryPlayPendingGhost();
        }

        // 0:30.000: 学習の値を捨てて、競技を 0 から始める（8章「学習中の値を破棄し、縁=0、評価=0、C=0、G=0、現在／最高ランク=C」）
        void HandlePlanCompetitionStarted(double t)
        {
            SetPractice(false);
            if (ScoreManager.Instance != null) ScoreManager.Instance.ResetAll();   // 縁・福の連なり C・救済数。onReset でご加護もやめる
            if (ShrineRating.Instance != null) ShrineRating.Instance.ResetAll();   // 評価・現在/最高ランク・ランクC停滞タイマー
            PlaytestLog.T1CounterReset(t);

            ReleaseCustomers();
            if (_source != null) _source.AutoSpawnSuspended = false;
            if (cueView != null) cueView.SetButtonRowVisible(false);

            Log($"{t:0.000}秒 競技開始: 学習の値を捨てて、縁・評価・福の連なり・ランク・C停滞タイマーを 0 から数えます" +
                (_plan.IsAchieved(LearningStage.Discern) ? "" : "（見抜くは未達）"));
            onCompetitionStarted?.Invoke();
        }

        void SetPractice(bool practice)
        {
            if (ScoreManager.Instance != null) ScoreManager.Instance.SetPractice(practice);
            if (ShrineRating.Instance != null) ShrineRating.Instance.SetPractice(practice);
        }

        // ---------- 段階 ----------

        void HandleStageStarted(LearningStage stage, double t)
        {
            _rescuedThisStage.Clear();
            ClearPulses();

            if (stage >= LearningStage.Deliver && stage <= LearningStage.Discern)
                PlaytestLog.T1StageStart((int)stage, t);

            if (stage == LearningStage.Discern) MarkPriorityCustomer();

            Log($"{t:0.000}秒 {StageLabel(stage)} 開始（色: {string.Join("・", settings.ColorsFor(stage))}）");
            if (_running) EnsureStageCustomers();
        }

        void HandleStageEnded(LearningStage stage, bool achieved, double t)
        {
            PlaytestLog.T1StageEnd((int)stage, achieved, t);
            Log($"{t:0.000}秒 {StageLabel(stage)} {(achieved ? "早期達成" : "時間切れ")}");
        }

        void HandleFreePracticeEnded(double seconds, double t)
        {
            PlaytestLog.T1FreePractice(seconds, t);
            Log($"自由練習 {seconds:0.000}秒");
        }

        static string StageLabel(LearningStage stage)
        {
            switch (stage)
            {
                case LearningStage.Deliver: return "教える1「届ける」";
                case LearningStage.Choose: return "教える2「選ぶ」";
                case LearningStage.Discern: return "教える3「見抜く」";
                case LearningStage.FreePractice: return "自由練習";
                default: return stage.ToString();
            }
        }

        // ---------- 学習の客 ----------

        // 今の段階の色の客がいなければ置く。同じ段階で救えた色は置きなおさない（自由練習だけは置きなおす）
        void EnsureStageCustomers()
        {
            if (_source == null || !_plan.IsLearning) return;

            LearningStage stage = _plan.Stage;
            double now = _session.ElapsedTime;
            foreach (OmamoriType color in settings.ColorsFor(stage))
            {
                if (HasCustomer(color)) continue;
                if (stage != LearningStage.FreePractice && _rescuedThisStage.Contains(color)) continue;
                if (_respawnAt.TryGetValue(color, out double at) && now < at) continue;

                bool priority = stage == LearningStage.Discern && color == settings.priorityColor;
                Spawn(color, BandFor(color), priority);
            }
        }

        void Spawn(OmamoriType color, int band, bool priority)
        {
            GameObject go = _source.SpawnLearningCustomer(color, band, !walkIn);
            if (go == null)
            {
                if (!_warnedSpawnFailure) Debug.LogWarning($"[Learning] {color} の学習の客を置けませんでした（空いている定位置がない）。次のフレームでもう一回ためします。", this);
                _warnedSpawnFailure = true;
                return;
            }

            var c = new LearningCustomer
            {
                Go = go,
                State = go.GetComponent<CustomerState>(),
                Color = color,
                Priority = priority,
                BaseRotation = go.transform.rotation,
                BaseScale = go.transform.localScale
            };
            if (c.State != null) c.State.LockDanger(priority ? settings.priorityDanger : 0f);
            _customers.Add(c);

            Log($"学習の客を置きました: {color}（帯 {band}{(priority ? $"・D={settings.priorityDanger:0} 固定の二重円" : "")}）ID={CustomerSpawnId.Of(go)}");
        }

        // 教える3が始まったときに、もういる二重円の色の客がいれば、その客を D=65 の二重円にする
        void MarkPriorityCustomer()
        {
            foreach (LearningCustomer c in _customers)
            {
                if (c.Color != settings.priorityColor || c.Go == null || c.State == null || c.State.IsFinished) continue;
                c.Priority = true;
                c.State.LockDanger(settings.priorityDanger);
            }
        }

        int BandFor(OmamoriType color)
        {
            int band = Array.IndexOf(settings.deliverColors ?? Array.Empty<OmamoriType>(), color) >= 0 ? deliverBand
                : Array.IndexOf(settings.chooseColors ?? Array.Empty<OmamoriType>(), color) >= 0 ? chooseBand
                : discernBand;
            int count = _source != null ? _source.BandCount : 0;
            return count > 0 ? Mathf.Clamp(band, 0, count - 1) : band;
        }

        bool HasCustomer(OmamoriType color)
        {
            foreach (LearningCustomer c in _customers)
            {
                if (c.Color == color && c.Go != null) return true;
            }
            return false;
        }

        LearningCustomer Find(GameObject go)
        {
            if (go == null) return null;
            foreach (LearningCustomer c in _customers)
            {
                if (c.Go == go) return c;
            }
            return null;
        }

        // いなくなった客（救われずに消えた）をリストから外す。外した色は次のフレームで置きなおす
        void SweepCustomers()
        {
            for (int i = _customers.Count - 1; i >= 0; i--)
            {
                LearningCustomer c = _customers[i];
                if (c.Go != null && (c.State == null || !c.State.IsFinished)) continue;
                _customers.RemoveAt(i);
            }
        }

        // 学習の客の D の固定を外して D=0 から進める。見た目ももどして、学習の客のリストを空にする（客そのものは残る）
        void ReleaseCustomers()
        {
            foreach (LearningCustomer c in _customers)
            {
                RestoreReaction(c);
                if (c.Go == null) continue;
                if (_source != null) _source.SetLearningPulse(c.Go, 0f);
                if (c.State != null && !c.State.IsFinished) c.State.UnlockDanger(0f);
            }
            _customers.Clear();
            ClearPulses();
        }

        void ForgetCustomers()
        {
            ReleaseCustomers();
        }

        // ---------- できごと ----------

        void HandleSwingAccepted(SwingAcceptedArgs e)
        {
            if (!_running || e.Kind != ThrowKind.Normal) return;
            _plan.NoteSwingAccepted(_session.SessionTimeAt(e.Time));
        }

        void HandleHitResolved(OmamoriHitInfo info)
        {
            if (!_running || !_plan.IsLearning || info.IsBlackCustomer || info.Customer == null) return;

            _plan.NoteCustomerHit(_session.ElapsedTime, info.IsGoodHit);
            if (!info.IsGoodHit && _plan.IsLearning && cueView != null)
                cueView.ShowWrongColor(ScreenOf(info.Customer));
        }

        void HandleCustomerFinished(CustomerState state, CustomerPhase phase)
        {
            if (!_running || state == null) return;
            LearningCustomer c = Find(state.gameObject);
            if (c == null) return;

            double t = _session.ElapsedTime;
            _plan.Advance(t);
            if (!_plan.IsLearning || phase != CustomerPhase.Rescued) return;

            RestoreReaction(c);
            if (_source != null) _source.SetLearningPulse(c.Go, 0f);
            _customers.Remove(c);
            _rescuedThisStage.Add(c.Color);
            _respawnAt[c.Color] = t + respawnSeconds;

            LearningStage before = _plan.Stage;
            _plan.NoteRescue(t, c.Priority);
            Log($"{t:0.000}秒 学習の客を救いました: {c.Color}{(c.Priority ? "（二重円）" : "")}");

            if (c.Priority && before == LearningStage.Discern && _plan.Stage == LearningStage.FreePractice)
            {
                if (cueView != null) cueView.ShowPriorityRescued(ScreenOf(state.gameObject));
                onPriorityRescued?.Invoke();
            }
        }

        void RecordStaffIntervention()
        {
            PlaytestLog.T1Intervention("staff");
            Log($"{_session.ElapsedTime:0.000}秒 スタッフの介入を記録しました");
        }

        // ---------- 同期パルス ----------

        void UpdatePulses()
        {
            LearningStage stage = _plan.Stage;
            double since = Math.Max(0.0, _session.ElapsedTime - _plan.StageStartSeconds);

            OmamoriType[] colors = null;
            if (stage == LearningStage.Deliver)
                colors = settings.deliverColors;
            else if (stage == LearningStage.Choose && since * pulsesPerSecond < chooseAutoPulseCount)
                colors = settings.chooseColors;

            // 0 から明るくなって、1周で 0 にもどる
            float wave = colors != null ? 0.5f - 0.5f * Mathf.Cos((float)(since * pulsesPerSecond * Math.PI * 2.0)) : 0f;

            for (int i = 0; i < _buttonPulse.Length; i++)
                SetButtonPulse(i, colors != null && Array.IndexOf(colors, (OmamoriType)i) >= 0 ? wave : 0f);

            if (_source == null) return;
            foreach (LearningCustomer c in _customers)
            {
                if (c.Go != null)
                    _source.SetLearningPulse(c.Go, colors != null && Array.IndexOf(colors, c.Color) >= 0 ? wave : 0f);
            }
        }

        void SetButtonPulse(int color, float amount)
        {
            _buttonPulse[color] = amount;
            if (cueView != null) cueView.SetButtonPulse(color, amount);
            ButtonPulseChanged?.Invoke(color, amount);
        }

        void ClearPulses()
        {
            for (int i = 0; i < _buttonPulse.Length; i++) SetButtonPulse(i, 0f);
            if (_source == null) return;
            foreach (LearningCustomer c in _customers)
            {
                if (c.Go != null) _source.SetLearningPulse(c.Go, 0f);
            }
        }

        // ---------- 照準反応 ----------

        void UpdateAimReaction()
        {
            LearningCustomer aimed = null;
            if (aimReaction && aim != null && aim.isActiveAndEnabled && aim.HasAim)
            {
                HitZoneTarget hit = OmamoriProjectile.FindTarget(aim.TargetPoint, out _);
                if (hit != null) aimed = Find(hit.gameObject);
            }

            float step = Time.deltaTime / Mathf.Max(0.01f, reactionSeconds);
            foreach (LearningCustomer c in _customers)
            {
                if (c.Go == null) continue;
                float target = c == aimed ? 1f : 0f;
                if (Mathf.Approximately(c.Reaction, target) && target == 0f) continue;

                c.Reaction = Mathf.MoveTowards(c.Reaction, target, step);
                Transform tr = c.Go.transform;
                Vector3 toPlayer = aim != null ? aim.Origin - tr.position : Vector3.zero;
                toPlayer.y = 0f;
                Quaternion look = toPlayer.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPlayer, Vector3.up) : c.BaseRotation;
                tr.rotation = Quaternion.Slerp(c.BaseRotation, look, c.Reaction);
                tr.localScale = c.BaseScale * Mathf.Lerp(1f, aimScale, c.Reaction);
            }
        }

        static void RestoreReaction(LearningCustomer c)
        {
            if (c.Go == null || c.Reaction <= 0f) return;
            c.Reaction = 0f;
            c.Go.transform.rotation = c.BaseRotation;
            c.Go.transform.localScale = c.BaseScale;
        }

        // ---------- ゴースト ----------

        void HandleGhostRequested(LearningGhost ghost, string reason, double t)
        {
            _pendingGhost = ghost;
            _pendingGhostReason = reason;
            _pendingGhostUntil = reason == StagedLearningPlan.ReasonStage1Idle
                ? Math.Min(settings.deliverEndSeconds, settings.learningSeconds)
                : t + settings.afterLearningGhostWindowSeconds;
        }

        // 合図を受けたゴーストを、相手の客が見つかったら1回だけ再生する。出してよい時間をすぎたら出さない
        void TryPlayPendingGhost()
        {
            if (_pendingGhost == LearningGhost.None || cueView == null) return;

            double now = _session.ElapsedTime;
            if (now > _pendingGhostUntil)
            {
                Log($"{now:0.000}秒 ゴースト（{_pendingGhostReason}:{_pendingGhost}）の相手がいないまま時間をすぎたので出しませんでした");
                _pendingGhost = LearningGhost.None;
                return;
            }

            HitZoneTarget target = _pendingGhost == LearningGhost.Priority ? PriorityTargetZone() : NearestRescueTarget();
            if (target == null) return;

            Camera cam = aim != null && aim.ViewCamera != null ? aim.ViewCamera : Camera.main;
            if (cam == null) return;

            Vector2 to = cam.WorldToScreenPoint(target.Center);
            Vector2 from = aim != null && aim.HasAim ? (Vector2)aim.ScreenPosition : new Vector2(Screen.width * 0.5f, Screen.height * 0.25f);
            CustomerRescue rescue = target.GetComponent<CustomerRescue>();
            int color = rescue != null ? (int)rescue.CorrectOmamori : -1;

            LearningGhost ghost = _pendingGhost;
            switch (ghost)
            {
                case LearningGhost.Swing: cueView.PlayGhost(from, to, slide: true, swing: true, pulseColor: -1); break;
                case LearningGhost.Aim: cueView.PlayGhost(from, to, slide: true, swing: false, pulseColor: -1); break;
                case LearningGhost.Color: cueView.PlayGhost(from, to, slide: true, swing: false, pulseColor: color); break;
                case LearningGhost.Priority: cueView.PlayGhost(from, to, slide: true, swing: false, pulseColor: color); break;
            }

            string situation = ghost.ToString().ToLowerInvariant();
            PlaytestLog.T1Ghost($"{_pendingGhostReason}:{situation}", now);
            // 0:30 のあとの状況べつゴーストは介入として残す（18章「未達者のゴースト表示は介入としてログへ残す」）
            if (_pendingGhostReason == StagedLearningPlan.ReasonAfterLearning)
                PlaytestLog.T1Intervention($"{PlaytestLog.GhostInterventionPrefix}:{situation}", now);

            Log($"{now:0.000}秒 ゴースト {_pendingGhostReason}:{situation} → {target.name}");
            _pendingGhost = LearningGhost.None;
        }

        // 照準にいちばん近い、救える対象の客
        HitZoneTarget NearestRescueTarget()
        {
            Vector3 from = aim != null && aim.HasAim ? aim.TargetPoint : (aim != null ? aim.Origin : Vector3.zero);
            HitZoneTarget best = null;
            float bestDistance = float.MaxValue;
            IReadOnlyList<HitZoneTarget> targets = HitZoneTarget.Active;
            for (int i = 0; i < targets.Count; i++)
            {
                HitZoneTarget z = targets[i];
                if (z == null) continue;
                CustomerState state = z.GetComponent<CustomerState>();
                if (state == null || !state.IsRescueTarget) continue;

                float d = (z.Center - from).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = z;
                }
            }
            return best;
        }

        // 今の二重円の客（#55 の PriorityRescue と同じ相手）
        static HitZoneTarget PriorityTargetZone()
        {
            int? id = PriorityRescue.CurrentTargetId;
            if (!id.HasValue) return null;
            IReadOnlyList<HitZoneTarget> targets = HitZoneTarget.Active;
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && CustomerSpawnId.Of(targets[i].gameObject) == id.Value) return targets[i];
            }
            return null;
        }

        // ---------- 部品 ----------

        Vector2 ScreenOf(GameObject customer)
        {
            Camera cam = aim != null && aim.ViewCamera != null ? aim.ViewCamera : Camera.main;
            if (cam == null || customer == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            HitZoneTarget zone = customer.GetComponent<HitZoneTarget>();
            return cam.WorldToScreenPoint(zone != null ? zone.Center : customer.transform.position);
        }

        static MonoBehaviour FindCustomerSource()
        {
            foreach (MonoBehaviour b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.InstanceID))
            {
                if (b is ILearningCustomerSource && b.isActiveAndEnabled) return b;
            }
            return null;
        }

        void Log(string message)
        {
            if (logToConsole) Debug.Log("[Learning] " + message, this);
        }
    }
}
