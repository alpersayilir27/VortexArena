#nullable enable
namespace VortexArena.Server.Core;

/// <summary>Connection state of a record (§2), carried on the wire as the
/// <c>PlayerInfo.connection</c> string.</summary>
/// <remarks>There is NO "offline" value — a dropped device is either awaited back or removed.
/// ⚠️ Not serialized on the Unity side (the wire format is a string), but new values still go at the
/// END: anything comparing or logging by numeric index would silently shift.</remarks>
public enum PlayerConnection
{
    /// <summary>Socket alive; all match gates open.</summary>
    Connected,

    /// <summary>Socket dropped (disconnect or HEARTBEAT_TIMEOUT); awaited back for RECONNECT_GRACE.
    /// The record stays but does not pass the match gates.</summary>
    Reconnecting,

    /// <summary>Grace expired, player dropped from the game. Only match participants reach this state
    /// (§10.2) — others are removed entirely.</summary>
    Left
}
