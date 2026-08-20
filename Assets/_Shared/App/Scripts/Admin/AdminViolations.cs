using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// The visible kind of a violation on the operator's screen (§10.9).
    /// <para>
    /// ⚠️ <b>NOT serialized</b> (never stored in a scene/asset, only turned into a color at
    /// runtime), so the "append new values at the end" rule does not apply; <c>None = 0</c> is for
    /// readability.
    /// </para>
    /// </summary>
    public enum AdminViolationKind
    {
        None = 0,

        /// <summary>The head is outside the boundary's safe area — <b>produces no penalty</b>.</summary>
        OutOfBounds,

        /// <summary>The head is inside an interior obstacle — the only violation kind that drains health.</summary>
        Obstacle
    }

    /// <summary>
    /// <b>Single source of truth for how a violation looks</b> (color, rhythm, text).
    /// <para>
    /// ⚠️ The ring (<see cref="AdminPlayerMarkers"/>) and the side panel row
    /// (<see cref="AdminPlayerRow"/>) show the same violation; with the rule written twice, the
    /// same player would look differently severe in two places.
    /// </para>
    /// </summary>
    public static class AdminViolations
    {
        /// <summary>Obstacle blink frequency (Hz) — fast, health is draining.</summary>
        private const float ObstacleBlinkHz = 3f;

        /// <summary>Out-of-bounds blink frequency (Hz) — deliberately slower, so the rhythm carries
        /// the severity difference too, not just the color.</summary>
        private const float OutOfBoundsBlinkHz = 1.5f;

        /// <summary>Color multiplier for the blink's dim half — the ring darkens, never disappears:
        /// full-off would leave "how many are in violation" uncountable half the time.</summary>
        private const float BlinkDim = 0.3f;

        private const string LabelObstacle = "DUVAR";
        private const string LabelOutOfBounds = "ALAN DIŞI";

        /// <summary>
        /// The player's current violation state, from the last snapshot's flags
        /// (<see cref="RemotePlayerRegistry"/>), so it clears itself when the state goes stale.
        /// <para>
        /// ⚠️ <b>Obstacle wins:</b> both can be true at once and the health-draining one is shown —
        /// the operator's order of intervention follows severity.
        /// </para>
        /// <para>Without a registry, <see cref="AdminViolationKind.None"/> — an unknown state must
        /// not be shown as an event that never happened.</para>
        /// </summary>
        public static AdminViolationKind Of(int playerId)
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null)
            {
                return AdminViolationKind.None;
            }

            if (registry.IsInObstacle(playerId))
            {
                return AdminViolationKind.Obstacle;
            }

            return registry.IsOutOfBounds(playerId)
                ? AdminViolationKind.OutOfBounds
                : AdminViolationKind.None;
        }

        /// <summary>
        /// The violation's current blink color; <see cref="UiKit.Border"/> for
        /// <see cref="AdminViolationKind.None"/> so an accidental call is not a visible error.
        /// <para>
        /// ⚠️ <b>The phase is NOT offset per player</b> (<c>Time.unscaledTime</c> only): synchronous
        /// blinking lets the operator count violations at a glance, which an offset phase would
        /// make impossible.
        /// </para>
        /// </summary>
        public static Color Blink(AdminViolationKind kind)
        {
            switch (kind)
            {
                case AdminViolationKind.Obstacle:
                    return Pulse(UiKit.Bad, ObstacleBlinkHz);
                case AdminViolationKind.OutOfBounds:
                    // "Warning but not an error" is Accent here (low battery, floor drift).
                    return Pulse(UiKit.Accent, OutOfBoundsBlinkHz);
                default:
                    return UiKit.Border;
            }
        }

        /// <summary>
        /// The violation's short label; empty when there is none.
        /// <para>⚠️ <b>Plain text only</b> — symbols not guaranteed by TMP's default font draw as □;
        /// same rule as the kill feed's "-&gt;".</para>
        /// </summary>
        public static string Label(AdminViolationKind kind)
        {
            switch (kind)
            {
                case AdminViolationKind.Obstacle: return LabelObstacle;
                case AdminViolationKind.OutOfBounds: return LabelOutOfBounds;
                default: return "";
            }
        }

        /// <summary>
        /// Label for a protocol <c>ArenaProtocol.VIOLATION_KIND_*</c> string (used by the feed rows).
        /// <para>⚠️ <b>An unrecognized kind is shown RAW</b>, not dropped: the server may add kinds,
        /// and swallowing the row would hide a real event from the operator.</para>
        /// </summary>
        public static string Label(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                return "";
            }

            if (kind == ArenaProtocol.VIOLATION_KIND_OBSTACLE)
            {
                return LabelObstacle;
            }

            return kind == ArenaProtocol.VIOLATION_KIND_OUT_OF_BOUNDS
                ? LabelOutOfBounds
                : kind;
        }

        /// <summary>Half a period lit, half dimmed — <c>hz</c> gives twice as many half periods per second.</summary>
        private static Color Pulse(Color color, float hz)
        {
            bool on = Mathf.Repeat(Time.unscaledTime * hz, 1f) < 0.5f;
            return on ? color : UiKit.Dim(color, BlinkDim);
        }
    }
}
