using UnityEngine;
using UnityEngine.UI;

namespace Toufuku.Hud
{
    /*
        プレイ中の HUD（#61 / 企画書 v8 7章「HUDに出すもの」、13章「観戦体験の設計」）

        ・右上: 今の縁（5桁）。その下に今日のベストスコアを並べる（待っている子が「勝てそう」と思える）
          今の縁がこのプレイを始めたときのベストをこえたら「ベスト更新中！」に変える
        ・左上: 今のランク（C/B/A/S）の文字と、金色の評価ゲージ。リザルトの称号（最高ランク）とちがって、ここは今のランク
        ・縁と今のランクは、列のうしろ（画面から約7m）から読める大きさにする（13章「画面外から読める大きさ」）
          文字の大きさは画面の高さのわりあいで持つ。既定値は 50インチのモニターでも 7m 先から読める（HudLegibility と EditMode テストで確認）
        ・リザルト中（GameSession.IsFinished）は消す。3:00 のあとの解決中は出したまま（最後の弾の救済得点が入るのを見せる）
        ・#58: 段階学習の練習中（ScoreManager.IsPractice）は出さない（7章「開始30秒は縁を出さない」、18章「得点・ランクは操作と危険度が理解された後に表示」）

        Canvas はコードで組み立てるので、シーンの空の GameObject に付けるだけで動く
        ScoreManager / ShrineRating / DailyBestRecorder のイベントを受け取って表示を変える（毎フレーム値を読みに行かない）
    */
    public class ScoreBoardHud : MonoBehaviour
    {
        // 既定の文字の大きさ（画面の高さに対するわりあい）
        public const float DefaultEnFontRatio = 0.15f;
        public const float DefaultRankFontRatio = 0.18f;
        public const float DefaultBestFontRatio = 0.105f;
        public const float DefaultLabelFontRatio = 0.04f;

        [Header("フォント（NotoSansJP の .ttf を入れる。未設定なら組み込みフォント）")]
        [SerializeField] Font font;

        [Header("文字の大きさ（画面の高さに対するわりあい）")]
        [Tooltip("縁の数字。既定 0.15 は 50インチのモニターで約10m先から読める。")]
        [SerializeField, Range(0.05f, 0.3f)] float enFontRatio = DefaultEnFontRatio;
        [Tooltip("今のランクの文字。既定 0.18 は 50インチのモニターで約12m先から読める。")]
        [SerializeField, Range(0.05f, 0.3f)] float rankFontRatio = DefaultRankFontRatio;
        [Tooltip("今日のベストの数字。既定 0.105 は 50インチのモニターで約7m先から読める。")]
        [SerializeField, Range(0.03f, 0.2f)] float bestFontRatio = DefaultBestFontRatio;
        [Tooltip("「縁」「今日のベスト」「神社ランク」の見出し。")]
        [SerializeField, Range(0.02f, 0.1f)] float labelFontRatio = DefaultLabelFontRatio;

        [Header("配置")]
        [Tooltip("画面のはしからのすきま（画面の高さに対するわりあい）。")]
        [SerializeField, Range(0f, 0.1f)] float marginRatio = 0.02f;
        [SerializeField] int sortingOrder = 50;

        [Header("色")]
        [SerializeField] Color enColor = Color.white;
        [SerializeField] Color labelColor = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] Color bestColor = new Color(1f, 0.84f, 0.3f);
        [SerializeField] Color newBestColor = new Color(1f, 0.95f, 0.45f);
        [SerializeField] Color gaugeColor = new Color(1f, 0.78f, 0.2f);
        [SerializeField] Color backplateColor = new Color(0f, 0f, 0f, 0.38f);

        RectTransform _canvas;
        Text _enValue;
        Text _bestLabel;
        Text _bestValue;
        Text _rankValue;
        RectTransform _gaugeFill;

        bool _scoreSubscribed;
        bool _ratingSubscribed;
        bool _bestSubscribed;

        int _en;
        ShrineRank _rank = ShrineRank.C;
        float _ratingNormalized;

        // 確認用: 表示している文字
        public string EnText => _enValue != null ? _enValue.text : null;
        public string BestLabelText => _bestLabel != null ? _bestLabel.text : null;
        public string BestValueText => _bestValue != null ? _bestValue.text : null;
        public string RankText => _rankValue != null ? _rankValue.text : null;
        public bool IsVisible => _canvas != null && _canvas.gameObject.activeSelf;

        public float EnFontRatio => enFontRatio;
        public float RankFontRatio => rankFontRatio;
        public float BestFontRatio => bestFontRatio;

#if UNITY_EDITOR
        void Reset()
        {
            font = HudUi.FindProjectJapaneseFont();
        }
#endif

        void Awake()
        {
            DailyBestRecorder.Ensure();
            Build();
        }

        void OnEnable()
        {
            TrySubscribe();
            RefreshAll();
        }

