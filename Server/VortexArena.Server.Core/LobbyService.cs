#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Lobi semantiği: hello→welcome yanıtı, roster her değiştiğinde herkese lobby_state
/// TAM anlık görüntüsü, set_name/set_ready/set_team/kick/identify işleme (§5).
/// <para>
/// Ayrıca <b>adminler arası ortak durumun</b> sahibidir (§5.3 <c>admin_state</c>): bir sonraki
/// maçın mod/harita seçimi burada yaşar — admin arayüzündeki seçiciler yerel bir değişkeni değil
/// bunu değiştirir (<c>set_selection</c>), sunucu da değişikliği TÜM adminlere geri yayar.
/// Böylece iki operatör aynı ekranı görür. Görünüm tercihleri (kamera, halka, saydamlık) buraya
/// GİRMEZ — onlar her admin'in kendi makinesinde kalır.
/// </para></summary>
public sealed class LobbyService
{
    private readonly PlayerRegistry _registry;
    private readonly MatchDirector _director;

    /// <summary>Ortak seçimi koruyan kilit; WS işleyicileri farklı thread'lerden gelebilir.</summary>
    private readonly object _selectionGate = new();
    private string _selectedModeId = "";
    private string _selectedSceneName = "";

    /// <summary>Bir sonraki maçın ortak parametreleri (§5.2); <c>0</c> = seçilmedi, modun
    /// varsayılanı kullanılacak. Mod/harita ile AYNI kanaldan gider: parametreler yerel kalsaydı
    /// bir operatörün 5 dk sandığı maç diğerinin seçtiği 30 dk ile başlardı.</summary>
    private int _selectedRoundSeconds;
    private int _selectedScoreLimit;

    public LobbyService(PlayerRegistry registry, MatchDirector director)
    {
        _registry = registry;
        _director = director;
        _registry.Changed += OnRegistryChanged;
    }

    private void OnRegistryChanged(PlayerState state, PlayerChangeKind kind)
    {
        _ = BroadcastLobbyStateAsync();

        // Admin geldi/gitti → adminCount değişti, kalan adminler tazelensin.
        if (state.Role == "admin" && kind != PlayerChangeKind.Updated)
        {
            var verb = kind switch
            {
                PlayerChangeKind.Added => "bağlandı",
                PlayerChangeKind.Reconnected => "yeniden bağlandı",
                _ => "ayrıldı"
            };
            _ = BroadcastAdminStateAsync($"{state.Name} {verb}");
        }
    }

    /// <summary>hello → kayıt + welcome (mevcut maç durumu ile; geç katılım senkronu §5.3).
    /// welcome gönderildikten SONRA Announce ile lobby_state yayını tetiklenir.</summary>
    public async Task HandleHelloAsync(ClientConnection connection, HelloMsg hello)
    {
        if (hello.protocolVersion != ArenaProtocol.PROTOCOL_VERSION)
            Console.WriteLine($"[Lobby] protokol sürüm uyumsuzluğu: istemci {hello.protocolVersion}, sunucu {ArenaProtocol.PROTOCOL_VERSION} — devam ediliyor.");

        if (!_registry.TryRegisterHello(hello, connection, out var state, out var kind))
        {
            Console.WriteLine($"[Lobby] playerId havuzu tükendi ({ArenaProtocol.PLAYER_ID_MAX}) — {hello.deviceName} reddedildi.");
            await SendSafeAsync(connection, JsonUtil.Serialize(new KickedMsg { reason = "Sunucu dolu" }), "(dolu)");
            connection.Abort();
            return;
        }
        connection.State = state;

        var welcome = new WelcomeMsg
        {
            protocolVersion = ArenaProtocol.PROTOCOL_VERSION,
            playerId = state.PlayerId,
            udpToken = state.UdpToken,
            match = _director.CurrentMatchInfo()
        };
        await SendSafeAsync(connection, JsonUtil.Serialize(welcome), state.Name);

        // Geç katılan admin ortak seçimi welcome'dan hemen sonra alır (§5.3): paneli açtığında
        // diğer operatörün seçtiği mod/harita yazıyor olmalı, kendi varsayılanı değil.
        if (state.Role == "admin")
            await SendSafeAsync(connection, BuildAdminStateJson(""), state.Name);

        _registry.Announce(state, kind); // konsol satırı + lobby_state yayını
    }

