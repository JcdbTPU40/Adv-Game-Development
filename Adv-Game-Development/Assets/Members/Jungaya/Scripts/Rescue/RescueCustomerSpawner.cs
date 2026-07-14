using UnityEngine;

namespace Toufuku.Rescue
{
    /// <summary>
    /// 客タイプカタログ(#16)を使って客を生成する簡易スポナー（任意利用）。
    ///
    /// ・一定間隔でプレハブを生成し、カタログからランダムなプロフィールを適用する。
    /// ・プレハブに <see cref="CustomerProfileApplier"/> が付いていればそれに委譲し、
    ///   見た目（代表カラー/Sprite/Prefab）＋正解お守りをまとめて反映させる。
    ///   付いていなければ <see cref="CustomerRescue"/> へ直接 Setup する（最低限の判定だけ動く）。
    ///
    /// 既存の Customer_Spawner とは独立。Rescue 系の動作確認用にシーンへ1つ置いて使う想定。
    /// </summary>
    public class RescueCustomerSpawner : MonoBehaviour
    {
        [Header("生成元")]
        [Tooltip("生成する客プレハブ。")]
        [SerializeField] private GameObject customerPrefab;
        [Tooltip("客タイプ定義のカタログ(#16)。")]
        [SerializeField] private CustomerProfileCatalog catalog;

        [Header("生成タイミング")]
        [Tooltip("生成間隔（秒）。")]
        [SerializeField] private float spawnInterval = 3f;
        [Tooltip("ON なら開始直後に1体目を生成する。")]
        [SerializeField] private bool spawnOnStart = true;

        [Header("生成位置")]
        [Tooltip("X座標をこのレンジ(min,max)でランダムにする。")]
        [SerializeField] private Vector2 spawnXRange = new Vector2(-3.3f, 3.3f);
        [Tooltip("生成Y座標。")]
        [SerializeField] private float spawnY = 1f;
        [Tooltip("生成Z座標（奥）。")]
        [SerializeField] private float spawnZ = 38f;

        private float _timer;

        private void Start()
        {
            _timer = spawnOnStart ? 0f : spawnInterval;
        }

        private void Update()
        {
            if (customerPrefab == null || catalog == null) return;

            // セッション終了中（リザルト）はスポーン停止（#32）
            if (GameSession.Instance != null && !GameSession.Instance.IsPlaying) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Spawn();
                _timer = spawnInterval;
            }
        }

        /// <summary>
        /// 1体生成して、カタログからランダムなプロフィールを適用する。生成した GameObject を返す。
        /// </summary>
        public GameObject Spawn()
        {
            if (customerPrefab == null) return null;

            float x = Random.Range(spawnXRange.x, spawnXRange.y);
            Vector3 pos = new Vector3(x, spawnY, spawnZ);
            GameObject go = Instantiate(customerPrefab, pos, Quaternion.identity);

            // 解消/怒り → 神社評価(#30) の結線。プレハブに付け忘れていても動くよう保険で付与。
            if (go.GetComponent<CustomerMood>() != null && go.GetComponent<ShrineRatingHook>() == null)
                go.AddComponent<ShrineRatingHook>();

            CustomerProfile profile = catalog != null ? catalog.GetRandom() : null;

            CustomerProfileApplier applier = go.GetComponent<CustomerProfileApplier>();
            if (applier != null)
            {
                applier.Apply(profile, catalog);
            }
            else
            {
                // Applier 無しプレハブでも最低限：相性判定だけは効くようにしておく。
                CustomerRescue rescue = go.GetComponent<CustomerRescue>();
                if (rescue != null && profile != null)
                    rescue.Setup(profile, catalog != null ? catalog.AffinityTable : null);
            }

            return go;
        }
    }
}
