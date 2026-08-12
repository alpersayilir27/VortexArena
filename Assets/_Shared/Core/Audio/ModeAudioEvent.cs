namespace VortexArena.Core.Audio
{
    /// <summary>
    /// Moda/haritaya göre değişen duyuru seslerinin tetikleyicisi. Hangi tetikleyicide hangi
    /// klibin çalacağını <see cref="ModeAudioRegistry"/> tutar, çalan tek yer
    /// <see cref="GameAudio"/>'dur.
    /// <para>
    /// ⚠️ Bu enum serialize edilir (kayıttaki kural satırları ona göre yazılır): <b>yeni değer
    /// SONA eklenir</b>, araya/başa ekleme mevcut asset'teki eşlemeyi kaydırır.
    /// </para>
    /// </summary>
    public enum ModeAudioEvent
    {
        /// <summary>
        /// Faz <c>playing</c>'e geçti. Tek turlu modlarda (tdm, ffa) maç başlangıcı, tur tabanlı
        /// modda (turnuva) <b>her tur</b> başlangıcı — ikisi de aynı geçiştir.
        /// </summary>
        RoundStart = 0,

        /// <summary>
        /// Turun bitmesine <see cref="ModeAudioRegistry.Rule.WarningSeconds"/> kaldı.
        /// <para>Tur tabanlı modda kalan süre turun süresidir; kaydda bu tetikleyiciye kural
        /// yazmak modu "tur tabanlı" ilan etmenin yoludur — <c>modeState</c> çekirdekte
        /// yorumlanmaz.</para>
        /// </summary>
        RoundEndWarning = 1,

        /// <summary>
        /// Maçın bitmesine <see cref="ModeAudioRegistry.Rule.WarningSeconds"/> kaldı.
        /// <see cref="RoundEndWarning"/> için eşleşen kural yoksa devralır.
        /// </summary>
        MatchEndWarning = 2
    }
}
