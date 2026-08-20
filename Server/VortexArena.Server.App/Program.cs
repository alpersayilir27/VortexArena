using System.Text;
using VortexArena.Protocol;
using VortexArena.Server.Core;

namespace VortexArena.Server.App;

/// <summary>Console server: load config → start hosts → print status → close cleanly on Ctrl+C.</summary>
/// <remarks>No UI — the management UI is the Unity admin build.</remarks>
internal static class Program
{
    /// <summary>Exit code: <c>0</c> clean shutdown, <c>2</c> startup validation failed (§11 fail-fast)
    /// so scripts/launcher can tell them apart.</summary>
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var configDir = ResolveConfigDir();
        var config = ServerConfig.Load(Path.Combine(configDir, "server.json"));
        // No weapon table (§10.3): the client computes damage, the server applies it as-is.
        // maps.json is exported from Unity (Tools > VortexArena > Server > Export Server Config);
        // without it the table stays empty and start_match skips map validation.
        var allMaps = MapTable.Load(Path.Combine(configDir, "maps.json"));

        // Venue selection (§11): only the chosen venue's maps are playable and visible to admins this
        // session. An empty table leaves nothing to choose.
        var maps = SelectVenue(allMaps, ArgValue(args, "--venue") ?? config.venue);

        using var registry = new PlayerRegistry(Path.Combine(configDir, "devices.json"));
        var director = new MatchDirector(registry, maps, config.lobbyScene);

        // ⚠️ Fail-fast (§11): the server's open scene is the client's only routing source
        // (welcome.match.sceneName). If it cannot be resolved the configuration is already broken, and
        // opening silently with an empty scene would carry that error into the field.
        if (!ValidateLobbyScene(director.LobbyScene, maps, config.lobbyScene)) return 2;
        var lobby = new LobbyService(registry, director);
        var control = new ControlHost(registry, lobby, director, config.controlPort);
        var beacon = new BeaconService(config.beaconPort, config.controlPort, config.statePort);
        // director is mandatory: StateHost reads the 0x03 shot relay gate (phase +
        // rules.fireWhilePaused) lock-free via MatchDirector.ShotRelayOpen (§6.5/§10.3). As a settable
        // property, forgetting to wire it would silently drop the events.
        var stateHost = new StateHost(registry, config.statePort, director);

        Console.WriteLine("VortexArena Sunucusu");
        Console.WriteLine($"  Mekan      : {config.venueName}");
        Console.WriteLine($"  Aktif alan : {(string.IsNullOrEmpty(maps.Venue) ? "(mekan ayrımı yok)" : maps.Venue)}");
        Console.WriteLine($"  WS kontrol : http://0.0.0.0:{config.controlPort}{ArenaProtocol.WS_PATH}");
        Console.WriteLine($"  UDP beacon : {config.beaconPort} (her {ArenaProtocol.BEACON_INTERVAL:0} sn)");
        Console.WriteLine($"  UDP state  : {config.statePort}");
        Console.WriteLine($"  Modlar     : {string.Join(", ", director.ModeIds)}");
        Console.WriteLine($"  Haritalar  : {(maps.IsEmpty ? "yok (doğrulama kapalı)" : string.Join(", ", maps.SceneNames))}");
        Console.WriteLine($"  Lobi       : {DescribeLobby(director.LobbyScene, maps)}");
        Console.WriteLine("  Hasar      : istemci bildirir (silah tablosu ve hile denetimi yok)");
        Console.WriteLine($"  Config     : {configDir}");

