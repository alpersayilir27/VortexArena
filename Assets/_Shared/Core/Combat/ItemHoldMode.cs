namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bir eşyanın kaç elle tutulduğu (<see cref="ItemDefinition.HoldMode"/>).
    /// Uzak çizimin "boş eli kabzaya yapıştırayım mı" kararının statik yarısıdır; dinamik yarısı
    /// telden gelen <c>FLAG_GRIP_LINKED</c> bitidir (Docs/ArenaNet-Protokol.md §6.6).
    /// <para>
    /// ⚠️ <b>Serialize edilen enum</b> — Unity sayısal indeks saklar. Yeni değer yalnız SONA
    /// eklenir; başa/ortaya ekleme mevcut <c>WD_*.asset</c>'lerdeki değerleri sessizce kaydırır.
    /// </para>
    /// </summary>
    public enum ItemHoldMode
    {
        OneHand,
        TwoHand
    }
}
