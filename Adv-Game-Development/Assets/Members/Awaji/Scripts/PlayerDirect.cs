using UnityEngine;
using System.Collections;

public class PlayerDirect : MonoBehaviour
{
    public ConecteController con;
    Quaternion targetRot;
    float yawOffset = 180f;

    void Start()
    {
        yawOffset = 180f;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
            yawOffset = con.yaw;

        targetRot =
            Quaternion.Euler(
                0,
                con.yaw - yawOffset,
                0
            );

        transform.rotation =
            Quaternion.Lerp(
                transform.rotation,
                targetRot,
                Time.deltaTime * 10f
            );
    }
}