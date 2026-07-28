#nullable enable
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using VortexArena.Protocol;

namespace VortexArena.PoseBot;

/// <summary>Sentetik oyuncu test istemcisi: WS hello → welcome → UDP kayıt → 20 Hz dairesel
/// yürüyüş pozu. Gerçek Quest olmadan poz senkronunu (snapshot, uzak avatar, taktik görünüm)
/// uçtan uca test etmek için. <c>--fight</c> ile bot ayrıca maça katılır (set_ready), karşı
/// takıma ateş eder (shot_fired + hit_report), ölür ve canlanma talebi gönderir — tam bir TDM
/// raundu Quest olmadan denenebilsin diye. <c>--admin</c> ile ayrıca tek bir admin bağlantısı
/// açılır ve maçı kendisi başlatır (Unity editörü oyuncu rolündeyken admin istemcisi kalmıyor).
/// Kullanım: PoseBot [ip] [botSayısı] [--fight] [--admin].</summary>
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    /// <summary>--admin bağlantısının o anki oturumu (stdin okuyucusu buradan gönderir).
    /// Yeniden bağlanmada tazelenir; birden çok stdin okuyucusu açılmasın diye statik.</summary>
    private static volatile BotSession? _adminSession;

    /// <summary>Gerçek istemcinin Build Settings sahne listesi. Sunucu <c>start_match</c>'te
    /// istenen sahnenin TÜM çevrimiçi oyuncuların hello.scenes'inde olmasını arar (§10.1) —
    /// bot burayı eksik bildirirse maç hiç başlamaz. Yeni arena eklendiğinde buraya da ekleyin.</summary>
    private static readonly string[] BuildScenes =
        { "Boot", "Lobby", "Arena10x10", "Arena12x12", "ArenaDemoVenue", "IceWorld" };

    /// <summary>Tek çağrıda açılabilecek bot sayısı üst sınırı. <b>Protokol kotası değildir</b> —
    /// protokolde eşzamanlı oyuncu sınırı yok (§2), bu yalnız yazım hatasına karşı dev aracı
    /// emniyeti (127 yerine 12 yazılması makine boğmasın).</summary>
    private const int MaxDevBots = 32;

    // ---- Savaş ayarları (§10.3) — sunucu hasarı doğrulamaz, bildirdiğimizi uygular ----
    private const string WeaponId = "ak47";  // yalnız etiket (kill feed); sunucu doğrulamaz
    private const float WeaponDamage = 34f;  // botun uyguladığı hasar — serbest seçilir
    private const float MuzzleHeight = 1.3f; // namlu yüksekliği (arena uzayı, y)
    private const int ReviveAttempts = 5;    // revive_request tekrar sayısı

    // ---- --admin ayarları ----
    /// <summary>--admin'in başlattığı maçın modu/sahnesi. Varsayılan tdm/Arena10x10; başka arena
    /// veya mod denemek için <c>--map &lt;sahneAdı&gt;</c> / <c>--mode &lt;modId&gt;</c>.
    /// Sahne adı hem BuildScenes'te hem sunucunun maps.json'ında olmalı.</summary>
    private static string AdminModeId = "tdm";
    private static string AdminSceneName = "Arena10x10";

    /// <summary>Admin kimliği OTURUMLUKtur (§2, gerçek admin istemcisiyle aynı kural): birden
    /// çok PoseBot --admin ya da PoseBot + gerçek admin aynı anda koşabilsin. Süreç ömrü boyunca
    /// sabittir ki yeniden bağlanma aynı kaydı bulsun.</summary>
    private static readonly string AdminDeviceId = $"posebot-admin-{Guid.NewGuid():N}";
    /// <summary>Roster'da 2+ çevrimiçi oyuncu bu kadar kararlı kaldıktan sonra start_match.</summary>
    private const double AdminStartDelay = 2.0;
    private const int AdminMinPlayers = 2;

    /// <summary>Bot atış aralığı (sn). Sunucuda atış hızı denetimi YOK (§10.3) — bu değer yalnız
    /// skorun okunabilir hızda ilerlemesi için: saniyede ~2 atış.</summary>
    private static readonly double FireInterval = 0.5;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Any(a => a is "--help" or "-h" or "/?")) { PrintUsage(); return 0; }
        if (!TryParseArgs(args, out var ip, out var count, out var fight, out var admin)) { PrintUsage(); return 1; }

        Console.WriteLine($"PoseBot: {count} bot → {ip}:{ArenaProtocol.CONTROL_PORT} (durdurmak için Ctrl+C)");
        Console.WriteLine(fight
            ? "Savaş modu AÇIK: yalnız ÇİFT indeksli botlar ateş eder (bot0, bot2…), tek indeksliler kurban. " +
              $"Silah {WeaponId}, {WeaponDamage:0} hasar, {FireInterval:0.00} sn aralık."
            : "Savaş modu kapalı (--fight ile açılır): yalnız poz akışı.");
        if (admin)
            Console.WriteLine($"Admin istemcisi AÇIK: roster'da {AdminMinPlayers}+ oyuncu görününce " +
                              $"start_match ({AdminModeId} / {AdminSceneName}); maçı elle bitirmek için q + Enter.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var tasks = Enumerable.Range(0, count).Select(i => RunBotAsync(ip, i, fight, cts.Token)).ToList();
        if (admin)
        {
            tasks.Add(RunAdminAsync(ip, cts.Token));
            _ = AdminInputAsync(cts.Token); // stdin okuyucusu tek sefer açılır (oturumdan bağımsız)
        }
        await Task.WhenAll(tasks);
        Console.WriteLine("PoseBot kapandı.");
        return 0;
    }

    /// <summary>Bayrak sırası serbest: ip ve botSayısı sıradaki konumsal argümanlardır.</summary>
    private static bool TryParseArgs(string[] args, out string ip, out int count, out bool fight, out bool admin)
    {
        ip = "127.0.0.1";
        count = 1;
        fight = false;
        admin = false;

        var positional = 0;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--fight", StringComparison.OrdinalIgnoreCase)) { fight = true; continue; }
            if (arg.Equals("--admin", StringComparison.OrdinalIgnoreCase)) { admin = true; continue; }
            if (arg.Equals("--map", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) { Console.WriteLine("--map bir sahne adı bekler."); return false; }
                AdminSceneName = args[++i];
                if (!BuildScenes.Contains(AdminSceneName, StringComparer.Ordinal))
                    Console.WriteLine($"UYARI: '{AdminSceneName}' bot'un BuildScenes listesinde yok — sunucu start_match'i reddeder.");
                continue;
            }
            if (arg.Equals("--mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length) { Console.WriteLine("--mode bir modId bekler."); return false; }
                AdminModeId = args[++i];
                continue;
            }
            if (arg.StartsWith('-')) { Console.WriteLine($"Bilinmeyen bayrak: {arg}"); return false; }

            switch (positional++)
            {
                case 0:
                    ip = arg;
                    break;
                case 1:
                    if (!int.TryParse(arg, out var n)) { Console.WriteLine($"Bot sayısı sayı olmalı: {arg}"); return false; }
                    count = Math.Clamp(n, 1, MaxDevBots);
                    break;
                default:
                    Console.WriteLine($"Fazladan argüman: {arg}");
                    return false;
            }
        }
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Kullanım: PoseBot [ip] [botSayısı] [--fight] [--admin] [--map <sahne>] [--mode <modId>]");
        Console.WriteLine("  ip          sunucu IP'si (varsayılan 127.0.0.1)");
        Console.WriteLine($"  botSayısı   1..{MaxDevBots} (varsayılan 1)");
        Console.WriteLine("  --fight     maça katıl: sahne yüklendi (set_ready), ateş (shot_fired+hit_report),");
        Console.WriteLine("              ölüm/canlanma (revive_request). Yalnız ÇİFT indeksli botlar ateş eder.");
        Console.WriteLine("  --admin     ek bir admin bağlantısı aç: 2+ oyuncu görününce start_match");
        Console.WriteLine($"              ({AdminModeId} / {AdminSceneName}); konsolda q + Enter → abort_match.");
        Console.WriteLine($"  --map       --admin'in başlatacağı arena sahnesi (varsayılan {AdminSceneName});");
        Console.WriteLine($"              geçerli: {string.Join(", ", BuildScenes.Skip(3))}");
        Console.WriteLine($"  --mode      --admin'in başlatacağı mod (varsayılan {AdminModeId})");
        Console.WriteLine("  --help      bu yardım");
        Console.WriteLine("Bayrak sırası serbest: \"PoseBot --fight 192.168.1.10 4 --admin\" da geçerlidir.");
    }

    private static async Task RunBotAsync(string ip, int index, bool fight, CancellationToken ct)
    {
        var tag = $"[bot{index}]";
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(ip, index, tag, fight, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"{tag} kopma: {ex.Message} — 2 sn sonra yeniden.");
            }
            try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private static async Task RunSessionAsync(string ip, int index, string tag, bool fight, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://{ip}:{ArenaProtocol.CONTROL_PORT}{ArenaProtocol.WS_PATH}"), ct);

        var bot = new BotSession(ws, index, tag, fight, verbose: index == 0);
        var hello = new HelloMsg
        {
            protocolVersion = ArenaProtocol.PROTOCOL_VERSION,
            role = "player",
            deviceId = $"posebot-{index:00}",
            deviceName = $"PoseBot {index:00}",
            appVersion = "posebot",
            currentScene = "Lobby",
            scenes = BuildScenes
        };
        await bot.SendAsync(hello, ct);

        // ⚠️ ZORUNLU: sunucu hello'da kalibrasyonu sıfırlar (§10.6) ve kalibresiz oyuncu ateş
        // edemez, hasar yemez, canlanamaz. Bu satır olmadan --fight botlarının TÜM hit_report'ları
        // "kalibresiz" diye reddedilir ve bot vurulamaz — dev penceresindeki bot düğmeleri sessizce
        // işe yaramaz hâle gelir. Bot'un fiziksel bir başlığı yok, hep hizalı sayılır.
        await bot.SendAsync(new SetCalibrationMsg { calibrated = true, source = "manual" }, ct);
        Console.WriteLine($"{tag} bağlandı, hello + set_calibration gönderildi.");

        using var udp = new UdpClient(0);
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? poseTask = null;
        Task? fireTask = null;

        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        var lastStatus = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), sessionCts.Token);
            if (result.MessageType == WebSocketMessageType.Close) break;
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);

            string? type;
            using (var doc = JsonDocument.Parse(json))
                type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == MessageTypes.Welcome && poseTask == null && Parse<WelcomeMsg>(json) is { } welcome)
            {
                bot.PlayerId = (byte)welcome.playerId;
                Console.WriteLine($"{tag} welcome: playerId {bot.PlayerId}.");
                // Geç katılım: sunucuda maç sürüyorsa faz buradan gelir (§5.3).
                if (bot.Fight && welcome.match != null && !string.IsNullOrEmpty(welcome.match.phase))
                    bot.Phase = welcome.match.phase;

                poseTask = Task.Run(() => PoseLoopAsync(udp, ip, bot, welcome.udpToken, sessionCts.Token), sessionCts.Token);
                if (bot.Shooter)
                    fireTask = Task.Run(() => FireLoopAsync(bot, sessionCts.Token), sessionCts.Token);
            }
            else if (bot.Fight)
            {
                // Maç akışı yalnız savaş modunda işlenir; --fight yoksa bot birebir eski
                // davranışını sürdürür (yalnız poz akışı, hiçbir ek mesaj göndermez).
                HandleMessage(bot, type, json, sessionCts.Token);
            }

            if (type == MessageTypes.Ping || DateTime.UtcNow - lastStatus > TimeSpan.FromSeconds(ArenaProtocol.STATUS_INTERVAL))
            {
                lastStatus = DateTime.UtcNow;
                await bot.SendAsync(new StatusMsg { scene = bot.Scene, battery = 1f, fps = 72f }, ct);
            }
        }

        sessionCts.Cancel();
        if (poseTask != null) { try { await poseTask; } catch (OperationCanceledException) { } }
        if (fireTask != null) { try { await fireTask; } catch (OperationCanceledException) { } }
    }

    // ================= --admin: maçı başlatan sentetik yönetici istemcisi =================
    // Editör oyuncu rolündeyken ortamda admin kalmadığı için (start_match yalnız role=admin'den
    // kabul edilir, §5.2) PoseBot tek bir admin bağlantısı açar. Admin poz göndermez, UDP
    // kaydı yapmaz — yalnız kontrol kanalı.

    private static async Task RunAdminAsync(string ip, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunAdminSessionAsync(ip, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[admin] kopma: {ex.Message} — 2 sn sonra yeniden.");
            }
            try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private static async Task RunAdminSessionAsync(string ip, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://{ip}:{ArenaProtocol.CONTROL_PORT}{ArenaProtocol.WS_PATH}"), ct);

        // Admin de Lobby'de başlar ve sunucunun load_match'iyle arena sahnesine geçer (§2).
        var admin = new BotSession(ws, 0, "[admin]", fight: false, verbose: true) { Scene = "Lobby" };
        _adminSession = admin;

        var hello = new HelloMsg
        {
            protocolVersion = ArenaProtocol.PROTOCOL_VERSION,
            role = "admin",
            deviceId = AdminDeviceId,
            deviceName = "PoseBot Admin",
            appVersion = "posebot",
            currentScene = "Lobby",
            scenes = BuildScenes
        };
        await admin.SendAsync(hello, ct);
        Console.WriteLine("[admin] bağlandı, hello gönderildi.");

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var director = Task.Run(() => AdminDirectorAsync(admin, sessionCts.Token), sessionCts.Token);

        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();
        var lastStatus = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), sessionCts.Token);
            if (result.MessageType == WebSocketMessageType.Close) break;
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);

            string? type;
            using (var doc = JsonDocument.Parse(json))
                type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;

            if (type == MessageTypes.Welcome && Parse<WelcomeMsg>(json) is { } welcome)
            {
                admin.PlayerId = (byte)welcome.playerId;
                if (welcome.match != null && !string.IsNullOrEmpty(welcome.match.phase))
                    admin.Phase = welcome.match.phase; // sürmekte olan maça geç katılım
                Console.WriteLine($"[admin] welcome: playerId {admin.PlayerId}, faz {admin.Phase}.");
            }
            else
            {
                HandleAdminMessage(admin, type, json);
            }

            if (type == MessageTypes.Ping || DateTime.UtcNow - lastStatus > TimeSpan.FromSeconds(ArenaProtocol.STATUS_INTERVAL))
            {
                lastStatus = DateTime.UtcNow;
                await admin.SendAsync(new StatusMsg { scene = admin.Scene, battery = 1f, fps = 60f }, ct);
            }
        }

        sessionCts.Cancel();
        try { await director; } catch (OperationCanceledException) { }
    }

    /// <summary>Admin'in izlediği mesajlar: roster (oyuncu sayımı) + maç akışı satırları.
    /// Bu satırları admin HER ZAMAN yazar, böylece bot0 sessizken bile akış konsolda okunur.</summary>
    private static void HandleAdminMessage(BotSession admin, string? type, string json)
    {
        switch (type)
        {
            case MessageTypes.LobbyState:
                if (Parse<LobbyStateMsg>(json) is { } lobby) admin.ApplyRoster(lobby.players);
                break;

            case MessageTypes.MatchState:
                if (Parse<MatchStateMsg>(json) is { } state) admin.ApplyMatchState(state);
                break;

            case MessageTypes.KillEvent:
                if (Parse<KillEventMsg>(json) is { } kill)
                    admin.LogFlow($"kill: {kill.killerId} → {kill.victimId} ({kill.weaponId}).");
                break;

            case MessageTypes.MatchEnd:
                if (Parse<MatchEndMsg>(json) is { } end)
                {
                    admin.Phase = "End";
                    // Sunucu MATCH_END_SECONDS sonra kendi return_to_lobby'sini yayınlar — admin bir şey yapmaz.
                    admin.LogFlow($"match_end: kazanan {end.winnerTeam} ({end.scoreRed}-{end.scoreBlue}); " +
                                  $"{ArenaProtocol.MATCH_END_SECONDS} sn sonra lobi.");
                }
                break;

            case MessageTypes.ReturnToLobby:
                admin.Phase = "Lobby";
                admin.LogFlow("return_to_lobby → lobi.");
                break;
        }
    }

    /// <summary>Roster'da AdminMinPlayers kadar çevrimiçi oyuncu AdminStartDelay boyunca kararlı
    /// kalınca TEK SEFER start_match gönderir (faz Lobby değilse hiç göndermez).</summary>
    private static async Task AdminDirectorAsync(BotSession admin, CancellationToken ct)
    {
        var stableSince = DateTime.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(0.5));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (admin.Phase != "Lobby") { stableSince = DateTime.MinValue; continue; }

            var players = admin.OnlinePlayerCount();
            if (players < AdminMinPlayers) { stableSince = DateTime.MinValue; continue; }
            if (stableSince == DateTime.MinValue) { stableSince = DateTime.UtcNow; continue; }
            if (DateTime.UtcNow - stableSince < TimeSpan.FromSeconds(AdminStartDelay)) continue;

            await admin.SendAsync(new StartMatchMsg { modeId = AdminModeId, sceneName = AdminSceneName }, ct);
            admin.Log($"start_match gönderildi (oyuncu sayısı {players}).");
            return; // bu oturumda bir kez
        }
    }

    /// <summary>Konsoldan "q" + Enter → abort_match (test sırasında maçı elle bitirmek için).
    /// Ayrı Task'ta çalışır, ana akışı bloklamaz; stdin yönlendirilmişse/kapanmışsa sessizce çıkar.</summary>
    private static async Task AdminInputAsync(CancellationToken ct)
    {
        if (Console.IsInputRedirected) return;

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try { line = await Task.Run(Console.ReadLine, ct); }
            catch (Exception) { return; }
            if (line == null) return; // stdin kapandı
            if (!line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase)) continue;

            var session = _adminSession;
            if (session == null) { Console.WriteLine("[admin] bağlantı yok, abort_match atlandı."); continue; }
            try
            {
                await session.SendAsync(new AbortMatchMsg(), ct);
                Console.WriteLine("[admin] abort_match gönderildi (q).");
            }
            catch (Exception ex) { Console.WriteLine($"[admin] abort_match gönderilemedi: {ex.Message}"); }
        }
    }

    /// <summary>Sunucudan gelen kontrol mesajını uygular; bilinmeyen type sessizce yok sayılır (§5).</summary>
    private static void HandleMessage(BotSession bot, string? type, string json, CancellationToken ct)
    {
        switch (type)
        {
            case MessageTypes.LobbyState:
                // Tam roster anlık görüntüsü — hedef seçimi (takım/rol/online) bundan beslenir.
                if (Parse<LobbyStateMsg>(json) is { } lobby) bot.ApplyRoster(lobby.players);
                break;

            case MessageTypes.LoadMatch:
                if (Parse<LoadMatchMsg>(json) is { } load)
                {
                    bot.Phase = "Loading";
                    if (!string.IsNullOrEmpty(load.yourTeam)) bot.Team = load.yourTeam;
                    if (!string.IsNullOrEmpty(load.sceneName)) bot.Scene = load.sceneName;
                    bot.LogFlow($"load_match: {load.modeId} / {load.sceneName}, takım {bot.Team}.");
                    _ = ReadyAfterLoadAsync(bot, ct);
                }
                break;

            case MessageTypes.Countdown:
                if (Parse<CountdownMsg>(json) is { } countdown)
                {
                    bot.Phase = "Countdown";
                    bot.LogFlow($"countdown {countdown.seconds}…");
                }
                break;

            case MessageTypes.MatchState:
                if (Parse<MatchStateMsg>(json) is { } state) bot.ApplyMatchState(state);
                break;

            case MessageTypes.HealthUpdate:
                if (Parse<HealthUpdateMsg>(json) is { } health && health.playerId == bot.PlayerId)
                    bot.ApplyHealth(health);
                break;

            case MessageTypes.KillEvent:
                if (Parse<KillEventMsg>(json) is { } kill)
                    bot.LogFlow($"kill: {kill.killerId} → {kill.victimId} ({kill.weaponId}).");
                break;

            case MessageTypes.Respawn:
                if (Parse<RespawnMsg>(json) is { } respawn && respawn.playerId == bot.PlayerId)
                {
                    bot.Alive = false;
                    _ = ReviveLoopAsync(bot, respawn.delaySeconds, bot.NextDeathTicket(), ct);
                }
                break;

            case MessageTypes.MatchEnd:
                if (Parse<MatchEndMsg>(json) is { } end)
                {
                    bot.Phase = "End";
                    bot.LogFlow($"match_end: kazanan {end.winnerTeam} ({end.scoreRed}-{end.scoreBlue}).");
                }
                break;

            case MessageTypes.ReturnToLobby:
                bot.ReturnToLobby();
                bot.LogFlow("return_to_lobby → lobiye dönüldü.");
                break;
        }
    }

    /// <summary>"Sahne yükleniyor" simülasyonu: bot indeksine göre kademeli 0.5–1.5 sn,
    /// ardından set_ready{true} = "sahne yüklendi" (§10.1 Loading).</summary>
    private static async Task ReadyAfterLoadAsync(BotSession bot, CancellationToken ct)
    {
        var loadMs = 500 + Math.Min(bot.Index, 10) * 100;
        try
        {
            await Task.Delay(loadMs, ct);
            await bot.SendAsync(new SetReadyMsg { ready = true }, ct);
            bot.LogFlow($"sahne yüklendi ({loadMs} ms) → set_ready.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) Console.WriteLine($"{bot.Tag} set_ready hatası: {ex.Message}"); }
    }

    /// <summary>Free-roam canlanmanın bot karşılığı (§10.4): delaySeconds + 1 sn pay bekler,
    /// sonra "tabanına döndüm" anlamında revive_request gönderir; canlanana dek 1 sn arayla
    /// en çok ReviveAttempts kez tekrarlar (sunucu REVIVE_GRACE içinde zaten zorla canlandırır).</summary>
    private static async Task ReviveLoopAsync(BotSession bot, float delaySeconds, int ticket, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0f, delaySeconds) + 1.0), ct);
            for (var attempt = 1; attempt <= ReviveAttempts; attempt++)
            {
                // Yeni bir ölüm/lobiye dönüş olduysa bu döngü devre dışı kalır.
                if (bot.Alive || bot.DeathTicket != ticket) return;
                if (bot.Phase != "Live") return;

                await bot.SendAsync(new ReviveRequestMsg(), ct);
                bot.Log($"canlanma talebi ({attempt}/{ReviveAttempts}).");
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) Console.WriteLine($"{bot.Tag} canlanma hatası: {ex.Message}"); }
    }

    /// <summary>Ateş döngüsü (yalnız çift indeksli --fight botları): faz Live ve bot hayattayken
    /// saniyede ~2 kez shot_fired + hit_report. Hasar sunucu tablosuyla aynı olmalı (§10.3).</summary>
    private static async Task FireLoopAsync(BotSession bot, CancellationToken ct)
    {
        var seq = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(FireInterval));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (bot.Phase != "Live" || !bot.Alive) continue;

            var target = bot.PickTarget();
            if (target < 0) continue; // karşı takımda çevrimiçi oyuncu yok → ateş yok

            // Namlu, botun o anki dairesel yürüyüş konumu + göğüs yüksekliği (arena uzayı).
            // Yön: arena merkezine doğru — tüm botlar merkez etrafında dönüyor, hedef kabaca
            // oradadır (sunucu yön/konum doğrulaması yapmaz, §10.3).
            float mx = bot.PosX, mz = bot.PosZ;
            var len = MathF.Sqrt(mx * mx + mz * mz);
            var dx = len > 0.001f ? -mx / len : 0f;
            var dz = len > 0.001f ? -mz / len : 1f;

            await bot.SendAsync(new ShotFiredMsg
            {
                seq = seq++,
                weaponId = WeaponId,
                muzzlePos = new[] { mx, MuzzleHeight, mz },
                muzzleDir = new[] { dx, 0f, dz }
            }, ct);

            await bot.SendAsync(new HitReportMsg
            {
                seq = seq++,
                targetPlayerId = target,
                weaponId = WeaponId,
                damage = WeaponDamage,
                hitPos = new[] { mx + dx * 3f, MuzzleHeight, mz + dz * 3f }
            }, ct);
        }
    }

    /// <summary>UDP kayıt (0x00 ack'e dek 1 sn) + 20 Hz dairesel yürüyüş PoseUpdate'leri.</summary>
    private static async Task PoseLoopAsync(UdpClient udp, string ip, BotSession bot, uint udpToken, CancellationToken ct)
    {
        var server = new IPEndPoint(IPAddress.Parse(ip), ArenaProtocol.STATE_PORT);
        var playerId = bot.PlayerId;

        // Kayıt: aynı 6 bayt geri gelene dek tekrar.
        var helloBytes = new byte[UdpHello.SIZE];
        using (var w = new BinaryWriter(new MemoryStream(helloBytes)))
            new UdpHello { playerId = playerId, udpToken = udpToken }.Write(w);

        var acked = false;
        var recvTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !acked)
            {
                var r = await udp.ReceiveAsync(ct);
                if (r.Buffer.Length >= UdpHello.SIZE && r.Buffer[0] == UdpPacketType.UdpHello) acked = true;
            }
        }, ct);
        while (!ct.IsCancellationRequested && !acked)
        {
            await udp.SendAsync(helloBytes, helloBytes.Length, server);
            await Task.Delay(1000, ct);
        }
        Console.WriteLine($"{bot.Tag} UDP kaydı tamam; 20 Hz poz akışı başladı.");

        // Dairesel yürüyüş: bot başına faz + yarıçap; arena uzayı (origin merkez, y=0 zemin).
        var radius = 2.0f + bot.Index * 0.7f;
        var phase = bot.Index * 1.7f;
        ushort seq = 0;
        var packet = new byte[PoseUpdate.SIZE];
        var stream = new MemoryStream(packet);
        var writer = new BinaryWriter(stream);
        var start = Environment.TickCount;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / ArenaProtocol.POSE_RATE_HZ));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var tSec = (Environment.TickCount - start) / 1000f;
            var a = phase + tSec * 0.6f; // ~0.6 rad/sn açısal hız
            var (sin, cos) = MathF.SinCos(a);
            float px = cos * radius, pz = sin * radius;
            var yaw = MathF.Atan2(-sin, cos); // teğet yön (hareket yönüne bakış)
            var (hs, hc) = MathF.SinCos(yaw * 0.5f);

            // Ateş döngüsü namlu konumunu buradan okur (ölü bot da hareket etmeyi sürdürür).
            bot.PosX = px;
            bot.PosZ = pz;

            PoseData At(float ox, float oy, float oz) => new()
            {
                // Ofsetler baş yaw'ına göre döndürülür (sağ = +x).
                px = px + ox * MathF.Cos(yaw) + oz * MathF.Sin(yaw),
                py = oy,
                pz = pz - ox * MathF.Sin(yaw) + oz * MathF.Cos(yaw),
                qx = 0f, qy = hs, qz = 0f, qw = hc
            };

            var pose = new PoseUpdate
            {
                playerId = playerId,
                seq = seq++,
                clientTimeMs = (uint)Environment.TickCount,
                head = At(0f, 1.65f, 0f),
                handL = At(-0.25f, 1.15f, 0.25f),
                handR = At(0.25f, 1.15f, 0.25f)
            };
            stream.Position = 0;
            pose.Write(writer);
            await udp.SendAsync(packet, packet.Length, server);
        }
    }

    private static T? Parse<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return null; } // şema sapması → sessizce yok say (§5)
    }

    /// <summary>Tek botun oturum durumu ve paylaşılan WS gönderimi. Poz döngüsü, ateş döngüsü,
    /// set_ready ve canlanma görevleri aynı soketi kullanır — ClientWebSocket eşzamanlı Send'i
    /// kaldırmaz, bu yüzden gönderim tek kilitten geçer. Alanlar iş parçacıkları arasında
    /// paylaşıldığı için volatile.</summary>
    private sealed class BotSession
    {
        private readonly ClientWebSocket _ws;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private volatile PlayerInfo[] _roster = Array.Empty<PlayerInfo>();
        private int _targetCursor;
        private int _scoreRed = -1;
        private int _scoreBlue = -1;

        public readonly int Index;
        public readonly string Tag;
        /// <summary>--fight verildi mi (maç akışına katılım).</summary>
        public readonly bool Fight;
        /// <summary>Yalnız çift indeksli savaş botları ateş eder; tek indeksliler kurbandır
        /// (skor tek yönlü ve okunur ilerlesin diye).</summary>
        public readonly bool Shooter;
        /// <summary>Maç akışı satırlarını yazar mı (bot0 ve admin yazar, ötekiler susar).</summary>
        public readonly bool Verbose;

        public byte PlayerId;
        public volatile string Team = "";
        public volatile string Phase = "Lobby";
        public volatile string Scene = "Lobby";
        public volatile bool Alive = true;
        public volatile float Hp = ArenaProtocol.PLAYER_MAX_HP;
        public volatile float PosX;
        public volatile float PosZ;
        /// <summary>Her ölümde artar; eski canlanma döngüsünü devre dışı bırakır.</summary>
        public volatile int DeathTicket;

        public BotSession(ClientWebSocket ws, int index, string tag, bool fight, bool verbose)
        {
            _ws = ws;
            Index = index;
            Tag = tag;
            Fight = fight;
            Verbose = verbose;
            Shooter = fight && index % 2 == 0;
        }

        public async Task SendAsync<T>(T msg, CancellationToken ct)
        {
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(msg, Json));
            await _sendLock.WaitAsync(ct);
            try { await _ws.SendAsync(payload, WebSocketMessageType.Text, true, ct); }
            finally { _sendLock.Release(); }
        }

        /// <summary>Her botun yazdığı satır (ölüm/canlanma gibi seyrek olaylar).</summary>
        public void Log(string text) => Console.WriteLine($"{Tag} {text}");

        /// <summary>Maç akışı satırı — yalnız bot0 ve admin yazar (16 bot konsolu boğmasın).</summary>
        public void LogFlow(string text)
        {
            if (Verbose) Console.WriteLine($"{Tag} {text}");
        }

        /// <summary>Roster'daki çevrimiçi role=player sayısı (--admin start_match eşiği).</summary>
        public int OnlinePlayerCount()
        {
            var n = 0;
            foreach (var p in _roster)
                if (p != null && p.online && string.Equals(p.role, "player", StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        public int NextDeathTicket() => ++DeathTicket;

        public void ApplyRoster(PlayerInfo[]? players)
        {
            var list = players ?? Array.Empty<PlayerInfo>();
            _roster = list;
            foreach (var p in list)
                if (p != null && p.playerId == PlayerId && !string.IsNullOrEmpty(p.team))
                    Team = p.team;
        }

        public void ApplyMatchState(MatchStateMsg m)
        {
            var phase = m.phase ?? "";
            var changed = phase != Phase || m.scoreRed != _scoreRed || m.scoreBlue != _scoreBlue;

            // §10.2: Live'a girerken sunucu herkesi tam canla diriltir.
            if (phase != Phase && phase == "Live") { Hp = ArenaProtocol.PLAYER_MAX_HP; Alive = true; }

            Phase = phase;
            _scoreRed = m.scoreRed;
            _scoreBlue = m.scoreBlue;
            if (changed) LogFlow($"{phase} — kalan {m.timeRemaining:0} sn, skor {m.scoreRed}-{m.scoreBlue}.");
        }

        public void ApplyHealth(HealthUpdateMsg m)
        {
            var wasAlive = Alive;
            Hp = m.hp;
            Alive = m.hp > 0f;
            if (wasAlive && !Alive) Log($"öldü (saldıran {m.attackerId}).");
            else if (!wasAlive && Alive) Log($"canlandı (hp {m.hp:0}).");
        }

        public void ReturnToLobby()
        {
            Phase = "Lobby";
            Scene = "Lobby";
            Hp = ArenaProtocol.PLAYER_MAX_HP;
            Alive = true;
            DeathTicket++; // bekleyen canlanma döngüsü varsa sussun
        }

        /// <summary>Hedef: farklı takımdan, çevrimiçi, role=player bir oyuncu (bot ya da gerçek
        /// Quest/editor oyuncusu). Adaylar arasında sırayla dolaşılır ki hasar ölü tek hedefe
        /// yığılmasın. Uygun hedef yoksa -1.</summary>
        public int PickTarget()
        {
            var myTeam = Team;
            if (string.IsNullOrEmpty(myTeam)) return -1;

            var candidates = new List<int>();
            foreach (var p in _roster)
            {
                if (p == null || !p.online || p.playerId == PlayerId) continue;
                if (!string.Equals(p.role, "player", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(p.team) || string.Equals(p.team, myTeam, StringComparison.OrdinalIgnoreCase)) continue;
                candidates.Add(p.playerId);
            }
            if (candidates.Count == 0) return -1;

            _targetCursor = (_targetCursor + 1) % candidates.Count;
            return candidates[_targetCursor];
        }
    }
}