        void OnDisable()
        {
            Unsubscribe();
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void Update()
        {
            // 動く順番のせいで、まだ ScoreManager などができていなかったときのための予備
            TrySubscribe();
            UpdateVisibility();
        }

        // ---------- 組み立て ----------

        void Build()
        {
            _canvas = HudUi.CreateCanvas(transform, "ScoreBoardHudCanvas", sortingOrder);

            int enSize = HudUi.FontSizeOf(enFontRatio);
            int rankSize = HudUi.FontSizeOf(rankFontRatio);
            int bestSize = HudUi.FontSizeOf(bestFontRatio);
            int labelSize = HudUi.FontSizeOf(labelFontRatio);
            float margin = marginRatio * HudUi.ReferenceHeight;
            float pad = labelSize * 0.4f;

            BuildScoreBoard(enSize, bestSize, labelSize, margin, pad);
            BuildRankBoard(rankSize, labelSize, margin, pad);
        }

        // 右上: 縁と今日のベスト
        void BuildScoreBoard(int enSize, int bestSize, int labelSize, float margin, float pad)
        {
            float enRowH = enSize * 1.2f;
            float bestRowH = bestSize * 1.2f;
            // 数字1けたは約 0.6em。見出しのぶんも足す
            float width = Mathf.Max(enSize * 0.6f * 5f + labelSize * 2f, bestSize * 0.6f * 5f + labelSize * 7f) + pad * 2f;
            float height = enRowH + bestRowH + pad * 2f;

            RectTransform board = HudUi.CreateRect(_canvas, "ScoreBoard", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-margin, -margin), new Vector2(width, height));
            HudUi.CreateImage(board, "Backplate", backplateColor);

            float rowW = width - pad * 2f;

            RectTransform enRow = HudUi.CreateRect(board, "EnRow", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-pad, -pad), new Vector2(rowW, enRowH));
            _enValue = HudUi.CreateText(enRow, "EnValue", font, enSize, TextAnchor.LowerRight, enColor);
            CreateBaselineLabel(enRow, "EnLabel", "縁", labelSize, enSize, TextAnchor.LowerLeft, labelColor);

