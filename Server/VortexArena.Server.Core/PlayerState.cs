#nullable enable
using System.Net;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Bağlanmış (veya daha önce bağlanmış) tek bir cihazın sunucu tarafı görünümü.</summary>
public sealed class PlayerState
{
    public string DeviceId { get; init; } = "";

    /// <summary>Sunucunun welcome'da atadığı 1..PLAYER_ID_MAX kimliği (UDP paketlerinde 1 bayt).</summary>
    public int PlayerId { get; init; }

    public string Name { get; set; } = "";

    /// <summary>Forma numarası 1..99 (§2); 0 = atanmamış. Admin'de daima 0 (admin oynamaz).
    /// <b>Tüm kayıtlı cihazlar arasında benzersizdir</b> — yalnız çevrimiçiler arasında değil.</summary>
    public int Number { get; set; }

    /// <summary>"player" (VR/Quest) veya "admin" (Windows masaüstü).</summary>
    public string Role { get; set; } = "player";

    /// <summary>"red" | "blue"; admin oynamadığı için admin'de boş kalır.</summary>
    public string Team { get; set; } = "";

    public bool Ready { get; set; }
    public bool Online { get; set; }

    /// <summary>0–1 aralığı; -1 = bilinmiyor.</summary>
    public float Battery { get; set; } = -1f;

    public float Fps { get; set; }
    public string Scene { get; set; } = "";

    /// <summary>hello'da bildirilen build sahne listesi (admin katalog doğrulaması için).</summary>
    public List<string> Scenes { get; set; } = new();

    /// <summary>Son hello/status'un UTC zamanı (OFFLINE_TIMEOUT süpürmesi buna bakar).</summary>
    public DateTime LastSeen { get; set; }

    // ---- Maç durumu (§10.2) ----
    // Bu alanların TEK yazarı MatchDirector'dır ve hepsi onun _gate kilidi altında okunur/yazılır.
    // (StateHost yalnız Alive'ı kilitsiz okur — bool okuması atomik, bir tik gecikme önemsiz.)

    /// <summary>0..PLAYER_MAX_HP; Live'a girerken ve canlanmada tam cana çekilir.</summary>
    public float Hp { get; set; } = ArenaProtocol.PLAYER_MAX_HP;

    /// <summary>Snapshot flags bit0 (FLAG_ALIVE) bunu besler; Lobby fazında herkes canlıdır.</summary>
    public bool Alive { get; set; } = true;

    public int Kills { get; set; }
    public int Deaths { get; set; }

    /// <summary>Bireysel maç skoru (§10.2). Kills ile aynı şey DEĞİLDİR: yazarı IGameMode'dur
    /// (MatchDirector'ın skor defteri üzerinden, _gate altında) ve anlamı mod başına değişir.</summary>
    public int Score { get; set; }

    /// <summary>Son ölümün UTC zamanı (RESPAWN_DELAY + REVIVE_GRACE hesabı, §10.4).</summary>
    public DateTime DiedAt { get; set; }

    // ---- Kalibrasyon durumu (§10.6) ----
    // MAÇ DURUMU DEĞİL cihaz durumudur: yazarı MatchDirector değil PlayerRegistry'dir
    // (SetCalibration, registry'nin kendi _gate'i altında) ve maç sıfırlamalarında KORUNUR.
    // MatchDirector bunları kendi _gate'i altında yalnız OKUR — Team ile birebir aynı desen;
    // bool okuması atomik olduğu için iki kilidi birbirine bağlamaya gerek yoktur (bağlamak
    // kilitlenme riski üretir, kazancı sıfırdır).

    /// <summary>Başlığın arena ile hizalı olduğunu bildirip bildirmediği. hello'da false'a
    /// çekilir (§10.6): sunucu yeniden bağlanan bir başlığın hizalamasını bilemez.
    /// false iken oyuncu ateş edemez, hasar yemez ve canlanamaz.</summary>
    public bool Calibrated { get; set; }

    /// <summary>"manual" | "anchor" | "cloud" | "" — doğrulanmayan serbest etiket (§5.1).</summary>
    public string CalibrationSource { get; set; } = "";

    /// <summary>welcome'da verilen UDP kayıt jetonu; her yeni hello'da yenilenir.</summary>
    public uint UdpToken { get; set; }

    /// <summary>0x00 UdpHello ile doğrulanmış UDP endpoint'i; kayıt öncesi null.</summary>
    public IPEndPoint? UdpEndpoint { get; set; }

    /// <summary>Poz yaz/oku kilidi — UDP recv thread'i ile snapshot timer'ı farklı thread'ler;
    /// ~90 B'lik PoseUpdate struct'ında tearing olmasın.</summary>
    public object PoseGate { get; } = new();

    /// <summary>En az bir geçerli PoseUpdate alındı mı (LastPose ancak o zaman okunur).</summary>
    public bool HasPose { get; set; }

    /// <summary>Son kabul edilen poz (arena uzayında; PoseGate altında oku/yaz).</summary>
    public PoseUpdate LastPose { get; set; }

    /// <summary>Son kabul edilen pozun sıra numarası (u16 sarmalamalı eskilik kontrolü için).</summary>
    public ushort LastSeq { get; set; }

    /// <summary>Son kabul edilen pozun UTC zamanı.</summary>
    public DateTime LastPoseAt { get; set; }

    public ClientConnection? Connection { get; set; }

    /// <summary>lobby_state için tel formatı anlık görüntüsü.</summary>
    public PlayerInfo ToPlayerInfo() => new()
    {
        playerId = PlayerId,
        name = Name,
        number = Number,
        role = Role,
        team = Team,
        ready = Ready,
        online = Online,
        battery = Battery,
        scene = Scene,
        // §10.2 sayaçları: admin istatistik tablosu bunları okur (§5.3 lobby_state).
        kills = Kills,
        deaths = Deaths,
        hp = Hp,
        alive = Alive,
        score = Score,
        // §10.6 — admin gözlemci arayüzündeki kalibrasyon tik'i bunu okur.
        calibrated = Calibrated,
        calibrationSource = CalibrationSource
    };
}
