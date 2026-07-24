#nullable enable
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>deviceId → PlayerState kaydı: playerId tahsisi (1..MAX_PLAYERS), devices.json ad
/// kalıcılığı ("Gözlük NN" otomatik), takım dengeleme ve OFFLINE_TIMEOUT süpürmesi.
/// (Cosmos DeviceRegistry + DeviceNameStore desenlerinin birleşimi.)</summary>
public sealed class PlayerRegistry : IDisposable
{
    private static readonly JsonSerializerOptions NamesJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Türkçe karakterler dosyada okunur kalsın
    };
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly Regex AutoNamePattern = new(@"^Gözlük (\d+)$", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, PlayerState> _players = new();
    private readonly object _gate = new();
    private readonly Timer _offlineTimer;
    private readonly string _devicesPath;
    private Dictionary<string, string> _names = new();

    /// <summary>İşçi thread'lerden tetiklenir. TryRegisterHello İÇİN raise edilmez —
    /// LobbyService welcome'ı yolladıktan sonra Announce çağırır (welcome her zaman
    /// lobby_state'ten önce gitsin diye).</summary>
    public event Action<PlayerState, PlayerChangeKind>? Changed;

    public PlayerRegistry(string devicesJsonPath)
    {
        _devicesPath = devicesJsonPath;
        LoadNames();
        _offlineTimer = new Timer(_ => CheckOfflinePlayers(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public IReadOnlyList<PlayerState> Snapshot() => _players.Values.ToList();

    public bool TryGet(string deviceId, out PlayerState state) => _players.TryGetValue(deviceId, out state!);

    public bool TryGetByPlayerId(int playerId, out PlayerState state)
    {
        foreach (var s in _players.Values)
        {
            if (s.PlayerId == playerId)
            {
                state = s;
                return true;
            }
        }
        state = null!;
        return false;
    }

    /// <summary>false = sunucu dolu (MAX_PLAYERS aşıldı). Aynı deviceId yeniden bağlanırsa
    /// eski soket kapatılır, playerId korunur (Docs/ArenaNet-Protokol.md §2).</summary>
    public bool TryRegisterHello(HelloMsg hello, ClientConnection connection, out PlayerState state, out PlayerChangeKind kind)
    {
        ClientConnection? stale = null;
        lock (_gate)
        {
            if (_players.TryGetValue(hello.deviceId, out var existing))
            {
                state = existing;
                kind = PlayerChangeKind.Reconnected;
                if (existing.Connection != null && !ReferenceEquals(existing.Connection, connection))
                    stale = existing.Connection;
            }
            else
            {
                var playerId = NextFreePlayerIdLocked();
                if (playerId == 0)
                {
                    state = null!;
                    kind = PlayerChangeKind.Added;
                    return false;
                }
                state = new PlayerState { DeviceId = hello.deviceId, PlayerId = playerId };
                _players[hello.deviceId] = state;
                kind = PlayerChangeKind.Added;
            }

            state.Role = hello.role == "admin" ? "admin" : "player";
            state.Name = ResolveNameLocked(state.DeviceId, state.Role, hello.deviceName);
            state.Team = state.Role == "player"
                ? (string.IsNullOrEmpty(state.Team) ? SmallerTeamLocked() : state.Team)
                : ""; // admin oynamaz
            state.Scene = hello.currentScene ?? "";
            state.Scenes = hello.scenes != null ? new List<string>(hello.scenes) : new List<string>();
            state.Ready = false;
            state.Online = true;
            state.LastSeen = DateTime.UtcNow;
            state.Connection = connection;
            // Her welcome yeni udpToken taşır; bayat UDP endpoint geçersizleşir
            // (istemci 0x00 UdpHello ile yeniden kaydolur).
            state.UdpToken = NextUdpToken();
            state.UdpEndpoint = null;
            // Bayat poz yeni oturuma taşınmasın; snapshot okuyucusu HasPose'a PoseGate altında
            // baktığı için sıfırlama da aynı kilit altında (PoseGate içinden asla _gate alınmaz).
            lock (state.PoseGate)
            {
                state.HasPose = false;
                state.LastSeq = 0;
            }
        }

        stale?.Abort();
        return true;
    }

    /// <summary>TryRegisterHello'nun ertelenmiş bildirimi — LobbyService welcome'dan SONRA çağırır.</summary>
    public void Announce(PlayerState state, PlayerChangeKind kind) => Changed?.Invoke(state, kind);

    public void UpdateStatus(string deviceId, StatusMsg status)
    {
        if (!_players.TryGetValue(deviceId, out var state)) return;
        lock (_gate)
        {
            state.Scene = status.scene ?? state.Scene;
            state.Battery = status.battery;
            state.Fps = status.fps;
            state.LastSeen = DateTime.UtcNow;
            state.Online = true;
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
    }

    public void SetName(string deviceId, string name)
    {
        if (!_players.TryGetValue(deviceId, out var state)) return;
        lock (_gate)
        {
            state.Name = name;
            _names[deviceId] = name;
            SaveNamesLocked();
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
    }

    public void SetReady(string deviceId, bool ready)
    {
        if (!_players.TryGetValue(deviceId, out var state)) return;
        lock (_gate) state.Ready = ready;
        Changed?.Invoke(state, PlayerChangeKind.Updated);
    }

    public bool SetTeam(int playerId, string team)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        lock (_gate) state.Team = team;
        Changed?.Invoke(state, PlayerChangeKind.Updated);
        return true;
    }

    /// <summary>0x00 UdpHello doğrulaması: playerId↔udpToken eşleşirse endpoint kaydedilir (§6.1).
    /// Pozlar yalnız kayıtlı endpoint'ten kabul edilir (StateHost 0x01 alımı).</summary>
    public bool TryRegisterUdpEndpoint(byte playerId, uint udpToken, IPEndPoint endpoint)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        lock (_gate)
        {
            if (udpToken == 0 || state.UdpToken != udpToken) return false;
            state.UdpEndpoint = endpoint;
        }
        return true;
    }

    /// <summary>Bir bağlantının recv döngüsü kapanınca çağrılır. Cihaz daha yeni bir bağlantıya
    /// geçtiyse no-op (yeniden bağlanma yarışına karşı).</summary>
    public void NotifyDisconnected(ClientConnection connection)
    {
        PlayerState? affected = null;
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (ReferenceEquals(state.Connection, connection))
                {
                    state.Connection = null;
                    state.Online = false;
                    state.Ready = false;
                    affected = state;
                    break;
                }
            }
        }
        if (affected != null) Changed?.Invoke(affected, PlayerChangeKind.Offline);
    }

    /// <summary>OFFLINE_TIMEOUT boyunca status gelmeyen cihazları çevrimdışına düşürür,
    /// bağlantılarını kapatır (§8).</summary>
    private void CheckOfflinePlayers()
    {
        var now = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(ArenaProtocol.OFFLINE_TIMEOUT);
        var timedOut = new List<(PlayerState State, ClientConnection? Connection)>();
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (state.Online && now - state.LastSeen > timeout)
                {
                    state.Online = false;
                    state.Ready = false;
                    timedOut.Add((state, state.Connection));
                    state.Connection = null;
                }
            }
        }
        foreach (var (state, connection) in timedOut)
        {
            connection?.Abort();
            Changed?.Invoke(state, PlayerChangeKind.Offline);
        }
    }

    public void Dispose() => _offlineTimer.Dispose();

    // ---- playerId / takım / token tahsisi ----

    private int NextFreePlayerIdLocked()
    {
        var used = new HashSet<int>();
        foreach (var state in _players.Values) used.Add(state.PlayerId);
        for (var id = 1; id <= ArenaProtocol.MAX_PLAYERS; id++)
            if (!used.Contains(id)) return id;
        return 0; // dolu
    }

    /// <summary>Yeni oyuncuyu az kişili takıma koyar (eşitlikte red); admin set_team ile değiştirir.</summary>
    private string SmallerTeamLocked()
    {
        int red = 0, blue = 0;
        foreach (var state in _players.Values)
        {
            if (state.Role != "player") continue;
            if (state.Team == "red") red++;
            else if (state.Team == "blue") blue++;
        }
        return red <= blue ? "red" : "blue";
    }

    private static uint NextUdpToken()
    {
        Span<byte> bytes = stackalloc byte[4];
        uint token;
        do
        {
            Random.Shared.NextBytes(bytes);
            token = BitConverter.ToUInt32(bytes);
        } while (token == 0);
        return token;
    }

    // ---- devices.json ad kalıcılığı (cosmos DeviceNameStore deseni; UTF-8 BOM'suz) ----

    private void LoadNames()
    {
        try
        {
            _names = File.Exists(_devicesPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_devicesPath)) ?? new()
                : new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerRegistry] devices.json okunamadı ({ex.Message}) — boş ad haritasıyla başlanıyor.");
            _names = new();
        }
    }

    /// <summary>Kayıtlı ad varsa onu döndürür; yoksa player'a ilk boş "Gözlük NN"yi atar ve dosyaya
    /// yazar. Admin'de hello.deviceName (boşsa "Admin") kullanılır.</summary>
    private string ResolveNameLocked(string deviceId, string role, string? fallbackDeviceName)
    {
        if (_names.TryGetValue(deviceId, out var existing) && !string.IsNullOrWhiteSpace(existing))
            return existing;

        var assigned = role == "player"
            ? NextFreeAutoNameLocked()
            : (!string.IsNullOrWhiteSpace(fallbackDeviceName) ? fallbackDeviceName! : "Admin");
        if (string.IsNullOrWhiteSpace(assigned))
            assigned = string.IsNullOrWhiteSpace(fallbackDeviceName) ? deviceId : fallbackDeviceName!;

        _names[deviceId] = assigned;
        SaveNamesLocked();
        return assigned;
    }

    private string NextFreeAutoNameLocked()
    {
        var used = new HashSet<int>();
        foreach (var value in _names.Values)
        {
            var match = AutoNamePattern.Match(value);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var n)) used.Add(n);
        }
        var candidate = 1;
        while (used.Contains(candidate)) candidate++;
        return $"Gözlük {candidate:00}";
    }

    private void SaveNamesLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_devicesPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_devicesPath, JsonSerializer.Serialize(_names, NamesJsonOptions), Utf8NoBom);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerRegistry] devices.json yazılamadı: {ex.Message}");
        }
    }
}
