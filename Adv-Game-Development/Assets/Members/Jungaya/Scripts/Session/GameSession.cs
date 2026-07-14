using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 3分1ゲームのセッション管理 — Issue #32
///
/// 企画書8章/7章。現実1分＝ゲーム内1ヶ月、三ヶ月＝3分で1ゲーム終了。
/// これが通しプレイの背骨。シーンに1つ置く。
///
/// ・経過時間 → 月(1分=1月)を進め、totalMonths ヶ月で終了。
/// ・現在の月/残り時間は CurrentMonth / RemainingSeconds と onMonthChanged で公開（HUD用）。
/// ・終了時に入力・スポーンを停止（TestShooter / RescueCustomerSpawner が IsPlaying を見る）。
/// ・リザルト表示は SessionHud（OnGUI）が担当。リトライは Retry() を呼ぶ。
/// ・月ごとの客構成変化・祭事は onMonthChanged がフック（実装は別Issue）。
/// </summary>
public class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }

    [Header("セッション設定")]
    [Tooltip("ゲーム内1ヶ月にあたる現実時間（秒）。企画書では60秒=1ヶ月。")]
    [SerializeField] float secondsPerMonth = 60f;
    [Tooltip("何ヶ月で1ゲーム終了か。企画書では3ヶ月=3分。")]
    [SerializeField] int totalMonths = 3;
    [Tooltip("ON ならシーン開始と同時にセッションを開始する。")]
    [SerializeField] bool autoStart = true;

    [Header("イベント（HUD/祭事/客構成フック用）")]
    [Tooltip("月が変わった（引数: 新しい月 1〜totalMonths）。月ごとの客構成変化・祭事はここに繋ぐ（別Issue）。")]
    public UnityEvent<int> onMonthChanged;
    public UnityEvent onSessionStart;
    public UnityEvent onSessionEnd;

    /// <summary>プレイ中か。false の間は入力・スポーンを止める。</summary>
    public bool IsPlaying { get; private set; }
    /// <summary>セッションが終了してリザルト表示中か。</summary>
    public bool IsFinished { get; private set; }
    /// <summary>現在の月（1〜totalMonths）。</summary>
    public int CurrentMonth { get; private set; } = 1;
    /// <summary>ゲーム終了までの残り時間（秒）。</summary>
    public float RemainingSeconds => Mathf.Max(0f, TotalSeconds - _elapsed);
    /// <summary>1ゲームの総時間（秒）。</summary>
    public float TotalSeconds => secondsPerMonth * totalMonths;
    /// <summary>総月数（HUD表示用）。</summary>
    public int TotalMonths => totalMonths;

    float _elapsed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (autoStart) StartSession();
    }

    void Update()
    {
        if (!IsPlaying) return;

        _elapsed += Time.deltaTime;

        // 月の更新（1分=1月）
        int month = Mathf.Min(totalMonths, Mathf.FloorToInt(_elapsed / Mathf.Max(0.01f, secondsPerMonth)) + 1);
        if (month != CurrentMonth)
        {
            CurrentMonth = month;
            Debug.Log($"[Session] {CurrentMonth}ヶ月目に入った（残り {RemainingSeconds:0}秒）");
            onMonthChanged?.Invoke(CurrentMonth); // 祭事・客構成変化のフック（別Issue）
        }

        if (_elapsed >= TotalSeconds)
            EndSession();
    }

    /// <summary>セッション開始（リトライ時も使う）。</summary>
    public void StartSession()
    {
        _elapsed = 0f;
        CurrentMonth = 1;
        IsPlaying = true;
        IsFinished = false;

        Debug.Log($"[Session] 開始（{totalMonths}ヶ月 / {TotalSeconds:0}秒）");
        onSessionStart?.Invoke();
        onMonthChanged?.Invoke(CurrentMonth);
    }

    /// <summary>時間切れによるゲーム終了。入力・スポーンが止まり、リザルトへ。</summary>
    public void EndSession()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        IsFinished = true;

        var sm = ScoreManager.Instance;
        var rating = ShrineRating.Instance;
        Debug.Log($"[Session] 終了！ リザルト → 縁 {(sm != null ? sm.En : 0)} / 神社ランク {(rating != null ? rating.Rank.ToString() : "-")} / 最大コンボ {(sm != null ? sm.MaxCombo : 0)}");

        onSessionEnd?.Invoke();
    }

    /// <summary>
    /// リトライ。スコア(#22)・評価(#30)をリセットし、残っている客を退場させて再開する。
    /// SessionHud のリトライボタンから呼ばれる。
    /// </summary>
    public void Retry()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.ResetAll(); // onReset 経由でご加護(#29)も解除される

        if (ShrineRating.Instance != null)
            ShrineRating.Instance.ResetAll();

        // 場に残っている客を一掃
        foreach (GameObject customer in GameObject.FindGameObjectsWithTag("Customer"))
            Destroy(customer);

        StartSession();
    }
}
