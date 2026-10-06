using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Status badge (CSS <c>.chip</c>): 24 px tall, 5 px chamfer, 8 px side padding, optional 14 px
    /// icon with a 5 px gap.
    /// <para>
    /// ⚠️ <b>Width is computed, not laid out:</b> the kit forbids Layout Groups (no reflow, no
    /// drift), so <see cref="Set"/> measures the label and writes <c>sizeDelta.x</c> itself.
    /// </para>
    /// </summary>
    [AddComponentMenu("VortexArena/UI/Chip")]
    public class UiChip : MonoBehaviour
    {
        /// <summary>CSS <c>.chip</c> height.</summary>
        public const float Height = 24f;

        /// <summary>CSS <c>.chip</c> side padding; zero for the surfaceless kinds.</summary>
        public const float Padding = 8f;

        public const float IconSize = 14f;
        public const float IconGap = 5f;

        [SerializeField] private UiShape shape;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image icon;
        [SerializeField] private RectTransform rect;

        // Not named `Rect`: a member with a type's name shadows the type inside the class (CS0119).
        public RectTransform Root => rect;

        public TMP_Text Label => label;

        /// <summary>Wires the parts; used by the prefab builder.</summary>
        public void Bind(UiShape surface, TMP_Text text, Image iconImage, RectTransform root)
        {
            shape = surface;
            label = text;
            icon = iconImage;
            rect = root;
        }

        /// <summary>
        /// Sets text, variant and optional icon, then resizes the badge to its content.
        /// <paramref name="iconName"/> is the bare icon name ("Warn" → <c>Ic_Warn</c>).
        /// </summary>
        public void Set(string text, UiChipKind kind, string iconName = null)
        {
            bool bare = kind == UiChipKind.Good || kind == UiChipKind.Plain;
            float pad = bare ? 0f : Padding;

            if (label != null)
            {
                label.text = text ?? "";
                label.color = LabelColor(kind);
            }

            bool hasIcon = icon != null && !string.IsNullOrEmpty(iconName);
            if (icon != null)
            {
                if (hasIcon)
                {
                    icon.sprite = Girdap.Icon(iconName);
                    icon.color = LabelColor(kind);
                    hasIcon = icon.sprite != null;
                }

                if (icon.gameObject.activeSelf != hasIcon)
                {
                    icon.gameObject.SetActive(hasIcon);
                }
            }

            ApplySurface(kind, bare);

            float textWidth = label != null && !string.IsNullOrEmpty(label.text)
                ? label.GetPreferredValues(label.text).x
                : 0f;
            float iconWidth = hasIcon ? IconSize + IconGap : 0f;
            float width = Mathf.Ceil(pad * 2f + iconWidth + textWidth);

            if (rect != null)
            {
                rect.sizeDelta = new Vector2(width, Height);
            }

            LayoutContent(pad, hasIcon, textWidth);
        }

        private void ApplySurface(UiChipKind kind, bool bare)
        {
            if (shape == null)
            {
                return;
            }

            if (bare)
            {
                // Surfaceless variants are hidden rather than filled with clear: an empty mesh is
                // cheaper than a fully transparent one on the Quest fill rate.
                if (shape.gameObject.activeSelf)
                {
                    shape.gameObject.SetActive(false);
                }

                return;
            }

            if (!shape.gameObject.activeSelf)
            {
                shape.gameObject.SetActive(true);
            }

            switch (kind)
            {
                case UiChipKind.Warn:
                    shape.Fill(Girdap.WarnA, Girdap.WarnB, UiGradientMode.Vertical);
                    break;
                case UiChipKind.Bad:
                    shape.Fill(Girdap.ConfirmA, Girdap.ConfirmB, UiGradientMode.Vertical);
                    break;
                case UiChipKind.Dead:
                    shape.Fill(Girdap.ChipDead);
                    break;
                default:
                    shape.Fill(Girdap.Control);
                    break;
            }
        }

        private static Color LabelColor(UiChipKind kind)
        {
            switch (kind)
            {
                case UiChipKind.Good: return Girdap.Good;
                case UiChipKind.Plain: return Girdap.Muted;
                case UiChipKind.Warn: return Girdap.Ink;
                case UiChipKind.Bad: return Girdap.ConfirmFg;
                case UiChipKind.Dead: return Girdap.Muted;
                default: return Girdap.Muted;
            }
        }

        // Icon then label, both measured from the left edge (pivot/anchor stay the caller's choice).
        private void LayoutContent(float pad, bool hasIcon, float textWidth)
        {
            float x = pad;
            if (hasIcon)
            {
                RectTransform ir = icon.rectTransform;
                ir.anchorMin = new Vector2(0f, 0.5f);
                ir.anchorMax = new Vector2(0f, 0.5f);
                ir.pivot = new Vector2(0f, 0.5f);
                ir.sizeDelta = new Vector2(IconSize, IconSize);
                ir.anchoredPosition = new Vector2(x, 0f);
                x += IconSize + IconGap;
            }

            if (label != null)
            {
                RectTransform lr = label.rectTransform;
                lr.anchorMin = new Vector2(0f, 0.5f);
                lr.anchorMax = new Vector2(0f, 0.5f);
                lr.pivot = new Vector2(0f, 0.5f);
                lr.sizeDelta = new Vector2(textWidth, Height);
                lr.anchoredPosition = new Vector2(x, 0f);
            }
        }
    }
}
