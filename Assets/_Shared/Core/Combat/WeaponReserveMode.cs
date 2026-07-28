namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Rezerv cephane muhasebesi kuralı (serialize edilir — <see cref="WeaponDefinition"/> taşır).
    /// <list type="bullet">
    /// <item><see cref="DiscardMagazine"/> — şarjör bazlı (VARSAYILAN ürün kuralı): rezerv tam
    /// şarjörler hâlinde harcanır; erken reload'da çıkarılan şarjörde kalan mermi YANAR.</item>
    /// <item><see cref="PoolRounds"/> — CS2 tarzı rezerv mermi havuzu: reload şarjörü havuzdan
    /// tamamlar, şarjörde kalan mermi kaybolmaz.</item>
    /// </list>
    /// </summary>
    public enum WeaponReserveMode
    {
        DiscardMagazine = 0,
        PoolRounds = 1
    }
}
