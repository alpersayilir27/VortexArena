using UnityEngine;
using VortexArena.Core;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Arena sahnesinde duran köprü: aktif modun HUD prefabını (GameCatalog →
    /// ModeDefinition.HudPrefab) örnekler. App katmanı mod assembly'lerini
    /// REFERANSLAMAZ — prefab burada yalnız GameObject olarak taşınır, mod
    /// bileşeninin tipine dokunulmaz. Yalnız player rolünde çalışır.
    /// <para>
    /// ⚠️ <b>HUD modId'ye bağlıdır, sahne ömrüne değil:</b> mod değişimi sahne değişimi olmadan
    /// gelebilir — aynı haritada yeni maç, ya da sahnelenmiş arenada başlayan maç (sahne zaten
    /// yüklü, SceneRouter yeniden yüklemez). <c>load_match</c> farklı bir mod getirirse mevcut HUD
    /// yok edilip yenisi örneklenir; eski modun HUD'ı kalsaydı yeni modun bileşenleri (ör.
    /// turnuvanın toplanma raporlayıcısı) hiç doğmaz ve maç akışı sessizce kilitlenirdi
    /// (Docs/Sistem-Ozeti.md §7 "mod değişimi sahne değişimi değildir").
    /// </para>
    /// </summary>
    public class ModeHudSpawner : MonoBehaviour
    {
        [Header("Katalog")]
        [SerializeField] private GameCatalog catalog;

        [Tooltip("HUD'ın altına örnekleneceği kök; boşsa Camera.main transformu kullanılır.")]
        [SerializeField] private Transform hudParent;

        private GameObject _hudInstance;

        /// <summary>Örneklenen HUD'ın modId'si — mod değişimini sahne değişiminden bağımsız
        /// yakalamak için.</summary>
        private string _spawnedModeId = "";

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
            // SceneRouter'da saklanır.
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
            if (AppSession.Role != AppSession.RolePlayer)
            {
                return; // admin gözlemci: mod HUD'ı oyuncuya aittir, AdminHud ayrı çizilir
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

            if (_hudInstance != null)
            {
                if (mode.ModeId == _spawnedModeId)
                {
                    return; // aynı mod — HUD zaten doğru
                }

                // Mod değişti ama sahne değişmedi (sınıf dokümanındaki uyarı): eski HUD gider.
                Destroy(_hudInstance);
                _hudInstance = null;
                _spawnedModeId = "";
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
            _spawnedModeId = mode.ModeId;

            // Yerel poz/rotasyon prefabtan gelir (VR'da kamera altında sabit konum).
            _hudInstance.transform.localPosition = prefab.transform.localPosition;
            _hudInstance.transform.localRotation = prefab.transform.localRotation;
        }

        /// <summary>
        /// modId'yi katalogdan çözer; bulunamazsa <c>null</c> (HUD örneklenmez).
        /// <para>
        /// ⚠️ Boş modId bağlı bir oturumda "ortada maç yok" demektir (sahnelenmiş arena lobi
        /// profiliyle koşar, §10.7) ve HUD'suzluk doğru durumdur. Katalogdaki İLK moda düşüş
        /// yalnız SUNUCUSUZ editör sandbox'ına aittir — bağlıyken yanlış modun HUD'ını örneklemek
        /// HUD'suzluktan kötüdür: hatasız görünür, yanlış davranır (o modun bileşenleri eksik).
        /// </para>
        /// </summary>
        private ModeDefinition ResolveMode(string modeId)
        {
            if (!string.IsNullOrEmpty(modeId))
            {
                ModeDefinition found = catalog.FindMode(modeId);
                if (found != null)
                {
                    return found;
                }

                Debug.LogWarning($"[ModeHudSpawner] '{modeId}' modu katalogda yok; HUD örneklenmedi.");
                return null;
            }

            ArenaClient client = ArenaClient.Instance;
            bool sandbox = Application.isEditor && (client == null || !client.IsConnected);
            if (!sandbox)
            {
                return null; // maç yok — HUD'suzluk doğru durum (yukarıdaki gerekçe)
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