        registry.Changed += (player, kind) =>
        {
            var connected = registry.Snapshot().Count(p => p.IsConnected);
            switch (kind)
            {
                case PlayerChangeKind.Added:
                    Console.WriteLine($"[+] {player.Name} bağlandı (playerId {player.PlayerId}, rol {player.Role}) — bağlı: {connected}");
                    break;
                case PlayerChangeKind.Reconnected:
                    Console.WriteLine($"[+] {player.Name} yeniden bağlandı (playerId {player.PlayerId}) — bağlı: {connected}");
                    break;
                case PlayerChangeKind.Reconnecting:
                    Console.WriteLine($"[-] {player.Name} bağlantı koptu, yeniden bekleniyor " +
                                      $"({ArenaProtocol.RECONNECT_GRACE:0} sn) — bağlı: {connected}");
                    break;
                case PlayerChangeKind.Left:
                    // Record NOT removed: as a match participant its row stays until the match ends (§10.2).
                    Console.WriteLine($"[-] {player.Name} oyundan çıkarıldı (maç istatistiği korunuyor) — bağlı: {connected}");
                    break;
                case PlayerChangeKind.Removed:
                    // Admin (session-scoped identity, §2), kicked player (§5.4) and an expired
                    // non-participant: the record is removed entirely.
                    Console.WriteLine($"[-] {player.Name} ayrıldı, kaydı silindi (playerId {player.PlayerId} havuza döndü) — bağlı: {connected}");
                    break;
                // Updated (status/ready/team) is not printed — the 5 s statuses would be noise.
            }
        };
        stateHost.UdpRegistered += (playerId, endpoint) =>
            Console.WriteLine($"[u] UDP kayıt: playerId {playerId} ← {endpoint}");

        await control.StartAsync();
        beacon.Start();
        stateHost.Start();
        director.Start(); // match tick loop (phase machine, 10 Hz)
        lobby.Start(); // net_stats broadcast (admins only, 1 Hz)
        Console.WriteLine("Sunucu hazır. Çıkmak için Ctrl+C.");

