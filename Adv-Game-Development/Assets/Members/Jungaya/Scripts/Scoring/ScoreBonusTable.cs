using UnityEngine;

/*
    ボーナスの点数の、プレイ中に使う数値の表（企画書 v8 付録B B-2 を写したもの）（#55）

    付録B が Phase 1 でただ1つの数値の元なので、コードに数値を直接書かないでこの ScriptableObject に集める
    （B-1 の客の種類の数値を CustomerKindTable に集めたのと同じ作り方）

    ここに置く理由（優先救済の +50）:
      付録B B-2 には優先救済が「+50 ／ T2 で支配的なら +30 へ」と書いてある（v8 7章「二重円だけを追う」が
      いちばん強い作戦になってしまったときの調整）。値をこの表に出しておけば、コードを直さないでアセットの数字だけで
      +50 → +30 に下げて T2 をやりなおせる

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

    // 命中精度のボーナス（中心 / 中 / 外側）
    public int AccuracyCenterBonus => accuracyCenterBonus;
    public int AccuracyInnerBonus => accuracyInnerBonus;
    public int AccuracyOuterBonus => accuracyOuterBonus;

    // 優先救済のボーナス（付録B B-2）
    public int PriorityRescueBonus => priorityRescueBonus;

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

#if UNITY_EDITOR
    // 付録B にない値に気づけるようにする（エディタだけ）
    void OnValidate()
    {
        if (priorityRescueBonus != 50 && priorityRescueBonus != 30)
            Debug.LogWarning(
                $"[ScoreBonusTable] {name}: 優先救済の加点が {priorityRescueBonus} です。" +
                "付録B B-2 にあるのは +50（既定）と +30（T2 で支配的だった場合）だけです。" +
                "別の値にするなら先に付録B を直してください。", this);
    }
#endif
}
