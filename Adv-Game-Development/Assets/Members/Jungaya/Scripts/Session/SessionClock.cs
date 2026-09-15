using System;

/*
    セッションの時計（#65 / 企画書 v8 7章「3:00境界の処理順」、11章「ランクC停滞タイマー」）

    3:00 の境界・危険度 D・スポーンの時間割・福の連なりの5秒・C停滞タイマーは、ぜんぶこの時計の秒で決める
    ・進めるのは Unity の単調増加時計（Time.realtimeSinceStartupAsDouble）の差だけ。フレームの deltaTime や timeScale は使わない
    ・止める理由（ポーズ・通信の復帰中）が1つでもあれば進まない。理由はビットで持って、ぜんぶ外れたら、外れた時刻から続きを進める
    ・1回で MaxStepSeconds（0.5秒）より大きくあいたら、こえたぶんは「止まっていた」とみなして足さない
      （ロードやポートを開くときの引っかかりで、D が一気に進んで黒客になったりしないように。足さなかった秒は StalledSeconds）
    ・イベントの時刻（振りを受け取った時刻など）を、この時計の秒に直せる（ElapsedAt）。3:00 の判定はフレームではなくこれで決める
    MonoBehaviour は使っていない（EditMode テストで時刻を好きに進められるように）
*/
public sealed class SessionClock
{
    public const double DefaultMaxStepSeconds = 0.5;

    // 1回で進めてよい最大の秒
    public double MaxStepSeconds { get; set; } = DefaultMaxStepSeconds;

    // Start を呼んだか
    public bool IsStarted { get; private set; }
    // 始めてから進んだ秒（止めていた時間と、引っかかりでこえたぶんは入らない）
    public double Elapsed { get; private set; }
    // 引っかかりとみなして足さなかった秒の合計
    public double StalledSeconds { get; private set; }
    // 止めている理由のビット。0 なら進む
    public int HoldMask { get; private set; }
    public bool IsHeld => HoldMask != 0;

    // 最後に進めた（または再開した）ときの実時間
    double _lastRealtime;
    // これより前の時刻のイベントは、この秒より小さくしない（再開や引っかかりの前後を、まっすぐな線でつながないため）
    double _floor;

    // 0 から始める。止めている理由はそのまま残す（通信の復帰中にリトライしたときなど）
    public void Start(double now)
    {
        IsStarted = true;
        Elapsed = 0.0;
        StalledSeconds = 0.0;
        _lastRealtime = now;
        _floor = 0.0;
    }

    // 実時間 now まで進める。返す値: 進んだ秒
    public double Advance(double now)
    {
        if (!IsStarted) return 0.0;

        double d = now - _lastRealtime;
        // 時刻が前にもどったり同じだったりしたら何もしない（単調増加の時計なので、ふつうは起きない）
        if (d <= 0.0) return 0.0;
        _lastRealtime = now;
        if (IsHeld) return 0.0;

        if (d > MaxStepSeconds)
        {
            StalledSeconds += d - MaxStepSeconds;
            Elapsed += MaxStepSeconds;
            _floor = Elapsed;
            return MaxStepSeconds;
        }

        Elapsed += d;
        return d;
    }

    // 理由 reasonBit で止める。止めた瞬間までの時間は足してから止める。返す値: 新しく止めたら true
    public bool Hold(int reasonBit, double now)
    {
        if (reasonBit == 0 || (HoldMask & reasonBit) == reasonBit) return false;
        if (!IsHeld) Advance(now);
        HoldMask |= reasonBit;
        return true;
    }

    // 理由 reasonBit を外す。ぜんぶ外れたら now から続きを進める。返す値: 外したら true
    public bool Release(int reasonBit, double now)
    {
        if ((HoldMask & reasonBit) == 0) return false;
        HoldMask &= ~reasonBit;
        if (!IsHeld)
        {
            _lastRealtime = Math.Max(_lastRealtime, now);
            _floor = Elapsed;
        }
        return true;
    }

    /*
        実時間 realtime に起きたイベントを、この時計の秒に直す
        ・最後に進めた時刻よりあとなら、そこから進めたときの秒（MaxStepSeconds までで止める。Advance と同じ決まり）
        ・前なら、さかのぼった秒。ただし再開・引っかかりより前にはさかのぼらない
        ・止めている間はずっと止めた秒
    */
    public double ElapsedAt(double realtime)
    {
        if (!IsStarted) return 0.0;
        if (IsHeld) return Elapsed;
        if (realtime >= _lastRealtime) return Elapsed + Math.Min(realtime - _lastRealtime, MaxStepSeconds);
        return Math.Max(_floor, Elapsed - (_lastRealtime - realtime));
    }
}
