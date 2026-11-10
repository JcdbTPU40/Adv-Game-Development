using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Toufuku.Rescue;

namespace Toufuku.Tutorial
{
    /*
        開始30秒の段階学習に、巫女の絵と吹き出しの「声」をのせるクラス

        ・遊びは止めない。吹き出しは1〜2行で、今光っている客やボタンを言葉で指すだけ
          （段階の進み方・客の置き方・ゴーストは StagedLearningDirector がこれまでどおり決める）
        ・吹き出しは2種類:
            段階の文（いまの段階で何をするか。次の段階が始まるまで出しておく）
            短い文（あいさつ・ほめる・色ちがい・ゴーストの合図。数秒で段階の文にもどる）
        ・開始の「参・弐・壱・始め！」（SessionEndCountdownTMP.PlayStartCountdown）は、
          競技が始まる時刻（0:30、見抜くを達成したらその自由練習の終わり）にちょうど「始め！」が出るように鳴らす
          ※ TimeUpImageDisplay と同じ GameObject に SessionEndCountdownTMP があると、襖が開いた直後にも鳴ってしまう。
            このシーンでは SessionEndCountdownTMP を別の GameObject に置いて、ここから呼ぶ
        ・競技が始まったら、巫女は画面の左へ下がる

        Canvas はコードで組み立てるので、空の GameObject に付けて巫女の絵とフォントを入れるだけで動く
    */
    [DisallowMultipleComponent]
    public class MikoGuideView : MonoBehaviour
    {
        [Header("参照（未設定ならシーンから探す）")]
        [SerializeField] StagedLearningDirector director;
        [Tooltip("開始の参・弐・壱を鳴らす部品。TimeUpImageDisplay とは別の GameObject に置く。")]
        [SerializeField] SessionEndCountdownTMP startCountdown;

        [Header("見た目")]
        [SerializeField] Sprite mikoSprite;
        [Tooltip("NotoSansJP の .ttf。未設定なら組み込みフォント。")]
        [SerializeField] Font font;
        [SerializeField] int sortingOrder = 65;
        [Tooltip("巫女の絵の高さ（1080 基準のピクセル）。")]
        [SerializeField, Min(100f)] float mikoHeight = 560f;
        [Tooltip("画面の左下から巫女の足もとまでのずれ（1080 基準）。")]
        [SerializeField] Vector2 mikoOffset = new Vector2(20f, -40f);
        [SerializeField, Min(10f)] float fontSize = 40f;
        [SerializeField] Color bubbleColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] Color bubbleBorderColor = new Color(0.64f, 0.18f, 0.18f, 1f);
        [SerializeField] Color textColor = new Color(0.17f, 0.17f, 0.16f, 1f);

        [Header("時間（秒）")]
        [SerializeField, Min(0f)] float slideSeconds = 0.4f;
        [SerializeField, Min(0f)] float greetingSeconds = 2.2f;
        [SerializeField, Min(0f)] float praiseSeconds = 1.4f;
        [SerializeField, Min(0f)] float hintSeconds = 2.2f;
        [Tooltip("競技が始まってから巫女が下がるまで。")]
        [SerializeField, Min(0f)] float exitDelaySeconds = 1.2f;
        [Tooltip("参・弐・壱 の長さ。SessionEndCountdownTMP の startCountdownStepDuration × 3 に合わせる。")]
        [SerializeField, Min(0f)] float countdownLeadSeconds = 3f;

        [Header("せりふ")]
        [SerializeField] string greetingLine = "ようこそ、投福神社へ！\n参拝のみなさんに福を届けましょう";
        [SerializeField] string deliverLine = "光っている人に照準を合わせて\n大幣を振ってね";
        [SerializeField] string chooseLine = "願いの色と同じお守りのボタンを\n選んでから振ってね";
        [SerializeField] string discernLine = "足もとに金色の二重の輪がある人は\n急いでいるよ。先に助けてあげて！";
        [SerializeField] string freePracticeLine = "その調子！\n本番まで自由に練習してね";
        [SerializeField] string countdownLine = "では本番です。がんばって！";
        [SerializeField] string praiseLine = "その調子！";
        [SerializeField] string priorityPraiseLine = "よくできました！";
        [SerializeField] string wrongColorLine = "色が違うよ\n願いの色をよく見てね";
        [SerializeField] string swingHintLine = "大幣を大きく振ってみて！";
        [SerializeField] string aimHintLine = "照準を光っている人に合わせてね";
        [SerializeField] string colorHintLine = "願いの色と同じボタンを押してね";
        [SerializeField] string priorityHintLine = "金色の二重の輪の人を先に助けてね";

