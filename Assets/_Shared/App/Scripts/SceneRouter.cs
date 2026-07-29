using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Sunucu sahne komutlarını (load_match / return_to_lobby + welcome geç katılım
    /// senkronu) sahne yüklemeye çevirir; Net katmanı sahne yüklemediği için köprü
    /// budur. UnityEngine.SceneManagement.SceneManager'ı gölgelememek için adı
    /// SceneRouter'dır. Kalıcı singleton — kendini önyükler.
    /// Ayrıca §10.1 Loading adımını kapatır: maç sahnesi yüklenince sunucuya
    /// set_ready{true} ("sahne yüklendi") gönderir.
    /// <para>
    /// <b>Rolden bağımsızdır:</b> admin de aynı sahneyi yükler — "her zaman sunucudaki
    /// aktif sahne" kuralı gözlemci görünümünün temelidir (§2). Rol yalnız <b>tek</b> yerde
    /// ayrışır: <see cref="ReportSceneLoaded"/> içindeki <c>set_ready</c> yalnız player'dan
    /// gider. Admin "hazır" görünürse operatör yanılır, ayrıca Loading kapısı zaten yalnız
    /// <c>role=player</c> bağlantılarını sayar (sunucu <c>OnlinePlayersLocked</c>).
    /// </para>
    /// </summary>
    public class SceneRouter : MonoBehaviour
    {
        public static SceneRouter Instance { get; private set; }

        /// <summary>Sunucunun en son istediği maç sahnesi (load_match / welcome.match); lobide boş.</summary>
        public string LastMatchScene { get; private set; } = "";

        /// <summary>Sunucunun en son istediği mod (HUD seçimi için ModeHudSpawner okur).</summary>
        public string LastModeId { get; private set; } = "";

        /// <summary>Sunucunun bildirdiği lobi sahnesi (§10.7, <c>server.json → lobbyScene</c>).
        /// Boşsa kabuk <c>Lobby</c> sahnesi kullanılır — sunucuya bağlanmadan önce zaten tek
        /// bildiğimiz sahne odur.</summary>
        public string LobbyScene { get; private set; } = "";

        /// <summary>
        /// Sunucunun <b>açık sahnesi</b> (§10.7): maç koşuyorsa arena, koşmuyorsa lobi ya da
        /// operatörün sahnelediği arena. Sunucu ayaktayken boş olmaz — açılış değeri işletmenin
        /// lobi haritasıdır. Bağlanmadan önce boştur (henüz hiçbir şey bilinmiyor).
        /// <para>Admin arayüzü "şu an ne açık" sorusunu buradan cevaplar: kendi harita imleci
        /// bir sonraki maçın adayıdır, açık sahne ise sunucunun söylediği gerçektir.</para>
        /// </summary>
        public string OpenScene => LastMatchScene.Length > 0 ? LastMatchScene : LobbyScene;

        /// <summary>Aynı maç sahnesi için set_ready bir kez gönderilir.</summary>
        private string _readyReportedScene = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[SceneRouter]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SceneRouter>();
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

        private void OnEnable()
        {
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        /// <summary>
        /// Bağlanınca sunucunun <b>açık sahnesine</b> gidilir — istemcinin tek yönlendirme
        /// kaynağı budur (§5.3). Maç koşuyorsa o arenadır (geç katılım), koşmuyorsa işletmenin
        /// lobisi ya da operatörün sahnelediği arena. Admin için de geçerli.
        /// </summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            if (msg?.match == null)
            {
                return;
            }

            // Maç dışında da sahne gelir (§10.7): sunucunun AÇIK SAHNESİ — işletmenin lobisi ya da
            // operatörün sahnelediği arena. Bu bir maç DEĞİLDİR — RememberMatch çağrılmaz, yani
            // set_ready gönderilmez ve ModeHudSpawner maç HUD'u aramaz.
            // Ayrım fazdan değil TÜRDEN gelir (§10.1): lobi türü açıkken maç kurulmamıştır.
            if (msg.match.modeId == ArenaProtocol.LOBBY_MODE_ID)
            {
                LobbyScene = msg.match.sceneName ?? "";
                LoadLobbyChecked();
                return;
            }

            if (string.IsNullOrEmpty(msg.match.sceneName))
            {
                return;
            }

            RememberMatch(msg.match.modeId, msg.match.sceneName);
            LoadChecked(msg.match.sceneName);
        }

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.sceneName))
            {
                Debug.LogError("[SceneRouter] load_match mesajında sahne adı yok; yok sayıldı.");
                return;
            }

            RememberMatch(msg.modeId, msg.sceneName);
            LoadChecked(msg.sceneName);
        }

        /// <summary>Lobiye dönüş (§10.7): hedef sahne sunucudan gelir, sabit değildir.
        /// <c>LastMatchScene</c> temizlenir — lobi bir maç sahnesi olmadığı için <c>set_ready</c>
        /// gönderilmemelidir.</summary>
        private void HandleReturnToLobby(ReturnToLobbyMsg msg)
        {
            LastMatchScene = "";
            LastModeId = "";
            _readyReportedScene = "";

            LobbyScene = msg?.sceneName ?? "";
            LoadLobbyChecked();
        }

        /// <summary>
        /// **Admin harita önizlemesi:** seçili arenayı YEREL olarak yükler (sunucuya hiçbir şey
        /// gönderilmez). Operatör tercihler panelinde haritayı değiştirdiğinde, maç başlamamışsa
        /// o arenayı hemen görebilsin diye vardır.
        /// <para>
        /// Kasıtlı olarak <see cref="LastMatchScene"/>/<see cref="LastModeId"/>'ye DOKUNMAZ:
        /// onlar sunucunun söylediği gerçektir. Böylece sunucu maçı başlattığında
        /// <c>load_match</c> normal yolundan gelir ve önizleme durumu hiçbir şeyi bozmaz
        /// (aynı sahnedeyse yükleme atlanır).
        /// </para>
        /// Yalnız admin rolünde iş yapar — oyuncu istemcisinde sahne yükleme kararı SUNUCUNUNDUR.
        /// </summary>
        public void LoadPreview(string sceneName)
        {
            if (AppSession.Role != AppSession.RoleAdmin)
            {
                Debug.LogWarning("[SceneRouter] Harita önizlemesi yalnız admin rolünde kullanılır.");
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            LoadChecked(sceneName);
        }

        /// <summary>Sunucudan gelen maç hedefini saklar (ModeHudSpawner + loading bildirimi okur).</summary>
        private void RememberMatch(string modeId, string sceneName)
        {
            LastMatchScene = sceneName ?? "";
            LastModeId = modeId ?? "";
            _readyReportedScene = "";
        }

        /// <summary>
        /// Lobi sahnesini yükler. Sunucunun bildirdiği sahne yoksa ya da bu build'in sahne
        /// listesinde değilse <b>kabuk <c>Lobby</c> sahnesine düşer</b>: oyuncunun lobisiz
        /// kalması, yanlış yapılandırılmış bir lobiden daha kötüdür (bağlantı/kurtarma arayüzü
        /// orada). Düşüş sessiz değildir — sebep konsola yazılır.
        /// </summary>
        private void LoadLobbyChecked()
        {
            string target = LobbyScene;

            if (!string.IsNullOrEmpty(target) && !Application.CanStreamedLevelBeLoaded(target))
            {
                Debug.LogError(
                    $"[SceneRouter] Sunucunun lobi sahnesi '{target}' bu build'in sahne listesinde " +
                    $"yok — kabuk '{AppSession.SceneLobby}' sahnesine dönülüyor. (Build Settings + " +
                    "server.json → lobbyScene uyumunu kontrol edin.)");
                target = "";
            }

            LoadChecked(string.IsNullOrEmpty(target) ? AppSession.SceneLobby : target);
        }

        private void LoadChecked(string sceneName)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneRouter] '{sceneName}' build listesinde yok; sahne yüklenemedi.");
                return;
            }

            if (SceneManager.GetActiveScene().name == sceneName)
            {
                // Zaten bu sahnedeyiz: sceneLoaded tetiklenmeyecek → hazır bildirimini elden ver.
                ReportSceneLoaded(sceneName);
                return;
            }

            Debug.Log($"[SceneRouter] Sahne yükleniyor → '{sceneName}'.");
            SceneManager.LoadScene(sceneName);
        }

        // ------------------------------------------------- §10.1 Loading bildirimi

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ReportSceneLoaded(scene.name);
        }

        /// <summary>
        /// Yüklenen sahne, sunucunun istediği maç sahnesiyse "sahne yüklendi" anlamında
        /// set_ready{true} gönderir (§10.1 Loading). Lobi sahnesi ve admin rolü es geçilir.
        /// </summary>
        private void ReportSceneLoaded(string sceneName)
        {
            if (AppSession.Role != AppSession.RolePlayer)
            {
                return;
            }

            if (string.IsNullOrEmpty(sceneName) || sceneName != LastMatchScene)
            {
                return;
            }

            if (_readyReportedScene == sceneName)
            {
                return; // aynı maç sahnesi için bir kez
            }

            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return;
            }

            _readyReportedScene = sceneName;
            client.Send(new SetReadyMsg { ready = true });
            Debug.Log($"[SceneRouter] '{sceneName}' yüklendi → set_ready gönderildi.");
        }
    }
}
