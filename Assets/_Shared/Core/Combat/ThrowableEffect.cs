using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// What a thrown item DOES when it goes off — the family every throwable effect derives from
    /// (blast today; fire pool, flashbang, smoke later). The flight (<see cref="Throwable"/>) is
    /// shared; only the trigger and this effect differ per type, so a new type is one component +
    /// one catalog entry and never a protocol version bump (Docs/ArenaNet-Protokol.md §6.4).
    /// <para>⚠️ <b>Only the THROWER's copy may report damage</b> (<see cref="Throwable.LocalOwner"/>):
    /// remote copies drift by centimetres and merely play FX. Reporting from every copy would deal
    /// the same blast n times.</para>
    /// <para>Damage-free types (flashbang, smoke) produce NO <c>hit_report</c> at all — each client
    /// evaluates only its OWN player and the server never sees them (§6.4).</para>
    /// </summary>
    public abstract class ThrowableEffect : MonoBehaviour
    {
        /// <summary>Fires the effect. Called once, right before the throwable is destroyed.</summary>
        public abstract void Trigger(Throwable source);

        /// <summary>Pay the presentation's one-off cost NOW, at <see cref="Throwable.Arm"/>, instead of
        /// at the trigger. No-op by default.
        /// <para>⚠️ The trigger is the one moment that must not stall: a pool built there hitches
        /// exactly when the player is watching the explosion. The fuse is dead time and pays for
        /// it.</para></summary>
        public virtual void Prewarm(ThrowableDefinition definition)
        {
        }
    }
}
