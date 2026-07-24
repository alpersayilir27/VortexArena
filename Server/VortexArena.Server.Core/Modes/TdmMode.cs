#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>Team Deathmatch: her doğrulanmış öldürme, ÖLDÜRENİN takımına +1 puan yazar.
/// Maç, bir takım skor limitine ulaşınca ya da süre bitince biter; süre bitiminde yüksek skor
/// kazanır, eşitlikte berabere (winnerTeam = "").</summary>
public sealed class TdmMode : IGameMode
{
    public string ModeId => "tdm";

    public int DefaultRoundSeconds => 300;

    public int DefaultScoreLimit => 30;

    public void OnMatchStart(MatchDirector director) =>
        Console.WriteLine($"[tdm] maç başladı — {director.RoundSeconds} sn, skor limiti {director.ScoreLimit}.");

    /// <summary>TDM'de zamana bağlı kural yok; süreyi MatchDirector işletir.</summary>
    public void OnTick(MatchDirector director, float deltaSeconds) { }

    /// <summary>TDM hasarı ayrıca puanlamaz (yalnız öldürme sayılır).</summary>
    public void OnHitApplied(MatchDirector director, int attackerId, int targetId, float damage, bool killed) { }

    public void OnKill(MatchDirector director, int killerId, int victimId, string weaponId)
    {
        var team = "";
        foreach (var player in director.OnlinePlayers())
        {
            if (player.PlayerId != killerId) continue;
            team = player.Team;
            break;
        }
        // Öldüren ayrılmış/takımsızsa puan yazılmaz (skor sahibi kalmadı).
        if (team != "red" && team != "blue") return;
        director.AddScore(team, 1);
    }

    public bool IsMatchOver(MatchDirector director, out string winnerTeam)
    {
        var red = director.ScoreRed;
        var blue = director.ScoreBlue;
        var limit = director.ScoreLimit;

        if (limit > 0 && red >= limit)
        {
            winnerTeam = "red";
            return true;
        }
        if (limit > 0 && blue >= limit)
        {
            winnerTeam = "blue";
            return true;
        }
        if (director.TimeRemaining <= 0f)
        {
            winnerTeam = red > blue ? "red" : blue > red ? "blue" : "";
            return true;
        }

        winnerTeam = "";
        return false;
    }
}
