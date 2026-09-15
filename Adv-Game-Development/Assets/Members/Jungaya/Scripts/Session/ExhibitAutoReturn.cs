using UnityEngine;
using UnityEngine.SceneManagement;

/*
    展示の自動復帰（#65 / 企画書 v8 11章「リザルト／自動復帰」、13章「1試遊サイクル」リザルト 20秒、17章 T7）

    ・リザルトを出してから returnAfterResultSeconds（20秒）たったら、タイトルのシーンへもどる（アテンドが何もしなくても次の人が遊べる）
    ・アテンドが returnKey（F12）を holdSeconds（1.5秒）長押しすると、プレイ中でもすぐタイトルへもどる
      （画面は動いているのに進まない・おかしくなった、のときの手順1。まちがって押しても長押しでないと効かない）
    ・タイトルのシーンが Build Settings に入っていないときは、何もしないで警告だけ出す
    ・時間は Time.realtimeSinceStartup で数える（ポーズで timeScale が 0 でも進む）
    アプリそのものが固まったときは中からは直せないので、ExhibitHeartbeat と Tools/exhibit_watchdog.ps1 が再起動する
*/
public class ExhibitAutoReturn : MonoBehaviour
{
    [Tooltip("もどる先のタイトルのシーン名（Build Settings に入っていること）。小林さんの Start.unity なら Start")]
    [SerializeField] string titleSceneName = "Start";
    [Tooltip("リザルトを出してからタイトルへもどるまでの秒（13章 1試遊サイクルのリザルト 20秒）。0 以下ならもどらない")]
    [SerializeField] float returnAfterResultSeconds = 20f;
    [Tooltip("アテンドがすぐタイトルへもどすキー（None で無効）")]
    [SerializeField] KeyCode returnKey = KeyCode.F12;
    [SerializeField, Min(0f)] float holdSeconds = 1.5f;

    double _finishedAt = double.NaN;
    double _keyDownAt = double.NaN;
    bool _returning;
    bool _warned;

    public string TitleSceneName => titleSceneName;

    // リザルトからタイトルへもどるまでの残り秒。リザルト中でなければ NaN
    public double SecondsUntilReturn =>
        double.IsNaN(_finishedAt) || returnAfterResultSeconds <= 0f
            ? double.NaN
            : System.Math.Max(0.0, returnAfterResultSeconds - (Time.realtimeSinceStartupAsDouble - _finishedAt));

    void Update()
    {
        if (_returning) return;

        double now = Time.realtimeSinceStartupAsDouble;
        GameSession session = GameSession.Instance;
        bool finished = session != null && session.IsFinished;
        if (!finished) _finishedAt = double.NaN;
        else if (double.IsNaN(_finishedAt)) _finishedAt = now;

        if (finished && returnAfterResultSeconds > 0f && now - _finishedAt >= returnAfterResultSeconds)
        {
            ReturnToTitle($"リザルトから {returnAfterResultSeconds:0}秒");
            return;
        }

        if (returnKey == KeyCode.None) return;
        if (!Input.GetKey(returnKey))
        {
            _keyDownAt = double.NaN;
            return;
        }
        if (double.IsNaN(_keyDownAt)) _keyDownAt = now;
        if (now - _keyDownAt >= holdSeconds)
            ReturnToTitle($"{returnKey} の長押し");
    }

    // タイトルへもどる。もどれなかったら false
    public bool ReturnToTitle(string reason)
    {
        if (_returning) return true;

        if (string.IsNullOrEmpty(titleSceneName) || !Application.CanStreamedLevelBeLoaded(titleSceneName))
        {
            if (!_warned)
                Debug.LogWarning($"[AutoReturn] タイトルのシーン「{titleSceneName}」が Build Settings にないので、もどれません（{reason}）", this);
            _warned = true;
            return false;
        }

        _returning = true;
        Debug.Log($"[AutoReturn] タイトルへもどります（{reason}）→ {titleSceneName}", this);
        Time.timeScale = 1f;
        SceneManager.LoadScene(titleSceneName, LoadSceneMode.Single);
        return true;
    }
}
