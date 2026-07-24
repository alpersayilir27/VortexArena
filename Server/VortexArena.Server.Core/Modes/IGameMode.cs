#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>Her oyun modu bu arayüzü uygular ve MatchDirector'a takılır.
/// ModeId, start_match.modeId ve Unity tarafındaki mod kutusu anahtarıyla birebir eşleşir
/// (ör. "tdm"); yeni mod eklerken Docs/ArenaNet-Protokol.md'ye modId işlenir (CLAUDE.md reçetesi).
///
/// <para>Tüm kancalar MatchDirector'ın kilidi DIŞINDA çağrılır; mod, director'ın public API'sini
/// (ScoreRed/ScoreBlue/AddScore/TimeRemaining/OnlinePlayers…) serbestçe kullanabilir — o API
/// kendi kilidini alır. Kancalar İÇİNDE bloklayan iş yapılmaz (maç tick'i 10 Hz).</para></summary>
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

    /// <summary>Maç tick'inde (10 Hz) yalnız Live fazında çağrılır; süreyi MatchDirector işletir.</summary>
    void OnTick(MatchDirector director, float deltaSeconds);

    /// <summary>Doğrulanmış her hasar sonrası (öldürme olsun olmasın) çağrılır — hasar bazlı
    /// puanlama/istatistik için. TDM gibi modlar boş bırakabilir.</summary>
    void OnHitApplied(MatchDirector director, int attackerId, int targetId, float damage, bool killed);

    /// <summary>Doğrulanmış bir öldürme sonrası skor kuralları.</summary>
    void OnKill(MatchDirector director, int killerId, int victimId, string weaponId);

    /// <summary>true dönerse MatchDirector fazı End'e taşır (süre doldu / skor limiti).
    /// winnerTeam: "red" | "blue" | "" (berabere).</summary>
    bool IsMatchOver(MatchDirector director, out string winnerTeam);
}
