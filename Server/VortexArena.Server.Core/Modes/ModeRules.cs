#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core.Modes;

/// <summary>Takım kipi (§10.5 <c>teamMode</c>): takımsız | kırmızı-mavi.</summary>
public enum TeamMode
{
    None,
    TwoTeams
}

/// <summary>Skor kime yazılır (§10.5 <c>scoring</c>).</summary>
public enum ScoreKind
{
    /// <summary>match_state.scoreRed / scoreBlue.</summary>
    Team,

    /// <summary>lobby_state → PlayerInfo.score (§10.2).</summary>
    Player
}

/// <summary>Canlanma şartı (§10.4/2, §10.5 <c>reviveAnchor</c>).</summary>
public enum ReviveAnchor
{
    /// <summary>Oyuncu kendi BaseZone'una fiziken girer (TDM).</summary>
    OwnBase,

    /// <summary>Oyuncu REVIVE_HOLD_SECONDS boyunca REVIVE_HOLD_RADIUS içinde sabit durur.</summary>
    StandStill
}

/// <summary>Silah nereden gelir (§10.5 <c>weaponSource</c>) — TÜMÜYLE istemci sunumu;
/// sunucuda karşılığı yoktur (§10.3: sunucuda silah tablosu yok).</summary>
public enum WeaponSource
{
    /// <summary>Sahnedeki taban rafları.</summary>
    Rack,

    /// <summary>Modun dağıttığı rastgele silah.</summary>
    RandomGrant
}

/// <summary>
/// Modun ŞEKLİ (Docs/ArenaNet-Protokol.md §10.5) — sunucu-otoriter. Her <see cref="IGameMode"/>
/// bunu döner; <see cref="MatchDirector"/> hem kendi davranışını buna göre kurar hem de
/// <c>load_match.rules</c> / <c>welcome.match.rules</c> ile istemciye yollar.
///
/// <para><b>Varsayılan = bugünkü TDM.</b> Bir mod hiçbir alan yazmazsa bugünkü davranışı alır;
/// yeni mod yalnız FARKLI olduğu alanları belirtir. Bu yüzden bir kural eklemek mevcut modların
/// hiçbirini değiştirmez.</para>
///
/// <para><c>record</c> + <c>init</c> bilinçli: kurallar maç boyunca değişmez, mod bunları bir kez
/// tanımlar. Değiştirilebilir olsalardı "maç ortasında kural değişti mi?" sorusu her tüketiciye
/// bulaşırdı.</para>
/// </summary>
public sealed record ModeRules
{
    public TeamMode Teams { get; init; } = TeamMode.TwoTeams;

    public ScoreKind Scoring { get; init; } = ScoreKind.Team;

    /// <summary>false = aynı takım vuramaz (§10.3/4). Boş takım asla takım arkadaşı sayılmaz.</summary>
    public bool FriendlyFire { get; init; }

    public ReviveAnchor Revive { get; init; } = ReviveAnchor.OwnBase;

    public WeaponSource Weapons { get; init; } = WeaponSource.Rack;

    /// <summary>respawn.delaySeconds + revive_request gecikme eşiği.</summary>
    public float RespawnDelay { get; init; } = ArenaProtocol.RESPAWN_DELAY;

    /// <summary>
    /// Faz <c>playing</c> değilken silah ateşlenebilir mi (§10.5). <c>true</c> = serbest atış alanı:
    /// <c>shot_fired</c> relay edilir ama <b>hasar yine yoktur</b> — <c>hit_report</c> kapısı her
    /// hâlükârda <c>playing</c>'dir (§10.3). Lobi türünün tek farkı budur.
    /// </summary>
    public bool FireWhilePaused { get; init; }

    /// <summary>Bugünkü TDM davranışı — yeni mod bir alanı belirtmezse buraya düşer.</summary>
    public static readonly ModeRules TeamDefault = new();

    /// <summary>
    /// Lobi türünün kural şekli (§10.7): tek farkı serbest atıştır. Lobi bir <see cref="IGameMode"/>
    /// DEĞİLDİR — bu kural yalnız istemciye "burada ateş edebilirsin, ama hasar yok" demek için
    /// telde taşınır.
    /// </summary>
    public static readonly ModeRules LobbyProfile = new() { FireWhilePaused = true };

    /// <summary>Tel formatına çevirir (§10.5). Enum → string: bilinmeyen değer okuyan tarafta
    /// varsayılana düştüğü için sürüm uyumu sayısal enum'dan güvenlidir.</summary>
    public ModeRulesInfo ToInfo() => new()
    {
        teamMode = Teams == TeamMode.None ? "none" : "two",
        scoring = Scoring == ScoreKind.Player ? "player" : "team",
        friendlyFire = FriendlyFire,
        reviveAnchor = Revive == ReviveAnchor.StandStill ? "standstill" : "base",
        weaponSource = Weapons == WeaponSource.RandomGrant ? "random" : "rack",
        respawnDelay = RespawnDelay,
        fireWhilePaused = FireWhilePaused
    };
}
