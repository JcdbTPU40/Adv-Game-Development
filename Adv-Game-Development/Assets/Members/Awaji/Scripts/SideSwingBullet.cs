
using Toufuku.Rescue;
using UnityEngine;

namespace Toufuku.GameInput
{
    public class SideSwingBullet : MonoBehaviour
    {
        [Header("’e‚Ìİ’è")]
        [SerializeField] private float speed = 15f;
        [SerializeField] private float lifeTime = 5f;

        private Vector3 direction;
        private bool launched;

        [Header("‰¡U‚è’e‚Ì‹~Ï”»’è")]
        [SerializeField]
        private OmamoriType rescueType = OmamoriType.Enmusubi;
        public void Launch(Vector3 forward)
        {
            direction = forward.normalized;

            if (direction.sqrMagnitude <= 0.0001f)
            {
                Destroy(gameObject);
                return;
            }

            transform.rotation = Quaternion.LookRotation(direction);
            launched = true;

            Destroy(gameObject, lifeTime);
        }

        private void Update()
        {
            if (!launched)
                return;

            transform.position += direction * speed * Time.deltaTime;
        }

        public void SetRescueType(OmamoriType type)
        {
            rescueType = type;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.gameObject.CompareTag("Customer"))
            {
                Vector3 hitPoint = collision.GetContact(0).point;

                HitZoneTarget target =
                    collision.gameObject.GetComponent<HitZoneTarget>();

                HitZone zone = target != null
                    ? target.EvaluateZone(hitPoint)
                    : HitZone.Inner;

                // ’Êí’e‚Æ“¯‚¶‹~Ï”»’è
                OmamoriHitResolver.ApplyHit(
                    collision.gameObject,
                    rescueType,
                    zone
                );

                // ‹q‚É“–‚½‚Á‚Ä‚àÁ‚³‚È‚¢iŠÑ’Êj
            }
        }
    }
}
