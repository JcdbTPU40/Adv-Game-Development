using System;

namespace Toufuku.Playtest
{
    /// <summary>T6-USB の計測区間。値は CSV の section 列（文字列）に書くので並べ替えてよい。</summary>
    public enum UsbGateSection
    {
        Safety,
        Throws100,
        DriftStatic,
        DriftOperate,
        Load,
        Children
    }

    /// <summary>開始前の安全チェック 1 項目。</summary>
    public readonly struct UsbSafetyItem
    {
        public readonly string Id;
        public readonly string Text;

        public UsbSafetyItem(string id, string text)
        {
            Id = id;
            Text = text;
        }
    }

    /// <summary>
    /// T6-USB 必須技術ゲートの数値と手順の定数 — Issue #52（仕様書 v8 17章・3章・12章・付録B）
    ///
    /// | 完了条件 | 値 |
    /// |---|---|
    /// | 安全チェック | 全項目適合・接触／逸脱 0 件 |
    /// | 入力遅延（SwingAccepted → 画面で弾が出る） | p95 ≤ 80ms・最大 ≤ 100ms（付録B LATENCY.USB） |
    /// | 意図的入力 | 欠落 &lt; 2%・誤発射 ≤ 2%（意図的 100 投） |
    /// | 接続 | 切断 0 |
    /// | ヨー角ドリフト | 3 分で画面幅の 5% 以下 |
    /// | 描画（黒客・退場者込み 30 体） | 60fps・1% low ≥ 55fps |
    /// | 子ども 5 人（2 分練習後） | 近 7/10・遠 6/10 命中 |
    ///
    /// USB が赤なら MVP を固定しない。BLE（T6-BLE）は別の任意ゲート。
    /// MonoBehaviour 非依存。
    /// </summary>
    public static class UsbGatePlan
    {
        public const string DefaultTestId = "T6-USB-1";

        // ── 入力遅延 ──
        public const double LatencyP95LimitMs = 80.0;
        public const double LatencyMaxLimitMs = 100.0;

        // ── 意図的 100 投 ──
        public const int IntendedThrows = 100;
        /// <summary>欠落率はこの値<b>未満</b>で合格（2% ちょうどは不合格）。</summary>
        public const double MissRateLimit = 0.02;
        /// <summary>誤発射率はこの値<b>以下</b>で合格（2% ちょうどは合格）。</summary>
        public const double FalseFireRateLimit = 0.02;
        /// <summary>合図の間隔（秒）。クールダウン（最長 0.65 秒）と飛翔（最長 0.65 秒）より十分長くする。</summary>
        public const float CueIntervalSeconds = 2.0f;
        /// <summary>最初の合図までの秒。</summary>
        public const float CueLeadInSeconds = 3.0f;
        /// <summary>合図より前にこの秒数以内の発射も、その合図への応答とみなす（合図を予測して振る人がいるため）。</summary>
        public const float CueWindowBeforeSeconds = 0.3f;
        /// <summary>合図からこの秒数以内の発射を、その合図への応答とみなす。</summary>
        public const float CueWindowAfterSeconds = 1.2f;

        // ── 接続 ──
        /// <summary>受信がこの秒数途切れたら「切断」1 回と数える（ボタン箱の 100ms 無受信解放より長い、明らかな途絶）。</summary>
        public const float DisconnectGapSeconds = 0.5f;
        /// <summary>入力時刻（コントローラの時計）を Unity の時計へ合わせるとき、前後この秒数の受信から時計差を推定する。</summary>
        public const double ClockWindowSeconds = 5.0;

        // ── ドリフト ──
        public const float DriftSeconds = 180f;
        public const double DriftLimitScreenRatio = 0.05;
        /// <summary>静止判定（3章 キャリブレーションの受理条件と同じ）: 角速度がこの値未満で…</summary>
        public const float StillAngularSpeed = 30f;
        /// <summary>…この秒数続いたら静止。</summary>
        public const float StillSeconds = 0.5f;
        /// <summary>静止を待つ上限。超えたら取り直しを促す。</summary>
        public const float StillTimeoutSeconds = 5f;
        /// <summary>静止ドリフト中に経過を記録する間隔（秒）。</summary>
        public const float DriftTrackIntervalSeconds = 10f;

        // ── 描画負荷 ──
        public const int LoadBodies = 30;
        public const double TargetFps = 60.0;
        /// <summary>垂直同期 59.94Hz の表示機や計測の丸めで 60.0 に届かないぶんの許容。平均 59.0fps 以上で「60fps」とみなす。</summary>
        public const double TargetFpsTolerance = 1.0;
        public const double OnePercentLowLimitFps = 55.0;
        public const float LoadWarmupSeconds = 5f;
        public const float LoadMeasureSeconds = 60f;
        /// <summary>負荷計測中の自動投擲の間隔（T0-3M の想定実操作周期 1.0〜1.4 秒の速い側）。</summary>
        public const float LoadAutoThrowSeconds = 1.0f;

