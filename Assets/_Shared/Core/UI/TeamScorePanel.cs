using TMPro;
using UnityEngine;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// The two teams' score next to the health bar, each in its own colour, with an optional line
    /// above it for the mode's own heading ("TUR 2").
    ///
    /// <para><b>Presentation only.</b> What the numbers MEAN differs per mode — rounds won in a
    /// tournament, kills in TDM (§10.5) — so this class computes nothing and is fed by the mode HUD
    /// (<see cref="ModeHudBase.OnMatchStateApplied"/>).</para>
    /// </summary>
    /// <remarks>
    /// <para>It lives inside <c>HealthHud.prefab</c> so it rides the health bar's single
    /// <see cref="HeadLockedHud"/>: "next to the bar" only holds while the two turn with the head
    /// together, and a second head-lock is exactly the exception <see cref="HudFollow"/> exists to
    /// keep rare.</para>
    /// <para>It hides itself where there are no teams (<see cref="ModeRuntime.IsTeamless"/>) — the same
    /// read point <c>BaseZoneVisibility</c> uses, so no <c>if (modeId == …)</c> is born here. ⚠️ What
    /// it toggles is a CHILD, never its own GameObject: deactivating itself would unsubscribe it in
    /// <c>OnDisable</c> and it would never hear the rule change that turns it back on.</para>
    /// </remarks>
    public class TeamScorePanel : MonoBehaviour
    {
        [Tooltip("Takımsız modda kapatılan içerik kökü — bileşenin KENDİ nesnesi olmamalı.")]
        [SerializeField] private GameObject content;
        [Tooltip("Opsiyonel üst satır (\"TUR 2\"). Tur kavramı olmayan mod bunu hiç yazmaz.")]
        [SerializeField] private TMP_Text roundText;
        [SerializeField] private TMP_Text redScoreText;
        [SerializeField] private TMP_Text blueScoreText;

        private void Awake()
        {
            // The prefab's texts are placeholders for authoring; nothing real is shown until a mode
            // feeds it.
            SetText(roundText, "");
            SetScore(0, 0);
        }

        private void OnEnable()
        {
            ModeRuntime.Changed += ApplyVisibility;
            ApplyVisibility();
        }

        private void OnDisable()
        {
            ModeRuntime.Changed -= ApplyVisibility;
        }

        public void SetScore(int red, int blue)
        {
            SetText(redScoreText, red.ToString());
            SetText(blueScoreText, blue.ToString());
        }

        /// <summary>The line above the score; empty clears it. The wording belongs to the mode.</summary>
        public void SetRoundLabel(string label)
        {
            SetText(roundText, label ?? "");
        }

        /// <summary>Back to the lobby — the finished match's numbers must not survive into the next
        /// one.</summary>
        public void Clear()
        {
            SetText(roundText, "");
            SetScore(0, 0);
        }

        private void ApplyVisibility()
        {
            if (content != null)
            {
                content.SetActive(!ModeRuntime.IsTeamless);
            }
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }
    }
}
