namespace Toufuku.Playtest
{
    /// <summary>1 人分の進行。値は表示の並びと同じ順。</summary>
    public enum CooldownPhase
    {
        Ready = 0,         // 説明・構え待ち
        SingleBlock = 1,   // 単発 n 回
        BlockRest = 2,     // ブロック間の短い休憩
        PairBlock = 3,     // 「自分の最速で 2 回振る」m 組
        Record = 4,        // この条件の記録（安全事象・所見）と保存
        ConditionRest = 5, // 次の条件へ移る前の休憩
        Done = 6           // 4 条件とも終わった
    }

    /// <summary>
    /// 1 人分の進行時計 — Issue #50
    ///
    /// 4 条件 ×（単発ブロック → 休憩 → 連投ブロック → 記録 → 休憩）。
    ///
    /// ブロックは<b>時間ではなく実施者の合図で終わる</b>（回数で終わるテストなので）。
    /// 受理数で自動終了させると、余分な発射が出たときにブロックが 1 回早く終わって
    /// 母数が計りたい値そのものに引きずられるため、終わりは人が決める。
    /// 休憩だけは時間で自動的に進む。
    /// 時刻は秒で渡す（MonoBehaviour 非依存）。
    /// </summary>
    public sealed class CooldownTrialClock
    {
        public double BlockRestSeconds = CooldownTestPlan.BlockRestSeconds;
        public double ConditionRestSeconds = CooldownTestPlan.ConditionRestSeconds;
        /// <summary>試す条件の数。</summary>
        public int ConditionCount = CooldownTestPlan.ConditionCount;

        double _phaseStart;

        public CooldownPhase Phase { get; private set; } = CooldownPhase.Ready;
        /// <summary>今その人が何番目の条件を試しているか（0 始まり）。</summary>
        public int TrialIndex { get; private set; }

        public bool IsBlock => Phase == CooldownPhase.SingleBlock || Phase == CooldownPhase.PairBlock;

        public CooldownBlock Block =>
            Phase == CooldownPhase.SingleBlock ? CooldownBlock.Single :
            Phase == CooldownPhase.PairBlock ? CooldownBlock.Pair : CooldownBlock.None;

        public double ElapsedSeconds(double now) => now - _phaseStart;

        /// <summary>このフェーズの残り秒数。時間で終わらないフェーズは 0。</summary>
        public double RemainingSeconds(double now)
        {
            double duration = DurationOf(Phase);
            if (double.IsPositiveInfinity(duration)) return 0.0;
            double remaining = duration - (now - _phaseStart);
            return remaining > 0.0 ? remaining : 0.0;
        }

        /// <summary>単発ブロックを始める。trialIndex を渡すと途中の条件から再開できる（記録の続きから）。</summary>
        public void Begin(double now, int trialIndex = 0)
        {
            if (trialIndex < 0) trialIndex = 0;
            if (trialIndex > ConditionCount - 1) trialIndex = ConditionCount - 1;
            TrialIndex = trialIndex;
            Phase = CooldownPhase.SingleBlock;
            _phaseStart = now;
        }

        /// <summary>実施者の合図で次へ。変わったら true。</summary>
        public bool Next(double now)
        {
            if (Phase == CooldownPhase.Ready)
            {
                Begin(now);
                return true;
            }
            if (Phase == CooldownPhase.Done) return false;

            Step(now);
            return true;
        }

        /// <summary>時間で終わるフェーズ（休憩）を進める。変わったら true。</summary>
        public bool Advance(double now)
        {
            bool changed = false;
            while (true)
            {
                double duration = DurationOf(Phase);
                if (double.IsPositiveInfinity(duration)) break;
                if (now - _phaseStart < duration) break;

                _phaseStart += duration;
                double carried = _phaseStart;
                Step(now);
                _phaseStart = carried; // 溜まった遅れを次のフェーズへ持ち越す
                changed = true;
            }
            return changed;
        }

        /// <summary>次の参加者へ。</summary>
        public void Reset()
        {
            Phase = CooldownPhase.Ready;
            TrialIndex = 0;
            _phaseStart = 0.0;
        }

        public double DurationOf(CooldownPhase phase)
        {
            switch (phase)
            {
                case CooldownPhase.BlockRest: return BlockRestSeconds;
                case CooldownPhase.ConditionRest: return ConditionRestSeconds;
                default: return double.PositiveInfinity;
            }
        }

        /// <summary>この条件が最後か。</summary>
        public bool IsLastCondition => TrialIndex >= ConditionCount - 1;

        void Step(double now)
        {
            switch (Phase)
            {
                case CooldownPhase.SingleBlock:
                    Phase = CooldownPhase.BlockRest;
                    break;
                case CooldownPhase.BlockRest:
                    Phase = CooldownPhase.PairBlock;
                    break;
                case CooldownPhase.PairBlock:
                    Phase = CooldownPhase.Record;
                    break;
                case CooldownPhase.Record:
                    Phase = IsLastCondition ? CooldownPhase.Done : CooldownPhase.ConditionRest;
                    break;
                case CooldownPhase.ConditionRest:
                    TrialIndex++;
                    Phase = CooldownPhase.SingleBlock;
                    break;
                default:
                    Phase = CooldownPhase.Done;
                    break;
            }
            _phaseStart = now;
        }

        public static string LabelOf(CooldownPhase phase)
        {
            switch (phase)
            {
                case CooldownPhase.Ready: return "準備";
                case CooldownPhase.SingleBlock: return "単発";
                case CooldownPhase.BlockRest: return "休憩";
                case CooldownPhase.PairBlock: return "連投";
                case CooldownPhase.Record: return "記録";
                case CooldownPhase.ConditionRest: return "休憩（次の設定へ）";
                default: return "終了";
            }
        }
    }
}
