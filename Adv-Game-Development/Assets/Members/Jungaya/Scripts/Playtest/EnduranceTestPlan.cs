using System;
using Toufuku.GameInput;

namespace Toufuku.Playtest
{
    /*
        T0-3M のふつうの耐久テストの時間と合格ラインの数値（#53 / 企画書 v8 17章）

        ・選んだ案（T0-A/B #49）・選んだクールダウン（T0-CD #50）・USB を固定して、客も得点もないふつうの的を3分振りつづける
        ・対象は10人。合格ラインは「10人中何人」のわりあいで持って、人数が変わっても同じわりあいで判定する
        ・1つのテストで調べることは1つだけ。手ごたえのくらべ（T0-A/B）やクールダウンの値さがし（T0-CD）はいっしょにやらない
        MonoBehaviour は使っていない
    */
    public static class EnduranceTestPlan
    {
        public const int DefaultParticipants = 10;

        // 1人が振りつづける秒数（3分）
        public const float TrialSeconds = 180f;
        // 投げた数と命中率を区切る長さ（1分ごと）
        public const float MinuteSeconds = 60f;
        public const int MinuteCount = 3;

        // 3:00 で入力をしめきってから、記録にうつるまでの秒。飛んでいる弾（いちばん長くて 0.65 秒）が落ちるのを待つ
        public const float SettleSeconds = 1.0f;

        // 3分最後までやってほしい人数のわりあい（9/10）
        public const double CompletionRatio = 0.9;
        // 最後の1分で投げた数が下がっていい上限（最初の分の 20% まで）
        public const double MaxThrowsDropRatio = 0.20;
        // 疲れの5段階の中央値の上限（2/5 以下）
        public const double MaxFatigueMedian = 2.0;
        // 本当にもう一回を選んでほしい人数のわりあい（7/10）
        public const double RetryRatio = 0.7;

        public const int MinScale = 1;
        public const int MaxScale = 5;

        // 8章の負荷の計算で、前提にしている実際に振る間かく（1投あたり 1.0〜1.4 秒）
        public const double ExpectedCycleMinSeconds = 1.0;
        public const double ExpectedCycleMaxSeconds = 1.4;

        // T0-CD が終わるまでのふつうの値（付録B INPUT.CD の基準の 0.50 秒）。選んだ値が出たらシーンで入れかえる
        public const CooldownPreset DefaultCooldown = CooldownPreset.Sec050;

        const double Epsilon = 1e-9;

        // 「9/10 以上」みたいな、一番少なくていい人数（切り上げ）
        public static int RequiredCount(int participants, double ratio) => AbTestPlan.RequiredCount(participants, ratio);

        // テストが始まってからの秒から、何分目か（0〜2）を出す。マイナスなら -1。3:00 をすぎたら最後の分に入れる
        public static int MinuteOf(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0.0) return -1;
            int minute = (int)Math.Floor(seconds / MinuteSeconds);
            return minute >= MinuteCount ? MinuteCount - 1 : minute;
        }

        // CSV の見出しとかに使う区間の名前（"0-60s" など）
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
