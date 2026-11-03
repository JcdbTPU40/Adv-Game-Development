using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// セッション終了前の数秒間、参・弐・壱を画面中央に表示します。
/// </summary>
public class SessionEndCountdownTMP : MonoBehaviour
{
    [Header("表示内容")]
    [SerializeField] private Font japaneseFont;
    [SerializeField] private TMP_FontAsset sdfFont;
    [SerializeField, Min(1f)] private float fontSize = 220f;
    [SerializeField] private Color textColor = Color.white;
    [SerializeField, Range(0f, 1f)] private float opacity = 0.68f;
    [SerializeField, Min(1f)] private float countdownStartSeconds = 3f;
    [SerializeField, Min(0.1f)] private float startCountdownStepDuration = 1f;
    [SerializeField, Min(0f)] private float startMessageDuration = 0.5f;
    [SerializeField, Range(0f, 1f)] private float startOpacity = 1f;

    [Header("配置")]
    [SerializeField] private Vector2 anchoredPosition = Vector2.zero;
    [SerializeField] private Vector2 textAreaSize = new Vector2(700f, 400f);
    [SerializeField] private int sortingOrder = 70;

    private static readonly string[] Numerals = { "", "壱", "弐", "参" };
    private GameSession session;
    private TMP_Text countdownText;
    private bool isStartingCountdown;

    private void Start()
    {
        CreateCountdownText();
        TrySubscribe();
        HideCountdown();
    }

    private void Update()
    {
        if (session == null)
        {
            TrySubscribe();
        }

        if (isStartingCountdown)
        {
            return;
        }

        if (session == null || countdownText == null || !session.IsPlaying)
        {
            HideCountdown();
            return;
        }

        float remaining = session.RemainingSeconds;
        if (remaining <= 0f || remaining > countdownStartSeconds)
        {
            HideCountdown();
            return;
        }

        int numeralIndex = Mathf.Clamp(Mathf.CeilToInt(remaining), 1, Numerals.Length - 1);
        countdownText.text = Numerals[numeralIndex];
        countdownText.enabled = true;
    }

    private void OnDisable()
    {
        if (session != null)
        {
            session.onSessionStart.RemoveListener(HideCountdown);
            session.onSessionEnd.RemoveListener(HideCountdown);
            session = null;
        }

        isStartingCountdown = false;
        HideCountdown();
    }

    /// <summary>
    /// ゲーム開始直前に、終了前表示と同じTMP・色・サイズで開始カウントダウンを再生します。
    /// </summary>
    public IEnumerator PlayStartCountdown()
    {
        if (countdownText == null)
        {
            CreateCountdownText();
        }

        isStartingCountdown = true;
        Color previousColor = countdownText.color;
        Color startColor = previousColor;
        startColor.a = startOpacity;
        countdownText.color = startColor;
        countdownText.enabled = true;

        string[] sequence = { "参", "弐", "壱" };
        for (int i = 0; i < sequence.Length; i++)
        {
            countdownText.text = sequence[i];
            yield return new WaitForSecondsRealtime(startCountdownStepDuration);
        }

        RectTransform textRect = countdownText.rectTransform;
        Vector2 previousTextAreaSize = textRect.sizeDelta;
        bool previousWordWrapping = countdownText.enableWordWrapping;
        countdownText.enableWordWrapping = false;
        textRect.sizeDelta = new Vector2(
            Mathf.Max(previousTextAreaSize.x, Mathf.Min(1920f, countdownText.fontSize * 3.2f)),
            previousTextAreaSize.y);
        countdownText.text = "始め！";
        if (startMessageDuration > 0f)
        {
            yield return new WaitForSecondsRealtime(startMessageDuration);
        }

        textRect.sizeDelta = previousTextAreaSize;
        countdownText.enableWordWrapping = previousWordWrapping;
        countdownText.color = previousColor;
        isStartingCountdown = false;
        HideCountdown();
    }

    private void CreateCountdownText()
    {
        var canvasObject = new GameObject("SessionEndCountdownCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var textObject = new GameObject("SessionEndCountdownText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(canvasObject.transform, false);

        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = textAreaSize;
        rect.anchoredPosition = anchoredPosition;

        countdownText = textObject.GetComponent<TextMeshProUGUI>();
        countdownText.font = sdfFont != null ? sdfFont : (japaneseFont != null ? TMP_FontAsset.CreateFontAsset(japaneseFont) : null);
        countdownText.fontSize = fontSize;
        var displayColor = textColor;
        displayColor.a = opacity;
        countdownText.color = displayColor;
        countdownText.alignment = TextAlignmentOptions.Center;
        countdownText.raycastTarget = false;
        countdownText.text = string.Empty;
        countdownText.enabled = false;
    }

    private void TrySubscribe()
    {
        if (session != null || GameSession.Instance == null)
        {
            return;
        }

        session = GameSession.Instance;
        session.onSessionStart.AddListener(HideCountdown);
        session.onSessionEnd.AddListener(HideCountdown);
    }

    private void HideCountdown()
    {
        if (isStartingCountdown)
        {
            return;
        }

        if (countdownText != null)
        {
            countdownText.enabled = false;
        }
    }
}
