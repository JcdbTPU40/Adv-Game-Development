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
/// 獲得縁の計算式:
///   獲得 = (基礎点 + 命中精度ボーナス) × コンボ倍率(Multiplier) × ご加護倍率(#29) × 神社評価倍率(#30)
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("命中の基礎スコア（縁）")]
    [Tooltip("命中したときの基礎点。命中精度に関係なく同じ（旧 outerScore の値を引き継ぐ）")]
    [FormerlySerializedAs("outerScore")]
    [SerializeField] int hitScore = 100;

    [Header("命中精度ボーナス（#60: 判定半径に対する中心からの距離）")]
    [Tooltip("中心 40% 以内")]
    [SerializeField] int centerBonus = 50;
    [Tooltip("40〜70%")]
    [SerializeField] int innerBonus  = 20;
    [Tooltip("70〜100%")]
    [SerializeField] int outerBonus  = 0;

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
    /// 命中を記録する。(基礎点 + 命中精度ボーナス) × 合計倍率を加算し、コンボを伸ばす。
    /// </summary>
    public void RegisterHit(HitZone zone)
    {
        // Miss が渡されたら命中扱いにしない（コンボ途切れへ）
        if (zone == HitZone.Miss) { RegisterMiss(); return; }

        Combo++;
        if (Combo > MaxCombo) MaxCombo = Combo;

        int bonus = AccuracyBonusOf(zone);
        int gained = Mathf.RoundToInt((hitScore + bonus) * TotalMultiplier);
        En += gained;

        LastZone = zone;
        LastGain = gained;
        LastBonus = bonus;

        Debug.Log($"[Score] HIT {zone} : base {hitScore} + 精度 {bonus} x{TotalMultiplier:0.00} = +{gained}  (Combo {Combo} / En {En})");

        // #31: ポーリング廃止。変化をイベントで配信（HUD/SE/ご加護#29/評価#30 が購読）。
        onComboChanged?.Invoke(Combo);
        onMultiplierChanged?.Invoke(TotalMultiplier);
        onEnChanged?.Invoke(En);
    }

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
        GokagoMultiplier = 1f;
        Debug.Log("[Score] Reset");

        onEnChanged?.Invoke(En);
        onComboChanged?.Invoke(Combo);
        onMultiplierChanged?.Invoke(TotalMultiplier);
        onReset?.Invoke();
    }

    /// <summary>命中ゾーンごとの命中精度ボーナス（#60）。</summary>
    public int AccuracyBonusOf(HitZone zone)
    {
        switch (zone)
        {
            case HitZone.Center: return centerBonus;
            case HitZone.Inner:  return innerBonus;
            case HitZone.Outer:  return outerBonus;
            default:             return 0;
        }
    }
}
