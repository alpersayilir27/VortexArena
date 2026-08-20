namespace VortexArena.Core
{
    /// <summary>
    /// The mode's team mode (Docs/ArenaNet-Protokol.md §10.5 <c>teamMode</c>).
    /// The client counterpart of <c>ModeRules.Teams</c> on the server.
    /// <para>⚠ SERIALIZED by <see cref="ModeDefinition"/> — new values are appended at the END.</para>
    /// </summary>
    public enum ModeTeamMode
    {
        /// <summary>Red/blue; the server balances the teams and the spawn slot is within the team.</summary>
        TwoTeams,

        /// <summary>No teams (<c>team:""</c>); the spawn slot comes from a single pool.</summary>
        None
    }
}
