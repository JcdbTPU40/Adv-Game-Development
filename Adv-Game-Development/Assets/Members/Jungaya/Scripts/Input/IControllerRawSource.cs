namespace Toufuku.GameInput
{
    /*
        大幣コントローラーからの入力そのもの（#51）

        ボタンは「今押されているか」だけを返す。押した・はなしたの判定や、
        優先のルール・キャンセル・クールダウンは ThrowInputController（InputStateMachine）がまとめてやる
        これを使っているのは KeyboardMouseRawSource（開発用）と Esp32RawSource（実機）
    */
    public interface IControllerRawSource
    {
        // 入力を受け取れる状態かどうか
        bool IsConnected { get; }

        // そのままのヨー角（度）。キャリブレーションする前の値
        float Yaw { get; }

        // そのままのピッチ角（度）。照準の奥行き（#60: 地面の 3〜18m）に使う
        float Pitch { get; }

        // 色ボタン（0〜4、OmamoriType の順番＝ボタン箱の左から右）が押されているかどうか
        bool IsColorHeld(int index);

        // 正面ボタンが押されているかどうか
        bool IsFrontHeld { get; }

        /*
            見つけた振りピークを1つ取り出す。なければ false
            1フレームにいくつか貯まることがあるので、false が返ってくるまで呼ぶ
        */
        bool TryConsumeSwingPeak(out float strength, out double time);
    }

    /*
        さっき取り出した振りピークの「入力時刻」を返せる入力（#63。T6-USB の #52 でも使う）

        IControllerRawSource.TryConsumeSwingPeak のすぐあと（同じ呼び出しの中）で読む
        コントローラー側の時計なので、Unity の時刻とはスタート地点がちがう。わからないときは NaN
    */
    public interface ISwingPeakInputTime
    {
        double LastSwingPeakInputTime { get; }
    }
}
