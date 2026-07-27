#nullable enable
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>deviceId → PlayerState kaydı: playerId tahsisi (1..PLAYER_ID_MAX), devices.json ad
/// kalıcılığı ("Gözlük NN" otomatik), takım dengeleme ve OFFLINE_TIMEOUT süpürmesi.
/// (Cosmos DeviceRegistry + DeviceNameStore desenlerinin birleşimi.)
/// <para>
/// <b>Rol başına kalıcılık farkı (§2):</b> oyuncu kaydı kalıcıdır (deviceId sabit, kopunca
/// Offline işaretlenir ama durur, adı devices.json'a yazılır). Admin kaydı OTURUMLUKtur —
/// deviceId'si her oturumda benzersizdir, kopunca kayıt tümüyle silinir ve adı diske yazılmaz;
/// aksi hâlde admin'i her açıp kapatma roster'a hayalet satır ve tükenen playerId bırakırdı.
/// </para></summary>
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

    /// <summary>Çevrimiçi admin sayısı (admin_state.adminCount).</summary>
    public int OnlineAdminCount()
    {
        var count = 0;
        foreach (var state in _players.Values)
            if (state.Online && state.Role == "admin") count++;
        return count;
    }

    /// <summary>Çevrimiçi adminlerin bağlantıları (admin_state yayını için anlık kopya).</summary>
    public List<ClientConnection> OnlineAdminConnections()
    {
        var result = new List<ClientConnection>();
        foreach (var state in _players.Values)
        {
            if (!state.Online || state.Role != "admin") continue;
            var connection = state.Connection;
            if (connection != null) result.Add(connection);
        }
        return result;
    }

    /// <summary>false = playerId havuzu tükendi (1..PLAYER_ID_MAX). Bu bir ürün kotası değil,
    /// u8 tel formatının tavanıdır. Aynı deviceId yeniden bağlanırsa eski soket kapatılır,
    /// playerId korunur (Docs/ArenaNet-Protokol.md §2).</summary>
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
            // Admin deviceId'si oturumluk (§2) — diske yazmak devices.json'ı çöple doldururdu.
            if (state.Role != "admin")
            {
                _names[deviceId] = name;
                SaveNamesLocked();
            }
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
        var removed = false;
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
                    removed = RetireLocked(state);
                    break;
                }
            }
        }
        if (affected != null)
            Changed?.Invoke(affected, removed ? PlayerChangeKind.Removed : PlayerChangeKind.Offline);
    }

    /// <summary>OFFLINE_TIMEOUT boyunca status gelmeyen cihazları çevrimdışına düşürür,
    /// bağlantılarını kapatır (§8). Admin kayıtları burada da silinir (§2).</summary>
    private void CheckOfflinePlayers()
    {
        var now = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(ArenaProtocol.OFFLINE_TIMEOUT);
        var timedOut = new List<(PlayerState State, ClientConnection? Connection, bool Removed)>();
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (state.Online && now - state.LastSeen > timeout)
                {
                    state.Online = false;
                    state.Ready = false;
                    var connection = state.Connection;
                    state.Connection = null;
                    timedOut.Add((state, connection, RetireLocked(state)));
                }
            }
        }
        foreach (var (state, connection, removed) in timedOut)
        {
            connection?.Abort();
            Changed?.Invoke(state, removed ? PlayerChangeKind.Removed : PlayerChangeKind.Offline);
        }
    }

    /// <summary>
    /// Çevrimdışına düşmüş kaydı emekliye ayırır. <b>Admin kaydı tümüyle silinir</b> (deviceId'si
    /// zaten oturumluk — §2: geri gelen admin yeni bir kimlikle gelir, eski satır roster'da
    /// hayalet olarak kalırdı ve playerId'si havuza dönmezdi). Oyuncu kaydı DURUR: deviceId'si
    /// kalıcıdır, aynı gözlük geri geldiğinde playerId'sini ve adını korumalıdır.
    /// <para>true = silindi. Çağıran <c>_gate</c> kilidini tutuyor olmalıdır.</para>
    /// </summary>
    private bool RetireLocked(PlayerState state)
    {
        if (state.Role != "admin") return false;
        _players.TryRemove(state.DeviceId, out _);
        return true;
    }

    public void Dispose() => _offlineTimer.Dispose();

    // ---- playerId / takım / token tahsisi ----

    private int NextFreePlayerIdLocked()
    {
        var used = new HashSet<int>();
        foreach (var state in _players.Values) used.Add(state.PlayerId);
        for (var id = 1; id <= ArenaProtocol.PLAYER_ID_MAX; id++)
            if (!used.Contains(id)) return id;
        return 0; // u8 havuzu tükendi (ürün kotası değil, tel formatı tavanı)
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

    /// <summary>
    /// <b>Oyuncu:</b> devices.json'daki kayıtlı ad varsa o, yoksa ilk boş "Gözlük NN" atanır ve
    /// dosyaya yazılır (kalıcı kimlik).
    /// <para>
    /// <b>Admin:</b> `hello.deviceName` (PC adı; boşsa "Admin") kullanılır ve <b>diske YAZILMAZ</b> —
    /// admin deviceId'si oturumluk olduğu için her açılış devices.json'a çöp bir satır eklerdi.
    /// Aynı ad başka bir çevrimiçi admin'de kullanılıyorsa sonuna " (2)", " (3)"… eklenir:
    /// aynı PC'de iki admin penceresi açıkken roster'da hangisinin hangisi olduğu ayırt edilebilsin.
    /// </para>
    /// </summary>
    private string ResolveNameLocked(string deviceId, string role, string? fallbackDeviceName)
    {
        if (role == "admin")
            return UniqueAdminNameLocked(deviceId, fallbackDeviceName);

        if (_names.TryGetValue(deviceId, out var existing) && !string.IsNullOrWhiteSpace(existing))
            return existing;

        var assigned = NextFreeAutoNameLocked();
        if (string.IsNullOrWhiteSpace(assigned))
            assigned = string.IsNullOrWhiteSpace(fallbackDeviceName) ? deviceId : fallbackDeviceName!;

        _names[deviceId] = assigned;
        SaveNamesLocked();
        return assigned;
    }

    /// <summary>Başka bir admin kaydının kullanmadığı ilk ad ("Ofis-PC", "Ofis-PC (2)", …).</summary>
    private string UniqueAdminNameLocked(string deviceId, string? fallbackDeviceName)
    {
        var baseName = string.IsNullOrWhiteSpace(fallbackDeviceName) ? "Admin" : fallbackDeviceName!.Trim();

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in _players.Values)
        {
            // Kendi kaydımız (yeniden bağlanma) adı serbest bırakır; oyuncu adları da çakışmasın.
            if (state.DeviceId == deviceId) continue;
            if (!string.IsNullOrEmpty(state.Name)) taken.Add(state.Name);
        }

        if (!taken.Contains(baseName)) return baseName;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (!taken.Contains(candidate)) return candidate;
        }
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
