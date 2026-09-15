using UnityEngine;
using UnityEngine.Events;

// 神社のランク（低い → 高い）。評価の値から出す
public enum ShrineRank
{
    C = 0,
    B = 1,
    A = 2,
    S = 3,
}

/*
    神社の評価とランク（#30 / #61）

    企画書1章。救うと評価がたまって、怒らせると減る。縁＝その場のスコア、評価＝プレイ全体の通知表

    #61: 企画書 v8 7章「神社の評価・ランク表示」、付録B RANK.* にそろえた
      ・評価値は 0〜300（下限0・上限300で止まる）。展示ビルドでは毎プレイ評価値 0・ランク C から始める
      ・救済成功 +10/+15/+25、黒客化 −20/−30（客の種類ごと。付録B B-1）、黒客に通常弾を当てた −15、誤投擲 0
      ・ランクは昇格と降格で別のしきい値（RankLadder）。境界でランク表示がちらつかない
      ・ランクは得点の倍率・人数・D の進み方に使わない（付録B「ランク すべて×1.0」）。前にあった縁の倍率はなくした
      ・HUD には今のランク、リザルトの称号には「プレイ中の最高ランク」（MaxRank）を出す
        終盤の負荷ウェーブで黒客が続いて評価が下がっても、山場の崩れで達成の記録を消さないため
      ・3:00 でスコアを固定したら、評価も固定する（Lock）

    #65: ランクC停滞タイマー（11章）。ランク C のままの秒を、競技中（0:30〜3:00）に時計が進んだぶんだけ数える
      学習中・ポーズ・リザルト・通信の復帰中は足さない。B 以上に上がった瞬間に 0。動的難易度（×1.2）はまだ使わない（MVP 後の追加）

    ・シーンに1つ置くシングルトン（1セッション＝1ゲームの間、値を持っておく）
    ・客を救えた・黒客になったは、ShrineRatingHook が CustomerState（#54）の
      onRescued / onBlack を受け取って Register○○() を呼んでくる
*/
public class ShrineRating : MonoBehaviour
{
    public static ShrineRating Instance { get; private set; }

    [Header("評価値（付録B RANK.VALUE）")]
    [Tooltip("評価値の上限。付録B RANK.VALUE は 0〜300。")]
    [SerializeField] float ratingMax = 300f;
    [Tooltip("ゲーム開始時の評価値。展示ビルドでは毎プレイ 0・ランクCスタート（7章）。")]
    [SerializeField] float initialRating = 0f;

    [Header("増減量（客種ごとの値が渡されなかったときの予備。付録B B-1）")]
    [Tooltip("救済成功1人あたりの加点（通常客の値）。")]
    [SerializeField] float fallbackRescueGain = 10f;
    [Tooltip("黒客化1人あたりの減点（通常客の値。プラスで書く）。")]
    [SerializeField] float fallbackBlackLoss = 20f;
    [Tooltip("黒客に通常弾を当てたときの減点（7章 評価値の増減。プラスで書く）。")]
    [SerializeField] float blackShotLoss = 15f;

    [Header("ランクのしきい値（付録B RANK.UP / RANK.DOWN）")]
    [SerializeField] RankThresholds thresholds = RankThresholds.Default;

    [Header("イベント（HUD/SE/演出用）")]
    [Tooltip("評価が変化した（引数: 0〜1 の正規化評価値）。")]
    public UnityEvent<float> onRatingChanged;
    [Tooltip("今のランクが変化した（引数: 新しいランク）。")]
    public UnityEvent<ShrineRank> onRankChanged;
    [Tooltip("プレイ中の最高ランクが上がった（引数: 新しい最高ランク）。リザルトの称号になる。")]
    public UnityEvent<ShrineRank> onMaxRankChanged;

    float _rating;
    ShrineRank _rank;

    // 今の評価の値（0〜RatingMax）
    public float Rating => _rating;
    // 評価の値の上限
    public float RatingMax => ratingMax;
    // 今の評価の値（0〜1 に直したもの。HUD のゲージ用）
    public float RatingNormalized => ratingMax > 0f ? _rating / ratingMax : 0f;
    // 今の神社のランク。HUD はこれを表示する
    public ShrineRank Rank => _rank;
    // プレイ中にとどいたいちばん高いランク。リザルトの称号はこれを表示する（7章 v7の変更）
    public ShrineRank MaxRank { get; private set; }
    // 最高ランクにとどいた時刻（セッション開始からの秒。19章の記録用）。C のままなら 0
    public float MaxRankReachedSeconds { get; private set; }
    // 評価を固定したか（3:00 の解決が終わった）
    public bool IsLocked { get; private set; }
    // ランクのしきい値
    public RankThresholds Thresholds => thresholds;
    // ランクC停滞タイマーの秒（#65 / 11章）。動的難易度を足すときはここを読む
    public float RankCStallSeconds => (float)_stall.Seconds;
    // ランク C で 30秒以上停滞しているか（7章「見えない救済」の条件。今は読めるだけで、難易度は変えない）
    public bool IsRankCStalled => _stall.IsStalled();

