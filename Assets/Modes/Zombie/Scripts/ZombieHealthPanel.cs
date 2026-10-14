using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie HUD's own health panel: the big number, the damage trail behind the bar and
    /// the low-health pulse.</summary>
    /// <remarks>
    /// Built and bound by <c>GirdapUiBuilder.ZombieHud.cs</c> (fields written BY NAME through
    /// SerializedObject — a rename here unbinds silently).
    /// <para>The value is read from <see cref="source"/>'s <c>fillAmount</c>, the Image
    /// <c>ModeHudBase.healthFill</c> drives: one writer for health, one rounding, no second subscription
    /// to combat.</para>
    /// <para>⚠️ Allocation-free per frame (72 Hz headset): the number comes from a prebuilt label table
    /// and the pulse goes through <c>CanvasRenderer.SetAlpha</c>, which does not rebuild the mesh.</para>
    /// </remarks>
    public class ZombieHealthPanel : MonoBehaviour
    {
        [Header("Bağlar")]
        [Tooltip("Tabanın sürdüğü can barı (healthFill) — değerin kaynağı.")]
        [SerializeField] private Image source;
        [Tooltip("Dolgunun arkasındaki hasar izi (Filled Image).")]
        [SerializeField] private Image trail;
        [Tooltip("Büyük can sayısı.")]
        [SerializeField] private TMP_Text valueText;
        [Tooltip("Panelin arkasındaki parıltı — yalnız nabızda görünür.")]
        [SerializeField] private Graphic glow;
        [Tooltip("Barın üstündeki nabız halkası.")]
        [SerializeField] private Graphic pulse;

        [Header("Davranış")]
        [Tooltip("İzin güncel cana inme süresi (sn, barın tamamı için).")]
        [SerializeField] private float trailSeconds = 0.6f;
        [Tooltip("Bu oranın altında panel nabız atar.")]
        [SerializeField] private float lowFraction = 0.3f;
        [Tooltip("Nabız hızı (Hz).")]
        [SerializeField] private float pulseHz = 1.4f;

        private string[] _labels;
        private float _amount = -1f;
        private float _trail;
        private float _trailShown = -1f;
        private float _pulseAlpha = -1f;

        private void Awake()
        {
            int max = Mathf.Max(1, Mathf.RoundToInt(ArenaProtocol.PLAYER_MAX_HP));
            _labels = new string[max + 1];
            for (int i = 0; i <= max; i++)
            {
                _labels[i] = i.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void OnEnable()
        {
            // Respawn: the previous life's trail would drain across the bar on the first frame.
            _amount = -1f;
            _trailShown = -1f;
            _pulseAlpha = -1f;
            _trail = source != null ? Mathf.Clamp01(source.fillAmount) : 1f;
        }

        // After the base's HpChanged handler: reading a value the same frame it is written.
        private void LateUpdate()
        {
            if (source == null)
            {
                return;
            }

            float amount = Mathf.Clamp01(source.fillAmount);
            if (amount != _amount)
            {
                if (amount > _amount)
                {
                    _trail = amount; // healing has no trail: it would read as damage
                }

                _amount = amount;
                WriteNumber(amount);
            }

            if (_trail > _amount)
            {
                _trail = Mathf.Max(_amount, _trail - Time.deltaTime / Mathf.Max(0.01f, trailSeconds));
            }

            SetTrail(_trail);
            SetPulse(amount);
        }

        private void WriteNumber(float amount)
        {
            if (valueText == null || _labels == null)
            {
                return;
            }

            int hp = Mathf.Clamp(Mathf.RoundToInt(amount * ArenaProtocol.PLAYER_MAX_HP), 0,
                _labels.Length - 1);
            valueText.text = _labels[hp];
        }

        private void SetTrail(float value)
        {
            if (trail == null || value == _trailShown)
            {
                return;
            }

            _trailShown = value;
            trail.fillAmount = value;
        }

        /// <summary>Low health breathes; a dead player does not — a pulsing 0 looks like a bug.</summary>
        private void SetPulse(float amount)
        {
            float alpha = 0f;
            if (amount > 0f && amount < lowFraction)
            {
                alpha = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseHz * 2f * Mathf.PI);
            }

            if (alpha == _pulseAlpha)
            {
                return;
            }

            _pulseAlpha = alpha;
            if (glow != null)
            {
                glow.canvasRenderer.SetAlpha(alpha);
            }

            if (pulse != null)
            {
                pulse.canvasRenderer.SetAlpha(alpha);
            }
        }
    }
}
