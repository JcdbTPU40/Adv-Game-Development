using System.Collections.Generic;
using UnityEngine;

namespace Toufuku.GameInput
{
    /// <summary>
    /// 実機（ESP32＋BNO055）の生入力 — Issue #51
    ///
    /// ConecteController が 1 行受信するたびに <see cref="ConecteController.SampleReceived"/> で受け取り、
    /// ピッチ角速度から振りピークを検出してキューに積む。ボタンは 4 項目目のビットマスクを読む
    /// （形式は <see cref="ControllerSample"/>）。
    ///
    /// ・現行ファームウェアはボタンを送らないので、その間は数字キー 1〜5／A キーで代用できる。
    /// ・未接続時はマウス左クリックを振りピークとして扱える（机上での確認用）。
    /// </summary>
    public class Esp32RawSource : MonoBehaviour, IControllerRawSource
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

        [Header("ボタンを送らないファームウェア向けの代用キー")]
        [SerializeField] bool keyboardFallbackForButtons = true;
        [SerializeField] KeyCode[] colorKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5
        };
        [SerializeField] KeyCode frontKey = KeyCode.A;

        [Header("未接続時はマウス左クリックを振りピークとして扱う")]
        [SerializeField] bool mouseSwingWhenDisconnected = true;

        // 同一フレームにまとめて届いた行の受信時刻はほぼ同じになるため、間隔が詰まりすぎたら名目間隔で補う
        const double MinSampleInterval = 0.005;

        readonly SwingPeakDetector _detector = new SwingPeakDetector();
        readonly Queue<(float strength, double time)> _peaks = new Queue<(float, double)>();

        ControllerSample _latest;
        double _detectorTime = double.NegativeInfinity;
        int _mouseConsumedFrame = -1;

        public bool IsConnected => con != null && con.isConnected;
        public float Yaw => con != null ? con.yaw : 0f;

        /// <summary>ファームウェアがボタンを送ってきているか。</summary>
        public bool HasHardwareButtons => _latest.HasButtons;

        public bool IsFrontHeld => _latest.HasButtons
            ? _latest.IsFrontHeld
            : keyboardFallbackForButtons && Input.GetKey(frontKey);

        void OnEnable()
        {
            if (con != null) con.SampleReceived += OnSample;
        }

        void OnDisable()
        {
            if (con != null) con.SampleReceived -= OnSample;
            _detector.Reset();
            _peaks.Clear();
        }

        void OnSample(ControllerSample sample)
        {
            _latest = sample;

            _detector.TriggerVelocity = triggerVelocity;
            _detector.ReleaseVelocity = releaseVelocity;
            _detector.Direction = swingIsPitchDecrease ? -1f : 1f;
            _detector.MaxSampleGap = maxSampleGap;

            double t = sample.Time;
            if (t < _detectorTime + MinSampleInterval)
                t = _detectorTime + nominalSampleInterval;
            _detectorTime = t;

            // ピーク時刻は受信時刻で返す（遅延計測 #52 と揃える）
            if (_detector.AddSample(sample.Pitch, t, out float peakVelocity))
                _peaks.Enqueue((peakVelocity, sample.Time));
        }

        public bool IsColorHeld(int index)
        {
            if (_latest.HasButtons) return _latest.IsColorHeld(index);
            return keyboardFallbackForButtons && colorKeys != null
                && index >= 0 && index < colorKeys.Length && Input.GetKey(colorKeys[index]);
        }

        public bool TryConsumeSwingPeak(out float strength, out double time)
        {
            if (_peaks.Count > 0)
            {
                (strength, time) = _peaks.Dequeue();
                return true;
            }

            strength = 0f;
            time = 0.0;
            if (mouseSwingWhenDisconnected && !IsConnected
                && _mouseConsumedFrame != Time.frameCount && Input.GetMouseButtonDown(0))
            {
                _mouseConsumedFrame = Time.frameCount;
                strength = 1f;
                time = Time.realtimeSinceStartupAsDouble;
                return true;
            }
            return false;
        }
    }
}