    readonly RankCStallTimer _stall = new RankCStallTimer();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        ApplyInitial();
        CheckSettings();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

#if UNITY_EDITOR
    // Inspector で設定の値を変えたときのチェック（エディタだけ）
    void OnValidate()
    {
        CheckSettings();
    }
#endif

    void Start()
    {
        // 最初の値を HUD に知らせる
        NotifyAll();
    }

    void Update()
    {
        // #65: GameSession が「競技中に時計が進んだ秒」を渡してくる（学習中・ポーズ・リザルト・通信の復帰中は 0）
        GameSession session = GameSession.Instance;
        if (!IsLocked) _stall.Tick(session != null ? session.CompetitionDeltaSeconds : Time.deltaTime, _rank);
    }

    /*
        救えた → 評価を増やす。ShrineRatingHook から呼ばれる
        gain: 客の種類ごとの増やす量（付録B B-1）。0以下ならこのコンポーネントの予備の値を使う
    */
    public void RegisterResolved(float gain = 0f) => Modify(+(gain > 0f ? gain : fallbackRescueGain), "救済成功");

    /*
        黒客になった（救えなかった）→ 評価を減らす。ShrineRatingHook から呼ばれる
        loss: 客の種類ごとの減らす量（プラスの値。付録B B-1）。0以下ならこのコンポーネントの予備の値を使う
    */
    public void RegisterAngry(float loss = 0f) => Modify(-(loss > 0f ? loss : fallbackBlackLoss), "黒客化");

    // 黒客に通常弾を当てた → 評価を減らす（7章 −15）。OmamoriHitResolver から呼ばれる
    public void RegisterBlackShot() => Modify(-blackShotLoss, "黒客に通常弾");

    // 評価を固定する（3:00 の解決が終わった。GameSession が呼ぶ）
    public void Lock()
    {
        if (IsLocked) return;
        IsLocked = true;
        Debug.Log($"[Rating] 評価固定 : {_rating:0}/{ratingMax:0}（今のランク {_rank} / 最高ランク {MaxRank}）");
    }

    // 評価を最初の値にもどす（リトライ用。GameSession #32 が呼ぶ）。固定も外す
    public void ResetAll()
    {
        IsLocked = false;
        ApplyInitial();
        Debug.Log("[Rating] Reset");
        NotifyAll();
    }

    void ApplyInitial()
    {
        _rating = Mathf.Clamp(initialRating, 0f, ratingMax);
        _rank = RankLadder.Next(ShrineRank.C, _rating, thresholds);
        MaxRank = _rank;
        MaxRankReachedSeconds = 0f;
        _stall.Reset();
    }

    void Modify(float delta, string reason)
    {
        if (IsLocked) return;

        float before = _rating;
        _rating = Mathf.Clamp(_rating + delta, 0f, ratingMax);
        if (Mathf.Approximately(before, _rating)) return;

        ShrineRank newRank = RankLadder.Next(_rank, _rating, thresholds);
        Debug.Log($"[Rating] {reason} {(delta >= 0 ? "+" : "")}{delta} → {_rating:0}/{ratingMax:0}（ランク {newRank}）");

        onRatingChanged?.Invoke(RatingNormalized);

        if (newRank != _rank)
        {
            _rank = newRank;
            _stall.OnRankChanged(_rank);
            Debug.Log($"[Rating] ランク変化 → {_rank}");
            onRankChanged?.Invoke(_rank);
        }

        if (_rank > MaxRank)
        {
            MaxRank = _rank;
            GameSession session = GameSession.Instance;
            MaxRankReachedSeconds = session != null ? session.ElapsedSeconds : Time.timeSinceLevelLoad;
            Debug.Log($"[Rating] 最高ランク更新 → {MaxRank}（{MaxRankReachedSeconds:0.0}秒）");
            onMaxRankChanged?.Invoke(MaxRank);
        }
    }

    void NotifyAll()
    {
        onRatingChanged?.Invoke(RatingNormalized);
        onRankChanged?.Invoke(_rank);
        onMaxRankChanged?.Invoke(MaxRank);
    }

    void CheckSettings()
    {
        if (!RankLadder.IsValid(thresholds, ratingMax, out string error))
            Debug.LogWarning($"[Rating] ランクのしきい値がおかしいです: {error}（付録B RANK.UP / RANK.DOWN を確認してください）", this);

        // 7章: 毎回必ずランクCからスタート
        if (RankLadder.Next(ShrineRank.C, Mathf.Clamp(initialRating, 0f, ratingMax), thresholds) != ShrineRank.C)
            Debug.LogWarning($"[Rating] 開始時の評価値 {initialRating} ではランク C から始まりません（7章 違反）。C→B の昇格の値 {thresholds.promoteToB} 未満にしてください。", this);
    }
}
