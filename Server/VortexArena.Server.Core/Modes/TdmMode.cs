#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>Team Deathmatch: her doğrulanmış öldürme, ÖLDÜRENİN takımına +1 puan yazar.
/// Maç, bir takım skor limitine ulaşınca ya da süre bitince biter; süre bitiminde yüksek skor
/// kazanır, eşitlikte berabere (<see cref="MatchOutcome.Draw"/>).
/// <para>Kurallar varsayılandan yalnız doğma korumasıyla ayrılır
/// (<see cref="ModeRules.TeamDefault"/>): iki takım, takım skoru, dost ateşi kapalı, kendi
/// tabanında canlanma, sahnede duran silah.</para></summary>
public sealed class TdmMode : IGameMode
{
    public string ModeId => "tdm";

    /// <summary>⚠️ Doğma koruması TDM'de zorunludur (§10.4): canlanma kendi tabanında olduğu için
    /// tabanı gözleyen bir rakip canlanan oyuncuyu doğduğu karede vurabilirdi.</summary>
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
        // Öldüren ayrılmış/takımsızsa puan yazılmaz (skor sahibi kalmadı).
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
