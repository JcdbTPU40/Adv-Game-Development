using UnityEngine;

/// <summary>
/// コインプッシャー方式の客の前進。
/// 自分は前(+Z)へ一定速度で押し続けるだけ。前が詰まっていれば物理で止まり、
/// 後ろから来た客が列全体を押す。前のフチに達した客は押し出されて落ちる。
/// ※ Rigidbody(非Kinematic) と Collider が必須。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Customer_Move : MonoBehaviour
{
    [Tooltip("前進の押し速度")]
    [SerializeField] float speed = 1.5f;

    [Tooltip("進む向き。+1 で +Z(プレイヤー側)、-1 で -Z")]
    [SerializeField] float moveSign = 1f;

    Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
        // 倒れて転がらないように回転だけ固定（落下・横ズレは物理に任せる）
        rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    void FixedUpdate()
    {
        // Z方向だけ一定速度で押す。X(横の押し合い)とY(落下)は物理に任せる。
        Vector3 v = rb.linearVelocity;
        v.z = speed * moveSign;
        rb.linearVelocity = v;
    }
}
