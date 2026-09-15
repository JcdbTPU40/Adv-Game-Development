using System;
using UnityEngine;
using UnityEngine.Serialization;

/*
    スコア（縁）とコンボをまとめて管理するクラス。シーンに1つだけ置く（#22 / #31）
    ・命中精度のボーナス: #60 で、判定半径の中心 40% 以内 +50 / 40〜70% +20 / 70〜100% +0
    ・連続コンボの倍率: 続けて当てると倍率が上がって、1回ミスすると切れる

    #31: スコアの変化を C# のイベントで配る。HUD・効果音・ご加護（#29）・評価（#30）は
         毎フレーム見に行かないで、これらのイベントを受け取ってつなぐ

    #55: 優先救済（二重円）のボーナスを足す。ボーナスの数値は付録B B-2 を写した
         ScoreBonusTable（入っていなければ下の予備の値）から取る

    もらえる縁の計算:
      もらえる縁 = (基礎点 + 命中精度ボーナス + 優先救済ボーナス) × コンボ倍率(Multiplier) × ご加護倍率(#29) × 神社評価倍率(#30)
*/
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

    /*
        ---------- 外に配るイベント（#31） ----------
        縁が変わった（引数: 今の縁の合計）
    */
    public event Action<int> onEnChanged;
    // コンボの数が変わった（引数: 今のコンボ）
    public event Action<int> onComboChanged;
    // 合計の倍率が変わった（引数: コンボ×ご加護×評価 の合計の倍率）
    public event Action<float> onMultiplierChanged;
    // ミス（外れ・相性✗）が起きた。コンボが切れる演出や効果音用
    public event Action onMiss;
    // ResetAll が呼ばれた（リトライ用。#32 のゲーム管理が受け取る）
    public event Action onReset;

    // 合計のスコア（縁）。減らないで増えつづける
    public int En { get; private set; }
    // 今の連続コンボの数。1回ミスすると0にもどる
    public int Combo { get; private set; }
    // このプレイ中のいちばん大きいコンボ（リザルト用）
    public int MaxCombo { get; private set; }

    // いちばん新しい命中ゾーン（HUD の表示・確認用）
    public HitZone LastZone { get; private set; }
    // いちばん新しくもらった点（HUD の表示・確認用）
    public int LastGain { get; private set; }
    // いちばん新しい命中精度のボーナス（倍率をかける前。HUD の表示・確認用）
    public int LastBonus { get; private set; }
    // いちばん新しい優先救済のボーナス（倍率をかける前。0 なら二重円の客じゃなかった。HUD の表示・確認用）
    public int LastPriorityBonus { get; private set; }

    // 優先救済（二重円）のボーナス（付録B B-2）。数値の表が入っていればその値
    public int PriorityRescueBonus => bonusTable != null ? bonusTable.PriorityRescueBonus : priorityRescueBonus;

    // 今のコンボの倍率。コンボ1で x1.0、そこから comboStep ずつ上がる
    public float Multiplier =>
        Mathf.Min(1f + Mathf.Max(0, Combo - 1) * comboStep, maxMultiplier);

    // ご加護タイム（#29）で上乗せする倍率。GokagoTime が設定する。ふつうは1
    public float GokagoMultiplier { get; private set; } = 1f;

    // 神社の評価（#30）による縁の倍率。ShrineRating を置いていなければ1
    public float RatingMultiplier =>
        ShrineRating.Instance != null ? ShrineRating.Instance.EnMultiplier : 1f;

    // もらえる縁の計算に使う合計の倍率（コンボ×ご加護×評価）
    public float TotalMultiplier => Multiplier * GokagoMultiplier * RatingMultiplier;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /*
        正しい色で当たったのを記録する（#54）。福の連なり C は正しい色で当たるたびにのびるけど、
        縁は救えた（R=0）ときだけ (基礎点 + 命中精度ボーナス) × 合計の倍率 で入る

        企画書 v8 の変更点5、付録B B-2: 欲張り客のとちゅうの当たりは 0 点で、救えたときに 300 点を1回で決める
        とちゅうの点を先に払わないので、何発も必要な客の点は「最後の弾の精度と倍率が1回だけかかった値」に1つに決まる
        zone: 命中精度のゾーン（Miss ならミスあつかい）
        rescued: この当たりで救えたか（R=0 になったか）
        rescueBaseScore: 救えたときの基礎点（客の種類ごと。付録B B-1）
        priorityRescue: 優先救済か（#55）。発射（SwingAccepted）したときに弾に保存した二重円の客を、その弾で救えたときだけ true
          飛んでいる間に二重円が別の客に移っても、この値は発射したときの判断のまま変わらない
    */
    public void RegisterCorrectHit(HitZone zone, bool rescued, int rescueBaseScore, bool priorityRescue = false)
    {
        // Miss が来たら当たりにしない（コンボが切れるほうへ）
        if (zone == HitZone.Miss) { RegisterMiss(); return; }

        Combo++;
        if (Combo > MaxCombo) MaxCombo = Combo;

        int bonus = rescued ? AccuracyBonusOf(zone) : 0;
        // 優先救済は救えた弾にだけのる（とちゅうの当たりは付録B B-2 のとおり 0 点）
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

        // #31: 毎フレーム見に行くのはやめた。変わったことをイベントで配る（HUD・効果音・ご加護 #29・評価 #30 が受け取る）
        onComboChanged?.Invoke(Combo);
        onMultiplierChanged?.Invoke(TotalMultiplier);
        onEnChanged?.Invoke(En);
    }

    /*
        古いメソッド（#22）。1発で救える客のときだけ正しい。#54 からは
        RegisterCorrectHit(HitZone,bool,int) を使って、基礎点は客の種類ごとの値を渡すこと
    */
    public void RegisterHit(HitZone zone) => RegisterCorrectHit(zone, rescued: true, rescueBaseScore: hitScore);

    // ミス（外れ・相性の合わないお守り）を記録する。コンボが切れる
    public void RegisterMiss()
    {
        if (Combo > 0)
            Debug.Log($"[Score] MISS : combo break (was {Combo})");

        Combo = 0;

        /*
            「渋る」リアクション（#14）は客ごとの CustomerReluctance が CustomerRescue.onBadHit を
            受け取って再生する（まちがい＝相性✗で当たったときだけ）。ここは「外れ」も入れた、ぜんぶのミスで共通の処理
        */
        onComboChanged?.Invoke(Combo);
        onMultiplierChanged?.Invoke(TotalMultiplier);
        onMiss?.Invoke(); // ミスの効果音やコンボが切れる演出（HUD の点滅など）はここを受け取る
    }

    // ご加護タイム（#29）から呼ぶ。上乗せする倍率を設定したり、やめたりする
    public void SetGokagoMultiplier(float multiplier)
    {
        GokagoMultiplier = Mathf.Max(1f, multiplier);
        onMultiplierChanged?.Invoke(TotalMultiplier);
    }

    // スコアとコンボを最初にもどす（テスト・リトライ用）
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

    // 命中ゾーンごとの命中精度のボーナス（#60 / 付録B B-2）。数値の表があればその値を使う
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
