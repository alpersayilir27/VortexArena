using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.UI;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// One violation feed row (CSS <c>.vf</c> / <c>.vf.live</c>).
    /// <para>⚠️ <b>The live row is deliberately loud</b> (filled plate, bigger text, hazard stripes,
    /// a blink): it is the only item on the HUD that asks the operator to physically go do something,
    /// and a finished violation next to it must not look equally urgent.</para>
    /// <para>Severity colors come from <see cref="AdminViolations"/> — the ring, the player card and
    /// this row must not disagree about how bad a violation is.</para>
    /// </summary>
    public class AdminViolationFeedRow : MonoBehaviour
    {
        /// <summary>CSS <c>.vf</c> height.</summary>
        public const float Height = 36f;

        /// <summary>CSS <c>.vf.live</c> height.</summary>
        public const float LiveHeight = 50f;

        private const float Pad = 12f;
        private const float Gap = 8f;

        /// <summary>CSS <c>.vf.live { padding-right: 72px }</c> — room for the hazard stripes.</summary>
        private const float LivePadRight = 72f;

        private const float IconSize = 18f;
        private const float LiveIconSize = 24f;

        /// <summary>Blink rate of the live row (CSS <c>pulse 1s steps(2)</c>).</summary>
        private const float BlinkHz = 1f;

        /// <summary>Brightness of the blink's dim half (CSS <c>filter: brightness(.62)</c>).</summary>
        private const float BlinkDim = 0.62f;

        [SerializeField] private RectTransform root;
        [SerializeField] private UiShape shape;

        [Tooltip("Canlı ihlalin sağ kenarındaki tehlike şeritleri; yalnız canlı satırda görünür.")]
        [SerializeField] private UiStripes stripes;

        [SerializeField] private Image icon;

        [Tooltip("Oyuncu adı (CSS .vf b) — satırın kalın parçası.")]
        [SerializeField] private TextMeshProUGUI nameLabel;

        [Tooltip("Adın devamı: \"— DUVAR\" / \"— ALAN DIŞI bitti (3.4 sn)\".")]
        [SerializeField] private TextMeshProUGUI restLabel;

        private bool _live;
        private Color _nameColor;
        private Color _restColor;
        private Color _iconColor;

        /// <summary>Is this row the player's CURRENT violation (the only kind that blinks).</summary>
        public bool IsLive => _live;

        /// <summary>
        /// Fills the row. <paramref name="live"/> means "this is still happening" — the newest entry
        /// of an ongoing violation; an older start line whose end already arrived is drawn calm.
        /// </summary>
        public void Bind(AdminViolationFeedEntry entry, bool live, float top)
        {
            if (entry == null || root == null)
            {
                return;
            }

            _live = live;
            float h = live ? LiveHeight : Height;
            float iconSize = live ? LiveIconSize : IconSize;

            nameLabel.text = entry.name;
            restLabel.text = RestLine(entry);

            if (live)
            {
                LiveSurface(AdminViolations.Kind(entry.kind));
            }
            else
            {
                CalmSurface();
            }

            nameLabel.fontSize = live ? 26f : 18f;
            restLabel.fontSize = live ? 26f : 18f;
            nameLabel.characterSpacing = live ? Girdap.Spacing(0.03f) : 0f;
            restLabel.characterSpacing = nameLabel.characterSpacing;

            if (icon != null)
            {
                // The calm row carries no icon: CSS only puts one on `.vf.live`.
                icon.gameObject.SetActive(live);
                icon.sprite = Girdap.Icon("Warn");
            }

            if (stripes != null)
            {
                stripes.gameObject.SetActive(live);
            }

            float nameWidth = Mathf.Ceil(nameLabel.GetPreferredValues(nameLabel.text).x);
            float restWidth = Mathf.Ceil(restLabel.GetPreferredValues(restLabel.text).x);

            float cursor = Pad;
            if (live)
            {
                RectTransform iconRect = icon.rectTransform;
                iconRect.sizeDelta = new Vector2(iconSize, iconSize);
                iconRect.anchoredPosition = new Vector2(cursor, 0f);
                cursor += iconSize + Gap;
            }

            nameLabel.rectTransform.anchoredPosition = new Vector2(cursor, 0f);
            nameLabel.rectTransform.sizeDelta = new Vector2(nameWidth, h);
            cursor += nameWidth + Gap;

            restLabel.rectTransform.anchoredPosition = new Vector2(cursor, 0f);
            restLabel.rectTransform.sizeDelta = new Vector2(restWidth, h);
            cursor += restWidth + (live ? LivePadRight : Pad);

            root.anchoredPosition = new Vector2(0f, -top);
            root.sizeDelta = new Vector2(cursor, h);
            ApplyBlink(false);
        }

        /// <summary>Drives the live row's blink; a no-op on a calm row, so the view can call it blind.</summary>
        public void TickBlink()
        {
            if (!_live)
            {
                return;
            }

            ApplyBlink(Mathf.Repeat(Time.unscaledTime * BlinkHz, 1f) >= 0.5f);
        }

        private void LiveSurface(AdminViolationKind kind)
        {
            // Only out-of-bounds is the softer amber; an unknown kind stays red — the operator should
            // look at something the client does not understand, not ignore it.
            bool soft = kind == AdminViolationKind.OutOfBounds;
            Color bd = soft ? Girdap.WarnBd : Girdap.ConfirmBd;
            Color a = soft ? Girdap.WarnA : Girdap.ConfirmA;
            Color b = soft ? Girdap.WarnB : Girdap.ConfirmB;
            Color fg = soft ? Girdap.WarnFg : Girdap.ConfirmFg;

            shape.Chamfer(10f).Outline(1f, bd).Fill(a, b, UiGradientMode.Vertical);
            _nameColor = Girdap.Ink;
            _restColor = fg;
            _iconColor = fg;
        }

        private void CalmSurface()
        {
            shape.Chamfer(7f).Outline(1f, Girdap.EdgeSoft).Fill(Girdap.SegBg);
            _nameColor = Girdap.Text;
            _restColor = Girdap.Muted;
            _iconColor = Girdap.Muted;
        }

        private void ApplyBlink(bool dim)
        {
            float k = dim ? BlinkDim : 1f;
            nameLabel.color = Scale(_nameColor, k);
            restLabel.color = Scale(_restColor, k);

            if (icon != null)
            {
                icon.color = Scale(_iconColor, k);
            }

            // UiPolygonGraphic multiplies its vertex colors by Graphic.color, so one assignment dims
            // border and fill together. The stripes stay black — dimming black is a no-op.
            shape.color = new Color(k, k, k, 1f);
        }

        private static Color Scale(Color c, float k)
        {
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        private static string RestLine(AdminViolationFeedEntry entry)
        {
            string label = AdminViolations.Label(entry.kind);
            return entry.active
                ? $"— {label}"
                : $"— {label} bitti ({entry.seconds:0.0} sn)";
        }
    }
}
