namespace VortexArena.Core
{
    /// <summary>
    /// Canlanma şartı (§10.4/2, §10.5 <c>reviveAnchor</c>).
    /// <para>
    /// ⚠ FREE-ROAM: ikisi de bir KONUM değişimi değildir — oyuncu fiziksel olarak yürür, hiçbir
    /// şart rig'i taşımaz. Şart yalnız "ne zaman <c>revive_request</c> gönderilir"i belirler.
    /// </para>
    /// <para>⚠ <see cref="ModeDefinition"/> tarafından SERIALIZE edilir — yeni değer SONA eklenir.</para>
    /// </summary>
    public enum ModeReviveAnchor
    {
        /// <summary>Oyuncu kendi <see cref="Arena.BaseZone"/>'una fiziken girer (TDM).</summary>
        OwnBase,

        /// <summary>Oyuncu <c>REVIVE_HOLD_RADIUS</c> içinde <c>REVIVE_HOLD_SECONDS</c> boyunca
        /// kesintisiz sabit durur (takım tabanı olmayan modlar).</summary>
        StandStill,

        /// <summary>
        /// Canlanma YOKTUR (tur tabanlı eleme — <c>tournament</c>). İstemci <c>revive_request</c>
        /// hiç göndermez, sunucu gelirse reddeder ve <c>REVIVE_GRACE</c> zorla canlandırması bu
        /// kipte çalışmaz. Ölü oyuncuyu yalnız modun başlattığı yeni tur canlandırır.
        /// <para>⚠ Enum'un SONUNA eklendi (serialize edilen enum kuralı) — araya eklemek tüm
        /// <see cref="ModeDefinition"/> asset'lerindeki değerleri kaydırırdı.</para>
        /// </summary>
        None
    }
}
