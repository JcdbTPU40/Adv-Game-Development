using UnityEngine;

/// <summary>
/// スコア（縁）とコンボの一元管理。シーンに1つだけ置く。
/// ・命中精度ボーナス … ゾーン別の基礎点で表現（中心ヒットほど高得点）
/// ・連続コンボ倍率   … 連続命中で倍率上昇／1ミスで途切れる
/// HUD表示・SE・ご加護タイム判定などは TODO のフック位置に後付けする。
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("ゾーン別 基礎スコア（縁）")]
    [SerializeField] int outerScore  = 100; // 外周
    [SerializeField] int innerScore  = 200; // 中
    [SerializeField] int centerScore = 300; // ど真ん中

    [Header("コンボ倍率")]
    [Tooltip("コンボ1つごとに倍率へ加算する量（例:0.1 → x1.0, x1.1, x1.2...）")]
    [SerializeField] float comboStep = 0.1f;
    [Tooltip("倍率の上限")]
    [SerializeField] float maxMultiplier = 3.0f;

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

    /// <summary>現在のコンボ倍率。コンボ1で x1.0、以降 comboStep ずつ上昇。</summary>
    public float Multiplier =>
        Mathf.Min(1f + Mathf.Max(0, Combo - 1) * comboStep, maxMultiplier);

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// 命中を記録する。ゾーンの基礎点 × 現在のコンボ倍率を加算し、コンボを伸ばす。
    /// </summary>
    public void RegisterHit(HitZone zone)
    {
        // Miss が渡されたら命中扱いにしない（コンボ途切れへ）
        if (zone == HitZone.Miss) { RegisterMiss(); return; }

        Combo++;
        if (Combo > MaxCombo) MaxCombo = Combo;

        int baseScore = BaseScoreOf(zone);
        int gained = Mathf.RoundToInt(baseScore * Multiplier);
        En += gained;

        LastZone = zone;
        LastGain = gained;

        Debug.Log($"[Score] HIT {zone} : base {baseScore} x{Multiplier:0.0} = +{gained}  (Combo {Combo} / En {En})");

        // TODO: HUD更新、命中SE、ご加護タイムのコンボ閾値チェックなどをここに追加
    }

    /// <summary>
    /// ミス（外し／相性の合わないお守り）を記録する。コンボが途切れる。
    /// </summary>
    public void RegisterMiss()
    {
        if (Combo > 0)
            Debug.Log($"[Score] MISS : combo break (was {Combo})");

        Combo = 0;

        // TODO: 「渋る」リアクション、ミスSE、コンボ途切れ演出のフックをここに追加
    }

    /// <summary>スコアとコンボを初期化（テスト・リトライ用）。</summary>
    public void ResetAll()
    {
        En = 0;
        Combo = 0;
        MaxCombo = 0;
        LastZone = HitZone.Miss;
        LastGain = 0;
        Debug.Log("[Score] Reset");
    }

    int BaseScoreOf(HitZone zone)
    {
        switch (zone)
        {
            case HitZone.Center: return centerScore;
            case HitZone.Inner:  return innerScore;
            case HitZone.Outer:  return outerScore;
            default:             return 0;
        }
    }
}
