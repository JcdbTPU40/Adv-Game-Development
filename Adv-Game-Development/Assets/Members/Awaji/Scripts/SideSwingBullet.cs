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

        [Header("’e‚ÌFi‚¨ç‚è5íj")]
        [SerializeField] private ParticleSystem particle;

        [SerializeField] private Color enmusubiColor = Color.red;       // ‰Œ‹‚ÑFÔ
        [SerializeField] private Color kinunColor = Color.yellow;       // ‹à‰^F‰©
        [SerializeField] private Color yakuyokeColor = Color.white;     // –ïœ‚¯F”’
        [SerializeField] private Color kenkouColor = Color.green;       // Œ’NF—Î
        [SerializeField] private Color gakugyouColor = Color.blue;      // Šw‹ÆFÂ

        private Color GetColor(OmamoriType type)
        {
            switch (type)
            {
                case OmamoriType.Enmusubi:
                    return enmusubiColor;

                case OmamoriType.Kinun:
                    return kinunColor;

                case OmamoriType.Yakuyoke:
                    return yakuyokeColor;

                case OmamoriType.Kenkou:
                    return kenkouColor;

                case OmamoriType.Gakugyou:
                    return gakugyouColor;

                default:
                    return Color.white;
            }
        }

        public void SetRescueType(OmamoriType type)
        {
            rescueType = type;
            ApplyColor();
        }

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

            ApplyColor();

            Destroy(gameObject, lifeTime);
        }

        private void Update()
        {
            if (!launched)
                return;

            transform.position += direction * speed * Time.deltaTime;
        }

        private void ApplyColor()
        {
            if (particle == null)
                return;

            Color color = GetColor(rescueType);

            ParticleSystem.MainModule main = particle.main;
            main.startColor = color;

            var colorOverLifetime = particle.colorOverLifetime;

            if (colorOverLifetime.enabled)
            {
                Gradient gradient = new Gradient();

                gradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(color, 0f),
                        new GradientColorKey(color, 1f)
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );

                colorOverLifetime.color = gradient;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!collision.gameObject.CompareTag("Customer"))
                return;

            Vector3 hitPoint = collision.GetContact(0).point;

            HitZoneTarget target =
                collision.gameObject.GetComponent<HitZoneTarget>();

            HitZone zone = target != null
                ? target.EvaluateZone(hitPoint)
                : HitZone.Inner;

            OmamoriHitResolver.ApplyHit(
                collision.gameObject,
                rescueType,
                zone
            );

            // ‹q‚É“–‚½‚Á‚Ä‚àÁ‚³‚È‚¢iŠÑ’Êj
        }
    }
}