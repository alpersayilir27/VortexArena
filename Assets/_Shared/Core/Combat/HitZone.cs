namespace VortexArena.Core.Combat
{
    /// <summary>
    /// A hitbox's body zone — the single source of the damage multiplier.
    /// <para>Model is CS2: head 4×, chest and ARMS 1×, stomach/pelvis 1.25×, leg 0.75×. The numeric
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

        /// <summary>Head — multiplier <c>WeaponDefinition.HeadshotMultiplier</c> (CS2: 4×).</summary>
        Head,

        /// <summary>Stomach/pelvis — multiplier <c>WeaponDefinition.StomachMultiplier</c> (CS2: 1.25×).</summary>
        Stomach,

        /// <summary>Legs — multiplier <c>WeaponDefinition.LegMultiplier</c> (CS2: 0.75×).</summary>
        Leg,
    }
}
