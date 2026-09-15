using System;

namespace Toufuku.Playtest
{
    /// <summary>
    /// 受信の途絶（切断）を数える — Issue #52（T6-USB「切断 0」）
    ///
    /// ・受信の間隔が <see cref="DisconnectGapSeconds"/> 以上空いたら切断 1 回。途絶が続いている間は 1 回だけ数える。
    /// ・行が来ないまま時間が過ぎる場合に備えて、毎フレーム <see cref="Poll"/> でも確かめる（戻ってこない切断も数えるため）。
    /// ・区間の開始時につながっていれば、開始時刻を「直前の受信」とみなす（最初から来ない場合も切断に数える）。
    /// ConecteController は例外を握りつぶして接続フラグを戻さないので、フラグではなく受信の間隔で見る。
    /// MonoBehaviour 非依存。
    /// </summary>
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

        /// <summary>今の途絶の始まり（途絶中でなければ NaN）。</summary>
        public double GapStart => _inGap ? _gapStart : double.NaN;

        /// <summary>区間を始める。expectSamples = 実機とつながっているか。</summary>
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

        /// <summary>受信 1 行ぶん。この行で途絶が終わったら、その途絶の秒を gap に返して true。</summary>
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

        /// <summary>毎フレーム呼ぶ。今この時点で途絶が始まったと分かったら true（1 回の途絶で 1 回だけ）。</summary>
        public bool Poll(double now)
        {
            if (double.IsNaN(_last) || _inGap) return false;
            if (now - _last < DisconnectGapSeconds - 1e-9) return false;
            _inGap = true;
            _gapStart = _last;
            Disconnects++;
            return true;
        }

        /// <summary>区間を終える。途絶したまま終わった場合も最大間隔に入れる。</summary>
        public void End(double now)
        {
            EndTime = now;
            if (!double.IsNaN(_last)) MaxGapSeconds = Math.Max(MaxGapSeconds, now - _last);
        }

        /// <summary>受信頻度（行/秒）。</summary>
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
