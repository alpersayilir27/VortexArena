using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Root of the admin spectator: adopts the scene, owns the camera/HUD/markers and handles
    /// keyboard shortcuts. Destroys itself outside the <c>admin</c> role — zero cost in the VR build.
    ///
    /// <para><b>Why it bootstraps itself:</b> the admin roams the Lobby and ALL arena scenes, so a
    /// hand-placed component would be a setup step to forget per arena. Hence the
    /// `ConnectionOverlay` pattern: a `DontDestroyOnLoad` singleton installed by `AppSingletons`.</para>
    ///
    /// <para><b>Role timing:</b> `AppBoot.Start()` runs AFTER this hook, so the decision is made
    /// lazily in <see cref="Update"/>: wait for the role, then activate (admin) or die (player).</para>
    ///
    /// <para><b>Scene adoption (every <c>sceneLoaded</c>):</b>
    /// <list type="bullet">
    /// <item>The BB Camera Rig root is DISABLED — all three of its cameras are tagged `MainCamera`,
    /// leaving `Camera.main` ambiguous so `RemoteAvatar` labels face the wrong camera. The rig is
    /// dead weight anyway since the admin releases XR (<see cref="AdminXrRelease"/>).</item>
    /// <item><see cref="ArenaCalibrator"/> and <see cref="BaseZone"/> are disabled — OVRSpatialAnchor
    /// /HMD logic produces meaningless data and logs on the desktop.</item>
    /// <item><see cref="ArenaBoundary"/> is <b>NOT disabled</b> but silenced via
    /// `SetSpectatorMode(true)`: the top-down framing still reads its
    /// <c>HalfExtents</c>/<c>LocalCenter</c>, which a disabled component would stop resolving.</item>
    /// <item>World-space canvases are disabled (the Lobby VR panel must not float on the desktop
    /// screen; the same info is in the HUD roster).</item>
    /// <item>The EventSystem is taken over (arena scenes have none, the Lobby has one).</item>
    /// </list></para>
    /// </summary>
    public class AdminSpectator : MonoBehaviour
    {
        public static AdminSpectator Instance { get; private set; }

        /// <summary>Spectator camera (null before activation).</summary>
        public Camera Camera { get; private set; }

        /// <summary>Active scene's arena boundary; null in unbounded scenes like the Lobby.</summary>
        public ArenaBoundary Boundary { get; private set; }

        private AdminSpectatorCamera _cameraDriver;
        private bool _active;

        /// <summary>Installs the singleton. ⚠️ The session decision (<b>which role needs it</b>)
        /// belongs to <see cref="AppSingletons"/>; the only gate here is this class' own
        /// existence condition.</summary>
        internal static void Install()
        {
            if (Instance != null)
            {
                return;
            }

            // The admin role never occurs in the Quest build.
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

        // ------------------------------------------------------------- activation

        private void TryActivate()
        {
            if (!AppSession.RoleResolved)
            {
                return; // AppBoot has not written the role yet
            }

            if (AppSession.Role != AppSession.RoleAdmin)
            {
                Destroy(gameObject); // player client: no spectator needed at all
                return;
            }

            _active = true;

            // Hiding enemy name labels is a GAME rule (RemoteAvatar); the operator must see who is
            // where, so the spectator is exempt.
            RemoteAvatar.SpectatorMode = true;

            // Window mode restores the operator's last choice (F11 / preferences). ⚠️ Admin role
            // only: the player client runs on Quest, where there are no windows.
            AdminSession.ApplyScreenMode();

            // Same for audio output, so the app does not open on the system default instead of the
            // venue's speakers. ⚠️ Admin role only — Quest has a single audio path.
            AdminSession.ApplyAudioOutput();

            // Seats the stored mix in the engine; otherwise everything plays unattenuated until the
            // panel is opened once. ⚠️ Admin role only — nothing writes AudioMix on the VR client
            // and its default is 1.
            AdminSession.ApplyAudioMix();

            gameObject.AddComponent<AdminRoster>();
            // Shared mode/map selection so multiple operators see the same screen.
            gameObject.AddComponent<AdminSelection>();

            // Venue background music from a folder on this PC. ⚠️ Admin role only — it reads a
            // Windows folder and plays on the operator's speakers; the headsets never hear it.
            gameObject.AddComponent<AdminMusicPlayer>();

            var cameraGo = new GameObject("[AdminSpectatorCamera]");
            cameraGo.transform.SetParent(transform, false);
            cameraGo.tag = "MainCamera"; // Camera.main = our camera (RemoteAvatar labels)

            Camera = cameraGo.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.Skybox;
            Camera.fieldOfView = 70f;
            Camera.nearClipPlane = 0.05f;
            Camera.farClipPlane = 300f;

            // Post-processing: admin only. URP defaults it to off on a camera built at runtime, so
            // the project's default volume profile (tonemapping/bloom/vignette) would never render.
            // ⚠️ Deliberately NOT enabled on the VR rig — the cost lands on the Quest GPU budget.
            Camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // The disabled rig leaves no listener in the scene ("no audio listener" warning).
            cameraGo.AddComponent<AudioListener>();

            _cameraDriver = cameraGo.AddComponent<AdminSpectatorCamera>();
            gameObject.AddComponent<AdminPlayerMarkers>();
            SpawnHud();

            AdoptScene(SceneManager.GetActiveScene());
            Debug.Log("[AdminSpectator] Admin gözlemci etkin — sahne devralındı.");
        }

        /// <summary>
        /// Instantiates the management UI from <c>Resources/UI/AdminHud</c>.
        /// <para>
        /// ⚠️ The prefab is NEVER placed in a scene: that would be a manual setup step per arena,
        /// forgotten one day. It is parented to the spectator so it inherits DontDestroyOnLoad and
        /// survives lobby ↔ arena transitions.
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

        /// <summary>Prepares the scene for the spectator (idempotent).</summary>
        private void AdoptScene(Scene scene)
        {
            UiKit.TakeOverEventSystem();

            // 1) BB Camera Rig root (OVRCameraRig + OVRManager + controller models).
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
            if (rig != null && rig.gameObject.activeSelf)
            {
                rig.gameObject.SetActive(false);
            }

            // 2) Components carrying HMD/controller logic.
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

            // 3) Arena boundary: silenced, NOT disabled — the top-down framing still reads its
            //    HalfExtents / LocalCenter.
            Boundary = FindFirstObjectByType<ArenaBoundary>();
            if (Boundary != null)
            {
                Boundary.SetSpectatorMode(true);
            }

            // 4) World-space panels designed for VR (Lobby panel, mode HUDs).
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

            // 5) Roof: see inside from above in top-down (per preference, ArenaRoof).
            //    ArenaRoof.OnEnable applies the last alpha itself, so this is only a refresh for
            //    preferences changed before the scene loaded.
            RefreshRoof();

            if (_cameraDriver != null)
            {
                _cameraDriver.OnSceneAdopted();
            }
        }

        /// <summary>
        /// Applies roof visibility from the current preference + camera mode. Callers: scene
        /// adoption, camera mode change (<see cref="AdminSpectatorCamera"/>), preferences panel.
        /// A no-op when the scene has no roof.
        /// </summary>
        public static void RefreshRoof()
        {
            ArenaRoof.ApplyAll(AdminSession.RoofAlphaNow());
        }

        // -------------------------------------------------------------- shortcuts

        /// <summary>
        /// Global shortcuts: 1/2/3 camera mode · Tab next player · F POV on selection ·
        /// P preferences · I stats · F11 fullscreen/windowed · Esc close panel.
        /// Camera-local input (WASD/QE/mouse/wheel) lives in <see cref="AdminSpectatorCamera"/>.
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

            // ⚠️ AdminSession applies and broadcasts the mode change; Screen is not touched here,
            // else the preference and the actual window state diverge.
            if (keyboard.f11Key.wasPressedThisFrame)
            {
                AdminSession.ToggleScreenMode();
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                AdminSession.ClosePanel();
            }
        }
    }
}
