using UnityEngine;

public class Customer_Spawner : MonoBehaviour
{
    float spawn_Time = 3f;
    [SerializeField]GameObject customer;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        spawn_Time -= Time.deltaTime;
        if(spawn_Time <= 0f)
        {
            Instantiate(customer, transform.position, Quaternion.identity);
            spawn_Time = 3f;
            float spawnPosX = Random.Range(-3.3f, 3.3f);
            transform.position = new Vector3(spawnPosX, 1, 38);
        }
    }
}
