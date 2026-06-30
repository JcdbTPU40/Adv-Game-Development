using UnityEngine;
using Toufuku.Rescue;

/// <summary>
/// 検証用の簡易発射。マウス左クリックで、カーソルが指すワールド地点へ向けて
/// 弾プレハブを撃つ。弾には Rigidbody + Collider + OmamoriBullet が必要。
/// カーソルを客の中心/端に合わせて当てれば、命中ゾーン（中心/中/外）を試せる。
/// OmamoriSelector を割り当てれば、数字キー 1〜5 で撃つお守り種類を切り替えて
/// 相性◯/✗（#10）も試せる。本番入力ができたら不要になるテスト専用スクリプト。
/// </summary>
public class TestShooter : MonoBehaviour
{
    [Header("発射するもの")]
    [Tooltip("OmamoriBullet が付いた弾プレハブ")]
    [SerializeField] GameObject bulletPrefab;
    [Tooltip("発射位置。未設定ならカメラ手前から撃つ")]
    [SerializeField] Transform spawnPoint;
    [SerializeField] float speed = 25f;
    [SerializeField] float bulletLife = 5f;

    [Header("お守り種類の供給元（#9/#10 テスト）。未設定なら fallbackType を使う")]
    [SerializeField] OmamoriSelector selector;
    [SerializeField] OmamoriType fallbackType = OmamoriType.Kenkou;

    Camera cam;

    void Awake()
    {
        cam = Camera.main;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
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

        // カーソルが指すワールド地点を求める
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 aimPoint = Physics.Raycast(ray, out RaycastHit hit, 200f)
            ? hit.point
            : ray.GetPoint(30f); // 何にも当たらなければ前方30mを狙う

        // 発射位置
        Vector3 origin = spawnPoint != null
            ? spawnPoint.position
            : cam.transform.position + cam.transform.forward * 1.0f;

        Vector3 dir = (aimPoint - origin).normalized;

        GameObject bullet = Instantiate(bulletPrefab, origin, Quaternion.LookRotation(dir));

        // 弾に種類を埋め込む（#10 の相性判定で使う）。
        var ob = bullet.GetComponent<OmamoriBullet>();
        if (ob != null)
            ob.SetType(selector != null ? selector.Current : fallbackType);

        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null) rb.linearVelocity = dir * speed;

        Destroy(bullet, bulletLife);
    }
}
