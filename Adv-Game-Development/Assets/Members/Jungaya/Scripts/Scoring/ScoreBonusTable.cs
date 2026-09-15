using UnityEngine;

/*
    ボーナスの点数の、プレイ中に使う数値の表（企画書 v8 付録B B-2 を写したもの）（#55 / #61）

    付録B が Phase 1 でただ1つの数値の元なので、コードに数値を直接書かないでこの ScriptableObject に集める
    （B-1 の客の種類の数値を CustomerKindTable に集めたのと同じ作り方）

    ここに置く理由（優先救済の +50）:
      付録B B-2 には優先救済が「+50 ／ T2 で支配的なら +30 へ」と書いてある（v8 7章「二重円だけを追う」が
      いちばん強い作戦になってしまったときの調整）。値をこの表に出しておけば、コードを直さないでアセットの数字だけで
      +50 → +30 に下げて T2 をやりなおせる

    #61: 福の連なり（3/6/10連続で ×1.10/×1.20/×1.30、5秒で途切れる）と笑顔の伝播（+20）も、T3 で検証する「MVP 仮説」の値なのでここに置く

    使い方:
      Project で右クリック → Create → Toufuku → 加点数値表 (ScoreBonusTable)
      シーンの ScoreManager に入れる。入っていなければ ScoreManager のほうの
      予備の値（付録B と同じふつうの値）を使う
*/
[CreateAssetMenu(
    fileName = "ScoreBonusTable",
    menuName = "Toufuku/加点数値表 (ScoreBonusTable)",
    order = 3)]
public class ScoreBonusTable : ScriptableObject
{
    [Header("命中精度（付録B B-2／#60）")]
    [Tooltip("中心（判定半径の 40% 以内）。")]
    [SerializeField] int accuracyCenterBonus = 50;
    [Tooltip("中間（40〜70%）。")]
    [SerializeField] int accuracyInnerBonus = 20;
    [Tooltip("外周（70〜100%）。")]
    [SerializeField] int accuracyOuterBonus = 0;

    [Header("優先救済（付録B B-2／#55）")]
    [Tooltip("発射時に保存した二重円の客を、その弾で救済完了させたときの加点。" +
             "既定 +50。T2 で「二重円だけを追う」が支配戦略と判定されたら +30 へ下げる（v8 7章）。")]
    [SerializeField] int priorityRescueBonus = 50;

    [Header("福の連なり（付録B B-2・CHAIN.TIMEOUT／#61）")]
    [Tooltip("1段目になる連続救済数（既定 3）。")]
    [SerializeField] int chainStep1Count = FukuChain.Step1Count;
    [Tooltip("1段目の倍率（既定 ×1.10）。")]
    [SerializeField] float chainStep1Multiplier = FukuChain.Step1Multiplier;
    [Tooltip("2段目になる連続救済数（既定 6）。")]
    [SerializeField] int chainStep2Count = FukuChain.Step2Count;
    [Tooltip("2段目の倍率（既定 ×1.20）。")]
    [SerializeField] float chainStep2Multiplier = FukuChain.Step2Multiplier;
    [Tooltip("3段目（上限）になる連続救済数（既定 10）。")]
    [SerializeField] int chainStep3Count = FukuChain.Step3Count;
    [Tooltip("3段目（上限）の倍率（既定 ×1.30）。")]
    [SerializeField] float chainStep3Multiplier = FukuChain.Step3Multiplier;
    [Tooltip("正しい色をどの客にも当てないまま、この秒数たつと福の連なりが 0 に戻る（既定 5秒）。")]
    [SerializeField] float chainTimeoutSeconds = FukuChain.DefaultTimeoutSeconds;

    [Header("笑顔の伝播（付録B B-2・PROPAGATE／#61）")]
    [Tooltip("伝播1人ぶんの縁（既定 +20）。救済時に保存した福の連なり倍率とご加護倍率を掛ける。")]
    [SerializeField] int propagationPoints = EnFormula.DefaultPropagationPoints;

    // 命中精度のボーナス（中心 / 中 / 外側）
    public int AccuracyCenterBonus => accuracyCenterBonus;
    public int AccuracyInnerBonus => accuracyInnerBonus;
    public int AccuracyOuterBonus => accuracyOuterBonus;

    // 優先救済のボーナス（付録B B-2）
    public int PriorityRescueBonus => priorityRescueBonus;

    // 福の連なりが途切れる秒数（付録B CHAIN.TIMEOUT）
    public float ChainTimeoutSeconds => chainTimeoutSeconds;

    // 笑顔の伝播1人ぶんの縁（付録B PROPAGATE）
    public int PropagationPoints => propagationPoints;

    // 命中ゾーンごとの命中精度のボーナス
    public int AccuracyBonusOf(HitZone zone)
    {
        switch (zone)
        {
            case HitZone.Center: return accuracyCenterBonus;
            case HitZone.Inner:  return accuracyInnerBonus;
            case HitZone.Outer:  return accuracyOuterBonus;
            default:             return 0;
        }
    }

    // 福の連なり C から倍率を出す
    public float ChainMultiplierOf(int chain)
    {
        return FukuChain.MultiplierOf(chain,
            chainStep1Count, chainStep1Multiplier,
            chainStep2Count, chainStep2Multiplier,
            chainStep3Count, chainStep3Multiplier);
    }

#if UNITY_EDITOR
    // 付録B にない値に気づけるようにする（エディタだけ）
    void OnValidate()
    {
        if (priorityRescueBonus != 50 && priorityRescueBonus != 30)
            Debug.LogWarning(
                $"[ScoreBonusTable] {name}: 優先救済の加点が {priorityRescueBonus} です。" +
                "付録B B-2 にあるのは +50（既定）と +30（T2 で支配的だった場合）だけです。" +
                "別の値にするなら先に付録B を直してください。", this);

        if (!(chainStep1Count < chainStep2Count && chainStep2Count < chainStep3Count))
            Debug.LogWarning($"[ScoreBonusTable] {name}: 福の連なりの段の数が 1段目 < 2段目 < 3段目 になっていません。", this);

        if (chainStep1Count != FukuChain.Step1Count || chainStep2Count != FukuChain.Step2Count || chainStep3Count != FukuChain.Step3Count
            || !Mathf.Approximately(chainStep1Multiplier, FukuChain.Step1Multiplier)
            || !Mathf.Approximately(chainStep2Multiplier, FukuChain.Step2Multiplier)
            || !Mathf.Approximately(chainStep3Multiplier, FukuChain.Step3Multiplier)
            || !Mathf.Approximately(chainTimeoutSeconds, FukuChain.DefaultTimeoutSeconds)
            || propagationPoints != EnFormula.DefaultPropagationPoints)
            Debug.LogWarning(
                $"[ScoreBonusTable] {name}: 福の連なりか笑顔の伝播の値が付録B（3/6/10連続 ×1.10/×1.20/×1.30・5秒・伝播+20）と違います。" +
                "T3 の結果で変えるなら、先に付録B を直してください。", this);
    }
#endif
}
