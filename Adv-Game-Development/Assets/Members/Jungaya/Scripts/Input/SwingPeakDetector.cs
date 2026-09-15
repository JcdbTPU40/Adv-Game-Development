using UnityEngine;

namespace Toufuku.GameInput
{
    /*
        振りのピークを見つけるクラス（#51）

        大幣の角度のデータ（度）から角速度を出して、角速度がいちばん大きくなった瞬間を「振りのピーク」として返す
        ・振る向きの角速度が TriggerVelocity をこえたら追いかけ始めて、遅くなり始めたデータでピークに決める
        ・ピークのあとは、角速度が ReleaseVelocity 以下にもどるまで次を見つけない（ヒステリシス）
          1回の振りで2発出てしまうのはここで防いで、わざと連続で投げるときの間かくはクールダウン（InputStateMachine）で決める
        ・データの間かくが MaxSampleGap をこえたら、通信がとぎれたと考えて追いかけるのをやめる
        しきい値は T0-CD（#50）で、まちがい発射と連投の抜けを見ながら決める
    */
    public sealed class SwingPeakDetector
    {
        // 追いかけ始める角速度（度/秒）
        public float TriggerVelocity { get; set; } = 180f;
        // また見つけてもいいことにする角速度（度/秒）
        public float ReleaseVelocity { get; set; } = 60f;
        // +1 なら角度が増える向き、-1 なら減る向きを「振り」とする
        public float Direction { get; set; } = -1f;
        // この秒数より間があいたデータでは、角速度を計算しない
        public float MaxSampleGap { get; set; } = 0.1f;

        bool _hasPrev;
        float _prevAngle;
        double _prevTime;
        float _prevVelocity;
        float _peakVelocity;
        bool _tracking;
        bool _armed = true;

        // データを1つ足す。ピークを見つけたら true と、そのときの角速度（度/秒）を返す
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
                    // 遅くなり始めた＝さっきまでの一番大きい値がピーク
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
