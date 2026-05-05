using UnityEngine;
using System.Collections;
using UnityEngine.InputSystem;

public class Shoot_Bullet : MonoBehaviour
{
    [SerializeField] GameObject omamori_bullet;
    [SerializeField] GameObject shootPos;
    [SerializeField] float bullet_speed = 3.0f;
    Vector3 mousePos;
    Vector3 youPos;
    Plane plane = new Plane();
    float distance = 0;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        mousePos = Input.mousePosition;
        var youPos = Camera.main.ScreenPointToRay(mousePos);

        plane.SetNormalAndPosition(Vector3.up, transform.localPosition);
        if(plane.Raycast(youPos, out distance))
        {
            var lookPoint = youPos.GetPoint(distance);
            transform.LookAt(lookPoint);
        }

        if (Input.GetMouseButtonDown(0))
        {
            Shoot();
        }
    }

    void Shoot()
    {
        GameObject bullet = Instantiate(omamori_bullet, shootPos.transform.position, Quaternion.identity);
        bullet.GetComponent<Rigidbody>().AddForce(bullet_speed * shootPos.transform.up);
        Destroy(bullet, 10);
    }
}
