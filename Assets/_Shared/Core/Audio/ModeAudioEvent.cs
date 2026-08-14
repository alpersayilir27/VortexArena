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
        MatchEndWarning = 2,

        /// <summary>
        /// Tur bitti ve <b>arkasından yenisi geliyor</b>: mod duraklatma istedi (faz
        /// <c>playing</c> → <c>paused</c>, <c>phaseReason == "mode"</c>). Tur tabanlı modda
        /// (turnuva) turlar arası toplanmanın başlangıcıdır.
        /// <para>⚠️ <b>Maçı bitiren tur bu tetikleyiciyi ÇALDIRMAZ</b>: orada faz doğrudan
        /// <c>finished</c>'a gider ve duyuruyu maç sonucu (<see cref="GameSoundId"/>) devralır —
        /// "mevzilerinize dönün" denecek bir sonraki tur yok.</para>
        /// </summary>
        RoundEnd = 3
    }
}
