using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.UI;

namespace VortexArena.App
{
    /// <summary>
    /// Loading screen for scene transitions: <see cref="SceneRouter"/> opens it with the async load
    /// and closes it once the scene is up; progress comes from <c>AsyncOperation.progress</c>.
    ///
    /// **Visuals come from a prefab, NOT the scene:** `Resources/UI/LoadingOverlayScreen` (desktop)
    /// or `…World` (VR), chosen by <see cref="Install"/>. Same pattern as
    /// <see cref="ConnectionOverlay"/>: ⚠️ never placed in a scene (that would be a step to forget
    /// per arena), loaded via `Resources.Load` as a `DontDestroyOnLoad` singleton, and
    /// **`AppSingletons` decides when it spawns**. This class only writes data and drives
    /// visibility.
    ///
    /// **Preloaded at startup**, not on first show: `Resources.Load` during the transition would
    /// hitch in the worst frame of the load.
    ///
    /// ⚠️ **VR safety rule** (as in `ConnectionOverlay` — the player walks 1:1 physically): the
    /// world-space variant has NO full-screen scrim, only a semi-transparent card. Darkening the
    /// view is dangerous in free-roam.
    /// </summary>
    public class LoadingOverlay : MonoBehaviour
    {
        // --------------------------------------------------------------- settings

        /// <summary>Bar fill per second — it should flow, not jump.</summary>
        private const float FillSpeed = 2.5f;

        /// <summary>Pulse period (s) — accent strip alpha 0.55 ↔ 1.0.</summary>
        private const float PulsePeriod = 1.2f;

        // ------------------------------------------------------------------- state

        private static LoadingOverlay _instance;

        /// <summary><c>Resources</c> paths (no extension) — VR world-space and desktop screen-space
        /// are two separate prefabs.</summary>
        public const string WorldResourcePath = "UI/LoadingOverlayWorld";

        public const string ScreenResourcePath = "UI/LoadingOverlayScreen";

        // ⚠️ [SerializeField] fields: the visuals come FROM THE PREFAB. Title and hint texts are
        // fixed there too (never touched by code), hence no fields for them.

        [Tooltip("Bu prefab VR (world-space) varyantı mı? Screen-space varyantta KAPALI olmalı.")]
        [SerializeField] private bool _worldSpace;

        [Header("Kök")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Yalnız world-space varyantta dolu — kartı tembel takiple kameranın önüne taşır.")]
        [SerializeField] private HudFollow _hudFollow;

        [Header("Kart")]
        [SerializeField] private Image _accentStrip;
        [Tooltip("Yüklenen sahnenin adı.")]
        [SerializeField] private TextMeshProUGUI _sceneText;
        [Tooltip("Yüzde göstergesi.")]
        [SerializeField] private TextMeshProUGUI _progressText;
        [Tooltip("İlerleme barının DOLGUSU (UiKit.Bar deseni: anchorMax.x ile sürülür).")]
        [SerializeField] private Image _progressFill;

        /// <summary>Opened via <see cref="Show"/> (intent); actual drawing is <see cref="_visible"/>
        /// — in VR the card waits for a camera.</summary>
        private bool _shown;

        private bool _visible;

        /// <summary>World-space mode: the camera the card stands in front of; a change re-seats it.</summary>
        private Camera _followedCamera;

        /// <summary>Target progress (0..1) — written by <see cref="SetProgress"/>.</summary>
        private float _target;

        /// <summary>Drawn progress; approaches the target smoothly.</summary>
        private float _displayed;

        /// <summary>Percentage on screen — TMP is only touched when it changes (no garbage).</summary>
        private int _shownPercent = -1;

        // --------------------------------------------------------------- bootstrap

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — the "is it needed in this
        /// session" decision belongs to <see cref="AppSingletons"/> (rationale is there).</summary>
        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            // World-space card on Quest / active XR device, screen-space on desktop.
            bool worldSpace = UnityEngine.XR.XRSettings.isDeviceActive ||
                              Application.platform == RuntimePlatform.Android;
            string path = worldSpace ? WorldResourcePath : ScreenResourcePath;

            var prefab = Resources.Load<LoadingOverlay>(path);
            if (prefab == null)
            {
                Debug.LogError($"[LoadingOverlay] '{path}' prefabı bulunamadı — yükleme ekranı " +
                               "çizilemeyecek. (Prefab Resources/UI altından ÇIKARILMAMALIDIR.)");
                return;
            }

            LoadingOverlay overlay = Instantiate(prefab);
            overlay.name = "[LoadingOverlay]";
            DontDestroyOnLoad(overlay.gameObject);
            _instance = overlay;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // `_visible = true` on purpose: ApplyVisible skips same-value writes, and a prefab saved
            // with an enabled canvas must still be turned off here.
            _visible = true;
            ApplyVisible(false);
        }

