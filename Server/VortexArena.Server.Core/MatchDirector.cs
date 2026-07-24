#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Maç fazları (§5.3): Lobby → Loading → Countdown → Live → End → Lobby.</summary>
public enum Phase
{
    Lobby,
    Loading,
    Countdown,
    Live,
    End
}

/// <summary>Faz 3 iskeleti: maç yaşam döngüsü (load_match/countdown/match_state yayınları),
/// IGameMode kancaları ve sunucu-otoriter skor/can kuralları burada toplanacak.
/// Faz 1'de yalnız welcome.match için mevcut durumu üretir.</summary>
public sealed class MatchDirector
{
    private readonly object _gate = new();

    private Phase _phase = Phase.Lobby;
    private string _modeId = "";
    private string _sceneName = "";
    private float _timeRemaining = 0f; // açık başlangıç değerleri: alanlar Faz 3'e dek yalnız okunur
    private int _scoreRed = 0;
    private int _scoreBlue = 0;

    public Phase CurrentPhase
    {
        get { lock (_gate) return _phase; }
    }

    /// <summary>welcome.match anlık görüntüsü — geç katılım senkronu bunu kullanır (§5.3).</summary>
    public MatchInfo CurrentMatchInfo()
    {
        lock (_gate)
        {
            return new MatchInfo
            {
                phase = _phase.ToString(),
                modeId = _modeId,
                sceneName = _sceneName,
                timeRemaining = _timeRemaining,
                scoreRed = _scoreRed,
                scoreBlue = _scoreBlue
            };
        }
    }

    // ---- Faz 3: aşağıdaki komutlar gerçek akışa bağlanacak (Lobby→Loading→Countdown→Live→End). ----

    public void StartMatch(string modeId, string sceneName) =>
        Console.WriteLine($"[MatchDirector] start_match (mod '{modeId}', sahne '{sceneName}') Faz 3'te uygulanacak — yok sayıldı.");

    public void AbortMatch() =>
        Console.WriteLine("[MatchDirector] abort_match Faz 3'te uygulanacak — yok sayıldı.");

    public void ReturnToLobby() =>
        Console.WriteLine("[MatchDirector] return_to_lobby Faz 3'te uygulanacak — yok sayıldı.");
}
