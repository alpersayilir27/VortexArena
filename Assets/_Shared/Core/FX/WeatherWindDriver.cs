using UnityEngine;

namespace VortexArena.Core.FX
{
    /// <summary>
    /// Overlays a wind that varies over time onto the snow/dust layers.
    /// <para>
    /// Why it is needed: snow blowing in one direction at a constant speed looks "pasted on" — a real
    /// storm breathes. Three things oscillate together via Perlin noise: the wind's STRENGTH, its
    /// DIRECTION (yaw) and the amount of turbulence. Because they are fed from a single noise channel,
    /// the increased scattering happens at the same time as the wind picks up; that single detail is
    /// what brings the effect to life the most.
    /// </para>
    /// Attached to the root object; it drives ALL particle systems beneath it. Each system's
    /// <c>Velocity over Lifetime</c> XZ values are taken as the baseline in <see cref="Awake"/>, then
    /// that baseline is rotated and scaled every frame — so the relative speed difference between the
    /// layers is preserved.
    /// <para>
    /// ⚠️ If you want to change the layers' relative wind, edit the systems' own Velocity over
    /// Lifetime values; the fields here only tune the oscillation.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VortexArena/FX/Weather Wind Driver")]
    public class WeatherWindDriver : MonoBehaviour
    {
        [Tooltip("Salınım hızı (Hz'e yakın). Düşük = uzun, ağır hamleler.")]
        [SerializeField] private float gustFrequency = 0.055f;

        [Tooltip("Rüzgar şiddetinin temel değerden sapması. 0.45 = 0.55x ile 1.45x arası.")]
        [SerializeField, Range(0f, 0.9f)] private float gustAmount = 0.45f;

        [Tooltip("Rüzgar yönünün yatayda sağa/sola savrulma genliği (derece).")]
        [SerializeField, Range(0f, 60f)] private float directionSwingDegrees = 24f;

        [Tooltip("Rüzgar sertleşirken Noise şiddetinin de artma miktarı.")]
        [SerializeField, Range(0f, 1f)] private float turbulenceGustAmount = 0.5f;

        private ParticleSystem[] systems;
        private float[] baseXLo;
        private float[] baseXHi;
        private float[] baseZLo;
        private float[] baseZHi;
        private float[] baseNoise;
        private bool[] driven;

        private void Awake()
        {
            systems = GetComponentsInChildren<ParticleSystem>(true);
            int count = systems.Length;
            baseXLo = new float[count];
            baseXHi = new float[count];
            baseZLo = new float[count];
            baseZHi = new float[count];
            baseNoise = new float[count];
            driven = new bool[count];

            for (int i = 0; i < count; i++)
            {
                ParticleSystem.VelocityOverLifetimeModule velocity = systems[i].velocityOverLifetime;
                driven[i] = velocity.enabled;
                baseXLo[i] = velocity.x.constantMin;
                baseXHi[i] = velocity.x.constantMax;
                baseZLo[i] = velocity.z.constantMin;
                baseZHi[i] = velocity.z.constantMax;
                baseNoise[i] = systems[i].noise.strengthMultiplier;
            }
        }

        private void Update()
        {
            if (systems == null)
            {
                return;
            }

            float time = Time.time;
            // One noise channel drives the strength, a second independent channel drives the direction.
            float gust = Mathf.PerlinNoise(time * gustFrequency, 0.37f);
            float swing = Mathf.PerlinNoise(time * gustFrequency * 0.63f, 8.11f);

            float speedMultiplier = Mathf.Lerp(1f - gustAmount, 1f + gustAmount, gust);
            float noiseMultiplier = Mathf.Lerp(1f - turbulenceGustAmount, 1f + turbulenceGustAmount, gust);
            float yaw = (swing - 0.5f) * 2f * directionSwingDegrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(yaw);
            float cos = Mathf.Cos(yaw);

            for (int i = 0; i < systems.Length; i++)
            {
                if (!driven[i])
                {
                    continue;
                }

                // Rotate + scale the baseline direction; the width of the min/max range is preserved
                // proportionally.
                float xMid = (baseXLo[i] + baseXHi[i]) * 0.5f;
                float zMid = (baseZLo[i] + baseZHi[i]) * 0.5f;
                float xSpread = (baseXHi[i] - baseXLo[i]) * 0.5f * speedMultiplier;
                float zSpread = (baseZHi[i] - baseZLo[i]) * 0.5f * speedMultiplier;
                float x = (xMid * cos - zMid * sin) * speedMultiplier;
                float z = (xMid * sin + zMid * cos) * speedMultiplier;

                ParticleSystem.VelocityOverLifetimeModule velocity = systems[i].velocityOverLifetime;
                velocity.x = new ParticleSystem.MinMaxCurve(x - xSpread, x + xSpread);
                velocity.z = new ParticleSystem.MinMaxCurve(z - zSpread, z + zSpread);

                ParticleSystem.NoiseModule noise = systems[i].noise;
                if (noise.enabled)
                {
                    noise.strengthMultiplier = baseNoise[i] * noiseMultiplier;
                }
            }
        }
    }
}
