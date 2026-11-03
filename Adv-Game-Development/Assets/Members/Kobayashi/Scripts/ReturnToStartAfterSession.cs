using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ゲーム終了後、キーが押されたらInspectorで指定したシーンへ戻ります。
/// </summary>
public class ReturnToStartAfterSession : MonoBehaviour
{
    [SerializeField, Tooltip("ゲーム終了後に戻るシーン名。Build Settingsに登録してください。")]
    private string returnSceneName = "Start";
    [SerializeField, Min(0f), Tooltip("リザルト表示後、キーとボタン操作を無効にする秒数。")]
    private float resultInputLockDuration = 3f;

    private GameSession subscribedSession;
    private Button retryButton;
    private bool sessionEnded;
    private bool sceneChangeStarted;
    private float resultShownAt;

    private void Start()
    {
        subscribedSession = GameSession.Instance;
        if (subscribedSession == null)
        {
            Debug.LogWarning("GameSessionが見つからないため、終了後のシーン復帰を設定できません。", this);
            return;
        }

        subscribedSession.onSessionEnd.AddListener(EnableReturnOnKey);
        if (subscribedSession.IsFinished)
        {
            BeginResultInputLock();
        }
    }

    private void OnDisable()
    {
        if (subscribedSession != null)
        {
            subscribedSession.onSessionEnd.RemoveListener(EnableReturnOnKey);
            subscribedSession = null;
        }

        if (retryButton != null)
        {
            retryButton.interactable = true;
            retryButton = null;
        }
    }

    private void Update()
    {
        if (!sessionEnded || sceneChangeStarted)
        {
            return;
        }

        if (Time.unscaledTime - resultShownAt < resultInputLockDuration)
        {
            SetRetryButtonInteractable(false);
            return;
        }

        SetRetryButtonInteractable(true);
        if (!Input.anyKeyDown)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(returnSceneName) || !Application.CanStreamedLevelBeLoaded(returnSceneName))
        {
            Debug.LogWarning($"戻り先のシーン「{returnSceneName}」が見つかりません。Build Settingsへの登録を確認してください。", this);
            return;
        }

        sceneChangeStarted = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(returnSceneName, LoadSceneMode.Single);
    }

    private void EnableReturnOnKey()
    {
        BeginResultInputLock();
    }

    private void BeginResultInputLock()
    {
        sessionEnded = true;
        resultShownAt = Time.unscaledTime;
        SetRetryButtonInteractable(false);
    }

    private void SetRetryButtonInteractable(bool interactable)
    {
        if (retryButton == null)
        {
            GameObject resultScreenObject = GameObject.Find("ScoreBoardHud");
            Transform buttonTransform = resultScreenObject != null
                ? resultScreenObject.transform.Find("ResultScreenCanvas/Panel/RetryButton")
                : null;
            if (buttonTransform != null)
            {
                retryButton = buttonTransform.GetComponent<Button>();
            }
        }

        if (retryButton != null)
        {
            retryButton.interactable = interactable;
        }
    }
}
