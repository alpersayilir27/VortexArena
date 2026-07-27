namespace VortexArena.Core
{
    /// <summary>
    /// Oyuncu/marker takımı. <c>Neutral</c> = takımsız (takımsız modlar, §10.5 <c>teamMode:"none"</c>).
    /// <para>
    /// ⚠ <b>Yeni değer SONA eklenir.</b> Bu enum <see cref="Arena.BaseZone"/>,
    /// <see cref="Arena.SpawnPoint"/> ve <see cref="Combat.Weapon"/> tarafından SERIALIZE edilir;
    /// Unity enum'ları sayısal indeksle saklar. Başa/ortaya eklemek mevcut sahnelerdeki değerleri
    /// kaydırır ve her arenanın taban/spawn takımları bozulur.
    /// </para>
    /// </summary>
    public enum Team { Red, Blue, Neutral }
}
