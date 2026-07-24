#nullable enable
using System.Net.WebSockets;
using System.Text;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Kabul edilmiş tek bir WS istemcisinin alma/yollama döngüsü (cosmos ClientConnection deseni).
/// Zarf kuralı: önce yalnız type parse edilir, bilinmeyen tip loglanıp yok sayılır (§5).</summary>
public sealed class ClientConnection
{
    /// <summary>Kontrol mesajları küçük JSON'lardır; bunu aşan gövde bozuk/istenmeyen bir eştir.</summary>
    private const int MaxMessageBytes = 256 * 1024;

    private readonly WebSocket _socket;
    private readonly PlayerRegistry _registry;
    private readonly LobbyService _lobby;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    /// <summary>hello işlenip kayıt tamamlanınca dolar; öncesinde null.</summary>
    public PlayerState? State { get; internal set; }

    public string? DeviceId => State?.DeviceId;
    public bool IsAdmin => State?.Role == "admin";

    public ClientConnection(WebSocket socket, PlayerRegistry registry, LobbyService lobby)
    {
        _socket = socket;
        _registry = registry;
        _lobby = lobby;
    }

    public async Task RunAsync(CancellationToken hostToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostToken, _cts.Token);
        var token = linked.Token;
        _ = HelloWatchdogAsync(token);

        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (_socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxMessageBytes)
                    {
                        Console.WriteLine($"[ClientConnection] mesaj {MaxMessageBytes} baytı aştı — bağlantı kapatılıyor.");
                        Abort();
                        return;
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    await HandleTextAsync(json);
                }
                // Binary: kontrol kanalı yalnız text taşır (pozlar UDP 47822'de) — yok sayılır.
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClientConnection] beklenmeyen hata: {ex}");
        }
        finally
        {
            _registry.NotifyDisconnected(this);
        }
    }

    private async Task HandleTextAsync(string json)
    {
        var type = JsonUtil.GetMessageType(json);
        switch (type)
        {
            case MessageTypes.Hello:
            {
                var hello = JsonUtil.Deserialize<HelloMsg>(json);
                if (hello == null || string.IsNullOrEmpty(hello.deviceId)) return;
                await _lobby.HandleHelloAsync(this, hello);
                return;
            }
            case MessageTypes.Status:
            {
                if (State == null) return; // hello öncesi status — yok sayılır
                var status = JsonUtil.Deserialize<StatusMsg>(json);
                if (status != null) _registry.UpdateStatus(State.DeviceId, status);
                return;
            }
            case MessageTypes.SetName:
            {
                if (State == null) return;
                var msg = JsonUtil.Deserialize<SetNameMsg>(json);
                if (msg != null) _lobby.HandleSetName(this, msg);
                return;
            }
            case MessageTypes.SetReady:
            {
                if (State == null) return;
                if (State.Role != "player")
                {
                    Console.WriteLine($"[ClientConnection] set_ready yalnız player içindir ({State.Name}) — yok sayıldı.");
                    return;
                }
                var msg = JsonUtil.Deserialize<SetReadyMsg>(json);
                if (msg != null) _lobby.HandleSetReady(this, msg);
                return;
            }
            case MessageTypes.SetTeam:
            {
                if (State == null) return;
                var msg = JsonUtil.Deserialize<SetTeamMsg>(json);
                if (msg == null) return;
                // Admin herkesin takımını değiştirir; oyuncu yalnız kendisininkini (§5.2).
                if (State.Role != "admin" && msg.playerId != State.PlayerId)
                {
                    Console.WriteLine($"[ClientConnection] set_team: {State.Name} başka oyuncunun takımını değiştiremez — yok sayıldı.");
                    return;
                }
                _lobby.HandleSetTeam(msg);
                return;
            }
            case MessageTypes.Kick:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<KickMsg>(json);
                if (msg != null) await _lobby.HandleKickAsync(msg);
                return;
            }
            case MessageTypes.Identify:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<IdentifyMsg>(json);
                if (msg != null) await _lobby.HandleIdentifyAsync(msg);
                return;
            }
            case MessageTypes.StartMatch:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<StartMatchMsg>(json);
                if (msg != null) _lobby.HandleStartMatch(msg);
                return;
            }
            case MessageTypes.AbortMatch:
            {
                if (!RequireAdmin(type)) return;
                _lobby.HandleAbortMatch();
                return;
            }
            case MessageTypes.ReturnToLobby:
            {
                if (!RequireAdmin(type)) return;
                _lobby.HandleReturnToLobby();
                return;
            }
            case MessageTypes.ShotFired:
            case MessageTypes.HitReport:
                // Faz 3: maç kanalı (relay + vuruş doğrulama) — şimdilik yok sayılır.
                return;
            default:
                Console.WriteLine($"[ClientConnection] bilinmeyen mesaj tipi '{type}' yok sayıldı.");
                return;
        }
    }

    /// <summary>Admin komutu admin olmayan bağlantıdan gelirse loglanıp yok sayılır (§5.2).</summary>
    private bool RequireAdmin(string type)
    {
        if (IsAdmin) return true;
        Console.WriteLine($"[ClientConnection] '{type}' admin komutu yetkisiz bağlantıdan geldi ({State?.Name ?? "hello öncesi"}) — yok sayıldı.");
        return false;
    }

    /// <summary>Tüm gönderimler (welcome, yayınlar, komutlar) tek semafordan geçer — tek-slot kuyruk.</summary>
    public async Task SendTextAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync();
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Bağlantıyı koparır; recv döngüsü çıkışında registry haberdar edilir.</summary>
    public void Abort()
    {
        _cts.Cancel();
        try { _socket.Abort(); } catch { /* zaten ölü */ }
    }

    /// <summary>HELLO_TIMEOUT içinde hello gelmezse bağlantı kapatılır (§8).</summary>
    private async Task HelloWatchdogAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(ArenaProtocol.HELLO_TIMEOUT), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (State == null)
        {
            Console.WriteLine("[ClientConnection] süresi içinde hello gelmedi — bağlantı kapatılıyor.");
            Abort();
        }
    }
}
