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
    StandStill,

    /// <summary>
    /// Canlanma YOKTUR (tur tabanlı eleme, <c>tournament</c>). <c>revive_request</c> reddedilir
    /// <b>ve</b> <see cref="ArenaProtocol.REVIVE_GRACE"/> zorla canlandırması çalışmaz — ölü
    /// oyuncuyu yalnız modun başlattığı yeni tur canlandırır.
    /// <para>⚠️ İkisi birden kapatılmadıkça kural işlevsizdir: yollar ayrıdır (talep tabanlı vs
    /// zamanlayıcı tabanlı) ve yalnız birini kapatmak oyuncuyu 20 sn sonra yine canlandırır
    /// (§10.4).</para>
    /// </summary>
    None
}

/// <summary>Silah nereden gelir (§10.5 <c>weaponSource</c>) — TÜMÜYLE istemci sunumu;
/// sunucuda karşılığı yoktur (§10.3: sunucuda silah tablosu yok).</summary>
public enum WeaponSource
{
    /// <summary>Sahnede duran silah — oyuncu onu çerçevesinden alır, silah tükenmez. Yerleşim
    /// arena kararıdır (elle konur); tel değeri <c>"weaponcanvas"</c>.</summary>
    WeaponCanvas,

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
/// <para><c>record</c> + <c>init</c> bilinçli: bir kural şekli oluşturulduktan sonra DEĞİŞMEZ.
/// Maç ortasında değişebilen tek alan <see cref="FriendlyFire"/>'dır ve o da yerinde yazılarak
/// değil, <c>with</c> ile YENİ bir kayıt üretilerek değişir (<c>MatchDirector.ApplyRulesLocked</c>)
/// — tüketiciler yine değişmez bir değer okur.</para>
/// </summary>
public sealed record ModeRules
{
    public TeamMode Teams { get; init; } = TeamMode.TwoTeams;

    public ScoreKind Scoring { get; init; } = ScoreKind.Team;

    /// <summary>false = aynı takım vuramaz (§10.3/4). Boş takım asla takım arkadaşı sayılmaz.
    /// <para>⚠️ <b>Modlar bu alanı YAZMAZ</b> (§5.2): değeri operatörün <c>set_friendly_fire</c>
    /// anahtarı belirler ve <c>MatchDirector.ApplyRulesLocked</c> her kural şekline damgalar. Burada
    /// durmasının sebebi telde taşınması — <c>ModeRulesInfo.friendlyFire</c> "o an geçerli değer"dir.
    /// Bir mod kendi değerini yazarsa anahtar sessizce ezilir.</para></summary>
    public bool FriendlyFire { get; init; }

    public ReviveAnchor Revive { get; init; } = ReviveAnchor.OwnBase;

    public WeaponSource Weapons { get; init; } = WeaponSource.WeaponCanvas;

    /// <summary>respawn.delaySeconds + revive_request gecikme eşiği.</summary>
    public float RespawnDelay { get; init; } = ArenaProtocol.RESPAWN_DELAY;

    /// <summary>
    /// Faz <c>playing</c> değilken silah ateşlenebilir mi (§10.5). <c>true</c> = serbest atış alanı:
    /// atış olayı (UDP <c>0x03</c>/<c>0x04</c>, §6.4/6.5) relay edilir ama <b>hasar yine yoktur</b> —
    /// <c>hit_report</c> kapısı her hâlükârda <c>playing</c>'dir (§10.3). Lobi türünün tek farkı budur.
    /// </summary>
    public bool FireWhilePaused { get; init; }

    /// <summary>Bugünkü TDM davranışı — yeni mod bir alanı belirtmezse buraya düşer.</summary>
    public static readonly ModeRules TeamDefault = new();

    /// <summary>
    /// Lobi türünün kural şekli (§10.7): serbest atış + silahı mod dağıtır. Lobi bir
    /// <see cref="IGameMode"/> DEĞİLDİR — bu kural yalnız istemciye "burada ateş edebilirsin, ama
    /// hasar yok" demek için telde taşınır.
    /// <para>
    /// <c>Weapons</c> bilinçli olarak <see cref="WeaponSource.RandomGrant"/>: lobide oyuncu
    /// grip'e basınca eline rastgele silah gelir, iki lobi sahnesinde silah yerleştirme işi
    /// doğmaz. Varsayılan (<c>WeaponCanvas</c>) bırakılsaydı her lobiye elle silah konması
    /// gerekirdi.
    /// </para>
    /// </summary>
    public static readonly ModeRules LobbyProfile = new()
    {
        FireWhilePaused = true,
        Weapons = WeaponSource.RandomGrant
    };

    /// <summary>Tel formatına çevirir (§10.5). Enum → string: bilinmeyen değer okuyan tarafta
    /// varsayılana düştüğü için sürüm uyumu sayısal enum'dan güvenlidir.</summary>
    public ModeRulesInfo ToInfo() => new()
    {
        teamMode = Teams == TeamMode.None ? "none" : "two",
        scoring = Scoring == ScoreKind.Player ? "player" : "team",
        friendlyFire = FriendlyFire,
        reviveAnchor = Revive switch
        {
            ReviveAnchor.StandStill => "standstill",
            ReviveAnchor.None => "none",
            _ => "base"
        },
        weaponSource = Weapons == WeaponSource.RandomGrant ? "random" : "weaponcanvas",
        respawnDelay = RespawnDelay,
        fireWhilePaused = FireWhilePaused
    };
}
