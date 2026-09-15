using UnityEngine;

namespace Toufuku.GameInput
{
    /*
        開発用の入力（キーボード＋マウス）（#51）

        ・色ボタン: 数字キー1〜5（押している間が「押している」）
        ・正面ボタン: Aキー（1秒長押しでキャリブレーション。前の「Aキーですぐリセット」をこのやり方にそろえた）
        ・振りピーク: マウスの左ボタンを押した瞬間（Shift を押しながらだと強い振り。#60 の飛ぶ時間の確認用）
        ・ヨー角: Q / E で左右に回す（キャリブレーションで 0 にもどるかの確認用）
        ・ピッチ角: マウスホイール（#60 のヨーとピッチの照準を机の上で確かめる用）

        ThrowInputController の rawSourceSource にドラッグして使う
    */
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
        [Tooltip("このキーを押しながら振ると strongSwingStrength で振ったことにする（#60 飛翔時間の確認用）")]
        [SerializeField] KeyCode strongSwingKey = KeyCode.LeftShift;
        [Tooltip("度/秒。OnusaThrower.fastStrength 以上なら最速（0.25 秒）で飛ぶ")]
        [SerializeField] float strongSwingStrength = 720f;

        [Header("ヨー角の代用（ドリフト確認用）")]
        [SerializeField] KeyCode yawLeftKey = KeyCode.Q;
        [SerializeField] KeyCode yawRightKey = KeyCode.E;
        [Tooltip("度/秒")]
        [SerializeField] float yawSpeed = 90f;
        [SerializeField] float initialYaw = 180f;

        [Header("ピッチ角の代用（#60: マウスホイールで照準の奥行き）")]
        [Tooltip("ホイール 1 目盛りあたりの度")]
        [SerializeField] float pitchPerScroll = 2.5f;
        [SerializeField] float initialPitch = 0f;
        [SerializeField] float minPitch = -60f;
        [SerializeField] float maxPitch = 60f;

        float _yaw;
        float _pitch;
        int _consumedFrame = -1;

        public bool IsConnected => true;
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        public bool IsFrontHeld => Input.GetKey(frontKey);

        void Awake()
        {
            _yaw = initialYaw;
            _pitch = initialPitch;
        }

        void Update()
        {
            float dir = 0f;
            if (Input.GetKey(yawLeftKey)) dir -= 1f;
            if (Input.GetKey(yawRightKey)) dir += 1f;
            if (dir != 0f)
                _yaw = Mathf.Repeat(_yaw + dir * yawSpeed * Time.unscaledDeltaTime, 360f);

            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f)
                _pitch = Mathf.Clamp(_pitch + scroll * pitchPerScroll, minPitch, maxPitch);
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
            strength = Input.GetKey(strongSwingKey) ? strongSwingStrength : swingStrength;
            time = Time.realtimeSinceStartupAsDouble;
            return true;
        }
    }
}
