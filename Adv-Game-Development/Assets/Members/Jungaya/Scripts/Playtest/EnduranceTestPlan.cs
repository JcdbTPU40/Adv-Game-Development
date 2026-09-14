using System;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /// <summary>
    /// T0-3M 標準耐久テストの時間・合格ラインの数値 — Issue #53（仕様書 v8 17章）
    ///
    /// ・採用案（T0-A/B #49）・採用クールダウン（T0-CD #50）・USB を固定し、参拝客も得点もない標準ターゲットを 3 分振り続ける。
    /// ・対象層 10 人。合格ラインは「10 人中」の割合で持ち、人数が変わっても同じ割合で判定する。
    /// ・1 テスト 1 仮説。手応えの比較（T0-A/B）やクールダウン値の探索（T0-CD）は同時に行わない。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class EnduranceTestPlan
    {
        public const int DefaultParticipants = 10;

        /// <summary>1 人が振り続ける秒数（3 分）。</summary>
        public const float TrialSeconds = 180f;
        /// <summary>投数と命中率を区切る長さ（1 分ごと）。</summary>
        public const float MinuteSeconds = 60f;
        public const int MinuteCount = 3;

        /// <summary>3:00 で入力を締め切ってから記録へ移るまでの秒。飛翔中の弾（最長 0.65 秒）の着弾を待つ。</summary>
        public const float SettleSeconds = 1.0f;

        /// <summary>3 分完走すべき人数の割合（9/10）。</summary>
        public const double CompletionRatio = 0.9;
        /// <summary>最終 1 分の投数低下の上限（初分比 20% 以内）。</summary>
        public const double MaxThrowsDropRatio = 0.20;
        /// <summary>疲労 5 段階の中央値の上限（2/5 以下）。</summary>
        public const double MaxFatigueMedian = 2.0;
        /// <summary>実際にもう一度を選ぶべき人数の割合（7/10）。</summary>
        public const double RetryRatio = 0.7;

        public const int MinScale = 1;
        public const int MaxScale = 5;

        /// <summary>8章の負荷算術が前提にしている実操作周期（1.0〜1.4 秒／投）。</summary>
        public const double ExpectedCycleMinSeconds = 1.0;
        public const double ExpectedCycleMaxSeconds = 1.4;

        /// <summary>T0-CD が終わるまでの既定（付録B INPUT.CD の基準 0.50 秒）。採用値が出たらシーンで差し替える。</summary>
        public const CooldownPreset DefaultCooldown = CooldownPreset.Sec050;

        const double Epsilon = 1e-9;

        /// <summary>「9/10 以上」のような下限の人数（切り上げ）。</summary>
        public static int RequiredCount(int participants, double ratio) => AbTestPlan.RequiredCount(participants, ratio);

        /// <summary>試技開始からの秒 → 何分目か（0〜2）。負なら -1。3:00 以降は最後の分に入れる。</summary>
        public static int MinuteOf(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0.0) return -1;
            int minute = (int)Math.Floor(seconds / MinuteSeconds);
            return minute >= MinuteCount ? MinuteCount - 1 : minute;
        }

        /// <summary>CSV の見出しなどに使う区間名（"0-60s" など）。</summary>
        public static string MinuteLabel(int minute)
        {
            int from = (int)(minute * MinuteSeconds);
            int to = (int)((minute + 1) * MinuteSeconds);
            return $"{from}-{to}s";
        }

        public static bool IsValidScale(int value) => value >= MinScale && value <= MaxScale;

        public static bool DropWithinLimit(double dropRatio) => dropRatio <= MaxThrowsDropRatio + Epsilon;

        public static bool CycleWithinExpected(double seconds) =>
            seconds >= ExpectedCycleMinSeconds - Epsilon && seconds <= ExpectedCycleMaxSeconds + Epsilon;
    }
}
