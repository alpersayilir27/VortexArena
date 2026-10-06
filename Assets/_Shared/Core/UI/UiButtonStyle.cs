using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Applies a themed button's whole look from ONE place: surface gradient, border, label and
    /// icon color, disabled state, hold progress.
    /// <para>
    /// ⚠️ <b>Why screens must not touch colors directly:</b> a button changes kind at runtime
    /// (BİTİR → BİTİR?, danger → confirm) and every call site that recolored by hand would have to
    /// restore four layers correctly. Call <see cref="SetKind"/>/<see cref="SetInteractable"/>.
    /// </para>
    /// </summary>
    [AddComponentMenu("VortexArena/UI/Button Style")]
    public class UiButtonStyle : MonoBehaviour
    {
        [SerializeField] private UiShape shape;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image icon;

        /// <summary>Optional hold-to-confirm fill; left-anchored over the surface.</summary>
        [SerializeField] private UiShape holdFill;

        /// <summary>Optional keycap parts (CSS <c>kbd</c>: <c>currentColor</c> at 0.6 opacity) —
        /// they follow the label ink, so a lit button gets a dark keycap.</summary>
        [SerializeField] private UiShape kbdBorder;

        /// <inheritdoc cref="kbdBorder"/>
        [SerializeField] private TMP_Text kbdLabel;

        [SerializeField] private Button button;
        [SerializeField] private UiButtonKind kind = UiButtonKind.Normal;
        [SerializeField] private bool interactable = true;

        private float holdProgress;

        public UiButtonKind Kind => kind;

        public bool Interactable => interactable;

        // Named TargetButton, not Button: a member called `Button` would shadow the TYPE inside this
        // class (CS0119 on `Button btn` parameters) — same trap as UiKit's using-aliases.
        public Button TargetButton => button;

        public TMP_Text Label => label;

        public UiShape Shape => shape;

        /// <summary>Wires the parts; used by the prefab builder.</summary>
        public void Bind(UiShape surface, TMP_Text text, Image iconImage, UiShape hold, Button btn,
            UiShape keycapBorder = null, TMP_Text keycapLabel = null)
        {
            shape = surface;
            label = text;
            icon = iconImage;
            holdFill = hold;
            button = btn;
            kbdBorder = keycapBorder;
            kbdLabel = keycapLabel;
        }

        public void SetKind(UiButtonKind k)
        {
            kind = k;
            Apply();
        }

        public void SetInteractable(bool value)
        {
            interactable = value;
            Apply();
        }

        public void SetLabel(string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        /// <summary>
        /// Hold-to-confirm progress (0..1). While above zero the surface switches to the hold look
        /// so the progress bar reads as "deleting", not as a normal button.
        /// </summary>
        public void SetHold(float progress01)
        {
            holdProgress = Mathf.Clamp01(progress01);
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        public void Apply()
        {
            Color bdA = Girdap.BtnBdA;
            Color bdB = Girdap.BtnBdB;
            Color bgA = Girdap.BtnBgA;
            Color bgB = Girdap.BtnBgB;
            Color fg = Girdap.Text;
            float borderWidth = 1f;

            switch (kind)
            {
                case UiButtonKind.Plate:
                    bgA = Girdap.PlateA;
                    bgB = Girdap.PlateB;
                    break;
                case UiButtonKind.On:
                case UiButtonKind.SegOn:
                    bdA = bdB = Girdap.OnBd;
                    bgA = Girdap.OnA;
                    bgB = Girdap.OnB;
                    fg = Girdap.OnFg;
                    break;
                case UiButtonKind.Go:
                    bdA = bdB = Girdap.GoBd;
                    bgA = Girdap.GoA;
                    bgB = Girdap.GoB;
                    fg = Girdap.GoFg;
                    break;
                case UiButtonKind.Danger:
                    bdA = bdB = Girdap.DangerBd;
                    bgA = Girdap.DangerA;
                    bgB = Girdap.DangerB;
                    fg = Girdap.DangerFg;
                    break;
                case UiButtonKind.Confirm:
                    bdA = bdB = Girdap.ConfirmBd;
                    bgA = Girdap.ConfirmA;
                    bgB = Girdap.ConfirmB;
                    fg = Girdap.ConfirmFg;
                    break;
                case UiButtonKind.WarnFill:
                    bdA = bdB = Girdap.WarnBd;
                    bgA = Girdap.WarnA;
                    bgB = Girdap.WarnB;
                    fg = Girdap.WarnFg;
                    break;
                case UiButtonKind.Seg:
                    bdA = bdB = Color.clear;
                    bgA = bgB = Color.clear;
                    borderWidth = 0f;
                    break;
                case UiButtonKind.Tab:
                    bdA = bdB = Color.clear;
                    bgA = bgB = Girdap.TabBg;
                    fg = Girdap.Muted;
                    borderWidth = 0f;
                    break;
                case UiButtonKind.TextGood:
                    fg = Girdap.Good;
                    break;
                case UiButtonKind.TextBad:
                    fg = Girdap.Bad;
                    break;
                case UiButtonKind.TextRed:
                    fg = Girdap.RedInk;
                    break;
                case UiButtonKind.TextBlue:
                    fg = Girdap.BlueInk;
                    break;
            }

            bool holding = holdFill != null && holdProgress > 0f;
            if (holding)
            {
                bdA = bdB = Girdap.Bad;
                bgA = bgB = Girdap.HoldTrack;
                fg = Girdap.Text;
                borderWidth = 1f;
            }
            else if (!interactable)
            {
                bdA = bdB = Girdap.OffBd;
                bgA = bgB = Girdap.OffBg;
                fg = Girdap.OffFg;
                borderWidth = 1f;
            }

            if (shape != null)
            {
                shape.Outline(borderWidth, bdA, bdB);
                shape.Fill(bgA, bgB, UiGradientMode.Vertical);
            }

            if (label != null)
            {
                label.color = fg;
            }

            if (icon != null)
            {
                icon.color = fg;
            }

            Color ink = fg;
            ink.a *= 0.6f;
            if (kbdLabel != null)
            {
                kbdLabel.color = ink;
            }

            if (kbdBorder != null)
            {
                kbdBorder.Outline(1f, ink);
            }

            ApplyHold();

            if (button != null)
            {
                button.interactable = interactable;
                button.targetGraphic = shape; // hover/press tint rides on the surface, not the label

                ColorBlock colors = button.colors;
                colors.normalColor = Color.white; // surface colors come from UiShape, not the block
                colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
                colors.selectedColor = Color.white;
                colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                colors.disabledColor = Color.white;
                colors.fadeDuration = 0.08f;
                button.colors = colors;
            }
        }

        private void ApplyHold()
        {
            if (holdFill == null)
            {
                return;
            }

            bool on = holdProgress > 0f;
            if (holdFill.gameObject.activeSelf != on)
            {
                holdFill.gameObject.SetActive(on);
            }

            if (!on)
            {
                return;
            }

            holdFill.Fill(Girdap.HoldBg);
            RectTransform rt = holdFill.rectTransform;
            rt.anchorMax = new Vector2(holdProgress, rt.anchorMax.y);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (isActiveAndEnabled)
            {
                Apply();
            }
        }
#endif
    }
}