        var quit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // we close the process, not Windows
            quit.TrySetResult();
        };
        await quit.Task;

        Console.WriteLine("Kapatılıyor...");
        lobby.Stop();
        director.Stop();
        beacon.Stop();
        stateHost.Stop();
        await control.StopAsync();
        Console.WriteLine("Kapandı.");
        return 0;
    }

    /// <summary>Reads a <c>--key value</c> argument; null if absent.</summary>
    private static string? ArgValue(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1].Trim();
        return null;
    }

    /// <summary>Picks this session's venue and narrows the map table to it (§11).</summary>
    /// <remarks>Order: <c>--venue</c> / <c>server.json → venue</c> → the only venue → ask on the
    /// console. The question is asked only on an interactive console: with redirected input (service,
    /// script, launcher) the server does not block but opens with the first venue and logs it.
    /// <para>An unrecognised name still leads to the question — opening another venue silently would
    /// show the operator the wrong arenas.</para></remarks>
    private static MapTable SelectVenue(MapTable all, string? preferred)
    {
        if (all.IsEmpty || all.Venues.Count == 0)
        {
            Console.WriteLine("[Venue] maps.json boş — mekan seçimi atlandı (harita doğrulaması kapalı).");
            return all;
        }

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            foreach (var v in all.Venues)
            {
                if (string.Equals(v, preferred.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[Venue] '{v}' yapılandırmadan seçildi (soru atlandı).");
                    return all.ForVenue(v);
                }
            }
            Console.WriteLine($"[Venue] '{preferred}' tanınmıyor — bilinen mekanlar: {string.Join(", ", all.Venues)}.");
        }

        if (all.Venues.Count == 1)
        {
            Console.WriteLine($"[Venue] Tek mekan var: '{all.Venues[0]}'.");
            return all.ForVenue(all.Venues[0]);
        }

        if (Console.IsInputRedirected)
        {
            Console.WriteLine($"[Venue] Konsol etkileşimli değil — '{all.Venues[0]}' ile açılıyor " +
                              "(seçmek için: --venue <ad> ya da server.json → venue).");
            return all.ForVenue(all.Venues[0]);
        }

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("Hangi mekan açılsın?");
            for (int i = 0; i < all.Venues.Count; i++)
            {
                var sub = all.ForVenue(all.Venues[i]);
                Console.WriteLine($"  {i + 1}) {all.Venues[i]}  ({sub.SceneNames.Count} harita)");
            }
            Console.Write($"Seçim [1-{all.Venues.Count}]: ");

            string? line = Console.ReadLine();
            if (line == null)
            {
                // Input stream closed (Ctrl+Z / end of pipe): continue with the first one, do not block.
                Console.WriteLine($"[Venue] Girdi yok — '{all.Venues[0]}' seçildi.");
                return all.ForVenue(all.Venues[0]);
            }

            line = line.Trim();
            if (line.Length == 0) line = "1";

            if (int.TryParse(line, out int index) && index >= 1 && index <= all.Venues.Count)
                return all.ForVenue(all.Venues[index - 1]);

            // Accept the name instead of the number — the operator is looking at the list anyway.
            foreach (var v in all.Venues)
                if (string.Equals(v, line, StringComparison.OrdinalIgnoreCase)) return all.ForVenue(v);

            Console.WriteLine("Geçersiz seçim.");
        }
    }

    /// <summary>The "Lobi" line of the startup log (§10.7).</summary>
    private static string DescribeLobby(string lobbyScene, MapTable maps)
    {
        if (maps.IsEmpty)
            return $"{lobbyScene} (maps.json yok — doğrulanamadı)";
        return lobbyScene;
    }

    /// <summary>Guarantees the open scene (§11 fail-fast); false makes the process exit with
    /// <c>2</c>.</summary>
    /// <remarks>The open scene is the client's ONLY routing source (<c>welcome.match.sceneName</c>) —
    /// if it cannot be resolved the server must not open at all, else players wait in a shell lobby and
    /// the error is only noticed in the field.
    /// <para>Exception: with no <c>maps.json</c> the table is empty and all validation is already off
    /// (§11) — blocking the server there would break the development flow, so it only warns.</para></remarks>
    private static bool ValidateLobbyScene(string resolved, MapTable maps, string? configured)
    {
        if (maps.IsEmpty)
        {
            Console.WriteLine("[Lobi] ⚠ maps.json yok — açık sahne doğrulanamadı. " +
                              "Tools > VortexArena > Server > Export Server Config çalıştırın.");
            return true;
        }

        if (string.IsNullOrEmpty(resolved))
        {
            Console.WriteLine();
            Console.WriteLine("HATA: Bu mekanın açık sahnesi belirlenemedi — sunucu açılmıyor.");
            Console.WriteLine($"  Mekan '{maps.Venue}' içinde lobi haritası yok " +
                              "(MapDefinition.supportedModeIds == [\"lobby\"] olan bir arena).");
            Console.WriteLine("  Çözüm: bu mekana bir lobi arenası ekleyip " +
                              "Tools > VortexArena > Server > Export Server Config çalıştırın, " +
                              "ya da server.json → lobbyScene alanına mevcut bir sahne yazın.");
            Console.WriteLine($"  Bu mekanda bilinen haritalar: {string.Join(", ", maps.SceneNames)}");
            return false;
        }

        if (!maps.TryGet(resolved, out _))
        {
            Console.WriteLine();
            Console.WriteLine("HATA: Açık sahne harita tablosunda yok — sunucu açılmıyor.");
            Console.WriteLine($"  İstenen sahne: '{resolved}'" +
                              (string.IsNullOrWhiteSpace(configured) ? "" : " (server.json → lobbyScene)"));
            Console.WriteLine($"  Mekan '{maps.Venue}' içinde bilinen haritalar: {string.Join(", ", maps.SceneNames)}");
            Console.WriteLine("  Çözüm: adı düzeltin ya da Export Server Config çalıştırın " +
                              "(maps.json Unity'den üretilir, elle düzenlenmez).");
            return false;
        }

        return true;
    }

    /// <summary>Finds config/server.json by walking up to 6 levels from the exe, else creates config/
    /// next to the exe (cosmos ConfigLocator pattern).</summary>
    /// <remarks>Needed because `dotnet run` runs from bin/Debug/… while the real files live under
    /// Server/config/. ServerConfig.Load writes the defaults into the fallback.</remarks>
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
