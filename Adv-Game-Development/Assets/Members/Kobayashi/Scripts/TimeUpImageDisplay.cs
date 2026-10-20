using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// セッション終了時に、左右のふすま画像を画面中央へスライド表示します。
/// </summary>
public class TimeUpImageDisplay : MonoBehaviour
{
    [Header("左右の画像")]
    [SerializeField] private Sprite leftPanelSprite;
    [SerializeField] private Sprite rightPanelSprite;

    [Header("スライド演出")]
    [SerializeField, Min(0f)] private float slideDuration = 0.8f;
    [SerializeField] private AnimationCurve slideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] private int sortingOrder = 55;

    private GameSession subscribedSession;
    private Canvas overlayCanvas;
    private Image leftPanelImage;
    private Image rightPanelImage;
    private Coroutine slideRoutine;

    private void OnEnable()
    {
        Subscribe();
    }

    private void Update()
    {
        // GameSessionが後から生成される場合に備えて、イベント購読だけを再試行します。
        if (subscribedSession == null)
        {
            Subscribe();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
        HideTimeUpPanels();
    }

    private void Subscribe()
    {
        if (subscribedSession != null || GameSession.Instance == null)
        {
            return;
        }

        subscribedSession = GameSession.Instance;
        subscribedSession.onSessionEnd.AddListener(ShowTimeUpPanels);
        subscribedSession.onSessionStart.AddListener(HideTimeUpPanels);
    }

    private void Unsubscribe()
    {
        if (subscribedSession == null)
        {
            return;
        }

        subscribedSession.onSessionEnd.RemoveListener(ShowTimeUpPanels);
        subscribedSession.onSessionStart.RemoveListener(HideTimeUpPanels);
        subscribedSession = null;
    }

    private void ShowTimeUpPanels()
    {
        if (leftPanelSprite == null || rightPanelSprite == null)
        {
            Debug.LogWarning("左右のふすま画像をInspectorで設定してください。", this);
            return;
        }

        EnsurePanels();
        leftPanelImage.sprite = leftPanelSprite;
        rightPanelImage.sprite = rightPanelSprite;
        leftPanelImage.SetNativeSize();
        rightPanelImage.SetNativeSize();
        ResizePanel(leftPanelImage.rectTransform);
        ResizePanel(rightPanelImage.rectTransform);

        overlayCanvas.gameObject.SetActive(true);
        leftPanelImage.gameObject.SetActive(true);
        rightPanelImage.gameObject.SetActive(true);
        SetPanelPositions(-OffscreenX, OffscreenX);
        StopSlide();
        slideRoutine = StartCoroutine(SlidePanelsIntoPlace());
    }

    private void EnsurePanels()
    {
        if (overlayCanvas != null)
        {
            return;
        }

        var canvasObject = new GameObject("TimeUpFusumaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        overlayCanvas = canvasObject.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = sortingOrder;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        leftPanelImage = CreatePanel(canvasObject.transform, "HusumaLeft");
        rightPanelImage = CreatePanel(canvasObject.transform, "HusumaRight");
        canvasObject.SetActive(false);
    }

    private Image CreatePanel(Transform parent, string objectName)
    {
        var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(parent, false);

        var rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        ResizePanel(rect);

        var image = imageObject.GetComponent<Image>();
        image.preserveAspect = false;
        image.raycastTarget = false;
        return image;
    }

    private void ResizePanel(RectTransform rect)
    {
        rect.sizeDelta = new Vector2(referenceResolution.x * 0.5f, referenceResolution.y);
    }

    private float OffscreenX => referenceResolution.x * 0.75f;
    private float PanelCenterX => referenceResolution.x * 0.25f;

    private void SetPanelPositions(float leftX, float rightX)
    {
        leftPanelImage.rectTransform.anchoredPosition = new Vector2(leftX, 0f);
        rightPanelImage.rectTransform.anchoredPosition = new Vector2(rightX, 0f);
    }

    private IEnumerator SlidePanelsIntoPlace()
    {
        float duration = Mathf.Max(0f, slideDuration);
        if (duration <= 0f)
        {
            SetPanelPositions(-PanelCenterX, PanelCenterX);
            slideRoutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float easedTime = slideCurve != null ? slideCurve.Evaluate(normalizedTime) : normalizedTime;
            SetPanelPositions(
                Mathf.Lerp(-OffscreenX, -PanelCenterX, easedTime),
                Mathf.Lerp(OffscreenX, PanelCenterX, easedTime));
            yield return null;
        }

        SetPanelPositions(-PanelCenterX, PanelCenterX);
        slideRoutine = null;
    }

    private void HideTimeUpPanels()
    {
        StopSlide();
        if (overlayCanvas != null)
        {
            overlayCanvas.gameObject.SetActive(false);
        }
    }

    private void StopSlide()
    {
        if (slideRoutine == null)
        {
            return;
        }

        StopCoroutine(slideRoutine);
        slideRoutine = null;
    }
}
