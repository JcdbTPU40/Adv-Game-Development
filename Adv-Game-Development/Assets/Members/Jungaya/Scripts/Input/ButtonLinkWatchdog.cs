namespace Toufuku.GameInput
{
    /*
        ボタン箱の受信の見張り（#65 / 企画書 v8 3章「ESP32 ×2 の役割分担」）

        ボタン箱は、押した・はなしたが変わったときと 30Hz のハートビートで、押下マスクを送ってくる
        Unity は 100ms 受け取らなかったら、ぜんぶのボタンをはなしたことにする（押している入力とためをキャンセルする）

        ・一度も受け取っていない間は見張らない（ボタンを送らないファームウェアや、キーボードの代用キーのため）
        ・とぎれたとみなす時刻は「最後に受け取った時刻 + 100ms」（LostTime）。はなした時刻にこれを使えば、
          フレームがおそくても長押し・ための秒数が 100ms より延びない
        ・また受け取ったら、その押下マスクにもどる
        MonoBehaviour は使っていない
    */
    public sealed class ButtonLinkWatchdog
    {
        public const double DefaultTimeoutSeconds = 0.1;

        // 1e-9 秒くらいのズレは許す（0.1 を double で足したときの誤差）
        const double TimeEpsilon = 1e-9;

        // これだけ受け取らなかったら、ぜんぶはなしたことにする
        public double TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

        // 一度でも受け取ったか
        public bool HasReceived { get; private set; }
        // 最後に受け取った時刻（Unity で受け取った時刻。秒）
        public double LastReceiveTime { get; private set; } = double.NaN;
        // 最後に受け取った押下マスク（bit0〜4 が色、bit5 が正面）
        public int LastMask { get; private set; }

        // とぎれたとみなす時刻。受け取っていなければ NaN
        public double LostTime => HasReceived ? LastReceiveTime + TimeoutSeconds : double.NaN;

        public void Receive(double time, int mask)
        {
            if (!HasReceived || time >= LastReceiveTime) LastReceiveTime = time;
            HasReceived = true;
            LastMask = mask;
        }

        // now の時点でとぎれているか
        public bool IsLost(double now)
        {
            return HasReceived && now - LastReceiveTime >= TimeoutSeconds - TimeEpsilon;
        }

        // now の時点で押していることにするマスク。とぎれていたら 0（ぜんぶはなした）
        public int EffectiveMask(double now)
        {
            return IsLost(now) ? 0 : LastMask;
        }

        public void Reset()
        {
            HasReceived = false;
            LastReceiveTime = double.NaN;
            LastMask = 0;
        }
    }
}
