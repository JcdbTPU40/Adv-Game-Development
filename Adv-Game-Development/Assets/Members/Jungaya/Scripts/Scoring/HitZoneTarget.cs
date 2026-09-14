using System.Collections.Generic;
using UnityEngine;
using Toufuku.Rescue;

/// <summary>
/// 参拝客（ターゲット）に付ける。当たった位置から命中ゾーン（的の輪）を判定する。
///
/// #60: 判定半径に対する割合で「中心（40% 以内）/ 中（40〜70%）/ 外周（70〜100%）」に分け、
///      半径を超えたら Miss 扱いにする（割合は <see cref="HitAccuracy"/>）。
///      着弾点判定（OmamoriProjectile）は地面上の水平距離、物理衝突（OmamoriBullet）は 3D 距離で測る。
/// 有効な HitZoneTarget は <see cref="Active"/> に登録され、着弾点判定の候補になる。
/// </summary>
public class HitZoneTarget : MonoBehaviour
{
    static readonly List<HitZoneTarget> s_active = new List<HitZoneTarget>();

    /// <summary>シーン上で有効な HitZoneTarget の一覧（着弾点判定の候補）。</summary>
    public static IReadOnlyList<HitZoneTarget> Active => s_active;

    [Header("判定半径（中心からの距離・ワールド単位）")]
    [Tooltip("これ以内が命中。40% 以内＝中心 / 70% 以内＝中 / それより外＝外周（#60）")]
    [SerializeField] float outerRadius = 0.90f;

    [Tooltip("判定の中心。未設定ならこのオブジェクトの位置を使う")]
    [SerializeField] Transform center;

    Collider[] _colliders;
    CustomerMood _mood;

    public Vector3 Center => center ? center.position : transform.position;

    /// <summary>判定半径（100% の距離）。</summary>
    public float Radius => outerRadius;

    /// <summary>
    /// 当たり判定が生きているか。結末確定（救済演出中）で CustomerMood が Collider を切ると false になり、
    /// 着弾点判定では弾が通過して後方の客で判定される。
    /// </summary>
    public bool IsHittable
    {
        get
        {
            if (!isActiveAndEnabled) return false;
            if (_mood != null && _mood.IsFinished) return false;
            if (_colliders == null || _colliders.Length == 0) return true;
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null && _colliders[i].enabled) return true;
            }
            return false;
        }
    }

    void Awake()
    {
        _colliders = GetComponentsInChildren<Collider>(true);
        _mood = GetComponent<CustomerMood>();
    }

    void OnEnable()
    {
        if (!s_active.Contains(this)) s_active.Add(this);
    }

    void OnDisable()
    {
        s_active.Remove(this);
    }

    /// <summary>
    /// ヒット地点（ワールド座標）から命中ゾーンを返す。中心からの 3D 距離で測る（物理衝突用）。
    /// </summary>
    public HitZone EvaluateZone(Vector3 hitPoint)
    {
        if (outerRadius <= 0f) return HitZone.Miss;
        return HitAccuracy.ZoneOf(Vector3.Distance(Center, hitPoint) / outerRadius);
    }

    /// <summary>
    /// 地面上の着弾点から命中ゾーンを返す。高さは無視して水平距離で測る（#60 着弾点判定用）。
    /// </summary>
    public HitZone EvaluateGroundZone(Vector3 groundPoint)
    {
        return HitAccuracy.ZoneOf(HitAccuracy.NormalizedDistance(Center, groundPoint, outerRadius));
    }

    // Sceneビューで選択中に各ゾーンの半径を可視化（調整用）
    void OnDrawGizmosSelected()
    {
        Vector3 c = Center;
        Gizmos.color = Color.red;    DrawCircle(c, outerRadius * HitAccuracy.CenterRatio);
        Gizmos.color = Color.yellow; DrawCircle(c, outerRadius * HitAccuracy.InnerRatio);
        Gizmos.color = Color.green;  DrawCircle(c, outerRadius);
    }

    static void DrawCircle(Vector3 c, float r)
    {
        const int segments = 32;
        Vector3 prev = c + new Vector3(r, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 next = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
