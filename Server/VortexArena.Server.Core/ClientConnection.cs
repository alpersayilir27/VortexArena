#nullable enable
using System.Net.WebSockets;
using System.Text;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Receive/send loop of one accepted WS client (cosmos ClientConnection pattern).</summary>
/// <remarks>Envelope rule: only the type is parsed first, unknown types are logged and ignored (§5).</remarks>
public sealed class ClientConnection
{
    /// <summary>Control messages are small JSONs; a larger body is a malformed/unwanted peer.</summary>
    private const int MaxMessageBytes = 256 * 1024;

    /// <summary>Budget (s) for both sending the close frame and the peer's reply on a kick (§5.4).</summary>
    private const double CloseHandshakeSeconds = 2;

    private readonly WebSocket _socket;
    private readonly PlayerRegistry _registry;
    private readonly LobbyService _lobby;
    private readonly MatchDirector _director;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Set once hello is handled and the record exists; null before that.</summary>
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
                        // If WE started the close (kick, §5.4) the frame is already sent and a second
                        // one throws; complete the handshake only if the peer started it.
                        if (_socket.State == WebSocketState.CloseReceived)
                        {
                            try
                            {
                                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                            }
                            catch (WebSocketException) { /* peer already gone */ }
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
                // Binary is ignored: the control channel carries text only (poses go over UDP 47822).
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
                if (State == null) return; // status before hello — ignored
                var status = JsonUtil.Deserialize<StatusMsg>(json);
                // State update + roster reconciliation (status.rosterVersion, §5.1).
                if (status != null) await _lobby.HandleStatusAsync(this, status);
                return;
            }
            case MessageTypes.SetIdentity:
            {
                if (State == null) return;
                var msg = JsonUtil.Deserialize<SetIdentityMsg>(json);
                if (msg == null) return;
                // Admin may change anyone's identity, a player only their own (§5.1); playerId 0 =
                // "myself". Same authority rule as set_team.
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
                // ⚠️ set_team is ADMIN-ONLY (§5.2) — a player may not even pick their own team: team
                // composition is the operator's, and self-assignment would silently break BalanceTeams
                // and the operator's plan.
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
            case MessageTypes.SetBodyScale:
            {
                if (State == null) return;
                if (State.Role != "player")
                {
                    Console.WriteLine($"[ClientConnection] set_body_scale yalnız player içindir ({State.Name}) — yok sayıldı.");
                    return;
                }
                var msg = JsonUtil.Deserialize<SetBodyScaleMsg>(json);
                if (msg != null) _lobby.HandleSetBodyScale(this, msg);
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
            case MessageTypes.ReloadCalibration:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<ReloadCalibrationMsg>(json);
                if (msg != null) await _lobby.HandleReloadCalibrationAsync(this, msg);
                return;
            }
            case MessageTypes.MeasureBodyScale:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<MeasureBodyScaleMsg>(json);
                if (msg != null) await _lobby.HandleMeasureBodyScaleAsync(this, msg);
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
            case MessageTypes.EndMatch:
            {
                if (!RequireAdmin(type)) return;
                await _lobby.HandleEndMatchAsync(this);
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
            case MessageTypes.SetFriendlyFire:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<SetFriendlyFireMsg>(json);
                if (msg != null) await _lobby.HandleSetFriendlyFireAsync(this, msg);
                return;
            }
            case MessageTypes.SetCalibrationMode:
            {
                if (!RequireAdmin(type)) return;
                var msg = JsonUtil.Deserialize<SetCalibrationModeMsg>(json);
                if (msg != null) await _lobby.HandleSetCalibrationModeAsync(this, msg);
                return;
            }
            // No shot notification here (v4): it moved to the UDP event channel (0x03, §6.4), handled
            // by StateHost. ⚠️ Do not bring it back — 10 shots/s/player floods the authoritative WS
            // channel. A `shot_fired` from an old client falls to the default arm below (the version
            // handshake rejects it anyway, §4).
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

    /// <summary>Admin commands from a non-admin connection are logged and ignored (§5.2).</summary>
    private bool RequireAdmin(string type)
    {
        if (IsAdmin) return true;
        Console.WriteLine($"[ClientConnection] '{type}' admin komutu yetkisiz bağlantıdan geldi ({State?.Name ?? "hello öncesi"}) — yok sayıldı.");
        return false;
    }

    /// <summary>All sends (welcome, broadcasts, commands) pass one semaphore — a single-slot queue.</summary>
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

    /// <summary>Tears the connection down; the registry is notified when the recv loop exits.</summary>
    public void Abort()
    {
        _cts.Cancel();
        try { _socket.Abort(); } catch { /* already dead */ }
    }

    /// <summary>Kick close (§5.4): sends the close frame first, tears down only after the grace.</summary>
    /// <remarks>⚠️ Never close with Abort: an abortive close (RST) can wipe the still-unread
    /// <c>kicked</c> frame from the client's buffer, so it treats the drop as an ordinary outage and
    /// reconnects — the kicked player would come back by itself. The close reason is
    /// <see cref="ArenaProtocol.KICK_CLOSE_REASON"/>, so the client still learns it was kicked even if
    /// the JSON is lost.
    /// <para>Fire-and-forget: spending the grace in the admin connection's receive loop would delay
    /// its other commands.</para></remarks>
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

            // If the client sends its own close frame the recv loop exits anyway; this grace is only an
            // upper bound for a frozen/vanished client that never answers.
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

    /// <summary>Closes the connection if no hello arrives within HELLO_TIMEOUT (§8).</summary>
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
