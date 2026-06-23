using UnityEngine;
using System.Collections;

public class PlayerDirect : MonoBehaviour
{
    [SerializeField]ArduinoTest arduinoTest;
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
            yawOffset = arduinoTest.yaw;

        targetRot =
            Quaternion.Euler(
                0,
                arduinoTest.yaw - yawOffset,
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