namespace Toufuku.Playtest
{
    /// <summary>T6-USB の記録の「event」列に入る種別。</summary>
    public static class UsbGateEventType
    {
        /// <summary>区間の開始。</summary>
        public const string SectionStart = "section_start";
        /// <summary>区間の終わり（flag = 1 最後まで行った / 0 中断）。中断した区間は合否に使わない。</summary>
        public const string SectionEnd = "section_end";
        /// <summary>計測条件（label = 項目名、detail = 値）。区間の開始時に書く。</summary>
        public const string Env = "env";
        /// <summary>安全チェック 1 項目（label = 項目 id、flag = 1 適合）。</summary>
        public const string SafetyItem = "safety_item";
        /// <summary>安全事象（label = contact 接触 / deviation 領域からの逸脱）。</summary>
        public const string Incident = "incident";
        /// <summary>意図的 100 投の合図（seq = 合図番号 1〜、t = 合図の秒）。</summary>
        public const string Cue = "cue";
        /// <summary>合図を無効にした（振らなかった・合図を見ていなかった）。seq = 合図番号。</summary>
        public const string CueVoid = "cue_void";
        /// <summary>
        /// 発射確定（seq = 区間内の発射番号）。
        /// input_time = コントローラの時計、input_unity = それを Unity の時計へ合わせた推定、
        /// receive_time = Unity 受信、fire_time = 発射確定、visible_time = 弾が画面に出た（その描画の present 後）。
        /// value = 振りの強さ。
        /// </summary>
        public const string Fire = "fire";
        /// <summary>振りピークを却下した（label = 理由、receive_time = 受信）。</summary>
        public const string Reject = "reject";
        /// <summary>子どもの着弾（seq = 発射番号、label = near / far、value = 的の中心からの距離 m、flag = 1 命中）。</summary>
        public const string Landing = "landing";
        /// <summary>子どもの区間の段落（label = practice / near / far、t = 始まった秒）。</summary>
        public const string Block = "block";
        /// <summary>受信の途絶（value = 途絶秒。区間の終わりまで戻らなければ終わりまでの秒）。</summary>
        public const string Disconnect = "disconnect";
        /// <summary>ドリフトの標本（label = start / end / track、value = 照準の画面 X ÷ 画面幅、flag = 1 静止を確認）。</summary>
        public const string DriftSample = "drift_sample";
        /// <summary>区間の集計値（label = 指標名、value = 値）。</summary>
        public const string Metric = "metric";
    }

    /// <summary><see cref="UsbGateEventType.Metric"/> の label。</summary>
    public static class UsbGateMetric
    {
        public const string AverageFps = "avg_fps";
        public const string OnePercentLowFps = "low1_fps";
        public const string WorstFrameMs = "worst_frame_ms";
        public const string Frames = "frames";
        public const string RenderedMin = "rendered_min";
        public const string RenderedAverage = "rendered_avg";
        public const string BlackAverage = "black_avg";
        public const string ExitingAverage = "exiting_avg";
        public const string Samples = "samples";
        public const string SampleRateHz = "sample_rate_hz";
        public const string MaxGapMs = "max_gap_ms";
        /// <summary>区間の開始時に実機（ESP32）とつながっていたか（1 / 0）。</summary>
        public const string Connected = "connected";
        /// <summary>5 項目目（millis）つきの受信行の数。0 なら入力時刻は測れていない。</summary>
        public const string DeviceTimeSamples = "device_time_samples";
    }

    /// <summary>
    /// T6-USB の記録 1 行 — Issue #52
    ///
    /// 区間（安全チェック・意図的 100 投・ドリフト・30 体負荷・子ども）をまたいで、1 つの縦長の CSV に 1 イベント 1 行で残す。
    /// 数値の空欄は欠測（null）で、0 とは区別する。MonoBehaviour 非依存。
    /// </summary>
    public sealed class UsbGateEvent
    {
        public string TestId = UsbGatePlan.DefaultTestId;
        /// <summary>実施日（yyyy-MM-dd）。</summary>
        public string Date = "";
        /// <summary>区間を始めるたびに 1 つ進む通し番号。同じ区間をやり直したときは、最後に最後まで行った回を使う。</summary>
        public int Run;
        /// <summary><see cref="UsbGatePlan.KeyOf"/> の値。</summary>
        public string Section = "";
        /// <summary>子どもの参加者番号（1〜）。ほかの区間は 0。氏名は書かない。</summary>
        public int Participant;
        public int Seq;
        public string Event = "";
        /// <summary>区間の開始からの秒。</summary>
        public double T;

        public double? InputTime;
        public double? InputUnity;
        public double? ReceiveTime;
        public double? FireTime;
        public double? VisibleTime;

        public double? Value;
        public string Label = "";
        public bool Flag;
        public string Detail = "";

        public bool Is(string type) => Event == type;

        public bool IsSection(UsbGateSection section) => Section == UsbGatePlan.KeyOf(section);
    }
}
