using System.Collections.Generic;
using UnityEngine;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /*
        おもちゃの手ごたえの A/B テストを進めて記録するクラス（#49 / 企画書 v8 17章）

        1人ぶんの流れ: 準備 → 1回目45秒 → 休けい60秒 → 2回目45秒 → 聞き取り → 保存
        ・参加者には「案1 / 案2」としか見せない（どっちが案Aかはかくす）
        ・参加者の番号が奇数なら A→B、偶数なら B→A（AbTestPlan.OrderOf）
        ・自分から振った回数は有効スイングの数。参加者に見せると回数を気にしてしまうので、やる人のパネル（Tab）にだけ出す
        ・休けいと聞き取りの間は ThrowInputController を止めるので、振っても何も起きない
        ・保存は1人1行で CSV に足していく（AbTestCsvFile）。合格かどうかはその場で AbTestSummary が出す

        操作: Space = スタート / 次へ、Tab = やる人のパネル、Backspace = 今のフェーズをとばす
    */
    [DefaultExecutionOrder(-80)]
    public class ToyFeelAbDirector : MonoBehaviour
    {
        [Header("テスト")]
        [SerializeField] string testId = "T0-AB-1";
        [Tooltip("責任者（19章のテスト記録に残す）")]
        [SerializeField] string owner = "";
        [SerializeField, Min(1)] int participantNo = 1;
        [Tooltip("予定人数。合格ラインはこの人数に対する割合で判定する（10 人なら 8/10・7/10・2/10）")]
        [SerializeField, Min(1)] int plannedParticipants = AbTestPlan.DefaultParticipants;
        [SerializeField, Min(5f)] float trialSeconds = AbTestPlan.TrialSeconds;
        [SerializeField, Min(0f)] float restSeconds = AbTestPlan.RestSeconds;

        [Header("比べる 2 案（テスト前に固定する。変えるのは 3 つの時刻差だけ）")]
        [SerializeField] FeedbackVariant variantA = FeedbackVariant.DefaultA();
        [SerializeField] FeedbackVariant variantB = FeedbackVariant.DefaultB();

        [Header("参照（未設定ならシーン内から探す）")]
        [SerializeField] FeedbackTimingShifter shifter;
        [SerializeField] ThrowInputController input;

        [Header("記録")]
        [Tooltip("空ならプロジェクト直下の PlaytestLogs/（ビルドでは persistentDataPath）")]
        [SerializeField] string csvFolder = "";
        [Tooltip("同じテストID・同じ日付のファイルがあれば読み込んで集計を続ける")]
        [SerializeField] bool loadExistingOnStart = true;

        [Header("表示")]
        [SerializeField] bool showOperatorPanel = true;
        [Tooltip("参加者に見せる呼び名（1 回目 / 2 回目に出す案）")]
        [SerializeField] string firstLabel = "案1";
        [SerializeField] string secondLabel = "案2";
        [Tooltip("フレーム上限を外して時刻差の丸めを小さくする")]
        [SerializeField] bool uncapFrameRate = true;

        readonly AbSessionClock _clock = new AbSessionClock();
        readonly List<AbParticipantRecord> _saved = new List<AbParticipantRecord>();

        AbParticipantRecord _record;
        AbTestSummary _summary;
        VariantId _countingVariant;
        bool _counting;
        int _accepted;
        int _rejected;
        string _csvPath = "";
        string _message = "";
        bool _subscribed;

        GUIStyle _bannerStyle;
        GUIStyle _subStyle;

        public AbPhase Phase => _clock.Phase;
        public AbParticipantRecord Record => _record;
        public AbTestSummary Summary => _summary;
        public IReadOnlyList<AbParticipantRecord> SavedRecords => _saved;
        public string CsvPath => _csvPath;

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            if (shifter == null) shifter = FindAnyObjectByType<FeedbackTimingShifter>();
            if (input == null) input = FindAnyObjectByType<ThrowInputController>();
            if (shifter == null)
                Debug.LogWarning("[T0-A/B] FeedbackTimingShifter がありません（案の切り替えができません）", this);

            _clock.TrialSeconds = trialSeconds;
            _clock.RestSeconds = restSeconds;

            if (uncapFrameRate)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 0;
            }
            Input.imeCompositionMode = IMECompositionMode.On;

            NewRecord();
            _csvPath = AbTestCsvFile.PathOf(csvFolder, testId, _record.date);
            if (loadExistingOnStart) _saved.AddRange(AbTestCsvFile.Load(_csvPath));
            if (_saved.Count > 0)
            {
                participantNo = _saved[_saved.Count - 1].participantNo + 1;
                NewRecord();
                _message = $"{_saved.Count} 人分を読み込みました";
            }
            RefreshSummary();

            int differences = FeedbackVariant.CountDifferences(variantA, variantB);
            if (differences == 0)
                Debug.LogWarning("[T0-A/B] 案A と案B が同じ内容です。比べる時刻差を決めてから始めてください", this);
            else
                Debug.Log($"[T0-A/B] 案A {variantA.Describe()} / 案B {variantB.Describe()}（違い {differences} 項目）", this);
        }

        void OnEnable()
        {
            Subscribe();
            EnterPhase(_clock.Phase);
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab)) showOperatorPanel = !showOperatorPanel;
            if (Input.GetKeyDown(KeyCode.Space)) HandleSpace();
            if (Input.GetKeyDown(KeyCode.Backspace) && _clock.Skip(Now)) EnterPhase(_clock.Phase);

            if (_clock.Advance(Now)) EnterPhase(_clock.Phase);
        }

        void HandleSpace()
        {
            switch (_clock.Phase)
            {
                case AbPhase.Ready:
                    _clock.Begin(Now);
                    EnterPhase(_clock.Phase);
                    break;
                case AbPhase.Done:
                    NextParticipant();
                    break;
            }
        }

        // ---- フェーズ ----

        void EnterPhase(AbPhase phase)
        {
            // さっきまで本番だったら、その案の投げた数を記録にうつす
            if (_counting && phase != AbPhase.FirstTrial && phase != AbPhase.SecondTrial)
            {
                _record.SetThrows(_countingVariant, _accepted, _rejected);
                _counting = false;
            }

            int trialIndex = _clock.TrialIndex;
            if (trialIndex >= 0)
            {
                _countingVariant = AbTestPlan.VariantAt(_record.order, trialIndex);
                _counting = true;
                _accepted = 0;
                _rejected = 0;
                ApplyVariant(_countingVariant);
            }

            if (input != null) input.enabled = trialIndex >= 0;
        }

        void ApplyVariant(VariantId id)
        {
            if (shifter == null) return;
            FeedbackVariant variant = id == VariantId.A ? variantA : variantB;
            shifter.ResetCounters();
            shifter.Variant = (variant ?? FeedbackVariant.DefaultA()).Clone();
        }

        void NewRecord()
        {
            _record = new AbParticipantRecord
            {
                testId = testId,
                owner = owner,
                date = System.DateTime.Now.ToString("yyyy-MM-dd"),
                participantNo = participantNo,
                order = AbTestPlan.OrderOf(participantNo)
            };
            _accepted = 0;
            _rejected = 0;
            _counting = false;
        }

        void NextParticipant()
        {
            participantNo++;
            NewRecord();
            _clock.Reset();
            EnterPhase(_clock.Phase);
            _message = "";
        }

        void Save()
        {
            _record.testId = testId;
            _record.owner = owner;

            if (!_record.IsComplete)
            {
                _message = $"未記入: {_record.MissingFields()}";
                return;
            }

            if (!AbTestCsvFile.Append(_csvPath, _record, out string error))
            {
                _message = $"保存できませんでした: {error}";
                return;
            }

            _saved.Add(_record.Clone());
            RefreshSummary();
            _clock.FinishSurvey();
            _message = $"参加者 {_record.participantNo} を保存しました（{_saved.Count}/{plannedParticipants} 人）";
            Debug.Log($"[T0-A/B] {_message} → {_csvPath}", this);
        }

        void RefreshSummary()
        {
            _summary = AbTestSummary.Of(_saved, plannedParticipants);
        }

        // ---- 投げた数 ----

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
            if (_counting) _accepted++;
        }

        void HandleSwingRejected(SwingRejectedArgs e)
        {
            if (_counting && e.Reason == SwingRejectReason.Cooldown) _rejected++;
        }

        // ---- 表示 ----

        // 参加者に見せる呼び名（1回目 = firstLabel）
        public string SlotLabel(int slot) => slot == 0 ? firstLabel : secondLabel;

        // その呼び名がどの案か
        public VariantId VariantOfSlot(int slot) => AbTestPlan.VariantAt(_record.order, slot);

        void OnGUI()
        {
            EnsureStyles();
            DrawBanner();

            if (_clock.Phase == AbPhase.Survey) DrawSurvey();
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
                _subStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }

        void DrawBanner()
        {
            string title;
            string sub;
            switch (_clock.Phase)
            {
                case AbPhase.Ready:
                    title = "準備";
                    sub = "実施者が Space で開始します";
                    break;
                case AbPhase.FirstTrial:
                case AbPhase.SecondTrial:
                    title = $"{SlotLabel(_clock.TrialIndex)}　残り {_clock.RemainingSeconds(Now):0} 秒";
                    sub = "好きなだけ振ってください";
                    break;
                case AbPhase.Rest:
                    title = $"休憩　残り {_clock.RemainingSeconds(Now):0} 秒";
                    sub = "大幣を置いて休んでください";
                    break;
                case AbPhase.Survey:
                    title = "聞き取り";
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

        void DrawSurvey()
        {
            const float width = 560f;
            const float height = 470f;
            var area = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"聞き取り — 参加者 {_record.participantNo}（{AbTestPlan.LabelOf(_record.order)}）");

            for (int slot = 0; slot < 2; slot++)
            {
                VariantId id = VariantOfSlot(slot);
                GUILayout.Space(4f);
                GUILayout.Label($"【{SlotLabel(slot)}】");

                GUILayout.BeginHorizontal();
                GUILayout.Label("手応え（5 段階）", GUILayout.Width(140f));
                for (int feel = AbParticipantRecord.MinFeel; feel <= AbParticipantRecord.MaxFeel; feel++)
                {
                    bool on = _record.FeelOf(id) == feel;
                    if (GUILayout.Toggle(on, feel.ToString(), GUI.skin.button, GUILayout.Width(36f)) && !on)
                        _record.SetFeel(id, feel);
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("すぐもう一度振りたい", GUILayout.Width(140f));
                bool answered = _record.AgainAnsweredOf(id);
                bool yes = answered && _record.AgainOf(id);
                bool no = answered && !_record.AgainOf(id);
                if (GUILayout.Toggle(yes, "はい", GUI.skin.button, GUILayout.Width(70f)) && !yes)
                    _record.SetAgain(id, true);
                if (GUILayout.Toggle(no, "いいえ", GUI.skin.button, GUILayout.Width(70f)) && !no)
                    _record.SetAgain(id, false);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("採用案（必ずどちらか）", GUILayout.Width(140f));
            for (int slot = 0; slot < 2; slot++)
            {
                VariantId id = VariantOfSlot(slot);
                bool on = _record.adoptedAnswered && _record.adopted == id;
                if (GUILayout.Toggle(on, SlotLabel(slot), GUI.skin.button, GUILayout.Width(100f)) && !on)
                    _record.SetAdopted(id);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("理由（そのままの言葉で）");
            _record.reason = GUILayout.TextField(_record.reason ?? "", GUILayout.Height(24f));

            GUILayout.Label("表情・声のメモ");
            _record.note = GUILayout.TextField(_record.note ?? "", GUILayout.Height(24f));

            GUILayout.Space(4f);
            _record.syncComplaint = GUILayout.Toggle(_record.syncComplaint, "映像・音・振動のずれを指摘した");

            GUILayout.BeginHorizontal();
            GUILayout.Label("安全", GUILayout.Width(40f));
            _record.pain = GUILayout.Toggle(_record.pain, "痛み", GUILayout.Width(80f));
            _record.fear = GUILayout.Toggle(_record.fear, "恐怖", GUILayout.Width(80f));
            _record.strapDeviation = GUILayout.Toggle(_record.strapDeviation, "ストラップ逸脱", GUILayout.Width(140f));
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            if (GUILayout.Button("記録して次へ", GUILayout.Height(30f))) Save();
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message);

            GUILayout.EndArea();
        }

        void DrawOperatorPanel()
        {
            const float width = 470f;
            var area = new Rect(10f, 10f, width, 330f);

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label($"[T0-A/B #49] {testId}　{_record.date}　責任者 {(string.IsNullOrEmpty(owner) ? "未記入" : owner)}");
            GUILayout.Label($"参加者 {_record.participantNo}（{AbTestPlan.LabelOf(_record.order)}）" +
                            $"　{SlotLabel(0)}={AbTestPlan.LabelOf(VariantOfSlot(0))}　{SlotLabel(1)}={AbTestPlan.LabelOf(VariantOfSlot(1))}");
            GUILayout.Label($"案A {variantA.Describe()}");
            GUILayout.Label($"案B {variantB.Describe()}");

            if (_counting)
            {
                GUILayout.Label($"試技中: {AbTestPlan.LabelOf(_countingVariant)}　自発投数 {_accepted}（CD 却下 {_rejected}）");
                if (shifter != null)
                    GUILayout.Label($"実測 SE {shifter.LastOffsetMs(FeedbackChannel.ThrowSe):0} / " +
                                    $"軌跡 {shifter.LastOffsetMs(FeedbackChannel.Trail):0} / " +
                                    $"振動 {shifter.LastOffsetMs(FeedbackChannel.Haptic):0} ms" +
                                    $"（丸め最大 {Mathf.Max(shifter.MaxErrorMs(FeedbackChannel.ThrowSe), shifter.MaxErrorMs(FeedbackChannel.Haptic)):0} ms）");
            }
            else
            {
                GUILayout.Label($"案A 投数 {_record.throwsA}　案B 投数 {_record.throwsB}");
            }

            GUILayout.Space(4f);
            if (_summary != null)
            {
                GUILayout.Label($"集計（{_summary.Count}/{plannedParticipants} 人）: " +
                                (_summary.Passed ? "合格" : "未達 — " + _summary.FailureSummary()));
                foreach (AbCriterion c in _summary.Criteria)
                    GUILayout.Label("　" + c);
            }

            GUILayout.Space(4f);
            GUILayout.Label($"保存先: {_csvPath}");
            GUILayout.Label("Space 開始/次へ　Tab このパネル　Backspace フェーズを飛ばす");
            GUILayout.EndArea();
        }
    }
}
