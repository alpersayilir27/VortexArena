using TMPro;
using UnityEngine;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// One line under the health bar for the round that just closed ("TUR KAZANILDI"), shown for a few
    /// seconds and then gone.
    ///
    /// <para><b>Presentation only:</b> the text is written by the mode, this class only holds it for
    /// <see cref="visibleSeconds"/> and tints it by <see cref="RoundOutcome"/>.</para>
    /// </summary>
    /// <remarks>
    /// <para>Under the bar and not in the middle of the view on purpose: the centre belongs to the
    /// regroup notice, which starts in the very same second (<see cref="ModeHudBase.SetCenterNotice"/>).
    /// Two big texts fighting for one spot would leave the player reading neither.</para>
    /// <para>⚠️ It carries its OWN <c>Canvas</c> with <c>Override Sorting</c>: the health strip is drawn
    /// UNDER the rest of the HUD (sorting order −1) so the bar cannot hang over the opaque death
    /// screen — but the round result is exactly what a player who died must still see.</para>
    /// <para>⚠️ It toggles a CHILD, never its own GameObject: <c>Update</c> is what takes the banner
    /// down, and a deactivated object never ticks again.</para>
    /// </remarks>
    public class RoundResultBanner : MonoBehaviour
    {
        [Tooltip("Açılıp kapanan içerik kökü — bileşenin KENDİ nesnesi olmamalı.")]
        [SerializeField] private GameObject content;
        [SerializeField] private TMP_Text label;
        [Tooltip("Şeridin ekranda kalma süresi (sn).")]
        [SerializeField] private float visibleSeconds = 3f;

        [Header("Renkler")]
        [SerializeField] private Color wonColor = new Color(0.35f, 0.90f, 0.40f);
        [SerializeField] private Color lostColor = new Color(0.90f, 0.30f, 0.30f);
        [Tooltip("Beraberlik ve takımı olmayan izleyici için nötr ton.")]
        [SerializeField] private Color drawColor = new Color(0.95f, 0.85f, 0.35f);

        /// <summary>Unscaled time the banner comes down at; negative = not showing.</summary>
        private float _hideAt = -1f;

        private void Awake()
        {
            Apply(false);
        }

        private void OnDisable()
        {
            // Scene/HUD going away mid-banner: the next match must not open with a stale line.
            Hide();
        }

        private void Update()
        {
            if (_hideAt < 0f || Time.unscaledTime < _hideAt)
            {
                return;
            }

            Hide();
        }

        /// <summary>Shows one line for <see cref="visibleSeconds"/>; an empty text takes it down.</summary>
        public void Show(string text, RoundOutcome outcome)
        {
            if (string.IsNullOrEmpty(text))
            {
                Hide();
                return;
            }

            if (label != null)
            {
                label.text = text;
                label.color = ColorOf(outcome);
            }

            // Restarted on every call: a second round result arriving while the first is still up must
            // get its own full reading time, not the remainder of the previous one.
            _hideAt = Time.unscaledTime + Mathf.Max(0.5f, visibleSeconds);
            Apply(true);
        }

        public void Hide()
        {
            _hideAt = -1f;
            Apply(false);
        }

        private Color ColorOf(RoundOutcome outcome)
        {
            switch (outcome)
            {
                case RoundOutcome.Won: return wonColor;
                case RoundOutcome.Lost: return lostColor;
                default: return drawColor;
            }
        }

        private void Apply(bool visible)
        {
            if (content != null)
            {
                content.SetActive(visible);
            }
        }
    }
}
