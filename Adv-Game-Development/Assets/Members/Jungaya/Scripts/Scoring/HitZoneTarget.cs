using System.Collections.Generic;
using UnityEngine;
using Toufuku.Rescue;

/*
    客（的）に付けるクラス。当たった位置から命中ゾーン（的の輪）を判定する

    #60: 判定半径に対するわりあいで「中心（40% 以内）/ 中（40〜70%）/ 外側（70〜100%）」に分けて、
         半径をこえたら Miss にする（わりあいは HitAccuracy）
         着弾点での判定（OmamoriProjectile）は地面の上の水平距離、物理でぶつかる判定（OmamoriBullet）は 3D の距離で測る
    有効な HitZoneTarget は Active に登録されて、着弾点の判定の候補になる
*/
public class HitZoneTarget : MonoBehaviour
{
    static readonly List<HitZoneTarget> s_active = new List<HitZoneTarget>();

    // シーンで有効な HitZoneTarget の一覧（着弾点の判定の候補）
    public static IReadOnlyList<HitZoneTarget> Active => s_active;

    [Header("判定半径（中心からの距離・ワールド単位）")]
    [Tooltip("これ以内が命中。40% 以内＝中心 / 70% 以内＝中 / それより外＝外周（#60）")]
    [SerializeField] float outerRadius = 0.90f;

    [Tooltip("判定の中心。未設定ならこのオブジェクトの位置を使う")]
    [SerializeField] Transform center;

    Collider[] _colliders;
    CustomerState _state;

    public Vector3 Center => center ? center.position : transform.position;

    // 判定半径（100% の距離）
    public float Radius => outerRadius;

    /*
        当たり判定が生きているかどうか。救えた（帰る演出中）ときに CustomerState が Collider を切ると false になって、
        着弾点の判定では弾が通りぬけて、うしろの客で判定される
        黒客の当たり判定は仕様どおり残るので（v8 6章）、黒客はここで外さない
    */
    public bool IsHittable
    {
        get
        {
            if (!isActiveAndEnabled) return false;
            if (_state != null && _state.IsRescued) return false;
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
        _state = GetComponent<CustomerState>();
    }

    void OnEnable()
    {
        if (!s_active.Contains(this)) s_active.Add(this);
    }

    void OnDisable()
    {
        s_active.Remove(this);
    }

    // 当たった場所（ワールド座標）から命中ゾーンを返す。中心からの 3D の距離で測る（物理でぶつかる判定用）
    public HitZone EvaluateZone(Vector3 hitPoint)
    {
        if (outerRadius <= 0f) return HitZone.Miss;
        return HitAccuracy.ZoneOf(Vector3.Distance(Center, hitPoint) / outerRadius);
    }

    // 地面の上の着弾点から命中ゾーンを返す。高さは見ないで水平距離で測る（#60 の着弾点の判定用）
    public HitZone EvaluateGroundZone(Vector3 groundPoint)
    {
        return HitAccuracy.ZoneOf(HitAccuracy.NormalizedDistance(Center, groundPoint, outerRadius));
    }

    // Scene ビューで選んでいるときに、それぞれのゾーンの半径を表示する（調整用）
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
