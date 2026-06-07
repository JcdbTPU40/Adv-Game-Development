using UnityEngine;
using System.Collections;

public class Customer_Move : MonoBehaviour
{
    [SerializeField] float speed;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Random.Range(3, 7);
    }

    // Update is called once per frame
    void Update()
    {
        transform.position += new Vector3(0, 0, -speed * Time.deltaTime);
        if(transform.position.z <= -33)
        {
            transform.position = new Vector3(transform.position.x, 1, -33);
        }
    }
}
