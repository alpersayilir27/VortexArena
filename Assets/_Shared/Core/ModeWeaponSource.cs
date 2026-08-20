namespace VortexArena.Core
{
    /// <summary>
    /// Where the weapon comes from (§10.5 <c>weaponSource</c>).
    /// <para>
    /// <b>Entirely client presentation</b> — it has no server counterpart (§10.3: the server keeps no
    /// weapon table, the client computes the damage). This rule only answers the question "should the
    /// weapon be picked up from the scene, or handed out by the mode".
    /// </para>
    /// <para>⚠ SERIALIZED by <see cref="ModeDefinition"/> — new values are appended at the END.</para>
    /// </summary>
    public enum ModeWeaponSource
    {
        /// <summary>
        /// A weapon standing in the scene: the player picks it from its frame (<c>WeaponFrame</c>),
        /// a clone lands in their hand and returns with the same ammo. The weapon is <b>not
        /// consumed</b> — it stays in its frame and can be taken an unlimited number of times.
        /// <para>
        /// There is NO component that places the weapon into the scene and none will be written:
        /// placement is an <b>arena decision</b>, done by hand while designing the map (as a prefab
        /// instance, like <c>BaseZone</c>). Therefore with this source it is the arena, not
        /// <see cref="ModeDefinition"/>, that decides which weapon stands in the scene —
        /// <c>loadout</c> is only meaningful for <see cref="RandomGrant"/>.
        /// </para>
        /// <para>Its name in the wire format is <c>"weaponcanvas"</c> (§10.5).</para>
        /// </summary>
        WeaponCanvas,

        /// <summary>A random weapon handed out by the mode.</summary>
        RandomGrant
    }
}
