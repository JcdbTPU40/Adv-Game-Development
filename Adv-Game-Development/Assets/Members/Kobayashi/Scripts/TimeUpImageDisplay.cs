using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// セッション終了時に、左右のふすま画像を画面中央へスライド表示します。
/// </summary>
[DefaultExecutionOrder(-200)]
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

    [Header("シーン開始時の襖演出")]
    [SerializeField] private bool openOnSceneStart;
    [SerializeField, Min(0f)] private float closedHoldDuration = 0.5f;

    private GameSession subscribedSession;
    private Canvas overlayCanvas;
    private Image leftPanelImage;
    private Image rightPanelImage;
    private Coroutine slideRoutine;
    private bool allowSessionStartToHidePanels;

    private void Start()
    {
        // シーン側のInspector設定に加え、現在のメインゲームシーン名でも開始を判定します。
        // シーンのシリアライズ値が更新されていない場合もGameScene1では必ず開きます。
        bool shouldOpenOnSceneStart = openOnSceneStart
            || SceneManager.GetActiveScene().name == "GameScene1";

        if (shouldOpenOnSceneStart)
        {
            Debug.Log("[TimeUpImageDisplay] GameScene1の襖を開く演出を開始します。", this);
            StartCoroutine(OpenFusumaThenStartSession());
        }
        else
        {
            // GameScene1をエディターから直接再生した場合も、セッションを開始します。
            StartCoroutine(StartSessionWhenReady());
        }
    }

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
        subscribedSession.onSessionStart.AddListener(HidePanelsOnSessionStart);
    }

    private void Unsubscribe()
    {
        if (subscribedSession == null)
        {
            return;
        }

        subscribedSession.onSessionEnd.RemoveListener(ShowTimeUpPanels);
        subscribedSession.onSessionStart.RemoveListener(HidePanelsOnSessionStart);
        subscribedSession = null;
    }

    private void ShowTimeUpPanels()
    {
        if (!PrepareTimeUpPanels())
        {
            return;
        }

        StopSlide();
        slideRoutine = StartCoroutine(SlidePanelsIntoPlace());
    }

    /// <summary>
    /// Start画面からゲーム画面へ移るときも、同じ襖演出を再生します。
    /// </summary>
    public IEnumerator PlayFusumaTransition()
    {
        if (!PrepareTimeUpPanels())
        {
            yield break;
        }

        StopSlide();
        yield return SlidePanelsIntoPlace();
    }

    private IEnumerator OpenFusumaThenStartSession()
    {
        allowSessionStartToHidePanels = false;
        if (PrepareTimeUpPanels())
        {
            StopSlide();
            // UIやセッションの初期化が終わるまで、閉じた状態でフレーム描画を待ちます。
            SetPanelPositions(-PanelCenterX, PanelCenterX);
            Canvas.ForceUpdateCanvases();
            yield return new WaitForEndOfFrame();
            if (closedHoldDuration > 0f)
            {
                yield return new WaitForSecondsRealtime(closedHoldDuration);
            }

            yield return SlidePanelsOutward();
            HideTimeUpPanels();
        }

        yield return StartSessionWhenReady();
    }

    private IEnumerator StartSessionWhenReady()
    {
        // GameSession.Start() が完了した後に開始して、autoStartとの二重開始を防ぎます。
        yield return null;
        allowSessionStartToHidePanels = true;
        if (GameSession.Instance != null && !GameSession.Instance.IsPlaying)
        {
            GameSession.Instance.StartSession();
        }
    }

    private void HidePanelsOnSessionStart()
    {
        // 初期化順の違いで開始イベントが襖の開き切る前に来ても、演出を消しません。
        if (allowSessionStartToHidePanels)
        {
            HideTimeUpPanels();
        }
    }

    private bool PrepareTimeUpPanels()
    {
        if (leftPanelSprite == null || rightPanelSprite == null)
        {
            Debug.LogWarning("左右のふすま画像をInspectorで設定してください。", this);
            return false;
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
        return true;
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

    private IEnumerator SlidePanelsOutward()
    {
        float duration = Mathf.Max(0f, slideDuration);
        if (duration <= 0f)
        {
            SetPanelPositions(-OffscreenX, OffscreenX);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            // 開く演出だけは単調な補間にし、襖が行き過ぎて戻るバウンドを防ぎます。
            float easedTime = Mathf.SmoothStep(0f, 1f, normalizedTime);
            SetPanelPositions(
                Mathf.Lerp(-PanelCenterX, -OffscreenX, easedTime),
                Mathf.Lerp(PanelCenterX, OffscreenX, easedTime));
            yield return null;
        }

        SetPanelPositions(-OffscreenX, OffscreenX);
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
