using System.Collections.Generic;
using System.Globalization;

/*
    3:00境界ログ（#65 / 企画書 v8 19章「3:00境界ログ」）

    1プレイごとに GameSession が集めて、スコアを固定したときに Console と計測ログ（#63 の CSV の #meta 行 boundary.*）に出す
    ・最後に受理した振りの検出時刻
    ・境界時の飛翔中弾数（3:00 より前に受理して、まだ落ちていない弾）
    ・3:00 以前の投擲による決着時刻（解決中に落ちた最後の弾）
    ・決着前後の縁・救済数（3:00 の瞬間と、スコアを固定した瞬間）
    ・3:00 以後に拒否した入力数（振り。固定するまで）
    時刻はすべて GameSession の時計の秒（SessionClock）
*/
public sealed class SessionBoundaryReport
{
    // 最後に受理した振りの検出時刻。受理がなければ NaN
    public double LastAcceptedSwingSeconds { get; set; } = double.NaN;
    // 3:00 を見つけたフレームの時刻（180.000 との差がフレームの粗さ。判定そのものはイベントの時刻でしている）
    public double BoundaryDetectedSeconds { get; set; } = double.NaN;
    // 境界時の飛翔中弾数
    public int InFlightAtBoundary { get; set; }
    // 3:00 以前の投擲の、最後の決着時刻。3:00 のあとに落ちた弾がなければ NaN
    public double LastSettleSeconds { get; set; } = double.NaN;
    // スコアを固定した時刻
    public double LockSeconds { get; set; } = double.NaN;
    public int EnAtBoundary { get; set; }
    public int EnAtLock { get; set; }
    public int RescuesAtBoundary { get; set; }
    public int RescuesAtLock { get; set; }
    // 3:00 以後に拒否した振りの数（スコアを固定するまで）
    public int RejectedAfterBoundary { get; set; }

    // 3:00 を通って、スコアの固定まで終わったか
    public bool IsComplete => !double.IsNaN(BoundaryDetectedSeconds) && !double.IsNaN(LockSeconds);

    // CSV のキーと値（#63 の #meta 行。キーの前に boundary. を付けて使う）
    public IEnumerable<KeyValuePair<string, string>> ToHeaderValues()
    {
        yield return Pair("last_accepted_swing_sec", F(LastAcceptedSwingSeconds));
        yield return Pair("detected_sec", F(BoundaryDetectedSeconds));
        yield return Pair("in_flight_at_boundary", I(InFlightAtBoundary));
        yield return Pair("last_settle_sec", F(LastSettleSeconds));
        yield return Pair("lock_sec", F(LockSeconds));
        yield return Pair("en_at_boundary", I(EnAtBoundary));
        yield return Pair("en_at_lock", I(EnAtLock));
        yield return Pair("rescues_at_boundary", I(RescuesAtBoundary));
        yield return Pair("rescues_at_lock", I(RescuesAtLock));
        yield return Pair("rejected_after_boundary", I(RejectedAfterBoundary));
    }

    public override string ToString()
    {
        return $"最後に受理した振り {F(LastAcceptedSwingSeconds)}秒 / 境界の検出 {F(BoundaryDetectedSeconds)}秒 / 飛翔中 {InFlightAtBoundary}発" +
               $" / 最後の決着 {F(LastSettleSeconds)}秒 / 固定 {F(LockSeconds)}秒" +
               $" / 縁 {EnAtBoundary}→{EnAtLock} / 救済 {RescuesAtBoundary}→{RescuesAtLock} / 3:00以後に拒否した振り {RejectedAfterBoundary}";
    }

    static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);

    static string F(double v) => double.IsNaN(v) ? "" : v.ToString("0.000", CultureInfo.InvariantCulture);

    static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
}
