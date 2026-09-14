using System;

namespace Toufuku.Playtest
{
    /// <summary>1 人分の進行。値は表示の並びと同じ順。</summary>
    public enum EndurancePhase
    {
        Ready = 0,  // 説明・構え待ち
        Trial = 1,  // 3 分振り続ける
        Settle = 2, // 入力締切後、飛翔中の弾の着弾を待つ
        Record = 3, // 直後の聞き取り（疲労・痛み・再挑戦）と保存
        Done = 4    // 記録済み
    }

    /// <summary>
    /// 1 人分の進行時計 — Issue #53
    ///
    /// 準備 → 3 分 → 着弾待ち → 記録。3 分は時間で自動的に終わる。
    /// 参加者が「やめたい」と言ったら（または安全のため実施者が止めたら）<see cref="StopEarly"/> で途中終了し、
    /// 完走していない記録になる。時刻は秒で渡す（MonoBehaviour 非依存）。
    /// </summary>
    public sealed class EnduranceTrialClock
    {
        public double TrialSeconds = EnduranceTestPlan.TrialSeconds;
        public double SettleSeconds = EnduranceTestPlan.SettleSeconds;

        double _trialStart;
        double _phaseStart;

        public EndurancePhase Phase { get; private set; } = EndurancePhase.Ready;

        /// <summary>3:00 より前に終了した（終了希望・中止）。</summary>
        public bool StoppedEarly { get; private set; }

        /// <summary>実際に振っていた秒。試技が終わった時点で決まる（途中終了ならそこまで）。</summary>
        public double EndSeconds { get; private set; }

        public bool IsTrial => Phase == EndurancePhase.Trial;

        /// <summary>試技が終わっていて、3:00 まで続けたか。</summary>
        public bool Completed => Phase >= EndurancePhase.Settle && !StoppedEarly;

        /// <summary>試技開始からの秒（着弾待ち中も進む。弾を発射した分へ振り分けるのに使う）。</summary>
        public double SinceTrialStart(double now) => Phase == EndurancePhase.Ready ? 0.0 : now - _trialStart;

        /// <summary>振っていた秒（試技中は経過、終わったら <see cref="EndSeconds"/>）。</summary>
        public double TrialElapsed(double now)
        {
            switch (Phase)
            {
                case EndurancePhase.Ready: return 0.0;
                case EndurancePhase.Trial: return Math.Min(Math.Max(0.0, now - _trialStart), TrialSeconds);
                default: return EndSeconds;
            }
        }

        /// <summary>このフェーズの残り秒数。時間で終わらないフェーズは 0。</summary>
        public double RemainingSeconds(double now)
        {
            double remaining;
            switch (Phase)
            {
                case EndurancePhase.Trial: remaining = TrialSeconds - (now - _trialStart); break;
                case EndurancePhase.Settle: remaining = SettleSeconds - (now - _phaseStart); break;
                default: return 0.0;
            }
            return remaining > 0.0 ? remaining : 0.0;
        }

        public void Begin(double now)
        {
            Phase = EndurancePhase.Trial;
            _trialStart = now;
            _phaseStart = now;
            StoppedEarly = false;
            EndSeconds = 0.0;
        }

        /// <summary>終了希望・中止で試技を終える。試技中でなければ何もしない。変わったら true。</summary>
        public bool StopEarly(double now)
        {
            if (Phase != EndurancePhase.Trial) return false;

            double elapsed = Math.Max(0.0, now - _trialStart);
            EndSeconds = Math.Min(elapsed, TrialSeconds);
            StoppedEarly = elapsed < TrialSeconds; // 3:00 を過ぎてから押されたぶんは完走のまま
            Phase = EndurancePhase.Settle;
            _phaseStart = now;
            return true;
        }

        /// <summary>時間で終わるフェーズを進める。フレームが飛んでも 1 回で追いつく。変わったら true。</summary>
        public bool Advance(double now)
        {
            bool changed = false;
            while (true)
            {
                if (Phase == EndurancePhase.Trial && now - _trialStart >= TrialSeconds)
                {
                    EndSeconds = TrialSeconds;
                    StoppedEarly = false;
                    Phase = EndurancePhase.Settle;
                    _phaseStart = _trialStart + TrialSeconds;
                    changed = true;
                }
                else if (Phase == EndurancePhase.Settle && now - _phaseStart >= SettleSeconds)
                {
                    Phase = EndurancePhase.Record;
                    _phaseStart += SettleSeconds;
                    changed = true;
                }
                else
                {
                    break;
                }
            }
            return changed;
        }

        /// <summary>聞き取りを保存し終えた。</summary>
        public void FinishRecord()
        {
            if (Phase == EndurancePhase.Record) Phase = EndurancePhase.Done;
        }

        /// <summary>次の参加者へ。</summary>
        public void Reset()
        {
            Phase = EndurancePhase.Ready;
            StoppedEarly = false;
            EndSeconds = 0.0;
            _trialStart = 0.0;
            _phaseStart = 0.0;
        }

        public static string LabelOf(EndurancePhase phase)
        {
            switch (phase)
            {
                case EndurancePhase.Ready: return "準備";
                case EndurancePhase.Trial: return "3 分";
                case EndurancePhase.Settle: return "着弾待ち";
                case EndurancePhase.Record: return "記録";
                default: return "終了";
            }
        }
    }
}
