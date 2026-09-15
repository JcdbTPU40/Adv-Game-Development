using UnityEngine;

namespace Toufuku.Playtest
{
    /*
        フレームが始まった時刻を取るクラス（#52。T6-USB の「画面に弾が出た」時刻）

        Unity は前のフレームの絵を present してから（垂直同期ならその待ちも入れて）次のフレームの Update に入る
        なので、いちばん早い順番でこの時刻を取って、「弾をはじめて描いたフレームの次のフレームの始まり」を
        弾が画面に出た時刻（present が終わった）のいちばん遅い場合として使う。モニターそのものの遅れ（数 ms〜）は入らない
        ConecteController（-200）より先に動くので、受け取ったデータを読む時間もまざらない
    */
    [DefaultExecutionOrder(-1000)]
    public class UsbFrameClock : MonoBehaviour
    {
        // 今のフレームの始まり（Time.realtimeSinceStartupAsDouble）
        public double FrameStart { get; private set; } = double.NaN;
        // FrameStart を取ったフレームの番号
        public int Frame { get; private set; } = -1;
        // 前のフレームの始まりから今のフレームの始まりまで（秒）。最初のフレームは NaN
        public double LastFrameSeconds { get; private set; } = double.NaN;

        void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            LastFrameSeconds = double.IsNaN(FrameStart) ? double.NaN : now - FrameStart;
            FrameStart = now;
            Frame = Time.frameCount;
        }
    }
}
