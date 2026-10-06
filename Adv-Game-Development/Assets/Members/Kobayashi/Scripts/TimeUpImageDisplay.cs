using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// セッション終了時に、Inspectorで指定した画像を画面いっぱいに表示します。
/// </summary>
public class TimeUpImageDisplay : MonoBehaviour
{
    [SerializeField] private Image displayImage;
    [SerializeField] private Sprite timeUpSprite;
    private GameSession subscribedSession;

    private void OnEnable() => Subscribe();
    private void OnDisable() => Unsubscribe();

    private void Subscribe()
    {
        if (subscribedSession != null || GameSession.Instance == null) return;
        subscribedSession = GameSession.Instance;
        subscribedSession.onSessionEnd.AddListener(ShowTimeUpImage);
    }

    private void Unsubscribe()
    {
        if (subscribedSession == null) return;
        subscribedSession.onSessionEnd.RemoveListener(ShowTimeUpImage);
        subscribedSession = null;
    }

    private void ShowTimeUpImage()
    {
        if (timeUpSprite == null)
        {
            Debug.LogWarning("時間切れ画像がInspectorで設定されていません。", this);
            return;
        }

        if (displayImage == null)
        {
            var canvasObject = new GameObject("TimeUpImageCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var imageObject = new GameObject("TimeUpImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            displayImage = imageObject.GetComponent<Image>();
        }

        displayImage.sprite = timeUpSprite;
        displayImage.enabled = true;
        displayImage.gameObject.SetActive(true);
    }
}
