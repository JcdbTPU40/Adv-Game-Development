using UnityEngine;
using Toufuku.Rescue;
using Toufuku.GameInput;

/// <summary>
/// 検証用の簡易発射。発射入力で、照準が指すワールド地点へ向けて
/// 弾プレハブを撃つ。弾には Rigidbody + Collider + OmamoriBullet が必要。
/// カーソルを客の中心/端に合わせて当てれば、命中ゾーン（中心/中/外）を試せる。
/// OmamoriSelector を割り当てれば、数字キー 1〜5 で撃つお守り種類を切り替えて
/// 相性◯/✗（#10）も試せる。
///
/// #20: 入力は IInputProvider 経由（未設定ならマウス直読みにフォールバック）。
///      ESP32 コントローラ版は inputProviderSource を差し替えるだけでよい。
/// #32: GameSession が終了中（リザルト）のときは発射しない。
/// </summary>
public class TestShooter : MonoBehaviour
{
    [Header("発射するもの")]
    [Tooltip("OmamoriBullet が付いた弾プレハブ")]
    [SerializeField] GameObject bulletPrefab;
    [Tooltip("発射位置。未設定ならカメラ手前から撃つ")]
    [SerializeField] Transform spawnPoint;
    [Tooltip("着弾までの時間(秒)。大きいほど山なり・小さいほど直線的に投げる")]
    [SerializeField] float flightTime = 1.0f;
    [SerializeField] float bulletLife = 5f;

    [Header("クリック判定")]
    [Tooltip("クリックが何にも当たらなかったときの着弾面の高さ(Y)")]
    [SerializeField] float groundY = 1f;
    [Tooltip("クリックのレイが届く最大距離")]
    [SerializeField] float maxRayDistance = 500f;
    [Tooltip("クリック判定で当てたいレイヤー。透明な壁(Wall)やお守り(Omamori)は外しておく")]
    [SerializeField] LayerMask aimMask = ~0;

    [Header("お守り種類の供給元（#9/#10 テスト）。未設定なら fallbackType を使う")]
    [SerializeField] OmamoriSelector selector;
    [SerializeField] OmamoriType fallbackType = OmamoriType.Kenkou;

    [Header("入力の供給元（#20）。IInputProvider 実装（例: MouseInputProvider）をドラッグ。未設定ならマウス直読み")]
    [SerializeField] MonoBehaviour inputProviderSource;

    Camera cam;
    IInputProvider _input;

    void Awake()
    {
        cam = Camera.main;

        _input = inputProviderSource as IInputProvider;
        if (inputProviderSource != null && _input == null)
            Debug.LogWarning("[TestShooter] inputProviderSource が IInputProvider を実装していません", this);
    }

    void Update()
    {
        // セッション終了中（リザルト画面）は入力停止（#32）
        if (GameSession.Instance != null && !GameSession.Instance.IsPlaying) return;

        bool fire = _input != null ? _input.FireTriggered : Input.GetMouseButtonDown(0);
        if (fire)
            Fire();
    }

    void Fire()
    {
        if (bulletPrefab == null)
        {
            Debug.LogWarning("[TestShooter] bulletPrefab が未設定です");
            return;
        }
        if (cam == null) cam = Camera.main;

        // 照準が指すワールド地点（着弾点）を求める（#20: 入力は抽象化済み）
        Vector3 aimScreenPos = _input != null ? _input.AimScreenPosition : Input.mousePosition;
        Ray ray = cam.ScreenPointToRay(aimScreenPos);
        Vector3 aimPoint;
        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, aimMask, QueryTriggerInteraction.Ignore))
        {
            // 客や地面など、何かに当たればその点を狙う（壁などは aimMask で除外）
            aimPoint = hit.point;
        }
        else
        {
            // 何にも当たらなければ、高さ groundY の水平面との交点を着弾点にする
            Plane ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            aimPoint = ground.Raycast(ray, out float d) ? ray.GetPoint(d) : ray.GetPoint(30f);
        }

        // 発射位置
        Vector3 origin = spawnPoint != null
            ? spawnPoint.position
            : cam.transform.position + cam.transform.forward * 1.0f;

        // 重力を考慮して、flightTime 秒後にちょうど aimPoint へ着弾する初速を計算する。
        //   aimPoint = origin + v*t + 0.5*g*t^2  →  v = (aimPoint-origin)/t - 0.5*g*t
        float t = Mathf.Max(0.01f, flightTime);
        Vector3 g = Physics.gravity;
        Vector3 launchVel = (aimPoint - origin) / t - 0.5f * g * t;

        GameObject bullet = Instantiate(bulletPrefab, origin, Quaternion.LookRotation(launchVel));

        // 弾に種類を埋め込む（#10 の相性判定で使う）。
        var ob = bullet.GetComponent<OmamoriBullet>();
        if (ob != null)
            ob.SetType(selector != null ? selector.Current : fallbackType);

        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = true;          // 弾道計算は重力ありが前提
            rb.linearVelocity = launchVel; // この初速で投げれば aimPoint に届く
        }

        Destroy(bullet, bulletLife);
    }
}
