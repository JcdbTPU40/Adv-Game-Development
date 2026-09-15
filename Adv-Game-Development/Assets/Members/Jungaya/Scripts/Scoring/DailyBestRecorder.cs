using System;
using UnityEngine;

/*
    今日のベストスコアを、スコアが固定されたときに登録するクラス（#61）

    ・ScoreManager.onScoreLocked（3:00 のあと受理済みの弾がぜんぶ落ちた）で、そのプレイの縁を DailyBestScore に登録する
    ・ScoreBoardHud（プレイ中）と ResultScreen（リザルト）はここから読む。シーンになければ HUD が自動で足す
    ・BestBeforeThisPlay は「このプレイを始めたときのベスト」。プレイ中の HUD は、今の縁がこれをこえたら「更新中」を出す
*/
public class DailyBestRecorder : MonoBehaviour
{
    // 保存しないとき（persist = OFF）の入れ物
    class MemoryStore : IBestScoreStore
    {
        string _date = "";
        int _best;
        public string LoadDate() => _date;
        public int LoadBest() => _best;
        public void Save(string date, int best) { _date = date; _best = best; }
    }

    public static DailyBestRecorder Instance { get; private set; }

    [Tooltip("ON なら PlayerPrefs に保存して、アプリを閉じても同じ日なら残す。OFF ならこのアプリを起動している間だけ覚える（テスト用）。")]
    [SerializeField] bool persist = true;

    // ベストや「新記録か」が変わった
    public event Action onBestChanged;

    // 今日のベスト（今のプレイを登録したあとの値）
    public int TodayBest { get; private set; }
    // このプレイを始めたときの今日のベスト（プレイ中の比べる相手）
    public int BestBeforeThisPlay { get; private set; }
    // さっき固定したプレイでベストを更新したか（リザルトで大きく祝う）
    public bool LastPlayWasNewRecord { get; private set; }
    // さっき固定したプレイの縁
    public int LastSubmittedScore { get; private set; }

    DailyBestScore _best;
    bool _subscribed;

    // シーンにあればそれを、なければ新しく作って返す
    public static DailyBestRecorder Ensure()
    {
        if (Instance != null) return Instance;

        DailyBestRecorder found = FindAnyObjectByType<DailyBestRecorder>();
        if (found != null)
        {
            found.Init();
            return found;
        }

        return new GameObject("DailyBestRecorder").AddComponent<DailyBestRecorder>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Init();
    }

    void Init()
    {
        if (_best != null) return;
        Instance = this;
        _best = new DailyBestScore(persist ? (IBestScoreStore)new PlayerPrefsBestScoreStore() : new MemoryStore());
        TodayBest = _best.TodayBest;
        BestBeforeThisPlay = TodayBest;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        // 動く順番のせいで、まだ ScoreManager ができていなかったときのための予備
        if (!_subscribed) TrySubscribe();
    }

    void OnDisable()
    {
        if (_subscribed && ScoreManager.Instance != null)
        {
            ScoreManager.Instance.onScoreLocked -= OnScoreLocked;
            ScoreManager.Instance.onReset -= OnScoreReset;
        }
        _subscribed = false;
    }

    void TrySubscribe()
    {
        ScoreManager sm = ScoreManager.Instance;
        if (_subscribed || sm == null) return;
        sm.onScoreLocked += OnScoreLocked;
        sm.onReset += OnScoreReset;
        _subscribed = true;
    }

    void OnScoreLocked(int en)
    {
        Init();
        LastSubmittedScore = en;
        LastPlayWasNewRecord = _best.Submit(en);
        TodayBest = _best.TodayBest;
        Debug.Log(LastPlayWasNewRecord
            ? $"[DailyBest] 今日のベスト更新！ {BestBeforeThisPlay} → {TodayBest}"
            : $"[DailyBest] 今日のベスト {TodayBest}（今回 {en}）");
        onBestChanged?.Invoke();
    }

    // リトライで次のプレイが始まる。日付が変わっていたらここで 0 にもどる
    void OnScoreReset()
    {
        Init();
        LastPlayWasNewRecord = false;
        LastSubmittedScore = 0;
        TodayBest = _best.TodayBest;
        BestBeforeThisPlay = TodayBest;
        onBestChanged?.Invoke();
    }
}
