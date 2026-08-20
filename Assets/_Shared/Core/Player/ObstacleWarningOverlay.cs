using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>Pulsing warning text shown over the blackout while inside an obstacle: the player must
    /// know why the screen went dark and what to do.</summary>
    /// <remarks>
    /// <b>Measures nothing</b> — reads <see cref="ObstacleViolationProbe"/>'s static state. Its alpha
    /// follows the probe's blackout (<see cref="ObstacleViolationProbe.FadeAlpha"/>), so the text arrives
    /// with the darkening, never before it.
    /// <para>It sits <b>nearer</b> than the blackout quad (z ≈ 0.42 vs quad 0.5 in the prefab), so
    /// transparent sorting draws it on top — no separate queue/sorting setting needed.
    /// (<see cref="DamageVignette"/> solves the same problem differently: it relies on the <c>Overlay</c>
    /// queue rather than distance, because it must be above everything.)</para>
    /// <para>Owned by the <c>VA_CameraRig</c> prefab. Never runs on an admin observer, whose rig root is
    /// disabled.</para>
    /// </remarks>
    [DefaultExecutionOrder(30200)]
    public class ObstacleWarningOverlay : MonoBehaviour
    {
        [Tooltip("Uyarı yazısı (bu objenin kendi TextMesh'i).")]
        [SerializeField] private TextMesh warningText;

        /// <summary>Pulse frequency (Hz) — slow, so the text stays readable.</summary>
        private const float PulseHz = 1.6f;

        /// <summary>Lower end of the pulse: the text never disappears, it only fades.</summary>
        private const float MinPulseAlpha = 0.55f;

        /// <summary>Amplitude of the scale breathing (ratio).</summary>
        private const float ScaleBreathe = 0.04f;

        private Renderer _renderer;
        private Vector3 _baseScale;

        private void Awake()
        {
            if (warningText == null)
            {
                warningText = GetComponent<TextMesh>();
            }

            _renderer = warningText != null ? warningText.GetComponent<Renderer>() : null;
            _baseScale = transform.localScale;
            SetVisible(false);
        }

        private void OnDisable()
        {
            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (warningText == null)
            {
                return;
            }

            // ⚠️ The gate contains neither phase nor aliveness: the text EXPLAINS the blackout, and the
            // blackout runs in every situation (rationale in ObstacleViolationProbe.ReportPresentation).
            // Silencing it while dead would leave a pitch-black screen unexplained — and since there is no
            // revive inside an obstacle (§10.9), this is exactly the instruction the player needs.
            bool show = ObstacleViolationProbe.IsViolating;
            if (!show)
            {
                SetVisible(false);
                return;
            }

            // The blackout itself arrives in 0.2 s; the text appears together with it.
            float fade = Mathf.Clamp01(ObstacleViolationProbe.FadeAlpha);
            float pulse = Mathf.Lerp(MinPulseAlpha, 1f,
                0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * PulseHz * 2f * Mathf.PI));

            Color color = warningText.color;
            color.a = fade * pulse;
            warningText.color = color;

            transform.localScale = _baseScale * (1f + ScaleBreathe * (pulse - MinPulseAlpha));

            SetVisible(color.a > 0.01f);
        }

        private void SetVisible(bool on)
        {
            if (_renderer != null)
            {
                _renderer.enabled = on;
            }
        }
    }
}
