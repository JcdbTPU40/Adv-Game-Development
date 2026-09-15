using System;

namespace Toufuku.Playtest
{
    // 1人ぶんの進み方。値は表示する順番と同じ
    public enum EndurancePhase
    {
        Ready = 0,  // 説明して、構えるのを待つ
        Trial = 1,  // 3分振りつづける
        Settle = 2, // 入力をしめきったあと、飛んでいる弾が落ちるのを待つ
        Record = 3, // 直後の聞き取り（疲れ・痛い・もう一回）と保存
        Done = 4    // 記録した
    }

    /*
        1人ぶんの進み方を管理する時計（#53）

        準備 → 3分 → 弾が落ちるのを待つ → 記録。3分は時間で自動で終わる
        参加者が「やめたい」と言ったら（または安全のためにやる人が止めたら）StopEarly でとちゅうで終わって、
        最後までやっていない記録になる。時刻は秒で渡す（MonoBehaviour は使っていない）
    */
    public sealed class EnduranceTrialClock
    {
        public double TrialSeconds = EnduranceTestPlan.TrialSeconds;
        public double SettleSeconds = EnduranceTestPlan.SettleSeconds;

        double _trialStart;
        double _phaseStart;

        public EndurancePhase Phase { get; private set; } = EndurancePhase.Ready;

        // 3:00 より前に終わった（やめたい・中止）
        public bool StoppedEarly { get; private set; }

        // 実際に振っていた秒。テストが終わった時点で決まる（とちゅうで終わったらそこまで）
        public double EndSeconds { get; private set; }

        public bool IsTrial => Phase == EndurancePhase.Trial;

        // テストが終わっていて、3:00 まで続けたかどうか
        public bool Completed => Phase >= EndurancePhase.Settle && !StoppedEarly;

        // テストが始まってからの秒（弾が落ちるのを待っている間も進む。弾を発射した分にふり分けるのに使う）
        public double SinceTrialStart(double now) => Phase == EndurancePhase.Ready ? 0.0 : now - _trialStart;

        // 振っていた秒（テスト中は経過した時間、終わったら EndSeconds）
        public double TrialElapsed(double now)
        {
            switch (Phase)
            {
                case EndurancePhase.Ready: return 0.0;
                case EndurancePhase.Trial: return Math.Min(Math.Max(0.0, now - _trialStart), TrialSeconds);
                default: return EndSeconds;
            }
        }

        // このフェーズの残りの秒数。時間で終わらないフェーズは 0
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

        // やめたい・中止でテストを終わる。テスト中じゃなければ何もしない。変わったら true
        public bool StopEarly(double now)
        {
            if (Phase != EndurancePhase.Trial) return false;

            double elapsed = Math.Max(0.0, now - _trialStart);
            EndSeconds = Math.Min(elapsed, TrialSeconds);
            StoppedEarly = elapsed < TrialSeconds; // 3:00 をすぎてから押されたときは、最後までやったことにする
            Phase = EndurancePhase.Settle;
            _phaseStart = now;
            return true;
        }

        // 時間で終わるフェーズを進める。フレームが飛んでも1回で追いつく。変わったら true
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

        // 聞き取りを保存し終わった
        public void FinishRecord()
        {
            if (Phase == EndurancePhase.Record) Phase = EndurancePhase.Done;
        }

        // 次の参加者へ
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
