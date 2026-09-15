namespace Toufuku.Playtest
{
    // T6-USB の記録の「event」列に入る種類
    public static class UsbGateEventType
    {
        // 区間が始まった
        public const string SectionStart = "section_start";
        // 区間が終わった（flag = 1 最後までやった / 0 やめた）。やめた区間は合格かどうかに使わない
        public const string SectionEnd = "section_end";
        // 測ったときの条件（label = 項目の名前、detail = 値）。区間が始まったときに書く
        public const string Env = "env";
        // 安全チェックの1項目（label = 項目の id、flag = 1 OK）
        public const string SafetyItem = "safety_item";
        // 安全のこと（label = contact ぶつかった / deviation 安全な場所からはみ出た）
        public const string Incident = "incident";
        // わざと100投の合図（seq = 合図の番号 1から、t = 合図の秒）
        public const string Cue = "cue";
        // 合図をなしにした（振らなかった・合図を見ていなかった）。seq = 合図の番号
        public const string CueVoid = "cue_void";
        /*
            発射が決まった（seq = 区間の中の発射の番号）
            input_time = コントローラーの時計、input_unity = それを Unity の時計に合わせて出した値、
            receive_time = Unity が受け取った時刻、fire_time = 発射が決まった時刻、visible_time = 弾が画面に出た時刻（その絵の present のあと）
            value = 振りの強さ
        */
        public const string Fire = "fire";
        // 振りピークをはじいた（label = 理由、receive_time = 受け取った時刻）
        public const string Reject = "reject";
        // 子どもの着弾（seq = 発射の番号、label = near / far、value = 的の中心からの距離 m、flag = 1 当たり）
        public const string Landing = "landing";
        // 子どもの区間の区切り（label = practice / near / far、t = 始まった秒）
        public const string Block = "block";
        // 受け取りがとぎれた（value = とぎれた秒。区間の終わりまでもどらなければ、終わりまでの秒）
        public const string Disconnect = "disconnect";
        // ドリフトのデータ（label = start / end / track、value = 照準の画面 X ÷ 画面のはば、flag = 1 止まっているのを確認した）
        public const string DriftSample = "drift_sample";
        // 区間の集計の値（label = 指標の名前、value = 値）
        public const string Metric = "metric";
    }

    // UsbGateEventType.Metric の label
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
        // 区間が始まったときに実機（ESP32）とつながっていたか（1 / 0）
        public const string Connected = "connected";
        // 5つ目（millis）付きで受け取った行の数。0 なら入力時刻は測れていない
        public const string DeviceTimeSamples = "device_time_samples";
    }

    /*
        T6-USB の記録の1行（#52）

        区間（安全チェック・わざと100投・ドリフト・30人の負荷・子ども）をまたいで、1つのたて長の CSV に1イベント1行で残す
        数字の空欄はデータなし（null）で、0 とは分ける。MonoBehaviour は使っていない
    */
    public sealed class UsbGateEvent
    {
        public string TestId = UsbGatePlan.DefaultTestId;
        // やった日（yyyy-MM-dd）
        public string Date = "";
        // 区間を始めるたびに1つ進む通し番号。同じ区間をやりなおしたときは、最後に最後までやった回を使う
        public int Run;
        // UsbGatePlan.KeyOf の値
        public string Section = "";
        // 子どもの参加者の番号（1から）。ほかの区間は 0。名前は書かない
        public int Participant;
        public int Seq;
        public string Event = "";
        // 区間が始まってからの秒
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
