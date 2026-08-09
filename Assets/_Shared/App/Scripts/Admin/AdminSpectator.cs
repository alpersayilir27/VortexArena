using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VortexArena.Core.Arena;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Admin gözlemcinin kökü: sahneyi devralır, kamerayı/HUD'ı/işaretçileri sahiplenir ve
    /// klavye kısayollarını işler. Rol <c>admin</c> DEĞİLSE kendini yok eder — VR build'de
    /// hiçbir maliyeti yoktur.
    ///
    /// <para><b>Neden kendini önyükler:</b> admin artık Lobby ve TÜM arena sahnelerinde geziniyor.
    /// Sahneye elle konan bir bileşen, yeni arena eklerken unutulacak bir adım olurdu
    /// (arena sahneleri kendine yeten kutulardır). Bu yüzden `ConnectionOverlay` deseni:
    /// `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` + `DontDestroyOnLoad` tekil.</para>
    ///
    /// <para><b>Rol ne zaman biliniyor:</b> `AppBoot.Start()` bu kancadan SONRA koşar, yani
    /// önyüklemede rol henüz çözülmemiş olabilir. Bu yüzden karar <see cref="Update"/> içinde
    /// tembel verilir: rol çözülene kadar bekler, admin ise etkinleşir, player ise ölür.</para>
    ///
    /// <para><b>Sahne devralma (her <c>sceneLoaded</c>):</b>
    /// <list type="bullet">
    /// <item>BB Camera Rig kökü KAPATILIR — üç kamerası da `MainCamera` etiketli olduğu için
    /// `Camera.main` belirsiz kalır ve `RemoteAvatar` ad etiketlerini yanlış kameraya döndürür.
    /// Standalone'da XR açılışta başlar (editörde Link ile player için) ama admin rolü onu
    /// <see cref="AdminXrRelease"/> ile bırakır, yani rig işlevsizdir.</item>
    /// <item><see cref="ArenaCalibrator"/> ve <see cref="BaseZone"/> bileşenleri kapatılır —
    /// OVRSpatialAnchor/HMD mantığı masaüstünde anlamsız veri ve log üretir.</item>
    /// <item><see cref="ArenaBoundary"/> <b>KAPATILMAZ</b>, `SetSpectatorMode(true)` ile susturulur:
    /// admin'in HMD'si olmadığı için muhafaza mantığı anlamsızdır, ama kuş bakışı kadrajı onun
    /// <c>HalfExtents</c>/<c>LocalCenter</c> değerlerini okumaya devam ediyor (kapatılan bileşen
    /// planı çözmeyi de bırakırdı).</item>
    /// <item>World-space canvas'lar kapatılır (Lobby'nin VR paneli masaüstü ekranında havada
    /// durmasın; aynı bilgi HUD roster'ında var).</item>
    /// <item>EventSystem devralınır (arena sahnelerinde HİÇ yok, Lobby'de bir tane var).</item>
    /// </list></para>
    /// </summary>
    public class AdminSpectator : MonoBehaviour
    {
        public static AdminSpectator Instance { get; private set; }

        /// <summary>Gözlemci kamerası (etkinleşmeden önce null).</summary>
        public Camera Camera { get; private set; }

        /// <summary>Aktif sahnenin arena sınırı; Lobby gibi sınırsız sahnelerde null.</summary>
        public ArenaBoundary Boundary { get; private set; }

        private AdminSpectatorCamera _cameraDriver;
        private bool _active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            // Quest build'inde admin rolü hiç oluşmaz — tekili kurmaya bile gerek yok.
            if (Application.platform == RuntimePlatform.Android)
            {
                return;
            }

            var go = new GameObject("[AdminSpectator]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AdminSpectator>();
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

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void Update()
        {
            if (!_active)
            {
                TryActivate();
                return;
            }

            ReadShortcuts();
        }

        // ------------------------------------------------------------ etkinleşme

        private void TryActivate()
        {
            if (!AppSession.RoleResolved)
            {
                return; // AppBoot henüz rolü yazmadı
            }

            if (AppSession.Role != AppSession.RoleAdmin)
            {
                Destroy(gameObject); // oyuncu istemcisi: gözlemciye hiç ihtiyaç yok
                return;
            }

            _active = true;

            gameObject.AddComponent<AdminRoster>();
            // Adminler arası ortak seçim (mod/harita) — birden çok operatör aynı ekranı görsün.
            gameObject.AddComponent<AdminSelection>();

            var cameraGo = new GameObject("[AdminSpectatorCamera]");
            cameraGo.transform.SetParent(transform, false);
            cameraGo.tag = "MainCamera"; // Camera.main = bizim kamera (RemoteAvatar etiketleri)

            Camera = cameraGo.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.Skybox;
            Camera.fieldOfView = 70f;
            Camera.nearClipPlane = 0.05f;
            Camera.farClipPlane = 300f;

            // Rig kapatıldığı için sahnede dinleyici kalmaz ("no audio listener" uyarısı).
            cameraGo.AddComponent<AudioListener>();

            _cameraDriver = cameraGo.AddComponent<AdminSpectatorCamera>();
            gameObject.AddComponent<AdminPlayerMarkers>();
            SpawnHud();

            AdoptScene(SceneManager.GetActiveScene());
            Debug.Log("[AdminSpectator] Admin gözlemci etkin — sahne devralındı.");
        }

        /// <summary>
        /// Yönetim arayüzünü prefabtan örnekler (<c>Resources/UI/AdminHud</c>).
        /// <para>
        /// ⚠️ Prefab SAHNEYE KONMAZ, buradan yüklenir: sahneye konsaydı her yeni arena sahnesine
        /// elle bir kurulum adımı doğardı ve bir gün unutulurdu. Aynı sebeple gözlemcinin
        /// altına örneklenir — gözlemci kalıcıdır (DontDestroyOnLoad), arayüz de öyle olur ve
        /// lobi ↔ arena geçişlerinde yeniden kurulmaz.
        /// </para>
        /// </summary>
        private void SpawnHud()
        {
            var prefab = Resources.Load<AdminHud>(AdminHud.ResourcePath);
            if (prefab == null)
            {
                Debug.LogError(
                    $"[AdminSpectator] '{AdminHud.ResourcePath}' prefabı bulunamadı — yönetim " +
                    "arayüzü çizilemiyor. Tools > VortexArena > Bake UI Prefabs ile üretilmeli.");
                return;
            }

            AdminHud hud = Instantiate(prefab, transform);
            hud.name = "AdminHud";
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_active)
            {
                AdoptScene(scene);
            }
        }

        /// <summary>Sahneyi gözlemci için hazırlar (idempotent: aynı sahnede tekrar çağrılabilir).</summary>
        private void AdoptScene(Scene scene)
        {
            UiKit.TakeOverEventSystem();

            // 1) BB Camera Rig kökü (OVRCameraRig + OVRManager + kumanda modelleri).
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            if (rig != null && rig.gameObject.activeSelf)
            {
                rig.gameObject.SetActive(false);
            }

            // 2) HMD/kumanda mantığı taşıyan bileşenler.
            ArenaCalibrator calibrator = FindFirstObjectByType<ArenaCalibrator>(FindObjectsInactive.Include);
            if (calibrator != null)
            {
                calibrator.enabled = false;
            }

            BaseZone[] zones = FindObjectsByType<BaseZone>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] != null)
                {
                    zones[i].enabled = false;
                }
            }

            // 3) Arena sınırı: KAPATILMAZ, susturulur — kuş bakışı kadrajı onun HalfExtents /
            //    LocalCenter değerlerini okumaya devam ediyor.
            Boundary = FindFirstObjectByType<ArenaBoundary>();
            if (Boundary != null)
            {
                Boundary.SetSpectatorMode(true);
            }

            // 4) VR için tasarlanmış world-space panelleri (Lobby paneli, mod HUD'ları).
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas != null && canvas.renderMode == RenderMode.WorldSpace &&
                    !canvas.transform.IsChildOf(transform))
                {
                    canvas.gameObject.SetActive(false);
                }
            }

            // 5) Çatı: kuş bakışında tepeden içerisi görülsün (tercihe göre; §çatı ArenaRoof).
            //    ArenaRoof.OnEnable son alfayı kendine uyguladığı için burada yalnız tazeleriz —
            //    tercih sahne yüklenmeden değişmişse de doğru değere oturur.
            RefreshRoof();

            if (_cameraDriver != null)
            {
                _cameraDriver.OnSceneAdopted();
            }
        }

        /// <summary>
        /// Çatı görünürlüğünü o anki tercih + kamera kipine göre uygular. Çağıranlar: sahne
        /// devralma, kamera kipi değişimi (<see cref="AdminSpectatorCamera"/>) ve tercih paneli.
        /// Sahnede çatı yoksa hiçbir şey yapmaz (arenaların çoğu açık tavanlıdır).
        /// </summary>
        public static void RefreshRoof()
        {
            ArenaRoof.ApplyAll(AdminSession.RoofAlphaNow());
        }

        // ------------------------------------------------------------- kısayollar

        /// <summary>
        /// Genel kısayollar: 1/2/3 kamera kipi · Tab sonraki oyuncu · F seçiliye POV ·
        /// P tercihler · I istatistikler · Esc açık paneli kapat.
        /// Kamera içi girdi (WASD/QE/fare/tekerlek) <see cref="AdminSpectatorCamera"/>'da.
        /// </summary>
        private void ReadShortcuts()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                AdminSession.CameraMode = AdminCameraMode.Pov;
            }
            else if (keyboard.digit2Key.wasPressedThisFrame)
            {
                AdminSession.CameraMode = AdminCameraMode.Free;
            }
            else if (keyboard.digit3Key.wasPressedThisFrame)
            {
                AdminSession.CameraMode = AdminCameraMode.TopDown;
            }

            if (keyboard.tabKey.wasPressedThisFrame && AdminRoster.Instance != null)
            {
                AdminSession.SelectedPlayerId =
                    AdminRoster.Instance.NextPlayerId(AdminSession.SelectedPlayerId);
            }

            if (keyboard.fKey.wasPressedThisFrame && AdminSession.SelectedPlayerId != 0)
            {
                AdminSession.CameraMode = AdminCameraMode.Pov;
            }

            if (keyboard.pKey.wasPressedThisFrame)
            {
                AdminSession.TogglePanel(AdminPanelKind.Preferences);
            }

            if (keyboard.iKey.wasPressedThisFrame)
            {
                AdminSession.TogglePanel(AdminPanelKind.Stats);
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                AdminSession.ClosePanel();
            }
        }
    }
}