        GameSession _session;
        StagedLearningPlan _plan;

        RectTransform _root;
        RectTransform _miko;
        RectTransform _bubble;
        Text _text;

        string _standingLine;
        string _transientLine;
        float _transientUntil;
        string _shownLine;

        bool _active;
        bool _countdownPlayed;
        bool _leaving;
        float _slide;          // 0: 画面の外、1: 定位置
        float _slideTarget;
        float _bubblePop;      // 吹き出しが出たときの小さなはずみ
        float _leaveAt = -1f;

        public string CurrentLine => _shownLine;
        public bool IsShown => _active && !_leaving;
        public bool CountdownPlayed => _countdownPlayed;

        void Awake()
        {
            BuildUi();
            SetVisible(false);
        }

        void Start()
        {
            if (director == null) director = FindAnyObjectByType<StagedLearningDirector>();
            if (startCountdown == null) startCountdown = FindAnyObjectByType<SessionEndCountdownTMP>();
            _session = GameSession.Instance;

            if (director == null || director.Plan == null)
            {
                Debug.LogWarning("[MikoGuide] StagedLearningDirector がないので巫女の案内は出しません。", this);
                return;
            }

            _plan = director.Plan;
            _plan.StageStarted += HandleStageStarted;
            _plan.StageEnded += HandleStageEnded;
            _plan.GhostRequested += HandleGhostRequested;
            if (director.onPriorityRescued != null) director.onPriorityRescued.AddListener(HandlePriorityRescued);
            OmamoriHitResolver.HitResolved += HandleHitResolved;

            if (_session != null)
            {
                if (_session.onSessionStart != null) _session.onSessionStart.AddListener(HandleSessionStart);
                if (_session.onSessionEnd != null) _session.onSessionEnd.AddListener(HandleSessionEnd);
                _session.CompetitionStarted += HandleCompetitionStarted;
                if (_session.IsPlaying && !_session.HasCompetitionStarted) HandleSessionStart();
            }
        }

        void OnDestroy()
        {
            if (_plan != null)
            {
                _plan.StageStarted -= HandleStageStarted;
                _plan.StageEnded -= HandleStageEnded;
                _plan.GhostRequested -= HandleGhostRequested;
            }
            if (director != null && director.onPriorityRescued != null) director.onPriorityRescued.RemoveListener(HandlePriorityRescued);
            OmamoriHitResolver.HitResolved -= HandleHitResolved;
            if (_session != null)
            {
                if (_session.onSessionStart != null) _session.onSessionStart.RemoveListener(HandleSessionStart);
                if (_session.onSessionEnd != null) _session.onSessionEnd.RemoveListener(HandleSessionEnd);
                _session.CompetitionStarted -= HandleCompetitionStarted;
            }
        }

        // ---------- できごと ----------

        void HandleSessionStart()
        {
            _active = true;
            _leaving = false;
            _countdownPlayed = false;
            _leaveAt = -1f;
            _standingLine = null;
            _slide = 0f;
            _slideTarget = 1f;
            SetVisible(true);
            Say(greetingLine, greetingSeconds);
        }

        void HandleSessionEnd()
        {
            _active = false;
            SetVisible(false);
        }

        void HandleStageStarted(LearningStage stage, double t)
        {
            switch (stage)
            {
                case LearningStage.Deliver: _standingLine = deliverLine; break;
                case LearningStage.Choose: _standingLine = chooseLine; break;
                case LearningStage.Discern: _standingLine = discernLine; break;
                case LearningStage.FreePractice: _standingLine = freePracticeLine; break;
            }
        }

