using UnityEngine;

public class Customer_SpawnerA : MonoBehaviour
{
    [SerializeField] float setSpawnTime = 3f;
    [SerializeField] GameObject customer;

    [Tooltip("横方向(X)のばらつき。スポナー位置を中心に ±この値 でランダムに出す")]
    [SerializeField] float laneHalfWidth = 3.3f;

    float spawn_Time;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        spawn_Time = setSpawnTime;
    }

    // Update is called once per frame
    void Update()
    {
        // セッション終了中（リザルト）はスポーン停止（#32）
        if (GameSession.Instance != null && !GameSession.Instance.IsPlaying) return;

        spawn_Time -= Time.deltaTime;
        if (spawn_Time <= 0f)
        {
            spawn_Time = setSpawnTime;

            // スポナー自身は動かさない。毎回スポナーの位置(鳥居)で、X だけランダムにして出す。
            float x = transform.position.x + Random.Range(-laneHalfWidth, laneHalfWidth);
            Vector3 spawnPos = new Vector3(x, transform.position.y, transform.position.z);
            Instantiate(customer, spawnPos, Quaternion.identity);
        }
    }
}
