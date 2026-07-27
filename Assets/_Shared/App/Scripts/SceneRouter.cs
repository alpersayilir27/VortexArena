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
    /// <b>Rolden bağımsız (Faz 6):</b> admin de aynı sahneyi yükler — "her zaman sunucudaki
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
        /// Geç katılım senkronu: welcome'daki match fazı Lobby dışındaysa maç sahnesine yetiş.
        /// Admin için de geçerli — maç koşarken açılan admin doğrudan arena sahnesine düşer.
        /// </summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            if (msg == null || msg.match == null ||
                string.IsNullOrEmpty(msg.match.phase) || msg.match.phase == "Lobby" ||
                string.IsNullOrEmpty(msg.match.sceneName))
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

        private void HandleReturnToLobby()
        {
            LastMatchScene = "";
            LastModeId = "";
            _readyReportedScene = "";

            LoadChecked(AppSession.SceneLobby);
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