        void HandleStageEnded(LearningStage stage, bool achieved, double t)
        {
            if (achieved && stage != LearningStage.Discern) Say(praiseLine, praiseSeconds);
        }

        void HandlePriorityRescued()
        {
            Say(priorityPraiseLine, praiseSeconds);
        }

        void HandleHitResolved(OmamoriHitInfo info)
        {
            if (!_active || director == null || !director.IsLearning) return;
            if (info.IsBlackCustomer || info.Customer == null || info.IsGoodHit) return;
            Say(wrongColorLine, hintSeconds);
        }

        void HandleGhostRequested(LearningGhost ghost, string reason, double t)
        {
            switch (ghost)
            {
                case LearningGhost.Swing: Say(swingHintLine, hintSeconds); break;
                case LearningGhost.Aim: Say(aimHintLine, hintSeconds); break;
                case LearningGhost.Color: Say(colorHintLine, hintSeconds); break;
                case LearningGhost.Priority: Say(priorityHintLine, hintSeconds); break;
            }
        }

        void HandleCompetitionStarted(double t)
        {
            // 参・弐・壱 が間に合わなかった（自由練習が 0:27 をすぎて始まったなど）ときも、せりふは本番にそろえる
            _standingLine = countdownLine;
            _transientLine = null;
            _leaveAt = Time.unscaledTime + exitDelaySeconds;
        }

        // ---------- 毎フレーム ----------

        void Update()
        {
            if (!_active) return;

            UpdateCountdown();

            if (_leaveAt >= 0f && Time.unscaledTime >= _leaveAt)
            {
                _leaveAt = -1f;
                _leaving = true;
                _slideTarget = 0f;
            }

            float step = slideSeconds > 0f ? Time.unscaledDeltaTime / slideSeconds : 1f;
            _slide = Mathf.MoveTowards(_slide, _slideTarget, step);
            if (_leaving && _slide <= 0f)
            {
                _active = false;
                SetVisible(false);
                return;
            }

            string line = _transientLine != null && Time.unscaledTime < _transientUntil ? _transientLine : _standingLine;
            if (_leaving) line = null;
            if (line != _shownLine)
            {
                _shownLine = line;
                _text.text = line ?? "";
                _bubblePop = string.IsNullOrEmpty(line) ? 0f : 1f;
            }
            _bubblePop = Mathf.MoveTowards(_bubblePop, 0f, Time.unscaledDeltaTime * 5f);

            ApplyLayout();
        }

        void UpdateCountdown()
        {
            if (_countdownPlayed || _plan == null || _session == null || !_session.IsPlaying || _session.HasCompetitionStarted) return;

            double start = CompetitionStartSeconds();
            if (_session.ElapsedTime < start - countdownLeadSeconds) return;

            _countdownPlayed = true;
            _standingLine = countdownLine;
            _transientLine = null;
            if (startCountdown != null) startCountdown.StartCoroutine(startCountdown.PlayStartCountdown());
        }

        // 競技が始まる予定の時刻（セッションの秒）
        double CompetitionStartSeconds()
        {
            double start = _session.LearningSeconds;
            if (_plan.Stage == LearningStage.FreePractice && _plan.FreePracticeStartSeconds.HasValue)
                start = System.Math.Min(start, _plan.FreePracticeStartSeconds.Value + _plan.Settings.freePracticeSeconds);
            return start;
        }

        void Say(string line, float seconds)
        {
            if (!_active || _leaving || string.IsNullOrEmpty(line)) return;
            _transientLine = line;
            _transientUntil = Time.unscaledTime + seconds;
        }

        // ---------- 見た目 ----------

        void ApplyLayout()
        {
            float eased = 1f - Mathf.Pow(1f - _slide, 3f);
            float width = MikoWidth();
            float hiddenX = -width - 40f;
            float bob = Mathf.Sin(Time.unscaledTime * 2.2f) * 6f;
            _miko.anchoredPosition = new Vector2(Mathf.Lerp(hiddenX, mikoOffset.x, eased), mikoOffset.y + bob);

            bool hasLine = !string.IsNullOrEmpty(_shownLine);
            _bubble.gameObject.SetActive(hasLine && _slide > 0.6f);
            float pop = 1f + 0.08f * Mathf.Sin(_bubblePop * Mathf.PI);
            _bubble.localScale = new Vector3(pop, pop, 1f);
        }

