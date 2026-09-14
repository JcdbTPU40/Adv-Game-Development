using UnityEngine;

namespace Toufuku.Feedback
{
    /// <summary>
    /// 終了カウントダウンの「いつ鳴らすか」— Issue #64
    ///
    /// 残り秒数を毎フレーム渡すと、残りが N 秒以下になった最初のフレームで N を返す（N = From〜1）。
    /// フレームが飛んで複数の数字をまたいでも、鳴らすのは最新の 1 つだけ（音を詰めて鳴らさない）。
    /// </summary>
    public sealed class CountdownTicker
    {
        /// <summary>何秒前から鳴らすか。</summary>
        public int From = 10;

        int _last = int.MaxValue;

        /// <summary>セッション開始時に呼ぶ。</summary>
        public void Reset()
        {
            _last = int.MaxValue;
        }

        /// <summary>今回鳴らす数字（From〜1）。鳴らさないフレームは 0。</summary>
        public int Advance(float remainingSeconds)
        {
            if (!(remainingSeconds > 0f)) return 0;

            int sec = Mathf.CeilToInt(remainingSeconds);
            if (sec > From || sec >= _last) return 0;

            _last = sec;
            return sec;
        }
    }
}
