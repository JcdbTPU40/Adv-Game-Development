using UnityEngine;
using System.Collections;

public class PlayerDirect : MonoBehaviour
{
    [SerializeField]ArduinoTest arduinoTest;
    Quaternion targetRot;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        targetRot =
            Quaternion.Euler(
                0,
                arduinoTest.yaw - 180,
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