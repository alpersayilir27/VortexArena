using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>Red damage vignette on the HMD: pulses on health loss, steady frame at low health.</summary>
    /// <remarks>
    /// ⚠️ <b>Must NOT be added as a <see cref="ScreenFade"/> source.</b> The arbiter picks the highest
    /// alpha, so a 0.5 red always loses to a 1.0 obstacle blackout and the player never sees their
    /// health draining on a pitch-black screen. Hence its own renderer, drawn ON TOP of the fade quad
    /// (<c>Overlay</c> queue, <c>ZTest Always</c>).
    /// <para>Not obstacle-specific — bullet damage looks the same. Obstacle health is reported at ~4 Hz,
    /// so it pulses rhythmically there.</para>
    /// <para>Sampled, not subscribed: health is state and this is presentation; per-frame diffing avoids
    /// managing a subscription lifetime tied to singleton birth order.</para>
    /// <para>Owned by the <c>VA_CameraRig</c> prefab (under CenterEyeAnchor, in front of the fade quad).
    /// Never runs on an admin observer, whose rig root is disabled.</para>
    /// </remarks>
    [DefaultExecutionOrder(30200)]
    public class DamageVignette : MonoBehaviour
    {
        [Tooltip("Vinyet quad'ının renderer'ı (bu objenin kendi MeshRenderer'ı).")]
        [SerializeField] private Renderer vignetteRenderer;

        /// <summary>Damage in one packet that means a full pulse (HP).</summary>
        private const float PulseFullDamage = 25f;

        /// <summary>Pulse decay rate (units/s).</summary>
        private const float PulseDecayPerSecond = 2f;

        /// <summary>Pulse ceiling — never paints the screen fully red, the player must keep seeing.</summary>
        private const float MaxPulseAlpha = 0.55f;

        /// <summary>Below this health the steady vignette appears (HP).</summary>
        private const float LowHpThreshold = 40f;

        /// <summary>Ceiling of the steady (low health) vignette.</summary>
        private const float MaxLowHpAlpha = 0.35f;

        private static readonly Color VignetteColor = new Color(0.75f, 0.03f, 0.03f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _propertyBlock;
        private float _pulse;
        private float _lastHp = ArenaProtocol.PLAYER_MAX_HP;

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
            _pulse = 0f;
            Draw(0f);
        }

        private void LateUpdate()
        {
            float hp = ArenaCombat.LocalHp;

            // ⚠️ Only a DROP pulses: revive (0 → 100) and healing stay silent.
            float drop = _lastHp - hp;
            _lastHp = hp;
            if (drop > 0f)
            {
                _pulse = Mathf.Clamp01(_pulse + drop / PulseFullDamage);
            }

            _pulse = Mathf.Max(0f, _pulse - PulseDecayPerSecond * Time.unscaledDeltaTime);

            if (!ArenaCombat.IsAlive)
            {
                // The death overlay presents itself; a red frame on top adds no information.
                _pulse = 0f;
                Draw(0f);
                return;
            }

            float lowHp = hp < LowHpThreshold
                ? Mathf.InverseLerp(LowHpThreshold, 0f, hp) * MaxLowHpAlpha
                : 0f;

            Draw(Mathf.Max(_pulse * MaxPulseAlpha, lowHp));
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
            Color color = VignetteColor;
            color.a = alpha;
            _propertyBlock.SetColor(BaseColorId, color);
            vignetteRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
