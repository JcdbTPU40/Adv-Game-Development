using System;

namespace Toufuku.Playtest
{
    // T6-USB で測る区間。値は CSV の section 列（文字）に書くので、ならべかえてもいい
    public enum UsbGateSection
    {
        Safety,
        Throws100,
        DriftStatic,
        DriftOperate,
        Load,
        Children
    }

    // 始める前の安全チェックの1項目
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

    /*
        T6-USB の「絶対に通さないといけない技術のチェック」の数値と手順の定数（#52 / 企画書 v8 17章・3章・12章・付録B）

        完了条件と値:
        ・安全チェック: ぜんぶの項目が OK、ぶつかった・はみ出たが0件
        ・入力の遅れ（SwingAccepted から画面に弾が出るまで）: p95 が 80ms 以下、最大が 100ms 以下（付録B LATENCY.USB）
        ・わざと入力: 抜けが 2% 未満、まちがい発射が 2% 以下（わざと100投）
        ・接続: 切断0
        ・ヨー角のドリフト: 3分で画面のはばの 5% 以下
        ・描画（黒客と帰っている途中の客も入れて30人）: 60fps、1% low が 55fps 以上
        ・子ども5人（2分練習したあと）: 近いのは 7/10、遠いのは 6/10 当たる

        USB がだめなら MVP を決めない。BLE（T6-BLE）は別の、やってもやらなくてもいいチェック
        MonoBehaviour は使っていない
    */
    public static class UsbGatePlan
    {
        public const string DefaultTestId = "T6-USB-1";

        // ---- 入力の遅れ ----
        public const double LatencyP95LimitMs = 80.0;
        public const double LatencyMaxLimitMs = 100.0;

        // ---- わざと100投 ----
        public const int IntendedThrows = 100;
        // 抜けのわりあいはこの値より小さければ合格（ちょうど 2% は不合格）
        public const double MissRateLimit = 0.02;
        // まちがい発射のわりあいはこの値以下なら合格（ちょうど 2% は合格）
        public const double FalseFireRateLimit = 0.02;
        // 合図の間かく（秒）。クールダウン（いちばん長くて 0.65 秒）と飛ぶ時間（いちばん長くて 0.65 秒）より十分長くする
        public const float CueIntervalSeconds = 2.0f;
        // 最初の合図までの秒
        public const float CueLeadInSeconds = 3.0f;
        // 合図より前でもこの秒数以内の発射なら、その合図に反応したとする（合図を予想して振る人がいるから）
        public const float CueWindowBeforeSeconds = 0.3f;
        // 合図からこの秒数以内の発射を、その合図に反応したとする
        public const float CueWindowAfterSeconds = 1.2f;

        /*
            ---- 接続 ----
            受け取りがこの秒数とぎれたら「切断」1回と数える（ボタン箱の 100ms 受け取れなかったら放すのより長い、はっきりしたとぎれ）
        */
        public const float DisconnectGapSeconds = 0.5f;
        // 入力時刻（コントローラーの時計）を Unity の時計に合わせるとき、前後この秒数に受け取ったデータから時計の差を出す
        public const double ClockWindowSeconds = 5.0;

        // ---- ドリフト ----
        public const float DriftSeconds = 180f;
        public const double DriftLimitScreenRatio = 0.05;
        // 止まっているかの判定（3章のキャリブレーションを受け付ける条件と同じ）: 角速度がこの値より小さい状態が…
        public const float StillAngularSpeed = 30f;
        // …この秒数つづいたら止まっているとする
        public const float StillSeconds = 0.5f;
        // 止まるのを待ついちばん長い時間。こえたら取りなおしてもらう
        public const float StillTimeoutSeconds = 5f;
        // 止めたままのドリフトの間に、とちゅうの様子を記録する間かく（秒）
        public const float DriftTrackIntervalSeconds = 10f;

        // ---- 描画の負荷 ----
        public const int LoadBodies = 30;
        public const double TargetFps = 60.0;
        // 垂直同期 59.94Hz のモニターや計測の丸めで 60.0 に届かないぶんをゆるす。平均 59.0fps 以上なら「60fps」とする
        public const double TargetFpsTolerance = 1.0;
        public const double OnePercentLowLimitFps = 55.0;
        public const float LoadWarmupSeconds = 5f;
        public const float LoadMeasureSeconds = 60f;
        // 負荷を測っている間の、自動で投げる間かく（T0-3M で思っている実際の間かく 1.0〜1.4 秒の速いほう）
        public const float LoadAutoThrowSeconds = 1.0f;

        // ---- 子ども ----
        public const int ChildParticipants = 5;
        public const float ChildPracticeSeconds = 120f;
        public const int ThrowsPerRange = 10;
        public const double NearHitRatio = 0.7;
        public const double FarHitRatio = 0.6;
        // 的の判定半径（T0 のふつうの的と同じ 0.90m）
        public const float TargetRadius = 0.90f;

        // 時間切れや決まった数に届いたあと、飛んでいる弾（いちばん長くて 0.65 秒）が落ちるのを待つ秒
        public const float SettleSeconds = 1.0f;

        /*
            始める前の安全チェック（17章 T6-USB・12章「安全領域と配線」）。ぜんぶ OK じゃなければ測り始めない
            id は CSV に残るので変えないこと（文章は変えてもいい）
        */
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

        // CSV の section 列の値
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

        // 受け取り・発射を測る区間かどうか（切断と遅れを見る）。安全チェックは入力を使わない
        public static bool UsesController(UsbGateSection section) => section != UsbGateSection.Safety;
    }
}
