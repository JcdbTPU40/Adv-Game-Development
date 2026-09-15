using System;
using UnityEngine;

namespace Toufuku.Playtest
{
    /// <summary>
    /// ほかの機能から計測ログへ書き込む窓口 — Issue #63
    ///
    /// 計測ロガー（<see cref="PlaytestLogger"/>）がシーンに無い・記録中でないときは何もしない。
    /// 時刻（セッション開始からの秒・realtime・フレーム）はロガーが入れる。
    ///
    /// | 呼ぶ側 | 呼ぶもの |
    /// |---|---|
    /// | #58 段階学習 | <see cref="T1StageStart"/> / <see cref="T1StageEnd"/> / <see cref="T1CounterReset"/> / <see cref="T1Ghost"/>（任意で <see cref="T1FreePractice"/>） |
    /// | 観察者（介入の記録） | <see cref="T1Intervention"/> |
    /// | T2 の試行開始（任意） | <see cref="T2TrialStart"/> |
    /// | #55 優先救済 | 既定で <c>PriorityRescue</c>（二重円と同じ規則）を使う。差し替えたいときだけ <see cref="PriorityTargetProvider"/> を入れる |
    /// | #56 笑顔の伝播 | <see cref="SmilePropagation"/>（<c>SmileCarrier</c> が成立のたびに呼ぶ） |
    /// </summary>
    public static class PlaytestLog
    {
        /// <summary>
        /// 優先対象（二重円の客）の ID を返す関数。null の間は #55 の規則
        /// （<c>PriorityRescue</c>：画面内の候補のうち D 最大 → 遠い → active 化が早い → 生成ID 昇順）を使う。
        /// 検証シーンで対象を人為的に固定したいときだけ差し替える。
        /// </summary>
        public static Func<int?> PriorityTargetProvider;

        public static bool IsRecording => PlaytestLogger.Active != null && PlaytestLogger.Active.IsRecording;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            PriorityTargetProvider = null;
        }

        /// <summary>T1: 学習段階 stage（1〜3）を始めた。</summary>
        public static void T1StageStart(int stage)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1StageStart) { Stage = stage });
        }

        /// <summary>T1: 学習段階 stage を達成した（achieved = true）／締切で次へ進んだ（false = 未達フラグ）。</summary>
        public static void T1StageEnd(int stage, bool achieved)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1StageEnd)
            {
                Stage = stage,
                Detail = achieved ? "achieved" : "timeout"
            });
        }

        /// <summary>T1: 自由練習の秒数（呼ばなければ 3 段階目の達成から学習区間の終わりまでで集計する）。</summary>
        public static void T1FreePractice(double seconds)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1FreePractice) { Value = seconds });
        }

        /// <summary>T1: 0:30.000 のカウンタ初期化（競技開始）。</summary>
        public static void T1CounterReset()
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1CounterReset));
        }

        /// <summary>T1: ゴーストを表示した（段階 1 の 3 秒停止／0:30 後の状況別）。</summary>
        public static void T1Ghost(string situation)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1Ghost) { Detail = situation });
        }

        /// <summary>スタッフが介入した。</summary>
        public static void T1Intervention(string note)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1Intervention) { Detail = note });
        }

        /// <summary>T2: 試行を開始した（決定秒の起点）。呼ばなければセッション開始が起点。</summary>
        public static void T2TrialStart()
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T2TrialStart));
        }

        /// <summary>
        /// 笑顔の伝播が 1 件成立した（#56）。<see cref="SmileCarrier"/> から呼ぶ。
        ///
        /// 完了条件「倍率スナップショットの値がログで追える」はこの行で満たす。
        /// multiplier 列に入るのは伝播が起きた時点の倍率ではなく、<b>救済完了時に保存した</b>値
        /// （福の連なり倍率 × ご加護倍率）。
        /// </summary>
        /// <param name="rescuerId">笑顔を配った救済客の生成ID（有向ペアの始点）。</param>
        /// <param name="targetId">笑顔を受け取った客の生成ID。</param>
        /// <param name="gain">この 1 回で入った縁。</param>
        /// <param name="snapshotMultiplier">救済時に保存した倍率（福の連なり × ご加護）。</param>
        /// <param name="order">この救済で何人目か（1〜4）。</param>
        /// <param name="en">計上後の累計縁。分からなければ null。</param>
        public static void SmilePropagation(int rescuerId, int targetId, int gain, double snapshotMultiplier,
            int order, int? en = null)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.Propagate)
            {
                TargetId = targetId,
                Gain = gain,
                En = en,
                Multiplier = snapshotMultiplier,
                Value = order,
                Detail = $"from:{rescuerId}"
            });
        }

        /// <summary>任意の目印を残す（event 列 = eventType）。</summary>
        public static void Marker(string eventType, string detail = null, double? value = null)
        {
            if (string.IsNullOrEmpty(eventType)) return;
            Record(new PlaytestEvent(0, eventType) { Detail = detail, Value = value });
        }

        static void Record(PlaytestEvent e)
        {
            PlaytestLogger logger = PlaytestLogger.Active;
            if (logger != null) logger.Record(e);
        }
    }
}
