using System.Globalization;
using System.Text;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    // 今どっちのブロックか
    public enum CooldownBlock
    {
        None = 0,
        // 1回振り（1回振って止まる）
        Single = 1,
        // 「自分の最速で2回振る」
        Pair = 2
    }

    // そのままのログの行の種類
    public enum CooldownEventKind
    {
        SessionStart,
        SyncMark,
        BlockStart,
        BlockEnd,
        Accepted,
        Rejected
    }

    /*
        発射1回を1行にしたそのままのログ（#50）

        「振ろうとした」のは外の動画に映った振りで決めるので、このログだけでは合格かどうかは決めない
        ログと動画を同じ時間のものさしで見られるように、

        ・1列目の「経過秒」は CooldownTestDirector が始まってからの秒で、画面にも同じ値を大きく出す
          （動画にその数字が映るので、コマ送りしながら行と照らし合わせられる）
        ・Vキーの同期マーク（画面全体が1回光る）も CooldownEventKind.SyncMark として残す
          光ったコマとこの行の経過秒を合わせれば、あとはフレーム数で計算できる

        動画を見返して数えるのは4つだけ:
        1回振りの回数、連投の組の数、よけいな発射、2発目が出なかった組の数（CooldownConditionRecord の video の列）
        MonoBehaviour は使っていない
    */
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
