using UnityEngine;

namespace Toufuku.GameInput
{
    /*
        入力をまとめるためのインターフェース（#20）

        マウス版と ESP32 コントローラー版を入れかえられるように、発射する側が読む入力を
        このインターフェースに集めている。7月にコントローラーができたときに作りなおさなくていいようにするため

        使い方:
          ・中身は MonoBehaviour で作って（例: MouseInputProvider）、
            TestShooter / OmamoriSelector の inputProviderSource にドラッグする
          ・ESP32 版は、このインターフェースを使った Esp32InputProvider を足して、
            Inspector で入れかえるだけでいい（発射する側のコードは変えなくていい）
    */
    public interface IInputProvider
    {
        // このフレームで発射の入力があったか（マウスの左クリックと同じ）
        bool FireTriggered { get; }

        // 照準のスクリーン座標（マウスカーソルと同じ）
        Vector3 AimScreenPosition { get; }

        /*
            このフレームで選ばれたお守りの番号（0〜4）。なければ -1
            OmamoriType の enum の値と同じ（0:健康 1:学業成就 2:厄除け安全 3:縁結び 4:金運）
        */
        int OmamoriSelectTriggered { get; }
    }
}
