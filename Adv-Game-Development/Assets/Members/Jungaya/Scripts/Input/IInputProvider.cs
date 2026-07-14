using UnityEngine;

namespace Toufuku.GameInput
{
    /// <summary>
    /// 入力の抽象化 — Issue #20
    ///
    /// マウス版と ESP32 コントローラ版を差し替えられるよう、発射側が読む入力を
    /// このインターフェースに集約する。7月のコントローラ完成時の手戻り防止。
    ///
    /// 使い方:
    ///   ・実装は MonoBehaviour（例: <see cref="MouseInputProvider"/>）にして、
    ///     TestShooter / OmamoriSelector の inputProviderSource へドラッグする。
    ///   ・ESP32 版はこのインターフェースを実装した Esp32InputProvider を追加し、
    ///     Inspector で差し替えるだけでよい（発射側のコードは変更不要）。
    /// </summary>
    public interface IInputProvider
    {
        /// <summary>このフレームで発射入力があったか（マウス左クリック相当）。</summary>
        bool FireTriggered { get; }

        /// <summary>照準のスクリーン座標（マウスカーソル相当）。</summary>
        Vector3 AimScreenPosition { get; }

        /// <summary>
        /// このフレームで選択されたお守り番号（0〜4）。なければ -1。
        /// OmamoriType の enum 値に対応する（0:健康 1:学業 2:恋愛 3:金運 4:厄除け）。
        /// </summary>
        int OmamoriSelectTriggered { get; }
    }
}
