using UnityEngine;
using UnityEngine.SceneManagement;

/*
    3:00 のあと、リザルトのシーン（ResultScene）へ移るクラス（GameScene に1つ置く）

    ・GameSession がスコアを固定した（IsFinished）ら、値を LastSessionResult に写して、delaySeconds 秒あとにリザルトのシーンを開く
      すぐ切りかえないのは、終わりの鈴（GameFeedbackDirector）と計測ログの書き出し（PlaytestLogger）を先にすませるため
    ・時間は Time.realtimeSinceStartup で数える（ポーズで timeScale が 0 のままでも進む）
    ・リザルトのシーンが Build Settings に入っていないときは、警告だけ出して移らない
*/
public class ResultSceneTransition : MonoBehaviour
{
    // リザルトのシーンへ移るのを待っているか（テスト用の SessionHud が、かんたんなリザルトを重ねないために見る）
    public static bool IsPending { get; private set; }

    [Tooltip("リザルトのシーン名（Build Settings に入っていること）")]
    [SerializeField] string resultSceneName = "ResultScene";
    [Tooltip("スコアを固定してからリザルトのシーンへ移るまでの秒")]
    [SerializeField, Min(0f)] float delaySeconds = 1.5f;

    double _finishedAt = double.NaN;
    bool _loading;

    public string ResultSceneName => resultSceneName;

    void OnDisable()
    {
        IsPending = false;
    }

    void Update()
    {
        if (_loading) return;

        GameSession session = GameSession.Instance;
        if (session == null || !session.IsFinished)
        {
            _finishedAt = double.NaN;
            IsPending = false;
            return;
        }

        double now = Time.realtimeSinceStartupAsDouble;
        if (double.IsNaN(_finishedAt))
        {
            _finishedAt = now;
            LastSessionResult.Capture();
            IsPending = true;
        }

        if (now - _finishedAt >= delaySeconds) LoadResultScene();
    }

    void LoadResultScene()
    {
        _loading = true;
        if (string.IsNullOrEmpty(resultSceneName) || !Application.CanStreamedLevelBeLoaded(resultSceneName))
        {
            Debug.LogWarning($"[ResultScene] リザルトのシーン「{resultSceneName}」が Build Settings にないので、移れません", this);
            IsPending = false;
            return;
        }

        Debug.Log($"[ResultScene] リザルトへ移ります → {resultSceneName}", this);
        Time.timeScale = 1f;
        SceneManager.LoadScene(resultSceneName, LoadSceneMode.Single);
    }
}
