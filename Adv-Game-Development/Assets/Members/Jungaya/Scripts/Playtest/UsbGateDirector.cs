using System;
using System.Collections.Generic;
using UnityEngine;
using Toufuku.Aim;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /// <summary>T6-USB の進行フェーズ。</summary>
    public enum UsbGatePhase
    {
        /// <summary>区間を選ぶ。</summary>
        Menu,
        /// <summary>安全チェックの記入。</summary>
        Safety,
        /// <summary>区間の説明（Space で開始）。</summary>
        Prepare,
        /// <summary>計測中。</summary>
        Active,
        /// <summary>飛翔中の弾の着弾待ち。</summary>
        Settle,
        /// <summary>ドリフトの終わりの標本（置き台に戻して Space）。</summary>
        CaptureEnd,
        /// <summary>区間の結果（Space でメニューへ）。</summary>
        Result
    }

    /// <summary>
    /// T6-USB 必須技術ゲートの進行と記録 — Issue #52（仕様書 v8 17章）
    ///
    /// 区間（メニューから選ぶ。F5〜F10）:
    /// | 区間 | 何をするか | 記録 |
    /// |---|---|---|
    /// | 安全チェック | 12章の安全領域・配線・ストラップ・接合部を全項目確かめる。<b>全項目適合するまでほかの区間は始められない</b> | 項目ごとの適合 |
    /// | 意図的 100 投 | 2 秒ごとの合図（画面＋音）に合わせて 1 回ずつ振る | 入力時刻／Unity 受信／発射確定／画面に弾、合図、却下 |
    /// | 3 分静止ドリフト | 置き台に置いたまま 3 分 | 置き台での照準の画面 X（始め・終わり・10 秒ごと） |
    /// | 3 分操作ドリフト | 置き台 → 手に持って 3 分振る → 置き台へ戻す | 同上（始め・終わり） |
    /// | 30 体負荷 | 黒客・退場者込み 30 体を描画しながら 5 秒慣らし＋60 秒計測（1 秒ごとに自動投擲） | 平均 fps・1% low・描画体数 |
    /// | 子ども 近・遠 | 2 分練習 → 近くの的へ 10 投 → 遠くの的へ 10 投 | 発射・着弾の距離と命中 |
    ///
    /// ・実機を使う区間では受信の途絶（切断）・受信頻度・フレーム時間をいつも測る。群衆は区間ごとに出す／出さないを選べる。
    /// ・クールダウンは T0-CD の採用値、フィードバックは T0-A/B の採用案（同じ GameObject の FeedbackTimingShifter）で固定する。
    /// ・区間が終わるたびに記録 CSV へ追記し、判定 CSV を書き直す（<see cref="UsbGateCsvFile"/>）。
    ///
    /// 操作: F5〜F10 区間を選ぶ / Space 開始・標本・次へ / Esc 中断 / C 接触 / O 逸脱（その場で中止）/
    /// X 直前の合図を無効（振らなかった）/ Tab 実施者パネル / L 記録を読み直す。
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class UsbGateDirector : MonoBehaviour
    {
        [Header("テスト")]
        [SerializeField] string testId = UsbGatePlan.DefaultTestId;
        [Tooltip("責任者（19章のテスト記録に残す）")]
        [SerializeField] string owner = "";
        [SerializeField, Min(1)] int plannedChildren = UsbGatePlan.ChildParticipants;

        [Header("固定する条件（T0-A/B・T0-CD の採用値。テスト中は変えない）")]
        [SerializeField] CooldownPreset adoptedCooldown = EnduranceTestPlan.DefaultCooldown;
        [Tooltip("T0-A/B（#49）の採用案の名前（記録用）")]
        [SerializeField] string feedbackLabel = "案A";
        [Tooltip("接続の名前（記録用）。BLE で同じ手順を通すとき（T6-BLE）はここを変える")]
        [SerializeField] string connectionLabel = "USB";

        [Header("描画の条件（展示と同じにする）")]
        [Tooltip("1 = 垂直同期あり（展示の既定）。0 にすると fps は上がるが展示と条件が変わる")]
        [SerializeField, Range(0, 2)] int vSyncCount = 1;
        [Tooltip("-1 = 上限なし（垂直同期に従う）")]
        [SerializeField] int targetFrameRate = -1;

        [Header("参照（未設定ならシーン内から探す）")]
        [SerializeField] ThrowInputController input;
        [SerializeField] OnusaAimController aim;
        [SerializeField] ConecteController controller;
        [SerializeField] UsbFrameClock frameClock;
        [SerializeField] UsbLoadCrowd loadCrowd;
        [SerializeField] Transform nearTarget;
        [SerializeField] Transform farTarget;
        [SerializeField] AudioSource seSource;

        [Header("意図的 100 投")]
        [SerializeField, Min(1)] int cueCount = UsbGatePlan.IntendedThrows;
        [SerializeField, Min(0.8f)] float cueIntervalSeconds = UsbGatePlan.CueIntervalSeconds;
        [SerializeField, Min(0f)] float cueLeadInSeconds = UsbGatePlan.CueLeadInSeconds;

        [Header("ドリフト")]
        [SerializeField, Min(5f)] float driftSeconds = UsbGatePlan.DriftSeconds;

        [Header("30 体負荷")]
        [SerializeField, Min(0f)] float loadWarmupSeconds = UsbGatePlan.LoadWarmupSeconds;
        [SerializeField, Min(5f)] float loadMeasureSeconds = UsbGatePlan.LoadMeasureSeconds;
        [Tooltip("計測中に 1 秒ごとに振りピークを入れて、弾・軌跡・SE・振動の描画も負荷に含める（実機で振る場合も OFF にしなくてよい）")]
        [SerializeField] bool loadAutoThrow = true;
        [SerializeField, Min(0.2f)] float loadAutoThrowSeconds = UsbGatePlan.LoadAutoThrowSeconds;
        [SerializeField] float loadAutoThrowStrength = 400f;

        [Header("子ども 近・遠")]
        [SerializeField, Min(0f)] float practiceSeconds = UsbGatePlan.ChildPracticeSeconds;
        [SerializeField, Min(1)] int throwsPerRange = UsbGatePlan.ThrowsPerRange;
        [SerializeField, Min(0.1f)] float targetRadius = UsbGatePlan.TargetRadius;

        [Header("区間ごとに群衆（30 体）を出すか")]
        [SerializeField] bool crowdInThrows = true;
        [SerializeField] bool crowdInDrift = true;
        [Tooltip("子どもには的だけを見せる（照準と命中を見るため）。最悪描画のまま測るなら ON")]
        [SerializeField] bool crowdInChildren = false;

        [Header("記録")]
        [Tooltip("空ならプロジェクト直下の PlaytestLogs/（ビルドでは persistentDataPath）")]
        [SerializeField] string csvFolder = "";
        [SerializeField] bool loadExistingOnStart = true;

        [Header("表示")]
        [SerializeField] bool showOperatorPanel = true;

        /// <summary>発射 1 つ分の追跡（画面に出た時刻と、子どもの着弾の判定に使う）。</summary>
        sealed class FireTrack
        {
            public UsbGateEvent Row;
            public OmamoriProjectile Projectile;
            public int VisibleFrame = -1;
            public string Block = "";
            public Vector3 Target;
        }

        readonly List<UsbGateEvent> _events = new List<UsbGateEvent>();
        readonly List<UsbGateEvent> _pending = new List<UsbGateEvent>();
        readonly List<FireTrack> _tracks = new List<FireTrack>();
        readonly List<double> _cueTimes = new List<double>();

        readonly UsbLinkMonitor _link = new UsbLinkMonitor();
        readonly UsbClockAligner _clock = new UsbClockAligner();
        readonly FrameTimeStats _frames = new FrameTimeStats();
        readonly UsbStillnessDetector _still = new UsbStillnessDetector();

        UsbGateSummary _summary;
        UsbGatePhase _phase = UsbGatePhase.Menu;
        UsbGateSection _section;
        int _run;
        int _participant;
        int _nextChild = 1;

        double _sectionStart;
        double _phaseStart;
        int _fireSeq;
        bool _connectedAtStart;
        int _deviceTimeSamples;
        UsbGateEvent _openDisconnect;
        bool _framesMeasuring;

        // 意図的 100 投
        int _cueIndex;
        double _cueFlashUntil = double.NegativeInfinity;
        AudioClip _cueClip;

        // ドリフト
        bool _driftRunning;
        double _driftRunStart;
        double _lastTrack;
        string _captureLabel;
        double _captureRequested = double.NaN;

        // 負荷
        bool _loadMeasuring;
        double _nextAutoThrow;

        // 子ども
        string _block = "";
        int _blockFires;
        double _blockStart;

        // 発射と弾の結び付け（購読の順番が前後しても同じフレームなら結ぶ）
        FireTrack _unlinkedFire;
        int _unlinkedFireFrame = -1;
        OmamoriProjectile _unlinkedProjectile;
        int _unlinkedProjectileFrame = -1;

        bool[] _safetyChecks = new bool[UsbGatePlan.SafetyItems.Length];
        string _safetyNote = "";

        string _eventsPath = "";
        string _summaryPath = "";
        string _message = "";
        bool _subscribed;
        GUIStyle _bannerStyle;
        GUIStyle _subStyle;
        GUIStyle _cueStyle;
        Vector2 _panelScroll;

        public UsbGatePhase Phase => _phase;
        public UsbGateSection Section => _section;
        public UsbGateSummary Summary => _summary;
        public IReadOnlyList<UsbGateEvent> Events => _events;
        public IReadOnlyList<UsbGateEvent> PendingEvents => _pending;
        public string EventsPath => _eventsPath;
        public string SummaryPath => _summaryPath;
        public string Message => _message;
        public int NextChild => _nextChild;

        /// <summary>今日の最後の安全チェックで全項目が適合しているか。</summary>
        public bool SafetyPassed => _summary != null && _summary.SafetyChecked &&
                                    _summary.SafetyItemsOk == UsbGatePlan.SafetyItems.Length;

        static double Now => Time.realtimeSinceStartupAsDouble;
        string Today => DateTime.Now.ToString("yyyy-MM-dd");

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<ThrowInputController>();
            if (aim == null) aim = FindAnyObjectByType<OnusaAimController>();
            if (controller == null) controller = FindAnyObjectByType<ConecteController>();
            if (frameClock == null) frameClock = FindAnyObjectByType<UsbFrameClock>();
            if (loadCrowd == null) loadCrowd = FindAnyObjectByType<UsbLoadCrowd>();
            if (input == null) Debug.LogWarning("[T6-USB] ThrowInputController がありません", this);
            if (frameClock == null) Debug.LogWarning("[T6-USB] UsbFrameClock がありません（画面に出た時刻が 1 フレーム遅れて記録されます）", this);

            QualitySettings.vSyncCount = vSyncCount;
            Application.targetFrameRate = targetFrameRate;
            Input.imeCompositionMode = IMECompositionMode.On;

            _eventsPath = UsbGateCsvFile.EventsPathOf(csvFolder, testId, Today);
            _summaryPath = UsbGateCsvFile.SummaryPathOf(csvFolder, testId, Today);
            if (loadExistingOnStart) Reload();
            else RefreshSummary();

            ShowTargets(true, true);
            ApplyCooldown();
        }

        void OnEnable() => Subscribe();

        void OnDisable() => Unsubscribe();

        void OnDestroy()
        {
            if (_cueClip != null) Destroy(_cueClip);
        }

        void Start()
        {
            // 区間の外では振っても何も出さない
            if (input != null) input.enabled = false;
        }

        // ══════════════════════════════════════════════════════
        //  進行
        // ══════════════════════════════════════════════════════

        void Update()
        {
            double now = Now;
            StampVisibleTimes(now);

            if (Input.GetKeyDown(KeyCode.Tab)) showOperatorPanel = !showOperatorPanel;

            bool idle = _phase == UsbGatePhase.Menu || _phase == UsbGatePhase.Result;
            if (idle)
            {
                if (Input.GetKeyDown(KeyCode.L)) { Reload(); _message = $"{_events.Count} 行を読み直しました"; }
                if (Input.GetKeyDown(KeyCode.F5)) StartSection(UsbGateSection.Safety);
                if (Input.GetKeyDown(KeyCode.F6)) StartSection(UsbGateSection.Throws100);
                if (Input.GetKeyDown(KeyCode.F7)) StartSection(UsbGateSection.DriftStatic);
                if (Input.GetKeyDown(KeyCode.F8)) StartSection(UsbGateSection.DriftOperate);
                if (Input.GetKeyDown(KeyCode.F9)) StartSection(UsbGateSection.Load);
                if (Input.GetKeyDown(KeyCode.F10)) StartSection(UsbGateSection.Children);
            }
            else if (_phase != UsbGatePhase.Safety)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) Abort("実施者が中断");
                if (Input.GetKeyDown(KeyCode.C)) MarkIncident(UsbGateBlock.Contact);
                if (Input.GetKeyDown(KeyCode.O)) MarkIncident(UsbGateBlock.Deviation);
                if (_phase == UsbGatePhase.Active && _section == UsbGateSection.Throws100 && Input.GetKeyDown(KeyCode.X))
                    VoidLastCue();
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                _phase = UsbGatePhase.Menu;
            }

            // 記録画面・安全チェックでは所見を入力できるので、Space は入力欄にフォーカスが無いときだけ
            if (Input.GetKeyDown(KeyCode.Space) && GUIUtility.keyboardControl == 0) HandleSpace(now);

            if (_phase == UsbGatePhase.Active || _phase == UsbGatePhase.Settle || _phase == UsbGatePhase.CaptureEnd)
            {
                if (_link.Poll(now)) OpenDisconnect(now);
                if (_framesMeasuring)
                {
                    double dt = frameClock != null && frameClock.Frame == Time.frameCount
                        ? frameClock.LastFrameSeconds
                        : Time.unscaledDeltaTime;
                    _frames.Add(dt);
                }
            }

            switch (_phase)
            {
                case UsbGatePhase.Active: TickActive(now); break;
                case UsbGatePhase.Settle: TickSettle(now); break;
                case UsbGatePhase.CaptureEnd: TickCapture(now); break;
            }
        }

        void LateUpdate()
        {
            // 弾（と軌跡）の見た目がこのフレームで初めて有効になったか。present は次のフレームの始まりまでに終わる
            int frame = Time.frameCount;
            foreach (FireTrack track in _tracks)
            {
                if (track.VisibleFrame >= 0 || track.Projectile == null) continue;
                if (IsVisible(track.Projectile)) track.VisibleFrame = frame;
            }
        }

        void HandleSpace(double now)
        {
            switch (_phase)
            {
                case UsbGatePhase.Prepare:
                    BeginActive(now);
                    break;
                case UsbGatePhase.CaptureEnd:
                    if (double.IsNaN(_captureRequested)) RequestCapture(UsbGateBlock.DriftEnd, now);
                    break;
                case UsbGatePhase.Active:
                    if (IsDrift && !_driftRunning && double.IsNaN(_captureRequested)) RequestCapture(UsbGateBlock.DriftStart, now);
                    break;
                case UsbGatePhase.Result:
                    _phase = UsbGatePhase.Menu;
                    _message = "";
                    break;
            }
        }

        bool IsDrift => _section == UsbGateSection.DriftStatic || _section == UsbGateSection.DriftOperate;

        /// <summary>区間を選ぶ。安全チェックが全項目適合していなければ、安全チェック以外は始められない。</summary>
        public bool StartSection(UsbGateSection section)
        {
            if (_phase != UsbGatePhase.Menu && _phase != UsbGatePhase.Result) return false;
            if (section != UsbGateSection.Safety && !SafetyPassed)
            {
                _message = "先に安全チェック（F5）で全項目適合を記録してください";
                return false;
            }

            _section = section;
            _pending.Clear();
            _tracks.Clear();
            _fireSeq = 0;
            _message = "";

            if (section == UsbGateSection.Safety)
            {
                _safetyChecks = new bool[UsbGatePlan.SafetyItems.Length];
                _safetyNote = "";
                _phase = UsbGatePhase.Safety;
                return true;
            }

            _participant = section == UsbGateSection.Children ? _nextChild : 0;
            if (loadCrowd != null) loadCrowd.LoadActive = CrowdFor(section);
            ShowTargets(true, true);
            ApplyCooldown();
            _still.Reset();
            _phase = UsbGatePhase.Prepare;
            // 準備中もキャリブレーション（正面ボタン 1 秒）を受け付けるため入力は有効にする。準備中の発射は記録しない
            if (input != null) input.enabled = true;
            return true;
        }

        bool CrowdFor(UsbGateSection section)
        {
            switch (section)
            {
                case UsbGateSection.Throws100: return crowdInThrows;
                case UsbGateSection.DriftStatic:
                case UsbGateSection.DriftOperate: return crowdInDrift;
                case UsbGateSection.Children: return crowdInChildren;
                default: return true;
            }
        }

        /// <summary>計測を始める（準備画面で Space）。</summary>
        public void BeginActive(double now)
        {
            if (_phase != UsbGatePhase.Prepare) return;

            _run = NextRun();
            _sectionStart = now;
            _phaseStart = now;
            _phase = UsbGatePhase.Active;
            _connectedAtStart = controller != null && controller.isConnected;
            _link.Begin(now, _connectedAtStart);
            _clock.Clear();
            _deviceTimeSamples = 0;
            _openDisconnect = null;
            _frames.Clear();
            _framesMeasuring = _section != UsbGateSection.Load;
            _unlinkedFire = null;
            _unlinkedProjectile = null;

            AddRow(UsbGateEventType.SectionStart, 0.0);
            WriteEnv();

            switch (_section)
            {
                case UsbGateSection.Throws100:
                    _cueTimes.Clear();
                    for (int k = 0; k < cueCount; k++) _cueTimes.Add(cueLeadInSeconds + k * cueIntervalSeconds);
                    _cueIndex = 0;
                    _cueFlashUntil = double.NegativeInfinity;
                    break;
                case UsbGateSection.DriftStatic:
                case UsbGateSection.DriftOperate:
                    _driftRunning = false;
                    RequestCapture(UsbGateBlock.DriftStart, now);
                    break;
                case UsbGateSection.Load:
                    _loadMeasuring = false;
                    _nextAutoThrow = now + loadAutoThrowSeconds;
                    break;
                case UsbGateSection.Children:
                    StartBlock(UsbGateBlock.Practice, now);
                    break;
            }
        }

        void TickActive(double now)
        {
            double t = now - _sectionStart;
            switch (_section)
            {
                case UsbGateSection.Throws100:
                    while (_cueIndex < _cueTimes.Count && t >= _cueTimes[_cueIndex])
                    {
                        UsbGateEvent cue = AddRow(UsbGateEventType.Cue, _cueTimes[_cueIndex]);
                        cue.Seq = _cueIndex + 1;
                        _cueIndex++;
                        _cueFlashUntil = now + 0.35;
                        PlayCue();
                    }
                    if (_cueTimes.Count == 0 || t >= _cueTimes[_cueTimes.Count - 1] + UsbGatePlan.CueWindowAfterSeconds)
                        EnterSettle(now);
                    break;

                case UsbGateSection.DriftStatic:
                case UsbGateSection.DriftOperate:
                    if (!_driftRunning)
                    {
                        TickCapture(now);
                        break;
                    }
                    if (_section == UsbGateSection.DriftStatic && now - _lastTrack >= UsbGatePlan.DriftTrackIntervalSeconds)
                    {
                        _lastTrack = now;
                        AddDriftSample(UsbGateBlock.DriftTrack, now);
                    }
                    if (now - _driftRunStart >= driftSeconds)
                    {
                        _phase = UsbGatePhase.CaptureEnd;
                        _phaseStart = now;
                        _captureRequested = double.NaN;
                        if (input != null) input.enabled = false;
                    }
                    break;

                case UsbGateSection.Load:
                    if (!_loadMeasuring && t >= loadWarmupSeconds)
                    {
                        _loadMeasuring = true;
                        _framesMeasuring = true;
                        _frames.Clear();
                        if (loadCrowd != null) loadCrowd.ResetWindow();
                    }
                    if (loadAutoThrow && input != null && input.Machine != null && now >= _nextAutoThrow)
                    {
                        _nextAutoThrow += loadAutoThrowSeconds;
                        // 本番と同じ入口（振りピーク）から入れる。以後は SwingAccepted → 発射・SE・振動の本番の経路
                        input.Machine.SwingPeak(loadAutoThrowStrength, now);
                    }
                    if (t >= loadWarmupSeconds + loadMeasureSeconds) EnterSettle(now);
                    break;

                case UsbGateSection.Children:
                    if (_block == UsbGateBlock.Practice && now - _blockStart >= practiceSeconds)
                        StartBlock(UsbGateBlock.Near, now);
                    break;
            }
        }

        void EnterSettle(double now)
        {
            _phase = UsbGatePhase.Settle;
            _phaseStart = now;
            if (input != null) input.enabled = false;
        }

        void TickSettle(double now)
        {
            if (now - _phaseStart < UsbGatePlan.SettleSeconds) return;

            if (_section == UsbGateSection.Children && _block == UsbGateBlock.Near)
            {
                _phase = UsbGatePhase.Active;
                StartBlock(UsbGateBlock.Far, now);
                return;
            }
            Finish(true, "");
        }

        void StartBlock(string block, double now)
        {
            _block = block;
            _blockFires = 0;
            _blockStart = now;
            UsbGateEvent row = AddRow(UsbGateEventType.Block, now - _sectionStart);
            row.Label = block;
            ShowTargets(block != UsbGateBlock.Far, block != UsbGateBlock.Near);
            if (input != null) input.enabled = true;
        }

        void Abort(string reason)
        {
            if (_phase == UsbGatePhase.Prepare)
            {
                // まだ何も記録していない
                _phase = UsbGatePhase.Menu;
                if (input != null) input.enabled = false;
                _message = $"{UsbGatePlan.LabelOf(_section)}: 開始前にやめました";
                return;
            }
            Finish(false, reason);
        }

        /// <summary>区間を終えて記録する。completed = false なら中断（合否に使わない）。</summary>
        void Finish(bool completed, string reason)
        {
            double now = Now;
            if (input != null) input.enabled = false;
            _framesMeasuring = false;
            _link.End(now);
            ShowTargets(true, true);

            if (_openDisconnect != null && !double.IsNaN(_link.GapStart))
                _openDisconnect.Value = now - _link.GapStart;
            _openDisconnect = null;

            foreach (UsbGateEvent row in _pending)
            {
                if (!row.Is(UsbGateEventType.Fire) || !row.InputTime.HasValue || !row.ReceiveTime.HasValue) continue;
                row.InputUnity = _clock.ToUnity(row.InputTime.Value, row.ReceiveTime.Value);
            }

            double end = now - _sectionStart;
            Metric(UsbGateMetric.Connected, _connectedAtStart ? 1 : 0, end);
            Metric(UsbGateMetric.Samples, _link.Samples, end);
            Metric(UsbGateMetric.SampleRateHz, _link.SampleRateHz, end);
            Metric(UsbGateMetric.MaxGapMs, _link.Samples > 0 || _connectedAtStart ? _link.MaxGapSeconds * 1000.0 : (double?)null, end);
            Metric(UsbGateMetric.DeviceTimeSamples, _deviceTimeSamples, end);
            Metric(UsbGateMetric.AverageFps, _frames.AverageFps, end);
            Metric(UsbGateMetric.OnePercentLowFps, _frames.OnePercentLowFps, end);
            Metric(UsbGateMetric.WorstFrameMs, _frames.WorstFrameMs, end);
            Metric(UsbGateMetric.Frames, _frames.Frames, end);
            if (_section == UsbGateSection.Load && loadCrowd != null)
            {
                Metric(UsbGateMetric.RenderedMin, loadCrowd.RenderedMin, end);
                Metric(UsbGateMetric.RenderedAverage, loadCrowd.RenderedAverage, end);
                Metric(UsbGateMetric.BlackAverage, loadCrowd.BlackAverage, end);
                Metric(UsbGateMetric.ExitingAverage, loadCrowd.ExitingAverage, end);
            }

            UsbGateEvent last = AddRow(UsbGateEventType.SectionEnd, end);
            last.Flag = completed;
            last.Detail = reason ?? "";

            bool saved = Save();
            if (completed && _section == UsbGateSection.Children) _nextChild = Math.Max(_nextChild, _participant + 1);

            _message = $"{UsbGatePlan.LabelOf(_section)}" + (_participant > 0 ? $"（参加者 {_participant}）" : "") +
                       (completed ? " を記録しました" : $" を中断しました（{reason}）。この回は合否に使いません") +
                       (saved ? "" : " — 保存に失敗");
            _phase = UsbGatePhase.Result;
            Debug.Log($"[T6-USB] {_message} → {_eventsPath}", this);
        }

        // ══════════════════════════════════════════════════════
        //  安全チェック
        // ══════════════════════════════════════════════════════

        /// <summary>安全チェックを記録する（全項目の適合／不適合を残す。不適合があればほかの区間は始められない）。</summary>
        public void SaveSafety(bool[] checks, string note)
        {
            if (_phase != UsbGatePhase.Safety) return;
            _run = NextRun();
            _sectionStart = Now;
            _pending.Clear();
            AddRow(UsbGateEventType.SectionStart, 0.0);
            WriteEnv();

            for (int i = 0; i < UsbGatePlan.SafetyItems.Length; i++)
            {
                UsbGateEvent row = AddRow(UsbGateEventType.SafetyItem, 0.0);
                row.Label = UsbGatePlan.SafetyItems[i].Id;
                row.Flag = checks != null && i < checks.Length && checks[i];
                row.Detail = UsbGatePlan.SafetyItems[i].Text;
            }
            if (!string.IsNullOrWhiteSpace(note))
            {
                UsbGateEvent n = AddRow(UsbGateEventType.Env, 0.0);
                n.Label = "note";
                n.Detail = note;
            }
            UsbGateEvent end = AddRow(UsbGateEventType.SectionEnd, 0.0);
            end.Flag = true;

            Save();
            _message = SafetyPassed ? "安全チェック: 全項目適合。計測を始められます" : "安全チェック: 不適合があります。直してからもう一度チェックしてください";
            _phase = UsbGatePhase.Result;
        }

        public void MarkIncident(string kind)
        {
            if (_phase == UsbGatePhase.Menu || _phase == UsbGatePhase.Result || _phase == UsbGatePhase.Safety) return;
            if (_phase == UsbGatePhase.Prepare)
            {
                // 計測前でも安全事象は残す（回を始めて、そのまま中断として記録する）
                BeginActive(Now);
            }
            UsbGateEvent row = AddRow(UsbGateEventType.Incident, Now - _sectionStart);
            row.Label = kind;
            Debug.LogWarning($"[T6-USB] 安全事象: {kind}", this);
            // 領域からの逸脱は即時停止（12章）
            if (kind == UsbGateBlock.Deviation) Finish(false, "領域からの逸脱のため停止");
        }

        // ══════════════════════════════════════════════════════
        //  意図的 100 投
        // ══════════════════════════════════════════════════════

        void VoidLastCue()
        {
            if (_cueIndex <= 0) return;
            foreach (UsbGateEvent e in _pending)
            {
                if (e.Is(UsbGateEventType.CueVoid) && e.Seq == _cueIndex) return;
            }
            UsbGateEvent row = AddRow(UsbGateEventType.CueVoid, Now - _sectionStart);
            row.Seq = _cueIndex;
            _message = $"合図 {_cueIndex} を無効にしました";
        }

        void PlayCue()
        {
            if (seSource == null) return;
            if (_cueClip == null) _cueClip = ProceduralTone.Create("UsbGateCue", 880f, 0.08f, 0.5f);
            seSource.PlayOneShot(_cueClip);
        }

        // ══════════════════════════════════════════════════════
        //  ドリフト
        // ══════════════════════════════════════════════════════

        void RequestCapture(string label, double now)
        {
            _captureLabel = label;
            _captureRequested = now;
            _message = "静止を確かめています…";
        }

        void TickCapture(double now)
        {
            if (double.IsNaN(_captureRequested)) return;

            bool connected = controller != null && controller.isConnected;
            bool still = !connected || _still.IsStill;
            if (!still)
            {
                if (now - _captureRequested > UsbGatePlan.StillTimeoutSeconds)
                {
                    _captureRequested = double.NaN;
                    _message = "静止していません。置き台で手を離してから Space で取り直してください";
                }
                return;
            }

            if (!AddDriftSample(_captureLabel, now))
            {
                _captureRequested = double.NaN;
                _message = "照準が取れていません（OnusaAimController）";
                return;
            }
            _captureRequested = double.NaN;
            _message = "";

            if (_captureLabel == UsbGateBlock.DriftStart)
            {
                _driftRunning = true;
                _driftRunStart = now;
                _lastTrack = now;
                // 操作ドリフトは手に持って振る。静止ドリフトは置いたまま（振っても記録には残る）
            }
            else if (_captureLabel == UsbGateBlock.DriftEnd)
            {
                Finish(true, "");
            }
        }

        bool AddDriftSample(string label, double now)
        {
            if (aim == null || !aim.HasAim || Screen.width <= 0) return false;
            bool connected = controller != null && controller.isConnected;
            Vector3 screen = aim.ScreenPosition;
            UsbGateEvent row = AddRow(UsbGateEventType.DriftSample, now - _sectionStart);
            row.Label = label;
            row.Value = screen.x / Screen.width;
            row.Flag = connected && _still.IsStill;
            float pitch = input != null && input.RawSource != null ? input.RawSource.Pitch : 0f;
            row.Detail = FormattableString.Invariant(
                $"yaw={(input != null ? input.RelativeYaw : 0f):0.00};pitch={pitch:0.00};y={screen.y / Mathf.Max(1, Screen.height):0.0000};connected={(connected ? 1 : 0)}");
            return true;
        }

        // ══════════════════════════════════════════════════════
        //  計測（購読）
        // ══════════════════════════════════════════════════════

        void Subscribe()
        {
            if (_subscribed) return;
            if (input != null)
            {
                input.SwingAccepted += HandleSwingAccepted;
                input.SwingRejected += HandleSwingRejected;
            }
            if (controller != null) controller.SampleReceived += HandleSample;
            OmamoriProjectile.AnyLaunched += HandleLaunched;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;
            if (input != null)
            {
                input.SwingAccepted -= HandleSwingAccepted;
                input.SwingRejected -= HandleSwingRejected;
            }
            if (controller != null) controller.SampleReceived -= HandleSample;
            OmamoriProjectile.AnyLaunched -= HandleLaunched;
            _subscribed = false;
        }

        bool Recording => _phase == UsbGatePhase.Active;

        void HandleSwingAccepted(SwingAcceptedArgs e)
        {
            if (!Recording) return;
            if (_section == UsbGateSection.Children && _block != UsbGateBlock.Practice && _blockFires >= throwsPerRange) return;

            double now = Now;
            UsbGateEvent row = AddRow(UsbGateEventType.Fire, now - _sectionStart);
            row.Seq = ++_fireSeq;
            row.ReceiveTime = e.Time;
            row.FireTime = now;
            row.Value = e.Strength;
            row.Detail = e.Kind.ToString();
            if (input != null && input.RawSource is ISwingPeakInputTime inputTime && !double.IsNaN(inputTime.LastSwingPeakInputTime))
                row.InputTime = inputTime.LastSwingPeakInputTime;

            var track = new FireTrack { Row = row };
            if (_section == UsbGateSection.Children)
            {
                row.Label = _block;
                track.Block = _block;
                Transform target = _block == UsbGateBlock.Far ? farTarget : nearTarget;
                if (target != null) track.Target = target.position;
            }
            _tracks.Add(track);

            if (_unlinkedProjectile != null && _unlinkedProjectileFrame == Time.frameCount)
            {
                Link(track, _unlinkedProjectile);
                _unlinkedProjectile = null;
            }
            else
            {
                _unlinkedFire = track;
                _unlinkedFireFrame = Time.frameCount;
            }

            if (_section == UsbGateSection.Children && _block != UsbGateBlock.Practice)
            {
                _blockFires++;
                if (_blockFires >= throwsPerRange) EnterSettle(now);
            }
        }

        void HandleSwingRejected(SwingRejectedArgs e)
        {
            if (!Recording) return;
            UsbGateEvent row = AddRow(UsbGateEventType.Reject, Now - _sectionStart);
            row.Label = e.Reason.ToString();
            row.ReceiveTime = e.Time;
            row.Value = e.Strength;
            if (_section == UsbGateSection.Children) row.Detail = _block;
        }

        void HandleLaunched(OmamoriProjectile projectile)
        {
            if (_unlinkedFire != null && _unlinkedFireFrame == Time.frameCount)
            {
                Link(_unlinkedFire, projectile);
                _unlinkedFire = null;
            }
            else if (Recording)
            {
                _unlinkedProjectile = projectile;
                _unlinkedProjectileFrame = Time.frameCount;
            }
        }

        void Link(FireTrack track, OmamoriProjectile projectile)
        {
            track.Projectile = projectile;
            if (IsVisible(projectile)) track.VisibleFrame = Time.frameCount;
            projectile.Landed += result => HandleLanded(track, result);
        }

        void HandleLanded(FireTrack track, LandingResult result)
        {
            if (_section != UsbGateSection.Children) return;
            if (track.Block != UsbGateBlock.Near && track.Block != UsbGateBlock.Far) return;
            if (_phase != UsbGatePhase.Active && _phase != UsbGatePhase.Settle) return;
            if (!_pending.Contains(track.Row)) return;

            Vector3 d = result.LandedPoint - track.Target;
            d.y = 0f;
            float distance = d.magnitude;
            UsbGateEvent row = AddRow(UsbGateEventType.Landing, Now - _sectionStart);
            row.Seq = track.Row.Seq;
            row.Label = track.Block;
            row.Value = distance;
            row.Flag = distance <= targetRadius + 1e-4f;
        }

        void HandleSample(ControllerSample sample)
        {
            if (_phase == UsbGatePhase.Menu || _phase == UsbGatePhase.Result || _phase == UsbGatePhase.Safety) return;
            _still.Add(sample.Yaw, sample.Pitch, sample.Time);

            if (_phase != UsbGatePhase.Active && _phase != UsbGatePhase.Settle && _phase != UsbGatePhase.CaptureEnd) return;
            if (_link.AddSample(sample.Time, out double gap))
            {
                if (_openDisconnect != null) _openDisconnect.Value = gap;
                else
                {
                    UsbGateEvent row = AddRow(UsbGateEventType.Disconnect, sample.Time - _sectionStart);
                    row.Value = gap;
                }
                _openDisconnect = null;
            }
            if (sample.HasDeviceTime)
            {
                _clock.Add(sample.Time, sample.DeviceTime);
                _deviceTimeSamples++;
            }
        }

        void OpenDisconnect(double now)
        {
            _openDisconnect = AddRow(UsbGateEventType.Disconnect, now - _sectionStart);
            _openDisconnect.Detail = "受信が途絶";
            Debug.LogWarning("[T6-USB] 受信が途絶えました（切断 1 回）", this);
        }

        void StampVisibleTimes(double now)
        {
            int frame = Time.frameCount;
            double frameStart = frameClock != null && frameClock.Frame == frame ? frameClock.FrameStart : now;
            foreach (FireTrack track in _tracks)
            {
                if (track.VisibleFrame < 0 || track.VisibleFrame >= frame || track.Row.VisibleTime.HasValue) continue;
                track.Row.VisibleTime = frameStart;
            }
        }

        static bool IsVisible(OmamoriProjectile projectile)
        {
            if (projectile == null || !projectile.gameObject.activeInHierarchy) return false;
            foreach (Renderer r in projectile.GetComponentsInChildren<Renderer>())
            {
                if (r != null && r.enabled && !(r is TrailRenderer)) return true;
            }
            return false;
        }

        // ══════════════════════════════════════════════════════
        //  記録
        // ══════════════════════════════════════════════════════

        UsbGateEvent AddRow(string type, double t)
        {
            var row = new UsbGateEvent
            {
                TestId = testId,
                Date = Today,
                Run = _run,
                Section = UsbGatePlan.KeyOf(_section),
                Participant = _participant,
                Event = type,
                T = t
            };
            _pending.Add(row);
            return row;
        }

        void Metric(string label, double? value, double t)
        {
            UsbGateEvent row = AddRow(UsbGateEventType.Metric, t);
            row.Label = label;
            row.Value = value;
        }

        void WriteEnv()
        {
            Env("owner", owner);
            Env("connection", connectionLabel);
            Env("cooldown_sec", CurrentCooldownSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            Env("feedback", feedbackLabel);
            Env("platform", Application.isEditor ? "Editor" : Application.platform.ToString());
            Env("unity", Application.unityVersion);
            Env("build", Application.version);
            Env("device", SystemInfo.deviceModel);
            Env("gpu", SystemInfo.graphicsDeviceName);
            Env("resolution", $"{Screen.width}x{Screen.height}");
            Env("refresh_hz", Screen.currentResolution.refreshRateRatio.value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            Env("vsync", QualitySettings.vSyncCount.ToString());
            Env("target_frame_rate", Application.targetFrameRate.ToString());
            Env("crowd", loadCrowd != null && loadCrowd.LoadActive ? "on" : "off");
        }

        void Env(string key, string value)
        {
            UsbGateEvent row = AddRow(UsbGateEventType.Env, 0.0);
            row.Label = key;
            row.Detail = value ?? "";
        }

        bool Save()
        {
            if (!UsbGateCsvFile.Append(_eventsPath, _pending, out string error))
            {
                _message = $"保存できませんでした: {error}";
                return false;
            }
            _events.AddRange(_pending);
            _pending.Clear();
            _tracks.Clear();
            RefreshSummary();
            UsbGateCsvFile.WriteSummary(_summaryPath, testId, Today, _summary, out _);
            return true;
        }

        /// <summary>記録 CSV を読み直して判定しなおす。</summary>
        public void Reload()
        {
            _events.Clear();
            _events.AddRange(UsbGateCsvFile.Load(_eventsPath));
            RefreshSummary();
            foreach (int participant in UsbGateSummary.LatestCompletedRunPerParticipant(_events, UsbGateSection.Children).Keys)
                _nextChild = Math.Max(_nextChild, participant + 1);
        }

        void RefreshSummary()
        {
            _summary = UsbGateSummary.Of(_events, plannedChildren);
        }

        int NextRun()
        {
            int max = 0;
            foreach (UsbGateEvent e in _events) max = Math.Max(max, e.Run);
            return max + 1;
        }

        float CurrentCooldownSeconds => input != null ? input.CooldownSeconds : CooldownTestPlan.SecondsOf(adoptedCooldown);

        void ApplyCooldown()
        {
            if (input == null) return;
            input.Preset = adoptedCooldown;
            if (input.Machine != null) input.Machine.CooldownSeconds = input.CooldownSeconds;
        }

        void ShowTargets(bool near, bool far)
        {
            if (nearTarget != null) nearTarget.gameObject.SetActive(near);
            if (farTarget != null) farTarget.gameObject.SetActive(far);
        }

        // ══════════════════════════════════════════════════════
        //  表示
        // ══════════════════════════════════════════════════════

        void OnGUI()
        {
            EnsureStyles();
            DrawBanner();
            if (_phase == UsbGatePhase.Safety) DrawSafetyPanel();
            if (showOperatorPanel) DrawOperatorPanel();
        }

        void EnsureStyles()
        {
            if (_bannerStyle == null)
                _bannerStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            if (_subStyle == null)
                _subStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            if (_cueStyle == null)
                _cueStyle = new GUIStyle(GUI.skin.label) { fontSize = 96, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }

        void DrawBanner()
        {
            double now = Now;
            string title = "", sub = "";
            switch (_phase)
            {
                case UsbGatePhase.Menu:
                    title = "T6-USB";
                    sub = "実施者が区間を選びます";
                    break;
                case UsbGatePhase.Safety:
                    title = "安全チェック";
                    break;
                case UsbGatePhase.Prepare:
                    title = UsbGatePlan.LabelOf(_section);
                    sub = PrepareText();
                    break;
                case UsbGatePhase.Active:
                    ActiveText(now, out title, out sub);
                    break;
                case UsbGatePhase.Settle:
                    title = "おしまい";
                    sub = "そのまま待ってください";
                    break;
                case UsbGatePhase.CaptureEnd:
                    title = "置き台に戻す";
                    sub = double.IsNaN(_captureRequested) ? "大幣を置き台に戻して手を離したら、実施者が Space" : "静止を確かめています…";
                    break;
                case UsbGatePhase.Result:
                    title = "記録しました";
                    sub = string.IsNullOrEmpty(_message) ? "Space でメニューへ" : _message;
                    break;
            }

            var rect = new Rect(0f, 16f, Screen.width, 46f);
            GUI.Label(rect, title, _bannerStyle);
            GUI.Label(new Rect(0f, rect.yMax, Screen.width, 26f), sub, _subStyle);

            if (_phase == UsbGatePhase.Active && _section == UsbGateSection.Throws100 && now < _cueFlashUntil)
                GUI.Label(new Rect(0f, Screen.height * 0.35f, Screen.width, 140f), "ふって！", _cueStyle);
        }

        string PrepareText()
        {
            switch (_section)
            {
                case UsbGateSection.Throws100:
                    return $"合図（音と「ふって！」）が出たら 1 回だけ振る × {cueCount}。実施者が Space で開始";
                case UsbGateSection.DriftStatic:
                    return "大幣を置き台に置き、的を向けて正面ボタン 1 秒 → 手を離して実施者が Space";
                case UsbGateSection.DriftOperate:
                    return "置き台で正面ボタン 1 秒 → 手を離して Space → 合図で手に持って 3 分振る";
                case UsbGateSection.Load:
                    return $"黒客・退場者込み {UsbGatePlan.LoadBodies} 体で {loadWarmupSeconds:0} 秒慣らし＋{loadMeasureSeconds:0} 秒計測。Space で開始";
                case UsbGateSection.Children:
                    return $"参加者 {_participant}: 真ん中の的を向けて正面ボタン 1 秒 → 実施者が Space で {practiceSeconds / 60f:0.#} 分練習";
                default:
                    return "";
            }
        }

        void ActiveText(double now, out string title, out string sub)
        {
            double t = now - _sectionStart;
            switch (_section)
            {
                case UsbGateSection.Throws100:
                    title = $"合図 {_cueIndex} / {_cueTimes.Count}";
                    sub = "合図が出たら 1 回だけ振ってください";
                    return;
                case UsbGateSection.DriftStatic:
                case UsbGateSection.DriftOperate:
                    if (!_driftRunning)
                    {
                        title = "置き台";
                        sub = double.IsNaN(_captureRequested) ? "手を離して実施者が Space" : "静止を確かめています…";
                        return;
                    }
                    title = $"のこり {Clock(driftSeconds - (now - _driftRunStart))}";
                    sub = _section == UsbGateSection.DriftStatic ? "置き台に置いたまま触らないでください" : "手に持って、ふったり向きを変えたりしてください";
                    return;
                case UsbGateSection.Load:
                    title = t < loadWarmupSeconds ? "慣らし中" : $"計測中 のこり {Clock(loadWarmupSeconds + loadMeasureSeconds - t)}";
                    sub = loadCrowd != null ? $"描画 {loadCrowd.Rendered} 体（黒 {loadCrowd.Black}・退場 {loadCrowd.Exiting}）" : "";
                    return;
                case UsbGateSection.Children:
                    if (_block == UsbGateBlock.Practice)
                    {
                        title = $"れんしゅう のこり {Clock(practiceSeconds - (now - _blockStart))}";
                        sub = "的をねらって、ふってみよう";
                    }
                    else
                    {
                        title = $"{(_block == UsbGateBlock.Near ? "ちかくの的" : "とおくの的")}　あと {Mathf.Max(0, throwsPerRange - _blockFires)} 回";
                        sub = "光っている的をねらってください";
                    }
                    return;
                default:
                    title = sub = "";
                    return;
            }
        }

        void DrawSafetyPanel()
        {
            const float width = 760f;
            float height = Mathf.Min(560f, Screen.height - 40f);
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("開始前の安全チェック（12章・17章 T6-USB）— 実地で確かめてから入れる。1 項目でも不適合なら計測しない");
            for (int i = 0; i < UsbGatePlan.SafetyItems.Length; i++)
                _safetyChecks[i] = GUILayout.Toggle(_safetyChecks[i], UsbGatePlan.SafetyItems[i].Text);

            GUILayout.Space(6f);
            GUILayout.Label("所見（直した箇所など）");
            _safetyNote = GUILayout.TextField(_safetyNote ?? "", GUILayout.Height(22f));

            GUILayout.Space(8f);
            int ok = 0;
            foreach (bool c in _safetyChecks) if (c) ok++;
            if (GUILayout.Button($"記録する（{ok}/{_safetyChecks.Length} 項目適合）", GUILayout.Height(32f)))
                SaveSafety(_safetyChecks, _safetyNote);
            if (GUILayout.Button("やめる（Esc）")) _phase = UsbGatePhase.Menu;
            GUILayout.EndArea();
        }

        void DrawOperatorPanel()
        {
            const float width = 600f;
            var area = new Rect(10f, 10f, width, Mathf.Min(620f, Screen.height - 20f));
            double now = Now;

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"[T6-USB #52] {testId}　{Today}　責任者 {(string.IsNullOrEmpty(owner) ? "未記入" : owner)}　" +
                            $"{connectionLabel}　CD {CurrentCooldownSeconds:0.00}s　{feedbackLabel}");
            bool connected = controller != null && controller.isConnected;
            GUILayout.Label($"実機 {(connected ? "接続" : "未接続（クリック代用・判定は不合格扱い）")}　" +
                            $"安全チェック {(SafetyPassed ? "適合" : "未")}　今: {UsbGatePlan.LabelOf(_section)} / {_phase}");

            if (_phase == UsbGatePhase.Menu || _phase == UsbGatePhase.Result)
            {
                GUILayout.BeginHorizontal();
                SectionButton("F5 安全", UsbGateSection.Safety);
                SectionButton("F6 100投", UsbGateSection.Throws100);
                SectionButton("F7 静止", UsbGateSection.DriftStatic);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                SectionButton("F8 操作", UsbGateSection.DriftOperate);
                SectionButton("F9 30体", UsbGateSection.Load);
                SectionButton($"F10 子ども{_nextChild}", UsbGateSection.Children);
                GUILayout.EndHorizontal();
            }
            else if (_phase != UsbGatePhase.Safety && _phase != UsbGatePhase.Prepare)
            {
                GUILayout.Label($"受信 {_link.Samples} 行（{(now - _sectionStart > 0 ? _link.Samples / (now - _sectionStart) : 0):0} Hz）" +
                                $"　最大間隔 {_link.MaxGapSeconds * 1000:0}ms　切断 {_link.Disconnects}　入力時刻つき {_deviceTimeSamples} 行");
                GUILayout.Label($"fps 平均 {Num(_frames.AverageFps)}　1% low {Num(_frames.OnePercentLowFps)}　フレーム {_frames.Frames}" +
                                (loadCrowd != null && loadCrowd.LoadActive ? $"　描画 {loadCrowd.Rendered} 体" : ""));
                GUILayout.Label(UsbLatencyStats.Of(_pending).Describe());
                if (_section == UsbGateSection.Throws100)
                {
                    var cues = new List<double>();
                    var voided = new HashSet<int>();
                    var fires = new List<double>();
                    foreach (UsbGateEvent e in _pending)
                    {
                        if (e.Is(UsbGateEventType.Cue)) cues.Add(e.T);
                        else if (e.Is(UsbGateEventType.CueVoid)) voided.Add(e.Seq - 1);
                        else if (e.Is(UsbGateEventType.Fire)) fires.Add(e.T);
                    }
                    UsbIntentResult r = UsbIntentMatcher.Match(cues, voided, fires);
                    GUILayout.Label($"合図 {r.Cues}（無効 {r.Voided}）　応答 {r.Matched}　欠落 {r.Missed}　誤発射 {r.Extra}");
                }
            }

            GUILayout.Space(4f);
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);
            if (_summary != null)
            {
                GUILayout.Label($"判定: {(_summary.Passed ? "合格" : "未達 — " + _summary.FailureSummary())}");
                foreach (UsbGateCriterion c in _summary.Criteria) GUILayout.Label("　" + c);
                GUILayout.Label("　" + _summary.Latency.Describe());
            }
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message);
            GUILayout.Label($"記録: {_eventsPath}");
            GUILayout.Label("Space 開始/標本/次へ　Esc 中断　C 接触　O 逸脱(停止)　X 合図を無効　Tab パネル　L 読み直し");
            GUILayout.EndArea();
        }

        void SectionButton(string label, UsbGateSection section)
        {
            if (GUILayout.Button(label, GUILayout.Height(26f))) StartSection(section);
        }

        static string Clock(double seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        static string Num(double? value) => value.HasValue ? $"{value.Value:0.0}" : "-";
    }
}