    public void HandleSetName(ClientConnection connection, SetNameMsg msg)
    {
        var name = msg.name?.Trim();
        if (string.IsNullOrEmpty(name) || connection.State == null) return;
        _registry.SetName(connection.State.DeviceId, name);
    }

    public void HandleSetReady(ClientConnection connection, SetReadyMsg msg)
    {
        if (connection.State == null) return;
        _registry.SetReady(connection.State.DeviceId, msg.ready);
    }

    public void HandleSetTeam(ClientConnection connection, SetTeamMsg msg)
    {
        if (msg.team != "red" && msg.team != "blue")
        {
            Console.WriteLine($"[Lobby] set_team geçersiz takım '{msg.team}' — yok sayıldı.");
            return;
        }
        if (!_registry.TryGetByPlayerId(msg.playerId, out var target))
        {
            Console.WriteLine($"[Lobby] set_team: playerId {msg.playerId} bulunamadı.");
            return;
        }
        if (target.Role != "player")
        {
            Console.WriteLine($"[Lobby] set_team: {target.Name} admin — takım atanmaz.");
            return;
        }
        _registry.SetTeam(msg.playerId, msg.team);
        if (connection.IsAdmin)
            _ = BroadcastAdminStateAsync(Notice(connection, $"{target.Name} -> {msg.team}"));
    }

    public async Task HandleKickAsync(ClientConnection connection, KickMsg msg)
    {
        if (!_registry.TryGetByPlayerId(msg.playerId, out var target))
        {
            Console.WriteLine($"[Lobby] kick: playerId {msg.playerId} bulunamadı.");
            return;
        }
        Console.WriteLine($"[Lobby] kick: {target.Name} (playerId {target.PlayerId}).");
        await BroadcastAdminStateAsync(Notice(connection, $"{target.Name} atıldı"));
        var targetConnection = target.Connection;
        if (targetConnection == null) return;
        await SendSafeAsync(targetConnection, JsonUtil.Serialize(new KickedMsg { reason = "" }), target.Name);
        targetConnection.Abort(); // recv döngüsü kapanınca Offline + lobby_state yayını gelir
    }

    public async Task HandleIdentifyAsync(ClientConnection connection, IdentifyMsg msg)
    {
        if (!_registry.TryGetByPlayerId(msg.playerId, out var target) || target.Connection == null)
        {
            Console.WriteLine($"[Lobby] identify: playerId {msg.playerId} bulunamadı/çevrimdışı.");
            return;
        }
        // Sunucu→istemci yönünde istemci kendi kimlik overlay'ini gösterir (§5.3).
        await SendSafeAsync(target.Connection, JsonUtil.Serialize(new IdentifyMsg { playerId = target.PlayerId }), target.Name);
        await BroadcastAdminStateAsync(Notice(connection, $"{target.Name} kimlik gösterdi"));
    }

    // ---- Ortak seçim (§5.2 set_selection / §5.3 admin_state) ----

    /// <summary>Bir sonraki maçın ortak mod/harita seçimi. Maçı BAŞLATMAZ; boş alan mevcut
    /// değerini korur. Değişiklik tüm adminlere yayılır — çoklu operatör aynı ekranı görsün.</summary>
    public Task HandleSetSelectionAsync(ClientConnection connection, SetSelectionMsg msg)
    {
        if (!ApplySelection(msg.modeId, msg.sceneName, msg.roundSeconds, msg.scoreLimit))
            return Task.CompletedTask; // değişmedi: gereksiz yayın yapma

        string modeId, sceneName;
        int roundSeconds, scoreLimit;
        lock (_selectionGate)
        {
            modeId = _selectedModeId;
            sceneName = _selectedSceneName;
            roundSeconds = _selectedRoundSeconds;
            scoreLimit = _selectedScoreLimit;
        }

        var parameters = roundSeconds > 0 || scoreLimit > 0
            ? $", {(roundSeconds > 0 ? roundSeconds + " sn" : "mod süresi")} / " +
              $"{(scoreLimit > 0 ? "limit " + scoreLimit : "mod limiti")}"
            : "";
        Console.WriteLine($"[Lobby] set_selection: mod '{modeId}', harita '{sceneName}'{parameters} ({connection.State?.Name}).");
        return BroadcastAdminStateAsync(Notice(connection, $"seçim -> {sceneName} / {modeId}{parameters}"));
    }

