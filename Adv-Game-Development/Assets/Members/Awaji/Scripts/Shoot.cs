using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shoot : MonoBehaviour
{
    [SerializeField]GameObject bullet_Sample;
    [SerializeField]GameObject shootPos;
    [SerializeField]ArduinoTest arduinoTest;
    float cooldown = 0;
    float ucooldown = 0;
    float power;
    float oldPitch;
    Queue<float> pitchHistory =
        new Queue<float>();
    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        cooldown -= Time.deltaTime;

        float pitch = arduinoTest.pitch;

        // Œ»Ý‚ÌPitch‚ð•Û‘¶
        pitchHistory.Enqueue(pitch);

        // 5ƒtƒŒ[ƒ€•ª‚½‚Ü‚é‚Ü‚Å‘Ò‚Â
        if (pitchHistory.Count > 5)
        {
            float oldPitch =
                pitchHistory.Dequeue();

            power =
                pitch - oldPitch;
            /*
            if (power < -15 && cooldown <= 0)
            {
                shoot();

                cooldown = 0.3f;
            }

            if (power > 30 && cooldown <= 0)
            {
                UShoot();

                cooldown = 0.3f;
            }
            */
            if (Input.GetMouseButton(0) && cooldown <= 0)
            {
                shoot();

                cooldown = 0.3f;
            }

            if (Input.GetMouseButton(1) && cooldown <= 0)
            {
                UShoot();

                cooldown = 0.3f;
            }
            Debug.Log(power);
        }
    }

    void shoot()
    {
        GameObject bullet = Instantiate(bullet_Sample, shootPos.transform.position, shootPos.transform.rotation);
        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        rb.linearVelocity = shootPos.transform.forward * 25f;
    }

    void UShoot()
    {
        GameObject bullet = Instantiate(bullet_Sample, shootPos.transform.position, shootPos.transform.rotation); 
        Rigidbody rb = bullet.GetComponent<Rigidbody>();

        rb.linearVelocity = (shootPos.transform.up + (shootPos.transform.forward * 1.5f)) * 10f;
        Destroy(bullet, 10);
    }
}
