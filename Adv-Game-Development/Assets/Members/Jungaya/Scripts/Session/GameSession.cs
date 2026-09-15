using UnityEngine;
using UnityEngine.Events;
using Toufuku.Aim;

/*
    3分で1ゲームのセッションを管理するクラス（#32 / #61）

    企画書8章・7章。現実の1分＝ゲームの中の1か月、3か月＝3分で1ゲームが終わる
    これが通しでプレイするときの背骨。シーンに1つ置く

    ・たった時間で月（1分=1か月）を進めて、totalMonths か月で終わる
    ・今の月と残り時間は CurrentMonth / RemainingSeconds と onMonthChanged で外から見られる（HUD 用）
    ・リザルトの表示は ResultScreen（テスト用は SessionHud）の担当。リトライは Retry() を呼ぶ
    ・月ごとに客の組み合わせが変わったり、お祭りがあったりするのは onMonthChanged でつなぐ（作るのは別の Issue）

    #61: 3:00 境界の処理順（企画書 v8 7章、付録B GAME.END。決まりは SessionBoundary）
      プレイ中（IsPlaying）
        → 3:00: 入力・スポーン・危険度の進行を止める（IsPlaying = false、IsResolving = true、onTimeUp）
        → 受理済みの弾が落ちるのを待つ（最長 3:00.65）。その弾が生んだ救済得点は入る。3:00 以後の伝播は入らない
        → 弾がぜんぶ落ちた: スコアと評価を固定して、リザルトへ（IsResolving = false、IsFinished = true、onSessionEnd）
      時計はいまはフレームの deltaTime を足した値。単調増加時計・自動復帰・障害処理は #65 でさしかえる
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

    [Header("3:00 境界（#61 / 付録B GAME.END）")]
    [Tooltip("受理済みの弾が落ちずに残ったとき（途中で壊された等）の保険。解決の締切（終了+0.65秒）から、さらにこの秒数待ったら強制的にスコアを固定する。")]
    [SerializeField] float resolveGraceSeconds = 0.5f;

    [Header("イベント（HUD/祭事/客構成フック用）")]
    [Tooltip("月が変わった（引数: 新しい月 1〜totalMonths）。月ごとの客構成変化・祭事はここに繋ぐ（別Issue）。")]
    public UnityEvent<int> onMonthChanged;
    public UnityEvent onSessionStart;
    [Tooltip("3:00 になった（入力とスポーンが止まる）。スコアはまだ固定していない。")]
    public UnityEvent onTimeUp;
    [Tooltip("受理済みの弾がすべて落ちて、スコアと評価を固定した。リザルトを出すのはここ。")]
    public UnityEvent onSessionEnd;

    // プレイ中かどうか。false の間は入力と客を出すのと危険度の進行を止める
    public bool IsPlaying { get; private set; }
    // 3:00 をすぎて、受理済みの弾が落ちるのを待っているか（#61）
    public bool IsResolving { get; private set; }
    // スコアを固定して、リザルトを表示しているかどうか
    public bool IsFinished { get; private set; }
    // 受理済みの弾の着弾をスコアに入れてよいか（3:00 までと、3:00 のあとの解決中）
    public bool ScoresAcceptedShots => IsPlaying || IsResolving;
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

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (autoStart) StartSession();
    }

    void Update()
    {
        if (IsPlaying)
        {
            _elapsed += Time.deltaTime;
            UpdateMonth();

            if (!SessionBoundary.AcceptsSwing(_elapsed, TotalSeconds))
                TimeUp();
        }
        else if (IsResolving)
        {
            _elapsed += Time.deltaTime;
        }

        if (IsResolving && SessionBoundary.ShouldLock(_elapsed, TotalSeconds, OmamoriProjectile.PendingSessionShots, resolveGraceSeconds))
            Finish();
    }

    void UpdateMonth()
    {
        // 月を進める（1分=1か月）
        int month = Mathf.Min(totalMonths, Mathf.FloorToInt(_elapsed / Mathf.Max(0.01f, secondsPerMonth)) + 1);
        if (month == CurrentMonth) return;

        CurrentMonth = month;
        Debug.Log($"[Session] {CurrentMonth}ヶ月目に入った（残り {RemainingSeconds:0}秒）");
        onMonthChanged?.Invoke(CurrentMonth); // お祭りや客の組み合わせが変わるときのフック（別の Issue）
    }

    // セッションを始める（リトライのときも使う）
    public void StartSession()
    {
        _elapsed = 0f;
        CurrentMonth = 1;
        IsPlaying = true;
        IsResolving = false;
        IsFinished = false;

        Debug.Log($"[Session] 開始（{totalMonths}ヶ月 / {TotalSeconds:0}秒）");
        onSessionStart?.Invoke();
        onMonthChanged?.Invoke(CurrentMonth);
    }

    /*
        すぐにゲームを終わる（デバッグ用）。受理済みの弾を待たないで、スコアを固定してリザルトへ
        ふつうの 3:00 の終わり方は Update が TimeUp → Finish の順で進める
    */
    public void EndSession()
    {
        if (!IsPlaying && !IsResolving) return;
        IsPlaying = false;
        Finish();
    }

    // 3:00 になった。入力・スポーン・危険度の進行を止めて、受理済みの弾を待つ
    void TimeUp()
    {
        IsPlaying = false;
        IsResolving = true;

        Debug.Log($"[Session] 3:00 入力締切（{_elapsed:0.000}秒）: 飛んでいる受理済みの弾 {OmamoriProjectile.PendingSessionShots}発を解決します");
        onTimeUp?.Invoke();
    }

    // 受理済みの弾がぜんぶ落ちた。スコアと評価を固定してリザルトへ
    void Finish()
    {
        IsPlaying = false;
        IsResolving = false;
        IsFinished = true;

        var sm = ScoreManager.Instance;
        var rating = ShrineRating.Instance;
        if (sm != null) sm.LockScore();
        if (rating != null) rating.Lock();

        Debug.Log($"[Session] 終了！（{_elapsed:0.000}秒でスコア固定）リザルト → 縁 {(sm != null ? sm.En : 0)}" +
                  $" / 神社の称号（最高ランク） {(rating != null ? rating.MaxRank.ToString() : "-")}" +
                  $" / 最大の福の連なり {(sm != null ? sm.MaxCombo : 0)}");

        onSessionEnd?.Invoke();
    }

    /*
        リトライ。スコア（#22）と評価（#30）をリセットして、残っている客を帰らせてからやりなおす
        リザルト画面のリトライボタンから呼ばれる
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
