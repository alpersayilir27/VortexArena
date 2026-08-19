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
    /// <summary>Bağlantı durumu (NetEvents.OnConnectionStateChanged ile yayınlanır).</summary>
    public enum ArenaConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    /// <summary>
    /// Kalıcı WS kontrol istemcisi tekili: Connect(ip, port, role) ile bağlanır,
    /// hello gönderir, welcome bekler, 5 sn'de bir status kalp atışı atar; kopuşta
    /// 1→2→5 sn backoff ile aynı adrese sonsuz yeniden dener (Disconnect çağrılmadıysa).
    ///
    /// Ağ işleri arka plan Task'larında koşar; Unity API'si gereken her iş
    /// ConcurrentQueue üzerinden ana thread'e (Update) taşınır. Sunucu mesajları
    /// NetEvents üzerinden yayınlanır — bu sınıf SAHNE YÜKLEMEZ, oyun bilgisi içermez.
    /// </summary>
    public class ArenaClient : MonoBehaviour
    {
        private const int ReceiveBufferSize = 64 * 1024;

        public static ArenaClient Instance { get; private set; }

        public ArenaConnectionState State { get; private set; } = ArenaConnectionState.Disconnected;

        /// <summary>welcome'da atanan 1..PLAYER_ID_MAX kimliği (0 = henüz yok).</summary>
        public int PlayerId { get; private set; }
        public uint UdpToken { get; private set; }
        public string ServerIp { get; private set; }
        public int ServerPort { get; private set; }

        /// <summary>Aynı kalıcı objede yaşayan UDP poz kanalı.</summary>
        public UdpStateChannel UdpChannel { get; private set; }

        /// <summary>Uzak oyuncu poz kayıtçısı: snapshot'ları biriktirir, interpolasyonlu okutur.</summary>
        public RemotePlayerRegistry Remotes { get; private set; }

        /// <summary>Uzak oyuncu iskelet kayıtçısı (§6.10): <c>0x08</c> girdilerini biriktirir.
        /// ⚠️ <see cref="Remotes"/>'tan AYRI: iki kanalın kadansı ve girdi ömrü farklıdır
        /// (blob tüketilir, kök interpole edilir).</summary>
        public RemoteSkeletonRegistry RemoteSkeletons { get; private set; }

        /// <summary>Soket açık mı — her thread'den güvenli.</summary>
        public bool IsConnected => IsSocketOpen;

        /// <summary>
        /// Son başarılı bağlantıdan beri kaçıncı bağlanma denemesindeyiz (bağlanınca 0).
        /// Bağlantı hata ekranı (ConnectionOverlay) bunu gösterir.
        /// </summary>
        public int ConnectAttempts => _connectAttempts;

        /// <summary>Son bağlanma hatasının mesajı (bağlanınca temizlenir); boş olabilir.</summary>
        public string LastError => _lastError;

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _cts;
        private volatile ClientWebSocket _socket;
        private volatile string _role = "player";
        private volatile string _currentSceneName = "";
        private volatile bool _userDisconnect = true; // Connect çağrılana dek reconnect yok
        private bool _shutdown;

        // Yalnız bağlantı döngüsü thread'i yazar, ana thread okur (int/string atomik atama).
        private volatile int _connectAttempts;
        private volatile string _lastError = "";

        // hello için ana thread'de önbelleğe alınan cihaz bilgileri
        // (ağ thread'i Unity API'sine dokunamaz).
        private string _hardwareId;
        private string _adminSessionId;
        private string _deviceName;
        private string _appVersion;
        private string[] _buildScenes;

        // Kumanda durumu (§5.1): ÖLÇÜMÜ App yapar, bu katman yalnız taşır — `battery`/`rttMs` ile
        // aynı desen. Burada ölçülemez: VortexArena.Net Oculus.VR'ı referanslamaz ve
        // referanslamayacak (asmdef grafiği hep aşağı bakar).
        // Bildirilmeyen istemci ve admin CONTROLLER_UNKNOWN'da kalır.
        private int _ctrlL = ArenaProtocol.CONTROLLER_UNKNOWN;
        private int _ctrlR = ArenaProtocol.CONTROLLER_UNKNOWN;

        // fps ölçümü: Update'te birikir, StatusLoop her aralıkta okuyup sıfırlar.
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

            _hardwareId = SystemInfo.deviceUniqueIdentifier;
            // Aynı PC'de iki admin penceresi açılabilsin diye admin kimliği OTURUMLUK olur (§2);
            // GUID Awake'te bir kez üretilir, yeniden bağlanmalarda aynı kalır (aynı kaydı bulsun).
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

        // ------------------------------------------------------------- genel API

        /// <summary>Verilen adrese bağlanır; önceki bağlantı/döngü varsa kapatır.</summary>
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

        /// <summary>Bağlantıyı kapatır ve otomatik yeniden denemeyi durdurur.</summary>
        public void Disconnect()
        {
            _userDisconnect = true;
            StopConnectionLoop();
            SetState(ArenaConnectionState.Disconnected);
        }

        /// <summary>
        /// Sol/sağ kumandanın durumunu bildirir (<c>ArenaProtocol.CONTROLLER_*</c>); bir sonraki
        /// <c>status</c> ile gider. Ölçümü yapan taraf <c>PlayerPoseTracker</c>'dır — bu katman
        /// yalnız taşır (<c>battery</c>/<c>rttMs</c> ile aynı desen), çünkü <c>VortexArena.Net</c>
        /// Oculus.VR'ı referanslamaz. Ek paket üretmez: alan zaten 5 sn'de bir giden mesajda.
        /// </summary>
        public void ReportControllerState(int ctrlL, int ctrlR)
        {
            _ctrlL = ctrlL;
            _ctrlR = ctrlR;
        }

        /// <summary>Protokol DTO'sunu JSON'a çevirip gönderir (soket kapalıysa no-op).</summary>
        public void Send<T>(T msg) where T : class
        {
            if (msg == null)
            {
                return;
            }

            TrySendText(JsonUtility.ToJson(msg));
        }

        /// <summary>
        /// Fire-and-forget text gönderimi: soket açık değilse no-op, hata loglanıp
        /// yutulur (reconnect döngüsü zaten kurtarır). Her thread'den çağrılabilir.
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

        // ------------------------------------------------------- bağlantı döngüsü

        private void StopConnectionLoop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (Exception)
            {
                // CTS zaten dispose olduysa yut.
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
                    // Soket zaten kapalıysa yut.
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
                        await socket.ConnectAsync(uri, ct);

                        _socket = socket;
                        backoffIndex = 0;
                        _connectAttempts = 0;
                        _lastError = "";
                        SetState(ArenaConnectionState.Connected);
                        Debug.Log("[ArenaClient] Bağlandı; hello gönderiliyor.");

                        await SendTextAsync(BuildHelloJson(), ct);
                        await ReceiveLoopAsync(socket, ct);
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

                // 1 → 2 → 5 sn (tavan son eleman), sonsuz.
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

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // Sunucu atma kapanışını sebep alanıyla imzalar (§5.4): `kicked` JSON'u
                        // kapanışa yetişemediyse bile bu kopuş "yeniden bağlan" değil "atıldın"dır.
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
                            // Kapanış el sıkışması başarısız olabilir; reconnect zaten devreye girer.
                        }

                        return;
                    }

                    // Mesaj birden çok segment hâlinde gelebilir — EndOfMessage'a dek biriktir.
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
                    // Binary WS mesajı beklenmez (v1) → yok say.

                    message.SetLength(0);
                }
            }
        }

        // ------------------------------------------------------- mesaj işleyiciler

        /// <summary>Ağ thread'inde koşar; olaylar kuyruk üzerinden ana thread'de yayınlanır.</summary>
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
                        // §5.3: eski anlık görüntü yeniyi EZEMEZ. Sunucuda yayın tek yayıncıdan
                        // gitse de bu ikinci emniyettir; bayat roster'ın belirtisi "atılan oyuncu
                        // hâlâ listede online görünüyor" olurdu.
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

                    case MessageTypes.ReturnToLobby:
                    {
                        ReturnToLobbyMsg msg = JsonUtility.FromJson<ReturnToLobbyMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseReturnToLobby(msg));
                        break;
                    }

                    // v4: `shot_fired` KALDIRILDI — atış/atma artık UDP 0x03/0x04 (§6.4/6.5),
                    // UdpStateChannel yayınlıyor. WS'te bu tip hiç gelmez.

                    case MessageTypes.Identify:
                    {
                        IdentifyMsg msg = JsonUtility.FromJson<IdentifyMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseIdentify(msg));
                        break;
                    }

                    case MessageTypes.MeasureBodyScale:
                    {
                        // Sunucu yalnız player'a yollar (§10.8); admin'de dinleyen yoktur.
                        // Ölçüm rig/karakter okuduğu için Unity API'si ister → ana thread.
                        _mainThreadActions.Enqueue(NetEvents.RaiseMeasureBodyScale);
                        break;
                    }

                    case MessageTypes.ClearCalibration:
                    {
                        // Operatör kalibrasyonu sıfırladı (§10.6). Sunucu yalnız player'a yollar;
                        // `playerId` taşınmaz (hedef zaten bu bağlantı) ama `keepSaved` taşınır:
                        // yumuşak kipte cihazdaki çapa korunur, sert kipte silinir. Alan yoksa
                        // `false` okunur = sert. ⚠️ Roster'daki `calibrated` alanına BAKILMAZ —
                        // yarım kalmış bir kalibrasyonda o alan zaten `false`'tur, yani sıfırlamanın
                        // orada görünür bir deltası yoktur (§5.3). Sahne/anchor dokunduğu için
                        // ana thread.
                        ClearCalibrationMsg msg = JsonUtility.FromJson<ClearCalibrationMsg>(json);
                        bool keepSaved = msg != null && msg.keepSaved;
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseClearCalibration(keepSaved));
                        break;
                    }

                    case MessageTypes.ReloadCalibration:
                    {
                        // Operatör kayıtlı çapadan hizalamayı yeniden yükletti (§10.6). Sunucu
                        // yalnız player'a yollar, alansız: hedef zaten bu bağlantı. Kalibresiz
                        // hedef ATLANMAZ — komut tam da onun için var. Anchor/rig dokunduğu için
                        // ana thread.
                        _mainThreadActions.Enqueue(NetEvents.RaiseReloadCalibration);
                        break;
                    }

                    case MessageTypes.CalibrationResult:
                    {
                        // Sunucu yalnız admin bağlantılarına yollar; player'a gelirse dinleyen yoktur.
                        CalibrationResultMsg msg = JsonUtility.FromJson<CalibrationResultMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseCalibrationResult(msg));
                        break;
                    }

                    case MessageTypes.AdminState:
                    {
                        // Sunucu yalnız admin bağlantılarına yollar; player'a gelirse zaten dinleyen yok.
                        AdminStateMsg msg = JsonUtility.FromJson<AdminStateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseAdminState(msg));
                        break;
                    }

                    case MessageTypes.RulesUpdate:
                    {
                        // Herkese gelir (§5.3). SelectionState'in aksine GERÇEK kuraldır: koşan
                        // maçın şekli değişti (bugün tek sebebi dost ateşi anahtarı) → ModeRuntime.
                        RulesUpdateMsg msg = JsonUtility.FromJson<RulesUpdateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseRulesUpdate(msg));
                        break;
                    }

                    case MessageTypes.SelectionState:
                    {
                        // Herkese gelir (§5.3). Kural DEĞİL sunum bilgisidir — ModeRuntime'a
                        // uygulanmaz; ModeSelection'a yazılır (taban şeritleri).
                        SelectionStateMsg msg = JsonUtility.FromJson<SelectionStateMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseSelectionState(msg));
                        break;
                    }

                    case MessageTypes.Ping:
                        // ⚠️ Bu bir GECİKME ÖLÇÜMÜ DEĞİL: sunucunun "bana bir status yolla" tetiği.
                        // Gecikme UDP 0x06 ile ölçülür (§6.7) — TCP üzerinden ölçmek retransmit'i
                        // sonuca karıştırır. status Unity API'si ister → ana thread.
                        _mainThreadActions.Enqueue(() => TrySendText(BuildStatusJson()));
                        break;

                    case MessageTypes.NetStats:
                    {
                        // Sunucu yalnız admin bağlantılarına yollar; player'a gelirse dinleyen yoktur.
                        NetStatsMsg msg = JsonUtility.FromJson<NetStatsMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseNetStats(msg));
                        break;
                    }

                    case MessageTypes.Violation:
                    {
                        // Sunucu yalnız admin bağlantılarına yollar; player'a gelirse dinleyen yoktur.
                        ViolationMsg msg = JsonUtility.FromJson<ViolationMsg>(json);
                        _mainThreadActions.Enqueue(() => NetEvents.RaiseViolation(msg));
                        break;
                    }

                    case MessageTypes.Kicked:
                        HandleKicked(JsonUtility.FromJson<KickedMsg>(json));
                        break;

                    default:
                        // Bilinmeyen tip → logla ve yok say (ileri sürüm uyumluluğu).
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
        /// Atılma (§5.4). Ağ thread'inde koşar: yeniden bağlanmayı **hemen** kapatır, çünkü
        /// `_userDisconnect` yalnız ana thread'de (kuyruktaki `Disconnect`) kalksaydı bu arada
        /// kopan soket backoff turunu başlatabilir ve atılan oyuncu geri bağlanabilirdi.
        /// Olay + soket kapatma ana thread'e bırakılır (Unity API'si + abone kodu).
        /// </summary>
        private void HandleKicked(KickedMsg msg)
        {
            _userDisconnect = true;

            _mainThreadActions.Enqueue(() =>
            {
                NetEvents.RaiseKicked(msg);
                // Protokol: istemci bağlantıyı kapatır; oto-reconnect yapılmaz.
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
                // Protokol gereği uyumsuzluk bağlantıyı KESMEZ, yalnız loglanır.
                Debug.LogWarning($"[ArenaClient] Protokol sürümü uyuşmuyor (sunucu {msg.protocolVersion}, istemci {ArenaProtocol.PROTOCOL_VERSION}); bağlantı sürdürülüyor.");
            }

            // Yeni oturum = yeni sürüm ekseni. AĞ THREAD'İNDE sıfırlanır (kuyruğa alınmaz):
            // bu welcome'ı izleyen lobby_state de ağ thread'inde işlenir, kuyruk beklenirse
            // ilk roster "eski sürüm" sanılıp atılırdı.
            _lastRosterVersion = 0;

            _mainThreadActions.Enqueue(() =>
            {
                PlayerId = msg.playerId;
                UdpToken = msg.udpToken;

                // §8: welcome sonrası UDP kaydı (0x00, ack'e dek tekrar).
                if (UdpChannel != null && msg.playerId > 0)
                {
                    UdpChannel.StartRegistration(ServerIp, ArenaProtocol.STATE_PORT, (byte)msg.playerId, msg.udpToken);
                }

                NetEvents.RaiseConnected(msg);

                // Batarya/sahne bilgisi ilk kalp atışını beklemeden sunucuya gitsin.
                TrySendText(BuildStatusJson());
            });
        }

        // --------------------------------------------------------- durum & status

        /// <summary>UYGULANAN son <c>lobby_state.version</c> (§5.3). Ağ thread'i yazar (guard),
        /// ana thread okur (status) → volatile. Her welcome'da 0'a döner: sunucu yeniden başlarsa
        /// sürüm de 0'dan başlar ve sıfırlamasak istemci tüm roster'ları eski sanıp atardı.</summary>
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

        /// <summary>Ağ thread'inden çağrılır; yalnız önbellek + JsonUtility (thread-safe) kullanır.</summary>
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
        /// Rol başına <c>deviceId</c> semantiği (Docs/ArenaNet-Protokol.md §2):
        /// <list type="bullet">
        /// <item><b>player:</b> düz donanım kimliği — KALICI. Sunucu adı <c>devices.json</c>'da
        /// buna bağlar, gözlük yeniden bağlandığında playerId'sini ve adını korur.</item>
        /// <item><b>admin:</b> <c>&lt;donanım&gt;:admin:&lt;oturum GUID'i&gt;</c> — OTURUMLUK.
        /// Aynı fiziksel PC'de iki admin penceresi açılabilsin diye: ortak kimlikle ikisi aynı
        /// sunucu kaydını paylaşır ve her <c>hello</c> diğerinin soketini kapatırdı (sonsuz kick
        /// döngüsü). GUID süreç ömrü boyunca sabittir — yeniden bağlanma aynı kaydı bulur.</item>
        /// </list>
        /// Rol bağlantı başına verildiği için burada okunur, Awake'te değil.
        /// </summary>
        private string ResolveDeviceId()
        {
            return _role == "admin" ? $"{_hardwareId}:admin:{_adminSessionId}" : _hardwareId;
        }

        /// <summary>YALNIZ ana thread'de çağrılır (SystemInfo/sahne API'si).</summary>
        private string BuildStatusJson()
        {
            var status = new StatusMsg
            {
                scene = EngineScenes.GetActiveScene().name,
                battery = SystemInfo.batteryLevel,
                ctrlL = _ctrlL,
                ctrlR = _ctrlR,
                fps = _lastFps,
                // §5.1 uzlaştırma: geride kaldıysak sunucu YALNIZ bize tam roster yollar.
                rosterVersion = _lastRosterVersion
            };

            // §6.7: ağ telemetrisini İSTEMCİ ölçer, status ile bildirir (ek kanal açılmaz — bu mesaj
            // zaten 5 sn'de bir gidiyor ve operatör göstergesi için o ritim fazlasıyla yeter).
            // Kanal henüz kurulmadıysa alanlar -1 (bilinmiyor) kalır.
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

        /// <summary>Herhangi bir thread'den güvenli; olaylar ana thread'de, yalnız değişimde tetiklenir.</summary>
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

        // -------------------------------------------------------------- gönderim

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

        /// <summary>Tüm gönderimler tek SemaphoreSlim'den geçer (WebSocket'te eşzamanlı Send yasak).</summary>
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

        // --------------------------------------------------------------- kapanış

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