            RectTransform bestRow = HudUi.CreateRect(board, "BestRow", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-pad, -pad - enRowH), new Vector2(rowW, bestRowH));
            _bestValue = HudUi.CreateText(bestRow, "BestValue", font, bestSize, TextAnchor.LowerRight, bestColor);
            _bestLabel = CreateBaselineLabel(bestRow, "BestLabel", "今日のベスト", labelSize, bestSize, TextAnchor.LowerLeft, bestColor);
        }

        // 左上: 今のランクと評価ゲージ
        void BuildRankBoard(int rankSize, int labelSize, float margin, float pad)
        {
            float labelRowH = labelSize * 1.4f;
            float rankRowH = rankSize * 1.15f;
            float gaugeH = Mathf.Max(12f, labelSize * 0.55f);
            float gaugeW = rankSize * 2.2f;
            float width = Mathf.Max(gaugeW, labelSize * 5.5f) + pad * 2f;
            float height = labelRowH + rankRowH + gaugeH + pad * 3f;

            RectTransform board = HudUi.CreateRect(_canvas, "RankBoard", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(margin, -margin), new Vector2(width, height));
            HudUi.CreateImage(board, "Backplate", backplateColor);

            RectTransform labelRow = HudUi.CreateRect(board, "LabelRow", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(pad, -pad), new Vector2(width - pad * 2f, labelRowH));
            HudUi.CreateText(labelRow, "RankLabel", font, labelSize, TextAnchor.UpperLeft, labelColor).text = "神社ランク";

            RectTransform rankRow = HudUi.CreateRect(board, "RankRow", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(pad, -pad - labelRowH), new Vector2(width - pad * 2f, rankRowH));
            _rankValue = HudUi.CreateText(rankRow, "RankValue", font, rankSize, TextAnchor.LowerLeft, HudUi.RankColor(ShrineRank.C));

            RectTransform gauge = HudUi.CreateRect(board, "Gauge", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(pad, -pad * 2f - labelRowH - rankRowH), new Vector2(gaugeW, gaugeH));
            HudUi.CreateImage(gauge, "GaugeBack", new Color(0f, 0f, 0f, 0.55f));

            _gaugeFill = HudUi.CreateRect(gauge, "GaugeFill", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            HudUi.Stretch(_gaugeFill);
            Image fill = _gaugeFill.gameObject.AddComponent<Image>();
            fill.color = gaugeColor;
            fill.raycastTarget = false;
        }

        /*
            大きい数字と同じ行に置く小さい見出し。下（ベースライン）を大きい数字にそろえる
            下ぞろえの文字はディセンダーのぶん浮くので、大きさの差のぶん見出しを持ち上げる
        */
        Text CreateBaselineLabel(RectTransform row, string name, string text, int labelSize, int bigSize, TextAnchor alignment, Color color)
        {
            float lift = HudUi.DescenderRatio * (bigSize - labelSize);
            RectTransform rt = HudUi.CreateRect(row, name, Vector2.zero, Vector2.zero, new Vector2(0f, lift), new Vector2(row.sizeDelta.x, labelSize * 1.5f));
            Text label = HudUi.CreateText(rt, "Text", font, labelSize, alignment, color);
            label.text = text;
            return label;
        }

        // ---------- イベント ----------

        void TrySubscribe()
        {
            ScoreManager sm = ScoreManager.Instance;
            if (!_scoreSubscribed && sm != null)
            {
                sm.onEnChanged += OnEnChanged;
                sm.onReset += RefreshAll;
                sm.onScoreLocked += OnScoreLocked;
                _scoreSubscribed = true;
                RefreshAll();
            }

            ShrineRating rating = ShrineRating.Instance;
            if (!_ratingSubscribed && rating != null)
            {
                rating.onRatingChanged.AddListener(OnRatingChanged);
                rating.onRankChanged.AddListener(OnRankChanged);
                _ratingSubscribed = true;
                RefreshAll();
            }

            DailyBestRecorder best = DailyBestRecorder.Instance;
            if (!_bestSubscribed && best != null)
            {
                best.onBestChanged += RefreshBest;
                _bestSubscribed = true;
                RefreshBest();
            }
        }

        void Unsubscribe()
        {
            ScoreManager sm = ScoreManager.Instance;
            if (_scoreSubscribed && sm != null)
            {
                sm.onEnChanged -= OnEnChanged;
                sm.onReset -= RefreshAll;
                sm.onScoreLocked -= OnScoreLocked;
            }
            _scoreSubscribed = false;

            ShrineRating rating = ShrineRating.Instance;
            if (_ratingSubscribed && rating != null)
            {
                rating.onRatingChanged.RemoveListener(OnRatingChanged);
                rating.onRankChanged.RemoveListener(OnRankChanged);
            }
            _ratingSubscribed = false;

            DailyBestRecorder best = DailyBestRecorder.Instance;
            if (_bestSubscribed && best != null)
                best.onBestChanged -= RefreshBest;
            _bestSubscribed = false;
        }

        void OnEnChanged(int en)
        {
            _en = en;
            RefreshEn();
        }

        void OnScoreLocked(int en)
        {
            _en = en;
            RefreshEn();
        }

        void OnRatingChanged(float normalized)
        {
            _ratingNormalized = normalized;
            RefreshRank();
        }

        void OnRankChanged(ShrineRank rank)
        {
            _rank = rank;
            RefreshRank();
        }

        // ---------- 表示 ----------

        void UpdateVisibility()
        {
            if (_canvas == null) return;
            GameSession session = GameSession.Instance;
            ScoreManager sm = ScoreManager.Instance;
            bool show = (session == null || !session.IsFinished) && !(sm != null && sm.IsPractice);
            if (_canvas.gameObject.activeSelf == show) return;

            _canvas.gameObject.SetActive(show);
            if (show) RefreshAll();
        }

        void RefreshAll()
        {
            ScoreManager sm = ScoreManager.Instance;
            _en = sm != null ? sm.En : 0;

            ShrineRating rating = ShrineRating.Instance;
            if (rating != null)
            {
                _rank = rating.Rank;
                _ratingNormalized = rating.RatingNormalized;
            }

            RefreshEn();
            RefreshRank();
        }

        void RefreshEn()
        {
            if (_enValue == null) return;
            _enValue.text = HudUi.FormatScore(_en);
            RefreshBest();
        }

        void RefreshBest()
        {
            if (_bestValue == null) return;

            DailyBestRecorder best = DailyBestRecorder.Instance;
            int before = best != null ? best.BestBeforeThisPlay : 0;

            if (before <= 0)
            {
                // 今日はじめてのプレイ。くらべる相手がまだいない
                _bestLabel.text = "今日のベスト";
                _bestValue.text = "―";
                _bestLabel.color = bestColor;
                _bestValue.color = bestColor;
            }
            else if (_en > before)
            {
                _bestLabel.text = "ベスト更新中！";
                _bestValue.text = HudUi.FormatScore(_en);
                _bestLabel.color = newBestColor;
                _bestValue.color = newBestColor;
            }
            else
            {
                _bestLabel.text = "今日のベスト";
                _bestValue.text = HudUi.FormatScore(before);
                _bestLabel.color = bestColor;
                _bestValue.color = bestColor;
            }
        }

        void RefreshRank()
        {
            if (_rankValue == null) return;
            _rankValue.text = _rank.ToString();
            _rankValue.color = HudUi.RankColor(_rank);
            if (_gaugeFill != null) _gaugeFill.anchorMax = new Vector2(Mathf.Clamp01(_ratingNormalized), 1f);
        }
    }
}
