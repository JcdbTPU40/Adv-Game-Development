using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ScoreManagerのイベントを受け取り、現在の縁を右上に表示します。
/// </summary>
public class CurrentScoreTMPHud : MonoBehaviour
{
    [Header("表示")]
    [SerializeField] private Font japaneseFont;
    [SerializeField] private TMP_FontAsset sdfFont;
    [SerializeField, Min(1f)] private float fontSize = 48f;
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private string labelPrefix = "縁";

    [Header("配置")]
    [SerializeField] private Vector2 topRightOffset = new Vector2(-36f, -36f);
    [SerializeField] private Vector2 textAreaSize = new Vector2(600f, 100f);
    [SerializeField] private int sortingOrder = 50;

    [Header("神社の評価ランク")]
    [SerializeField] private bool showRankLetter = true;
    [SerializeField, Min(1f)] private float rankFontSize = 64f;
    [SerializeField] private Color rankTextColor = Color.white;
    [SerializeField] private Vector2 rankTopLeftOffset = new Vector2(36f, -36f);
    [SerializeField] private Vector2 rankTextAreaSize = new Vector2(700f, 120f);

    private TMP_Text scoreText;
    private TMP_Text rankText;
    private ScoreManager subscribedScoreManager;
    private ShrineRating subscribedShrineRating;

    private void Start()
    {
        CreateScoreText();
        CreateRankText();
        HideLegacyRankLabel();
        HideLegacyRankValue();
        TrySubscribe();
        TrySubscribeRating();
    }

    private void Update()
    {
        if (subscribedScoreManager == null)
        {
            TrySubscribe();
        }

        if (showRankLetter && subscribedShrineRating == null)
        {
            TrySubscribeRating();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
        UnsubscribeRating();
    }

    private void CreateScoreText()
    {
        var canvasObject = new GameObject("CurrentScoreCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var textObject = new GameObject("CurrentScoreText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(canvasObject.transform, false);

        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.sizeDelta = textAreaSize;
        rect.anchoredPosition = topRightOffset;

        scoreText = textObject.GetComponent<TextMeshProUGUI>();
        scoreText.font = sdfFont != null ? sdfFont : (japaneseFont != null ? TMP_FontAsset.CreateFontAsset(japaneseFont) : null);
        scoreText.fontSize = fontSize;
        scoreText.color = textColor;
        scoreText.alignment = TextAlignmentOptions.TopRight;
        scoreText.textWrappingMode = TextWrappingModes.NoWrap;
        scoreText.raycastTarget = false;
        scoreText.text = string.Empty;
    }

    private void TrySubscribe()
    {
        if (subscribedScoreManager != null || ScoreManager.Instance == null)
        {
            return;
        }

        subscribedScoreManager = ScoreManager.Instance;
        subscribedScoreManager.onEnChanged += OnEnChanged;
        subscribedScoreManager.onReset += OnReset;
        OnEnChanged(subscribedScoreManager.En);
    }

    private void TrySubscribeRating()
    {
        if (!showRankLetter || subscribedShrineRating != null || ShrineRating.Instance == null)
        {
            return;
        }

        subscribedShrineRating = ShrineRating.Instance;
        subscribedShrineRating.onRankChanged.AddListener(OnRankChanged);
        OnRankChanged(subscribedShrineRating.Rank);
    }

    private void Unsubscribe()
    {
        if (subscribedScoreManager == null)
        {
            return;
        }

        subscribedScoreManager.onEnChanged -= OnEnChanged;
        subscribedScoreManager.onReset -= OnReset;
        subscribedScoreManager = null;
    }

    private void UnsubscribeRating()
    {
        if (subscribedShrineRating == null)
        {
            return;
        }

        subscribedShrineRating.onRankChanged.RemoveListener(OnRankChanged);
        subscribedShrineRating = null;
    }

    private void CreateRankText()
    {
        var textObject = new GameObject("CurrentShrineRankText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(scoreText.transform.parent, false);

        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = rankTextAreaSize;
        rect.anchoredPosition = rankTopLeftOffset;

        rankText = textObject.GetComponent<TextMeshProUGUI>();
        rankText.font = scoreText.font;
        rankText.fontSize = rankFontSize;
        rankText.color = rankTextColor;
        rankText.alignment = TextAlignmentOptions.TopLeft;
        rankText.textWrappingMode = TextWrappingModes.NoWrap;
        rankText.raycastTarget = false;
        rankText.text = showRankLetter ? "神社の評価 C" : "神社の評価";
    }

    private void HideLegacyRankLabel()
    {
        // 旧ScoreBoardHudの「神社ランク」だけを消し、既存の評価値表示は残します。
        GameObject legacyLabelObject = GameObject.Find(
            "ScoreBoardHud/ScoreBoardHudCanvas/RankBoard/LabelRow/RankLabel");
        if (legacyLabelObject == null)
        {
            return;
        }

        Text legacyLabel = legacyLabelObject.GetComponentInChildren<Text>(true);
        if (legacyLabel != null)
        {
            legacyLabel.enabled = false;
        }
        else
        {
            legacyLabelObject.SetActive(false);
        }
    }

    private void HideLegacyRankValue()
    {
        // 独自HUDに評価ラベルを残し、小さい旧ランク文字だけを隠します。
        GameObject legacyValueObject = GameObject.Find(
            "ScoreBoardHud/ScoreBoardHudCanvas/RankBoard/RankRow/RankValue");
        if (legacyValueObject != null)
        {
            legacyValueObject.SetActive(false);
        }
    }
    private void OnRankChanged(ShrineRank rank)
    {
        if (rankText != null)
        {
            rankText.text = $"神社の評価 {rank}";
        }
    }

    private void OnEnChanged(int en)
    {
        if (scoreText != null)
        {
            scoreText.text = string.IsNullOrEmpty(labelPrefix) ? $"{en:N0}" : $"{labelPrefix}  {en:N0}";
        }
    }

    private void OnReset()
    {
        OnEnChanged(0);
    }
}
