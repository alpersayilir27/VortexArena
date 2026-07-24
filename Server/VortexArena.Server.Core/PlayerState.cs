#nullable enable
using System.Net;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Bağlanmış (veya daha önce bağlanmış) tek bir cihazın sunucu tarafı görünümü.</summary>
public sealed class PlayerState
{
    public string DeviceId { get; init; } = "";

    /// <summary>Sunucunun welcome'da atadığı 1..MAX_PLAYERS kimliği (UDP paketlerinde 1 bayt).</summary>
    public int PlayerId { get; init; }

    public string Name { get; set; } = "";

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

    /// <summary>welcome'da verilen UDP kayıt jetonu; her yeni hello'da yenilenir.</summary>
    public uint UdpToken { get; set; }

    /// <summary>0x00 UdpHello ile doğrulanmış UDP endpoint'i; kayıt öncesi null.</summary>
    public IPEndPoint? UdpEndpoint { get; set; }

    public ClientConnection? Connection { get; set; }

    /// <summary>lobby_state için tel formatı anlık görüntüsü.</summary>
    public PlayerInfo ToPlayerInfo() => new()
    {
        playerId = PlayerId,
        name = Name,
        role = Role,
        team = Team,
        ready = Ready,
        online = Online,
        battery = Battery,
        scene = Scene
    };
}
