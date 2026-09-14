using System;
using System.Text;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 参加者 1 人 × クールダウン値 1 条件分の記録 — Issue #50（仕様書 v8 17章・19章）
    ///
    /// 「余分な発射」「意図的連投の欠落」「実連投間隔」を <b>別々に</b> 持つ。
    /// 値は 2 系統ある:
    ///
    /// | 系統 | 誰が入れるか | 使いみち |
    /// |---|---|---|
    /// | ゲーム側（<c>single*</c> / <c>pair*</c> / <c>interval*</c>） | 実施中に自動 | その場の進行と、動画を見返すときの当たり |
    /// | 動画側（<c>video*</c>） | あとから実施者 | <b>合否の正本</b>。外部動画の振りピークが意図 |
    ///
    /// ゲーム側だけでは「振ったのに出なかった」と「そもそも振っていない」が区別できないので、
    /// 合否は動画側だけで数える。動画側が未入力（<see cref="NotEntered"/>）の行は集計に入らない。
    /// MonoBehaviour 非依存。
    /// </summary>
    [Serializable]
    public class CooldownConditionRecord
    {
        /// <summary>動画側が未入力であることを表す値。</summary>
        public const int NotEntered = -1;

        public string testId = "T0-CD-1";
        /// <summary>実施日（yyyy-MM-dd）。別日の再現はこの日付で見分ける。</summary>
        public string date = "";
        public string owner = "";
        /// <summary>参加者番号（1 始まり）。4 で割った余りがラテン方格の行になる。</summary>
        public int participantNo = 1;
        /// <summary>ラテン方格の行（0〜3）。</summary>
        public int orderIndex;
        /// <summary>その人が何番目に試した条件か（0〜3）。</summary>
        public int trialIndex;
        public CooldownPreset preset = CooldownPreset.Sec050;
        /// <summary>実際に適用していた秒数（Custom や途中変更を残すため実測値で持つ）。</summary>
        public float cooldownSeconds = 0.50f;

        // ── ゲーム側（自動）──
        /// <summary>この条件で行う予定だった単発の回数。</summary>
        public int singleTarget;
        /// <summary>単発ブロックでゲームが受理した発射数。</summary>
        public int singleAccepted;
        /// <summary>単発ブロックでクールダウンにより却下された数。</summary>
        public int singleRejected;
        /// <summary>この条件で行う予定だった連投の組数。</summary>
        public int pairTarget;
        /// <summary>連投ブロックでゲームが受理した発射数。</summary>
        public int pairAccepted;
        /// <summary>連投ブロックでクールダウンにより却下された数。</summary>
        public int pairRejected;
        /// <summary>連投ブロックで 2 発とも出たと見られる組数（間隔が判定窓に収まった組）。</summary>
        public int pairCompleted;

        /// <summary>実連投間隔（秒）。ゲームが受理した 1 発目と 2 発目の差。</summary>
        public int intervalCount;
        public float intervalMin;
        public float intervalMedian;
        public float intervalP75;

        // ── 動画側（あとから入れる。合否の正本）──
        /// <summary>動画で数えた単発の振り数。</summary>
        public int videoSingleSwings = NotEntered;
        /// <summary>動画で数えた「2 回振った」組数。</summary>
        public int videoPairSets = NotEntered;
        /// <summary>動画の振りに対応しない余分な発射（＝誤発射）。単発・連投の合計。</summary>
        public int videoExtraFires = NotEntered;
        /// <summary>動画では 2 回振っているのに 2 発目が出なかった組数。</summary>
        public int videoMissedSecond = NotEntered;

        // ── 安全（全値で 0 件が完了条件）──
        public int strapDeviation;
        public int caseContact;

        public string note = "";

        /// <summary>誤発射率の母数。意図した振りの合計（単発 1 回 + 連投 1 組 = 2 回）。</summary>
        public int IntendedSwings =>
            videoSingleSwings <= NotEntered || videoPairSets <= NotEntered
                ? 0
                : videoSingleSwings + videoPairSets * 2;

        public bool HasSafetyIncident => strapDeviation > 0 || caseContact > 0;

        /// <summary>動画側が 4 項目とも入っているか。合否に数えられる行か。</summary>
        public bool IsComplete =>
            participantNo > 0 &&
            videoSingleSwings > NotEntered &&
            videoPairSets > NotEntered &&
            videoExtraFires > NotEntered &&
            videoMissedSecond > NotEntered;

        /// <summary>足りない項目（実施者パネルに出す）。すべてそろっていれば空文字。</summary>
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

        /// <summary>ゲーム側から見た暫定の余分な発射（動画を見返すときの当たりに使う。合否には使わない）。</summary>
        public int ProvisionalExtraFires
        {
            get
            {
                int intended = singleTarget + pairTarget * 2;
                int accepted = singleAccepted + pairAccepted;
                return accepted > intended ? accepted - intended : 0;
            }
        }

        /// <summary>ゲーム側から見た暫定の 2 発目欠落（同上）。</summary>
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
