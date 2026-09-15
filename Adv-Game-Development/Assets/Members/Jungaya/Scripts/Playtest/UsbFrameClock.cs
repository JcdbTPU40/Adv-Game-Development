using UnityEngine;

namespace Toufuku.Playtest
{
    /// <summary>
    /// フレームの始まりの時刻を取る — Issue #52（T6-USB の「画面で弾が出る」時刻）
    ///
    /// Unity は前のフレームの描画を present してから（垂直同期ならその待ちも含めて）次のフレームの Update に入る。
    /// そこでいちばん早い実行順でこの時刻を取り、「弾を初めて描いたフレームの次のフレームの始まり」を
    /// <b>弾が画面に出た時刻（present の完了）の上限</b>として使う。表示機そのものの遅れ（数 ms〜）は含まない。
    /// ConecteController（-200）より先に動くので、受信の読み込み時間も混ざらない。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class UsbFrameClock : MonoBehaviour
    {
        /// <summary>今のフレームの始まり（Time.realtimeSinceStartupAsDouble）。</summary>
        public double FrameStart { get; private set; } = double.NaN;
        /// <summary><see cref="FrameStart"/> を取ったフレーム番号。</summary>
        public int Frame { get; private set; } = -1;
        /// <summary>前のフレームの始まりから今のフレームの始まりまで（秒）。最初のフレームは NaN。</summary>
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
