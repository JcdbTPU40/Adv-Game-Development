using System;
using System.Collections.Generic;
using UnityEngine;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /// <summary>
    /// T0-CD クールダウン値の探索の進行と記録 — Issue #50（仕様書 v8 17章・付録B INPUT.CD）
    ///
    /// 1 人分の流れ（4 条件をラテン方格の順に）:
    /// 単発ブロック → 休憩 → 連投ブロック → 記録 → 休憩 → 次の条件…
    ///
    /// ・条件（0.40 / 0.50 / 0.60 / 0.65 秒）は <see cref="CooldownTestPlan.ConditionAt"/> の順で自動的に切り替える。
    ///   参加者には秒数を見せない（「設定1〜4」としか出さない）。
    /// ・ゲーム側は受理・却下・実連投間隔を自動で数えるが、<b>合否の正本は外部動画</b>。
    ///   動画で数えた 4 項目（単発振り数・連投組数・余分な発射・2 発目欠落）を記録画面か CSV に入れて初めて集計に入る。
    /// ・記録は 1 条件 1 行の集計 CSV と、発射 1 件 1 行の生ログ CSV の 2 本
    ///   （<see cref="CooldownCsvFile"/>）。
    ///
    /// 操作: Space = 開始 / 次へ、Tab = 実施者パネル、V = 同期マーク、L = CSV を読み直して集計。
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class CooldownTestDirector : MonoBehaviour
    {
        [Header("テスト")]
        [SerializeField] string testId = "T0-CD-1";
        [Tooltip("責任者（19章のテスト記録に残す）")]
        [SerializeField] string owner = "";
        [SerializeField, Min(1)] int participantNo = 1;
        [Tooltip("予定人数。1 人あたりの投数はここから割り出す（8 人なら単発 13/13/13/13/12/12/12/12 回）")]
        [SerializeField, Min(1)] int plannedParticipants = CooldownTestPlan.DefaultParticipants;

        [Header("休憩")]
        [SerializeField, Min(0f)] float blockRestSeconds = CooldownTestPlan.BlockRestSeconds;
        [SerializeField, Min(0f)] float conditionRestSeconds = CooldownTestPlan.ConditionRestSeconds;

        [Header("連投の判定")]
        [Tooltip("この秒数以内に続いた 2 発目を「1 組の 2 発目」とみなす。実連投間隔もこの範囲で拾う")]
        [SerializeField, Range(0.3f, 3f)] float pairWindowSeconds = 1.2f;

        [Header("参照（未設定ならシーン内から探す）")]
        [SerializeField] ThrowInputController input;
        [SerializeField] VideoSyncFlash videoSync;

        [Header("記録")]
        [Tooltip("空ならプロジェクト直下の PlaytestLogs/（ビルドでは persistentDataPath）")]
        [SerializeField] string csvFolder = "";
        [Tooltip("同じテストID・同じ日付のファイルがあれば読み込んで続きから始める")]
        [SerializeField] bool loadExistingOnStart = true;

        [Header("表示")]
        [SerializeField] bool showOperatorPanel = true;
        [Tooltip("フレーム上限を外して受理時刻の丸めを小さくする")]
        [SerializeField] bool uncapFrameRate = true;

        readonly CooldownTrialClock _clock = new CooldownTrialClock();
        readonly List<CooldownConditionRecord> _saved = new List<CooldownConditionRecord>();
        readonly IntervalStats _pairIntervals = new IntervalStats();

        CooldownConditionRecord _record;
        CooldownTestSummary _summary;

        double _sessionStart;
        int _accepted;
        int _rejected;
        int _pairCompleted;
        double _pairFirstTime = double.NegativeInfinity;
        double _lastAcceptedTime = double.NegativeInfinity;
        int _resumeTrialIndex;

        string _csvPath = "";
        string _eventPath = "";
        string _message = "";
        bool _subscribed;

        GUIStyle _bannerStyle;
        GUIStyle _subStyle;
        Vector2 _panelScroll;

        public CooldownPhase Phase => _clock.Phase;
        public CooldownConditionRecord Record => _record;
        public CooldownTestSummary Summary => _summary;
        public IReadOnlyList<CooldownConditionRecord> SavedRecords => _saved;
        public string CsvPath => _csvPath;
        public string EventPath => _eventPath;

        /// <summary>今の条件（参加者番号と試行順から決まる）。</summary>
        public CooldownPreset CurrentPreset =>
            CooldownTestPlan.ConditionAt(CooldownTestPlan.OrderIndexOf(participantNo), _clock.TrialIndex);

        static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>セッション開始からの経過秒（生ログ・画面表示・動画の突き合わせに使う共通の時間軸）。</summary>
        public double Elapsed => Now - _sessionStart;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<ThrowInputController>();
            if (videoSync == null) videoSync = FindAnyObjectByType<VideoSyncFlash>();
            if (input == null)
                Debug.LogWarning("[T0-CD] ThrowInputController がありません（投数を数えられません）", this);

            _clock.BlockRestSeconds = blockRestSeconds;
            _clock.ConditionRestSeconds = conditionRestSeconds;

            if (uncapFrameRate)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 0;
            }
            Input.imeCompositionMode = IMECompositionMode.On;

            _sessionStart = Now;
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            _csvPath = CooldownCsvFile.PathOf(csvFolder, testId, date);
            _eventPath = CooldownCsvFile.EventPathOf(csvFolder, testId, date);

            if (loadExistingOnStart) Reload();
            NewRecord();
            LogEvent(CooldownEventKind.SessionStart, note: $"テストID {testId} 予定 {plannedParticipants} 人");
        }

        void OnEnable()
        {
            Subscribe();
            EnterPhase();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void Update()
        {
            if (videoSync != null) videoSync.ElapsedSeconds = Elapsed;

            if (Input.GetKeyDown(KeyCode.Tab)) showOperatorPanel = !showOperatorPanel;
            if (Input.GetKeyDown(KeyCode.V)) MarkSync();
            if (Input.GetKeyDown(KeyCode.L)) { Reload(); _message = $"{_saved.Count} 行を読み直しました"; }
            if (Input.GetKeyDown(KeyCode.Space)) HandleSpace();

            if (_clock.Advance(Now)) EnterPhase();
        }

        void HandleSpace()
        {
            switch (_clock.Phase)
            {
                case CooldownPhase.Ready:
                    _clock.Begin(Now, _resumeTrialIndex);
                    _resumeTrialIndex = 0;
                    EnterPhase();
                    break;
                case CooldownPhase.Record:
                    Save();
                    break;
                case CooldownPhase.Done:
                    NextParticipant();
                    break;
                default:
                    _clock.Next(Now);
                    EnterPhase();
                    break;
            }
        }

        // ---- フェーズ ----

        void EnterPhase()
        {
            CooldownPhase phase = _clock.Phase;

            switch (phase)
            {
                case CooldownPhase.SingleBlock:
                    // 条件が変わる境目。ここで初めてクールダウン値を切り替える
                    if (_record == null || _record.trialIndex != _clock.TrialIndex) NewRecord();
                    ApplyPreset();
                    BeginBlock(CooldownBlock.Single);
                    break;
                case CooldownPhase.PairBlock:
                    BeginBlock(CooldownBlock.Pair);
                    break;
                case CooldownPhase.BlockRest:
                    EndBlock(CooldownBlock.Single);
                    break;
                case CooldownPhase.Record:
                    EndBlock(CooldownBlock.Pair);
                    break;
            }

            // ブロック中だけ投擲を受け付ける（休憩・記録中は振っても何も起きない）
            if (input != null) input.enabled = _clock.IsBlock;
        }

        void BeginBlock(CooldownBlock block)
        {
            _accepted = 0;
            _rejected = 0;
            _pairCompleted = 0;
            _pairFirstTime = double.NegativeInfinity;
            _lastAcceptedTime = double.NegativeInfinity;
            if (block == CooldownBlock.Pair) _pairIntervals.Clear();

            LogEvent(CooldownEventKind.BlockStart, block: block,
                note: block == CooldownBlock.Single ? $"目標 {SingleTarget} 回" : $"目標 {PairTarget} 組");
        }

        void EndBlock(CooldownBlock block)
        {
            if (_record == null) return;

            // 実際に適用されていた秒数を残す（F1〜F4 で手で変えられていても記録は実測に合わせる）
            if (input != null) _record.cooldownSeconds = input.CooldownSeconds;

            if (block == CooldownBlock.Single)
            {
                _record.singleTarget = SingleTarget;
                _record.singleAccepted = _accepted;
                _record.singleRejected = _rejected;
            }
            else
            {
                _record.pairTarget = PairTarget;
                _record.pairAccepted = _accepted;
                _record.pairRejected = _rejected;
                _record.pairCompleted = _pairCompleted;
                _record.SetIntervals(_pairIntervals);
            }

            LogEvent(CooldownEventKind.BlockEnd, block: block,
                note: $"受理 {_accepted} CD却下 {_rejected}" +
                      (block == CooldownBlock.Pair ? $" 成立 {_pairCompleted}組 {_pairIntervals.Describe()}" : ""));
        }

        void ApplyPreset()
        {
            if (input == null) return;
            CooldownPreset preset = CurrentPreset;
            input.Preset = preset;

            // ThrowInputController は自分の Update で状態機械へ流すが、実行順はこちらが後
            // （入力 -100 → 進行 -80）。切り替えたフレームの振りを古い値で判定させないよう、ここで直接入れる
            if (input.Machine != null) input.Machine.CooldownSeconds = input.CooldownSeconds;

            _record.preset = preset;
            _record.cooldownSeconds = input.CooldownSeconds;
        }

        int SingleTarget => CooldownTestPlan.SinglesFor(participantNo, plannedParticipants);
        int PairTarget => CooldownTestPlan.PairsFor(participantNo, plannedParticipants);

        void NewRecord()
        {
            _record = new CooldownConditionRecord
            {
                testId = testId,
                owner = owner,
                date = DateTime.Now.ToString("yyyy-MM-dd"),
                participantNo = participantNo,
                orderIndex = CooldownTestPlan.OrderIndexOf(participantNo),
                trialIndex = _clock.TrialIndex,
                preset = CurrentPreset,
                cooldownSeconds = CooldownTestPlan.SecondsOf(CurrentPreset),
                singleTarget = SingleTarget,
                pairTarget = PairTarget
            };
        }

        void NextParticipant()
        {
            participantNo++;
            _clock.Reset();
            _resumeTrialIndex = 0;
            NewRecord();
            EnterPhase();
            _message = "";
        }

        void Save()
        {
            if (_record == null) return;
            _record.testId = testId;
            _record.owner = owner;

            if (!CooldownCsvFile.Append(_csvPath, _record, out string error))
            {
                _message = $"保存できませんでした: {error}";
                return;
            }

            _saved.Add(_record.Clone());
            RefreshSummary();

            string missing = _record.MissingFields();
            _message = $"参加者 {_record.participantNo} / {CooldownTestPlan.LabelOf(_record.preset)} を保存しました" +
                       (string.IsNullOrEmpty(missing) ? "" : $"（{missing} はあとから CSV へ）");
            Debug.Log($"[T0-CD] {_message} → {_csvPath}", this);

            _clock.Next(Now);
            if (_clock.Phase != CooldownPhase.Done) NewRecord();
            EnterPhase();
        }

        /// <summary>CSV を読み直して集計しなおす（動画側の列を Excel で埋めたあと）。</summary>
        public void Reload()
        {
            _saved.Clear();
            _saved.AddRange(CooldownCsvFile.Load(_csvPath));
            RefreshSummary();

            if (_saved.Count == 0) return;

            // 続きから始める: 最後の参加者の記録が 4 条件そろっていれば次の人、足りなければその続きの条件から
            int lastNo = _saved[_saved.Count - 1].participantNo;
            int done = 0;
            for (int i = 0; i < _saved.Count; i++)
            {
                if (_saved[i].participantNo == lastNo) done++;
            }

            if (done >= CooldownTestPlan.ConditionCount)
            {
                participantNo = lastNo + 1;
                _resumeTrialIndex = 0;
                _message = $"{_saved.Count} 行を読み込みました（参加者 {participantNo} から）";
            }
            else
            {
                participantNo = lastNo;
                _resumeTrialIndex = done;
                _message = $"{_saved.Count} 行を読み込みました（参加者 {lastNo} の {done + 1} 条件目から）";
            }
        }

        void RefreshSummary()
        {
            _summary = CooldownTestSummary.Of(_saved);
        }

        void MarkSync()
        {
            if (videoSync != null) videoSync.Flash();
            LogEvent(CooldownEventKind.SyncMark,
                note: videoSync != null ? $"{videoSync.MarkCount} 回目" : "画面フラッシュなし");
        }

        // ---- 投数 ----

        void Subscribe()
        {
            if (_subscribed || input == null) return;
            input.SwingAccepted += HandleSwingAccepted;
            input.SwingRejected += HandleSwingRejected;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || input == null) return;
            input.SwingAccepted -= HandleSwingAccepted;
            input.SwingRejected -= HandleSwingRejected;
            _subscribed = false;
        }

        void HandleSwingAccepted(SwingAcceptedArgs e)
        {
            if (!_clock.IsBlock) return;

            _accepted++;
            double interval = _lastAcceptedTime > double.NegativeInfinity ? e.Time - _lastAcceptedTime : 0.0;

            if (_clock.Block == CooldownBlock.Pair)
            {
                if (_pairFirstTime > double.NegativeInfinity && e.Time - _pairFirstTime <= pairWindowSeconds)
                {
                    // 1 組の 2 発目。実連投間隔はこの差
                    _pairCompleted++;
                    _pairIntervals.Add(e.Time - _pairFirstTime);
                    _pairFirstTime = double.NegativeInfinity;
                }
                else
                {
                    _pairFirstTime = e.Time;
                }
            }

            _lastAcceptedTime = e.Time;
            LogEvent(CooldownEventKind.Accepted, block: _clock.Block, interval: interval, strength: e.Strength);
        }

        void HandleSwingRejected(SwingRejectedArgs e)
        {
            if (!_clock.IsBlock) return;
            if (e.Reason == SwingRejectReason.Cooldown) _rejected++;

            double interval = _lastAcceptedTime > double.NegativeInfinity ? e.Time - _lastAcceptedTime : 0.0;
            LogEvent(CooldownEventKind.Rejected, block: _clock.Block,
                reason: CooldownEventLog.LabelOf(e.Reason), interval: interval, strength: e.Strength);
        }

        void LogEvent(CooldownEventKind kind, CooldownBlock block = CooldownBlock.None,
            string reason = "", double interval = 0.0, float strength = 0f, string note = "")
        {
            if (string.IsNullOrEmpty(_eventPath)) return;

            float seconds = _record != null ? _record.cooldownSeconds : 0f;
            string line = CooldownEventLog.RowOf(
                Elapsed, participantNo, _clock.TrialIndex, seconds, block, kind, reason, interval, strength, note);
            CooldownCsvFile.AppendEvent(_eventPath, line, out _);
        }

        // ---- 表示 ----

        void OnGUI()
        {
            EnsureStyles();
            DrawBanner();

            if (_clock.Phase == CooldownPhase.Record) DrawRecordPanel();
            if (showOperatorPanel) DrawOperatorPanel();
        }

        void EnsureStyles()
        {
            if (_bannerStyle == null)
            {
                _bannerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 34,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
            }
            if (_subStyle == null)
            {
                _subStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            }
        }

        void DrawBanner()
        {
            // 参加者には秒数を見せない（何秒の設定かを知ると振り方が変わる）
            string setting = $"設定 {_clock.TrialIndex + 1} / {CooldownTestPlan.ConditionCount}";
            string title;
            string sub;

            switch (_clock.Phase)
            {
                case CooldownPhase.Ready:
                    title = "準備";
                    sub = "実施者が Space で開始します";
                    break;
                case CooldownPhase.SingleBlock:
                    title = $"{setting}　単発　{_accepted} / {SingleTarget}";
                    sub = "1 回振って、腕を戻して、また 1 回振ってください";
                    break;
                case CooldownPhase.PairBlock:
                    title = $"{setting}　連投　{_pairCompleted} / {PairTarget} 組";
                    sub = "自分の最速で 2 回振ってください（2 回で 1 組）";
                    break;
                case CooldownPhase.BlockRest:
                case CooldownPhase.ConditionRest:
                    title = $"休憩　残り {_clock.RemainingSeconds(Now):0} 秒";
                    sub = "大幣を置いて休んでください";
                    break;
                case CooldownPhase.Record:
                    title = "記録";
                    sub = "実施者が記録します";
                    break;
                default:
                    title = "終了";
                    sub = string.IsNullOrEmpty(_message) ? "Space で次の参加者へ" : _message;
                    break;
            }

            var rect = new Rect(0f, 16f, Screen.width, 46f);
            GUI.Label(rect, title, _bannerStyle);
            GUI.Label(new Rect(0f, rect.yMax, Screen.width, 26f), sub, _subStyle);
        }

        void DrawRecordPanel()
        {
            const float width = 560f;
            const float height = 380f;
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"記録 — 参加者 {_record.participantNo}　" +
                            $"{_clock.TrialIndex + 1} 条件目　{CooldownTestPlan.LabelOf(_record.preset)}");

            GUILayout.Label($"単発: 受理 {_record.singleAccepted} / 予定 {_record.singleTarget}　" +
                            $"CD却下 {_record.singleRejected}");
            GUILayout.Label($"連投: 成立 {_record.pairCompleted} / 予定 {_record.pairTarget} 組　" +
                            $"受理 {_record.pairAccepted}　CD却下 {_record.pairRejected}");
            GUILayout.Label($"実連投間隔: {_pairIntervals.Describe()}");
            GUILayout.Label($"暫定（動画前）: 余分な発射 {_record.ProvisionalExtraFires}　" +
                            $"2発目欠落 {_record.ProvisionalMissedSecond}");

            GUILayout.Space(6f);
            GUILayout.Label("安全（1 件でもあればその場で中止して原因を直す）");
            GUILayout.BeginHorizontal();
            GUILayout.Label("ストラップ逸脱", GUILayout.Width(110f));
            _record.strapDeviation = Counter(_record.strapDeviation);
            GUILayout.Space(12f);
            GUILayout.Label("筐体接触", GUILayout.Width(70f));
            _record.caseContact = Counter(_record.caseContact);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("動画を見てから入れる 4 項目（今は空のままでよい）");
            _record.videoSingleSwings = VideoField("単発の振り数", _record.videoSingleSwings);
            _record.videoPairSets = VideoField("連投の組数", _record.videoPairSets);
            _record.videoExtraFires = VideoField("余分な発射", _record.videoExtraFires);
            _record.videoMissedSecond = VideoField("2 発目欠落", _record.videoMissedSecond);

            GUILayout.Space(4f);
            GUILayout.Label("所見");
            _record.note = GUILayout.TextField(_record.note ?? "", GUILayout.Height(22f));

            GUILayout.Space(4f);
            if (GUILayout.Button("記録して次へ", GUILayout.Height(28f))) Save();
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message);

            GUILayout.EndArea();
        }

        static int Counter(int value)
        {
            if (GUILayout.Button("-", GUILayout.Width(26f)) && value > 0) value--;
            GUILayout.Label(value.ToString(), GUILayout.Width(26f));
            if (GUILayout.Button("+", GUILayout.Width(26f))) value++;
            return value;
        }

        /// <summary>空欄なら未入力（<see cref="CooldownConditionRecord.NotEntered"/>）のまま。</summary>
        static int VideoField(string label, int value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110f));
            string text = value <= CooldownConditionRecord.NotEntered ? "" : value.ToString();
            string edited = GUILayout.TextField(text, GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            if (string.IsNullOrWhiteSpace(edited)) return CooldownConditionRecord.NotEntered;
            return int.TryParse(edited, out int parsed) && parsed >= 0 ? parsed : value;
        }

        void DrawOperatorPanel()
        {
            const float width = 520f;
            var area = new Rect(10f, 10f, width, Mathf.Min(430f, Screen.height - 20f));

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"[T0-CD #50] {testId}　{DateTime.Now:yyyy-MM-dd}　" +
                            $"責任者 {(string.IsNullOrEmpty(owner) ? "未記入" : owner)}");
            GUILayout.Label($"参加者 {participantNo}（順序 {CooldownTestPlan.OrderIndexOf(participantNo) + 1}: " +
                            $"{OrderText()}）　1 条件あたり 単発 {SingleTarget} 回 / 連投 {PairTarget} 組");
            GUILayout.Label($"今: {CooldownTrialClock.LabelOf(_clock.Phase)}　" +
                            $"{_clock.TrialIndex + 1} 条件目 = {CooldownTestPlan.LabelOf(CurrentPreset)}　" +
                            $"（適用中 {(input != null ? input.CooldownSeconds : 0f):0.00}s）");

            if (_clock.IsBlock)
            {
                GUILayout.Label($"受理 {_accepted}　CD却下 {_rejected}　" +
                                (_clock.Block == CooldownBlock.Pair
                                    ? $"成立 {_pairCompleted} 組　{_pairIntervals.Describe()}"
                                    : ""));
            }

            GUILayout.Space(4f);
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);
            if (_summary != null)
            {
                GUILayout.Label($"集計（{_summary.Rows} 行" +
                                (_summary.Incomplete > 0 ? $"・未入力 {_summary.Incomplete} 行" : "") + "）: " +
                                (_summary.Passed ? "合格" : "未達 — " + _summary.FailureSummary()));
                foreach (CooldownValueResult v in _summary.Values) GUILayout.Label("　" + v.Describe());
                GUILayout.Space(2f);
                foreach (AbCriterion c in _summary.Criteria) GUILayout.Label("　" + c);
                if (_summary.NeedsDetectorChange)
                    GUILayout.Label("　→ どの値も両方を満たさない。片方だけを優先せず、角度閾値・ピーク検出・ヒステリシスを変える");
            }
            GUILayout.EndScrollView();

            GUILayout.Label($"集計: {_csvPath}");
            GUILayout.Label($"生ログ: {_eventPath}");
            GUILayout.Label("Space 開始/次へ　Tab このパネル　V 同期マーク　L CSV 読み直し");
            GUILayout.EndArea();
        }

        string OrderText()
        {
            int orderIndex = CooldownTestPlan.OrderIndexOf(participantNo);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < CooldownTestPlan.ConditionCount; i++)
            {
                if (i > 0) sb.Append('→');
                sb.Append(CooldownTestPlan.LabelOf(CooldownTestPlan.ConditionAt(orderIndex, i)));
            }
            return sb.ToString();
        }
    }
}
