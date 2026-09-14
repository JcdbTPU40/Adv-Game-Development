namespace Toufuku.Playtest
{
    /// <summary>1 人分の進行。値は表示の並びと同じ順。</summary>
    public enum AbPhase
    {
        Ready = 0,       // 説明・構え待ち
        FirstTrial = 1,  // 1 案目 45 秒
        Rest = 2,        // 休憩 60 秒
        SecondTrial = 3, // 2 案目 45 秒
        Survey = 4,      // 聞き取り（時間制限なし）
        Done = 5         // 記録済み
    }

    /// <summary>
    /// 1 人分の進行時計 — Issue #49（仕様書 v8 17章）
    ///
    /// 45 秒 → 60 秒休憩 → 45 秒 → 聞き取り。時刻は秒で渡す（MonoBehaviour 非依存）。
    /// フレームが飛んでもフェーズは 1 つずつ進む（<see cref="Advance"/> を毎フレーム呼ぶ）。
    /// </summary>
    public sealed class AbSessionClock
    {
        public double TrialSeconds = AbTestPlan.TrialSeconds;
        public double RestSeconds = AbTestPlan.RestSeconds;

        double _phaseStart;

        public AbPhase Phase { get; private set; } = AbPhase.Ready;

        /// <summary>今が 45 秒の試技中か。</summary>
        public bool IsTrial => Phase == AbPhase.FirstTrial || Phase == AbPhase.SecondTrial;

        /// <summary>試技の番号（0 = 1 案目、1 = 2 案目）。試技中でなければ -1。</summary>
        public int TrialIndex => Phase == AbPhase.FirstTrial ? 0 : Phase == AbPhase.SecondTrial ? 1 : -1;

        /// <summary>このフェーズに入ってからの秒数。</summary>
        public double ElapsedSeconds(double now) => now - _phaseStart;

        /// <summary>このフェーズの残り秒数。時間で終わらないフェーズは 0。</summary>
        public double RemainingSeconds(double now)
        {
            double duration = DurationOf(Phase);
            if (double.IsPositiveInfinity(duration)) return 0.0;
            double remaining = duration - (now - _phaseStart);
            return remaining > 0.0 ? remaining : 0.0;
        }

        /// <summary>1 案目を始める。</summary>
        public void Begin(double now)
        {
            Phase = AbPhase.FirstTrial;
            _phaseStart = now;
        }

        /// <summary>時間で終わるフェーズを進める。変わったら true。</summary>
        public bool Advance(double now)
        {
            bool changed = false;
            while (true)
            {
                double duration = DurationOf(Phase);
                if (double.IsPositiveInfinity(duration)) break;
                if (now - _phaseStart < duration) break;

                _phaseStart += duration;
                Phase = NextOf(Phase);
                changed = true;
            }
            return changed;
        }

        /// <summary>実施者の操作で次のフェーズへ飛ばす（休憩を切り上げる・言い直しで撮り直すなど）。</summary>
        public bool Skip(double now)
        {
            if (Phase == AbPhase.Ready)
            {
                Begin(now);
                return true;
            }
            if (Phase == AbPhase.Done) return false;

            Phase = NextOf(Phase);
            _phaseStart = now;
            return true;
        }

        /// <summary>聞き取りを記録し終えた。</summary>
        public void FinishSurvey()
        {
            Phase = AbPhase.Done;
        }

        /// <summary>次の参加者へ。</summary>
        public void Reset()
        {
            Phase = AbPhase.Ready;
            _phaseStart = 0.0;
        }

        public double DurationOf(AbPhase phase)
        {
            switch (phase)
            {
                case AbPhase.FirstTrial:
                case AbPhase.SecondTrial: return TrialSeconds;
                case AbPhase.Rest: return RestSeconds;
                default: return double.PositiveInfinity;
            }
        }

        public static AbPhase NextOf(AbPhase phase)
        {
            switch (phase)
            {
                case AbPhase.Ready: return AbPhase.FirstTrial;
                case AbPhase.FirstTrial: return AbPhase.Rest;
                case AbPhase.Rest: return AbPhase.SecondTrial;
                case AbPhase.SecondTrial: return AbPhase.Survey;
                default: return AbPhase.Done;
            }
        }

        /// <summary>参加者に見せる文言（どちらが案A / 案B かは伏せる）。</summary>
        public static string LabelOf(AbPhase phase)
        {
            switch (phase)
            {
                case AbPhase.Ready: return "準備";
                case AbPhase.FirstTrial: return "1 回目";
                case AbPhase.Rest: return "休憩";
                case AbPhase.SecondTrial: return "2 回目";
                case AbPhase.Survey: return "聞き取り";
                default: return "終了";
            }
        }
    }
}
