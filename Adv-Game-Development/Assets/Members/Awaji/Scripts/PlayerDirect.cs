using UnityEngine;
using System.Collections;
using Toufuku.GameInput;

public class PlayerDirect : MonoBehaviour
{
    public ConecteController con;
    [Tooltip("#51: 正面ボタン1秒長押しのキャリブレーション結果で向く。未設定ならシーン内から探し、無ければ従来の A キー即時リセット")]
    public ThrowInputController inputController;
    Quaternion targetRot;
    float yawOffset = 180f;

    void Start()
    {
        yawOffset = 180f;

        if (inputController == null)
            inputController = FindAnyObjectByType<ThrowInputController>();
    }

    // Update is called once per frame
    void Update()
    {
        float relativeYaw;
        if (inputController != null)
        {
            // #51: キャリブレーションは ThrowInputController に一本化
            relativeYaw = inputController.RelativeYaw;
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.A))
                yawOffset = con.yaw;
            relativeYaw = con.yaw - yawOffset;
        }

        targetRot =
            Quaternion.Euler(
                0,
                relativeYaw,
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