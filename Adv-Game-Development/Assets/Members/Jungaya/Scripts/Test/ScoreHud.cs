using UnityEngine;

/*
    検証用のかんたんな HUD（OnGUI で重ねて描く）。Canvas はいらなくて、シーンに1つ置くだけ
    縁・コンボ・倍率・いちばん大きいコンボ・いちばん新しい命中ゾーンともらった点を、画面の右上に表示する
    （企画書 v3 §7: 縁は HUD の右上に表示する。上の真ん中の月の表示 SessionHud と重ならない場所）

    #31: 毎フレーム見に行くのをやめて、ScoreManager のイベントを受け取って値を更新するバージョン
    本番の UI や効果音も、同じイベント（onEnChanged など）を受け取ればいい
    本番の UI ができたらいらなくなる、テスト専用のスクリプト
*/
public class ScoreHud : MonoBehaviour
{
    [SerializeField] int fontSize = 26;
    [SerializeField] Color color = Color.white;
    [Tooltip("#61: 右上は本番向けの ScoreBoardHud（縁・今日のベスト）が使うので、このデバッグ表示は画面の高さのこのわりあいから下に出す。")]
    [SerializeField, Range(0f, 0.9f)] float topRatio = 0.4f;

    [Header("ご加護タイム(#29)の表示（任意）")]
    [SerializeField] GokagoTime gokago;

    GUIStyle style;
    bool _subscribed;

    // イベントで受け取った値をとっておいて表示する（#31: 毎フレーム見に行くのはやめた）
    int _en;
    int _combo;
    float _multiplier = 1f;
    float _ratingNormalized = -1f; // マイナスならまだ受け取っていない
    ShrineRank _rank = ShrineRank.B;

    void Start()
    {
        TrySubscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void TrySubscribe()
    {
        if (_subscribed) return;

        var sm = ScoreManager.Instance;
        if (sm == null) return;

        sm.onEnChanged += OnEnChanged;
        sm.onComboChanged += OnComboChanged;
        sm.onMultiplierChanged += OnMultiplierChanged;
        sm.onMiss += OnMiss;

        // 最初の値を反映する
        _en = sm.En;
        _combo = sm.Combo;
        _multiplier = sm.TotalMultiplier;

        var rating = ShrineRating.Instance;
        if (rating != null)
        {
            rating.onRatingChanged.AddListener(OnRatingChanged);
            rating.onRankChanged.AddListener(OnRankChanged);
            _ratingNormalized = rating.RatingNormalized;
            _rank = rating.Rank;
        }

        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed) return;

        var sm = ScoreManager.Instance;
        if (sm != null)
        {
            sm.onEnChanged -= OnEnChanged;
            sm.onComboChanged -= OnComboChanged;
            sm.onMultiplierChanged -= OnMultiplierChanged;
            sm.onMiss -= OnMiss;
        }

        var rating = ShrineRating.Instance;
        if (rating != null)
        {
            rating.onRatingChanged.RemoveListener(OnRatingChanged);
            rating.onRankChanged.RemoveListener(OnRankChanged);
        }

        _subscribed = false;
    }

    // ---------- イベントを受け取る（#31） ----------
    void OnEnChanged(int en) => _en = en;
    void OnComboChanged(int combo) => _combo = combo;
    void OnMultiplierChanged(float multiplier) => _multiplier = multiplier;
    void OnMiss() { /* コンボが切れる演出（HUD の点滅など）を足すならここ */ }
    void OnRatingChanged(float normalized) => _ratingNormalized = normalized;
    void OnRankChanged(ShrineRank rank) => _rank = rank;

    void Update()
    {
        // 動く順番のせいで、Start のときに ScoreManager がまだ作られていなかったときのための予備
        if (!_subscribed) TrySubscribe();
    }

    void OnGUI()
    {
        var sm = ScoreManager.Instance;
        if (sm == null)
        {
            GUI.Label(new Rect(20, 20, 600, 30), "ScoreManager が見つかりません（シーンに配置してください）");
            return;
        }

        if (style == null || style.fontSize != fontSize)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = fontSize };
        }
        style.normal.textColor = color;

        /*
            企画書 v3 §7: 縁は HUD の右上に表示する。左上に直接書いていたのをやめて、
            画面のはばから右上を基準にして計算する（デバッグのボタンも同じ基準でついていく）
        */
        float panelW = 400f;
        float panelX = Screen.width - panelW - 14f;
        float panelY = Screen.height * topRatio;

        // 背景のパネル
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(panelX, panelY, panelW, 300), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = panelX + 14f, y = panelY + 8f, h = fontSize + 8;
        GUI.Label(new Rect(x, y + h * 0, 400, h), $"縁(En) : {_en}{(sm.IsLocked ? "（固定）" : "")}", style);
        GUI.Label(new Rect(x, y + h * 1, 400, h), $"福の連なり : {_combo}  (Max {sm.MaxCombo})", style);
        GUI.Label(new Rect(x, y + h * 2, 400, h), $"倍率   : x{_multiplier:0.00}（連なり×ご加護）", style);
        GUI.Label(new Rect(x, y + h * 3, 400, h), $"直近   : {sm.LastZone}  +{sm.LastGain}（精度 +{sm.LastBonus}）", style);

        // 神社の評価（#30 / #61: 0〜300、称号はプレイ中の最高ランク）
        var rating = ShrineRating.Instance;
        if (_ratingNormalized >= 0f && rating != null)
            GUI.Label(new Rect(x, y + h * 4, 400, h), $"評価 : {rating.Rating:0}/{rating.RatingMax:0}  ランク {_rank}（最高 {rating.MaxRank}）", style);

        // ご加護タイム（#29）
        if (gokago != null && gokago.IsActive)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = Color.yellow;
            GUI.Label(new Rect(x, y + h * 5, 400, h), $"★ご加護タイム！ 残り {gokago.Remaining:0.0}s", style);
            style.normal.textColor = prev;
        }

        // ボタン: テストの操作
        if (GUI.Button(new Rect(x, y + h * 6 + 6, 110, 34), "Reset"))
            sm.ResetAll();
        if (GUI.Button(new Rect(x + 120, y + h * 6 + 6, 130, 34), "Force Miss"))
            sm.RegisterMiss();
    }
}