        // ------------------------------------------------------------ public surface

        /// <summary>
        /// Opens the loading screen and resets progress. A missing prefab is a silent no-op — the
        /// absence of a loading screen must NOT block the scene transition.
        /// </summary>
        public static void Show(string sceneName)
        {
            // Spawn even when called before AppSingletons ran. ⚠️ This does not pierce the session
            // gate: only scene routing asks for the loading screen, and without that singleton
            // nobody calls here.
            Install();
            if (_instance == null)
            {
                return;
            }

            _instance.ShowInstance(sceneName);
        }

        /// <summary>Progress (0..1); bar and percentage follow it.</summary>
        public static void SetProgress(float normalized)
        {
            if (_instance == null)
            {
                return;
            }

            _instance._target = Mathf.Clamp01(normalized);
        }

        public static void Hide()
        {
            if (_instance == null)
            {
                return;
            }

            _instance._shown = false;
            _instance.ApplyVisible(false);
        }

        /// <summary>Is the screen visible (diagnostics/guards).</summary>
        public static bool IsVisible => _instance != null && _instance._visible;

        // -------------------------------------------------------------------- loop

        private void ShowInstance(string sceneName)
        {
            _target = 0f;
            _displayed = 0f;
            _shownPercent = -1;
            _shown = true;

            if (_sceneText != null)
            {
                _sceneText.text = sceneName ?? "";
            }

            // Re-seat in front of whichever camera is found (TrackCamera snaps it).
            _followedCamera = null;

            ApplyProgress();
            Refresh();
        }

        private void Update()
        {
            if (_instance != this)
            {
                return;
            }

            Refresh();

            if (!_visible)
            {
                return;
            }

            // Loading frames are irregular, so the bar eases forward; going backwards (a new load)
            // is instant.
            _displayed = _target < _displayed
                ? _target
                : Mathf.MoveTowards(_displayed, _target, Time.unscaledDeltaTime * FillSpeed);

            ApplyProgress();
            Pulse();
        }

        /// <summary>
        /// Re-decides visibility every frame: <see cref="_shown"/> on desktop, **plus the camera in
        /// VR**.
        /// <para>
        /// ⚠️ The world-space card follows <c>Camera.main</c> via <see cref="HudFollow"/>; drawn
        /// without a camera it hangs at the world origin where the player never sees it. A scene
        /// transition is exactly when the camera dies and respawns, so this is the rule, not an
        /// exception.
        /// </para>
        /// <para>
        /// Hence: no drawing until a camera exists, and <see cref="HudFollow"/> is restarted <b>on a
        /// camera change</b> — otherwise the panel glides from the old camera's position and sits in
        /// the wrong place for half the transition. (`ConnectionOverlay` uses the same gate, but
        /// there a lost camera is rare.)
        /// </para>
        /// </summary>
        private void Refresh()
        {
            ApplyVisible(_shown && (!_worldSpace || TrackCamera()));
        }

        /// <summary>Tracks the camera and snaps the card in front of a newly found one.</summary>
        private bool TrackCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                _followedCamera = null;
                return false;
            }

            if (_followedCamera != camera)
            {
                _followedCamera = camera;

                // OnEnable resets HudFollow._initialized → snap instead of glide. (HudFollow runs in
                // LateUpdate, so it is seated before this frame draws.)
                if (_hudFollow != null)
                {
                    _hudFollow.enabled = false;
                    _hudFollow.enabled = true;
                }
            }

            return true;
        }

        private void ApplyProgress()
        {
            UiKit.SetBarFill(_progressFill, _displayed);

            int percent = Mathf.Clamp(Mathf.RoundToInt(_displayed * 100f), 0, 100);
            if (_progressText != null && _shownPercent != percent)
            {
                _shownPercent = percent;
                _progressText.text = $"%{percent}";
            }
        }

        /// <summary>The only animation: a soft "work in progress" pulse.</summary>
        private void Pulse()
        {
            if (_accentStrip == null)
            {
                return;
            }

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / PulsePeriod));
            Color c = _accentStrip.color;
            c.a = Mathf.Lerp(0.55f, 1f, wave);
            _accentStrip.color = c;
        }

        private void ApplyVisible(bool visible)
        {
            if (_group == null || _canvas == null || _visible == visible)
            {
                return; // same value every frame would dirty the canvas
            }

            _visible = visible;
            _canvas.enabled = visible; // no draw cost at all while hidden
            _group.alpha = visible ? 1f : 0f;

            // Nothing here is clickable; on desktop it only blocks raycasts so the admin UI behind
            // it is not hit by accident.
            _group.blocksRaycasts = visible && !_worldSpace;
            _group.interactable = false;
        }
    }
}
