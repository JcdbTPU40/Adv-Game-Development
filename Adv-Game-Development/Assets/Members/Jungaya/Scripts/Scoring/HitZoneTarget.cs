using UnityEngine;

/// <summary>
/// 参拝客（ターゲット）に付ける。当たった位置から命中ゾーン（的の輪）を判定する。
/// 中心からの距離を3つの同心円しきい値で「中心 / 中 / 外」に分け、
/// 外周より外なら Miss 扱いにする。半径は Inspector とギズモで調整できる。
/// </summary>
public class HitZoneTarget : MonoBehaviour
{
    [Header("ゾーン半径（中心からの距離・ワールド単位）")]
    [Tooltip("これ以内＝ど真ん中（Center）")]
    [SerializeField] float centerRadius = 0.25f;
    [Tooltip("これ以内＝中（Inner）")]
    [SerializeField] float innerRadius = 0.50f;
    [Tooltip("これ以内＝外周（Outer）。超えたら Miss")]
    [SerializeField] float outerRadius = 0.90f;

    [Tooltip("判定の中心。未設定ならこのオブジェクトの位置を使う")]
    [SerializeField] Transform center;

    Vector3 Center => center ? center.position : transform.position;

    /// <summary>
    /// ヒット地点（ワールド座標）から命中ゾーンを返す。
    /// </summary>
    public HitZone EvaluateZone(Vector3 hitPoint)
    {
        float d = Vector3.Distance(Center, hitPoint);
        if (d <= centerRadius) return HitZone.Center;
        if (d <= innerRadius)  return HitZone.Inner;
        if (d <= outerRadius)  return HitZone.Outer;
        return HitZone.Miss;
    }

    // Sceneビューで選択中に各ゾーンの半径を可視化（調整用）
    void OnDrawGizmosSelected()
    {
        Vector3 c = Center;
        Gizmos.color = Color.red;    Gizmos.DrawWireSphere(c, centerRadius);
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(c, innerRadius);
        Gizmos.color = Color.green;  Gizmos.DrawWireSphere(c, outerRadius);
    }
}
