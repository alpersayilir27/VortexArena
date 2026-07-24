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
    /// </summary>
    public class SceneRouter : MonoBehaviour
    {
        public static SceneRouter Instance { get; private set; }

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
        }

        private void OnDisable()
        {
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
        }

        /// <summary>Geç katılım senkronu: welcome'daki match fazı Lobby dışındaysa maç sahnesine yetiş.</summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            if (AppSession.Role != AppSession.RolePlayer)
            {
                return;
            }

            if (msg == null || msg.match == null ||
                string.IsNullOrEmpty(msg.match.phase) || msg.match.phase == "Lobby" ||
                string.IsNullOrEmpty(msg.match.sceneName))
            {
                return;
            }

            LoadChecked(msg.match.sceneName);
        }

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.sceneName))
            {
                Debug.LogError("[SceneRouter] load_match mesajında sahne adı yok; yok sayıldı.");
                return;
            }

            if (AppSession.Role != AppSession.RolePlayer)
            {
                // Admin AdminConsole'da kalır (taktik üstten görünüm Faz 3+).
                Debug.Log($"[SceneRouter] Admin rolünde load_match ('{msg.sceneName}') sahne yüklemez.");
                return;
            }

            LoadChecked(msg.sceneName);
        }

        private void HandleReturnToLobby()
        {
            if (AppSession.Role != AppSession.RolePlayer)
            {
                return; // admin zaten AdminConsole'da
            }

            LoadChecked(AppSession.SceneLobby);
        }

        private static void LoadChecked(string sceneName)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneRouter] '{sceneName}' build listesinde yok; sahne yüklenemedi.");
                return;
            }

            if (SceneManager.GetActiveScene().name == sceneName)
            {
                return;
            }

            Debug.Log($"[SceneRouter] Sahne yükleniyor → '{sceneName}'.");
            SceneManager.LoadScene(sceneName);
        }
    }
}
