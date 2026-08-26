using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Static helper for the world↔arena conversion. All poses going to/coming from the network are
    /// IN ARENA SPACE, and arena space <b>coincides with world space</b> (origin 0,0,0, identity
    /// rotation) — which is why every conversion here is the identity.
    /// <para>
    /// ⚠️ The binding consequence of this on the scene side: <b>arena geometry is built relative to
    /// the world origin</b> — the arena floor must be at world y=0 and the arena center around
    /// world (0,0,0). Moving or rotating the scene as a whole shifts every player's network
    /// coordinates; there is NO origin on the wire that compensates for it.
    /// </para>
    /// <para>
    /// Even though the conversion is the identity, the class stays so that the call sites
    /// (<c>PlayerPoseTracker</c>, <c>RemoteAvatar</c>, <c>ArenaCombat</c>,
    /// <c>AdminSpectatorCamera</c>, <c>AdminPlayerMarkers</c>, <c>ProximityWarning</c>,
    /// <c>ArenaNetCharacterBehaviour</c>, <c>RemoteShotFx</c>) can see which value is in which space
    /// while reading — and so that, if the conversion ever comes back, it comes from a single place.
    /// </para>
    /// </summary>
    public static class ArenaSpace
    {
        /// <summary>Converts a world position into arena space (spaces coincide → identity).</summary>
        public static Vector3 WorldToArena(Vector3 worldPosition)
        {
            return worldPosition;
        }

        /// <summary>Converts a world rotation into arena space (spaces coincide → identity).</summary>
        public static Quaternion WorldToArena(Quaternion worldRotation)
        {
            return worldRotation;
        }

        /// <summary>Converts an arena position into world space (spaces coincide → identity).</summary>
        public static Vector3 ArenaToWorld(Vector3 arenaPosition)
        {
            return arenaPosition;
        }

        /// <summary>Converts an arena rotation into world space (spaces coincide → identity).</summary>
        public static Quaternion ArenaToWorld(Quaternion arenaRotation)
        {
            return arenaRotation;
        }

        /// <summary>
        /// Converts a <b>DIRECTION</b> from world to arena.
        /// <para>
        /// ⚠️ Even though the conversion is the identity, this gate stands apart because its job is
        /// not conversion but the <b>normalization contract</b>: in the protocol §6.4 every event
        /// carries a <b>unit</b> direction, there is no "no direction" value. No caller that sends a
        /// direction should write that contract by hand (<c>ArenaCombat</c>, the single gate that
        /// reports hits/shots to the network, uses this too).
        /// </para>
        /// <para>
        /// For input that cannot be normalized (zero vector, NaN) it returns
        /// <see cref="Vector3.forward"/>.
        /// </para>
        /// </summary>
        public static Vector3 WorldToArenaDirection(Vector3 worldDirection)
        {
            // Vector3.normalized returns ZERO for zero/NaN input (Unity's contract) — no separate
            // IsNaN check is needed, a single threshold catches both.
            Vector3 unit = worldDirection.normalized;
            return unit.sqrMagnitude < 0.5f ? Vector3.forward : unit;
        }

        /// <summary>
        /// Converts a <b>DIRECTION</b> from arena to world — the counterpart of
        /// <see cref="WorldToArenaDirection"/> and subject to the same normalization contract.
        /// <para>⚠️ A direction must NOT go through <see cref="ArenaToWorld(Vector3)"/>: that gate is
        /// for POSITIONS, and the day the spaces stop coinciding it would add the translation to a
        /// direction — silently, since both are the identity today.</para>
        /// </summary>
        public static Vector3 ArenaToWorldDirection(Vector3 arenaDirection)
        {
            Vector3 unit = arenaDirection.normalized;
            return unit.sqrMagnitude < 0.5f ? Vector3.forward : unit;
        }

        /// <summary>Converts a world pose into arena space (spaces coincide → identity).</summary>
        public static Pose WorldToArena(in Pose worldPose)
        {
            return worldPose;
        }

        /// <summary>Converts an arena pose into world space (spaces coincide → identity).</summary>
        public static Pose ArenaToWorld(in Pose arenaPose)
        {
            return arenaPose;
        }
    }
}
