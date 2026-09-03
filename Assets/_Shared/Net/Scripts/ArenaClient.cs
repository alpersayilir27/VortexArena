using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VortexArena.Protocol;
using EngineScenes = UnityEngine.SceneManagement.SceneManager;

namespace VortexArena.Net
{
    /// <summary>Connection state (published through NetEvents.OnConnectionStateChanged).</summary>
    public enum ArenaConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    /// <summary>
    /// Persistent WS control client singleton: Connect(ip, port, role) connects, sends hello, awaits
    /// welcome and heartbeats a status every 5 s; on a drop it retries the same address forever with a
    /// 1→2→5 s backoff (unless Disconnect was called). A link silent for HEARTBEAT_TIMEOUT is dropped
    /// by the client itself (<see cref="LinkWatchdogAsync"/>, §8) — TCP alone never notices a dead Wi-Fi.
    ///
    /// Network work runs on background Tasks; anything needing the Unity API is moved to the main
    /// thread (Update) through a ConcurrentQueue. Server messages are published through NetEvents —
    /// this class LOADS NO SCENES and holds no game knowledge.
    /// </summary>
    public class ArenaClient : MonoBehaviour
    {
        private const int ReceiveBufferSize = 64 * 1024;

        public static ArenaClient Instance { get; private set; }

        public ArenaConnectionState State { get; private set; } = ArenaConnectionState.Disconnected;

        /// <summary>The 1..PLAYER_ID_MAX id assigned in welcome (0 = none yet).</summary>
        public int PlayerId { get; private set; }
        public uint UdpToken { get; private set; }
        public string ServerIp { get; private set; }
        public int ServerPort { get; private set; }

        /// <summary>The UDP pose channel living on the same persistent object.</summary>
        public UdpStateChannel UdpChannel { get; private set; }

        /// <summary>Remote player pose registry: rings snapshots, read back interpolated.</summary>
        public RemotePlayerRegistry Remotes { get; private set; }

        /// <summary>Remote player skeleton registry (§6.10): rings <c>0x08</c> entries.
        /// ⚠️ SEPARATE from <see cref="Remotes"/>: the two channels differ in cadence and entry
        /// lifetime (the blob is consumed, the root interpolated).</summary>
        public RemoteSkeletonRegistry RemoteSkeletons { get; private set; }

        /// <summary>Remote OBJECT pose registry (§6.12): rings the object section of <c>0x05</c>.
        /// ⚠️ SEPARATE from <see cref="Remotes"/> (an object is not a player) but on the SAME clock —
        /// two time bases would drift a held object away from the hand holding it.</summary>
        public RemoteObjectRegistry RemoteObjects { get; private set; }

        /// <summary>Is the socket open — safe from any thread.</summary>
        public bool IsConnected => IsSocketOpen;

        /// <summary>Which connect attempt we are on since the last success (0 once connected); shown by
        /// the connection error screen (ConnectionOverlay).</summary>
        public int ConnectAttempts => _connectAttempts;

        /// <summary>Message of the last connect error (cleared on connect); may be empty.</summary>
        public string LastError => _lastError;

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _cts;
        private volatile ClientWebSocket _socket;
        private volatile string _role = "player";
        private volatile string _currentSceneName = "";
        private volatile bool _userDisconnect = true; // no reconnect until Connect is called
        private bool _shutdown;

        // Written only by the connection loop thread, read by the main thread (atomic int/string assign).
        private volatile int _connectAttempts;
        private volatile string _lastError = "";

        /// <summary>UTC ticks of the last frame received on the socket — the link watchdog's only
        /// input. Written by the receive loop, read by the watchdog (Interlocked).</summary>
        private long _lastReceivedTicks;

        // Device info cached on the main thread for hello (the net thread cannot touch the Unity API).
        private string _hardwareId;
        private string _adminSessionId;
        private string _deviceName;
        private string _appVersion;
        private string[] _buildScenes;

        // Controller state (§5.1): App MEASURES, this layer only carries — same pattern as
        // `battery`/`rttMs`. It cannot be measured here: VortexArena.Net does not and will not
        // reference Oculus.VR (the asmdef graph always points down).
        // A client that does not report, and every admin, stays at CONTROLLER_UNKNOWN.
        private int _ctrlL = ArenaProtocol.CONTROLLER_UNKNOWN;
        private int _ctrlR = ArenaProtocol.CONTROLLER_UNKNOWN;

