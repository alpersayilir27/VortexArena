#nullable enable
using System.Text.Json;
using VortexArena.Protocol;
using VortexArena.Server.Core.Modes;

namespace VortexArena.Server.Core;

/// <summary>The counterpart of config/server.json. Public fields — read through JsonUtil
/// (IncludeFields); the defaults come from the ArenaProtocol constants.</summary>
public sealed class ServerConfig
{
    public int controlPort = ArenaProtocol.CONTROL_PORT;
    public int beaconPort = ArenaProtocol.UDP_BEACON_PORT;
    public int statePort = ArenaProtocol.STATE_PORT;
    public string venueName = "Dev";
    public int tickHz = ArenaProtocol.SNAPSHOT_RATE_HZ; // the MatchDirector/StateHost tick

    /// <summary>Venue to select at startup (§11); empty = asked on the console every startup, filled
    /// = question skipped (automation/kiosk). <c>--venue &lt;name&gt;</c> overrides even this.</summary>
    public string venue = "";

    /// <summary>This venue's lobby scene (§10.7); empty = auto-resolved (the venue's map with
    /// <c>modes:["lobby"]</c>).</summary>
    /// <remarks>Fill in only for a venue with several lobbies or to play another one deliberately;
    /// the name must match a <c>sceneName</c> in <c>maps.json</c>.</remarks>
    public string lobbyScene = "";

    /// <summary>Hamburgerci balance (<c>burger</c> block); absent = the defaults in
    /// <see cref="BurgerSettings"/>.</summary>
    public BurgerSettings burger = new();

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true
    };

    /// <summary>Creates the file with defaults if missing; continues with defaults if malformed.</summary>
    public static ServerConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonUtil.Deserialize<ServerConfig>(File.ReadAllText(path));
                if (loaded != null) return loaded;
                Console.WriteLine($"[ServerConfig] {path} çözümlenemedi — varsayılanlar kullanılıyor.");
                return new ServerConfig();
            }

            var defaults = new ServerConfig();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, WriteOptions));
            Console.WriteLine($"[ServerConfig] {path} yoktu — varsayılanlarla oluşturuldu.");
            return defaults;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ServerConfig] okuma hatası ({ex.Message}) — varsayılanlar kullanılıyor.");
            return new ServerConfig();
        }
    }
}
