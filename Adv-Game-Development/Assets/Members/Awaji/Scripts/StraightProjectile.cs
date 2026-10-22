
using UnityEngine;

namespace Toufuku.GameInput
{
    public class StraightProjectile : MonoBehaviour
    {
        [Header("íeÇÃê›íË")]
        [SerializeField] private float speed = 15f;
        [SerializeField] private float lifeTime = 5f;

        private Vector3 direction;

        public void Launch(Vector3 forward)
        {
            direction = forward.normalized;

            if (direction.sqrMagnitude <= 0.0001f)
            {
                Destroy(gameObject);
                return;
            }

            transform.rotation = Quaternion.LookRotation(direction);
            Destroy(gameObject, lifeTime);
        }

        private void Update()
        {
            transform.position += direction * speed * Time.deltaTime;
        }
    }
}
