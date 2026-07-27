namespace VortexArena.Core
{
    /// <summary>
    /// Modun takım kipi (Docs/ArenaNet-Protokol.md §10.5 <c>teamMode</c>).
    /// Sunucudaki <c>ModeRules.Teams</c>'in istemci karşılığıdır.
    /// <para>⚠ <see cref="ModeDefinition"/> tarafından SERIALIZE edilir — yeni değer SONA eklenir.</para>
    /// </summary>
    public enum ModeTeamMode
    {
        /// <summary>Kırmızı/mavi; sunucu takımları dengeler, spawn slotu takım içidir.</summary>
        TwoTeams,

        /// <summary>Takım yok (<c>team:""</c>); spawn slotu tek havuzdan gelir.</summary>
        None
    }
}
