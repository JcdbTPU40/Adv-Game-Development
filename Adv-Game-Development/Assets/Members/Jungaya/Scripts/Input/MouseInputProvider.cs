using UnityEngine;

namespace Toufuku.GameInput
{
    /*
        マウスと数字キーで入力するクラス（#20）

        ・発射: マウスの左クリック
        ・照準: マウスカーソルの位置
        ・お守りを選ぶ: 数字キー1〜5

        シーンの GameManager などに付けて、TestShooter / OmamoriSelector の
        inputProviderSource にドラッグして使う
        ESP32 コントローラー版は、同じ IInputProvider を使ったクラスを作って入れかえる
    */
    public class MouseInputProvider : MonoBehaviour, IInputProvider
    {
        public bool FireTriggered => Input.GetMouseButtonDown(0);

        public Vector3 AimScreenPosition => Input.mousePosition;

        public int OmamoriSelectTriggered
        {
            get
            {
                // キー1〜5 は OmamoriType の enum の値（企画書 v3 §3 の順番）に合わせている
                if (Input.GetKeyDown(KeyCode.Alpha1)) return 0; // 健康
                if (Input.GetKeyDown(KeyCode.Alpha2)) return 1; // 学業成就
                if (Input.GetKeyDown(KeyCode.Alpha3)) return 2; // 厄除け安全
                if (Input.GetKeyDown(KeyCode.Alpha4)) return 3; // 縁結び
                if (Input.GetKeyDown(KeyCode.Alpha5)) return 4; // 金運
                return -1;
            }
        }
    }
}
