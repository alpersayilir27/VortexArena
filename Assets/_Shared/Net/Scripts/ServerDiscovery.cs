using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VortexArena.Protocol;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Networking;
#endif

namespace VortexArena.Net
{
    /// <summary>
    /// Sunucu keşfi tekili: UDP 47820'de beacon dinler (Android'de MulticastLock ile),
    /// geçerli beacon'ı ana thread'de OnBeacon olayıyla yayınlar ve öncelik zinciri
    /// yardımcıları sunar: PlayerPrefs (elle girilen IP) > beacon > arena.json.
    /// Beacon yalnız otomatik doldurma kolaylığıdır — bağlanma kararını App verir.
    /// </summary>
    public class ServerDiscovery : MonoBehaviour
    {
        private const string ConfigFileName = "arena.json";

        public const string PrefKeyServerIp = "arena.serverIp";
        public const string PrefKeyServerPort = "arena.serverPort";

        public static ServerDiscovery Instance { get; private set; }

        /// <summary>Ana thread'de tetiklenir: (beacon, çözümlenmiş sunucu IP'si).</summary>
        public event Action<BeaconMsg, string> OnBeacon;

        public BeaconMsg LastBeacon { get; private set; }
        public string LastBeaconIp { get; private set; }
        public ArenaStaticConfig StaticConfig { get; private set; } = new ArenaStaticConfig();

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private CancellationTokenSource _cts;
        private bool _warnedVersion;
        private bool _shutdown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[ServerDiscovery]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ServerDiscovery>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            if (Instance != this)
            {
                return;
            }

            StartCoroutine(LoadStaticConfigRoutine());

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            _ = Task.Run(() => ListenLoopAsync(token)).ContinueWith(
                t => Debug.LogError($"[ServerDiscovery] Beacon dinleyici beklenmedik biçimde sonlandı: {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
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
                    Debug.LogError($"[ServerDiscovery] Ana thread aksiyonu hata verdi: {e}");
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        // -------------------------------------------------- öncelik zinciri yardımcıları

        /// <summary>Elle girilen adresi kalıcı yazar (beacon'ı her zaman ezer).</summary>
        public static void SaveManualEndpoint(string ip, int port)
        {
            PlayerPrefs.SetString(PrefKeyServerIp, ip);
            PlayerPrefs.SetInt(PrefKeyServerPort, port);
            PlayerPrefs.Save();
        }

        /// <summary>PlayerPrefs'teki elle girilmiş adresi okur; yoksa false.</summary>
        public static bool TryGetSavedEndpoint(out string ip, out int port)
        {
            ip = PlayerPrefs.GetString(PrefKeyServerIp, "");
            port = PlayerPrefs.GetInt(PrefKeyServerPort, ArenaProtocol.CONTROL_PORT);
            if (string.IsNullOrWhiteSpace(ip))
            {
                return false;
            }

            if (port <= 0)
            {
                port = ArenaProtocol.CONTROL_PORT;
            }

            return true;
        }

        /// <summary>Öncelik zinciri: PlayerPrefs > son beacon > arena.json. Hiçbiri yoksa false.</summary>
        public bool TryGetPreferredEndpoint(out string ip, out int port)
        {
            if (TryGetSavedEndpoint(out ip, out port))
            {
                return true;
            }

            if (LastBeacon != null && !string.IsNullOrEmpty(LastBeaconIp))
            {
                ip = LastBeaconIp;
                port = LastBeacon.controlPort > 0 ? LastBeacon.controlPort : ArenaProtocol.CONTROL_PORT;
                return true;
            }

            ArenaStaticConfig config = StaticConfig;
            if (config != null && !string.IsNullOrWhiteSpace(config.serverIp) &&
                IPAddress.TryParse(config.serverIp, out IPAddress _))
            {
                ip = config.serverIp;
                port = config.serverPort > 0 ? config.serverPort : ArenaProtocol.CONTROL_PORT;
                return true;
            }

            ip = "";
            port = ArenaProtocol.CONTROL_PORT;
            return false;
        }

        /// <summary>"ip" ya da "ip:port" metnini çözer; port yoksa CONTROL_PORT kullanılır.</summary>
        public static bool TryParseEndpoint(string text, out string ip, out int port)
        {
            ip = "";
            port = ArenaProtocol.CONTROL_PORT;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();
            int colon = trimmed.IndexOf(':');
            string ipPart = colon >= 0 ? trimmed.Substring(0, colon) : trimmed;
            if (!IPAddress.TryParse(ipPart, out IPAddress _))
            {
                return false;
            }

            ip = ipPart;
            if (colon >= 0 && int.TryParse(trimmed.Substring(colon + 1), out int parsedPort) && parsedPort > 0)
            {
                port = parsedPort;
            }

            return true;
        }

        // ------------------------------------------------------------ arena.json

        private IEnumerator LoadStaticConfigRoutine()
        {
            string path = Path.Combine(Application.streamingAssetsPath, ConfigFileName);
            string json = null;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Android'de StreamingAssets APK (jar) içindedir; File API çalışmaz.
            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    json = request.downloadHandler.text;
                }
                else
                {
                    Debug.Log($"[ServerDiscovery] {ConfigFileName} okunamadı ({request.error}); statik adres yok.");
                }
            }
#else
            try
            {
                if (File.Exists(path))
                {
                    json = File.ReadAllText(path);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerDiscovery] {ConfigFileName} okunamadı: {e.Message}; statik adres yok.");
            }

            yield return null; // Editor/masaüstü derlemesinde de metodun iterator kalması için
#endif

            StaticConfig = ParseConfig(json);
        }

