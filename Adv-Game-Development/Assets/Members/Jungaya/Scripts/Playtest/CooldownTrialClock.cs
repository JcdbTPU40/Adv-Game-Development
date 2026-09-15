namespace Toufuku.Playtest
{
    // 1人ぶんの進み方。値は表示する順番と同じ
    public enum CooldownPhase
    {
        Ready = 0,         // 説明して、構えるのを待つ
        SingleBlock = 1,   // 1回振りを n 回
        BlockRest = 2,     // ブロックの間の短い休けい
        PairBlock = 3,     // 「自分の最速で2回振る」を m 組
        Record = 4,        // この条件の記録（安全のことや気づいたこと）と保存
        ConditionRest = 5, // 次の条件にうつる前の休けい
        Done = 6           // 4つの条件がぜんぶ終わった
    }

    /*
        1人ぶんの進み方を管理する時計（#50）

        4つの条件 ×（1回振りのブロック → 休けい → 連投のブロック → 記録 → 休けい）

        ブロックは時間じゃなくて、やる人の合図で終わる（回数で終わるテストなので）
        受け付けた数で自動で終わるようにすると、よけいな発射が出たときにブロックが1回早く終わって、
        わる数が測りたい値そのものに引きずられてしまうので、終わりは人が決める
        休けいだけは時間で自動で進む
        時刻は秒で渡す（MonoBehaviour は使っていない）
    */
    public sealed class CooldownTrialClock
    {
        public double BlockRestSeconds = CooldownTestPlan.BlockRestSeconds;
        public double ConditionRestSeconds = CooldownTestPlan.ConditionRestSeconds;
        // ためす条件の数
        public int ConditionCount = CooldownTestPlan.ConditionCount;

        double _phaseStart;

        public CooldownPhase Phase { get; private set; } = CooldownPhase.Ready;
        // 今その人が何番目の条件をためしているか（0から）
        public int TrialIndex { get; private set; }

        public bool IsBlock => Phase == CooldownPhase.SingleBlock || Phase == CooldownPhase.PairBlock;

        public CooldownBlock Block =>
            Phase == CooldownPhase.SingleBlock ? CooldownBlock.Single :
            Phase == CooldownPhase.PairBlock ? CooldownBlock.Pair : CooldownBlock.None;

        public double ElapsedSeconds(double now) => now - _phaseStart;

        // このフェーズの残りの秒数。時間で終わらないフェーズは 0
        public double RemainingSeconds(double now)
        {
            double duration = DurationOf(Phase);
            if (double.IsPositiveInfinity(duration)) return 0.0;
            double remaining = duration - (now - _phaseStart);
            return remaining > 0.0 ? remaining : 0.0;
        }

        // 1回振りのブロックを始める。trialIndex を渡すと、とちゅうの条件から始められる（記録の続きから）
        public void Begin(double now, int trialIndex = 0)
        {
            if (trialIndex < 0) trialIndex = 0;
            if (trialIndex > ConditionCount - 1) trialIndex = ConditionCount - 1;
            TrialIndex = trialIndex;
            Phase = CooldownPhase.SingleBlock;
            _phaseStart = now;
        }

        // やる人の合図で次へ。変わったら true
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

        // 時間で終わるフェーズ（休けい）を進める。変わったら true
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
                _phaseStart = carried; // たまった遅れを次のフェーズに持ちこす
                changed = true;
            }
            return changed;
        }

        // 次の参加者へ
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

        // この条件が最後かどうか
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
