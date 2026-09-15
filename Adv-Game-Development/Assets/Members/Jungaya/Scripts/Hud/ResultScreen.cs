using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Toufuku.Hud
{
    /*
        リザルト画面（#61 / 企画書 v8 7章「称号は最高ランクで出す」、13章「観戦体験の設計」）

        ・GameSession がスコアを固定した（IsFinished）ら出す。3:00 のあと受理済みの弾を待っている間はまだ出さない
        ・出すもの
            縁: 固定した値（3:00 をまたいだ弾の救済得点まで入った値）
            神社の称号: プレイ中の最高ランク（最終ランクではない。終盤の負荷ウェーブで評価が下がっても達成の記録を消さない）
            今日のベスト: 今回で更新したら大きく祝う
            最大の福の連なり
        ・「もう一度」ボタンで GameSession.Retry()
        ・Canvas はコードで組み立てるので、シーンの空の GameObject に付けるだけで動く。EventSystem がなければ足す（ボタンを押せるように）
    */
    public class ResultScreen : MonoBehaviour
    {
        // 今リザルトを出しているか（テスト用の SessionHud が、かんたんなリザルトを重ねないために見る）
        public static bool IsShowing { get; private set; }

        [Header("フォント（NotoSansJP の .ttf を入れる。未設定なら組み込みフォント）")]
        [SerializeField] Font font;
        [SerializeField] int sortingOrder = 60;

        [Header("色")]
        [SerializeField] Color titleColor = Color.white;
        [SerializeField] Color enColor = Color.white;
        [SerializeField] Color bestColor = new Color(1f, 0.84f, 0.3f);
        [SerializeField] Color newRecordColor = new Color(1f, 0.95f, 0.45f);

        RectTransform _canvas;
        Text _enValue;
        Text _titleValue;
        Text _bestText;
        Text _chainText;
        bool _shown;

        // 確認用: 表示した値
        public int ShownEn { get; private set; }
        public ShrineRank ShownTitle { get; private set; }
        public bool ShownNewRecord { get; private set; }
        public string BestText => _bestText != null ? _bestText.text : null;

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
            _canvas.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            Hide();
        }

        void Update()
        {
            GameSession session = GameSession.Instance;
            bool show = session != null && session.IsFinished;
            if (show && !_shown) Show();
            else if (!show && _shown) Hide();
        }

        void Show()
        {
            Populate();
            EnsureEventSystem();
            _canvas.gameObject.SetActive(true);
            _shown = true;
            IsShowing = true;
        }

        void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            _shown = false;
            IsShowing = false;
        }

        void Populate()
        {
            ScoreManager sm = ScoreManager.Instance;
            ShrineRating rating = ShrineRating.Instance;
            DailyBestRecorder best = DailyBestRecorder.Instance;

            ShownEn = sm != null ? sm.En : 0;
            ShownTitle = rating != null ? rating.MaxRank : ShrineRank.C;
            ShownNewRecord = best != null && best.LastPlayWasNewRecord;

            _enValue.text = HudUi.FormatScore(ShownEn);
            _titleValue.text = ShownTitle.ToString();
            _titleValue.color = HudUi.RankColor(ShownTitle);

            int todayBest = best != null ? best.TodayBest : 0;
            if (ShownNewRecord)
            {
                _bestText.text = $"★ 今日のベスト更新！ {HudUi.FormatScore(todayBest)} ★";
                _bestText.color = newRecordColor;
                _bestText.fontSize = 84;
            }
            else
            {
                _bestText.text = $"今日のベスト  {HudUi.FormatScore(todayBest)}";
                _bestText.color = bestColor;
                _bestText.fontSize = 64;
            }

            _chainText.text = $"最大の福の連なり  {(sm != null ? sm.MaxCombo : 0)}";
        }

        void OnRetry()
        {
            if (GameSession.Instance != null) GameSession.Instance.Retry();
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            if (FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem (ResultScreen)", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // ---------- 組み立て（1920×1080 基準のピクセル） ----------

        void Build()
        {
            _canvas = HudUi.CreateCanvas(transform, "ResultScreenCanvas", sortingOrder);
            HudUi.CreateImage(_canvas, "Dim", new Color(0f, 0f, 0f, 0.72f));

            Vector2 top = new Vector2(0.5f, 1f);
            RectTransform panel = HudUi.CreateRect(_canvas, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1400f, 960f));
            HudUi.CreateImage(panel, "Backplate", new Color(0.08f, 0.06f, 0.1f, 0.92f));

            Row(panel, "Title", top, new Vector2(0f, -40f), new Vector2(1300f, 90f), 60, TextAnchor.MiddleCenter, titleColor)
                .text = "―― 三ヶ月が経ちました ――";

            // 左: 縁
            Row(panel, "EnLabel", top, new Vector2(-340f, -160f), new Vector2(620f, 80f), 56, TextAnchor.MiddleCenter, titleColor)
                .text = "縁";
            _enValue = Row(panel, "EnValue", top, new Vector2(-340f, -240f), new Vector2(620f, 300f), 220, TextAnchor.MiddleCenter, enColor);

            // 右: 神社の称号（プレイ中の最高ランク）
            Row(panel, "TitleLabel", top, new Vector2(340f, -160f), new Vector2(620f, 80f), 56, TextAnchor.MiddleCenter, titleColor)
                .text = "神社の称号";
            _titleValue = Row(panel, "TitleValue", top, new Vector2(340f, -240f), new Vector2(620f, 300f), 240, TextAnchor.MiddleCenter, Color.white);
            Row(panel, "TitleNote", top, new Vector2(340f, -540f), new Vector2(620f, 50f), 36, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f))
                .text = "プレイ中の最高ランク";

            _bestText = Row(panel, "Best", top, new Vector2(0f, -610f), new Vector2(1300f, 110f), 64, TextAnchor.MiddleCenter, bestColor);
            _chainText = Row(panel, "Chain", top, new Vector2(0f, -725f), new Vector2(1300f, 60f), 44, TextAnchor.MiddleCenter, titleColor);

            BuildRetryButton(panel, top);
        }

        Text Row(RectTransform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment, Color color)
        {
            RectTransform rt = HudUi.CreateRect(parent, name, anchor, anchor, position, size);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = position;
            return HudUi.CreateText(rt, "Text", font, fontSize, alignment, color);
        }

        void BuildRetryButton(RectTransform panel, Vector2 anchor)
        {
            RectTransform rt = HudUi.CreateRect(panel, "RetryButton", anchor, new Vector2(0.5f, 1f), new Vector2(0f, -810f), new Vector2(460f, 110f));
            Image image = rt.gameObject.AddComponent<Image>();
            image.color = new Color(0.85f, 0.2f, 0.25f, 1f);

            Button button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(OnRetry);

            HudUi.CreateText(rt, "Label", font, 56, TextAnchor.MiddleCenter, Color.white).text = "もう一度";
        }
    }
}
