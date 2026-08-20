using System;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// The <b>single visibility switch</b> of the in-game HUDs (the mode HUD and the ammo indicator).
    /// While the switch is off these HUDs do not draw themselves; the match result overlay has taken
    /// over the screen.
    /// <para>
    /// <b>Its only writer is <c>MatchResultOverlay</c></b> (App). It is set from nowhere else — a
    /// second writer would leave "who turned it off, who will turn it back on" ambiguous and one day
    /// the HUD would silently stay off.
    /// </para>
    /// <para>
    /// <b>Why a switch and not the phase:</b> if the HUDs checked "is the phase <c>finished</c>"
    /// themselves, then whenever the match result overlay was not drawn for any reason (prefab not
    /// found, role is admin) the player would see NOTHING at the end of the match. Because the
    /// overlay itself flips the switch, the HUD is only hidden when something is genuinely put in
    /// its place.
    /// </para>
    /// <para>
    /// State and event are <b>static</b> (the <c>ModeRuntime</c> pattern) so listeners do not have to
    /// know when the writer is born. The writer sets the switch to the visible state while waking up,
    /// so a stale <c>true</c> left over from a Play session without domain reload is not carried over.
    /// </para>
    /// </summary>
    public static class GameplayHudGate
    {
        /// <summary>Whether the in-game HUDs are hidden.</summary>
        public static bool Hidden { get; private set; }

        /// <summary>Raised only when the VALUE changes (main thread).</summary>
        public static event Action<bool> HiddenChanged;

        /// <summary>Flips the switch. For its writer see the class documentation.</summary>
        public static void SetHidden(bool hidden)
        {
            if (Hidden == hidden)
            {
                return;
            }

            Hidden = hidden;
            HiddenChanged?.Invoke(hidden);
        }
    }
}
