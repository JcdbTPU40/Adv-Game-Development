using System;

namespace Toufuku.Playtest
{
    /*
        受け取りのとぎれ（切断）を数えるクラス（#52。T6-USB の「切断0」）

        ・受け取りの間かくが DisconnectGapSeconds 以上あいたら切断1回。とぎれている間は1回だけ数える
        ・行が来ないまま時間がすぎることもあるので、毎フレーム Poll でも確かめる（もどってこない切断も数えるため）
        ・区間が始まったときにつながっていたら、始まった時刻を「その前に受け取った」とする（最初から来ないときも切断に数える）
        ConecteController はエラーをにぎりつぶして接続のフラグをもどさないので、フラグじゃなくて受け取りの間かくで見る
        MonoBehaviour は使っていない
    */
    public sealed class UsbLinkMonitor
    {
        public double DisconnectGapSeconds { get; set; } = UsbGatePlan.DisconnectGapSeconds;

        public int Samples { get; private set; }
        public int Disconnects { get; private set; }
        public double MaxGapSeconds { get; private set; }
        public double BeginTime { get; private set; } = double.NaN;
        public double EndTime { get; private set; } = double.NaN;

        double _last = double.NaN;
        bool _inGap;
        double _gapStart;

        // 今のとぎれが始まった時刻（とぎれていなければ NaN）
        public double GapStart => _inGap ? _gapStart : double.NaN;

        // 区間を始める。expectSamples = 実機とつながっているか
        public void Begin(double now, bool expectSamples)
        {
            Samples = 0;
            Disconnects = 0;
            MaxGapSeconds = 0.0;
            BeginTime = now;
            EndTime = double.NaN;
            _last = expectSamples ? now : double.NaN;
            _inGap = false;
        }

        // 受け取った1行ぶん。この行でとぎれが終わったら、そのとぎれの秒を gap に入れて true を返す
        public bool AddSample(double receiveTime, out double gap)
        {
            gap = 0.0;
            bool closed = false;
            if (!double.IsNaN(_last))
            {
                double d = receiveTime - _last;
                if (d > MaxGapSeconds) MaxGapSeconds = d;
                if (d >= DisconnectGapSeconds - 1e-9)
                {
                    if (!_inGap) Disconnects++;
                    gap = d;
                    closed = true;
                }
            }
            _inGap = false;
            if (double.IsNaN(_last) || receiveTime >= _last) _last = receiveTime;
            Samples++;
            return closed;
        }

        // 毎フレーム呼ぶ。今この時点でとぎれが始まったとわかったら true（1回のとぎれで1回だけ）
        public bool Poll(double now)
        {
            if (double.IsNaN(_last) || _inGap) return false;
            if (now - _last < DisconnectGapSeconds - 1e-9) return false;
            _inGap = true;
            _gapStart = _last;
            Disconnects++;
            return true;
        }

        // 区間を終わる。とぎれたまま終わったときも、いちばん長い間かくに入れる
        public void End(double now)
        {
            EndTime = now;
            if (!double.IsNaN(_last)) MaxGapSeconds = Math.Max(MaxGapSeconds, now - _last);
        }

        // 受け取る回数（行/秒）
        public double? SampleRateHz
        {
            get
            {
                double end = double.IsNaN(EndTime) ? double.NaN : EndTime;
                if (double.IsNaN(BeginTime) || double.IsNaN(end) || end - BeginTime <= 0.0) return null;
                return Samples / (end - BeginTime);
            }
        }
    }
}
