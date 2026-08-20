#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>Free For All: every validated kill gives the killer +1 individual point, no teams; ends
/// on the score limit or on time (highest score wins, a tie at the top is a draw — §10.5
/// <see cref="ScoreKind.Player"/>).</summary>
/// <remarks>Differs from the TDM default (§10.5) only in: no teams, individual score, "stand still"
/// revive, mode-granted weapon, revive delay 0, spawn protection on. Everything else is the
/// <see cref="ModeRules"/> default, so adding this mode changes no line of TDM.</remarks>
public sealed class FfaMode : IGameMode
{
    public string ModeId => "ffa";

    /// <summary>The FFA rule shape.</summary>
    /// <remarks>⚠️ <c>FriendlyFire</c> is not written here (§5.2): it is the operator's session
    /// setting and <c>MatchDirector</c> stamps it onto every rule shape. In FFA it has no visible
    /// effect anyway — an empty team is never a teammate (§10.3/4) and everyone's team is <c>""</c>.
    /// <para><c>RespawnDelay = 0</c> is deliberate: <see cref="ReviveAnchor.StandStill"/> replaces the
    /// wait (<c>REVIVE_HOLD_SECONDS</c> of standing still), so the ~5 s is in the player's hands.</para>
    /// <para>Spawn protection is on (§10.4): revive happens where the player stands, possibly amid
    /// opponents.</para></remarks>
    public ModeRules Rules => new()
    {
        Teams = TeamMode.None,
        Scoring = ScoreKind.Player,
        Revive = ReviveAnchor.StandStill,
        Weapons = WeaponSource.RandomGrant,
        RespawnDelay = 0f,
        SpawnProtectionSeconds = 5f
    };

    public int DefaultRoundSeconds => 300;

    public int DefaultScoreLimit => 20;

    public void OnMatchStart(MatchDirector director) =>
        Console.WriteLine($"[ffa] maç başladı — {director.RoundSeconds} sn, skor limiti {director.ScoreLimit}.");

    public void OnKill(MatchDirector director, int killerId, int victimId, string weaponId)
    {
        // Environmental/suicide deaths score nothing: ownerless kills arrive with killerId 0, and
        // rewarding a self-kill would turn the score into a prize.
        if (killerId <= 0 || killerId == victimId) return;
        director.AddPlayerScore(killerId, 1);
    }

    public bool IsMatchOver(MatchDirector director, out MatchOutcome outcome)
    {
        var limit = director.ScoreLimit;
        // TryGetLeader returns false on a TIE (no single winner); both branches read that as "winner
        // undecided" — silently picking the first player would declare the wrong winner.
        var hasLeader = director.TryGetLeader(out var leaderId, out var leaderScore);

        if (limit > 0 && hasLeader && leaderScore >= limit)
        {
            outcome = MatchOutcome.Player(leaderId);
            return true;
        }

        if (director.TimeRemaining <= 0f)
        {
            // Single player at the top wins; a tie or no players (admin map preview, §10.1) = draw.
            outcome = hasLeader ? MatchOutcome.Player(leaderId) : MatchOutcome.Draw;
            return true;
        }

        outcome = MatchOutcome.Draw;
        return false;
    }
}
