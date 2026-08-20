#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>Team Deathmatch: every validated kill gives the killer's team +1; ends on the score limit
/// or on time (higher score wins, a tie is <see cref="MatchOutcome.Draw"/>).</summary>
/// <remarks>Differs from <see cref="ModeRules.TeamDefault"/> only by spawn protection: two teams, team
/// score, friendly fire off, revive at the own base, weapon standing in the scene.</remarks>
public sealed class TdmMode : IGameMode
{
    public string ModeId => "tdm";

    /// <summary>⚠️ Spawn protection is mandatory in TDM (§10.4): revive happens at the own base, so an
    /// opponent watching it could shoot the player on the spawn frame.</summary>
    public ModeRules Rules => ModeRules.TeamDefault with { SpawnProtectionSeconds = 5f };

    public int DefaultRoundSeconds => 300;

    public int DefaultScoreLimit => 30;

    public void OnMatchStart(MatchDirector director) =>
        Console.WriteLine($"[tdm] maç başladı — {director.RoundSeconds} sn, skor limiti {director.ScoreLimit}.");

    public void OnKill(MatchDirector director, int killerId, int victimId, string weaponId)
    {
        var team = "";
        foreach (var player in director.ConnectedPlayers())
        {
            if (player.PlayerId != killerId) continue;
            team = player.Team;
            break;
        }
        // Killer left or has no team: no point (there is no score owner).
        if (team != "red" && team != "blue") return;
        director.AddScore(team, 1);
    }

    public bool IsMatchOver(MatchDirector director, out MatchOutcome outcome)
    {
        var red = director.ScoreRed;
        var blue = director.ScoreBlue;
        var limit = director.ScoreLimit;

        if (limit > 0 && red >= limit)
        {
            outcome = MatchOutcome.Team("red");
            return true;
        }
        if (limit > 0 && blue >= limit)
        {
            outcome = MatchOutcome.Team("blue");
            return true;
        }
        if (director.TimeRemaining <= 0f)
        {
            outcome = red > blue ? MatchOutcome.Team("red")
                : blue > red ? MatchOutcome.Team("blue")
                : MatchOutcome.Draw;
            return true;
        }

        outcome = MatchOutcome.Draw;
        return false;
    }
}
