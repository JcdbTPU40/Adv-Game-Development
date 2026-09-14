using UnityEngine;

/// <summary>
/// 加点のランタイム数値表（企画書 v8 付録B B-2 の写し）— Issue #55
///
/// 付録B が Phase 1 の唯一の数値マスターなので、コードに数値を直書きせずこの ScriptableObject へ集める
/// （B-1 の客種数値を <c>CustomerKindTable</c> に集めたのと同じ作り）。
///
/// ここに置く理由（優先救済 +50）:
///   付録B B-2 は優先救済を「+50 ／ T2 で支配的なら +30 へ」と書いている（v8 7章「二重円だけを追う」が
///   支配戦略になった場合の調整）。値をこの表に出しておけば、<b>コードを直さずアセットの数字だけで</b>
///   +50 → +30 に下げて T2 を回し直せる。
///
/// 使い方:
///   Project で右クリック → Create → Toufuku → 加点数値表 (ScoreBonusTable)。
///   シーンの <see cref="ScoreManager"/> に割り当てる。割り当てが無ければ ScoreManager 側の
///   フォールバック値（付録B と同じ既定値）を使う。
/// </summary>
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

    /// <summary>命中精度の加点（中心 / 中間 / 外周）。</summary>
    public int AccuracyCenterBonus => accuracyCenterBonus;
    public int AccuracyInnerBonus => accuracyInnerBonus;
    public int AccuracyOuterBonus => accuracyOuterBonus;

    /// <summary>優先救済の加点（付録B B-2）。</summary>
    public int PriorityRescueBonus => priorityRescueBonus;

    /// <summary>命中ゾーンごとの命中精度の加点。</summary>
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
    /// <summary>付録B に無い値に気づけるようにする（エディタ専用）。</summary>
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
