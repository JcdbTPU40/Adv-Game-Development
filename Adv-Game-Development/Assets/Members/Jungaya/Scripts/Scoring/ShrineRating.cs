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
    神社の評価メーター（長い目で見た通知表）（#30）

    企画書1章。救うと評価がたまって、怒らせると減る。リザルトで神社のランクが決まる
    縁＝その場のスコア、評価＝プレイ全体の通知表。評価が縁に倍率としてかかる

    ・シーンに1つ置くシングルトン（1セッション＝1ゲームの間、値を持っておく）
    ・客を救えた・黒客になったは、ShrineRatingHook が CustomerState（#54）の
      onRescued / onBlack を受け取って Register○○() を呼んでくる。増やす量・減らす量は客の種類ごと（付録B B-1）
    ・評価から出した縁の倍率（EnMultiplier）は、ScoreManager のもらえる縁の計算に自動でかかる

    ※ 展示用のビルドでは、毎回必ずランクCからスタートする（v3 §7）
    ※ 展示用のビルドでは「評価が下がったら早く終わる」はやらない（回転を優先する）
*/
public class ShrineRating : MonoBehaviour
{
    public static ShrineRating Instance { get; private set; }

    [Header("評価値")]
    [SerializeField] float maxRating = 100f;
    [Tooltip("ゲーム開始時の評価値。企画書v3 §7により、必ずランクC圏（rankBThreshold 未満）にすること。")]
    [SerializeField] float startRating = 30f;

    [Header("増減量")]
    [Tooltip("救済成功1人あたりの加点（客種ごとの値が渡されなかったときの既定値）。")]
    [SerializeField] float resolveGain = 5f;
    [Tooltip("黒客化1人あたりの減点（客種ごとの値が渡されなかったときの既定値）。")]
    [SerializeField] float angryLoss = 10f;

    [Header("ランク閾値（この値以上でそのランク）")]
    [SerializeField] float rankSThreshold = 80f;
    [SerializeField] float rankAThreshold = 60f;
    [SerializeField] float rankBThreshold = 40f;
    // それより低ければ C

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

    // 今の評価の値（0〜maxRating）
    public float Rating => _rating;
    // 今の評価の値（0〜1 に直したもの。HUD 用）
    public float RatingNormalized => maxRating > 0f ? _rating / maxRating : 0f;
    // 今の神社のランク。リザルト（#32）がこれを表示する
    public ShrineRank Rank => _rank;

    // 評価による縁の倍率。ScoreManager のもらえる縁の計算にかかる（コンボの倍率とかけ算）
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

        // 企画書 v3 §7: 毎回必ずランクCからスタート、をプレイ中にもチェックする
        if (RankOf(_rating) != ShrineRank.C)
            Debug.LogError($"[Rating] 開始ランクが C ではありません（{RankOf(_rating)}）。startRating を rankBThreshold 未満にしてください（v3 §7 違反）。", this);
    }

#if UNITY_EDITOR
    /*
        Inspector で設定の値を変えたときのチェック（エディタだけ）
        企画書 v3 §7「毎回必ずランクCからスタート」を満たさない値を、早めに警告する
    */
    void OnValidate()
    {
        if (startRating >= rankBThreshold)
            Debug.LogWarning($"[Rating] startRating ({startRating}) がランクC圏を外れています（v3 §7 違反）。rankBThreshold ({rankBThreshold}) 未満にしてください。", this);
    }
#endif

    void Start()
    {
        // 最初の値を HUD に知らせる
        onRatingChanged?.Invoke(RatingNormalized);
        onRankChanged?.Invoke(_rank);
    }

    /*
        救えた → 評価を増やす。ShrineRatingHook から呼ばれる
        gain: 客の種類ごとの増やす量（付録B B-1）。0以下ならこのコンポーネントのふつうの値を使う
    */
    public void RegisterResolved(float gain = 0f) => Modify(+(gain > 0f ? gain : resolveGain), "救済成功");

    /*
        黒客になった（救えなかった）→ 評価を減らす。ShrineRatingHook から呼ばれる
        loss: 客の種類ごとの減らす量（プラスの値。付録B B-1）。0以下ならこのコンポーネントのふつうの値を使う
    */
    public void RegisterAngry(float loss = 0f) => Modify(-(loss > 0f ? loss : angryLoss), "黒客化");

    // 評価を最初の値にもどす（リトライ用。GameSession #32 が呼ぶ）
    public void ResetAll()
    {
        _rating = Mathf.Clamp(startRating, 0f, maxRating);
        Debug.Log("[Rating] Reset");
        ApplyChange();

        /*
            企画書 v3 §7: リトライのとき（GameSession.Retry → ResetAll）でも
            必ずランクCにもどるように、プレイ中にチェックする
        */
        if (RankOf(_rating) != ShrineRank.C)
            Debug.LogError($"[Rating] リセット後のランクが C ではありません（{RankOf(_rating)}）。startRating を rankBThreshold 未満にしてください（v3 §7 違反）。", this);
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