    /// <summary>true = seçim gerçekten değişti. Boş/null string ve <c>0</c> sayı mevcut değeri
    /// korur (§5.2) — arayüz yalnız değiştirdiği alanı doldurabilsin.</summary>
    private bool ApplySelection(string? modeId, string? sceneName, int roundSeconds, int scoreLimit)
    {
        lock (_selectionGate)
        {
            var changed = false;
            if (!string.IsNullOrEmpty(modeId) && _selectedModeId != modeId)
            {
                _selectedModeId = modeId;
                changed = true;
            }
            if (!string.IsNullOrEmpty(sceneName) && _selectedSceneName != sceneName)
            {
                _selectedSceneName = sceneName;
                changed = true;
            }
            if (roundSeconds > 0 && _selectedRoundSeconds != roundSeconds)
            {
                _selectedRoundSeconds = roundSeconds;
                changed = true;
            }
            if (scoreLimit > 0 && _selectedScoreLimit != scoreLimit)
            {
                _selectedScoreLimit = scoreLimit;
                changed = true;
            }
            return changed;
        }
    }

    // ---- Maç komutları (yalnız admin; doğrulama + yayınlar MatchDirector'da, §10.1). ----

    /// <summary>start_match ortak seçimi de günceller: maç başladığında tüm admin panelleri
    /// aynı mod/haritayı göstersin (komutu kim gönderdiyse gönderdi).</summary>
    public async Task HandleStartMatchAsync(ClientConnection connection, StartMatchMsg msg)
    {
        ApplySelection(msg.modeId, msg.sceneName, msg.roundSeconds, msg.scoreLimit);
        await BroadcastAdminStateAsync(Notice(connection, $"maç başlatılıyor: {msg.sceneName} / {msg.modeId}"));
        await _director.StartMatchAsync(msg.modeId, msg.sceneName, msg.roundSeconds, msg.scoreLimit);
    }

    public async Task HandleAbortMatchAsync(ClientConnection connection)
    {
        await BroadcastAdminStateAsync(Notice(connection, "maç iptal edildi"));
        await _director.AbortMatchAsync();
    }

    public async Task HandleReturnToLobbyAsync(ClientConnection connection)
    {
        await BroadcastAdminStateAsync(Notice(connection, "lobiye dönülüyor"));
        await _director.ReturnToLobbyAsync();
    }

    /// <summary>Duyuru satırı: "<admin adı>: <eylem>" — tüm adminlerin durum satırında görünür.</summary>
    private static string Notice(ClientConnection connection, string action) =>
        $"{connection.State?.Name ?? "Admin"}: {action}";

    /// <summary>Ortak durumu YALNIZ çevrimiçi adminlere yollar (§5.3).</summary>
    public async Task BroadcastAdminStateAsync(string notice)
    {
        try
        {
            var admins = _registry.OnlineAdminConnections();
            if (admins.Count == 0) return;
            var json = BuildAdminStateJson(notice);
            foreach (var connection in admins)
                await SendSafeAsync(connection, json, "(admin)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] admin_state yayını hatası: {ex.Message}");
        }
    }

    private string BuildAdminStateJson(string notice)
    {
        lock (_selectionGate)
        {
            return JsonUtil.Serialize(new AdminStateMsg
            {
                modeId = _selectedModeId,
                sceneName = _selectedSceneName,
                roundSeconds = _selectedRoundSeconds,
                scoreLimit = _selectedScoreLimit,
                notice = notice,
                adminCount = _registry.OnlineAdminCount()
            });
        }
    }

    /// <summary>Roster'ın TAM anlık görüntüsünü tüm çevrimiçi bağlantılara yollar (§5.3 lobby_state).</summary>
    public async Task BroadcastLobbyStateAsync()
    {
        try
        {
            var snapshot = _registry.Snapshot();
            var msg = new LobbyStateMsg
            {
                players = snapshot.OrderBy(p => p.PlayerId).Select(p => p.ToPlayerInfo()).ToArray()
            };
            var json = JsonUtil.Serialize(msg);
            foreach (var state in snapshot)
            {
                var connection = state.Connection;
                if (connection == null || !state.Online) continue;
                await SendSafeAsync(connection, json, state.Name);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] lobby_state yayını hatası: {ex.Message}");
        }
    }

    private static async Task SendSafeAsync(ClientConnection connection, string json, string who)
    {
        try
        {
            await connection.SendTextAsync(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] gönderim başarısız ({who}): {ex.Message}");
        }
    }
}
