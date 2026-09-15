using UnityEngine;
using UnityEngine.Events;

/*
    ご加護タイム（フィーバー）（#29）

    企画書7章。専用のゲージは増やさないで「連続で救ったコンボが決まった数たまったら発動」する
    いっときの高得点のチャンス。発動中は天気をむりやり晴れにする（フックを呼ぶだけ）

    ・ScoreManager.onComboChanged（#31）を受け取って、コンボがしきい値に届いたら発動する
    ・発動中はしばらくの間、もとからある Multiplier とは別の倍率（scoreMultiplier）をかける
    ・onGokagoStart / onGokagoEnd を外から使えるようにしている（HUD・効果音・天気のフック用）
    ・もう一回発動するのは「一度コンボがしきい値より下がってから、また届いたとき」にする（発動しっぱなしにならないように）

    ScoreManager と同じ GameObject（またはどこでもいい）に1つ置く
*/
public class GokagoTime : MonoBehaviour
{
    [Header("発動条件")]
    [Tooltip("この連続コンボ数に達すると発動する。")]
    [SerializeField] int comboThreshold = 10;

    [Header("効果")]
    [Tooltip("発動している時間（秒）。")]
    [SerializeField] float duration = 10f;
    [Tooltip("発動中にスコアへ上乗せする倍率（既存コンボ倍率とは別係数で乗算）。")]
    [SerializeField] float scoreMultiplier = 2f;

    [Header("イベント（HUD/SE/天候フック用）")]
    public UnityEvent onGokagoStart;
    public UnityEvent onGokagoEnd;
    [Tooltip("天候の強制晴朗化フック。天候システム実装後にここへ繋ぐ（現状はログのみ）。")]
    public UnityEvent onWeatherClearRequest;

    // 発動中かどうか（HUD 用）
    public bool IsActive { get; private set; }
    // 残り時間（秒、HUD 用）。発動していないときは0
    public float Remaining { get; private set; }

    bool _subscribed;
    bool _armed = true; // しきい値より下がると、また発動できるようになる

    void Start()
    {
        TrySubscribe();
    }

    void OnDisable()
    {
        if (_subscribed && ScoreManager.Instance != null)
        {
            ScoreManager.Instance.onComboChanged -= OnComboChanged;
            ScoreManager.Instance.onReset -= OnScoreReset;
            _subscribed = false;
        }
    }

    void Update()
    {
        // 動く順番のせいで、Start のときに ScoreManager がまだ作られていなかったときのための予備
        if (!_subscribed) TrySubscribe();

        if (!IsActive) return;

        Remaining -= Time.deltaTime;
        if (Remaining <= 0f)
            Deactivate();
    }

    void TrySubscribe()
    {
        if (_subscribed || ScoreManager.Instance == null) return;
        ScoreManager.Instance.onComboChanged += OnComboChanged;
        ScoreManager.Instance.onReset += OnScoreReset;
        _subscribed = true;
    }

    void OnComboChanged(int combo)
    {
        if (combo < comboThreshold)
        {
            _armed = true; // 一度下がったら、次に届いたときにまた発動できる
            return;
        }

        // #61: 3:00 以後の救済から新しいご加護タイムを始めない（7章「3:00境界の処理順」）
        if (GameSession.Instance != null && !GameSession.Instance.IsPlaying) return;

        if (_armed && !IsActive)
            Activate();
    }

    void Activate()
    {
        IsActive = true;
        _armed = false;
        Remaining = duration;

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.SetGokagoMultiplier(scoreMultiplier);

        Debug.Log($"[Gokago] ご加護タイム発動！ {duration}秒間 スコア x{scoreMultiplier}");
        onGokagoStart?.Invoke();

        // 天気をむりやり晴れにする（フックを呼ぶだけ。天気の本体はまだなくてOK）
        Debug.Log("[Gokago] 天候フック: 強制晴朗化を要求");
        onWeatherClearRequest?.Invoke();
    }

    void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        Remaining = 0f;

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.SetGokagoMultiplier(1f);

        Debug.Log("[Gokago] ご加護タイム終了");
        onGokagoEnd?.Invoke();
    }

    void OnScoreReset()
    {
        // リトライのとき（#32）はすぐにやめる
        Deactivate();
        _armed = true;
    }
}
