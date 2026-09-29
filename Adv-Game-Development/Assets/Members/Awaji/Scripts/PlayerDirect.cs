using UnityEngine;

public class PlayerDirect : MonoBehaviour
{
    public ConecteController con;

    float yawOffset;

    void Start()
    {
        if (con == null)
            con = FindAnyObjectByType<ConecteController>();

        yawOffset = con != null ? con.yaw : 0f;
    }

    void Update()
    {
        if (con == null)
            return;

        // Aキーを押した方向を「正面」にする
        if (Input.GetKeyDown(KeyCode.A))
        {
            yawOffset = con.yaw;
        }

        float relativeYaw = con.yaw - yawOffset;

        transform.rotation = Quaternion.Euler(
            0f,
            relativeYaw,
            0f
        );
    }
}