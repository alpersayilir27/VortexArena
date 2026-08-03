namespace VortexArena.Core
{
    /// <summary>
    /// Oyuncu/marker takımı. <c>Neutral</c> = takımsız (takımsız modlar, §10.5 <c>teamMode:"none"</c>).
    /// <para>
    /// ⚠ <b>Yeni değer SONA eklenir.</b> Bu enum <see cref="Arena.BaseZone"/> tarafından
    /// SERIALIZE edilir ve Unity enum'ları sayısal indeksle saklar; başa/ortaya eklemek mevcut
    /// sahnelerdeki değerleri kaydırır ve her arenanın taban bölgesi takımları bozulur.
    /// </para>
    /// </summary>
    public enum Team { Red, Blue, Neutral }
}
