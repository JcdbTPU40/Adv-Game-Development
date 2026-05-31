using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shoot : MonoBehaviour
{
    [SerializeField]GameObject bullet_Sample;
    [SerializeField]GameObject shootPos;
    [SerializeField]ArduinoTest arduinoTest;
    float cooldown = 0;
    float power;
    float oldPitch;
    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        cooldown -= Time.deltaTime;
        float pitch = arduinoTest.pitch;

        power = pitch - oldPitch;

        oldPitch = pitch;

        if(power < -15 && cooldown <= 0)
        {
            shoot();

            cooldown = 0.3f;
        }
        Debug.Log(power);
    }

    void shoot()
    {
        float p = -power;
        GameObject bullet = Instantiate(bullet_Sample, shootPos.transform.position, shootPos.transform.rotation);
        bullet.GetComponent<Rigidbody>().AddForce(50 * p * shootPos.transform.forward);
        Destroy(bullet, 10);
    }
}
