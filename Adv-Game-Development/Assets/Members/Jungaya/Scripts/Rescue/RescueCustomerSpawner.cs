using UnityEngine;
using Toufuku.Playtest;

namespace Toufuku.Rescue
{
    /*
        客のタイプのカタログ（#16）を使って客を作る、かんたんなスポナー（使っても使わなくてもいい）

        ・決まった間かくでプレハブを作って、カタログからランダムなプロフィールを入れる
        ・プレハブに CustomerProfileApplier が付いていればそっちにまかせて、
          見た目（代表の色/Sprite/Prefab）＋正解のお守りをまとめて反映させる
          付いていなければ CustomerRescue に直接 Setup する（最低限の判定だけ動く）

        もとからある Customer_Spawner とは関係なく動く。Rescue まわりの動作確認用に、シーンに1つ置いて使うつもり
        ※ ずっと補充するやり方を本番で作るときの注意: 上限が下がって人数が多くなっても、もういる客をむりやり帰らせないで、自然に減るのを待つこと（企画書 v3 §7/§8。MockCrowdDirector.BalanceToTarget と同じ方針）
    */
    public class RescueCustomerSpawner : MonoBehaviour
    {
        [Header("生成元")]
        [Tooltip("生成する客プレハブ。")]
        [SerializeField] private GameObject customerPrefab;
        [Tooltip("客タイプ定義のカタログ(#16)。")]
        [SerializeField] private CustomerProfileCatalog catalog;

        [Header("客種（付録B B-1 / #54）")]
        [Tooltip("生成する客種。初期R・D満タン秒数・基礎点はこの客種で数値表から引く。客種の抽選（比率）は #57 / #62 の担当。")]
        [SerializeField] private CustomerKind customerKind = CustomerKind.Normal;
        [Tooltip("客種ごとの数値表（付録B B-1）。未設定なら客プレハブ側の設定のままにする。")]
        [SerializeField] private CustomerKindTable kindTable;

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

            // ゲームが終わっている間（リザルト）は客を出さない（#32）
            if (GameSession.Instance != null && !GameSession.Instance.IsPlaying) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                Spawn();
                _timer = spawnInterval;
            }
        }

        // 1人作って、カタログからランダムなプロフィールを入れる。作った GameObject を返す
        public GameObject Spawn()
        {
            if (customerPrefab == null) return null;

            // #63: 計測プレイ中は「客ID × 使いみち」の決まったシードの乱数でくじを引く（管理されていなければ UnityEngine.Random）
            int id = CustomerSpawnId.NextId;
            float x = PlaytestRandom.Range(PlaytestRandom.TryFor(PlaytestStreams.Placement, id), spawnXRange.x, spawnXRange.y);
            Vector3 pos = new Vector3(x, spawnY, spawnZ);
            GameObject go = Instantiate(customerPrefab, pos, Quaternion.identity);
            CustomerSpawnId.Assign(go, CustomerSpawnId.CategoryNormal);

            // 救えた・黒客になった → 神社の評価（#30）につなぐ。プレハブに付けわすれていても動くように、念のため付ける
            if (go.GetComponent<CustomerState>() != null && go.GetComponent<ShrineRatingHook>() == null)
                go.AddComponent<ShrineRatingHook>();

            /*
                客の種類の数値（最初のR・D が満タンになる秒数・基礎点）を入れる。通常客の D が満タンになる秒数（15〜25秒）は
                「客ID × 使いみち」の決まったシードで引く（#63 で同じ結果にできるようにするため）
            */
            CustomerState state = go.GetComponent<CustomerState>();
            if (state != null && kindTable != null)
                state.Setup(customerKind, kindTable, PlaytestRandom.Value(PlaytestRandom.TryFor(PlaytestStreams.DangerSeconds, id)));

            CustomerProfile profile = catalog != null ? catalog.GetRandom(PlaytestRandom.TryFor(PlaytestStreams.Profile, id)) : null;

            CustomerProfileApplier applier = go.GetComponent<CustomerProfileApplier>();
            if (applier != null)
            {
                applier.Apply(profile, catalog);
            }
            else
            {
                // Applier がないプレハブでも最低限、相性の判定だけは効くようにしておく
                CustomerRescue rescue = go.GetComponent<CustomerRescue>();
                if (rescue != null && profile != null)
                    rescue.Setup(profile, catalog != null ? catalog.AffinityTable : null);
            }

            return go;
        }
    }
}
