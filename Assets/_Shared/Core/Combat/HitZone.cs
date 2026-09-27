namespace VortexArena.Core.Combat
{
    /// <summary>
    /// A hitbox's body zone — the single source of the damage multiplier.
    /// <para>Head is per weapon; chest, ARMS and stomach/pelvis are body (1×); leg 0.75×. The numeric
    /// values live in <see cref="WeaponDefinition.GetZoneMultiplier"/>, not here — balance numbers
    /// change per weapon, the zone list does not.</para>
    /// <para>⚠️ Serialized enum: new values are appended at the END. Unity stores a numeric index;
    /// inserting elsewhere silently shifts the zones of <c>RemoteAvatar.prefab</c>'s hitboxes.</para>
    /// <para>⚠️ <see cref="Body"/> stays zero: an unassigned hitbox must fall back to the most
    /// harmless value (1×, no surprise damage).</para>
    /// </summary>
    public enum HitZone
    {
        /// <summary>Chest and arms — multiplier 1× (reference damage).</summary>
        Body,

        /// <summary>Head — multiplier <c>WeaponDefinition.HeadshotMultiplier</c> (per weapon).</summary>
        Head,

        /// <summary>Stomach/pelvis — multiplier <c>WeaponDefinition.StomachMultiplier</c> (1×, counts as body).</summary>
        Stomach,

        /// <summary>Legs — multiplier <c>WeaponDefinition.LegMultiplier</c> (CS2: 0.75×).</summary>
        Leg,
    }
}
