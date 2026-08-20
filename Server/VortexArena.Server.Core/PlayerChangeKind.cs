#nullable enable
namespace VortexArena.Server.Core;

/// <summary>The reason behind the PlayerRegistry.Changed event.</summary>
public enum PlayerChangeKind
{
    /// <summary>First connection (a new playerId was allocated).</summary>
    Added,

    /// <summary>A known deviceId reconnected (old socket closed, playerId preserved).</summary>
    Reconnected,

    /// <summary>Roster data changed (status, name, ready, team).</summary>
    Updated,

    /// <summary>Connection dropped or no status for HEARTBEAT_TIMEOUT: the device is awaited back for
    /// RECONNECT_GRACE (§2).</summary>
    Reconnecting,

    /// <summary>Record removed entirely: admin disconnect (session-scoped identity, leaves no ghost
    /// row — Docs/ArenaNet-Protokol.md §2), a kicked player (§5.4), or an expired RECONNECT_GRACE on a
    /// NON-participant.</summary>
    Removed,

    /// <summary>RECONNECT_GRACE expired on a match participant: dropped from the game, but name and
    /// counters stay in the table until the match ends (§10.2).
    /// <para>⚠️ New values go to the END of the enum.</para></summary>
    Left
}
