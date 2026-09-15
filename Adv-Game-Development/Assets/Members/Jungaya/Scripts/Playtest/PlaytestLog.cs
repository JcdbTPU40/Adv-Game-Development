using System;
using UnityEngine;

namespace Toufuku.Playtest
{
    /*
        ほかの機能から計測ログに書きこむための窓口（#63）

        計測ロガー（PlaytestLogger）がシーンにないときや、記録中じゃないときは何もしない
        時刻（ゲームが始まってからの秒・realtime・フレーム）はロガーが入れる

        だれが何を呼ぶか:
        ・#58 段階学習: T1StageStart / T1StageEnd / T1CounterReset / T1Ghost（使いたければ T1FreePractice も）
        ・見ている人（手伝ったときの記録）: T1Intervention
        ・T2 のテスト開始（使いたければ）: T2TrialStart
        ・#55 優先救済: ふつうは PriorityRescue（二重円と同じルール）を使う。入れかえたいときだけ PriorityTargetProvider を入れる
        ・#56 笑顔の伝播: SmilePropagation（伝播が成立するたびに SmileCarrier が呼ぶ）
    */
    public static class PlaytestLog
    {
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
        public static void T1StageStart(int stage)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1StageStart) { Stage = stage });
        }

        // T1: 学習の段階 stage をクリアした（achieved = true）、または時間切れで次に進んだ（false = クリアしていない）
        public static void T1StageEnd(int stage, bool achieved)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1StageEnd)
            {
                Stage = stage,
                Detail = achieved ? "achieved" : "timeout"
            });
        }

        // T1: 自由練習の秒数（呼ばなければ、3段階目をクリアしてから学習の区間が終わるまでで集計する）
        public static void T1FreePractice(double seconds)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1FreePractice) { Value = seconds });
        }

        // T1: 0:30.000 でカウンターを最初にもどした（本番スタート）
        public static void T1CounterReset()
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1CounterReset));
        }

        // T1: ゴーストを表示した（段階1で3秒止まったとき、0:30 のあとの状況べつ）
        public static void T1Ghost(string situation)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1Ghost) { Detail = situation });
        }

        // スタッフが手伝った
        public static void T1Intervention(string note)
        {
            Record(new PlaytestEvent(0, PlaytestEventType.T1Intervention) { Detail = note });
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

        static void Record(PlaytestEvent e)
        {
            PlaytestLogger logger = PlaytestLogger.Active;
            if (logger != null) logger.Record(e);
        }
    }
}
