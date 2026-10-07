using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.Combat;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Girdap status line (CSS <c>.vstat</c>): chamfered plate + icon + text, sized to the text and
    /// hidden when empty. The icon follows <see cref="CombatStatusKind"/>; the words are the HUD's.
    /// </summary>
    /// <remarks>⚠️ Toggles a CHILD (<see cref="body"/>), never its own GameObject — the HUD keeps
    /// calling <see cref="Set"/> while the line is empty.</remarks>
    public class StatusPlate : MonoBehaviour
    {
        [Tooltip("Açılıp kapanan içerik kökü (plaka + ikon + metin) — bileşenin KENDİ nesnesi olmamalı.")]
        [SerializeField] private GameObject body;
        [Tooltip("Metne göre genişletilen plaka dikdörtgeni (merkez ankrajlı).")]
        [SerializeField] private RectTransform plate;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text text;

        [Header("Ölçüler (CSS .vstat)")]
        [SerializeField] private float padLeft = 12f;
        [SerializeField] private float padRight = 14f;
        [SerializeField] private float gap = 8f;
        [SerializeField] private float iconSize = 18f;
        [SerializeField] private float maxWidth = 560f;

        /// <summary>Empty text takes the plate down.</summary>
        public void Set(string value, CombatStatusKind kind)
        {
            value ??= "";
            if (body != null)
            {
                body.SetActive(value.Length > 0);
            }

            if (value.Length == 0)
            {
                return;
            }

            string iconName = IconOf(kind);
            Sprite sprite = iconName != null ? Girdap.Icon(iconName) : null;
            bool hasIcon = sprite != null;
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.gameObject.SetActive(hasIcon);
            }

            if (text != null)
            {
                text.text = value;
            }

            // Flex row by hand: no LayoutGroup in Girdap prefabs.
            float textW = text != null ? Mathf.Ceil(text.GetPreferredValues(value).x) : 0f;
            float left = padLeft + (hasIcon ? iconSize + gap : 0f);
            float width = Mathf.Min(maxWidth, left + textW + padRight);
            if (plate != null)
            {
                plate.sizeDelta = new Vector2(width, plate.sizeDelta.y);
            }

            if (text != null)
            {
                RectTransform rt = text.rectTransform;
                rt.offsetMin = new Vector2(left, rt.offsetMin.y);
                rt.offsetMax = new Vector2(-padRight, rt.offsetMax.y);
            }
        }

        /// <summary>Theme icon per rule (<c>Ic_&lt;Name&gt;</c>); null = no icon.</summary>
        private static string IconOf(CombatStatusKind kind)
        {
            switch (kind)
            {
                case CombatStatusKind.Calibration: return "Warn";
                case CombatStatusKind.DeadWait: return "Skull";
                case CombatStatusKind.Obstacle: return "Warn";
                case CombatStatusKind.DeadCountdown: return "Timer";
                case CombatStatusKind.HoldStill: return "Timer";
                case CombatStatusKind.Reviving: return "Refresh";
                case CombatStatusKind.ReturnBase: return "Home";
                case CombatStatusKind.SpawnProtection: return "Shield";
                default: return null;
            }
        }
    }
}
