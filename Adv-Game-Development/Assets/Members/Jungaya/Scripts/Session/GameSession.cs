using System;
using UnityEngine;
using UnityEngine.Events;
using Toufuku.Aim;
using Toufuku.GameInput;
using Toufuku.Playtest;

// 時計を止めている理由（#65）。いくつ重なってもよく、ぜんぶ外れたら時計が進む
[Flags]
public enum SessionHoldReason
{
    None = 0,
    Paused = 1 << 0,       // アテンドの一時停止（pauseKey）
    LinkRecovery = 1 << 1  // コントローラーの通信の復帰中（ControllerLinkSupervisor）
}

/*
    3分で1ゲームのセッションを管理するクラス（#32 / #61 / #65）

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

    #65: 時計・障害処理
      ・時計は SessionClock。Unity の単調増加時計（Time.realtimeSinceStartupAsDouble）の差で進める。フレームの deltaTime・timeScale は使わない
      ・振りを受け付けるかは、振りを受け取った時刻で決める（AcceptsSwingAt）。3:00 をまたいだフレームでも、180.000秒未満に受け取った振りだけが弾になる
      ・危険度 D・スポーン・ご加護の残り時間は PlayDeltaSeconds（3:00.000 までに進んだ秒）、C停滞タイマーは CompetitionDeltaSeconds（0:30〜3:00）で進める
        フレームの区切りがどうでも合計は同じなので、低いフレームレートでも境界の結果が変わらない
      ・止める理由（アテンドのポーズ・通信の復帰中）があるときは時計が進まない（Hold / Release）。そのあいだ振りも受け付けない
        freezeWorldWhileHeld なら Time.timeScale も 0 にして、客の歩行や弾の飛翔も止める
      ・3:00境界ログ（19章）を集めて、スコアを固定したときに LastBoundaryReport に置く
      ・この GameSession は入力（-100）や通信（-200）より先に動かす（-300）。フレームの始めに時計を進めてから、そのフレームの入力を判定するため
*/
[DefaultExecutionOrder(-300)]
public class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }

    // #65: このフレームで進んだ「プレイの秒」（3:00.000 まで。止めている間・3:00 以後は 0）。GameSession がないシーンでは Time.deltaTime
    public static float PlayDeltaTime => Instance != null ? Instance.PlayDeltaSeconds : Time.deltaTime;

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

    [Header("学習と競技の区切り（8章）")]
    [Tooltip("開始からこの秒数は学習専用（競技計時は 0:30.000 から）。ランクC停滞タイマーと段階学習（#58 StagedLearningDirector）が見る。")]
    [SerializeField, Min(0f)] float learningSeconds = 30f;

    [Header("時計（#65）")]
    [Tooltip("1フレームでこれより長く時間があいたら、こえたぶんはフリーズやロードの引っかかりとみなして時計に足さない。")]
    [SerializeField, Min(0.05f)] float maxClockStepSeconds = (float)SessionClock.DefaultMaxStepSeconds;
    [Tooltip("ポーズ・通信の復帰中は Time.timeScale を 0 にして、客の歩行や弾の飛翔も止める。")]
    [SerializeField] bool freezeWorldWhileHeld = true;
    [Tooltip("アテンド用の一時停止キー（プレイ中だけ効く。None で無効）。")]
    [SerializeField] KeyCode pauseKey = KeyCode.F9;
    [Tooltip("3:00境界ログ（19章）を Console に出す。")]
    [SerializeField] bool logBoundaryReport = true;

    [Header("イベント（HUD/祭事/客構成フック用）")]
    [Tooltip("月が変わった（引数: 新しい月 1〜totalMonths）。月ごとの客構成変化・祭事はここに繋ぐ（別Issue）。")]
    public UnityEvent<int> onMonthChanged;
    public UnityEvent onSessionStart;
    [Tooltip("3:00 になった（入力とスポーンが止まる）。スコアはまだ固定していない。")]
    public UnityEvent onTimeUp;
    [Tooltip("受理済みの弾がすべて落ちて、スコアと評価を固定した。リザルトを出すのはここ。")]
    public UnityEvent onSessionEnd;

    // #65: 時計を止めた・再開した（引数: 今止めている理由。None なら動いている）
    public event Action<SessionHoldReason> HoldChanged;

    /*
        #58: 時計が learningSeconds（0:30.000）に届いた。1プレイに1回。引数: 学習の秒数（=競技の始まりの時計の秒）
        このクラスは入力（-100）より先に動くので、受け取った側がカウンタを初期化すると、同じフレームの着弾から競技として数えられる
    */
    public event Action<double> CompetitionStarted;

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
    public float RemainingSeconds => Mathf.Max(0f, TotalSeconds - ElapsedSeconds);
    // セッションが始まってからたった時間（秒）。#63 の計測ログの時刻（T3 の区間分け）に使う。止めていた時間は入らない
    public float ElapsedSeconds => (float)_clock.Elapsed;
    // #65: ElapsedSeconds の double 版（境界の判定・ログ用）
    public double ElapsedTime => _clock.Elapsed;
    // 1ゲームの全部の時間（秒）
    public float TotalSeconds => secondsPerMonth * totalMonths;
    // 全部の月の数（HUD の表示用）
    public int TotalMonths => totalMonths;

    // #65: 学習専用の秒数（8章 0:00〜0:30）と、今が学習中か
    public float LearningSeconds => learningSeconds;
    public bool IsLearning => IsPlaying && _clock.Elapsed < learningSeconds;
    // #58: このプレイで 0:30.000 を通って、競技が始まったか（CompetitionStarted を出したか）
    public bool HasCompetitionStarted { get; private set; }
    // #65: このフレームで進んだプレイの秒（[0, 3:00) と重なるぶん）。危険度 D・スポーン・ご加護の残り時間に使う
    public float PlayDeltaSeconds { get; private set; }
    // #65: このフレームで進んだ競技の秒（[0:30, 3:00) と重なるぶん）。ランクC停滞タイマーに使う
    public float CompetitionDeltaSeconds { get; private set; }
    // #65: 今時計を止めている理由
    public SessionHoldReason HoldReasons => (SessionHoldReason)_clock.HoldMask;
    public bool IsHeld => _clock.IsHeld;
    // #65: 引っかかり（1フレームで maxClockStepSeconds をこえた）とみなして時計に足さなかった秒の合計
    public double StalledSeconds => _clock.StalledSeconds;
    public KeyCode PauseKey => pauseKey;
    // #65: いちばん新しいプレイの3:00境界ログ（19章）。3:00 を通ってスコアを固定するまでは null
    public SessionBoundaryReport LastBoundaryReport { get; private set; }

    readonly SessionClock _clock = new SessionClock();
    double _lastUpdateElapsed;
    SessionBoundaryReport _report = new SessionBoundaryReport();
    ThrowInputController _input;
    float _timeScaleBeforeHold = 1f;
    bool _frozeWorld;

    static double Now => Time.realtimeSinceStartupAsDouble;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        OmamoriProjectile.AnyLanded += HandleProjectileLanded;
    }

    void OnDisable()
    {
        OmamoriProjectile.AnyLanded -= HandleProjectileLanded;
    }

    void OnDestroy()
    {
        UnhookInput();
        // 止めたままシーンが変わっても（タイトルへの自動復帰など）、時間の流れをもとにもどす
        RestoreWorld();
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        HookInput();
        if (autoStart) StartSession();
    }

    void Update()
    {
        // アテンドの一時停止はプレイ中（解決中もふくむ）だけ。止めているときは解除だけできる
        if (pauseKey != KeyCode.None && Input.GetKeyDown(pauseKey)
            && (IsPlaying || IsResolving || (HoldReasons & SessionHoldReason.Paused) != 0))
            TogglePause();

        if (IsPlaying || IsResolving) _clock.Advance(Now);

        double before = _lastUpdateElapsed;
        double after = _clock.Elapsed;
        _lastUpdateElapsed = after;
        PlayDeltaSeconds = IsPlaying ? (float)SessionBoundary.Overlap(before, after, 0.0, TotalSeconds) : 0f;
        CompetitionDeltaSeconds = IsPlaying ? (float)SessionBoundary.Overlap(before, after, learningSeconds, TotalSeconds) : 0f;

        if (IsPlaying)
        {
            // #58: 0:30.000 に届いたフレームで1回だけ知らせる（学習の時間が 0 なら最初のフレーム）
            if (!HasCompetitionStarted && after >= learningSeconds)
            {
                HasCompetitionStarted = true;
                Debug.Log($"[Session] 競技開始 {learningSeconds:0.000}秒（見つけたフレーム {after:0.000}秒）");
                CompetitionStarted?.Invoke(learningSeconds);
            }

            UpdateMonth();

            if (!SessionBoundary.AcceptsSwing(after, TotalSeconds))
                TimeUp();
        }

        if (IsResolving && SessionBoundary.ShouldLock(after, TotalSeconds, OmamoriProjectile.PendingSessionShots, resolveGraceSeconds))
            Finish();
    }

    void UpdateMonth()
    {
        // 月を進める（1分=1か月）
        int month = Mathf.Min(totalMonths, Mathf.FloorToInt(ElapsedSeconds / Mathf.Max(0.01f, secondsPerMonth)) + 1);
        if (month == CurrentMonth) return;

        CurrentMonth = month;
        Debug.Log($"[Session] {CurrentMonth}ヶ月目に入った（残り {RemainingSeconds:0}秒）");
        onMonthChanged?.Invoke(CurrentMonth); // お祭りや客の組み合わせが変わるときのフック（別の Issue）
    }

    // セッションを始める（リトライのときも使う）
    public void StartSession()
    {
        _clock.MaxStepSeconds = Math.Max(0.05, maxClockStepSeconds);
        _clock.Start(Now);
        _lastUpdateElapsed = 0.0;
        PlayDeltaSeconds = 0f;
        CompetitionDeltaSeconds = 0f;
        _report = new SessionBoundaryReport();
        LastBoundaryReport = null;

        CurrentMonth = 1;
        IsPlaying = true;
        IsResolving = false;
        IsFinished = false;
        HasCompetitionStarted = false;

        Debug.Log($"[Session] 開始（{totalMonths}ヶ月 / {TotalSeconds:0}秒）" + (IsHeld ? $" 時計は {HoldReasons} で止めています" : ""));
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

    /*
        #65: 実時間 realtime（Time.realtimeSinceStartupAsDouble）に受け取った振りを、弾にしてよいか
        プレイ中で、時計を止めていなくて、その時刻が 180.000秒未満のときだけ true（7章「SwingAccepted時刻が180.000秒未満なら受理」）
    */
    public bool AcceptsSwingAt(double realtime)
    {
        if (!IsPlaying || _clock.IsHeld) return false;
        return SessionBoundary.AcceptsSwing(_clock.ElapsedAt(realtime), TotalSeconds);
    }

    // #65: 実時間 realtime を、このセッションの時計の秒に直す
    public double SessionTimeAt(double realtime) => _clock.ElapsedAt(realtime);

    // #65: 時計の秒 contactSeconds に起きた笑顔の伝播の接触を、有効にしてよいか（180.000秒未満）
    public bool AcceptsPropagationAt(double contactSeconds)
    {
        return (IsPlaying || IsResolving) && SessionBoundary.AcceptsPropagation(contactSeconds, TotalSeconds);
    }

    // #65: 理由 reason で時計を止める（危険度・スポーン・福の連なりの5秒・C停滞・3:00 までの残り時間が進まない。振りも受け付けない）
    public void Hold(SessionHoldReason reason)
    {
        bool wasHeld = _clock.IsHeld;
        if (!_clock.Hold((int)reason, Now)) return;
        if (!wasHeld) FreezeWorld();

        Debug.Log($"[Session] 時計を止めました（{reason}）: {_clock.Elapsed:0.000}秒");
        PlaytestLog.Marker("session_hold", reason.ToString(), _clock.Elapsed);
        HoldChanged?.Invoke(HoldReasons);
    }

    // #65: 理由 reason を外す。ぜんぶ外れたら、外した時刻から時計を進める
    public void Release(SessionHoldReason reason)
    {
        if (!_clock.Release((int)reason, Now)) return;
        if (!_clock.IsHeld) RestoreWorld();

        Debug.Log($"[Session] 時計の停止を外しました（{reason}）: {_clock.Elapsed:0.000}秒" + (IsHeld ? $" まだ {HoldReasons} で止めています" : ""));
        PlaytestLog.Marker("session_release", reason.ToString(), _clock.Elapsed);
        HoldChanged?.Invoke(HoldReasons);
    }

    // #65: アテンドの一時停止を切りかえる
    public void TogglePause()
    {
        if ((HoldReasons & SessionHoldReason.Paused) != 0) Release(SessionHoldReason.Paused);
        else Hold(SessionHoldReason.Paused);
    }

    void FreezeWorld()
    {
        if (!freezeWorldWhileHeld || _frozeWorld) return;
        _timeScaleBeforeHold = Time.timeScale;
        Time.timeScale = 0f;
        _frozeWorld = true;
    }

    void RestoreWorld()
    {
        if (!_frozeWorld) return;
        Time.timeScale = _timeScaleBeforeHold;
        _frozeWorld = false;
    }

    // 3:00 になった。入力・スポーン・危険度の進行を止めて、受理済みの弾を待つ
    void TimeUp()
    {
        IsPlaying = false;
        IsResolving = true;

        ScoreManager sm = ScoreManager.Instance;
        _report.BoundaryDetectedSeconds = _clock.Elapsed;
        _report.InFlightAtBoundary = OmamoriProjectile.PendingSessionShots;
        _report.EnAtBoundary = sm != null ? sm.En : 0;
        _report.RescuesAtBoundary = sm != null ? sm.RescueCount : 0;

        Debug.Log($"[Session] 3:00 入力締切（{TotalSeconds:0.000}秒。見つけたフレーム {_clock.Elapsed:0.000}秒）: 飛んでいる受理済みの弾 {OmamoriProjectile.PendingSessionShots}発を解決します");
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

        Debug.Log($"[Session] 終了！（{_clock.Elapsed:0.000}秒でスコア固定）リザルト → 縁 {(sm != null ? sm.En : 0)}" +
                  $" / 神社の称号（最高ランク） {(rating != null ? rating.MaxRank.ToString() : "-")}" +
                  $" / 最大の福の連なり {(sm != null ? sm.MaxCombo : 0)}");

        // #65: 3:00 を通って固定したプレイだけ、3:00境界ログを残す（EndSession で途中で終えたときは残さない）
        if (!double.IsNaN(_report.BoundaryDetectedSeconds))
        {
            _report.LockSeconds = _clock.Elapsed;
            _report.EnAtLock = sm != null ? sm.En : 0;
            _report.RescuesAtLock = sm != null ? sm.RescueCount : 0;
            LastBoundaryReport = _report;
            if (logBoundaryReport) Debug.Log($"[Session] 3:00境界ログ: {_report}");
        }

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

    // ---- 3:00境界ログ（#65 / 19章）のための入力と着弾 ----

    void HookInput()
    {
        _input = FindAnyObjectByType<ThrowInputController>();
        if (_input == null) return;
        _input.SwingAccepted += HandleSwingAccepted;
        _input.SwingRejected += HandleSwingRejected;
    }

    void UnhookInput()
    {
        if (_input == null) return;
        _input.SwingAccepted -= HandleSwingAccepted;
        _input.SwingRejected -= HandleSwingRejected;
        _input = null;
    }

    void HandleSwingAccepted(SwingAcceptedArgs e)
    {
        if (IsPlaying) _report.LastAcceptedSwingSeconds = _clock.ElapsedAt(e.Time);
    }

    void HandleSwingRejected(SwingRejectedArgs e)
    {
        // 3:00 以後の振りは、ほかの理由より先に Inactive ではじかれる
        if (e.Reason != SwingRejectReason.Inactive || !(IsPlaying || IsResolving)) return;
        if (_clock.ElapsedAt(e.Time) >= TotalSeconds) _report.RejectedAfterBoundary++;
    }

    void HandleProjectileLanded(LandingResult result)
    {
        // 解決中にスコアへ入った弾は、ぜんぶ 3:00 より前に受理した弾
        if (IsResolving && result.Scored) _report.LastSettleSeconds = _clock.Elapsed;
    }
}
