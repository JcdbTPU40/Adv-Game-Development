using UnityEngine;
using Toufuku.GameInput;

/*
    マウスで操作するときに、大幣（赤いキューブ）をカーソルの方向に左右（ヨーだけ）に向けるクラス

    ・ねらう場所の出し方は TestShooter.Fire() と同じ（レイキャスト → 当たらなかったら groundY の平面）
      aimMask と groundY は TestShooter と同じ値にしておくと、見た目と落ちる場所がそろう
    ・ESP32 のコントローラーがつながっているときは PlayerDirect（con.yaw）が回転を担当するので、
      このスクリプトは何もしない
    ・PlayerDirect が Update で回転を書くので、こっちは LateUpdate で上書きする
      （つながっていないときに PlayerDirect が最初の向きにもどそうとするのを打ち消すため）
*/
public class OnusaAim : MonoBehaviour
{
    [Header("入力の供給元（#20）。IInputProvider 実装をドラッグ。未設定ならマウス直読み")]
    [SerializeField] MonoBehaviour inputProviderSource;

    [Header("照準判定（TestShooter と同じ設定にする）")]
    [Tooltip("レイが何にも当たらなかったときの照準面の高さ(Y)")]
    [SerializeField] float groundY = 1f;
    [SerializeField] float maxRayDistance = 500f;
    [Tooltip("照準判定で当てたいレイヤー。透明な壁(Wall)やお守り(Omamori)は外しておく")]
    [SerializeField] LayerMask aimMask = ~0;

    [Header("回転")]
    [Tooltip("振り向きの速さ。大きいほどキビキビ動く")]
    [SerializeField] float turnSpeed = 10f;

    [Header("ESP32 接続時はコントローラ側（PlayerDirect）に回転を譲る。未設定ならマウス扱い")]
    [SerializeField] ConecteController con;

    Camera cam;
    IInputProvider _input;

    void Awake()
    {
        cam = Camera.main;

        _input = inputProviderSource as IInputProvider;
        if (inputProviderSource != null && _input == null)
            Debug.LogWarning("[OnusaAim] inputProviderSource が IInputProvider を実装していません", this);
    }

    void LateUpdate()
    {
        // コントローラーがつながっているときは PlayerDirect（実機の向き）を優先する
        if (con != null && con.isConnected) return;

        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) return;
        }

        // 照準が指しているワールドの場所を出す（TestShooter.Fire() と同じ手順）
        Vector3 aimScreenPos = _input != null ? _input.AimScreenPosition : Input.mousePosition;
        Ray ray = cam.ScreenPointToRay(aimScreenPos);
        Vector3 aimPoint;
        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, aimMask, QueryTriggerInteraction.Ignore))
        {
            aimPoint = hit.point;
        }
        else
        {
            Plane ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            aimPoint = ground.Raycast(ray, out float d) ? ray.GetPoint(d) : ray.GetPoint(30f);
        }

        // 高さは見ないで、左右（ヨー）だけカーソルの方向に向ける
        Vector3 dir = aimPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return; // 真上や真下を指しているときは向きをそのままにする

        Quaternion targetRot = Quaternion.LookRotation(dir);
        transform.rotation =
            Quaternion.Lerp(
                transform.rotation,
                targetRot,
                Time.deltaTime * turnSpeed
            );
    }
}
