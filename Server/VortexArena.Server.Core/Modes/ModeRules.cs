#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core.Modes;

/// <summary>Team mode (§10.5 <c>teamMode</c>): teamless | red-blue.</summary>
public enum TeamMode
{
    None,
    TwoTeams
}

/// <summary>Who the score is written to (§10.5 <c>scoring</c>).</summary>
public enum ScoreKind
{
    /// <summary>match_state.scoreRed / scoreBlue.</summary>
    Team,

    /// <summary>lobby_state → PlayerInfo.score (§10.2).</summary>
    Player,

    /// <summary>Co-op: the contribution goes to PlayerInfo.score, the shared total to
    /// match_state.scoreRed; scoreBlue stays 0 and there is no winner. Wire value <c>"shared"</c>.</summary>
    PlayerAndShared
}

/// <summary>Revive condition (§10.4/2, §10.5 <c>reviveAnchor</c>).</summary>
public enum ReviveAnchor
{
    /// <summary>The player physically enters their own BaseZone (TDM).</summary>
    OwnBase,

    /// <summary>The player stands still within REVIVE_HOLD_RADIUS for REVIVE_HOLD_SECONDS.</summary>
    StandStill,

    /// <summary>NO revive (round based elimination, <c>tournament</c>): <c>revive_request</c> is
    /// rejected, only the mode's new round revives the dead (§10.4).</summary>
    None
}

/// <summary>Where the weapon comes from (§10.5 <c>weaponSource</c>) — entirely client presentation,
/// with no server counterpart (§10.3: no weapon table on the server).</summary>
public enum WeaponSource
{
    /// <summary>A weapon standing in the scene, taken from its frame and never used up; placement is
    /// an arena decision. Wire value <c>"weaponcanvas"</c>.</summary>
    WeaponCanvas,

    /// <summary>A random weapon granted by the mode.</summary>
    RandomGrant,

    /// <summary>No weapon: the frame is hidden, nothing is granted, the trigger stays silent. Wire
    /// value <c>"none"</c>.</summary>
    None
}

/// <summary>The mode's SHAPE (Docs/ArenaNet-Protokol.md §10.5) — server-authoritative, returned by
/// every <see cref="IGameMode"/>.</summary>
/// <remarks><see cref="MatchDirector"/> configures itself from it and sends it via
/// <c>load_match.rules</c> / <c>welcome.match.rules</c>.
/// <para>The default = today's TDM: a mode specifies only the fields where it DIFFERS, so adding a
/// rule changes none of the existing modes.</para>
/// <para><c>record</c> + <c>init</c> is deliberate: a rule shape is immutable once created. Only
/// <see cref="FriendlyFire"/> changes mid-match, and via <c>with</c> into a NEW record
/// (<c>MatchDirector.ApplyRulesLocked</c>), so consumers still read an immutable value.</para></remarks>
public sealed record ModeRules
{
    public TeamMode Teams { get; init; } = TeamMode.TwoTeams;

    public ScoreKind Scoring { get; init; } = ScoreKind.Team;

    /// <summary>false = teammates cannot hit each other (§10.3/4); an empty team is never a
    /// teammate.</summary>
    /// <remarks>⚠️ Modes do NOT write this (§5.2) — the operator's <c>set_friendly_fire</c> switch
    /// decides it and <c>MatchDirector.ApplyRulesLocked</c> stamps every rule shape. It sits here only
    /// because it is carried on the wire (<c>ModeRulesInfo.friendlyFire</c> = the value in effect); a
    /// mode writing its own value silently overwrites the switch.</remarks>
    public bool FriendlyFire { get; init; }

    public ReviveAnchor Revive { get; init; } = ReviveAnchor.OwnBase;

    public WeaponSource Weapons { get; init; } = WeaponSource.WeaponCanvas;

    /// <summary>respawn.delaySeconds + the revive_request delay threshold.</summary>
    public float RespawnDelay { get; init; } = ArenaProtocol.RESPAWN_DELAY;

    /// <summary>Can the weapon fire while the phase is not <c>playing</c> (§10.5)?</summary>
    /// <remarks><c>true</c> = free firing range: the shot event (UDP <c>0x03</c>/<c>0x04</c>,
    /// §6.4/6.5) is relayed but there is still NO damage — the <c>hit_report</c> gate is always
    /// <c>playing</c> (§10.3). This is the lobby profile's only difference.</remarks>
    public bool FireWhilePaused { get; init; }

    /// <summary>Seconds a revived player takes no damage; <c>0</c> = no protection (§10.4) and the
    /// default, so modes that ignore it are unaffected.</summary>
    /// <remarks>⚠️ Not on the wire (absent from <see cref="ToInfo"/>): the client has no use for the
    /// duration — it reads the protection from snapshot bit6
    /// (<see cref="SnapshotEntry.FLAG_SPAWN_PROTECTED"/>) and only draws it. Sending the number too
    /// would be a second source of truth.</remarks>
    public float SpawnProtectionSeconds { get; init; }

    /// <summary>Today's TDM behaviour — the fallback for any field a mode does not specify.</summary>
    public static readonly ModeRules TeamDefault = new();

    /// <summary>The lobby profile's rule shape (§10.7): free firing + mode-granted weapon.</summary>
    /// <remarks>The lobby is NOT an <see cref="IGameMode"/> — this rule goes on the wire only to tell
    /// the client "you can shoot here, but there is no damage".
    /// <para><c>Weapons</c> is deliberately <see cref="WeaponSource.RandomGrant"/>: grip gives a random
    /// weapon, so no lobby needs hand-placed weapons as the default (<c>WeaponCanvas</c>) would
    /// require.</para></remarks>
    public static readonly ModeRules LobbyProfile = new()
    {
        FireWhilePaused = true,
        Weapons = WeaponSource.RandomGrant
    };

    /// <summary>The lobby profile of a KIDS family map (§10.7): no weapon at all.</summary>
    /// <remarks>The normal lobby profile hands out a random weapon on grip — in a children's game there
    /// must be no weapon during the WAIT either, not just during the match.
    /// <para><c>FireWhilePaused</c> stays false: with no weapon there is nothing to fire.
    /// <c>weaponSource:"none"</c> alone closes both grant paths on the client, so no extra client
    /// rule is needed.</para></remarks>
    public static readonly ModeRules KidsLobbyProfile = new()
    {
        Weapons = WeaponSource.None
    };

    /// <summary>Converts to the wire format (§10.5); enum → string, because an unknown value falls
    /// back to the default on the reading side — safer across versions than a numeric enum.</summary>
    public ModeRulesInfo ToInfo() => new()
    {
        teamMode = Teams == TeamMode.None ? "none" : "two",
        scoring = Scoring switch
        {
            ScoreKind.Player => "player",
            ScoreKind.PlayerAndShared => "shared",
            _ => "team"
        },
        friendlyFire = FriendlyFire,
        reviveAnchor = Revive switch
        {
            ReviveAnchor.StandStill => "standstill",
            ReviveAnchor.None => "none",
            _ => "base"
        },
        weaponSource = Weapons switch
        {
            WeaponSource.RandomGrant => "random",
            WeaponSource.None => "none",
            _ => "weaponcanvas"
        },
        respawnDelay = RespawnDelay,
        fireWhilePaused = FireWhilePaused
    };
}
