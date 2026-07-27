namespace VortexArena.Core
{
    /// <summary>
    /// Silahın nereden geldiği (§10.5 <c>weaponSource</c>).
    /// <para>
    /// <b>Tümüyle istemci sunumudur</b> — sunucuda karşılığı yoktur (§10.3: sunucuda silah tablosu
    /// tutulmaz, hasarı istemci hesaplar). Bu kural yalnız "sahnedeki rafı göster mi, mod mu silah
    /// dağıtsın" sorusunu cevaplar.
    /// </para>
    /// <para>⚠ <see cref="ModeDefinition"/> tarafından SERIALIZE edilir — yeni değer SONA eklenir.</para>
    /// </summary>
    public enum ModeWeaponSource
    {
        /// <summary>Sahnedeki taban rafları (bugünkü davranış).</summary>
        Rack,

        /// <summary>Modun dağıttığı rastgele silah.</summary>
        RandomGrant
    }
}
