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

        /// <summary>Maç bitti, kazanan KIRMIZI takım.</summary>
        TeamRedWon = 4,

        /// <summary>Maç bitti, kazanan MAVİ takım.</summary>
        TeamBlueWon = 5,

        /// <summary>Geri sayımın her saniyesi.</summary>
        CountdownTick = 6,

        /// <summary>Bir oyuncunun fiziksel ihlali başladı (§10.9) — <b>yalnız admin PC'sinde</b>
        /// çalar; oyuncunun uyarısı zaten kendi ekranındadır. Sesin kapısı ve en fazla ne sıklıkta
        /// çalacağı <c>AdminRoster</c>'dadır: bu enum yalnız hangi klibin çalacağını söyler.</summary>
        AdminViolation = 7,

        /// <summary>Maç berabere bitti — kazanan takım da kazanan oyuncu da yok.</summary>
        MatchDraw = 8,

        /// <summary>Yerel oyuncu kendi TAKIM ARKADAŞINI öldürdü (dost ateşi açıkken).
        /// <para>⚠️ <see cref="EnemyEliminated"/>'in yerine geçer, ona ek DEĞİL: bir öldürme
        /// olayında ikisinden yalnız biri çalar.</para></summary>
        TeammateEliminated = 9,
    }
}
