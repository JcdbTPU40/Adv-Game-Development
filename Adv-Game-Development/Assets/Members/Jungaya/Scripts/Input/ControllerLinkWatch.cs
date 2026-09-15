namespace Toufuku.GameInput
{
    public enum ControllerLinkEvent
    {
        None,
        Lost,      // 受け取りがとぎれた
        Recovered  // また続けて受け取れるようになった
    }

    /*
        コントローラー（大幣）の通信のとぎれと復帰の見張り（#65 / 企画書 v8 3章「接続」、17章 T7「切断0／60秒以内で交換・再開」）

        ・受け取りの間かくが LostGapSeconds（0.5秒。#52 の「切断」と同じ）以上あいたら「とぎれた」
        ・とぎれたあと、StableSeconds（0.3秒）のあいだ続けて受け取れたら「復帰した」
          （USB を挿したときに1行だけ来てまた止まる、を復帰にしないため）
        ・とぎれていた秒（LastDownSeconds）は、とぎれる前に最後に受け取った時刻から、復帰を決めた時刻まで（T7 の「復帰秒」）
        ・一度も受け取っていない間は、とぎれたことにしない（開発用のキーボード操作のとき）
        MonoBehaviour は使っていない
    */
    public sealed class ControllerLinkWatch
    {
        public const double DefaultLostGapSeconds = 0.5;
        public const double DefaultStableSeconds = 0.3;

        const double TimeEpsilon = 1e-9;

        public double LostGapSeconds { get; set; } = DefaultLostGapSeconds;
        public double StableSeconds { get; set; } = DefaultStableSeconds;

        public bool HasReceived { get; private set; }
        // 今とぎれているか（復帰を決めるまで true）
        public bool IsLost { get; private set; }
        public double LastReceiveTime { get; private set; } = double.NaN;
        // 今のとぎれの前に、最後に受け取った時刻。とぎれていなければ NaN
        public double LostSince { get; private set; } = double.NaN;
        // いちばん新しい復帰で、とぎれていた秒
        public double LastDownSeconds { get; private set; } = double.NaN;
        // とぎれた回数
        public int LossCount { get; private set; }

        // とぎれたあと、また続けて受け取り始めた時刻
        double _resumeStart = double.NaN;

        // 1行受け取った
        public void Receive(double time)
        {
            if (HasReceived && time < LastReceiveTime) return;

            // とぎれている間に来た最初の行（か、来たあとにまた間があいた行）から、続けて受け取れているかを数え始める
            if (IsLost && (double.IsNaN(_resumeStart) || time - LastReceiveTime >= LostGapSeconds - TimeEpsilon))
                _resumeStart = time;

            LastReceiveTime = time;
            HasReceived = true;
        }

        // 毎フレーム呼ぶ。とぎれた・復帰したが決まった瞬間だけ、そのイベントを返す
        public ControllerLinkEvent Poll(double now)
        {
            if (!HasReceived) return ControllerLinkEvent.None;

            if (!IsLost)
            {
                if (now - LastReceiveTime < LostGapSeconds - TimeEpsilon) return ControllerLinkEvent.None;
                IsLost = true;
                LostSince = LastReceiveTime;
                _resumeStart = double.NaN;
                LossCount++;
                return ControllerLinkEvent.Lost;
            }

            if (double.IsNaN(_resumeStart)) return ControllerLinkEvent.None;

            // また来なくなった。次に来た行から数えなおす
            if (now - LastReceiveTime >= LostGapSeconds - TimeEpsilon)
            {
                _resumeStart = double.NaN;
                return ControllerLinkEvent.None;
            }

            if (now - _resumeStart < StableSeconds - TimeEpsilon) return ControllerLinkEvent.None;

            IsLost = false;
            LastDownSeconds = now - LostSince;
            LostSince = double.NaN;
            _resumeStart = double.NaN;
            return ControllerLinkEvent.Recovered;
        }

        public void Reset()
        {
            HasReceived = false;
            IsLost = false;
            LastReceiveTime = double.NaN;
            LostSince = double.NaN;
            LastDownSeconds = double.NaN;
            LossCount = 0;
            _resumeStart = double.NaN;
        }
    }
}
