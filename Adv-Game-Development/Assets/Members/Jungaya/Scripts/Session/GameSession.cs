using UnityEngine;
using UnityEngine.Events;

/*
    3分で1ゲームのセッションを管理するクラス（#32）

    企画書8章・7章。現実の1分＝ゲームの中の1か月、3か月＝3分で1ゲームが終わる
    これが通しでプレイするときの背骨。シーンに1つ置く

    ・たった時間で月（1分=1か月）を進めて、totalMonths か月で終わる
    ・今の月と残り時間は CurrentMonth / RemainingSeconds と onMonthChanged で外から見られる（HUD 用）
    ・終わったら入力と客を出すのを止める（TestShooter / RescueCustomerSpawner が IsPlaying を見る）
    ・リザルトの表示は SessionHud（OnGUI）の担当。リトライは Retry() を呼ぶ
    ・月ごとに客の組み合わせが変わったり、お祭りがあったりするのは onMonthChanged でつなぐ（作るのは別の Issue）
*/
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

    // プレイ中かどうか。false の間は入力と客を出すのを止める
    public bool IsPlaying { get; private set; }
    // セッションが終わってリザルトを表示しているかどうか
    public bool IsFinished { get; private set; }
    // 今の月（1〜totalMonths）
    public int CurrentMonth { get; private set; } = 1;
    // ゲームが終わるまでの残り時間（秒）
    public float RemainingSeconds => Mathf.Max(0f, TotalSeconds - _elapsed);
    // セッションが始まってからたった時間（秒）。#63 の計測ログの時刻（T3 の区間分け）に使う
    public float ElapsedSeconds => _elapsed;
    // 1ゲームの全部の時間（秒）
    public float TotalSeconds => secondsPerMonth * totalMonths;
    // 全部の月の数（HUD の表示用）
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

        // 月を進める（1分=1か月）
        int month = Mathf.Min(totalMonths, Mathf.FloorToInt(_elapsed / Mathf.Max(0.01f, secondsPerMonth)) + 1);
        if (month != CurrentMonth)
        {
            CurrentMonth = month;
            Debug.Log($"[Session] {CurrentMonth}ヶ月目に入った（残り {RemainingSeconds:0}秒）");
            onMonthChanged?.Invoke(CurrentMonth); // お祭りや客の組み合わせが変わるときのフック（別の Issue）
        }

        if (_elapsed >= TotalSeconds)
            EndSession();
    }

    // セッションを始める（リトライのときも使う）
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

    // 時間切れでゲームを終わる。入力と客を出すのが止まって、リザルトへ
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

    /*
        リトライ。スコア（#22）と評価（#30）をリセットして、残っている客を帰らせてからやりなおす
        SessionHud のリトライボタンから呼ばれる
    */
    public void Retry()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.ResetAll(); // onReset を通してご加護（#29）もやめになる

        if (ShrineRating.Instance != null)
            ShrineRating.Instance.ResetAll();

        // その場に残っている客をぜんぶ片付ける
        foreach (GameObject customer in GameObject.FindGameObjectsWithTag("Customer"))
            Destroy(customer);

        StartSession();
    }
}
