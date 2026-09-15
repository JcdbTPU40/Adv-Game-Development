using UnityEngine;
using Toufuku.Rescue;
using Toufuku.GameInput;

/*
    検証用のかんたんな発射のクラス。発射の入力で、照準が指しているワールドの場所に向けて
    弾のプレハブを撃つ。弾には Rigidbody + Collider + OmamoriBullet が必要
    カーソルを客の真ん中やはしに合わせて当てれば、命中ゾーン（中心/中/外）をためせる
    OmamoriSelector を入れれば、数字キー1〜5 で撃つお守りの種類を切りかえて、
    相性◯/✗（#10）もためせる

    #20: 入力は IInputProvider を通す（入っていなければマウスを直接読む）
         ESP32 のコントローラー版は inputProviderSource を入れかえるだけでいい
    #32: GameSession が終わっている（リザルト）ときは発射しない
*/
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
        // セッションが終わっている間（リザルト画面）は入力を止める（#32）
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

        // 照準が指しているワールドの場所（落ちる場所）を出す（#20: 入力はまとめてある）
        Vector3 aimScreenPos = _input != null ? _input.AimScreenPosition : Input.mousePosition;
        Ray ray = cam.ScreenPointToRay(aimScreenPos);
        Vector3 aimPoint;
        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, aimMask, QueryTriggerInteraction.Ignore))
        {
            // 客や地面など、何かに当たればその点をねらう（かべなどは aimMask で外す）
            aimPoint = hit.point;
        }
        else
        {
            // 何にも当たらなければ、高さ groundY の水平な面と交わる点を落ちる場所にする
            Plane ground = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            aimPoint = ground.Raycast(ray, out float d) ? ray.GetPoint(d) : ray.GetPoint(30f);
        }

        // 発射する位置
        Vector3 origin = spawnPoint != null
            ? spawnPoint.position
            : cam.transform.position + cam.transform.forward * 1.0f;

        /*
            重力を考えて、flightTime 秒後にちょうど aimPoint に落ちる最初の速さを計算する
              aimPoint = origin + v*t + 0.5*g*t^2 なので v = (aimPoint-origin)/t - 0.5*g*t
        */
        float t = Mathf.Max(0.01f, flightTime);
        Vector3 g = Physics.gravity;
        Vector3 launchVel = (aimPoint - origin) / t - 0.5f * g * t;

        GameObject bullet = Instantiate(bulletPrefab, origin, Quaternion.LookRotation(launchVel));

        // 弾に種類を入れる（#10 の相性の判定で使う）
        var ob = bullet.GetComponent<OmamoriBullet>();
        if (ob != null)
            ob.SetType(selector != null ? selector.Current : fallbackType);

        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = true;          // 弾の計算は重力があることが前提
            rb.linearVelocity = launchVel; // この最初の速さで投げれば aimPoint に届く
        }

        Destroy(bullet, bulletLife);
    }
}
