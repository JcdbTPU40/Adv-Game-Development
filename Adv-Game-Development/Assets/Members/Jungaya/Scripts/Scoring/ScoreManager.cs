using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// スコア（縁）とコンボの一元管理。シーンに1つだけ置く。— Issue #22 / #31
/// ・命中精度ボーナス … #60: 判定半径の中心 40% 以内 +50 / 40〜70% +20 / 70〜100% +0
/// ・連続コンボ倍率   … 連続命中で倍率上昇／1ミスで途切れる
///
/// #31: スコア変化を C# イベントで配信する。HUD/SE/ご加護(#29)/評価(#30)は
///      ポーリングせず、これらのイベントを購読して結線する。
///
/// #55: 優先救済（二重円）の加点を足す。加点の数値は付録B B-2 の写しである
///      <see cref="ScoreBonusTable"/>（未割り当てなら下のフォールバック値）から引く。
///
/// 獲得縁の計算式:
///   獲得 = (基礎点 + 命中精度ボーナス + 優先救済ボーナス) × コンボ倍率(Multiplier) × ご加護倍率(#29) × 神社評価倍率(#30)
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("命中の基礎スコア（縁）")]
    [Tooltip("旧APIのフォールバック基礎点。#54 以降の正規の基礎点は客種ごと（CustomerKindTable / 付録B B-1）で、" +
             "救済完了時に CustomerState から渡される。")]
    [FormerlySerializedAs("outerScore")]
    [SerializeField] int hitScore = 100;

    [Header("加点の数値表（付録B B-2）")]
    [Tooltip("加点の数値表（付録B B-2 の写し）。割り当てるとこの表の値が下のフォールバックより優先される。" +
             "T2 で優先救済を +50 → +30 へ下げるときは、この表の数字だけを直す。")]
    [SerializeField] ScoreBonusTable bonusTable;

    [Header("フォールバック：命中精度ボーナス（#60: 判定半径に対する中心からの距離）")]
    [Tooltip("中心 40% 以内")]
    [SerializeField] int centerBonus = 50;
    [Tooltip("40〜70%")]
    [SerializeField] int innerBonus  = 20;
    [Tooltip("70〜100%")]
    [SerializeField] int outerBonus  = 0;

    [Header("フォールバック：優先救済ボーナス（#55 / 付録B B-2）")]
    [Tooltip("発射時に保存した二重円の客を、その弾で救済完了させたときの加点。数値表が未割り当てのときだけ使う。")]
    [SerializeField] int priorityRescueBonus = 50;

    [Header("コンボ倍率")]
    [Tooltip("コンボ1つごとに倍率へ加算する量（例:0.1 → x1.0, x1.1, x1.2...）")]
    [SerializeField] float comboStep = 0.1f;
    [Tooltip("倍率の上限")]
    [SerializeField] float maxMultiplier = 3.0f;

    // ---------- 公開イベント（#31） ----------
    /// <summary>縁が変化した（引数: 現在の累計縁）。</summary>
    public event Action<int> onEnChanged;
    /// <summary>コンボ数が変化した（引数: 現在のコンボ）。</summary>
    public event Action<int> onComboChanged;
    /// <summary>合計倍率が変化した（引数: コンボ×ご加護×評価 の合計倍率）。</summary>
    public event Action<float> onMultiplierChanged;
    /// <summary>ミス（外し／相性✗）が起きた。コンボ途切れ演出・SE用。</summary>
    public event Action onMiss;
    /// <summary>ResetAll が呼ばれた（リトライ用。#32 のセッションが購読）。</summary>
    public event Action onReset;

    /// <summary>累計スコア（縁）。減らずに増え続ける。</summary>
    public int En { get; private set; }
    /// <summary>現在の連続コンボ数。1ミスで0に戻る。</summary>
    public int Combo { get; private set; }
    /// <summary>このプレイ中の最大コンボ（リザルト用）。</summary>
    public int MaxCombo { get; private set; }

    /// <summary>直近の命中ゾーン（HUD表示・確認用）。</summary>
    public HitZone LastZone { get; private set; }
    /// <summary>直近の獲得点（HUD表示・確認用）。</summary>
    public int LastGain { get; private set; }
    /// <summary>直近の命中精度ボーナス（倍率を掛ける前。HUD表示・確認用）。</summary>
    public int LastBonus { get; private set; }
    /// <summary>直近の優先救済ボーナス（倍率を掛ける前。0 なら二重円の客ではなかった。HUD表示・確認用）。</summary>
    public int LastPriorityBonus { get; private set; }

    /// <summary>優先救済（二重円）の加点（付録B B-2）。数値表が割り当てられていればその値。</summary>
    public int PriorityRescueBonus => bonusTable != null ? bonusTable.PriorityRescueBonus : priorityRescueBonus;

    /// <summary>現在のコンボ倍率。コンボ1で x1.0、以降 comboStep ずつ上昇。</summary>
    public float Multiplier =>
        Mathf.Min(1f + Mathf.Max(0, Combo - 1) * comboStep, maxMultiplier);

    /// <summary>ご加護タイム(#29)の上乗せ倍率。GokagoTime が設定する。通常は1。</summary>
    public float GokagoMultiplier { get; private set; } = 1f;

    /// <summary>神社評価(#30)による縁倍率。ShrineRating 未配置なら1。</summary>
    public float RatingMultiplier =>
        ShrineRating.Instance != null ? ShrineRating.Instance.EnMultiplier : 1f;

    /// <summary>獲得計算に使う合計倍率（コンボ×ご加護×評価）。</summary>
    public float TotalMultiplier => Multiplier * GokagoMultiplier * RatingMultiplier;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 正色命中を記録する（#54）。福の連なり C は正色命中のたびに伸びるが、
    /// <b>縁は救済完了（R=0）のときだけ</b> (基礎点 + 命中精度ボーナス) × 合計倍率で入る。
    ///
    /// 企画書 v8 変更点5／付録B B-2：欲張り客の途中命中は 0 点、救済完了時に 300 点を1回で確定する。
    /// 途中点を先払いしないので、複数発客の得点は「最終弾の精度・倍率が1回だけ乗った値」に一意に決まる。
    /// </summary>
    /// <param name="zone">命中精度のゾーン（Miss ならミス扱い）。</param>
    /// <param name="rescued">この命中で救済が完了したか（R=0 になったか）。</param>
    /// <param name="rescueBaseScore">救済完了時の基礎点（客種ごと。付録B B-1）。</param>
    /// <param name="priorityRescue">
    /// 優先救済か（#55）。発射（SwingAccepted）時に弾へ保存した二重円の客を、その弾で救済完了させたときだけ true。
    /// 飛翔中に二重円が別の客へ移っても、この値は発射時の判断のまま変わらない。
    /// </param>
    public void RegisterCorrectHit(HitZone zone, bool rescued, int rescueBaseScore, bool priorityRescue = false)
    {
        // Miss が渡されたら命中扱いにしない（コンボ途切れへ）
        if (zone == HitZone.Miss) { RegisterMiss(); return; }

        Combo++;
        if (Combo > MaxCombo) MaxCombo = Combo;

        int bonus = rescued ? AccuracyBonusOf(zone) : 0;
        // 優先救済は救済完了した弾にだけ乗る（途中命中は付録B B-2 どおり 0 点）。
        int priorityBonus = rescued && priorityRescue ? PriorityRescueBonus : 0;
        int gained = rescued ? Mathf.RoundToInt((rescueBaseScore + bonus + priorityBonus) * TotalMultiplier) : 0;
        En += gained;

        LastZone = zone;
        LastGain = gained;
        LastBonus = bonus;
        LastPriorityBonus = priorityBonus;

        if (rescued)
            Debug.Log($"[Score] 救済完了 {zone} : 基礎 {rescueBaseScore} + 精度 {bonus}" +
                      (priorityBonus > 0 ? $" + 優先救済 {priorityBonus}" : "") +
                      $" x{TotalMultiplier:0.00} = +{gained}  (連なり {Combo} / 縁 {En})");
        else
            Debug.Log($"[Score] 正色命中（救済途中）: 縁は入らない (連なり {Combo} / 縁 {En})");

        // #31: ポーリング廃止。変化をイベントで配信（HUD/SE/ご加護#29/評価#30 が購読）。
        onComboChanged?.Invoke(Combo);
        onMultiplierChanged?.Invoke(TotalMultiplier);
        onEnChanged?.Invoke(En);
    }

    /// <summary>
    /// 旧API（#22）。1発で救済が完了する客だけ正しい。#54 以降は
    /// <see cref="RegisterCorrectHit(HitZone,bool,int)"/> を使い、基礎点は客種ごとの値を渡すこと。
    /// </summary>
    public void RegisterHit(HitZone zone) => RegisterCorrectHit(zone, rescued: true, rescueBaseScore: hitScore);

    /// <summary>
    /// ミス（外し／相性の合わないお守り）を記録する。コンボが途切れる。
    /// </summary>
    public void RegisterMiss()
    {
        if (Combo > 0)
            Debug.Log($"[Score] MISS : combo break (was {Combo})");

        Combo = 0;

        // 「渋る」リアクション（#14）は客ごとの CustomerReluctance が CustomerRescue.onBadHit を
        // 購読して再生する（誤投擲＝相性✗ヒット時のみ）。ここは「外し」も含む全ミス共通の処理。
        onComboChanged?.Invoke(Combo);
        onMultiplierChanged?.Invoke(TotalMultiplier);
        onMiss?.Invoke(); // ミスSE・コンボ途切れ演出（HUD点滅など）はここを購読する
    }

    /// <summary>ご加護タイム(#29)から呼ぶ。上乗せ倍率の設定/解除。</summary>
    public void SetGokagoMultiplier(float multiplier)
    {
        GokagoMultiplier = Mathf.Max(1f, multiplier);
        onMultiplierChanged?.Invoke(TotalMultiplier);
    }

    /// <summary>スコアとコンボを初期化（テスト・リトライ用）。</summary>
    public void ResetAll()
    {
        En = 0;
        Combo = 0;
        MaxCombo = 0;
        LastZone = HitZone.Miss;
        LastGain = 0;
        LastBonus = 0;
        LastPriorityBonus = 0;
        GokagoMultiplier = 1f;
        Debug.Log("[Score] Reset");

        onEnChanged?.Invoke(En);
        onComboChanged?.Invoke(Combo);
        onMultiplierChanged?.Invoke(TotalMultiplier);
        onReset?.Invoke();
    }

    /// <summary>命中ゾーンごとの命中精度ボーナス（#60 / 付録B B-2）。数値表があればその値を使う。</summary>
    public int AccuracyBonusOf(HitZone zone)
    {
        if (bonusTable != null) return bonusTable.AccuracyBonusOf(zone);

        switch (zone)
        {
            case HitZone.Center: return centerBonus;
            case HitZone.Inner:  return innerBonus;
            case HitZone.Outer:  return outerBonus;
            default:             return 0;
        }
    }
}