        // ── 子ども ──
        public const int ChildParticipants = 5;
        public const float ChildPracticeSeconds = 120f;
        public const int ThrowsPerRange = 10;
        public const double NearHitRatio = 0.7;
        public const double FarHitRatio = 0.6;
        /// <summary>的の判定半径（T0 の標準ターゲットと同じ 0.90m）。</summary>
        public const float TargetRadius = 0.90f;

        /// <summary>時間切れ・規定数到達のあと、飛翔中の弾（最長 0.65 秒）の着弾を待つ秒。</summary>
        public const float SettleSeconds = 1.0f;

        /// <summary>
        /// 開始前の安全チェック（17章 T6-USB・12章「安全領域と配線」）。全項目適合でなければ計測を始めない。
        /// id は CSV に残るので変えないこと（文言は変えてよい）。
        /// </summary>
        public static readonly UsbSafetyItem[] SafetyItems =
        {
            new UsbSafetyItem("area_1_5m", "前後左右 1.5m の振り抜き安全領域をコーン／ベルトで囲った（大幣の全長＋腕の長さで、紙垂を含む先端が境界に届かない）"),
            new UsbSafetyItem("audience_2m", "観戦列・アテンド待機位置が後方 2m 以上、かつ安全領域の外"),
            new UsbSafetyItem("floor_mat", "立ち位置の床マットが固定され、めくれ・滑りがない"),
            new UsbSafetyItem("usb_route", "USB 4 本の経路: ボタン箱・大幣からプレイヤーの後方へ逃がしている"),
            new UsbSafetyItem("usb_strain", "USB: 台の脚へ 2 点でストレインリリーフしている"),
            new UsbSafetyItem("usb_cover", "USB: 床を横切る部分はケーブルカバーで固定し、振り抜き領域と通路を横断しない"),
            new UsbSafetyItem("usb_port", "USB 端子: 抜けかけ・ぐらつきがない（PC 側・機器側とも）"),
            new UsbSafetyItem("strap", "ストラップ: 装着できる・摩耗や裂けがない・留め具が外れない"),
            new UsbSafetyItem("joint", "接合部: 柄・先端・紙垂の固定にガタ・ひび・緩みがない")
        };

        const double Epsilon = 1e-9;

        public static bool LatencyWithinLimit(double p95Ms, double maxMs) =>
            p95Ms <= LatencyP95LimitMs + Epsilon && maxMs <= LatencyMaxLimitMs + Epsilon;

        public static bool MissRateOk(double rate) => rate < MissRateLimit - Epsilon;

        public static bool FalseFireRateOk(double rate) => rate <= FalseFireRateLimit + Epsilon;

        public static bool DriftOk(double screenRatio) => Math.Abs(screenRatio) <= DriftLimitScreenRatio + Epsilon;

        public static bool AverageFpsOk(double fps) => fps >= TargetFps - TargetFpsTolerance - Epsilon;

        public static bool OnePercentLowOk(double fps) => fps >= OnePercentLowLimitFps - Epsilon;

        public static bool HitRatioOk(int hits, int throws, double ratio) =>
            throws > 0 && hits + Epsilon >= throws * ratio;

        /// <summary>CSV の section 列の値。</summary>
        public static string KeyOf(UsbGateSection section)
        {
            switch (section)
            {
                case UsbGateSection.Safety: return "safety";
                case UsbGateSection.Throws100: return "throws100";
                case UsbGateSection.DriftStatic: return "drift_static";
                case UsbGateSection.DriftOperate: return "drift_operate";
                case UsbGateSection.Load: return "load30";
                default: return "children";
            }
        }

        public static bool TryParseSection(string key, out UsbGateSection section)
        {
            foreach (UsbGateSection s in (UsbGateSection[])Enum.GetValues(typeof(UsbGateSection)))
            {
                if (KeyOf(s) != key) continue;
                section = s;
                return true;
            }
            section = UsbGateSection.Safety;
            return false;
        }

        public static string LabelOf(UsbGateSection section)
        {
            switch (section)
            {
                case UsbGateSection.Safety: return "安全チェック";
                case UsbGateSection.Throws100: return "意図的 100 投";
                case UsbGateSection.DriftStatic: return "3 分静止ドリフト";
                case UsbGateSection.DriftOperate: return "3 分操作ドリフト";
                case UsbGateSection.Load: return "30 体負荷";
                default: return "子ども 近・遠";
            }
        }

        /// <summary>受信・発射を測る区間か（切断・遅延の対象）。安全チェックは入力を使わない。</summary>
        public static bool UsesController(UsbGateSection section) => section != UsbGateSection.Safety;
    }
}
