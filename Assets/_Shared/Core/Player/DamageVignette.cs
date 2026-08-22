using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>Red damage vignette on the HMD: rises fast on health loss, decays with an ease-out
    /// curve, and breathes at low health. The centre stays fully clear (the shader's radial ramp) —
    /// the player is physically walking and blocking their view is a safety problem.</summary>
    /// <remarks>
    /// ⚠️ <b>Must NOT be added as a <see cref="ScreenFade"/> source.</b> The arbiter picks the highest
    /// alpha, so a 0.4 red always loses to a 1.0 obstacle blackout and the player never sees their
    /// health draining on a pitch-black screen. Hence its own renderer, drawn ON TOP of the fade quad
    /// (<c>Overlay</c> queue, <c>ZTest Always</c>).
    /// <para>Not obstacle-specific — bullet damage looks the same. Obstacle health is reported at ~4 Hz,
    /// so repeated reports keep refreshing the envelope and the frame stays lit while inside.</para>
    /// <para>Sampled, not subscribed: health is state and this is presentation; per-frame diffing avoids
    /// managing a subscription lifetime tied to singleton birth order. The DIRECTION half of the
    /// feedback does subscribe, because it needs <c>attackerId</c> — see
    /// <see cref="DamageDirectionIndicator"/>.</para>
    /// <para>Owned by the <c>VA_CameraRig</c> prefab (under CenterEyeAnchor, in front of the fade quad).
    /// Never runs on an admin observer, whose rig root is disabled.</para>
    /// </remarks>
    [DefaultExecutionOrder(30200)]
    public class DamageVignette : MonoBehaviour
    {
        [Header("Genel")]
        [Tooltip("Ana anahtar — kapalıyken vinyet hiç çizilmez.")]
        [SerializeField] private bool effectEnabled = true;

        [Tooltip("Vinyet quad'ının renderer'ı (bu objenin kendi MeshRenderer'ı).")]
        [SerializeField] private Renderer vignetteRenderer;

        [Header("Görünüm")]
        [Tooltip("Vinyet rengi (#8E1F1F). Saf kırmızı DEĞİL: doygunluğu düşürülmüş koyu kırmızı gözü yormaz.")]
        [SerializeField] private Color vignetteColor = new Color(0.5569f, 0.1216f, 0.1216f);

        [Tooltip("Kenardaki en yüksek opaklık. Merkezin temiz kalmasını shader'ın yarıçapları sağlar.")]
        [Range(0f, 1f)]
        [SerializeField] private float maxAlpha = 0.40f;

        [Header("Zamanlama")]
        [Tooltip("Vuruştan sonra tepe opaklığa çıkma süresi (sn).")]
        [Range(0f, 1f)]
        [SerializeField] private float attackSeconds = 0.06f;

        [Tooltip("Tepeden sıfıra sönme süresi (sn). Eğri sona doğru yavaşlar.")]
        [Range(0.05f, 3f)]
        [SerializeField] private float decaySeconds = 0.40f;

        [Tooltip("Tek pakette tam yoğunluk sayılan hasar (HP).")]
        [SerializeField] private float fullIntensityDamage = 25f;

        [Header("Düşük can")]
        [Tooltip("Bu can oranının ALTINDA vinyet tamamen sönmez, yavaşça nabız atar (0..1).")]
        [Range(0f, 1f)]
        [SerializeField] private float lowHpThreshold = 0.30f;

        [Tooltip("Düşük can nabzının tepe opaklığı (can sıfıra yaklaşırken).")]
        [Range(0f, 1f)]
        [SerializeField] private float lowHpAlpha = 0.16f;

        [Tooltip("Eşiğin hemen altında tepe opaklığın ne kadarı görünsün. 0 olsaydı eşiği yeni geçen " +
                 "oyuncu hiçbir şey görmezdi — 'tamamen sönmesin' şartı tam olarak budur.")]
        [Range(0f, 1f)]
        [SerializeField] private float lowHpEntryScale = 0.5f;

        [Tooltip("Nabzın dip noktası (tepe opaklığın oranı). 0 olsaydı vinyet tamamen sönerdi.")]
        [Range(0f, 1f)]
        [SerializeField] private float lowHpPulseFloor = 0.55f;

        [Tooltip("Düşük can nabzının hızı (Hz). ⚠️ Konfor tavanı saniyede 3 yanıp sönmedir.")]
        [Range(0.05f, 3f)]
        [SerializeField] private float lowHpPulseHz = 0.35f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _propertyBlock;
        private float _lastHp = ArenaProtocol.PLAYER_MAX_HP;

        // Hit envelope. A new hit REFRESHES these instead of adding a second envelope: stacked
        // envelopes would push the screen past maxAlpha exactly when the player can least afford it.
        private float _hitTime = float.NegativeInfinity;
        private float _hitPeak;
        private float _hitFrom;

        private void Awake()
        {
            if (vignetteRenderer == null)
            {
                vignetteRenderer = GetComponent<Renderer>();
            }

            _propertyBlock = new MaterialPropertyBlock();
            _lastHp = ArenaCombat.LocalHp;
            Draw(0f);
        }

        private void OnDisable()
        {
            _hitPeak = 0f;
            Draw(0f);
        }

        private void LateUpdate()
        {
            if (!effectEnabled)
            {
                Draw(0f);
                return;
            }

            float hp = ArenaCombat.LocalHp;

            // ⚠️ Only a DROP fires: revive (0 → 100) and healing stay silent.
            float drop = _lastHp - hp;
            _lastHp = hp;
            if (drop > 0f)
            {
                RegisterHit(drop);
            }

            if (!ArenaCombat.IsAlive)
            {
                // The death overlay presents itself; a red frame on top adds no information.
                _hitPeak = 0f;
                Draw(0f);
                return;
            }

            Draw(Mathf.Max(HitEnvelope() * maxAlpha, LowHpAlpha(hp)));
        }

        /// <summary>Refreshes the envelope: the intensity takes the HIGHER of the running level and this
        /// hit, and the decay clock restarts. Starting the attack from the CURRENT level (not from 0)
        /// is what keeps a second hit from dipping the screen before it brightens.</summary>
        private void RegisterHit(float damage)
        {
            float current = HitEnvelope();
            float intensity = fullIntensityDamage > 0f
                ? Mathf.Clamp01(damage / fullIntensityDamage)
                : 1f;

            _hitFrom = current;
            _hitPeak = Mathf.Max(current, intensity);
            _hitTime = Time.unscaledTime;
        }

        /// <summary>Attack then ease-out decay, 0..1. unscaledTime: presentation must keep its timing
        /// even if Time.timeScale is played with (same rationale as <see cref="ScreenFade"/>).</summary>
        private float HitEnvelope()
        {
            if (_hitPeak <= 0f)
            {
                return 0f;
            }

            float elapsed = Time.unscaledTime - _hitTime;
            if (elapsed <= 0f)
            {
                return _hitFrom;
            }

            if (elapsed < attackSeconds)
            {
                return Mathf.Lerp(_hitFrom, _hitPeak, elapsed / attackSeconds);
            }

            if (decaySeconds <= 0f)
            {
                return 0f;
            }

            float u = (elapsed - attackSeconds) / decaySeconds;
            if (u >= 1f)
            {
                return 0f;
            }

            // Squared falloff: quick at first, flattening toward the end (zero slope at u = 1).
            float remaining = 1f - u;
            return _hitPeak * remaining * remaining;
        }

        /// <summary>Steady low-health breathing. Below the threshold it NEVER reaches zero: it enters at
        /// <see cref="lowHpEntryScale"/> of the ceiling and grows toward it as death approaches, and the
        /// breath swings only down to <see cref="lowHpPulseFloor"/> of that. Two floors, two jobs — the
        /// entry one keeps "you are in danger" visible, the pulse one keeps it from blinking OFF.</summary>
        private float LowHpAlpha(float hp)
        {
            if (lowHpThreshold <= 0f || ArenaProtocol.PLAYER_MAX_HP <= 0f)
            {
                return 0f;
            }

            float fraction = hp / ArenaProtocol.PLAYER_MAX_HP;
            if (fraction >= lowHpThreshold)
            {
                return 0f;
            }

            float severity = Mathf.InverseLerp(lowHpThreshold, 0f, fraction);
            float breath = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * lowHpPulseHz * 2f * Mathf.PI);
            return lowHpAlpha
                   * Mathf.Lerp(lowHpEntryScale, 1f, severity)
                   * Mathf.Lerp(lowHpPulseFloor, 1f, breath);
        }

        private void Draw(float alpha)
        {
            if (vignetteRenderer == null)
            {
                return;
            }

            bool visible = alpha > 0.001f;
            vignetteRenderer.enabled = visible;
            if (!visible)
            {
                return;
            }

            _propertyBlock ??= new MaterialPropertyBlock();
            vignetteRenderer.GetPropertyBlock(_propertyBlock);

            // ⚠️ .linear is REQUIRED: the project renders in Linear space but a serialized Color holds
            // what the picker showed (sRGB), and SetColor does no conversion. Without it #8E1F1F
            // reaches the shader far too bright and the "desaturated" part of the colour is lost.
            Color color = vignetteColor.linear;
            color.a = alpha;
            _propertyBlock.SetColor(BaseColorId, color);
            vignetteRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
