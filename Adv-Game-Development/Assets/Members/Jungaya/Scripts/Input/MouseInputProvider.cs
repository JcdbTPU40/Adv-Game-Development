using UnityEngine;

namespace Toufuku.GameInput
{
    /// <summary>
    /// マウス＋数字キーの入力実装 — Issue #20
    ///
    /// ・発射: マウス左クリック
    /// ・照準: マウスカーソル位置
    /// ・お守り選択: 数字キー 1〜5
    ///
    /// シーンの GameManager 等に付け、TestShooter / OmamoriSelector の
    /// inputProviderSource へドラッグして使う。
    /// ESP32 コントローラ版は同じ IInputProvider を実装して差し替える。
    /// </summary>
    public class MouseInputProvider : MonoBehaviour, IInputProvider
    {
        public bool FireTriggered => Input.GetMouseButtonDown(0);

        public Vector3 AimScreenPosition => Input.mousePosition;

        public int OmamoriSelectTriggered
        {
            get
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) return 0; // 健康
                if (Input.GetKeyDown(KeyCode.Alpha2)) return 1; // 学業
                if (Input.GetKeyDown(KeyCode.Alpha3)) return 2; // 恋愛
                if (Input.GetKeyDown(KeyCode.Alpha4)) return 3; // 金運
                if (Input.GetKeyDown(KeyCode.Alpha5)) return 4; // 厄除け
                return -1;
            }
        }
    }
}
