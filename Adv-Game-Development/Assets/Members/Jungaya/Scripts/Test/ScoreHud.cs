using UnityEngine;

/// <summary>
/// 検証用の簡易HUD（OnGUIオーバーレイ）。Canvas不要、シーンに1つ置くだけ。
/// 縁・コンボ・倍率・最大コンボ・直近の命中ゾーンと獲得点を画面左上に表示する。
///
/// #31: ポーリングをやめ、ScoreManager のイベント購読で値を更新する版。
/// 本番UI/SE も同じイベント（onEnChanged 等）を購読すればよい。
/// 本番UIができたら不要になるテスト専用スクリプト。
/// </summary>
public class ScoreHud : MonoBehaviour
{
    [SerializeField] int fontSize = 26;
    [SerializeField] Color color = Color.white;

    [Header("ご加護タイム(#29)の表示（任意）")]
    [SerializeField] GokagoTime gokago;

    GUIStyle style;
    bool _subscribed;

    // イベントで受け取った値をキャッシュして表示する（#31: ポーリング廃止）
    int _en;
    int _combo;
    float _multiplier = 1f;
    float _ratingNormalized = -1f; // 負なら未受信
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
        sm.onOverSell += OnOverSell;

        // 初期値を反映
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
            sm.onOverSell -= OnOverSell;
        }

        var rating = ShrineRating.Instance;
        if (rating != null)
        {
            rating.onRatingChanged.RemoveListener(OnRatingChanged);
            rating.onRankChanged.RemoveListener(OnRankChanged);
        }

        _subscribed = false;
    }

    // ---------- イベント受信（#31） ----------
    void OnEnChanged(int en) => _en = en;
    void OnComboChanged(int combo) => _combo = combo;
    void OnMultiplierChanged(float multiplier) => _multiplier = multiplier;
    void OnMiss() { /* コンボ途切れ演出（HUD点滅など）を足すならここ */ }
    void OnOverSell() { /* 押し売りSE/演出を足すならここ */ }
    void OnRatingChanged(float normalized) => _ratingNormalized = normalized;
    void OnRankChanged(ShrineRank rank) => _rank = rank;

    void Update()
    {
        // 実行順の都合で Start 時に ScoreManager が未生成だった場合の保険
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

        // 背景パネル
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(10, 10, 400, 300), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = 24, y = 18, h = fontSize + 8;
        GUI.Label(new Rect(x, y + h * 0, 400, h), $"縁(En) : {_en}", style);
        GUI.Label(new Rect(x, y + h * 1, 400, h), $"コンボ : {_combo}  (Max {sm.MaxCombo})", style);
        GUI.Label(new Rect(x, y + h * 2, 400, h), $"倍率   : x{_multiplier:0.00}", style);
        GUI.Label(new Rect(x, y + h * 3, 400, h), $"直近   : {sm.LastZone}  +{sm.LastGain}", style);

        // 神社評価（#30）
        if (_ratingNormalized >= 0f)
            GUI.Label(new Rect(x, y + h * 4, 400, h), $"神社評価 : {_ratingNormalized * 100f:0}  ランク {_rank}", style);

        // ご加護タイム（#29）
        if (gokago != null && gokago.IsActive)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = Color.yellow;
            GUI.Label(new Rect(x, y + h * 5, 400, h), $"★ご加護タイム！ 残り {gokago.Remaining:0.0}s", style);
            style.normal.textColor = prev;
        }

        // ボタン：テスト操作
        if (GUI.Button(new Rect(x, y + h * 6 + 6, 110, 34), "Reset"))
            sm.ResetAll();
        if (GUI.Button(new Rect(x + 120, y + h * 6 + 6, 130, 34), "Force Miss"))
            sm.RegisterMiss();
    }
}
