using UnityEngine;
using UnityEngine.Events;

/// <summary>神社ランク（低→高）。評価値から算出する。</summary>
public enum ShrineRank
{
    C = 0,
    B = 1,
    A = 2,
    S = 3,
}

/// <summary>
/// 神社評価メーター（長期通信簿）— Issue #30
///
/// 企画書1章。救済で評価が溜まり、怒らせると減る。リザルトで神社ランク確定。
/// 縁＝瞬間スコア、評価＝プレイ全体の通信簿。評価が縁に倍率としてかかる。
///
/// ・シーンに1つ置くシングルトン（1セッション＝1ゲームの間、値を保持）。
/// ・客の解消/怒りは <see cref="ShrineRatingHook"/> が CustomerMood の
///   onResolved / onAngry を購読して Register○○() を呼んでくる。
/// ・過剰押し売り(#33)の微減は ScoreManager.RegisterOverSell 経由で入る。
/// ・評価→縁倍率(EnMultiplier)は ScoreManager の獲得計算に自動で乗る。
///
/// ※ 展示ビルドでは「評価低下で早期終了」は不採用（回転率優先）。
/// </summary>
public class ShrineRating : MonoBehaviour
{
    public static ShrineRating Instance { get; private set; }

    [Header("評価値")]
    [SerializeField] float maxRating = 100f;
    [Tooltip("ゲーム開始時の評価値。")]
    [SerializeField] float startRating = 50f;

    [Header("増減量")]
    [Tooltip("救済成功（解消）1人あたりの加点。")]
    [SerializeField] float resolveGain = 5f;
    [Tooltip("救済失敗（怒り）1人あたりの減点。")]
    [SerializeField] float angryLoss = 10f;
    [Tooltip("過剰押し売り（#33）1回あたりの微減。")]
    [SerializeField] float overSellLoss = 2f;

    [Header("ランク閾値（この値以上でそのランク）")]
    [SerializeField] float rankSThreshold = 80f;
    [SerializeField] float rankAThreshold = 60f;
    [SerializeField] float rankBThreshold = 40f;
    // それ未満は C

    [Header("ランク別 縁倍率（獲得計算に乗る）")]
    [SerializeField] float multiplierC = 0.8f;
    [SerializeField] float multiplierB = 1.0f;
    [SerializeField] float multiplierA = 1.2f;
    [SerializeField] float multiplierS = 1.5f;

    [Header("イベント（HUD/SE/演出用）")]
    [Tooltip("評価が変化した（引数: 0〜1 の正規化評価値）。")]
    public UnityEvent<float> onRatingChanged;
    [Tooltip("ランクが変化した（引数: 新しいランク）。")]
    public UnityEvent<ShrineRank> onRankChanged;

    float _rating;
    ShrineRank _rank;

    /// <summary>現在の評価値（0〜maxRating）。</summary>
    public float Rating => _rating;
    /// <summary>現在の評価値（0〜1 正規化。HUD用）。</summary>
    public float RatingNormalized => maxRating > 0f ? _rating / maxRating : 0f;
    /// <summary>現在の神社ランク。リザルト(#32)がこれを表示する。</summary>
    public ShrineRank Rank => _rank;

    /// <summary>評価による縁倍率。ScoreManager の獲得計算に乗る（コンボ倍率と乗算）。</summary>
    public float EnMultiplier
    {
        get
        {
            switch (_rank)
            {
                case ShrineRank.S: return multiplierS;
                case ShrineRank.A: return multiplierA;
                case ShrineRank.B: return multiplierB;
                default:           return multiplierC;
            }
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _rating = Mathf.Clamp(startRating, 0f, maxRating);
        _rank = RankOf(_rating);
    }

    void Start()
    {
        // 初期値をHUDへ通知
        onRatingChanged?.Invoke(RatingNormalized);
        onRankChanged?.Invoke(_rank);
    }

    /// <summary>救済成功（解消）→ 加点。ShrineRatingHook から呼ばれる。</summary>
    public void RegisterResolved() => Modify(+resolveGain, "解消");

    /// <summary>救済失敗（怒り）→ 減点。ShrineRatingHook から呼ばれる。</summary>
    public void RegisterAngry() => Modify(-angryLoss, "怒り");

    /// <summary>過剰押し売り（#33）→ 微減。ScoreManager から呼ばれる。</summary>
    public void RegisterOverSell() => Modify(-overSellLoss, "押し売り");

    /// <summary>評価を初期値へ戻す（リトライ用。GameSession #32 が呼ぶ）。</summary>
    public void ResetAll()
    {
        _rating = Mathf.Clamp(startRating, 0f, maxRating);
        Debug.Log("[Rating] Reset");
        ApplyChange();
    }

    void Modify(float delta, string reason)
    {
        float before = _rating;
        _rating = Mathf.Clamp(_rating + delta, 0f, maxRating);
        if (Mathf.Approximately(before, _rating)) return;

        Debug.Log($"[Rating] {reason} {(delta >= 0 ? "+" : "")}{delta} → {_rating:0}/{maxRating:0}（ランク {RankOf(_rating)} / 縁倍率 x{EnMultiplierOf(RankOf(_rating)):0.0}）");
        ApplyChange();
    }

    void ApplyChange()
    {
        onRatingChanged?.Invoke(RatingNormalized);

        ShrineRank newRank = RankOf(_rating);
        if (newRank != _rank)
        {
            _rank = newRank;
            Debug.Log($"[Rating] ランク変化 → {_rank}");
            onRankChanged?.Invoke(_rank);
        }
    }

    ShrineRank RankOf(float rating)
    {
        if (rating >= rankSThreshold) return ShrineRank.S;
        if (rating >= rankAThreshold) return ShrineRank.A;
        if (rating >= rankBThreshold) return ShrineRank.B;
        return ShrineRank.C;
    }

    float EnMultiplierOf(ShrineRank rank)
    {
        switch (rank)
        {
            case ShrineRank.S: return multiplierS;
            case ShrineRank.A: return multiplierA;
            case ShrineRank.B: return multiplierB;
            default:           return multiplierC;
        }
    }
}
