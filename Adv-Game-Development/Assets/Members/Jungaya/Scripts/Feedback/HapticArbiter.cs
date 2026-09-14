namespace Toufuku.Feedback
{
    /// <summary>振動の種類。値が大きいほど優先（救済成功 ＞ 命中 ＞ 発射）。</summary>
    public enum HapticKind
    {
        Throw = 0,
        Hit = 1,
        Rescue = 2
    }

    /// <summary>1 回分の振動（長さと強さ）。</summary>
    public readonly struct HapticPulse
    {
        public readonly HapticKind Kind;
        public readonly float Seconds;
        /// <summary>0〜1。弱 / 中 / 強。</summary>
        public readonly float Amplitude;

        public HapticPulse(HapticKind kind, float seconds, float amplitude)
        {
            Kind = kind;
            Seconds = seconds;
            Amplitude = amplitude;
        }
    }

    /// <summary>
    /// 振動の優先順位と置き換え — Issue #64（仕様書 v8 15章）
    ///
    /// ・振動は常に 1 本だけ出す。**波形は足さない**。
    /// ・再生中の振動が始まってから <see cref="OverlapWindowSeconds"/>（100ms）以内に次の要求が来たら、
    ///   優先度が同じか高いほうへ置き換え、低いほうは捨てる。
    /// ・窓を過ぎてから来た要求は、優先度に関係なく新しいほうへ置き換える（1 本だけ出す原則は同じ）。
    /// ・MonoBehaviour 非依存。時刻は秒で渡す。
    /// </summary>
    public sealed class HapticArbiter
    {
        public const double DefaultOverlapWindowSeconds = 0.100;

        public double OverlapWindowSeconds = DefaultOverlapWindowSeconds;

        HapticPulse _current;
        double _startTime;
        bool _hasPulse;

        /// <summary>
        /// 振動を要求する。採用されたら true（再生中の振動は置き換わる）。捨てられたら false。
        /// </summary>
        public bool Request(HapticPulse pulse, double now)
        {
            if (IsActive(now) && now - _startTime <= OverlapWindowSeconds && pulse.Kind < _current.Kind)
                return false;

            _current = pulse;
            _startTime = now;
            _hasPulse = true;
            return true;
        }

        /// <summary>その時刻に振動が出ているか。</summary>
        public bool IsActive(double now)
        {
            return _hasPulse && now >= _startTime && now - _startTime < _current.Seconds;
        }

        /// <summary>その時刻に出ている振動。出ていなければ false。</summary>
        public bool TryGetActive(double now, out HapticPulse pulse)
        {
            pulse = _current;
            return IsActive(now);
        }

        /// <summary>その時刻のモーター出力（0〜1）。重なっても上位 1 本の強さだけを返す。</summary>
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
