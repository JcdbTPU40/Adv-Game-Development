using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ゲーム終了後、キーが押されたらInspectorで指定したシーンへ戻ります。
/// </summary>
public class ReturnToStartAfterSession : MonoBehaviour
{
    [SerializeField, Tooltip("ゲーム終了後に戻るシーン名。Build Settingsに登録してください。")]
    private string returnSceneName = "Start";

    private GameSession subscribedSession;
    private bool sessionEnded;
    private bool sceneChangeStarted;

    private void Start()
    {
        subscribedSession = GameSession.Instance;
        if (subscribedSession == null)
        {
            Debug.LogWarning("GameSessionが見つからないため、終了後のシーン復帰を設定できません。", this);
            return;
        }

        subscribedSession.onSessionEnd.AddListener(EnableReturnOnKey);
        sessionEnded = subscribedSession.IsFinished;
    }

    private void OnDisable()
    {
        if (subscribedSession != null)
        {
            subscribedSession.onSessionEnd.RemoveListener(EnableReturnOnKey);
            subscribedSession = null;
        }
    }

    private void Update()
    {
        if (!sessionEnded || sceneChangeStarted || !Input.anyKeyDown)
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
        sessionEnded = true;
    }
}
