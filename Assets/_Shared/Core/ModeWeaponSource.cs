namespace VortexArena.Core
{
    /// <summary>
    /// Silahın nereden geldiği (§10.5 <c>weaponSource</c>).
    /// <para>
    /// <b>Tümüyle istemci sunumudur</b> — sunucuda karşılığı yoktur (§10.3: sunucuda silah tablosu
    /// tutulmaz, hasarı istemci hesaplar). Bu kural yalnız "silahı sahneden mi alsın, mod mu
    /// dağıtsın" sorusunu cevaplar.
    /// </para>
    /// <para>⚠ <see cref="ModeDefinition"/> tarafından SERIALIZE edilir — yeni değer SONA eklenir.</para>
    /// </summary>
    public enum ModeWeaponSource
    {
        /// <summary>
        /// Sahnede duran silah: oyuncu onu çerçevesinden seçer (<c>WeaponFrame</c>), klonu eline
        /// gelir ve aynı mermiyle geri döner. Silah <b>tükenmez</b> — çerçevesinde kalır ve
        /// sınırsız kez alınır.
        /// <para>
        /// Silahı sahneye koyan bir bileşen YOKTUR ve yazılmaz: yerleşim <b>arena kararıdır</b>,
        /// harita tasarlanırken elle konur (<c>BaseZone</c> gibi bir prefab örneği olarak).
        /// Dolayısıyla bu kaynakta sahnede hangi silahın duracağını <see cref="ModeDefinition"/>
        /// değil arena belirler — <c>loadout</c> yalnız <see cref="RandomGrant"/> için anlamlıdır.
        /// </para>
        /// <para>Tel formatındaki adı <c>"weaponcanvas"</c>'tır (§10.5).</para>
        /// </summary>
        WeaponCanvas,

        /// <summary>Modun dağıttığı rastgele silah.</summary>
        RandomGrant
    }
}
