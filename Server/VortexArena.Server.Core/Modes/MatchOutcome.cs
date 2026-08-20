#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>The single match result type (§5.3 <c>match_end</c>): winning TEAM or winning
/// PLAYER.</summary>
/// <remarks>One type because <see cref="IGameMode.IsMatchOver"/> gives a single "is it over + who
/// won" answer; the mode's score channel (<see cref="ModeRules.Scoring"/>) decides which field is
/// filled. A mode never fills both — the client reads whichever is set.</remarks>
/// <param name="WinnerTeam">"red" | "blue" | "" (none/draw).</param>
/// <param name="WinnerPlayerId">The playerId of the winning player; 0 = none/draw.</param>
public readonly record struct MatchOutcome(string WinnerTeam, int WinnerPlayerId)
{
    /// <summary>No winner (a draw, or a match ended before any score).</summary>
    public static readonly MatchOutcome Draw = new("", 0);

    /// <summary>Shortcut for team-scored modes.</summary>
    public static MatchOutcome Team(string team) => new(team ?? "", 0);

    /// <summary>Shortcut for individually-scored modes.</summary>
    public static MatchOutcome Player(int playerId) => new("", playerId);
}
