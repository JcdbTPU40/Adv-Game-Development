namespace Toufuku.Playtest
{
    // 1人ぶんの進み方。値は表示する順番と同じ
    public enum AbPhase
    {
        Ready = 0,       // 説明して、構えるのを待つ
        FirstTrial = 1,  // 1つ目の案を45秒
        Rest = 2,        // 60秒休けい
        SecondTrial = 3, // 2つ目の案を45秒
        Survey = 4,      // 聞き取り（時間の制限なし）
        Done = 5         // 記録した
    }

    /*
        1人ぶんの進み方を管理する時計（#49 / 企画書 v8 17章）

        45秒 → 60秒休けい → 45秒 → 聞き取り。時刻は秒で渡す（MonoBehaviour は使っていない）
        フレームが飛んでも、フェーズは1つずつ進む（Advance を毎フレーム呼ぶ）
    */
    public sealed class AbSessionClock
    {
        public double TrialSeconds = AbTestPlan.TrialSeconds;
        public double RestSeconds = AbTestPlan.RestSeconds;

        double _phaseStart;

        public AbPhase Phase { get; private set; } = AbPhase.Ready;

        // 今が45秒の本番中かどうか
        public bool IsTrial => Phase == AbPhase.FirstTrial || Phase == AbPhase.SecondTrial;

        // 本番の番号（0 = 1つ目の案、1 = 2つ目の案）。本番中じゃなければ -1
        public int TrialIndex => Phase == AbPhase.FirstTrial ? 0 : Phase == AbPhase.SecondTrial ? 1 : -1;

        // このフェーズに入ってからの秒数
        public double ElapsedSeconds(double now) => now - _phaseStart;

        // このフェーズの残りの秒数。時間で終わらないフェーズは 0
        public double RemainingSeconds(double now)
        {
            double duration = DurationOf(Phase);
            if (double.IsPositiveInfinity(duration)) return 0.0;
            double remaining = duration - (now - _phaseStart);
            return remaining > 0.0 ? remaining : 0.0;
        }

        // 1つ目の案を始める
        public void Begin(double now)
        {
            Phase = AbPhase.FirstTrial;
            _phaseStart = now;
        }

        // 時間で終わるフェーズを進める。変わったら true
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

        // やる人の操作で次のフェーズにとばす（休けいを早めに終わる・言いまちがえてやりなおすなど）
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

        // 聞き取りの記録が終わった
        public void FinishSurvey()
        {
            Phase = AbPhase.Done;
        }

        // 次の参加者へ
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

        // 参加者に見せる文字（どっちが案Aで、どっちが案Bかはかくす）
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
