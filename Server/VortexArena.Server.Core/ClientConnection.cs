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

    /// <summary>Atma kapanışında (§5.4) hem çerçeve yollama hem karşı tarafın cevabı için pay (sn).</summary>
    private const double CloseHandshakeSeconds = 2;

    private readonly WebSocket _socket;
    private readonly PlayerRegistry _registry;
    private readonly LobbyService _lobby;
    private readonly MatchDirector _director;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    /// <summary>hello işlenip kayıt tamamlanınca dolar; öncesinde null.</summary>
    public PlayerState? State { get; internal set; }

    public string? DeviceId => State?.DeviceId;
    public bool IsAdmin => State?.Role == "admin";

    public ClientConnection(WebSocket socket, PlayerRegistry registry, LobbyService lobby, MatchDirector director)
    {
        _socket = socket;
        _registry = registry;
        _lobby = lobby;
        _director = director;
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
                        // Kapanışı BİZ başlattıysak (atma, §5.4) çerçeve zaten gitti; ikincisini
                        // yollamak hata verir. Yalnız karşı taraf başlattıysa el sıkışmayı kapatırız.
                        if (_socket.State == WebSocketState.CloseReceived)
                        {
                            try
                            {
                                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                            }
                            catch (WebSocketException) { /* karşı taraf çoktan gitti */ }
                        }
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
                // Durum güncellemesi + roster uzlaştırması (status.rosterVersion, §5.1).
                if (status != null) await _lobby.HandleStatusAsync(this, status);
                return;
            }
            case MessageTypes.SetIdentity:
            {
                if (State == null) return;
                var msg = JsonUtil.Deserialize<SetIdentityMsg>(json);
                if (msg == null) return;
                // Admin herkesin kimliğini değiştirir; oyuncu yalnız kendisininkini (§5.1).
                // playerId 0 = "kendim" kısayolu. set_team ile birebir aynı yetki kuralı.
                if (!IsAdmin && msg.playerId != 0 && msg.playerId != State.PlayerId)
                {
                    Console.WriteLine($"[ClientConnection] set_identity: {State.Name} başka oyuncunun kimliğini değiştiremez — yok sayıldı.");
                    return;
                }
                _lobby.HandleSetIdentity(this, msg);
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
                // ⚠️ set_team YALNIZ ADMİN komutudur (§5.2) — oyuncu kendi takımını da seçemez.
                // Takım kurgusu operatörün elindedir; oyuncunun kendini bir tarafa yazması
                // dengelemeyi (BalanceTeams) ve operatörün planını sessizce bozardı.
                if (!IsAdmin)
                {
                    Console.WriteLine($"[ClientConnection] set_team yalnız admin içindir ({State.Name}) — yok sayıldı.");
                    return;
                }
                _lobby.HandleSetTeam(this, msg);
                return;
            }
            case MessageTypes.SetCalibration:
            {
                if (State == null) return;
                if (State.Role != "player")
                {
                    Console.WriteLine($"[ClientConnection] set_calibration yalnız player içindir ({State.Name}) — yok sayıldı.");
                    return;
                }
                var msg = JsonUtil.Deserialize<SetCalibrationMsg>(json);
                if (msg != null) _lobby.HandleSetCalibration(this, msg);
                return;
            }
            case MessageTypes.Kick:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<KickMsg>(json);
                if (msg != null) await _lobby.HandleKickAsync(this, msg);
                return;
            }
            case MessageTypes.ClearCalibration:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<ClearCalibrationMsg>(json);
                if (msg != null) await _lobby.HandleClearCalibrationAsync(this, msg);
                return;
            }
            case MessageTypes.Identify:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<IdentifyMsg>(json);
                if (msg != null) await _lobby.HandleIdentifyAsync(this, msg);
                return;
            }
            case MessageTypes.StartMatch:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<StartMatchMsg>(json);
                if (msg != null) await _lobby.HandleStartMatchAsync(this, msg);
                return;
            }
            case MessageTypes.AbortMatch:
            {
                if (!RequireAdmin(type)) return;
                await _lobby.HandleAbortMatchAsync(this);
                return;
            }
            case MessageTypes.PauseMatch:
            {
                if (!RequireAdmin(type)) return;
                await _lobby.HandlePauseMatchAsync(this);
                return;
            }
            case MessageTypes.ResumeMatch:
            {
                if (!RequireAdmin(type)) return;
                await _lobby.HandleResumeMatchAsync(this);
                return;
            }
            case MessageTypes.ReturnToLobby:
            {
                if (!RequireAdmin(type)) return;
                await _lobby.HandleReturnToLobbyAsync(this);
                return;
            }
            case MessageTypes.SetSelection:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<SetSelectionMsg>(json);
                if (msg != null) await _lobby.HandleSetSelectionAsync(this, msg);
                return;
            }
            // Atış bildirimi burada YOKTUR (v4): UDP olay kanalına taşındı (0x03, §6.4) ve
            // StateHost'ta karşılanıyor. ⚠️ Geri getirilmez — 10 atış/sn/oyuncu otoriter WS
            // kanalını boğar. Eski istemciden `shot_fired` gelirse aşağıdaki default kolu bir
            // satır log basıp yok sayar (sürüm el sıkışması zaten reddeder, §4).
            case MessageTypes.HitReport:
            {
                if (State == null) return;
                var msg = JsonUtil.Deserialize<HitReportMsg>(json);
                if (msg != null) await _director.HandleHitReportAsync(State, msg);
                return;
            }
            case MessageTypes.ReviveRequest:
            {
                if (State == null) return;
                if (State.Role != "player")
                {
                    Console.WriteLine($"[ClientConnection] revive_request yalnız player içindir ({State.Name}) — yok sayıldı.");
                    return;
                }
                await _director.HandleReviveRequestAsync(State);
                return;
            }
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

    /// <summary>
    /// Atma kapanışı (§5.4): önce kapanış çerçevesi yollanır, ancak paydan sonra koparılır.
    /// ⚠️ <b>Abort ile kapatılmaz:</b> abortif kapanış (RST) istemcinin daha okumadığı
    /// <c>kicked</c> çerçevesini tamponundan silebilir — o zaman istemci kopuşu sıradan bir
    /// kesinti sanıp yeniden bağlanır, yani atılan oyuncu kendiliğinden geri gelirdi.
    /// Kapanış sebebi <see cref="ArenaProtocol.KICK_CLOSE_REASON"/>'dır: JSON kaybolsa bile
    /// istemci atıldığını buradan anlar.
    /// <para>Çağıran BEKLEMEZ (fire-and-forget) — payı admin bağlantısının alma döngüsünde
    /// harcamak diğer komutları geciktirirdi.</para>
    /// </summary>
    public async Task CloseAfterKickAsync()
    {
        try
        {
            await _sendLock.WaitAsync();
            try
            {
                if (_socket.State == WebSocketState.Open)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(CloseHandshakeSeconds));
                    await _socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure, ArenaProtocol.KICK_CLOSE_REASON, closeCts.Token);
                }
            }
            finally
            {
                _sendLock.Release();
            }

            // İstemci kendi kapanış çerçevesini yollarsa recv döngüsü zaten çıkar; bu pay
            // yalnız cevapsız kalan (donmuş/uçmuş) istemci için üst sınırdır.
            await Task.Delay(TimeSpan.FromSeconds(CloseHandshakeSeconds));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ClientConnection] atma kapanışı tamamlanamadı: {ex.Message}");
        }
        finally
        {
            Abort();
        }
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
