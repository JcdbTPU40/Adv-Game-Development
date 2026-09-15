using UnityEngine;

namespace Toufuku.Feedback
{
    /*
        終わりのカウントダウンを「いつ鳴らすか」決めるクラス（#64）

        残り秒数を毎フレーム渡すと、残りが N 秒以下になった最初のフレームで N を返す（N は From〜1）
        フレームが飛んで数字をいくつかまたいでも、鳴らすのはいちばん新しい1つだけ（音を連続で鳴らさない）
    */
    public sealed class CountdownTicker
    {
        // 何秒前から鳴らすか
        public int From = 10;

        int _last = int.MaxValue;

        // ゲームが始まるときに呼ぶ
        public void Reset()
        {
            _last = int.MaxValue;
        }

        // 今回鳴らす数字（From〜1）を返す。鳴らさないフレームは 0
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
