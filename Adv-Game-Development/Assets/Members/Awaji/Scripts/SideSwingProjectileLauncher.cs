
using UnityEngine;
using Toufuku.GameInput;
using Toufuku.Rescue;

public class SideSwingProjectileLauncher : MonoBehaviour
{
    [Header("横振り検知")]
    [SerializeField] private SideSwingDetector swingDetector;

    [Header("お守り選択")]
    [SerializeField] private OmamoriSelector omamoriSelector;

    [Header("専用弾")]
    [SerializeField] private SideSwingBullet sideSwingBulletPrefab;
    [SerializeField] private Transform spawnPoint;

    private void OnEnable()
    {
        if (swingDetector != null)
            swingDetector.SideSwingDetected += Fire;
    }

    private void OnDisable()
    {
        if (swingDetector != null)
            swingDetector.SideSwingDetected -= Fire;
    }

    private void Fire()
    {
        if (sideSwingBulletPrefab == null || spawnPoint == null)
        {
            Debug.LogWarning(
                "[SideSwingLauncher] 弾Prefabまたは発射地点が未設定です。",
                this
            );
            return;
        }

        if (omamoriSelector == null)
        {
            Debug.LogWarning(
                "[SideSwingLauncher] OmamoriSelectorが未設定です。",
                this
            );
            return;
        }

        // 現在選択中のお守りの種類を取得
        OmamoriType selectedType = omamoriSelector.Current;

        // 横振り専用弾を生成
        SideSwingBullet bullet = Instantiate(
            sideSwingBulletPrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        // 弾の救済判定に使用する種類を設定
        bullet.SetRescueType(selectedType);

        // 正面に発射（Launchは1回だけ呼ぶ）
        bullet.Launch(spawnPoint.forward);

        Debug.Log(
            $"[SideSwingLauncher] 横振り弾を発射: {selectedType}"
        );
    }
}
