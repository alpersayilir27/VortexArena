#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Lobi semantiği: hello→welcome yanıtı, roster her değiştiğinde herkese lobby_state
/// TAM anlık görüntüsü, set_name/set_ready/set_team/kick/identify işleme (§5).</summary>
public sealed class LobbyService
{
    private readonly PlayerRegistry _registry;
    private readonly MatchDirector _director;

    public LobbyService(PlayerRegistry registry, MatchDirector director)
    {
        _registry = registry;
        _director = director;
        _registry.Changed += OnRegistryChanged;
    }

    private void OnRegistryChanged(PlayerState state, PlayerChangeKind kind) => _ = BroadcastLobbyStateAsync();

    /// <summary>hello → kayıt + welcome (mevcut maç durumu ile; geç katılım senkronu §5.3).
    /// welcome gönderildikten SONRA Announce ile lobby_state yayını tetiklenir.</summary>
    public async Task HandleHelloAsync(ClientConnection connection, HelloMsg hello)
    {
        if (hello.protocolVersion != ArenaProtocol.PROTOCOL_VERSION)
            Console.WriteLine($"[Lobby] protokol sürüm uyumsuzluğu: istemci {hello.protocolVersion}, sunucu {ArenaProtocol.PROTOCOL_VERSION} — devam ediliyor.");

        if (!_registry.TryRegisterHello(hello, connection, out var state, out var kind))
        {
            Console.WriteLine($"[Lobby] sunucu dolu ({ArenaProtocol.MAX_PLAYERS}) — {hello.deviceName} reddedildi.");
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

    public void HandleSetTeam(SetTeamMsg msg)
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
    }

    public async Task HandleKickAsync(KickMsg msg)
    {
        if (!_registry.TryGetByPlayerId(msg.playerId, out var target))
        {
            Console.WriteLine($"[Lobby] kick: playerId {msg.playerId} bulunamadı.");
            return;
        }
        Console.WriteLine($"[Lobby] kick: {target.Name} (playerId {target.PlayerId}).");
        var connection = target.Connection;
        if (connection == null) return;
        await SendSafeAsync(connection, JsonUtil.Serialize(new KickedMsg { reason = "" }), target.Name);
        connection.Abort(); // recv döngüsü kapanınca Offline + lobby_state yayını gelir
    }

    public async Task HandleIdentifyAsync(IdentifyMsg msg)
    {
        if (!_registry.TryGetByPlayerId(msg.playerId, out var target) || target.Connection == null)
        {
            Console.WriteLine($"[Lobby] identify: playerId {msg.playerId} bulunamadı/çevrimdışı.");
            return;
        }
        // Sunucu→istemci yönünde istemci kendi kimlik overlay'ini gösterir (§5.3).
        await SendSafeAsync(target.Connection, JsonUtil.Serialize(new IdentifyMsg { playerId = target.PlayerId }), target.Name);
    }

    // ---- Maç komutları: Faz 3'te MatchDirector doldurulunca gerçek akışa bağlanır. ----

    public void HandleStartMatch(StartMatchMsg msg) => _director.StartMatch(msg.modeId, msg.sceneName);
    public void HandleAbortMatch() => _director.AbortMatch();
    public void HandleReturnToLobby() => _director.ReturnToLobby();

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
