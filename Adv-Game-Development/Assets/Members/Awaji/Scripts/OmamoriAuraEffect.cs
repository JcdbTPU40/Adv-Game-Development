
using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class OmamoriAuraEffect : MonoBehaviour
{
    [Header("オーラの色")]
    [SerializeField] private Color auraColor = new Color(1f, 0.8f, 0.35f, 1f);

    [Header("粒子の設定")]
    [SerializeField, Min(1)] private int particlesPerSecond = 18;
    [SerializeField, Min(0.01f)] private float particleLifetime = 1.2f;
    [SerializeField, Min(0.01f)] private float particleSize = 0.08f;
    [SerializeField, Min(0f)] private float spreadRadius = 0.25f;
    [SerializeField, Min(0f)] private float riseSpeed = 0.18f;
    [SerializeField, Min(0f)] private float outwardSpeed = 0.12f;

    private ParticleSystem ps;

    private void Awake()
    {
        ps = GetComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = true;
        main.startLifetime = particleLifetime;
        main.startSize = particleSize;
        main.startColor = auraColor;
        main.startSpeed = outwardSpeed;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 300;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = particlesPerSecond;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = spreadRadius;
        shape.radiusThickness = 0.15f;

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.y = riseSpeed;

        var color = ps.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(
            new Gradient
            {
                colorKeys = new GradientColorKey[]
                {
                    new GradientColorKey(auraColor, 0f),
                    new GradientColorKey(auraColor, 1f)
                },
                alphaKeys = new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.8f, 0.15f),
                    new GradientAlphaKey(0f, 1f)
                }
            }
        );

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.6f;
        noise.scrollSpeed = 0.3f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        ps.Play();
    }

    public void SetAuraColor(Color color)
    {
        auraColor = color;

        if (ps == null)
            return;

        var main = ps.main;
        main.startColor = auraColor;

        var colorOverLifetime = ps.colorOverLifetime;
        if (colorOverLifetime.enabled)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(auraColor, 0f),
                    new GradientColorKey(auraColor, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.8f, 0.15f),
                    new GradientAlphaKey(0f, 1f)
                }
            );

            colorOverLifetime.color =
                new ParticleSystem.MinMaxGradient(gradient);
        }
    }
}
