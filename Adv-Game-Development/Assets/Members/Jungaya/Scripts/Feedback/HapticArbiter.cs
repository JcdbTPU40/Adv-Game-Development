namespace Toufuku.Feedback
{
    // 振動の種類。値が大きいほうが優先（救済成功 ＞ 命中 ＞ 発射）
    public enum HapticKind
    {
        Throw = 0,
        Hit = 1,
        Rescue = 2
    }

    // 1回ぶんの振動（長さと強さ）
    public readonly struct HapticPulse
    {
        public readonly HapticKind Kind;
        public readonly float Seconds;
        // 0〜1。弱・中・強
        public readonly float Amplitude;

        public HapticPulse(HapticKind kind, float seconds, float amplitude)
        {
            Kind = kind;
            Seconds = seconds;
            Amplitude = amplitude;
        }
    }

    /*
        振動の優先順位と入れかえを決めるクラス（#64 / 企画書 v8 15章）

        ・振動はいつも1本だけ出す。波形を足したりはしない
        ・今の振動が始まってから OverlapWindowSeconds（100ms）以内に次のお願いが来たら、
          優先度が同じか高いほうに入れかえて、低いほうはすてる
        ・100ms を過ぎてから来たお願いは、優先度に関係なく新しいほうに入れかえる（1本だけ出すのは同じ）
        ・MonoBehaviour は使っていない。時刻は秒で渡す
    */
    public sealed class HapticArbiter
    {
        public const double DefaultOverlapWindowSeconds = 0.100;

        public double OverlapWindowSeconds = DefaultOverlapWindowSeconds;

        HapticPulse _current;
        double _startTime;
        bool _hasPulse;

        // 振動をお願いする。採用されたら true（今の振動は入れかわる）。すてられたら false
        public bool Request(HapticPulse pulse, double now)
        {
            if (IsActive(now) && now - _startTime <= OverlapWindowSeconds && pulse.Kind < _current.Kind)
                return false;

            _current = pulse;
            _startTime = now;
            _hasPulse = true;
            return true;
        }

        // その時刻に振動が出ているかどうか
        public bool IsActive(double now)
        {
            return _hasPulse && now >= _startTime && now - _startTime < _current.Seconds;
        }

        // その時刻に出ている振動を返す。出ていなければ false
        public bool TryGetActive(double now, out HapticPulse pulse)
        {
            pulse = _current;
            return IsActive(now);
        }

        // その時刻のモーターの出力（0〜1）。重なっていても、いちばん上の1本の強さだけを返す
        public float AmplitudeAt(double now)
        {
            return IsActive(now) ? _current.Amplitude : 0f;
        }

        public void Stop()
        {
            _hasPulse = false;
        }
    }
}
