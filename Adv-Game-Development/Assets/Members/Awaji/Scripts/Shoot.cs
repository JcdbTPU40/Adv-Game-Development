using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Shoot : MonoBehaviour
{
    [SerializeField] GameObject[] bullet;
    [SerializeField]GameObject shootPos;
    [SerializeField] ConecteController conecteController;
    int bullet_type = 0;
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

        float pitch = conecteController.pitch;

        // ���݂�Pitch��ۑ�
        pitchHistory.Enqueue(pitch);
        type();
        // 5�t���[�������܂�܂ő҂�
        if (pitchHistory.Count > 5)
        {
            float oldPitch =
                pitchHistory.Dequeue();

            power =
                pitch - oldPitch;
            
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
        GameObject b = Instantiate(bullet[bullet_type], shootPos.transform.position, shootPos.transform.rotation);
        Rigidbody rb = b.GetComponent<Rigidbody>();
        rb.linearVelocity = shootPos.transform.forward * 25f;
        Destroy(b, 10);
    }

    void UShoot()
    {
        GameObject b = Instantiate(bullet[bullet_type], shootPos.transform.position, shootPos.transform.rotation);
        Rigidbody rb = b.GetComponent<Rigidbody>();
        rb.linearVelocity = (shootPos.transform.up + (shootPos.transform.forward * 1.5f)) * 10f;
        Destroy(b, 10);
    }

    void type()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            bullet_type = 0;
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            bullet_type = 1;
        }
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            bullet_type = 2;
        }
        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            bullet_type = 3;
        }
        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            bullet_type = 4;
        }
    }
}
