using System;
using VortexArena.Protocol;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// The ONE place a <c>kill_event</c> becomes a line of text — player HUD
    /// (<see cref="ModeHudBase"/>) and admin roster read the same rule.
    /// <para>⚠️ <b>Why it is not written twice:</b> the branches are not cosmetic, they answer
    /// "what killed this player" — and a copy that is missing one silently degrades to a WRONG
    /// answer instead of no answer. A suicide read as "öldü" is indistinguishable from a server
    /// death, so the operator investigates a bug that never happened.</para>
    /// <para>⚠️ Only characters that EXIST in the TMP font: LiberationSans SDF (+ fallback) has no
    /// arrows or skulls, and a missing glyph is drawn on screen as □. Use <c>-&gt;</c>.</para>
    /// </summary>
    public static class KillFeedText
    {
        /// <summary>Builds the feed line. <paramref name="nameOf"/> resolves a player id to a display
        /// name (each surface has its own roster).</summary>
        /// <param name="withWeaponLabel">Append <c>[weaponId]</c> — the operator's view wants it, the
        /// player's does not. ⚠️ Never appended to the environmental line: there the weapon id IS the
        /// cause and would print as "engelde kaldı [obstacle]".</param>
        public static string Line(KillEventMsg msg, Func<int, string> nameOf, bool withWeaponLabel = false)
        {
            if (msg == null || nameOf == null)
            {
                return "";
            }

            string victim = nameOf(msg.victimId);
            string weaponId = msg.weaponId ?? "";
            bool obstacle = string.Equals(weaponId, ArenaProtocol.WEAPON_ID_OBSTACLE);
            string label = withWeaponLabel && !obstacle && weaponId.Length > 0 ? $" [{weaponId}]" : "";

            if (msg.killerId > 0 && msg.killerId != msg.victimId)
            {
                return $"{nameOf(msg.killerId)} -> {victim}{label}";
            }

            if (msg.killerId > 0 && msg.killerId == msg.victimId)
            {
                // Own blast (§10.3 gate 5, friendly fire on): killerId == victimId. Its own line —
                // "öldü" would read as an environmental death and hide who did it.
                return $"{victim} kendini havaya uçurdu{label}";
            }

            if (obstacle)
            {
                // §10.9 environmental death: killerId is 0, so the branches above do not match. A
                // separate line for the operator/player distinction — "öldü" did not tell a player
                // melting inside a wall apart from a server error.
                return $"{victim} engelde kaldı";
            }

            return $"{victim} öldü{label}";
        }
    }
}
