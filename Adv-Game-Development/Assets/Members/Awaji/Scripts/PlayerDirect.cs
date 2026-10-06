using UnityEngine;

public class PlayerDirect : MonoBehaviour
{
    public ConecteController con;

    float yawOffset;
    float rollOffset;

    void Start()
    {
        if (con == null)
            con = FindAnyObjectByType<ConecteController>();

        yawOffset = con != null ? con.yaw : 0f;
        rollOffset = con != null ? con.roll : 0f;
    }

    void Update()
    {
        if (con == null)
            return;

        // Aキーを押した方向を「正面」にする
        if (Input.GetKeyDown(KeyCode.A))
        {
            yawOffset = con.yaw;
            rollOffset = con.roll;
        }

        float relativeYaw = con.yaw - yawOffset;
        float relativeRoll = con.roll - rollOffset;

        transform.rotation = Quaternion.Euler(
            -relativeRoll,
            relativeYaw,
            0f
        );
    }
}