using System;
using System.Text;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /*
        参加者1人 × クールダウンの値1つぶんの記録（#50 / 企画書 v8 17章・19章）

        「よけいな発射」「わざと連続で投げたときの抜け」「実際の連投の間かく」を別々に持つ
        値は2つのグループがある:

        ・ゲーム側（single* / pair* / interval*）: テスト中に自動で入る。その場で進めるのと、動画を見返すときの目安に使う
        ・動画側（video*）: あとからやる人が入れる。合格かどうかはこっちで決める。外の動画に映った振りを「振ろうとした」とする

        ゲーム側だけだと「振ったのに出なかった」のか「そもそも振っていない」のか見分けられないので、
        合格かどうかは動画側だけで数える。動画側がまだ入っていない（NotEntered）行は集計に入れない
        MonoBehaviour は使っていない
    */
    [Serializable]
    public class CooldownConditionRecord
    {
        // 動画側がまだ入っていないことを表す値
        public const int NotEntered = -1;

        public string testId = "T0-CD-1";
        // やった日（yyyy-MM-dd）。別の日にやったのかはこの日付で見分ける
        public string date = "";
        public string owner = "";
        // 参加者の番号（1から）。4でわった余りがラテン方格の行になる
        public int participantNo = 1;
        // ラテン方格の行（0〜3）
        public int orderIndex;
        // その人が何番目にためした条件か（0〜3）
        public int trialIndex;
        public CooldownPreset preset = CooldownPreset.Sec050;
        // 実際に使っていた秒数（Custom やとちゅうで変えたときも残せるように、実際の値で持つ）
        public float cooldownSeconds = 0.50f;

        /*
            ---- ゲーム側（自動） ----
            この条件でやる予定だった1回振りの回数
        */
        public int singleTarget;
        // 1回振りのブロックで、ゲームが受け付けた発射の数
        public int singleAccepted;
        // 1回振りのブロックで、クールダウンではじかれた数
        public int singleRejected;
        // この条件でやる予定だった連投の組の数
        public int pairTarget;
        // 連投のブロックで、ゲームが受け付けた発射の数
        public int pairAccepted;
        // 連投のブロックで、クールダウンではじかれた数
        public int pairRejected;
        // 連投のブロックで、2発とも出たと思われる組の数（間かくが判定のはんいに入った組）
        public int pairCompleted;

        // 実際の連投の間かく（秒）。ゲームが受け付けた1発目と2発目の差
        public int intervalCount;
        public float intervalMin;
        public float intervalMedian;
        public float intervalP75;

        /*
            ---- 動画側（あとから入れる。合格かどうかはこっちで決める） ----
            動画で数えた1回振りの回数
        */
        public int videoSingleSwings = NotEntered;
        // 動画で数えた「2回振った」組の数
        public int videoPairSets = NotEntered;
        // 動画の振りに合わないよけいな発射（＝まちがい発射）。1回振りと連投の合計
        public int videoExtraFires = NotEntered;
        // 動画では2回振っているのに2発目が出なかった組の数
        public int videoMissedSecond = NotEntered;

        // ---- 安全（ぜんぶの値で0件が完了条件） ----
        public int strapDeviation;
        public int caseContact;

        public string note = "";

        // まちがい発射のわりあいのわる数。振ろうとした回数の合計（1回振りは1回、連投1組は2回）
        public int IntendedSwings =>
            videoSingleSwings <= NotEntered || videoPairSets <= NotEntered
                ? 0
                : videoSingleSwings + videoPairSets * 2;

        public bool HasSafetyIncident => strapDeviation > 0 || caseContact > 0;

        // 動画側の4つがぜんぶ入っているか。合格かどうかに数えていい行か
        public bool IsComplete =>
            participantNo > 0 &&
            videoSingleSwings > NotEntered &&
            videoPairSets > NotEntered &&
            videoExtraFires > NotEntered &&
            videoMissedSecond > NotEntered;

        // 足りない項目（やる人のパネルに出す）。ぜんぶそろっていれば空の文字
        public string MissingFields()
        {
            var sb = new StringBuilder();
            if (participantNo <= 0) Append(sb, "参加者番号");
            if (videoSingleSwings <= NotEntered) Append(sb, "動画:単発振り数");
            if (videoPairSets <= NotEntered) Append(sb, "動画:連投組数");
            if (videoExtraFires <= NotEntered) Append(sb, "動画:余分な発射");
            if (videoMissedSecond <= NotEntered) Append(sb, "動画:2発目欠落");
            return sb.ToString();
        }

        // ゲーム側から見た、とりあえずのよけいな発射（動画を見返すときの目安。合格かどうかには使わない）
        public int ProvisionalExtraFires
        {
            get
            {
                int intended = singleTarget + pairTarget * 2;
                int accepted = singleAccepted + pairAccepted;
                return accepted > intended ? accepted - intended : 0;
            }
        }

        // ゲーム側から見た、とりあえずの2発目の抜け（上と同じ）
        public int ProvisionalMissedSecond
        {
            get
            {
                int missed = pairTarget - pairCompleted;
                return missed > 0 ? missed : 0;
            }
        }

        public void SetIntervals(IntervalStats stats)
        {
            if (stats == null) return;
            intervalCount = stats.Count;
            intervalMin = (float)stats.Min;
            intervalMedian = (float)stats.Median;
            intervalP75 = (float)stats.P75;
        }

        public CooldownConditionRecord Clone() => (CooldownConditionRecord)MemberwiseClone();

        static void Append(StringBuilder sb, string label)
        {
            if (sb.Length > 0) sb.Append('・');
            sb.Append(label);
        }
    }
}
