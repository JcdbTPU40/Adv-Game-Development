using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.GameInput
{
    /*
        実機（ESP32＋BNO055）からの入力そのままを受け取るクラス（#51）

        ConecteController が1行受け取るたびに ConecteController.SampleReceived でもらって、
        ピッチの角速度から振りのピークを見つけて順番に貯めておく。ボタンは4つ目のビットの集まりを読む
        （形は ControllerSample を見る）

        ・今のファームウェアはボタンを送ってこないので、その間は数字キー1〜5とAキーで代わりにできる
        ・つながっていないときは、マウスの左クリックを振りのピークとしてあつかえる（机の上で確認する用）
        ・#65: ボタン付きの行を 100ms 受け取らなかったら、ぜんぶのボタンをはなしたことにする（ButtonLinkWatchdog / 3章）
    */
    public class Esp32RawSource : MonoBehaviour, IControllerRawSource, ISwingPeakInputTime, IButtonLinkState
    {
        [SerializeField] ConecteController con;

        [Header("振りピーク検出（ピッチ角速度・度/秒）— T0-CD #50 で調整")]
        [SerializeField] float triggerVelocity = 180f;
        [SerializeField] float releaseVelocity = 60f;
        [Tooltip("ON: ピッチが減る向き（振り下ろし）を振りとみなす。従来の Shoot2 と同じ向き")]
        [SerializeField] bool swingIsPitchDecrease = true;
        [Tooltip("この秒数より間隔が空いたら通信途切れとみなして追跡を捨てる")]
        [SerializeField] float maxSampleGap = 0.1f;
        [Tooltip("1 フレームにまとめて届いた行へ割り当てるサンプル間隔（ファームウェアの送信周期）")]
        [SerializeField] float nominalSampleInterval = 0.02f;

        [Header("ボタン箱の受信の見張り（#65 / 3章: 100ms 受信がなければ全ボタン解放）")]
        [Tooltip("ボタン付きの行をこの秒数受け取らなかったら、ぜんぶのボタンをはなしたことにする（押している入力とためをキャンセル）")]
        [SerializeField, Min(0.01f)] float buttonLinkTimeoutSeconds = (float)ButtonLinkWatchdog.DefaultTimeoutSeconds;

        [Header("ボタンを送らないファームウェア向けの代用キー")]
        [SerializeField] bool keyboardFallbackForButtons = true;
        [SerializeField] KeyCode[] colorKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5
        };
        [SerializeField] KeyCode frontKey = KeyCode.A;

        [Header("未接続時はマウス左クリックを振りピークとして扱う")]
        [SerializeField] bool mouseSwingWhenDisconnected = true;
        [Tooltip("未接続時、このキーを押しながらクリックすると強い振り扱い（#60 飛翔時間の確認用）")]
        [SerializeField] KeyCode strongSwingKey = KeyCode.LeftShift;
        [SerializeField] float strongSwingStrength = 720f;

        // 同じフレームにまとめて届いた行は受け取った時刻がほとんど同じになるので、間かくがつまりすぎたら決まった間かくでおぎなう
        const double MinSampleInterval = 0.005;

        readonly SwingPeakDetector _detector = new SwingPeakDetector();
        readonly Queue<(float strength, double time, double deviceTime)> _peaks = new Queue<(float, double, double)>();
        readonly ButtonLinkWatchdog _buttonWatch = new ButtonLinkWatchdog();

        ControllerSample _latest;
        double _detectorTime = double.NegativeInfinity;
        int _mouseConsumedFrame = -1;

        // #63: さっき取り出した振りピークの、コントローラー側の時刻（秒）。ファームウェアが送ってこないときやマウスのときは NaN
        public double LastSwingPeakInputTime { get; private set; } = double.NaN;

        public bool IsConnected => con != null && con.isConnected;
        public float Yaw => con != null ? con.yaw : 0f;
        public float Pitch => con != null ? con.pitch : 0f;

        // ファームウェアがボタンの情報を送ってきているかどうか
        public bool HasHardwareButtons => _latest.HasButtons;

        public bool IsFrontHeld => _latest.HasButtons
            ? (ButtonMaskNow & (1 << ControllerSample.FrontButtonBit)) != 0
            : keyboardFallbackForButtons && Input.GetKey(frontKey);

        // #65: 今押していることにするボタンのマスク。ボタン箱の受信が 100ms とぎれていたら 0
        int ButtonMaskNow => _buttonWatch.EffectiveMask(Time.realtimeSinceStartupAsDouble);

        public bool IsButtonLinkLost(double now) => _buttonWatch.IsLost(now);

        public double ButtonLinkLostTime => _buttonWatch.LostTime;

        void OnEnable()
        {
            if (con != null) con.SampleReceived += OnSample;
        }

        void OnDisable()
        {
            if (con != null) con.SampleReceived -= OnSample;
            _detector.Reset();
            _peaks.Clear();
            _buttonWatch.Reset();
        }

        void OnSample(ControllerSample sample)
        {
            _latest = sample;
            if (sample.HasButtons)
            {
                _buttonWatch.TimeoutSeconds = buttonLinkTimeoutSeconds;
                _buttonWatch.Receive(sample.Time, sample.Buttons);
            }

            _detector.TriggerVelocity = triggerVelocity;
            _detector.ReleaseVelocity = releaseVelocity;
            _detector.Direction = swingIsPitchDecrease ? -1f : 1f;
            _detector.MaxSampleGap = maxSampleGap;

            double t = sample.Time;
            if (t < _detectorTime + MinSampleInterval)
                t = _detectorTime + nominalSampleInterval;
            _detectorTime = t;

            // ピークの時刻は受け取った時刻で返す（#52 の遅れの計測とそろえるため）
            if (_detector.AddSample(sample.Pitch, t, out float peakVelocity))
                _peaks.Enqueue((peakVelocity, sample.Time, sample.DeviceTime));
        }

        public bool IsColorHeld(int index)
        {
            if (_latest.HasButtons)
                return index >= 0 && index < ControllerSample.FrontButtonBit && (ButtonMaskNow & (1 << index)) != 0;
            return keyboardFallbackForButtons && colorKeys != null
                && index >= 0 && index < colorKeys.Length && Input.GetKey(colorKeys[index]);
        }

        public bool TryConsumeSwingPeak(out float strength, out double time)
        {
            if (_peaks.Count > 0)
            {
                var peak = _peaks.Dequeue();
                strength = peak.strength;
                time = peak.time;
                LastSwingPeakInputTime = peak.deviceTime;
                return true;
            }

            strength = 0f;
            time = 0.0;
            if (mouseSwingWhenDisconnected && !IsConnected
                && _mouseConsumedFrame != Time.frameCount && Input.GetMouseButtonDown(0))
            {
                _mouseConsumedFrame = Time.frameCount;
                LastSwingPeakInputTime = double.NaN;
                strength = Input.GetKey(strongSwingKey) ? strongSwingStrength : 1f;
                time = Time.realtimeSinceStartupAsDouble;
                return true;
            }
            return false;
        }
    }
}
