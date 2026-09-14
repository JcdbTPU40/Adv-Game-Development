using UnityEngine;

namespace Toufuku.GameInput
{
    /// <summary>
    /// 開発用の生入力（キーボード＋マウス）— Issue #51
    ///
    /// ・色ボタン: 数字キー 1〜5（押している間＝押下）
    /// ・正面ボタン: A キー（1 秒長押しでキャリブレーション。従来の A キー即時リセットをこの方式に統一）
    /// ・振りピーク: マウス左ボタンを押した瞬間
    /// ・ヨー角: Q / E で左右に回す（キャリブレーションで 0 に戻ることの確認用）
    ///
    /// ThrowInputController の rawSourceSource へドラッグして使う。
    /// </summary>
    public class KeyboardMouseRawSource : MonoBehaviour, IControllerRawSource
    {
        [Header("色ボタン 0〜4（OmamoriType の並び）に割り当てるキー")]
        [SerializeField] KeyCode[] colorKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5
        };

        [Header("正面ボタン")]
        [SerializeField] KeyCode frontKey = KeyCode.A;

        [Header("振りピーク")]
        [Tooltip("0=左 1=右 2=中")]
        [SerializeField] int swingMouseButton = 0;
        [SerializeField] float swingStrength = 1f;

        [Header("ヨー角の代用（ドリフト確認用）")]
        [SerializeField] KeyCode yawLeftKey = KeyCode.Q;
        [SerializeField] KeyCode yawRightKey = KeyCode.E;
        [Tooltip("度/秒")]
        [SerializeField] float yawSpeed = 90f;
        [SerializeField] float initialYaw = 180f;

        float _yaw;
        int _consumedFrame = -1;

        public bool IsConnected => true;
        public float Yaw => _yaw;
        public bool IsFrontHeld => Input.GetKey(frontKey);

        void Awake()
        {
            _yaw = initialYaw;
        }

        void Update()
        {
            float dir = 0f;
            if (Input.GetKey(yawLeftKey)) dir -= 1f;
            if (Input.GetKey(yawRightKey)) dir += 1f;
            if (dir != 0f)
                _yaw = Mathf.Repeat(_yaw + dir * yawSpeed * Time.unscaledDeltaTime, 360f);
        }

        public bool IsColorHeld(int index)
        {
            return colorKeys != null && index >= 0 && index < colorKeys.Length && Input.GetKey(colorKeys[index]);
        }

        public bool TryConsumeSwingPeak(out float strength, out double time)
        {
            strength = 0f;
            time = 0.0;
            if (_consumedFrame == Time.frameCount || !Input.GetMouseButtonDown(swingMouseButton))
                return false;

            _consumedFrame = Time.frameCount;
            strength = swingStrength;
            time = Time.realtimeSinceStartupAsDouble;
            return true;
        }
    }
}
