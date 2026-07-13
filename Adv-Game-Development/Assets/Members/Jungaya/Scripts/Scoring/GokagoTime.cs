using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ご加護タイム（フィーバー）— Issue #29
///
/// 企画書7章。専用ゲージは増やさず「連続救済コンボが一定数たまったら発動」。
/// 一時的な高得点チャンス。発動中は天候を強制晴朗化（フック呼び出しのみ）。
///
/// ・ScoreManager.onComboChanged(#31) を購読し、コンボが閾値に達したら発動。
/// ・発動中は一定時間、既存 Multiplier とは別係数（scoreMultiplier）を乗算。
/// ・onGokagoStart / onGokagoEnd を公開（HUD/SE/天候フック用）。
/// ・再発動は「一度コンボが閾値未満に落ちてから再び到達」で行う（発動しっぱなし防止）。
///
/// ScoreManager と同じ GameObject（または任意の場所）に1つ置く。
/// </summary>
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

    /// <summary>発動中かどうか（HUD用）。</summary>
    public bool IsActive { get; private set; }
    /// <summary>残り時間（秒、HUD用）。非発動中は0。</summary>
    public float Remaining { get; private set; }

    bool _subscribed;
    bool _armed = true; // 閾値未満に落ちると再武装される

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
        // 実行順の都合で Start 時に ScoreManager が未生成だった場合の保険
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
            _armed = true; // 一度落ちたら次の到達で再発動できる
            return;
        }

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

        // 天候の強制晴朗化（フックを呼ぶだけ。天候本体は未実装でOK）
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
        // リトライ時（#32）は即時解除
        Deactivate();
        _armed = true;
    }
}
