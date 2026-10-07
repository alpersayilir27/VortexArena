using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Girdap health strip (CSS <c>.hpbar</c>): segment bar + value + "CAN" label/icon, tinted by
    /// the CSS thresholds (&gt;50 good, &gt;20 warn, else bad). Presentation only — fed by
    /// <see cref="ModeHudBase"/>.
    /// </summary>
    public class HealthStrip : MonoBehaviour
    {
        [SerializeField] private UiSegmentBar bar;
        [SerializeField] private TMP_Text value;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image icon;

        private int _tone = -1; // 0 good · 1 warn · 2 bad

        /// <summary>Draws <paramref name="hp"/> of <paramref name="max"/>; a max ≤ 0 draws empty.</summary>
        public void SetHp(float hp, float max)
        {
            float n = max > 0f ? Mathf.Clamp01(hp / max) : 0f;
            if (bar != null)
            {
                bar.SetFill(n);
            }

            if (value != null)
            {
                value.text = Mathf.RoundToInt(n * max).ToString();
            }

            int tone = n > 0.5f ? 0 : n > 0.2f ? 1 : 2;
            if (tone == _tone)
            {
                return;
            }

            _tone = tone;
            if (bar != null)
            {
                bar.SetFillColors(
                    tone == 0 ? Girdap.HpA : tone == 1 ? Girdap.HpWarnA : Girdap.HpBadA,
                    tone == 0 ? Girdap.Good : tone == 1 ? Girdap.Warn : Girdap.Bad);
            }

            if (value != null)
            {
                value.color = tone == 0 ? Girdap.Text : tone == 1 ? Girdap.Warn : Girdap.Bad;
                // CSS `.hpbar.bad .val { text-shadow: 0 0 14px rgba(bad, .55) }` — Underlay preset
                // from the builder; only its colour moves here.
                Color halo = Girdap.Bad;
                halo.a = tone == 2 ? 0.55f : 0f;
                value.fontMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, halo);
            }

            Color side = tone == 2 ? Girdap.Bad : Girdap.Muted;
            if (label != null)
            {
                label.color = side;
            }

            if (icon != null)
            {
                icon.color = side;
            }
        }
    }
}
