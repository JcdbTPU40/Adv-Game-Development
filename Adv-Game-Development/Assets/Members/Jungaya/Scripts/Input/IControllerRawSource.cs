namespace Toufuku.GameInput
{
    /// <summary>
    /// 大幣コントローラの生入力 — Issue #51
    ///
    /// ボタンは「押されているか」のレベル値だけを返し、押した／離したのエッジ化と
    /// 優先規則・キャンセル・クールダウンは <see cref="ThrowInputController"/>（InputStateMachine）が一括で行う。
    /// 実装: <see cref="KeyboardMouseRawSource"/>（開発用）／<see cref="Esp32RawSource"/>（実機）。
    /// </summary>
    public interface IControllerRawSource
    {
        /// <summary>入力を受け取れる状態か。</summary>
        bool IsConnected { get; }

        /// <summary>生のヨー角（度）。キャリブレーション前の値。</summary>
        float Yaw { get; }

        /// <summary>生のピッチ角（度）。照準の奥行き（#60: 地面上 3〜18m）に使う。</summary>
        float Pitch { get; }

        /// <summary>色ボタン（0〜4、OmamoriType の並び＝ボタン箱の左→右）が押されているか。</summary>
        bool IsColorHeld(int index);

        /// <summary>正面ボタンが押されているか。</summary>
        bool IsFrontHeld { get; }

        /// <summary>
        /// 検出済みの振りピークを 1 つ取り出す。なければ false。
        /// 1 フレームに複数溜まることがあるので、false が返るまで呼ぶ。
        /// </summary>
        bool TryConsumeSwingPeak(out float strength, out double time);
    }

    /// <summary>
    /// 直前に取り出した振りピークの「入力時刻」を返せる生入力 — Issue #63（T6-USB #52 と共用）
    ///
    /// <see cref="IControllerRawSource.TryConsumeSwingPeak"/> の直後（同じ呼び出しの中）に読む。
    /// コントローラ側の時計なので Unity の時刻とは原点が違う。分からなければ NaN。
    /// </summary>
    public interface ISwingPeakInputTime
    {
        double LastSwingPeakInputTime { get; }
    }
}
