using System;
using System.Collections.Generic;
using UnityEngine;
using Toufuku.Aim;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /// <summary>
    /// T0-3M 標準耐久テストの進行と記録 — Issue #53（仕様書 v8 17章）
    ///
    /// 1 人分の流れ: 準備 → 3 分振り続ける → 着弾待ち 1 秒 → 直後の聞き取り → 保存。
    ///
    /// ・クールダウンは T0-CD の採用値（<see cref="adoptedCooldown"/>）、フィードバックは T0-A/B の採用案
    ///   （同じ GameObject の <see cref="FeedbackTimingShifter"/>）で固定する。テスト中は変えない。
    /// ・1 分ごとの投数・命中率と実操作周期はゲームが自動で数える。
    /// ・持ち替え・腕の下がり・終了希望は、実施者が見ながらキーで入れる（G / D / E）。
    /// ・疲労・痛み・恐怖・ストラップ逸脱・実際の再挑戦選択は、終わった直後の記録画面で入れる。
    /// ・記録は 1 人 1 行の CSV（<see cref="EnduranceCsvFile"/>）。
    ///
    /// 操作: Space = 開始 / 保存 / 次の参加者、E = 終了希望（途中終了）、G = 持ち替え、D = 腕の下がり、
    /// Tab = 実施者パネル、L = CSV を読み直して集計。
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class EnduranceTestDirector : MonoBehaviour
    {
        [Header("テスト")]
        [SerializeField] string testId = "T0-3M-1";
        [Tooltip("責任者（19章のテスト記録に残す）")]
        [SerializeField] string owner = "";
        [SerializeField, Min(1)] int participantNo = 1;
        [SerializeField, Min(1)] int plannedParticipants = EnduranceTestPlan.DefaultParticipants;

        [Header("固定する条件（T0-A/B・T0-CD の採用値。テスト中は変えない）")]
        [Tooltip("T0-CD（#50）の採用値。F1〜F4 の手動切替は切ってある")]
        [SerializeField] CooldownPreset adoptedCooldown = EnduranceTestPlan.DefaultCooldown;
        [Tooltip("T0-A/B（#49）の採用案の名前（記録用）。時刻差そのものは FeedbackTimingShifter に入れる")]
        [SerializeField] string feedbackLabel = "案A";

        [Header("時間")]
        [SerializeField, Min(10f)] float trialSeconds = EnduranceTestPlan.TrialSeconds;
        [SerializeField, Min(0f)] float settleSeconds = EnduranceTestPlan.SettleSeconds;

        [Header("参照（未設定ならシーン内から探す）")]
        [SerializeField] ThrowInputController input;

        [Header("記録")]
        [Tooltip("空ならプロジェクト直下の PlaytestLogs/（ビルドでは persistentDataPath）")]
        [SerializeField] string csvFolder = "";
        [Tooltip("同じテストID・同じ日付のファイルがあれば読み込んで続きの参加者番号から始める")]
        [SerializeField] bool loadExistingOnStart = true;

        [Header("表示")]
        [SerializeField] bool showOperatorPanel = true;
        [Tooltip("フレーム上限を外して発射時刻の丸めを小さくする（実操作周期の精度）")]
        [SerializeField] bool uncapFrameRate = true;

        readonly EnduranceTrialClock _clock = new EnduranceTrialClock();
        readonly EnduranceThrowLog _log = new EnduranceThrowLog();
        readonly List<EnduranceRecord> _saved = new List<EnduranceRecord>();

        EnduranceRecord _record;
        EnduranceTestSummary _summary;

        string _csvPath = "";
        string _message = "";
        bool _subscribed;
        /// <summary>この試技の自動の値を記録へ写したか（記録フェーズに入ったとき 1 回だけ写す）。</summary>
        bool _trialCopied;

        GUIStyle _bannerStyle;
        GUIStyle _subStyle;
        Vector2 _panelScroll;

        public EndurancePhase Phase => _clock.Phase;
        public EnduranceRecord Record => _record;
        public EnduranceTestSummary Summary => _summary;
        public EnduranceThrowLog Log => _log;
        public IReadOnlyList<EnduranceRecord> SavedRecords => _saved;
        public string CsvPath => _csvPath;

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            if (input == null) input = FindAnyObjectByType<ThrowInputController>();
            if (input == null)
                Debug.LogWarning("[T0-3M] ThrowInputController がありません（投数を数えられません）", this);

            _clock.TrialSeconds = trialSeconds;
            _clock.SettleSeconds = settleSeconds;

            if (uncapFrameRate)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 0;
            }
            Input.imeCompositionMode = IMECompositionMode.On;

            _csvPath = EnduranceCsvFile.PathOf(csvFolder, testId, DateTime.Now.ToString("yyyy-MM-dd"));
            if (loadExistingOnStart) Reload();
            NewRecord();
            ApplyCooldown();
        }

        void OnEnable()
        {
            Subscribe();
            OnPhaseChanged();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) showOperatorPanel = !showOperatorPanel;
            if (Input.GetKeyDown(KeyCode.L)) { Reload(); _message = $"{_saved.Count} 行を読み直しました"; }

            // 記録画面では文字入力（所見）があるので、1 文字キーは試技中だけ受け付ける
            if (_clock.IsTrial)
            {
                if (Input.GetKeyDown(KeyCode.E)) RequestStop();
                if (Input.GetKeyDown(KeyCode.G)) MarkGripChange();
                if (Input.GetKeyDown(KeyCode.D)) MarkArmDrop();
            }
            // 記録画面では所見の入力中に空白を打てるよう、Space で保存するのは入力欄にフォーカスが無いときだけ
            if (Input.GetKeyDown(KeyCode.Space) &&
                (_clock.Phase != EndurancePhase.Record || GUIUtility.keyboardControl == 0))
                HandleSpace();

            if (_clock.Advance(Now)) OnPhaseChanged();
        }

        void HandleSpace()
        {
            switch (_clock.Phase)
            {
                case EndurancePhase.Ready: Begin(); break;
                case EndurancePhase.Record: Save(); break;
                case EndurancePhase.Done: NextParticipant(); break;
            }
        }

        // ---- 進行 ----

        /// <summary>3 分を始める。</summary>
        public void Begin()
        {
            if (_clock.Phase != EndurancePhase.Ready) return;
            ApplyCooldown();
            _log.Clear();
            NewRecord();
            _message = "";
            _clock.Begin(Now);
            OnPhaseChanged();
            Debug.Log($"[T0-3M] 参加者 {participantNo} 開始（クールダウン {CurrentCooldownSeconds:0.00}s / {feedbackLabel}）", this);
        }

        /// <summary>終了希望（または安全のための中止）。完走していない記録になる。</summary>
        public void RequestStop()
        {
            if (!_clock.StopEarly(Now)) return;
            Debug.Log($"[T0-3M] 参加者 {participantNo} 終了希望 {_clock.EndSeconds:0.0}s", this);
            OnPhaseChanged();
        }

        public void MarkGripChange()
        {
            if (!_clock.IsTrial || _record == null) return;
            _record.gripChanges++;
        }

        public void MarkArmDrop()
        {
            if (!_clock.IsTrial || _record == null) return;
            _record.MarkArmDrop(_clock.TrialElapsed(Now));
        }

        void OnPhaseChanged()
        {
            if (_clock.Phase == EndurancePhase.Record && _record != null && !_trialCopied)
            {
                _record.SetFromLog(_log, _clock.EndSeconds, _clock.Completed);
                _record.cooldownSeconds = CurrentCooldownSeconds;
                _trialCopied = true;
            }

            // 3 分のあいだだけ投擲を受け付ける（着弾待ち・記録中は振っても何も起きない）
            if (input != null) input.enabled = _clock.IsTrial;
        }

        float CurrentCooldownSeconds => input != null ? input.CooldownSeconds : CooldownTestPlan.SecondsOf(adoptedCooldown);

        void ApplyCooldown()
        {
            if (input == null) return;
            input.Preset = adoptedCooldown;
            // 入力（-100）のほうが先に動くので、状態機械へも直接入れて最初のフレームから採用値で判定させる
            if (input.Machine != null) input.Machine.CooldownSeconds = input.CooldownSeconds;
        }

        void NewRecord()
        {
            _trialCopied = false;
            _record = new EnduranceRecord
            {
                testId = testId,
                owner = owner,
                date = DateTime.Now.ToString("yyyy-MM-dd"),
                participantNo = participantNo,
                cooldownSeconds = CurrentCooldownSeconds,
                feedbackLabel = feedbackLabel,
                plannedSeconds = trialSeconds
            };
        }

        void Save()
        {
            if (_record == null || _clock.Phase != EndurancePhase.Record) return;
            _record.testId = testId;
            _record.owner = owner;
            _record.feedbackLabel = feedbackLabel;

            if (!EnduranceCsvFile.Append(_csvPath, _record, out string error))
            {
                _message = $"保存できませんでした: {error}";
                return;
            }

            _saved.Add(_record.Clone());
            RefreshSummary();

            string missing = _record.MissingFields();
            _message = $"参加者 {_record.participantNo} を保存しました" +
                       (string.IsNullOrEmpty(missing) ? "" : $"（{missing} が未記入。あとから CSV へ）");
            Debug.Log($"[T0-3M] {_message} → {_csvPath}", this);

            _clock.FinishRecord();
            OnPhaseChanged();
        }

        void NextParticipant()
        {
            participantNo++;
            _clock.Reset();
            _log.Clear();
            NewRecord();
            _message = "";
            OnPhaseChanged();
        }

        /// <summary>CSV を読み直して集計しなおす（聞き取りの列を Excel で埋めたあと）。</summary>
        public void Reload()
        {
            _saved.Clear();
            _saved.AddRange(EnduranceCsvFile.Load(_csvPath));
            RefreshSummary();

            if (_saved.Count == 0 || _clock.Phase != EndurancePhase.Ready) return;
            participantNo = Mathf.Max(participantNo, _saved[_saved.Count - 1].participantNo + 1);
            if (_record != null) _record.participantNo = participantNo;
            _message = $"{_saved.Count} 行を読み込みました（参加者 {participantNo} から）";
        }

        void RefreshSummary()
        {
            _summary = EnduranceTestSummary.Of(_saved, plannedParticipants);
        }

        // ---- 計測 ----

        void Subscribe()
        {
            if (_subscribed) return;
            if (input != null)
            {
                input.SwingAccepted += HandleSwingAccepted;
                input.SwingRejected += HandleSwingRejected;
            }
            OmamoriProjectile.AnyLanded += HandleLanded;
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
            OmamoriProjectile.AnyLanded -= HandleLanded;
            _subscribed = false;
        }

        void HandleSwingAccepted(SwingAcceptedArgs e)
        {
            if (!_clock.IsTrial) return;
            // 発射確定を配ったこのフレームの時刻を、試技開始からの秒で持つ
            _log.AddFire(_clock.SinceTrialStart(Now));
        }

        void HandleSwingRejected(SwingRejectedArgs e)
        {
            if (_clock.IsTrial && e.Reason == SwingRejectReason.Cooldown) _log.AddRejected();
        }

        void HandleLanded(LandingResult result)
        {
            if (_clock.Phase != EndurancePhase.Trial && _clock.Phase != EndurancePhase.Settle) return;
            _log.AddLanding(_clock.SinceTrialStart(Now), result.ElapsedSeconds, result.Hit != null);
        }

        // ---- 表示 ----

        void OnGUI()
        {
            EnsureStyles();
            DrawBanner();

            if (_clock.Phase == EndurancePhase.Record) DrawRecordPanel();
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
                _subStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        }

        void DrawBanner()
        {
            // 参加者には投数・命中率を見せない（数字を追うと振り方が変わる）。見せるのは残り時間だけ
            string title;
            string sub;
            switch (_clock.Phase)
            {
                case EndurancePhase.Ready:
                    title = "準備";
                    sub = "実施者が Space で開始します";
                    break;
                case EndurancePhase.Trial:
                    title = $"のこり {Clock(_clock.RemainingSeconds(Now))}";
                    sub = "的をねらって、ふり続けてください（やめたくなったら言ってください）";
                    break;
                case EndurancePhase.Settle:
                    title = "おしまい";
                    sub = "大幣を置いてください";
                    break;
                case EndurancePhase.Record:
                    title = "おつかれさまでした";
                    sub = "実施者が記録します";
                    break;
                default:
                    title = "記録しました";
                    sub = string.IsNullOrEmpty(_message) ? "Space で次の参加者へ" : _message;
                    break;
            }

            var rect = new Rect(0f, 16f, Screen.width, 46f);
            GUI.Label(rect, title, _bannerStyle);
            GUI.Label(new Rect(0f, rect.yMax, Screen.width, 26f), sub, _subStyle);
        }

        void DrawRecordPanel()
        {
            const float width = 600f;
            const float height = 520f;
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"記録 — 参加者 {_record.participantNo}　" +
                            (_record.completed ? "3 分完走" : $"終了希望 {_record.endSeconds:0.0}s") +
                            $"　クールダウン {_record.cooldownSeconds:0.00}s　{_record.feedbackLabel}");

            GUILayout.Label(MinuteText(_record));
            GUILayout.Label($"最終 1 分の投数低下: {Percent(_record.ThrowsDropRatio)}　CD却下 {_record.rejected}");
            GUILayout.Label($"実操作周期: n={_record.CycleCount} 中央 {Sec(_record.CycleMedian)} p75 {Sec(_record.CycleP75)}");

            GUILayout.Space(6f);
            GUILayout.Label("試技中の観察（G / D キーで入れた値。ここで直してもよい）");
            GUILayout.BeginHorizontal();
            GUILayout.Label("持ち替え", GUILayout.Width(70f));
            _record.gripChanges = Counter(_record.gripChanges);
            GUILayout.Space(16f);
            GUILayout.Label("腕の下がり", GUILayout.Width(80f));
            _record.armDrops = Counter(_record.armDrops);
            GUILayout.Label(_record.firstArmDropSec >= 0f ? $"初回 {_record.firstArmDropSec:0}s" : "");
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("直後に聞く: 「腕や肩はどれくらい疲れた？ 1 まったく 〜 5 とても」");
            GUILayout.BeginHorizontal();
            GUILayout.Label("疲労", GUILayout.Width(70f));
            for (int i = EnduranceTestPlan.MinScale; i <= EnduranceTestPlan.MaxScale; i++)
            {
                bool on = GUILayout.Toggle(_record.fatigue == i, i.ToString(), GUI.skin.button, GUILayout.Width(44f));
                if (on && _record.fatigue != i) _record.fatigue = i;
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("安全（1 件でもあれば、原因を直すまで次の人へ進まない）");
            GUILayout.BeginHorizontal();
            _record.pain = GUILayout.Toggle(_record.pain, "痛み", GUILayout.Width(90f));
            _record.fear = GUILayout.Toggle(_record.fear, "恐怖", GUILayout.Width(90f));
            _record.strapDeviation = GUILayout.Toggle(_record.strapDeviation, "ストラップ逸脱", GUILayout.Width(140f));
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label("「もう一回やる？ それとも別の遊びにする？」— 実際に選んだほうを押す");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_record.retryAnswered && _record.retry, "もう一度", GUI.skin.button, GUILayout.Width(140f)))
                _record.SetRetry(true);
            if (GUILayout.Toggle(_record.retryAnswered && !_record.retry, "別の遊びへ", GUI.skin.button, GUILayout.Width(140f)))
                _record.SetRetry(false);
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label("所見（表情・第一声・持ち方など）");
            _record.note = GUILayout.TextField(_record.note ?? "", GUILayout.Height(22f));

            GUILayout.Space(6f);
            string missing = _record.MissingFields();
            if (GUILayout.Button(string.IsNullOrEmpty(missing) ? "記録する" : $"記録する（{missing} が未記入）", GUILayout.Height(30f)))
                Save();
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message);

            GUILayout.EndArea();
        }

        void DrawOperatorPanel()
        {
            const float width = 540f;
            var area = new Rect(10f, 10f, width, Mathf.Min(460f, Screen.height - 20f));

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"[T0-3M #53] {testId}　{DateTime.Now:yyyy-MM-dd}　" +
                            $"責任者 {(string.IsNullOrEmpty(owner) ? "未記入" : owner)}");
            GUILayout.Label($"参加者 {participantNo} / 予定 {plannedParticipants} 人　" +
                            $"クールダウン {CurrentCooldownSeconds:0.00}s（採用 {CooldownTestPlan.LabelOf(adoptedCooldown)}）　{feedbackLabel}");
            GUILayout.Label($"今: {EnduranceTrialClock.LabelOf(_clock.Phase)}　経過 {Clock(_clock.TrialElapsed(Now))}");

            if (_clock.IsTrial || _clock.Phase == EndurancePhase.Settle)
            {
                GUILayout.Label(LiveMinuteText());
                GUILayout.Label($"周期 中央 {Sec(_log.CycleMedian)} p75 {Sec(_log.CycleP75)}　" +
                                $"持ち替え {_record.gripChanges}　腕の下がり {_record.armDrops}");
            }

            GUILayout.Space(4f);
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);
            if (_summary != null)
            {
                GUILayout.Label($"集計（{_summary.Count} 人" +
                                (_summary.Incomplete > 0 ? $"・未記入 {_summary.Incomplete} 行" : "") + "）: " +
                                (_summary.Passed ? "合格" : "未達 — " + _summary.FailureSummary()));
                foreach (AbCriterion c in _summary.Criteria) GUILayout.Label("　" + c);
                GUILayout.Label("　" + _summary.CycleDescribe());
                foreach (WaveCandidateCheck w in _summary.Waves) GUILayout.Label("　　" + w.Describe(_summary.CycleP75));
            }
            GUILayout.EndScrollView();

            GUILayout.Label($"記録: {_csvPath}");
            GUILayout.Label("Space 開始/記録/次へ　E 終了希望　G 持ち替え　D 腕の下がり　Tab パネル　L 読み直し");
            GUILayout.EndArea();
        }

        string LiveMinuteText()
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < EnduranceTestPlan.MinuteCount; k++)
            {
                if (k > 0) sb.Append("　");
                int landings = _log.LandingsIn(k);
                string rate = landings > 0 ? $"{(double)_log.HitsIn(k) / landings:P0}" : "-";
                sb.Append($"{k + 1}分目 {_log.ThrowsIn(k)}投 {rate}");
            }
            return sb.ToString();
        }

        static string MinuteText(EnduranceRecord r)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < EnduranceTestPlan.MinuteCount; k++)
            {
                if (k > 0) sb.Append("　");
                int? throws = r.ThrowsOf(k);
                sb.Append($"{k + 1}分目 ");
                sb.Append(throws.HasValue ? $"{throws.Value}投 命中 {Percent(r.HitRateOf(k))}" : "未到達");
            }
            return sb.ToString();
        }

        static int Counter(int value)
        {
            if (GUILayout.Button("-", GUILayout.Width(26f)) && value > 0) value--;
            GUILayout.Label(value.ToString(), GUILayout.Width(26f));
            if (GUILayout.Button("+", GUILayout.Width(26f))) value++;
            return value;
        }

        static string Clock(double seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        static string Percent(double? value) => value.HasValue ? $"{value.Value * 100.0:0.#}%" : "-";

        static string Sec(double? value) => value.HasValue ? $"{value.Value:0.000}s" : "-";
    }
}
