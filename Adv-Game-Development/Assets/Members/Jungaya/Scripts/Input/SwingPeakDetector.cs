using UnityEngine;

namespace Toufuku.GameInput
{
    /// <summary>
    /// 振りピーク検出 — Issue #51
    ///
    /// 大幣の角度サンプル（度）から角速度を求め、角速度が最大になった瞬間を「振りのピーク」として返す。
    /// ・振り方向の角速度が <see cref="TriggerVelocity"/> を超えたら追跡を始め、減速に転じたサンプルでピーク確定。
    /// ・ピーク後は角速度が <see cref="ReleaseVelocity"/> 以下に戻るまで再検出しない（ヒステリシス）。
    ///   1 回の振りで 2 発出るのはここで防ぎ、意図的な連投の間隔はクールダウン（InputStateMachine）で決める。
    /// ・サンプル間隔が <see cref="MaxSampleGap"/> を超えたら通信途切れとみなして追跡を捨てる。
    /// 閾値は T0-CD（#50）で誤発射／連投欠落を見ながら調整する。
    /// </summary>
    public sealed class SwingPeakDetector
    {
        /// <summary>追跡を始める角速度（度/秒）。</summary>
        public float TriggerVelocity { get; set; } = 180f;
        /// <summary>再検出を許可する角速度（度/秒）。</summary>
        public float ReleaseVelocity { get; set; } = 60f;
        /// <summary>+1 なら角度が増える向き、-1 なら減る向きを振りとみなす。</summary>
        public float Direction { get; set; } = -1f;
        /// <summary>この秒数より間隔が空いたサンプルでは角速度を計算しない。</summary>
        public float MaxSampleGap { get; set; } = 0.1f;

        bool _hasPrev;
        float _prevAngle;
        double _prevTime;
        float _prevVelocity;
        float _peakVelocity;
        bool _tracking;
        bool _armed = true;

        /// <summary>サンプルを 1 つ追加する。ピークを検出したら true と、そのピーク角速度（度/秒）を返す。</summary>
        public bool AddSample(float angle, double time, out float peakVelocity)
        {
            peakVelocity = 0f;

            if (!_hasPrev)
            {
                _hasPrev = true;
                _prevAngle = angle;
                _prevTime = time;
                return false;
            }

            double dt = time - _prevTime;
            if (dt <= 0.0) return false;

            float velocity = Mathf.DeltaAngle(_prevAngle, angle) / (float)dt * Mathf.Sign(Direction);
            _prevAngle = angle;
            _prevTime = time;

            if (dt > MaxSampleGap)
            {
                _tracking = false;
                _armed = true;
                _prevVelocity = 0f;
                return false;
            }

            bool detected = false;
            if (_tracking)
            {
                if (velocity < _prevVelocity)
                {
                    // 減速に転じた＝直前までの最大値がピーク
                    peakVelocity = _peakVelocity;
                    detected = true;
                    _tracking = false;
                    _armed = false;
                }
                else
                {
                    _peakVelocity = Mathf.Max(_peakVelocity, velocity);
                }
            }
            else if (_armed && velocity >= TriggerVelocity)
            {
                _tracking = true;
                _peakVelocity = velocity;
            }

            if (!_armed && !_tracking && velocity <= ReleaseVelocity)
                _armed = true;

            _prevVelocity = velocity;
            return detected;
        }

        public void Reset()
        {
            _hasPrev = false;
            _tracking = false;
            _armed = true;
            _prevVelocity = 0f;
            _peakVelocity = 0f;
        }
    }
}
