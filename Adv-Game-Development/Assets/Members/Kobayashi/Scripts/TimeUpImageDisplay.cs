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

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribedSession != null || GameSession.Instance == null)
        {
            return;
        }

        subscribedSession = GameSession.Instance;
        subscribedSession.onSessionEnd.AddListener(ShowTimeUpImage);
    }

    private void Unsubscribe()
    {
        if (subscribedSession == null)
        {
            return;
        }

        subscribedSession.onSessionEnd.RemoveListener(ShowTimeUpImage);
        subscribedSession = null;
    }

    private void ShowTimeUpImage()
    {
        if (displayImage == null || timeUpSprite == null)
        {
            Debug.LogWarning("時間切れ画像または表示先ImageがInspectorで設定されていません。", this);
            return;
        }

        displayImage.sprite = timeUpSprite;
        displayImage.enabled = true;
        displayImage.gameObject.SetActive(true);
    }
}
