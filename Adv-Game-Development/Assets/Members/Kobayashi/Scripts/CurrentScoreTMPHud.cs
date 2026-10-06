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
    [SerializeField, Min(1f)] private float fontSize = 48f;
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private string labelPrefix = "縁";

    [Header("配置")]
    [SerializeField] private Vector2 topRightOffset = new Vector2(-36f, -36f);
    [SerializeField] private Vector2 textAreaSize = new Vector2(600f, 100f);
    [SerializeField] private int sortingOrder = 50;

    private TMP_Text scoreText;
    private ScoreManager subscribedScoreManager;

    private void Start()
    {
        CreateScoreText();
        TrySubscribe();
    }

    private void Update()
    {
        if (subscribedScoreManager == null)
        {
            TrySubscribe();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
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
        if (japaneseFont != null)
        {
            scoreText.font = TMP_FontAsset.CreateFontAsset(japaneseFont);
        }
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
