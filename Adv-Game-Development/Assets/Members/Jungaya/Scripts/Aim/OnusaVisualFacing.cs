using UnityEngine;

namespace Toufuku.Aim
{
    /*
        大幣の見た目（先に紙垂がついている）を、いつも画面の奥へ向けておくクラス
        ・親（ShootObject）は、マウスのときは OnusaAim が「照準のほうを +Z」に向ける
          コントローラーのときは PlayerDirect が「ヨー 0 度付近」に向けるので、+Z は手前（カメラ側）を向く
          赤い箱は前後が同じ形だったので問題なかったが、大幣は先と手もとがあるので、そのままだと逆を向く
        ・なので、親の +Z が奥を向いていなければ、見た目だけ 180 度まわす
        ・親の回転は OnusaAim が LateUpdate で書くので、そのあとに動かす
    */
    [DefaultExecutionOrder(200)]
    public class OnusaVisualFacing : MonoBehaviour
    {
        [Tooltip("奥の向きの基準にするカメラ。未設定なら Camera.main")]
        [SerializeField] Camera cam;

        static readonly Quaternion Flipped = Quaternion.Euler(0f, 180f, 0f);

        void LateUpdate()
        {
            Transform parent = transform.parent;
            if (parent == null) return;
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null) return;
            }

            Vector3 away = cam.transform.forward;
            away.y = 0f;
            Vector3 forward = parent.forward;
            forward.y = 0f;
            if (away.sqrMagnitude < 0.0001f || forward.sqrMagnitude < 0.0001f) return;

            bool facingAway = Vector3.Dot(forward, away) >= 0f;
            transform.localRotation = facingAway ? Quaternion.identity : Flipped;
        }
    }
}
