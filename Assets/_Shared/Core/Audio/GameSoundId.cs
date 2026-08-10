namespace VortexArena.Core.Audio
{
    /// <summary>
    /// Haritadan ve moddan bağımsız, <b>tüm oyunda ortak</b> geri bildirim sesleri.
    /// Klipleri <see cref="GameSoundBank"/> taşır, çalan tek yer <see cref="GameAudio"/>'dur.
    /// <para>
    /// ⚠️ Bu enum serialize edilir (<see cref="GameSoundBank"/> alanları ona göre yazılır):
    /// <b>yeni değer SONA eklenir</b>, araya/başa ekleme mevcut asset'lerdeki eşlemeyi kaydırır.
    /// </para>
    /// </summary>
    public enum GameSoundId
    {
        /// <summary>Yerel oyuncu bir rakibi öldürdü ("rakip elendi").</summary>
        EnemyEliminated = 0,

        /// <summary>Yerel oyuncu öldü.</summary>
        LocalDeath = 1,

        /// <summary>Yerel oyuncu canlandı.</summary>
        LocalRespawn = 2,

        /// <summary>Faz <c>playing</c>'e geçti (maç başladı / turdan devam edildi).</summary>
        MatchStart = 3,

        /// <summary>Maç bitti, yerel oyuncu kazanan tarafta.</summary>
        MatchWin = 4,

        /// <summary>Maç bitti, yerel oyuncu kazanan tarafta değil.</summary>
        MatchLose = 5,

        /// <summary>Geri sayımın her saniyesi.</summary>
        CountdownTick = 6,
    }
}
