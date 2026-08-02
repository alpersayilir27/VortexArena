namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silahın <b>nasıl</b> verildiği — <see cref="Weapon.GrantTo"/>'nun ikinci argümanı.
    /// <para>
    /// <b>Neden ayrı bir tip:</b> eskiden tek bir bayrak (<c>Weapon.IsGranted</c>) üç ayrı şeyi
    /// birden anlatıyordu — "silah elde SABİT duruyor (ISDK kavraması işletilmez)", "reload
    /// KAPALI" ve "tek el / rezerv yok". Üçü FFA'nın rastgele silahında birlikte doğruydu, ama
    /// çerçeveden seçilen silah yalnız ilkini ister: onun reload'u açıktır, rezervi vardır ve
    /// ikinci el ön kabzayı tutabilir. Tek bayrakla devam edilseydi bu üç kural birbirine
    /// kilitli kalır, ikinci yol ancak <c>if (modeId == …)</c> zinciriyle yazılabilirdi.
    /// </para>
    /// <para>
    /// ⚠️ Bu enum <b>serialize EDİLMEZ</b> (yalnız çalışma anı durumu; hiçbir SO/sahne alanı bunu
    /// tutmaz), o yüzden "serialize edilen enum'a yeni değer SONA eklenir" kuralı burada bağlayıcı
    /// değildir — sıra okunabilirlik için seçilmiştir.
    /// </para>
    /// </summary>
    public enum WeaponGrantKind
    {
        /// <summary>Verilmedi: silah sahnededir ya da ISDK kavramasıyla tutulur.</summary>
        None,

        /// <summary>FFA'nın rastgele silahı (§10.5 <c>weaponSource:"random"</c>): grip bırakılınca
        /// YOK OLUR, tekrar basınca yenisi gelir. Reload kapalı, rezerv yok, her zaman tek elli.</summary>
        Disposable,

        /// <summary>Çerçeveden seçilen silah: grip bırakılınca yalnız GİZLENİR, aynı örnek aynı
        /// mermiyle geri gelir. Reload açık, rezerv var, ikinci el ön kabzayı tutabilir.</summary>
        Persistent
    }
}
