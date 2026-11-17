
using UnityEngine;
using Toufuku.Aim;

public class PlayerDirect : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private OnusaAimController aimController;
    [SerializeField] private ConecteController con;

    [Header("回転設定")]
    [SerializeField] private float rotationSpeed = 10f;

    // Y軸の向きが逆の場合はON
    [SerializeField] private bool invertYaw = true;

    void Start()
    {
        if (aimController == null)
            aimController = FindAnyObjectByType<OnusaAimController>();

        if (con == null)
            con = FindAnyObjectByType<ConecteController>();
    }

    void Update()
    {
        if (aimController == null || con == null)
            return;

        if (!aimController.HasAim || !con.isConnected)
            return;

        // X軸：OnusaAimControllerの照準方向から計算
        Vector3 aimDirection =
            aimController.TargetPoint - aimController.Origin;

        float horizontalDistance =
            new Vector2(aimDirection.x, aimDirection.z).magnitude;

        float xAngle = -Mathf.Atan2(
            aimDirection.x,
            horizontalDistance
        ) * Mathf.Rad2Deg;

        // Y軸：ConecteControllerのロール
        float yAngle = con.roll;

        Quaternion targetRotation = Quaternion.Euler(
            yAngle - 10,
            xAngle,
            0f
        );

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }
}
