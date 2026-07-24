namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Yeni arenanın hangi kutuya üretileceği:
    /// <see cref="Standard"/> → <c>Assets/Arenas/Standard/&lt;arenaId&gt;</c>,
    /// <see cref="Venue"/> → <c>Assets/Arenas/Venues/&lt;venueName&gt;</c> (işletmeye özel alan).
    /// </summary>
    public enum ArenaTemplateTarget
    {
        /// <summary>Standart (katalog) arena kutusu.</summary>
        Standard,

        /// <summary>İşletmeye özel (venue) arena kutusu.</summary>
        Venue
    }
}
