using UnityEngine;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// "Kan" skin constants — the ONE place <c>Docs/Gelistirici/Arayuz/zombi.css</c>'s tokens live in
    /// code (Zombie mode HUD: strip, wave countdown, toast).
    /// <para>
    /// ⚠️ <b>Do not hand-pick a colour at a call site</b> (same rule as <see cref="Girdap"/> and
    /// <see cref="Lokanta"/>): the mockup is the single source of truth and a literal copied into a
    /// screen drifts silently.
    /// </para>
    /// <para>
    /// ⚠️ A THIRD palette on purpose — it skins one mode and does not replace <see cref="Girdap"/> on
    /// admin/result screens.
    /// </para>
    /// </summary>
    public static class Kan
    {
        // --------------------------------------------------------------- blood

        public static readonly Color Blood = Girdap.Hex(0x8E0E0E);
        public static readonly Color BloodHi = Girdap.Hex(0xD11F1F);
        public static readonly Color BloodLo = Girdap.Hex(0x3A0505);

        /// <summary>Hard shadow under the dripping title (CSS <c>0 6px 0 #2A0303</c>).</summary>
        public static readonly Color BloodShadow = Girdap.Hex(0x2A0303);

        // ---------------------------------------------------------------- bone

        /// <summary>Label/value ink on the rusted plate.</summary>
        public static readonly Color Bone = Girdap.Hex(0xEDE3CF);

        /// <summary>Ink on the blood cell and the toast — warmer than <see cref="Bone"/>.</summary>
        public static readonly Color BoneWarm = Girdap.Hex(0xFFE9E0);

        /// <summary>The big countdown number.</summary>
        public static readonly Color BoneBright = Girdap.Hex(0xF2E6D6);

        /// <summary>Small caps labels (CSS <c>#C9B79A</c>).</summary>
        public static readonly Color Label = Girdap.Hex(0xC9B79A);

        // ------------------------------------------------------- rusted plate

        public static readonly Color PlateA = Girdap.Hex(0x24170F);
        public static readonly Color PlateB = Girdap.Hex(0x0F0906);
        public static readonly Color ClockA = Girdap.Hex(0x2A1D17);
        public static readonly Color ClockB = Girdap.Hex(0x140D0A);
        public static readonly Color Border = Girdap.Hex(0x4B2A1E);

        /// <summary>Zombies still standing — the one number the player reacts to.</summary>
        public static readonly Color Alive = Girdap.Hex(0xFF5A4A);

        // -------------------------------------------------------------- shades
        // Alpha-carrying tokens; the CSS rgba() values, not re-derived at call sites.

        /// <summary>Cell separator (CSS <c>rgba(142, 14, 14, .55)</c>).</summary>
        public static readonly Color Divider = new Color(142f / 255f, 14f / 255f, 14f / 255f, 0.55f);

        /// <summary>Row inner glow (CSS <c>inset 0 0 40px rgba(120, 10, 10, .35)</c>).</summary>
        public static readonly Color InnerBlood = new Color(120f / 255f, 10f / 255f, 10f / 255f, 0.35f);

        /// <summary>Row drop shadow (CSS <c>0 10px 30px rgba(0, 0, 0, .7)</c>).</summary>
        public static readonly Color Drop = new Color(0f, 0f, 0f, 0.7f);

        /// <summary>Hard text shadow on the blood cell (CSS <c>0 3px 0 rgba(0, 0, 0, .5)</c>).</summary>
        public static readonly Color TextDrop = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>Countdown veil (same weight as the shared round notice).</summary>
        public static readonly Color Dim = new Color(0f, 0f, 0f, 0.35f);

        // ------------------------------------------------------------- metrics

        /// <summary>Plate border width (CSS <c>3px solid</c>).</summary>
        public const float Border1 = 3f;

        public const float PlateRadius = 10f;
        public const float ClockRadius = 8f;
    }
}