        private static ArenaStaticConfig ParseConfig(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ArenaStaticConfig();
            }

            try
            {
                ArenaStaticConfig config = JsonUtility.FromJson<ArenaStaticConfig>(json);
                return config != null ? config : new ArenaStaticConfig();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerDiscovery] {ConfigFileName} parse edilemedi: {e.Message}; statik adres yok.");
                return new ArenaStaticConfig();
            }
        }

        // -------------------------------------------------------- beacon dinleme

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            object multicastLock = AcquireMulticastLock();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    UdpClient udp = null;
                    try
                    {
                        udp = new UdpClient();
                        // Editor + build aynı makinede aynı portu dinleyebilsin.
                        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        udp.Client.Bind(new IPEndPoint(IPAddress.Any, ArenaProtocol.UDP_BEACON_PORT));

                        // İptalde soket kapatılır → bekleyen ReceiveAsync hata ile düşer.
                        using (ct.Register(() => { try { udp.Close(); } catch (Exception) { } }))
                        {
                            while (!ct.IsCancellationRequested)
                            {
                                UdpReceiveResult datagram = await udp.ReceiveAsync();
                                HandleDatagram(datagram);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            break;
                        }

                        Debug.LogWarning($"[ServerDiscovery] Beacon dinleme hatası: {e.Message}");
                        try
                        {
                            await Task.Delay(1000, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                    finally
                    {
                        udp?.Dispose();
                    }
                }
            }
            finally
            {
                ReleaseMulticastLock(multicastLock);
            }
        }

        /// <summary>Ağ thread'inde koşar; geçerli beacon ana thread kuyruğuna atılır.</summary>
        private void HandleDatagram(UdpReceiveResult datagram)
        {
            string json = Encoding.UTF8.GetString(datagram.Buffer);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            BeaconMsg beacon;
            try
            {
                beacon = JsonUtility.FromJson<BeaconMsg>(json);
            }
            catch (Exception)
            {
                return; // beacon olmayan/deforme datagram — yok say
            }

            if (beacon == null || beacon.app != ArenaProtocol.APP_ID)
            {
                return;
            }

            if (beacon.ver != ArenaProtocol.PROTOCOL_VERSION && !_warnedVersion)
            {
                _warnedVersion = true;
                Debug.LogWarning($"[ServerDiscovery] Beacon protokol sürümü farklı (sunucu {beacon.ver}, istemci {ArenaProtocol.PROTOCOL_VERSION}); bağlantı yine de denenebilir.");
            }

            string ip = beacon.ip;
            if (!IPAddress.TryParse(ip, out IPAddress _))
            {
                ip = datagram.RemoteEndPoint.Address.ToString(); // JSON'daki ip bozuksa gönderenin adresi
            }

            string resolvedIp = ip;
            _mainThreadActions.Enqueue(() =>
            {
                LastBeacon = beacon;
                LastBeaconIp = resolvedIp;
                OnBeacon?.Invoke(beacon, resolvedIp);
            });
        }

        // --------------------------------------------------------- MulticastLock

        /// <summary>
        /// Android'de Wi-Fi broadcast/multicast paketlerinin uygulamaya ulaşabilmesi
        /// için WifiManager MulticastLock alınır (kilit olmadan bazı cihazlar
        /// broadcast'i düşürür). Diğer platformlarda no-op (null döner).
        /// </summary>
        private static object AcquireMulticastLock()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Arka plan thread'inden JNI kullanımı için thread JVM'e bağlanmalı
                // (zaten bağlıysa no-op).
                AndroidJNI.AttachCurrentThread();

                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaObject wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    AndroidJavaObject multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "vortexarena");
                    multicastLock.Call("setReferenceCounted", false);
                    multicastLock.Call("acquire");
                    return multicastLock;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerDiscovery] MulticastLock alınamadı: {e.Message}");
                return null;
            }
#else
            return null;
#endif
        }

        private static void ReleaseMulticastLock(object multicastLock)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var javaLock = multicastLock as AndroidJavaObject;
            if (javaLock == null)
            {
                return;
            }

            try
            {
                // Continuation farklı bir thread'de koşabilir — JVM'e yeniden bağlan.
                AndroidJNI.AttachCurrentThread();
                javaLock.Call("release");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerDiscovery] MulticastLock bırakılamadı: {e.Message}");
            }
            finally
            {
                javaLock.Dispose();
            }
#endif
            // Diğer platformlarda no-op.
        }

        private void Shutdown()
        {
            if (_shutdown)
            {
                return;
            }

            _shutdown = true;

            try
            {
                _cts?.Cancel();
            }
            catch (Exception)
            {
                // CTS zaten dispose olduysa yut — uygulama kapanıyor.
            }
        }
    }
}
