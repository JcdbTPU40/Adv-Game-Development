using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Toufuku.Aim;
using Toufuku.GameInput;
using Toufuku.Rescue;
using Toufuku.Rescue.Mock;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 計測ログ基盤 — Issue #63（仕様書 v8 17章）
    ///
    /// ・シーンに 1 つ置く。起動時にシードを決めて <see cref="PlaytestRandom"/> を制御し、スポーン・配置を再現できるようにする。
    /// ・<see cref="GameSession"/> のプレイ開始から終了までのイベントを集め、終了した瞬間に CSV を 2 本自動保存する。
    ///   - <c>…_events.csv</c> : 1 イベント 1 行（時刻・種別・対象ID・色・命中精度・得点・倍率・優先対象ID・入力時刻／Unity受信／発射確定）
    ///   - <c>…_summary.csv</c>: T1・T2・T3 の判定に使う数値（<see cref="PlaytestMetrics"/>）
    /// ・ファイル名とヘッダーにテストID・日付・ビルド番号・シード値、ヘッダーにパラメータ（主要コンポーネントの設定値）を入れる。
    /// ・既存の処理は変えず、公開イベントを購読するだけ（発射 = ThrowInputController.SwingAccepted、命中 = OmamoriHitResolver.HitResolved、
    ///   着弾 = OmamoriProjectile.Landed、結末 = CustomerState.AnyFinished、評価 = ShrineRating.onRatingChanged）。
    /// ・シードはほかのスポーン処理より先に決めるため、実行順を最も早くしている。
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class PlaytestLogger : MonoBehaviour
    {
        public static PlaytestLogger Active { get; private set; }

        [Header("計測プレイの識別（ビルドでは起動引数 -playtestTestId など で上書き）")]
        [SerializeField] PlaytestMeta meta = new PlaytestMeta();
        [SerializeField] bool readCommandLine = true;

        [Header("集計（T1 の区間・停止の秒数・T3 の区間）")]
        [SerializeField] PlaytestMetricsSettings metrics = new PlaytestMetricsSettings();

        [Header("保存")]
        [Tooltip("セッション終了時に CSV を自動保存する")]
        [SerializeField] bool saveOnSessionEnd = true;
        [Tooltip("途中で終わったプレイ（Play 停止・アプリ終了）も completed=0 として保存する")]
        [SerializeField] bool saveIncompletePlays = true;
        [Tooltip("パラメータとしてヘッダーへ書き出すコンポーネント。空ならゲーム側の主要コンポーネントを自動で探す")]
        [SerializeField] MonoBehaviour[] parameterSources;

        [Header("優先対象ID（#55 の二重円の規則で決める。OFF にすると列を空にする）")]
        [SerializeField] bool estimatePriorityTarget = true;

        [Header("デバッグ")]
        [SerializeField] bool showHud = true;
        [SerializeField] bool logSaves = true;

        /// <summary>保存した（引数: イベント CSV のパス, 集計 CSV のパス）。</summary>
        public event Action<string, string> Saved;

        static readonly Type[] s_defaultParameterTypes =
        {
            typeof(GameSession), typeof(ScoreManager), typeof(ShrineRating), typeof(GokagoTime),
            typeof(ThrowInputController), typeof(OnusaThrower), typeof(OnusaAimController),
            typeof(MockCrowdDirector), typeof(RescueCustomerSpawner), typeof(Esp32RawSource), typeof(KeyboardMouseRawSource)
        };

        readonly List<PlaytestEvent> _events = new List<PlaytestEvent>();
        readonly List<CustomerSpawnId> _pendingSpawns = new List<CustomerSpawnId>();
        readonly List<KeyValuePair<string, string>> _parameters = new List<KeyValuePair<string, string>>();
        readonly HashSet<string> _capturedCustomerCategories = new HashSet<string>();
        readonly List<(OmamoriProjectile Projectile, int Frame)> _unlinkedProjectiles = new List<(OmamoriProjectile, int)>();

        PlaytestMeta _meta;
        List<string> _commandLineKeys = new List<string>();
        int _seed;
        bool _recording;
        bool _hasPlayed;
        int _playIndex;
        DateTime _startedAt;
        double _startRealtime;
        double _lastT;
        double _lastTRealtime;

        int _throwNo;
        int _lastFireFrame = -1;
        int? _lastFirePriority;
        bool _lastFireLinked = true;

        double _lastSwingT;
        bool _idleOpen;

        PlaytestEvent _pendingHit;
        GameObject _pendingHitCustomer;

        ThrowInputController _input;
        ISwingPeakInputTime _inputTime;
        OnusaAimController _aim;
        ShrineRating _rating;
        GameSession _session;
        GokagoTime[] _gokago = Array.Empty<GokagoTime>();

        public bool IsRecording => _recording;
        public int Seed => _seed;
        public int PlayIndex => _playIndex;
        public PlaytestMeta Meta => _meta;
        public IReadOnlyList<PlaytestEvent> Events => _events;
        public string LastSavedEventsPath { get; private set; }
        public string LastSavedSummaryPath { get; private set; }

        /// <summary>ファイル名・ヘッダーに入れるビルド番号（未設定なら Application.version）。</summary>
        public string BuildLabel => string.IsNullOrWhiteSpace(_meta?.buildNumber) ? Application.version : _meta.buildNumber;

        // ---- ライフサイクル ----

        void Awake()
        {
            if (Active != null && Active != this)
            {
                Debug.LogWarning("[Playtest] PlaytestLogger がシーンに複数あります。2 つ目は無効にします", this);
                enabled = false;
                return;
            }
            Active = this;

            _meta = meta.Clone();
            if (readCommandLine)
                _commandLineKeys = _meta.ApplyCommandLine(Environment.GetCommandLineArgs());

            _seed = _meta.seedMode == PlaytestSeedMode.Fixed ? _meta.seed : PlaytestRandom.NewSeed();
            PlaytestRandom.Control(_seed);
            CustomerSpawnId.ResetSequence();
        }

        void OnEnable()
        {
            if (Active != this) return;
            CustomerSpawnId.Spawned += HandleSpawned;
            CustomerSpawnId.Despawned += HandleDespawned;
            OmamoriHitResolver.HitResolved += HandleHitResolved;
            CustomerState.AnyFinished += HandleCustomerFinished;
            OmamoriProjectile.AnyLaunched += HandleProjectileLaunched;
        }

        void Start()
        {
            if (Active != this) return;
            HookScene();
        }

        void OnDisable()
        {
            if (Active != this) return;
            if (_recording) EndPlay(completed: false);

            CustomerSpawnId.Spawned -= HandleSpawned;
            CustomerSpawnId.Despawned -= HandleDespawned;
            OmamoriHitResolver.HitResolved -= HandleHitResolved;
            CustomerState.AnyFinished -= HandleCustomerFinished;
            OmamoriProjectile.AnyLaunched -= HandleProjectileLaunched;
        }

        void OnDestroy()
        {
            if (Active != this) return;
            UnhookScene();
            Active = null;
            PlaytestRandom.Release();
        }

        void OnApplicationQuit()
        {
            if (Active == this && _recording) EndPlay(completed: false);
        }

        void Update()
        {
            if (Active != this) return;

            GameSession session = GameSession.Instance;
            if (session == null)
            {
                // セッション管理の無いシーンでは起動からアプリ終了までを 1 プレイとして記録する
                if (!_recording && !_hasPlayed) BeginPlay();
            }
            else if (!_recording && session.IsPlaying)
            {
                BeginPlay();
            }
            else if (_recording && !session.IsPlaying)
            {
                EndPlay(completed: session.IsFinished);
            }

            if (_recording)
            {
                _lastT = CurrentT();
                _lastTRealtime = Time.realtimeSinceStartupAsDouble;
                DetectIdle();
            }
        }

        void LateUpdate()
        {
            if (Active != this) return;
            FlushPendingHit();
            FlushPendingSpawns();
        }

        // ---- プレイの開始・終了 ----

        void BeginPlay()
        {
            GameSession session = GameSession.Instance;

            if (_hasPlayed)
            {
                // リトライ: ID を 1 から振り直し、同じシード（RandomEachPlay なら新しいシード）で同じ列を使う
                if (_meta.seedMode == PlaytestSeedMode.RandomEachPlay) _seed = PlaytestRandom.NewSeed();
                PlaytestRandom.Control(_seed);
                CustomerSpawnId.ResetSequence();
                _pendingSpawns.Clear();
            }

            _hasPlayed = true;
            _playIndex++;
            _recording = true;
            _events.Clear();
            _capturedCustomerCategories.Clear();
            _unlinkedProjectiles.Clear();
            _pendingHit = null;
            _pendingHitCustomer = null;
            _throwNo = 0;
            _lastFireFrame = -1;
            _lastFireLinked = true;
            _startedAt = DateTime.Now;
            _startRealtime = Time.realtimeSinceStartupAsDouble;
            _lastT = 0.0;
            _lastTRealtime = _startRealtime;
            _lastSwingT = 0.0;
            _idleOpen = false;

            CaptureParameters();

            PlaytestEvent start = New(PlaytestEventType.SessionStart);
            start.Value = session != null ? session.TotalSeconds : (double?)null;
            start.Detail = "play=" + _playIndex.ToString(CultureInfo.InvariantCulture);
            Add(start);

            if (ShrineRating.Instance != null) AddRatingRow();
            FlushPendingSpawns();
        }

        void EndPlay(bool completed)
        {
            if (!_recording) return;

            FlushPendingHit();
            FlushPendingSpawns();

            double end = CurrentT();
            if (_idleOpen)
            {
                PlaytestEvent idleEnd = New(PlaytestEventType.IdleEnd);
                idleEnd.Value = end - _lastSwingT;
                idleEnd.Detail = "session_end";
                Add(idleEnd);
                _idleOpen = false;
            }

            PlaytestEvent row = New(PlaytestEventType.SessionEnd);
            row.Detail = completed ? "completed" : "aborted";
            if (ScoreManager.Instance != null) row.En = ScoreManager.Instance.En;
            Add(row);

            _recording = false;

            if (completed ? saveOnSessionEnd : saveIncompletePlays)
                Save(completed, end);
        }

        /// <summary>セッション開始からの秒。セッションが無い・破棄済みなら realtime から進める。</summary>
        double CurrentT()
        {
            GameSession session = GameSession.Instance;
            if (session != null) return session.ElapsedSeconds;
            if (!_recording) return _lastT;
            return _lastT + (Time.realtimeSinceStartupAsDouble - _lastTRealtime);
        }

        // ---- 記録 ----

        /// <summary>外部（<see cref="PlaytestLog"/>）からの記録。時刻・フレームはここで入れる。</summary>
        public void Record(PlaytestEvent e)
        {
            if (!_recording || e == null) return;
            e.T = CurrentT();
            e.Realtime = Time.realtimeSinceStartupAsDouble;
            e.Frame = Time.frameCount;
            Add(e);
        }

        PlaytestEvent New(string type)
        {
            return new PlaytestEvent(CurrentT(), type)
            {
                Realtime = Time.realtimeSinceStartupAsDouble,
                Frame = Time.frameCount
            };
        }

        void Add(PlaytestEvent e)
        {
            _events.Add(e);
        }

        // ---- シーンの結線 ----

        void HookScene()
        {
            _input = FindAnyObjectByType<ThrowInputController>();
            if (_input != null)
            {
                _input.SwingAccepted += HandleSwingAccepted;
                _input.SwingRejected += HandleSwingRejected;
                _input.SelectionChanged += HandleSelectionChanged;
                _inputTime = _input.RawSource as ISwingPeakInputTime;
            }
            else
            {
                Debug.LogWarning("[Playtest] ThrowInputController が無いので発射・却下・選択は記録されません", this);
            }

            _aim = FindAnyObjectByType<OnusaAimController>();

            _rating = ShrineRating.Instance;
            if (_rating != null) _rating.onRatingChanged.AddListener(HandleRatingChanged);

            _session = GameSession.Instance;
            if (_session != null) _session.onMonthChanged.AddListener(HandleMonthChanged);

            _gokago = FindObjectsByType<GokagoTime>(FindObjectsSortMode.None);
            foreach (GokagoTime g in _gokago)
            {
                g.onGokagoStart.AddListener(HandleGokagoStart);
                g.onGokagoEnd.AddListener(HandleGokagoEnd);
            }
        }

        void UnhookScene()
        {
            if (_input != null)
            {
                _input.SwingAccepted -= HandleSwingAccepted;
                _input.SwingRejected -= HandleSwingRejected;
                _input.SelectionChanged -= HandleSelectionChanged;
            }
            if (_rating != null) _rating.onRatingChanged.RemoveListener(HandleRatingChanged);
            if (_session != null) _session.onMonthChanged.RemoveListener(HandleMonthChanged);
            foreach (GokagoTime g in _gokago)
            {
                if (g == null) continue;
                g.onGokagoStart.RemoveListener(HandleGokagoStart);
                g.onGokagoEnd.RemoveListener(HandleGokagoEnd);
            }
        }

        // ---- 停止（3 秒以上振らない）----

        /// <summary>振った。停止中なら停止の終わりを記録する。</summary>
        void NoteSwing()
        {
            double t = CurrentT();
            if (_idleOpen)
            {
                PlaytestEvent row = New(PlaytestEventType.IdleEnd);
                row.Value = t - _lastSwingT;
                Add(row);
                _idleOpen = false;
            }
            _lastSwingT = t;
        }

        /// <summary>最後の振りから idleThresholdSeconds 経ったら、その時点で停止の始まりを記録する（value = 最後に振った秒）。</summary>
        void DetectIdle()
        {
            if (_idleOpen || CurrentT() - _lastSwingT < metrics.idleThresholdSeconds) return;
            PlaytestEvent row = New(PlaytestEventType.IdleStart);
            row.Value = _lastSwingT;
            Add(row);
            _idleOpen = true;
        }

        // ---- 入力 ----

        void HandleSwingAccepted(SwingAcceptedArgs e)
        {
            if (!_recording) return;
            NoteSwing();

            double fireTime = Time.realtimeSinceStartupAsDouble;
            int? priority = CurrentPriorityTarget();
            _throwNo++;

            PlaytestEvent row = New(PlaytestEventType.Fire);
            row.ThrowNo = _throwNo;
            row.Omamori = ((OmamoriType)e.ColorIndex).ToString();
            row.PriorityTargetId = priority;
            row.ReceiveTime = e.Time;
            row.FireTime = fireTime;
            row.Strength = e.Strength;
            row.Detail = e.Kind.ToString();
            double input = _inputTime != null ? _inputTime.LastSwingPeakInputTime : double.NaN;
            if (!double.IsNaN(input)) row.InputTime = input;
            Add(row);

            _lastFireFrame = Time.frameCount;
            _lastFirePriority = priority;
            _lastFireLinked = false;

            // OnusaThrower の購読がこちらより先なら、弾はもう飛んでいる
            for (int i = 0; i < _unlinkedProjectiles.Count; i++)
            {
                (OmamoriProjectile projectile, int frame) = _unlinkedProjectiles[i];
                if (projectile == null || frame != Time.frameCount) continue;
                LinkProjectile(projectile, _throwNo, priority);
                _lastFireLinked = true;
                break;
            }
            _unlinkedProjectiles.Clear();
        }

        void HandleSwingRejected(SwingRejectedArgs e)
        {
            if (!_recording) return;
            if (e.Reason != SwingRejectReason.Inactive) NoteSwing();
            PlaytestEvent row = New(PlaytestEventType.SwingRejected);
            row.Detail = e.Reason.ToString();
            row.ReceiveTime = e.Time;
            row.Strength = e.Strength;
            Add(row);
        }

        void HandleSelectionChanged(int color)
        {
            if (!_recording) return;
            PlaytestEvent row = New(PlaytestEventType.Select);
            row.Omamori = ((OmamoriType)color).ToString();
            Add(row);
        }

        // ---- 弾 ----

        void HandleProjectileLaunched(OmamoriProjectile projectile)
        {
            if (!_recording || projectile == null) return;

            if (_lastFireFrame == Time.frameCount && !_lastFireLinked)
            {
                LinkProjectile(projectile, _throwNo, _lastFirePriority);
                _lastFireLinked = true;
            }
            else
            {
                _unlinkedProjectiles.Add((projectile, Time.frameCount));
            }
        }

        void LinkProjectile(OmamoriProjectile projectile, int throwNo, int? priority)
        {
            projectile.Landed += result => HandleLanded(result, throwNo, priority);
        }

        void HandleLanded(LandingResult result, int throwNo, int? priority)
        {
            if (!_recording) return;

            PlaytestEvent row;
            if (result.Hit != null && _pendingHit != null && _pendingHitCustomer == result.Hit.gameObject)
            {
                // 同じ呼び出しの中で判定し終えた命中（得点・倍率入り）を着弾の行にまとめる
                row = _pendingHit;
                row.Type = PlaytestEventType.Landing;
                _pendingHit = null;
                _pendingHitCustomer = null;
            }
            else
            {
                FlushPendingHit();
                row = New(PlaytestEventType.Landing);
                row.Omamori = result.Type.ToString();
                row.Zone = result.Zone.ToString();
                if (result.Hit != null) FillCustomer(row, result.Hit.gameObject);
            }

            row.ThrowNo = throwNo;
            row.PriorityTargetId = priority;
            row.FlightSeconds = result.PlannedSeconds;
            if (result.Hit != null) row.Accuracy = result.NormalizedDistance;
            else
            {
                row.PosX = result.TargetPoint.x;
                row.PosZ = result.TargetPoint.z;
            }
            if (_aim != null)
            {
                Vector3 flat = result.TargetPoint - _aim.Origin;
                flat.y = 0f;
                row.Distance = flat.magnitude;
            }
            if (!result.Scored) row.Detail = "unscored";
            Add(row);
        }

        // ---- 命中・結末 ----

        void HandleHitResolved(OmamoriHitInfo info)
        {
            if (!_recording) return;
            FlushPendingHit();

            PlaytestEvent row = New(PlaytestEventType.Hit);
            FillCustomer(row, info.Customer);
            row.IsBlack = info.IsBlackCustomer;
            if (info.IsBlackCustomer) row.Color = CustomerSpawnId.CategoryBlack;
            row.Omamori = info.Type.ToString();
            row.Zone = info.Zone.ToString();
            row.ColorError = !info.IsGoodHit && !info.IsBlackCustomer;
            row.Rescued = info.Rescued;
            row.FukuChain = info.Combo;

            ScoreManager score = ScoreManager.Instance;
            if (score != null)
            {
                row.Gain = info.IsGoodHit ? score.LastGain : 0;
                row.En = score.En;
                row.Multiplier = score.TotalMultiplier;
            }

            _pendingHit = row;
            _pendingHitCustomer = info.Customer;
        }

        void FlushPendingHit()
        {
            if (_pendingHit == null) return;
            if (_recording) Add(_pendingHit);
            _pendingHit = null;
            _pendingHitCustomer = null;
        }

        void HandleCustomerFinished(CustomerState state, CustomerPhase result)
        {
            if (!_recording || state == null) return;

            PlaytestEvent row = New(result == CustomerPhase.Black
                ? PlaytestEventType.BlackConversion
                : PlaytestEventType.Rescue);
            FillCustomer(row, state.gameObject);
            if (ShrineRating.Instance != null) row.Rating = ShrineRating.Instance.Rating;
            Add(row);
        }

        // ---- 客の出現・退場 ----

        void HandleSpawned(CustomerSpawnId id)
        {
            // 記録前（開始時の一括配置）に出た客は、記録開始時にまとめて書く
            if (_recording || !_hasPlayed) _pendingSpawns.Add(id);
        }

        void FlushPendingSpawns()
        {
            if (!_recording || _pendingSpawns.Count == 0) return;

            foreach (CustomerSpawnId id in _pendingSpawns)
            {
                if (id == null) continue;

                PlaytestEvent row = New(PlaytestEventType.Spawn);
                FillCustomer(row, id.gameObject);
                if (id.Destination.HasValue)
                {
                    // 鳥居から歩いて入る客は、生成位置ではなく立ち位置（配置）を残す
                    row.PosX = id.Destination.Value.x;
                    row.PosZ = id.Destination.Value.z;
                }
                Add(row);
                CaptureCustomerParameters(id);
            }
            _pendingSpawns.Clear();
        }

        void HandleDespawned(CustomerSpawnId id)
        {
            _pendingSpawns.Remove(id);
            if (!_recording || id == null) return;

            PlaytestEvent row = New(PlaytestEventType.Despawn);
            row.TargetId = id.Id;
            row.Category = id.Category;
            Add(row);
        }

        /// <summary>客の ID・分類・色・黒客・位置・危険度を行に入れる。</summary>
        void FillCustomer(PlaytestEvent row, GameObject customer)
        {
            if (customer == null) return;

            CustomerSpawnId id = customer.GetComponent<CustomerSpawnId>();
            row.TargetId = id != null && id.Id > 0 ? id.Id : CustomerSpawnId.Of(customer);
            row.Category = id != null ? id.Category : CustomerSpawnId.CategoryUnregistered;

            bool black = OmamoriHitResolver.IsBlackCustomer(customer);
            row.IsBlack = black;
            row.Color = black ? CustomerSpawnId.CategoryBlack : ColorOf(customer);

            Vector3 pos = customer.transform.position;
            row.PosX = pos.x;
            row.PosZ = pos.z;

            CustomerState state = customer.GetComponent<CustomerState>();
            if (state != null) row.Danger = state.Danger;
        }

        static string ColorOf(GameObject customer)
        {
            MockCustomerTag tag = customer.GetComponent<MockCustomerTag>();
            if (tag != null && tag.ColorIndex >= 0) return ((OmamoriType)tag.ColorIndex).ToString();

            CustomerRescue rescue = customer.GetComponent<CustomerRescue>();
            return rescue != null ? rescue.CorrectOmamori.ToString() : null;
        }

        // ---- 評価・セッション ----

        void HandleRatingChanged(float normalized)
        {
            if (_recording) AddRatingRow();
        }

        void AddRatingRow()
        {
            ShrineRating rating = ShrineRating.Instance;
            if (rating == null) return;
            PlaytestEvent row = New(PlaytestEventType.Rating);
            row.Rating = rating.Rating;
            row.Rank = rating.Rank.ToString();
            Add(row);
        }

        void HandleMonthChanged(int month)
        {
            if (!_recording) return;
            PlaytestEvent row = New(PlaytestEventType.Month);
            row.Value = month;
            Add(row);
        }

        void HandleGokagoStart()
        {
            if (_recording) Add(New(PlaytestEventType.GokagoStart));
        }

        void HandleGokagoEnd()
        {
            if (_recording) Add(New(PlaytestEventType.GokagoEnd));
        }

        // ---- 優先対象ID ----

        /// <summary>
        /// 発射確定の瞬間の優先対象（二重円の客）の ID。
        /// 判定は画面に出ている二重円と同じ <see cref="PriorityRescue"/>（#55）に任せる。
        /// ログと画面と加点が同じ 1 か所を見るので、三者が食い違わない。
        /// </summary>
        int? CurrentPriorityTarget()
        {
            if (PlaytestLog.PriorityTargetProvider != null) return PlaytestLog.PriorityTargetProvider();
            if (!estimatePriorityTarget) return null;
            return PriorityRescue.CurrentTargetId;
        }

        // ---- パラメータ（1 ビルド 1 仮説の記録）----

        void CaptureParameters()
        {
            _parameters.Clear();

            var sources = new List<MonoBehaviour>();
            if (parameterSources != null && parameterSources.Length > 0)
            {
                foreach (MonoBehaviour source in parameterSources)
                {
                    if (source != null) sources.Add(source);
                }
            }
            else
            {
                foreach (Type type in s_defaultParameterTypes)
                {
                    foreach (UnityEngine.Object found in FindObjectsByType(type, FindObjectsSortMode.InstanceID))
                    {
                        if (found is MonoBehaviour behaviour && behaviour.isActiveAndEnabled) sources.Add(behaviour);
                    }
                }
            }

            var seen = new Dictionary<string, int>();
            foreach (MonoBehaviour source in sources)
            {
                string key = "params." + source.GetType().Name;
                seen.TryGetValue(key, out int n);
                seen[key] = n + 1;
                if (n > 0) key += "#" + (n + 1).ToString(CultureInfo.InvariantCulture);
                _parameters.Add(new KeyValuePair<string, string>(key, JsonUtility.ToJson(source)));
            }
        }

        /// <summary>分類ごとに最初の 1 体だけ、客のバランス値（客種・初期R・D満タン秒数・判定半径）を残す。</summary>
        void CaptureCustomerParameters(CustomerSpawnId id)
        {
            if (id == null || !_capturedCustomerCategories.Add(id.Category)) return;

            string prefix = "customer_params." + id.Category + ".";
            foreach (MonoBehaviour behaviour in new MonoBehaviour[]
                     {
                         id.GetComponent<CustomerState>(), id.GetComponent<CustomerRescue>(), id.GetComponent<HitZoneTarget>()
                     })
            {
                if (behaviour != null)
                    _parameters.Add(new KeyValuePair<string, string>(prefix + behaviour.GetType().Name, JsonUtility.ToJson(behaviour)));
            }
        }

        // ---- 保存 ----

        void Save(bool completed, double endTime)
        {
            List<PlaytestSummaryRow> summary = PlaytestMetrics.Compute(_events, metrics, endTime);
            List<KeyValuePair<string, string>> header = BuildHeader(completed, endTime);

            try
            {
                string folder = ResolveOutputFolder();
                string stem = _meta.FileStem(_startedAt, BuildLabel, _seed);
                if (!completed) stem += "_incomplete";

                string unique = stem;
                for (int n = 2; File.Exists(Path.Combine(folder, unique + "_events.csv")); n++)
                    unique = stem + "-" + n.ToString(CultureInfo.InvariantCulture);

                string eventsPath = Path.Combine(folder, unique + "_events.csv");
                string summaryPath = Path.Combine(folder, unique + "_summary.csv");
                PlaytestCsv.WriteFile(eventsPath, PlaytestCsv.BuildEvents(header, _events));
                PlaytestCsv.WriteFile(summaryPath, PlaytestCsv.BuildSummary(header, summary));

                LastSavedEventsPath = eventsPath;
                LastSavedSummaryPath = summaryPath;
                if (logSaves)
                    Debug.Log($"[Playtest] 保存しました（{_events.Count} 行・completed={(completed ? 1 : 0)}）\n{eventsPath}\n{summaryPath}", this);
                Saved?.Invoke(eventsPath, summaryPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Playtest] CSV の保存に失敗しました: {ex.Message}", this);
            }
        }

        List<KeyValuePair<string, string>> BuildHeader(bool completed, double endTime)
        {
            var h = new List<KeyValuePair<string, string>>();
            void Put(string key, string value) => h.Add(new KeyValuePair<string, string>(key, value ?? ""));
            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

            Put("format_version", PlaytestCsv.FormatVersion);
            Put("test_id", _meta.testId);
            Put("date", _startedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Put("started_at", _startedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            Put("ended_at", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
            Put("build_number", BuildLabel);
            Put("app_version", Application.version);
            Put("unity_version", Application.unityVersion);
            Put("platform", Application.platform.ToString());
            Put("is_editor", Application.isEditor ? "1" : "0");
            Put("scene", gameObject.scene.name);
            Put("seed", _seed.ToString(CultureInfo.InvariantCulture));
            Put("seed_mode", _meta.seedMode.ToString());
            Put("play_index", _playIndex.ToString(CultureInfo.InvariantCulture));
            Put("participant_id", _meta.participantId);
            Put("operator", _meta.operatorName);
            Put("hypothesis", _meta.hypothesis);
            Put("note", _meta.note);
            Put("completed", completed ? "1" : "0");
            Put("duration_sec", F(endTime));
            Put("session_total_sec", GameSession.Instance != null ? F(GameSession.Instance.TotalSeconds) : "");
            Put("event_count", _events.Count.ToString(CultureInfo.InvariantCulture));
            Put("command_line", string.Join(" ", _commandLineKeys));
            Put("metrics.segment_sec", F(metrics.segmentSeconds));
            Put("metrics.segment_count", metrics.segmentCount.ToString(CultureInfo.InvariantCulture));
            Put("metrics.idle_threshold_sec", F(metrics.idleThresholdSeconds));
            Put("metrics.t1_window_sec", F(metrics.t1WindowSeconds));
            Put("priority_target_source", PlaytestLog.PriorityTargetProvider != null ? "provider" : (estimatePriorityTarget ? "priority_rescue_55" : "none"));
            h.AddRange(_parameters);
            return h;
        }

        /// <summary>保存先。Editor ではプロジェクト直下の PlaytestLogs/、ビルドでは persistentDataPath/PlaytestLogs/。</summary>
        public string ResolveOutputFolder()
        {
            string root = Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PlaytestLogs"))
                : Path.Combine(Application.persistentDataPath, "PlaytestLogs");

            string custom = _meta != null ? _meta.outputFolder : null;
            if (string.IsNullOrWhiteSpace(custom)) return root;
            return Path.IsPathRooted(custom) ? custom : Path.Combine(root, custom);
        }

        // ---- HUD ----

        void OnGUI()
        {
            if (!showHud || Active != this || _meta == null) return;

            string state = _recording
                ? $"記録中 {CurrentT():0.0}s  {_events.Count} 行  投 {_throwNo}"
                : (_hasPlayed ? "停止（次のプレイ開始で記録）" : "待機");
            string saved = string.IsNullOrEmpty(LastSavedSummaryPath) ? "-" : Path.GetFileName(LastSavedSummaryPath);
            string text =
                $"[計測 #63] {_meta.testId}  seed {_seed}  b{BuildLabel}  {state}\n" +
                $"保存: {saved}";

            const float width = 520f;
            var rect = new Rect(Screen.width - width - 10f, Screen.height - 52f, width, 42f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 3f, rect.width - 12f, rect.height - 6f), text);
        }
    }
}
