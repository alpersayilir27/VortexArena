using System.Text;
using VortexArena.Protocol;
using VortexArena.Server.Core;

namespace VortexArena.Server.App;

/// <summary>Konsol sunucusu: config yükle → host'ları başlat → durum satırları bas →
/// Ctrl+C ile temiz kapan. UI YOK — yönetim UI'ı Unity admin build'idir.</summary>
internal static class Program
{
    private static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var configDir = ResolveConfigDir();
        var config = ServerConfig.Load(Path.Combine(configDir, "server.json"));
        var weapons = WeaponTable.Load(Path.Combine(configDir, "weapons.json"));
        // maps.json Unity'den export edilir (Tools > VortexArena > Export Server Config);
        // yoksa tablo boş kalır ve start_match harita doğrulaması atlanır.
        var maps = MapTable.Load(Path.Combine(configDir, "maps.json"));

        using var registry = new PlayerRegistry(Path.Combine(configDir, "devices.json"));
        var director = new MatchDirector(registry, weapons, maps);
        var lobby = new LobbyService(registry, director);
        var control = new ControlHost(registry, lobby, director, config.controlPort);
        var beacon = new BeaconService(config.beaconPort, config.controlPort, config.statePort);
        var stateHost = new StateHost(registry, config.statePort);

        Console.WriteLine("VortexArena Sunucusu");
        Console.WriteLine($"  Mekan      : {config.venueName}");
        Console.WriteLine($"  WS kontrol : http://0.0.0.0:{config.controlPort}{ArenaProtocol.WS_PATH}");
        Console.WriteLine($"  UDP beacon : {config.beaconPort} (her {ArenaProtocol.BEACON_INTERVAL:0} sn)");
        Console.WriteLine($"  UDP state  : {config.statePort}");
        Console.WriteLine($"  Modlar     : {string.Join(", ", director.ModeIds)}");
        Console.WriteLine($"  Silahlar   : {string.Join(", ", weapons.WeaponIds)}");
        Console.WriteLine($"  Haritalar  : {(maps.IsEmpty ? "yok (doğrulama kapalı)" : string.Join(", ", maps.SceneNames))}");
        Console.WriteLine($"  Config     : {configDir}");

        registry.Changed += (player, kind) =>
        {
            var online = registry.Snapshot().Count(p => p.Online);
            switch (kind)
            {
                case PlayerChangeKind.Added:
                    Console.WriteLine($"[+] {player.Name} bağlandı (playerId {player.PlayerId}, rol {player.Role}) — çevrimiçi: {online}");
                    break;
                case PlayerChangeKind.Reconnected:
                    Console.WriteLine($"[+] {player.Name} yeniden bağlandı (playerId {player.PlayerId}) — çevrimiçi: {online}");
                    break;
                case PlayerChangeKind.Offline:
                    Console.WriteLine($"[-] {player.Name} çevrimdışı — çevrimiçi: {online}");
                    break;
                // Updated (status/ready/takım) konsola basılmaz — 5 sn'lik status'larla gürültü olur.
            }
        };
        stateHost.UdpRegistered += (playerId, endpoint) =>
            Console.WriteLine($"[u] UDP kayıt: playerId {playerId} ← {endpoint}");

        await control.StartAsync();
        beacon.Start();
        stateHost.Start();
        director.Start(); // maç tick döngüsü (faz makinesi, 10 Hz)
        Console.WriteLine("Sunucu hazır. Çıkmak için Ctrl+C.");

        var quit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // süreci Windows değil, biz kapatalım
            quit.TrySetResult();
        };
        await quit.Task;

        Console.WriteLine("Kapatılıyor...");
        director.Stop();
        beacon.Stop();
        stateHost.Stop();
        await control.StopAsync();
        Console.WriteLine("Kapandı.");
    }

    /// <summary>`dotnet run` bin/Debug/... içinden çalışır; gerçek dosyalar Server/config/ altındadır.
    /// Exe yanından başlayıp 6 seviye yukarı config/server.json arar; bulunamazsa exe yanında
    /// config/ oluşturur (ServerConfig.Load varsayılanları oraya yazar). Cosmos ConfigLocator deseni.</summary>
    private static string ResolveConfigDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level <= 6 && dir != null; level++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(candidate, "server.json")))
                return candidate;
        }

        var fallback = Path.Combine(AppContext.BaseDirectory, "config");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
