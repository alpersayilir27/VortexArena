#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>Faz 3: her oyun modu bu arayüzü uygular ve MatchDirector'a takılır.
/// ModeId, start_match.modeId ve Unity tarafındaki mod kutusu anahtarıyla birebir eşleşir
/// (ör. "tdm"); yeni mod eklerken Docs/ArenaNet-Protokol.md'ye modId işlenir (CLAUDE.md reçetesi).</summary>
public interface IGameMode
{
    /// <summary>start_match.modeId ile eşleşen anahtar.</summary>
    string ModeId { get; }

    /// <summary>load_match.roundSeconds için varsayılan tur süresi.</summary>
    int DefaultRoundSeconds { get; }

    /// <summary>load_match.scoreLimit için varsayılan skor limiti.</summary>
    int DefaultScoreLimit { get; }

    /// <summary>Faz Live'a geçerken bir kez çağrılır.</summary>
    void OnMatchStart(MatchDirector director);

    /// <summary>Sunucu tick'inde (config.tickHz) çağrılır; süre/skor ilerletme burada.</summary>
    void OnTick(MatchDirector director, float deltaSeconds);

    /// <summary>Doğrulanmış bir öldürme sonrası skor kuralları.</summary>
    void OnKill(MatchDirector director, int killerId, int victimId, string weaponId);

    /// <summary>true dönerse MatchDirector fazı End'e taşır (süre doldu / skor limiti).</summary>
    bool IsMatchOver(MatchDirector director);
}
