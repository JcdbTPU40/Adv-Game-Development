using System;

namespace Toufuku.Playtest
{
    /*
        大幣が止まっているかを見るクラス（#52。ドリフトのデータを取るとき）

        3章の正面のキャリブレーションを受け付ける条件と同じ「角速度 30deg/s 未満が 0.5 秒つづいた」で止まっているとする
        角速度はヨーとピッチの変化（1周したときの補正あり）を合わせたもの
        MonoBehaviour は使っていない
    */
    public sealed class UsbStillnessDetector
    {
        public float SpeedLimit { get; set; } = UsbGatePlan.StillAngularSpeed;
        public float HoldSeconds { get; set; } = UsbGatePlan.StillSeconds;

        bool _has;
        float _yaw;
        float _pitch;
        double _time;
        double _stillSince = double.NaN;

        public bool IsStill { get; private set; }
        public float LastSpeed { get; private set; }

        public void Reset()
        {
            _has = false;
            _stillSince = double.NaN;
            IsStill = false;
            LastSpeed = 0f;
        }

        public void Add(float yaw, float pitch, double time)
        {
            if (!_has)
            {
                _has = true;
                _yaw = yaw;
                _pitch = pitch;
                _time = time;
                return;
            }

            double dt = time - _time;
            if (dt <= 0.0) return;

            double dy = DeltaAngle(_yaw, yaw);
            double dp = DeltaAngle(_pitch, pitch);
            LastSpeed = (float)(Math.Sqrt(dy * dy + dp * dp) / dt);

            if (LastSpeed < SpeedLimit)
            {
                if (double.IsNaN(_stillSince)) _stillSince = _time;
            }
            else
            {
                _stillSince = double.NaN;
            }

            IsStill = !double.IsNaN(_stillSince) && time - _stillSince >= HoldSeconds - 1e-9;
            _yaw = yaw;
            _pitch = pitch;
            _time = time;
        }

        // from から to へのいちばん近い角度の差（-180〜180）
        public static double DeltaAngle(double from, double to)
        {
            double d = (to - from) % 360.0;
            if (d > 180.0) d -= 360.0;
            if (d < -180.0) d += 360.0;
            return d;
        }
    }
}