        float MikoWidth()
        {
            if (mikoSprite == null) return mikoHeight * 0.54f;
            Rect r = mikoSprite.rect;
            return r.height > 0f ? mikoHeight * r.width / r.height : mikoHeight * 0.54f;
        }

        void SetVisible(bool visible)
        {
            if (_root != null) _root.gameObject.SetActive(visible);
            if (!visible) _shownLine = null;
        }

        void BuildUi()
        {
            var canvasGo = new GameObject("MikoGuideCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            _root = canvasGo.GetComponent<RectTransform>();

            // 巫女（左下に足もとを合わせる）
            var mikoGo = new GameObject("Miko", typeof(RectTransform), typeof(Image));
            mikoGo.transform.SetParent(_root, false);
            _miko = mikoGo.GetComponent<RectTransform>();
            _miko.anchorMin = _miko.anchorMax = new Vector2(0f, 0f);
            _miko.pivot = new Vector2(0f, 0f);
            _miko.sizeDelta = new Vector2(MikoWidth(), mikoHeight);
            var mikoImage = mikoGo.GetComponent<Image>();
            mikoImage.sprite = mikoSprite;
            mikoImage.preserveAspect = true;
            mikoImage.raycastTarget = false;
            if (mikoSprite == null) mikoImage.color = new Color(0.64f, 0.18f, 0.18f, 0.6f);

            // 吹き出し（巫女の顔の右に、巫女といっしょに動く）
            var bubbleGo = new GameObject("Bubble", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            bubbleGo.transform.SetParent(_miko, false);
            _bubble = bubbleGo.GetComponent<RectTransform>();
            _bubble.anchorMin = _bubble.anchorMax = new Vector2(1f, 1f);
            _bubble.pivot = new Vector2(0f, 1f);
            _bubble.anchoredPosition = new Vector2(-mikoHeight * 0.1f, -mikoHeight * 0.06f);
            var bubbleBorder = bubbleGo.GetComponent<Image>();
            bubbleBorder.color = bubbleBorderColor;
            bubbleBorder.raycastTarget = false;
            var layout = bubbleGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(5, 5, 5, 5);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var fitter = bubbleGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var innerGo = new GameObject("Inner", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            innerGo.transform.SetParent(_bubble, false);
            var inner = innerGo.GetComponent<Image>();
            inner.color = bubbleColor;
            inner.raycastTarget = false;
            var innerLayout = innerGo.GetComponent<VerticalLayoutGroup>();
            innerLayout.padding = new RectOffset(34, 34, 20, 22);
            innerLayout.childControlWidth = innerLayout.childControlHeight = true;
            innerLayout.childForceExpandWidth = innerLayout.childForceExpandHeight = false;

            // しっぽ（巫女の顔のほうを向く小さな三角を、ひし形で代用）
            var tailGo = new GameObject("Tail", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            tailGo.transform.SetParent(_bubble, false);
            tailGo.GetComponent<LayoutElement>().ignoreLayout = true;
            var tail = tailGo.GetComponent<RectTransform>();
            tail.anchorMin = tail.anchorMax = new Vector2(0f, 1f);
            tail.sizeDelta = new Vector2(30f, 30f);
            tail.anchoredPosition = new Vector2(2f, -52f);
            tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
            var tailImage = tailGo.GetComponent<Image>();
            tailImage.color = bubbleBorderColor;
            tailImage.raycastTarget = false;
            tailGo.transform.SetAsFirstSibling();

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(innerGo.transform, false);
            _text = textGo.GetComponent<Text>();
            _text.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = Mathf.RoundToInt(fontSize);
            _text.color = textColor;
            _text.alignment = TextAnchor.MiddleLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.lineSpacing = 1.1f;
            _text.raycastTarget = false;
        }
    }
}
