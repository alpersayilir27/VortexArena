using System.Text;
using VortexArena.Protocol;
using VortexArena.Server.Core;

namespace VortexArena.Server.App;

/// <summary>Konsol sunucusu: config yükle → host'ları başlat → durum satırları bas →
/// Ctrl+C ile temiz kapan. UI YOK — yönetim UI'ı Unity admin build'idir.</summary>
internal static class Program
{
    /// <summary>Çıkış kodu: <c>0</c> temiz kapanış, <c>2</c> açılış doğrulaması başarısız
    /// (§11 fail-fast) — betikler/launcher bunu ayırt edebilsin.</summary>
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var configDir = ResolveConfigDir();
        var config = ServerConfig.Load(Path.Combine(configDir, "server.json"));
        // Silah tablosu YOK (§10.3): hasarı istemci hesaplar, sunucu aynen uygular.
        // maps.json Unity'den export edilir (Tools > VortexArena > Server > Export Server Config);
        // yoksa tablo boş kalır ve start_match harita doğrulaması atlanır.
        var allMaps = MapTable.Load(Path.Combine(configDir, "maps.json"));

        // Mekan seçimi (§11): bu oturumda YALNIZ seçilen mekanın haritaları oynatılabilir ve
        // adminlere yalnız onlar görünür. Tablo boşsa seçilecek bir şey yoktur.
        var maps = SelectVenue(allMaps, ArgValue(args, "--venue") ?? config.venue);

        using var registry = new PlayerRegistry(Path.Combine(configDir, "devices.json"));
        var director = new MatchDirector(registry, maps, config.lobbyScene);

        // ⚠️ Fail-fast (§11): sunucunun AÇIK SAHNESİ istemcinin tek yönlendirme kaynağıdır
        // (welcome.match.sceneName). Çözülemiyorsa zaten bir yapılandırma hatası vardır ve oyuncu
        // doğru oynayamaz — sessizce boş sahneyle açılmak hatayı sahaya taşır.
        if (!ValidateLobbyScene(director.LobbyScene, maps, config.lobbyScene)) return 2;
        var lobby = new LobbyService(registry, director);
        var control = new ControlHost(registry, lobby, director, config.controlPort);
        var beacon = new BeaconService(config.beaconPort, config.controlPort, config.statePort);
        // director ZORUNLU: StateHost 0x03 atış olayının relay kapısını (faz + rules.fireWhilePaused)
        // MatchDirector.ShotRelayOpen üzerinden KİLİTSİZ okur (§6.5/§10.3). Sonradan set edilen bir
        // property olsaydı kurulumu unutmak olayları sessizce düşürürdü.
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
                case PlayerChangeKind.Removed:
                    // Yalnız admin: kimliği oturumluk olduğu için kaydı tümüyle silinir (§2).
                    Console.WriteLine($"[-] {player.Name} ayrıldı, kaydı silindi (playerId {player.PlayerId} havuza döndü) — çevrimiçi: {online}");
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
        lobby.Start(); // net_stats yayını (yalnız adminlere, 1 Hz)
        Console.WriteLine("Sunucu hazır. Çıkmak için Ctrl+C.");

        var quit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // süreci Windows değil, biz kapatalım
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

    /// <summary><c>--anahtar deger</c> biçimindeki argümanı okur; yoksa null.</summary>
    private static string? ArgValue(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1].Trim();
        return null;
    }

    /// <summary>
    /// Bu oturumda hangi mekanın oynatılacağını belirler ve harita tablosunu ona daraltır (§11).
    /// <para>Sıra: <c>--venue</c> / <c>server.json → venue</c> → tek mekan varsa o → konsolda sor.
    /// <b>Soru yalnız konsol etkileşimliyse sorulur</b>: girdi yönlendirilmişse (servis, betik,
    /// launcher) sunucu bloklanmaz, ilk mekanla açılır ve bunu loglar.</para>
    /// <para>Yazılan ad tanınmazsa yine sorulur — sessizce başka bir mekanı açmak, operatörün
    /// yanlış arenaları görmesi demek olurdu.</para>
    /// </summary>
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
                // Girdi akışı kapandı (Ctrl+Z / boru sonu): bloklamadan ilkiyle devam et.
                Console.WriteLine($"[Venue] Girdi yok — '{all.Venues[0]}' seçildi.");
                return all.ForVenue(all.Venues[0]);
            }

            line = line.Trim();
            if (line.Length == 0) line = "1";

            if (int.TryParse(line, out int index) && index >= 1 && index <= all.Venues.Count)
                return all.ForVenue(all.Venues[index - 1]);

            // Numara yerine adı da yazılabilsin — operatör listeye bakıyor zaten.
            foreach (var v in all.Venues)
                if (string.Equals(v, line, StringComparison.OrdinalIgnoreCase)) return all.ForVenue(v);

            Console.WriteLine("Geçersiz seçim.");
        }
    }

    /// <summary>Açılış logundaki "Lobi" satırı (§10.7).</summary>
    private static string DescribeLobby(string lobbyScene, MapTable maps)
    {
        if (maps.IsEmpty)
            return $"{lobbyScene} (maps.json yok — doğrulanamadı)";
        return lobbyScene;
    }

    /// <summary>
    /// Açık sahne garantisi (§11 fail-fast). Sunucunun açık sahnesi istemcinin TEK yönlendirme
    /// kaynağıdır (<c>welcome.match.sceneName</c>) — çözülemiyorsa sunucu hiç açılmamalıdır,
    /// yoksa oyuncular hiçbir sahneye gidemeden kabuk lobide bekler ve hata sahada fark edilir.
    /// <para><b>İstisna:</b> <c>maps.json</c> hiç yoksa harita tablosu boştur ve doğrulamanın
    /// tamamı zaten kapalıdır (§11) — o yapılandırmada sunucuyu kilitlemek geliştirme akışını
    /// kırardı, yalnız uyarılır.</para>
    /// <para>false dönerse çağıran süreç <c>2</c> ile kapanır.</para>
    /// </summary>
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
