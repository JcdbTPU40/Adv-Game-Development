using System;
using UnityEngine;

namespace Toufuku.Playtest
{
    /*
        ほかの機能から計測ログに書きこむための窓口（#63）

        計測ロガー（PlaytestLogger）がシーンにないときや、記録中じゃないときは何もしない
        時刻（ゲームが始まってからの秒・realtime・フレーム）はロガーが入れる
        #58: T1 の記録は時刻 t を渡せる（渡せばその秒で残す。段階の締切 0:08.000 などを、見つけたフレームの時刻でなく締切の時刻で残すため）

        だれが何を呼ぶか:
        ・#58 段階学習（StagedLearningDirector）: T1StageStart / T1StageEnd / T1FreePractice / T1CounterReset / T1Ghost、
          0:30 のあとの状況べつゴーストは T1Intervention("ghost:…") も
        ・見ている人（手伝ったときの記録）: T1Intervention（段階学習の staffInterventionKey でも記録できる）
        ・T2 のテスト開始（使いたければ）: T2TrialStart
        ・#55 優先救済: ふつうは PriorityRescue（二重円と同じルール）を使う。入れかえたいときだけ PriorityTargetProvider を入れる
        ・#56 笑顔の伝播: SmilePropagation（伝播が成立するたびに SmileCarrier が呼ぶ）
    */
    public static class PlaytestLog
    {
        // T1Intervention の detail がこれで始まれば、スタッフではなく 0:30 のあとの状況べつゴーストの介入（#58）
        public const string GhostInterventionPrefix = "ghost";

        /*
            優先の相手（二重円の客）のIDを返す関数。null の間は #55 のルール
            （PriorityRescue: 画面の中の候補のうち D がいちばん大きい → 遠い → active になったのが早い → 生成IDが小さい）を使う
            検証のシーンで相手をわざと決めたいときだけ入れかえる
        */
        public static Func<int?> PriorityTargetProvider;

        public static bool IsRecording => PlaytestLogger.Active != null && PlaytestLogger.Active.IsRecording;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            PriorityTargetProvider = null;
        }

        // T1: 学習の段階 stage（1〜3）を始めた
        public static void T1StageStart(int stage, double? t = null)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1StageStart) { Stage = stage }, t);
        }

        // T1: 学習の段階 stage をクリアした（achieved = true）、または時間切れで次に進んだ（false = クリアしていない）
        public static void T1StageEnd(int stage, bool achieved, double? t = null)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1StageEnd)
            {
                Stage = stage,
                Detail = achieved ? "achieved" : "timeout"
            }, t);
        }

        // T1: 自由練習の秒数（呼ばなければ、3段階目をクリアしてから学習の区間が終わるまでで集計する）
        public static void T1FreePractice(double seconds, double? t = null)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1FreePractice) { Value = seconds }, t);
        }

        // T1: 0:30.000 でカウンターを最初にもどした（本番スタート）
        public static void T1CounterReset(double? t = null)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1CounterReset), t);
        }

        // T1: ゴーストを表示した（段階1で3秒止まったとき、0:30 のあとの状況べつ）。#58 は "stage1_idle:swing" / "after_learning:aim" の形
        public static void T1Ghost(string situation, double? t = null)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1Ghost) { Detail = situation }, t);
        }

        // 手伝った（スタッフの介入は "staff"。0:30 のあとの状況べつゴーストは "ghost:…"）
        public static void T1Intervention(string note, double? t = null)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1Intervention) { Detail = note }, t);
        }

        // T2: テストを始めた（決めるまでの秒のスタート）。呼ばなければゲームの開始がスタート
        public static void T2TrialStart()
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T2TrialStart));
        }

        /*
            笑顔が1件伝わった（#56）。SmileCarrier から呼ぶ

            完了条件「倍率スナップショットの値がログで追える」はこの行で満たす
            multiplier の列に入るのは、伝わった時点の倍率じゃなくて「救済できたときに保存した」値
            （福の連なり倍率 × ご加護倍率）

            rescuerId: 笑顔をくばった救済客のID（矢印のはじまり）
            targetId: 笑顔をうけとった客のID
            gain: この1回で入った縁
            snapshotMultiplier: 救済したときに保存した倍率（福の連なり × ご加護）
            order: その救済で何人目か（1〜4）
            en: 足したあとの縁の合計。わからなければ null
        */
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

        // 好きな目印を残す（event の列 = eventType）
        public static void Marker(string eventType, string detail = null, double? value = null)
        {
            if (string.IsNullOrEmpty(eventType)) return;
            Record(new PlaytestEvent(0, eventType) { Detail = detail, Value = value });
        }

        static void Record(PlaytestEvent e, double? t = null)
        {
            PlaytestLogger logger = PlaytestLogger.Active;
            if (logger == null) return;
            if (t.HasValue) logger.RecordAt(e, t.Value);
            else logger.Record(e);
        }
    }
}
