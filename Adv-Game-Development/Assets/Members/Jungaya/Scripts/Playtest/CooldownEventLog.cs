using System.Globalization;
using System.Text;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /// <summary>今どちらのブロックか。</summary>
    public enum CooldownBlock
    {
        None = 0,
        /// <summary>単発（1 回振って止まる）。</summary>
        Single = 1,
        /// <summary>「自分の最速で 2 回振る」。</summary>
        Pair = 2
    }

    /// <summary>生ログの行の種類。</summary>
    public enum CooldownEventKind
    {
        SessionStart,
        SyncMark,
        BlockStart,
        BlockEnd,
        Accepted,
        Rejected
    }

    /// <summary>
    /// 発射 1 件 = 1 行の生ログ — Issue #50
    ///
    /// <b>外部動画の振りピークが意図の正本</b>なので、このログ単体では合否を決めない。
    /// ログと動画を同じ時間軸に乗せるために、
    ///
    /// ・1 列目の「経過秒」は <see cref="CooldownTestDirector"/> の開始からの秒で、画面にも同じ値を大きく出す
    ///   （動画にその数字が映るので、コマ送りしながら行と突き合わせられる）。
    /// ・V キーの同期マーク（画面全体が 1 回光る）も <see cref="CooldownEventKind.SyncMark"/> として残す。
    ///   光った瞬間のコマとこの行の経過秒を合わせれば、あとはフレーム数で換算できる。
    ///
    /// 動画を見返して数えるのは 4 つだけ:
    /// 単発の振り数／連投の組数／余分な発射／2 発目が出なかった組数（<see cref="CooldownConditionRecord"/> の video 列）。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class CooldownEventLog
    {
        public const string Header =
            "経過秒,参加者番号,試行順,クールダウン秒,ブロック,種別,理由,前からの間隔,強さ,備考";

        static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static string RowOf(
            double elapsedSeconds, int participantNo, int trialIndex, float cooldownSeconds,
            CooldownBlock block, CooldownEventKind kind, string reason = "",
            double intervalSeconds = 0.0, float strength = 0f, string note = "")
        {
            var sb = new StringBuilder();
            Add(sb, elapsedSeconds.ToString("0.000", Culture));
            Add(sb, participantNo.ToString(Culture));
            Add(sb, (trialIndex + 1).ToString(Culture));
            Add(sb, cooldownSeconds.ToString("0.00", Culture));
            Add(sb, LabelOf(block));
            Add(sb, LabelOf(kind));
            Add(sb, reason);
            Add(sb, intervalSeconds > 0.0 ? intervalSeconds.ToString("0.000", Culture) : "");
            Add(sb, strength > 0f ? strength.ToString("0.0", Culture) : "");
            Add(sb, note);
            return sb.ToString();
        }

        public static string LabelOf(CooldownBlock block)
        {
            switch (block)
            {
                case CooldownBlock.Single: return "単発";
                case CooldownBlock.Pair: return "連投";
                default: return "-";
            }
        }

        public static string LabelOf(CooldownEventKind kind)
        {
            switch (kind)
            {
                case CooldownEventKind.SessionStart: return "開始";
                case CooldownEventKind.SyncMark: return "同期";
                case CooldownEventKind.BlockStart: return "ブロック開始";
                case CooldownEventKind.BlockEnd: return "ブロック終了";
                case CooldownEventKind.Accepted: return "受理";
                default: return "却下";
            }
        }

        public static string LabelOf(SwingRejectReason reason)
        {
            switch (reason)
            {
                case SwingRejectReason.Cooldown: return "クールダウン";
                case SwingRejectReason.FrontHeld: return "正面ボタン";
                case SwingRejectReason.NoSelection: return "未選択";
                default: return "停止中";
            }
        }

        static void Add(StringBuilder sb, string field)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(AbTestCsv.Escape(field));
        }
    }
}
