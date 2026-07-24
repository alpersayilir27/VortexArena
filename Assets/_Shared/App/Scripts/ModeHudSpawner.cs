using UnityEngine;
using VortexArena.Core;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Arena sahnesinde duran köprü: aktif modun HUD prefabını (GameCatalog →
    /// ModeDefinition.HudPrefab) bir kez örnekler. App katmanı mod assembly'lerini
    /// REFERANSLAMAZ — prefab burada yalnız GameObject olarak taşınır, mod
    /// bileşeninin tipine dokunulmaz. Yalnız player rolünde çalışır.
    /// </summary>
    public class ModeHudSpawner : MonoBehaviour
    {
        [Header("Katalog")]
        [SerializeField] private GameCatalog catalog;

        [Tooltip("HUD'ın altına örnekleneceği kök; boşsa Camera.main transformu kullanılır.")]
        [SerializeField] private Transform hudParent;

        private GameObject _hudInstance;

        private void OnEnable()
        {
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnConnected += HandleConnected;
        }

        private void OnDisable()
        {
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnConnected -= HandleConnected;
        }

        private void Start()
        {
            // Normal akışta load_match sahne yüklenmeden ÖNCE gelir; son modId
            // SceneRouter'da saklanır. Yoksa katalogdaki ilk mod (Editor testi).
            string modeId = SceneRouter.Instance != null ? SceneRouter.Instance.LastModeId : "";
            SpawnHud(modeId);
        }

        // -------------------------------------------------------- olay işleyiciler

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg != null)
            {
                SpawnHud(msg.modeId);
            }
        }

        /// <summary>Geç katılım: welcome içindeki maç bilgisinden modu al.</summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            if (msg != null && msg.match != null)
            {
                SpawnHud(msg.match.modeId);
            }
        }

        // ---------------------------------------------------------------- kurulum

        private void SpawnHud(string modeId)
        {
            if (_hudInstance != null)
            {
                return; // zaten örneklendi — mod başına tek HUD
            }

            if (AppSession.Role != AppSession.RolePlayer)
            {
                return; // admin AdminConsole kabuğunda; VR HUD'ı yok
            }

            if (catalog == null)
            {
                Debug.LogWarning("[ModeHudSpawner] GameCatalog atanmadı; mod HUD'ı örneklenemiyor.");
                return;
            }

            ModeDefinition mode = ResolveMode(modeId);
            if (mode == null)
            {
                return;
            }

            GameObject prefab = mode.HudPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[ModeHudSpawner] '{mode.ModeId}' modunun HudPrefab'ı yok; HUD örneklenmedi.");
                return;
            }

            Transform parent = ResolveParent();
            _hudInstance = Instantiate(prefab, parent, false);
            _hudInstance.name = prefab.name;

            // Yerel poz/rotasyon prefabtan gelir (VR'da kamera altında sabit konum).
            _hudInstance.transform.localPosition = prefab.transform.localPosition;
            _hudInstance.transform.localRotation = prefab.transform.localRotation;
        }

        /// <summary>modeId boşsa/bulunamazsa katalogdaki ilk modu döndürür (Editor kolaylığı).</summary>
        private ModeDefinition ResolveMode(string modeId)
        {
            if (!string.IsNullOrEmpty(modeId))
            {
                ModeDefinition found = catalog.FindMode(modeId);
                if (found != null)
                {
                    return found;
                }

                Debug.LogWarning($"[ModeHudSpawner] '{modeId}' modu katalogda yok; ilk mod deneniyor.");
            }

            ModeDefinition[] modes = catalog.Modes;
            if (modes == null)
            {
                return null;
            }

            for (int i = 0; i < modes.Length; i++)
            {
                if (modes[i] != null)
                {
                    return modes[i];
                }
            }

            return null;
        }

        private Transform ResolveParent()
        {
            if (hudParent != null)
            {
                return hudParent;
            }

            Camera cam = Camera.main;
            return cam != null ? cam.transform : transform;
        }
    }
}
