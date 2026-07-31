#nullable enable
namespace VortexArena.Server.Core.Modes;

/// <summary>Her oyun modu bu arayüzü uygular ve MatchDirector'a takılır.
/// ModeId, start_match.modeId ve Unity tarafındaki mod kutusu anahtarıyla birebir eşleşir
/// (ör. "tdm"); yeni mod eklerken Docs/ArenaNet-Protokol.md'ye modId işlenir (CLAUDE.md reçetesi).
///
/// <para>Tüm kancalar MatchDirector'ın kilidi DIŞINDA çağrılır; mod, director'ın public API'sini
/// (ScoreRed/ScoreBlue/AddScore/AddPlayerScore/TimeRemaining/OnlinePlayers…) serbestçe
/// kullanabilir — o API kendi kilidini alır. Kancalar İÇİNDE bloklayan iş yapılmaz (maç tik'i 10 Hz).</para>
///
/// <para><b>Yeni kanca eklerken VARSAYILAN GÖVDE kullan</b> (default interface method): böylece
/// mevcut modların hiçbiri değişmez, yalnız ilgilenen mod override eder. Tersi — her modun boş
/// uygulamak zorunda kaldığı ölü kanca — her yeni moda ödenen bir vergidir. Aynı sebeple
/// <b>tüketicisi olmayan kanca EKLENMEZ</b>: sonradan eklemek bu kural sayesinde ücretsizdir.</para></summary>
public interface IGameMode
{
    /// <summary>start_match.modeId ile eşleşen anahtar.</summary>
    string ModeId { get; }

    /// <summary>Modun ŞEKLİ (§10.5): takım kipi, skor kanalı, dost ateşi, canlanma, silah kaynağı.
    /// MatchDirector hem kendini buna göre kurar hem de load_match.rules ile istemciye yollar.
    /// Bugünkü TDM davranışı için <see cref="ModeRules.TeamDefault"/> döndürmek yeterlidir.</summary>
    ModeRules Rules { get; }

    /// <summary>load_match.roundSeconds için varsayılan tur süresi. <b>Kilit değil varsayılan:</b>
    /// admin start_match.roundSeconds ile o maça özel bir değer verebilir (§5.2).</summary>
    int DefaultRoundSeconds { get; }

    /// <summary>load_match.scoreLimit için varsayılan skor limiti (aynı şekilde ezilebilir).</summary>
    int DefaultScoreLimit { get; }

    /// <summary>Maç boyunca BİR KEZ, ilk kez Live'a geçerken çağrılır.
    /// <para>⚠️ Tur tabanlı modda (<c>tournament</c>) sonraki turların Live girişleri bunu
    /// TEKRAR TETİKLEMEZ — "maç başladı" ile "tur başladı" ayrı olaylardır
    /// (bkz. <see cref="OnRoundStart"/>).</para></summary>
    void OnMatchStart(MatchDirector director);

    /// <summary>Faz Live'a HER girişte çağrılır — maçın ilk turu dahil, <see cref="OnMatchStart"/>
    /// hemen ardından. Hasarın açıldığı andır.
    /// <para>Tur kavramı olmayan modlar bunu hiç yazmaz (maçta bir kez çağrılır, yok sayılır).</para></summary>
    void OnRoundStart(MatchDirector director) { }

    /// <summary>true dönerse MatchDirector fazı End'e taşır (süre doldu / skor limiti).
    /// Kazanan <see cref="MatchOutcome"/> ile ifade edilir: takım skorlu modlarda WinnerTeam,
    /// bireysel skorlu modlarda WinnerPlayerId (bkz. <see cref="ModeRules.Scoring"/>).</summary>
    bool IsMatchOver(MatchDirector director, out MatchOutcome outcome);

    /// <summary>Doğrulanmış bir öldürme sonrası skor kuralları. Skoru YALNIZ director'ın skor
    /// defterinden yaz (AddScore / AddPlayerScore).</summary>
    void OnKill(MatchDirector director, int killerId, int victimId, string weaponId) { }

    /// <summary>Maç tik'inde (10 Hz) yalnız Live fazında çağrılır; süreyi MatchDirector işletir.
    /// Zamana bağlı kuralı olmayan mod bunu hiç yazmaz.</summary>
    void OnTick(MatchDirector director, float deltaSeconds) { }

    /// <summary>Doğrulanmış her hasar sonrası (öldürme olsun olmasın) çağrılır — hasar bazlı
    /// puanlama/istatistik için. İlgilenmeyen mod hiç yazmaz.</summary>
    void OnHitApplied(MatchDirector director, int attackerId, int targetId, float damage, bool killed) { }
}