        // fps measurement: accumulated in Update, read and reset by StatusLoop each interval.
        private int _frameCount;
        private float _fpsElapsed;
        private float _lastFps;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[ArenaClient]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ArenaClient>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            UdpChannel = gameObject.AddComponent<UdpStateChannel>();
            Remotes = gameObject.AddComponent<RemotePlayerRegistry>();
            RemoteSkeletons = gameObject.AddComponent<RemoteSkeletonRegistry>();
            RemoteObjects = gameObject.AddComponent<RemoteObjectRegistry>();

            _hardwareId = SystemInfo.deviceUniqueIdentifier;
            // The admin id is PER SESSION so two admin windows can run on one PC (§2); the GUID is
            // generated once in Awake and survives reconnects (so it finds the same record).
            _adminSessionId = Guid.NewGuid().ToString("N");
            _deviceName = SystemInfo.deviceName;
            _appVersion = Application.version;

            int sceneCount = EngineScenes.sceneCountInBuildSettings;
            _buildScenes = new string[sceneCount];
            for (int i = 0; i < sceneCount; i++)
            {
                _buildScenes[i] = Path.GetFileNameWithoutExtension(
                    UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i));
            }

            _currentSceneName = EngineScenes.GetActiveScene().name;
            EngineScenes.activeSceneChanged += OnActiveSceneChanged;
        }

        private void Start()
        {
            if (Instance != this)
            {
                return;
            }

            StartCoroutine(StatusLoop());
        }

        private void Update()
        {
            if (Instance != this)
            {
                return;
            }

            while (_mainThreadActions.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ArenaClient] Ana thread aksiyonu hata verdi: {e}");
                }
            }

            _frameCount++;
            _fpsElapsed += Time.unscaledDeltaTime;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            EngineScenes.activeSceneChanged -= OnActiveSceneChanged;
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        // ------------------------------------------------------------- public API

        /// <summary>Connects to the given address; closes a previous connection/loop if there is one.</summary>
        public void Connect(string ip, int port, string role)
        {
            if (string.IsNullOrWhiteSpace(ip) || port <= 0)
            {
                Debug.LogWarning($"[ArenaClient] Geçersiz adres: '{ip}:{port}'; bağlanılmadı.");
                return;
            }

            StopConnectionLoop();

            ServerIp = ip.Trim();
            ServerPort = port;
            _role = string.IsNullOrWhiteSpace(role) ? "player" : role.Trim();
            _userDisconnect = false;

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            string loopIp = ServerIp;
            int loopPort = ServerPort;

            _ = Task.Run(() => RunConnectionLoopAsync(loopIp, loopPort, token)).ContinueWith(
                t => Debug.LogError($"[ArenaClient] Bağlantı döngüsü beklenmedik biçimde sonlandı: {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        /// <summary>Closes the connection and stops automatic retrying.</summary>
        public void Disconnect()
        {
            _userDisconnect = true;
            StopConnectionLoop();
            SetState(ArenaConnectionState.Disconnected);
        }

        /// <summary>
        /// Reports left/right controller state (<c>ArenaProtocol.CONTROLLER_*</c>); goes out with the
        /// next <c>status</c>. <c>PlayerPoseTracker</c> measures, this layer only carries (same pattern
        /// as <c>battery</c>/<c>rttMs</c>) because <c>VortexArena.Net</c> does not reference Oculus.VR.
        /// No extra packets: the field rides a message already sent every 5 s.
        /// </summary>
        public void ReportControllerState(int ctrlL, int ctrlR)
        {
            _ctrlL = ctrlL;
            _ctrlR = ctrlR;
        }

        /// <summary>Serialises a protocol DTO to JSON and sends it (no-op when the socket is closed).</summary>
        public void Send<T>(T msg) where T : class
        {
            if (msg == null)
            {
                return;
            }

            TrySendText(JsonUtility.ToJson(msg));
        }

        /// <summary>
        /// Fire-and-forget text send: a no-op when the socket is closed, errors are logged and swallowed
        /// (the reconnect loop recovers anyway). Callable from any thread.
        /// </summary>
        public void TrySendText(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            ClientWebSocket socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open)
            {
                return;
            }

            _ = SendGuardedAsync(json);
        }

        // ------------------------------------------------------- connection loop

        private void StopConnectionLoop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (Exception)
            {
                // Swallow it when the CTS is already disposed.
            }

            _cts = null;

            ClientWebSocket socket = _socket;
            _socket = null;
            if (socket != null)
            {
                try
                {
                    socket.Abort();
                }
                catch (Exception)
                {
                    // Swallow it when the socket is already closed.
                }
            }
        }

        private async Task RunConnectionLoopAsync(string ip, int port, CancellationToken ct)
        {
            int backoffIndex = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    SetState(ArenaConnectionState.Connecting);
                    _connectAttempts++;
                    var uri = new Uri($"ws://{ip}:{port}{ArenaProtocol.WS_PATH}");
                    Debug.Log($"[ArenaClient] Bağlanılıyor: {uri}");

                    using (var socket = new ClientWebSocket())
                    {
                        // Bounded attempt (CONNECT_TIMEOUT): a cancelled connect lands in the generic
                        // catch below (the loop's own token is not the one that fired) → backoff.
                        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            connectCts.CancelAfter(TimeSpan.FromSeconds(ArenaProtocol.CONNECT_TIMEOUT));
                            await socket.ConnectAsync(uri, connectCts.Token);
                        }

                        _socket = socket;
                        Interlocked.Exchange(ref _lastReceivedTicks, DateTime.UtcNow.Ticks);
                        backoffIndex = 0;
                        _connectAttempts = 0;
                        _lastError = "";
                        SetState(ArenaConnectionState.Connected);
                        Debug.Log("[ArenaClient] Bağlandı; hello gönderiliyor.");

                        await SendTextAsync(BuildHelloJson(), ct);

                        // The watchdog ends the receive loop by aborting the socket; the loop's
                        // exception is what lands in the catch below and starts the reconnect.
                        Task receive = ReceiveLoopAsync(socket, ct);
                        _ = LinkWatchdogAsync(socket, receive, ct);
                        await receive;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    _lastError = e.Message;
                    Debug.LogWarning($"[ArenaClient] Bağlantı koptu/başarısız: {e.Message}");
                }
                finally
                {
                    _socket = null;
                }

                if (ct.IsCancellationRequested || _userDisconnect)
                {
                    break;
                }

                SetState(ArenaConnectionState.Disconnected);

                // 1 → 2 → 5 s (last element is the ceiling), forever.
                float[] steps = ArenaProtocol.RECONNECT_BACKOFF;
                float delay = steps[Math.Min(backoffIndex, steps.Length - 1)];
                if (backoffIndex < steps.Length - 1)
                {
                    backoffIndex++;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            SetState(ArenaConnectionState.Disconnected);
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
        {
            byte[] buffer = new byte[ReceiveBufferSize];
            using (var message = new MemoryStream())
            {
                while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    Interlocked.Exchange(ref _lastReceivedTicks, DateTime.UtcNow.Ticks);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // The server signs a kick close with the reason field (§5.4): even if the
                        // `kicked` JSON lost the race, this drop means "kicked", not "reconnect".
                        if (socket.CloseStatusDescription == ArenaProtocol.KICK_CLOSE_REASON)
                        {
                            Debug.Log("[ArenaClient] Sunucu bağlantıyı ATMA sebebiyle kapattı.");
                            HandleKicked(new KickedMsg());
                        }
                        else
                        {
                            Debug.Log("[ArenaClient] Sunucu bağlantıyı kapattı.");
                        }

                        try
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, ct);
                        }
                        catch (Exception)
                        {
                            // The close handshake may fail; the reconnect path handles it.
                        }

                        return;
                    }

                    // A message may arrive in several segments — accumulate until EndOfMessage.
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                        HandleTextMessage(json);
                    }
                    // No binary WS message is expected (v1) → ignore.

                    message.SetLength(0);
                }
            }
        }

        /// <summary>Client-side dead-link detection (§8). A silently dead Wi-Fi link never errors on
        /// its own: sends sit in TCP retransmit and <c>ReceiveAsync</c> waits forever, so the headset
        /// would stay "connected" until the app is restarted. The server answers every status with
        /// <c>heartbeat</c>; HEARTBEAT_TIMEOUT without ANY frame = dead → abort → reconnect loop.</summary>
        private async Task LinkWatchdogAsync(ClientWebSocket socket, Task receive, CancellationToken ct)
        {
            long timeoutTicks = TimeSpan.FromSeconds(ArenaProtocol.HEARTBEAT_TIMEOUT).Ticks;

            try
            {
                while (!receive.IsCompleted && !ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);

                    long silence = DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastReceivedTicks);
                    if (receive.IsCompleted || silence < timeoutTicks)
                    {
                        continue;
                    }

                    Debug.LogWarning($"[ArenaClient] Sunucudan {ArenaProtocol.HEARTBEAT_TIMEOUT:0} sn'dir " +
                                     "mesaj yok — bağlantı ölü sayıldı, yeniden bağlanılacak.");
                    socket.Abort();
                    return;
                }
            }
            catch (Exception)
            {
                // Cancelled, or the socket is already gone: the receive loop reports that itself.
            }
        }

        // ------------------------------------------------------- message handlers

        /// <summary>Runs on the network thread; events are published on the main thread via the queue.</summary>
        private void HandleTextMessage(string json)
        {
            MsgEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<MsgEnvelope>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArenaClient] Geçersiz JSON mesajı yok sayıldı: {e.Message}");
                return;
            }

            if (envelope == null || string.IsNullOrEmpty(envelope.type))
            {
                Debug.LogWarning("[ArenaClient] 'type' alanı olmayan mesaj yok sayıldı.");
                return;
            }

            try
            {
                switch (envelope.type)
                {
                    case MessageTypes.Welcome:
                        HandleWelcome(JsonUtility.FromJson<WelcomeMsg>(json));
                        break;

                    case MessageTypes.LobbyState:
                    {
                        LobbyStateMsg msg = JsonUtility.FromJson<LobbyStateMsg>(json);
                        // §5.3: an old snapshot must NOT overwrite a newer one. A second safety net even
                        // though the server broadcasts from one publisher; the symptom of a stale roster
                        // would be "a kicked player still listed as online".
                        if (msg == null || msg.version <= _lastRosterVersion)
                        {
                            break;
                        }

                        _lastRosterVersion = msg.version;
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseLobbyState(msg));
                        break;
                    }

                    case MessageTypes.LoadMatch:
                    {
                        LoadMatchMsg msg = JsonUtility.FromJson<LoadMatchMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseLoadMatch(msg));
                        break;
                    }

                    case MessageTypes.Countdown:
                    {
                        CountdownMsg msg = JsonUtility.FromJson<CountdownMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseCountdown(msg));
                        break;
                    }

                    case MessageTypes.MatchState:
                    {
                        MatchStateMsg msg = JsonUtility.FromJson<MatchStateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseMatchState(msg));
                        break;
                    }

                    case MessageTypes.HealthUpdate:
                    {
                        HealthUpdateMsg msg = JsonUtility.FromJson<HealthUpdateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseHealthUpdate(msg));
                        break;
                    }

                    case MessageTypes.KillEvent:
                    {
                        KillEventMsg msg = JsonUtility.FromJson<KillEventMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseKillEvent(msg));
                        break;
                    }

                    case MessageTypes.Respawn:
                    {
                        RespawnMsg msg = JsonUtility.FromJson<RespawnMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseRespawn(msg));
                        break;
                    }

                    case MessageTypes.MatchEnd:
                    {
                        MatchEndMsg msg = JsonUtility.FromJson<MatchEndMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseMatchEnd(msg));
                        break;
                    }

                    case MessageTypes.ObjectState:
                    {
                        ObjectStateMsg msg = JsonUtility.FromJson<ObjectStateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseObjectState(msg));
                        break;
                    }

                    case MessageTypes.ObjectSpawn:
                    {
                        // Same body as object_state (§5.3) — only the TYPE separates a spawn from a
                        // drifted id, so it must not be merged into the case above.
                        ObjectStateMsg msg = JsonUtility.FromJson<ObjectStateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseObjectSpawn(msg));
                        break;
                    }

                    case MessageTypes.ObjectDespawn:
                    {
                        ObjectDespawnMsg msg = JsonUtility.FromJson<ObjectDespawnMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseObjectDespawn(msg));
                        break;
                    }

                    case MessageTypes.ObjectEvent:
                    {
                        ObjectEventMsg msg = JsonUtility.FromJson<ObjectEventMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseObjectEvent(msg));
                        break;
                    }

                    case MessageTypes.WorldState:
                    {
                        WorldStateMsg msg = JsonUtility.FromJson<WorldStateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseWorldState(msg));
                        break;
                    }

                    case MessageTypes.ReturnToLobby:
                    {
                        ReturnToLobbyMsg msg = JsonUtility.FromJson<ReturnToLobbyMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseReturnToLobby(msg));
                        break;
                    }

                    // v4: `shot_fired` was REMOVED — shots/throws ride UDP 0x03/0x04 (§6.4/6.5) and are
                    // published by UdpStateChannel. This type never arrives on WS.

                    case MessageTypes.MeasureBodyScale:
                    {
                        // Sent to players only (§10.8); no listener on admin. The measurement reads the
                        // rig/character, so it needs the Unity API → main thread.
                        _mainThreadActions.Enqueue(NetEvents.RaiseMeasureBodyScale);
                        break;
                    }

                    case MessageTypes.RestartBodyTracking:
                    {
                        // Sent to players only (§6.11); no listener on admin. The repair toggles a
                        // MonoBehaviour and calls OVRPlugin → main thread.
                        _mainThreadActions.Enqueue(NetEvents.RaiseRestartBodyTracking);
                        break;
                    }

                    case MessageTypes.ClearCalibration:
                    {
                        // The operator reset calibration (§10.6). Players only; no `playerId` (the target
                        // is this connection) but `keepSaved` does ride: soft keeps the device anchor,
                        // hard deletes it; a missing field reads `false` = hard. ⚠️ The roster's
                        // `calibrated` field is NOT consulted — in a half-finished calibration it is
                        // already `false`, so the reset has no visible delta there (§5.3). Touches
                        // scene/anchor → main thread.
                        ClearCalibrationMsg msg = JsonUtility.FromJson<ClearCalibrationMsg>(json);
                        bool keepSaved = msg != null && msg.keepSaved;
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseClearCalibration(keepSaved));
                        break;
                    }

                    case MessageTypes.ReloadCalibration:
                    {
                        // The operator asked for an alignment reload from the saved anchor (§10.6).
                        // Players only, fieldless: the target is this connection. An uncalibrated target
                        // is NOT skipped — that is exactly who the command is for. Touches anchor/rig →
                        // main thread.
                        _mainThreadActions.Enqueue(NetEvents.RaiseReloadCalibration);
                        break;
                    }

                    case MessageTypes.CalibrationResult:
                    {
                        // Admin connections only; on a player there is no listener.
                        CalibrationResultMsg msg = JsonUtility.FromJson<CalibrationResultMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseCalibrationResult(msg));
                        break;
                    }

                    case MessageTypes.AdminState:
                    {
                        // Admin connections only; on a player there is no listener.
                        AdminStateMsg msg = JsonUtility.FromJson<AdminStateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseAdminState(msg));
                        break;
                    }

                    case MessageTypes.RulesUpdate:
                    {
                        // Goes to everyone (§5.3). Unlike SelectionState this IS a real rule: the running
                        // match's shape changed (today only the friendly-fire switch) → ModeRuntime.
                        RulesUpdateMsg msg = JsonUtility.FromJson<RulesUpdateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseRulesUpdate(msg));
                        break;
                    }

                    case MessageTypes.SelectionState:
                    {
                        // Goes to everyone (§5.3). Presentation info, NOT a rule — never applied to
                        // ModeRuntime; written to ModeSelection (base strips).
                        SelectionStateMsg msg = JsonUtility.FromJson<SelectionStateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseSelectionState(msg));
                        break;
                    }

                    case MessageTypes.Heartbeat:
                        // Payload-less by design: its arrival already refreshed the link watchdog.
                        break;

                    case MessageTypes.Ping:
                        // ⚠️ NOT a latency measurement: it is the server's "send me a status" trigger.
                        // Latency is measured with UDP 0x06 (§6.7) — over TCP retransmits would
                        // contaminate it. status needs the Unity API → main thread.
                        _mainThreadActions.Enqueue(() => TrySendText(BuildStatusJson()));
                        break;

                    case MessageTypes.NetStats:
                    {
                        // Admin connections only; on a player there is no listener.
                        NetStatsMsg msg = JsonUtility.FromJson<NetStatsMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseNetStats(msg));
                        break;
                    }

                    case MessageTypes.Violation:
                    {
                        // Admin connections only; on a player there is no listener.
                        ViolationMsg msg = JsonUtility.FromJson<ViolationMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseViolation(msg));
                        break;
                    }

                    case MessageTypes.Kicked:
                        HandleKicked(JsonUtility.FromJson<KickedMsg>(json));
                        break;

                    default:
                        // Unknown type → log and ignore (forward version compatibility).
                        Debug.Log($"[ArenaClient] Bilinmeyen mesaj tipi '{envelope.type}' yok sayıldı.");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArenaClient] '{envelope.type}' mesajı işlenemedi: {e.Message}");
            }
        }

        /// <summary>
        /// Kick (§5.4). Runs on the network thread and disables reconnect **immediately**: if
        /// `_userDisconnect` were only set on the main thread (the queued `Disconnect`), the socket
        /// dropping meanwhile could start a backoff round and let the kicked player back in.
        /// The event + socket close are left to the main thread (Unity API + subscriber code).
        /// </summary>
        private void HandleKicked(KickedMsg msg)
        {
            _userDisconnect = true;

            _mainThreadActions.Enqueue(() =>
            {
                NetEvents.RaiseKicked(msg);
                // Protocol: the client closes the connection; no auto-reconnect.
                Disconnect();
            });
        }

        private void HandleWelcome(WelcomeMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (msg.protocolVersion != ArenaProtocol.PROTOCOL_VERSION)
            {
                // By protocol a mismatch does NOT drop the connection, it is only logged.
                Debug.LogWarning($"[ArenaClient] Protokol sürümü uyuşmuyor (sunucu {msg.protocolVersion}, istemci {ArenaProtocol.PROTOCOL_VERSION}); bağlantı sürdürülüyor.");
            }

            // New session = new version axis. Reset ON THE NETWORK THREAD (not queued): the lobby_state
            // following this welcome is handled on the network thread too, and waiting for the queue
            // would make the first roster look like an "old version" and get dropped.
            _lastRosterVersion = 0;

            _mainThreadActions.Enqueue(() =>
            {
                PlayerId = msg.playerId;
                UdpToken = msg.udpToken;

                // §8: UDP registration after welcome (0x00, retried until acked).
                if (UdpChannel != null && msg.playerId > 0)
                {
                    UdpChannel.StartRegistration(ServerIp, ArenaProtocol.STATE_PORT, (byte)msg.playerId, msg.udpToken);
                }

                NetEvents.RaiseConnected(msg);

                // Send battery/scene info without waiting for the first heartbeat.
                TrySendText(BuildStatusJson());
            });
        }

        // --------------------------------------------------------- state &amp; status

        /// <summary>The last APPLIED <c>lobby_state.version</c> (§5.3); net thread writes (guard), main
        /// thread reads (status) → volatile. Reset to 0 on every welcome: a restarted server counts
        /// versions from 0 again and without the reset the client would drop every roster as old.</summary>
        private volatile int _lastRosterVersion;

        private IEnumerator StatusLoop()
        {
            while (true)
            {
                yield return new WaitForSecondsRealtime(ArenaProtocol.STATUS_INTERVAL);

                if (_fpsElapsed > 0f)
                {
                    _lastFps = _frameCount / _fpsElapsed;
                }

                _frameCount = 0;
                _fpsElapsed = 0f;

                if (IsSocketOpen)
                {
                    TrySendText(BuildStatusJson());
                }
            }
        }

        /// <summary>Called from the network thread; uses only the cache + JsonUtility (thread-safe).</summary>
        private string BuildHelloJson()
        {
            var hello = new HelloMsg
            {
                protocolVersion = ArenaProtocol.PROTOCOL_VERSION,
                role = _role,
                deviceId = ResolveDeviceId(),
                deviceName = _deviceName,
                appVersion = _appVersion,
                currentScene = _currentSceneName,
                scenes = _buildScenes
            };
            return JsonUtility.ToJson(hello);
        }

        /// <summary>
        /// Per-role <c>deviceId</c> semantics (Docs/ArenaNet-Protokol.md §2):
        /// <list type="bullet">
        /// <item><b>player:</b> the plain hardware id — PERSISTENT. The server binds the name to it in
        /// <c>devices.json</c>, so a reconnecting headset keeps its playerId and name.</item>
        /// <item><b>admin:</b> <c>&lt;hardware&gt;:admin:&lt;session GUID&gt;</c> — PER SESSION, so two
        /// admin windows can run on one physical PC: with a shared id both would share the same server
        /// record and every <c>hello</c> would close the other's socket (an endless kick loop). The GUID
        /// is fixed for the process lifetime, so a reconnect finds the same record.</item>
        /// </list>
        /// Read here rather than in Awake because the role is given per connection.
        /// </summary>
        private string ResolveDeviceId()
        {
            return _role == "admin" ? $"{_hardwareId}:admin:{_adminSessionId}" : _hardwareId;
        }

        /// <summary>MAIN THREAD ONLY (SystemInfo / scene API).</summary>
        private string BuildStatusJson()
        {
            var status = new StatusMsg
            {
                scene = EngineScenes.GetActiveScene().name,
                battery = SystemInfo.batteryLevel,
                ctrlL = _ctrlL,
                ctrlR = _ctrlR,
                fps = _lastFps,
                // §5.1 reconciliation: if we fell behind, the server sends the full roster to US only.
                rosterVersion = _lastRosterVersion
            };

            // §6.7: the CLIENT measures net telemetry and reports it with status (no extra channel —
            // this message already goes every 5 s, plenty for an operator readout). Before the channel
            // exists the fields stay -1 (unknown).
            if (UdpChannel != null)
            {
                UdpChannel.SampleTelemetry(out int rttMs, out float jitterMs, out float lossPct);
                status.rttMs = rttMs;
                status.jitterMs = jitterMs;
                status.lossPct = lossPct;
            }

            return JsonUtility.ToJson(status);
        }

        private bool IsSocketOpen
        {
            get
            {
                ClientWebSocket socket = _socket;
                return socket != null && socket.State == WebSocketState.Open;
            }
        }

        /// <summary>Safe from any thread; events fire on the main thread and only on a change.</summary>
        private void SetState(ArenaConnectionState newState)
        {
            _mainThreadActions.Enqueue(() =>
            {
                if (State == newState)
                {
                    return;
                }

                ArenaConnectionState oldState = State;
                State = newState;

                if (newState != ArenaConnectionState.Connected && UdpChannel != null)
                {
                    UdpChannel.Stop();
                }

                NetEvents.RaiseConnectionStateChanged(newState);

                if (oldState == ArenaConnectionState.Connected)
                {
                    NetEvents.RaiseDisconnected();
                }
            });
        }

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene previous, UnityEngine.SceneManagement.Scene current)
        {
            _currentSceneName = current.name;
        }

        // -------------------------------------------------------------- sending

        private async Task SendGuardedAsync(string json)
        {
            try
            {
                await SendTextAsync(json, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ArenaClient] Gönderim başarısız: {e.Message}");
            }
        }

        /// <summary>Every send goes through one SemaphoreSlim (concurrent Send is forbidden on a WebSocket).</summary>
        private async Task SendTextAsync(string json, CancellationToken ct)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            await _sendLock.WaitAsync(ct);
            try
            {
                ClientWebSocket socket = _socket;
                if (socket == null || socket.State != WebSocketState.Open)
                {
                    return;
                }

                await socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, ct);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        // -------------------------------------------------------------- shutdown

        private void Shutdown()
        {
            if (_shutdown)
            {
                return;
            }

            _shutdown = true;
            _userDisconnect = true;
            StopConnectionLoop();
            Debug.Log("[ArenaClient] Kapatıldı.");
        }
    }
}
