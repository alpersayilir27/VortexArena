#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>
/// Maç sonucunun tek tipi (§5.3 <c>match_end</c>): kazanan TAKIM ya da kazanan OYUNCU.
/// <para>
/// İkisi tek tipte toplanır çünkü <see cref="IGameMode.IsMatchOver"/> tek bir "bitti mi + kim
/// kazandı" cevabı verir; modun skor kanalı (<see cref="ModeRules.Scoring"/>) hangi alanın dolu
/// olacağını belirler. Bir mod ikisini birden doldurmaz — okuyan istemci dolu olana bakar.
/// </para>
/// </summary>
/// <param name="WinnerTeam">"red" | "blue" | "" (yok/berabere).</param>
/// <param name="WinnerPlayerId">Kazanan oyuncunun playerId'si; 0 = yok/berabere.</param>
public readonly record struct MatchOutcome(string WinnerTeam, int WinnerPlayerId)
{
    /// <summary>Kazanan yok (berabere ya da skor yazılmadan biten maç).</summary>
    public static readonly MatchOutcome Draw = new("", 0);

    /// <summary>Takım skorlu modlar için kısayol.</summary>
    public static MatchOutcome Team(string team) => new(team ?? "", 0);

    /// <summary>Bireysel skorlu modlar için kısayol.</summary>
    public static MatchOutcome Player(int playerId) => new("", playerId);
}
