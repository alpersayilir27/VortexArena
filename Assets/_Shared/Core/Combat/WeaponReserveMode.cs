namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Reserve ammo accounting rule (serialized — carried by <see cref="WeaponDefinition"/>).
    /// <list type="bullet">
    /// <item><see cref="DiscardMagazine"/> — magazine based (the DEFAULT product rule): the
    /// reserve is spent in whole magazines; on an early reload the rounds left in the ejected
    /// magazine are BURNED.</item>
    /// <item><see cref="PoolRounds"/> — CS2 style reserve round pool: a reload tops the magazine
    /// up from the pool, the rounds left in the magazine are not lost.</item>
    /// </list>
    /// </summary>
    public enum WeaponReserveMode
    {
        DiscardMagazine = 0,
        PoolRounds = 1
    }
}
