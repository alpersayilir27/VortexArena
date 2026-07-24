#nullable enable
using System.Text.Json;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>config/server.json karşılığı. Public alan — JsonUtil (IncludeFields) ile okunur;
/// varsayılanlar ArenaProtocol sabitlerinden gelir.</summary>
public sealed class ServerConfig
{
    public int controlPort = ArenaProtocol.CONTROL_PORT;
    public int beaconPort = ArenaProtocol.UDP_BEACON_PORT;
    public int statePort = ArenaProtocol.STATE_PORT;
    public string venueName = "Dev";
    public int tickHz = ArenaProtocol.SNAPSHOT_RATE_HZ; // Faz 2/3: MatchDirector/StateHost tick'i

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true
    };

    /// <summary>Dosya yoksa varsayılanlarla oluşturur; bozuksa varsayılanlarla devam eder.</summary>
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
