using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VortexArena.Core.Arena;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Styled error screen shown **in game** when the server is unreachable. NEVER starts the
    /// server; only reports state (address, elapsed time, attempt count, last error) and offers a
    /// manual "reconnect" on desktop. Same information hierarchy on desktop admin (screen-space +
    /// scrim + button) and on Quest (world-space card, lazy-follow, no button).
    ///
    /// **Visuals come from the prefab, NOT the scene:** two variants —
    /// `Resources/UI/ConnectionOverlayScreen` (desktop) and `…World` (VR); <see cref="Install"/>
    /// picks one. The prefab is NOT placed in scenes: that would be a step forgotten on every new
    /// arena (arena scenes are self-contained boxes). Hence the `ArenaClient` pattern —
    /// `Resources.Load` + `DontDestroyOnLoad` singleton; **`AppSingletons` decides which sessions
    /// spawn it** (never in serverless sessions such as calibration).
    /// This class only **writes data and drives visibility**; layout/color/size live in the prefab.
    ///
    /// **Why a grace period:** for momentary drops (WS reconnect backoff 1→2→5 s) a flashing
    /// screen is both ugly and distracting mid-match. Shown after being disconnected for
    /// <see cref="GraceSeconds"/>; hidden instantly on `Connected` with the counter reset. Same
    /// logic covers both startup and mid-match drops.
    ///
    /// **Two player-facing states (§8):** until `RECONNECT_GRACE` expires "disconnected — N s
    /// until removal" (stats preserved), afterwards "removed from game". ⚠️ Only the PRESENTATION
    /// changes: `ArenaClient` keeps retrying in both (infinite backoff), rejoins by itself when the
    /// network returns and takes its old row if the match is still running. The countdown runs from
    /// the client's own drop instant — it cannot arrive from the server while offline, and the
    /// constant is identical on both sides so drift stays within seconds. With no known address
    /// (launched without the launcher) "server not found" stays instead: that is a configuration
    /// problem, not a connection one.
    ///
    /// **VR safety rule:** the player walks 1:1 in physical space. (a) NO fullscreen scrim — only a
    /// translucent card; dimming their view is dangerous. (b) While `ArenaBoundary` reports
    /// out-of-bounds the overlay hides COMPLETELY: the out-of-bounds dim + warning must always
    /// dominate — a connection error screen must never make a player walk into a wall.
    ///
    /// ⚠️ **The card is lazy-following and stands right in front of the head, so it COVERS any world
    /// UI opened at that moment.** Whoever opens such a panel must file a
    /// <see cref="SetSuppressed"/> request (today the lobby IP numpad): without it the panel that
    /// solves the very problem this screen reports is unreachable behind it.
    /// </summary>
    public class ConnectionOverlay : MonoBehaviour
    {
        // -------------------------------------------------------------- settings

        /// <summary>Screen appears after this long without a connection (s).</summary>
        private const float GraceSeconds = 3f;

        /// <summary>Text refresh interval (s) — ~4 Hz, avoids needless TMP redraws.</summary>
        private const float RefreshInterval = 0.25f;

        /// <summary>Pulse period (s) — accent strip + badge alpha 0.55 ↔ 1.0.</summary>
        private const float PulsePeriod = 1.2f;

        /// <summary>`LastError` is shown up to this many characters.</summary>
        private const int MaxErrorChars = 120;

        private const float CardWidth = 900f;
        private const float CardHeightVr = 520f;
        private const float CardHeightDesktop = 600f;

        /// <summary>World-space mode: 900 px → ~0.9 m.</summary>
        private const float WorldScale = 0.001f;

        // Palette + procedural element factories live in `UiKit` (same visual language as admin HUD).
        private static readonly Color ColorScrim = UiKit.Scrim;
        private static readonly Color ColorCard = UiKit.Card;
        private static readonly Color ColorCardWorld = UiKit.CardTranslucent; // alpha ≈ 0.88 (VR)
        private static readonly Color ColorBorder = UiKit.Border;
        private static readonly Color ColorAccent = UiKit.Accent;
        private static readonly Color ColorTitle = UiKit.Title;
        private static readonly Color ColorMuted = UiKit.Muted;
        private static readonly Color ColorFaint = UiKit.Faint;
        private static readonly Color ColorOnAccent = UiKit.OnAccent;

        /// <summary>Card corner radius (px) — this screen's own value, larger than the panel default.</summary>
        private const float CardRadius = 20f;

        // ----------------------------------------------------------------- state

        private static ConnectionOverlay _instance;

        /// <summary>Requesters currently hiding the screen — see <see cref="SetSuppressed"/>.</summary>
        private static readonly List<UnityEngine.Object> SuppressRequesters = new List<UnityEngine.Object>();

        /// <summary>Prefab <c>Resources</c> paths (no extension) — VR world-space and desktop
        /// screen-space are two separate prefabs; <see cref="Install"/> picks one.</summary>
        public const string WorldResourcePath = "UI/ConnectionOverlayWorld";

        public const string ScreenResourcePath = "UI/ConnectionOverlayScreen";

        // ⚠️ Fields are [SerializeField] — visuals come FROM THE PREFAB. This class only writes
        // data and drives visibility; layout/color/size are edited in the prefab.

        [Tooltip("Bu prefab VR (world-space) varyantı mı? Screen-space varyantta KAPALI olmalı.")]
        [SerializeField] private bool _worldSpace;

        [Header("Kök")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private CanvasGroup _group;
        [Tooltip("Yalnız world-space varyantta dolu — kartı tembel takiple kameranın önüne taşır.")]
        [SerializeField] private HudFollow _hudFollow;

        [Header("Kart")]
        [SerializeField] private Image _accentStrip;
        [SerializeField] private Image _badge;
        [SerializeField] private TextMeshProUGUI _titleText;
        [SerializeField] private TextMeshProUGUI _addressText;
        [SerializeField] private TextMeshProUGUI _metaText;
        [SerializeField] private TextMeshProUGUI _hintText;
        [SerializeField] private TextMeshProUGUI _errorText;

        [Tooltip("Yalnız masaüstü (screen-space) varyantta dolu — VR'da yeniden bağlanma düğmesi yok.")]
        [SerializeField] private Button _reconnectButton;
        [SerializeField] private TextMeshProUGUI _reconnectLabel;

        /// <summary>Instant we became disconnected (unscaled); -1 while connected.</summary>
        private float _disconnectedSince = -1f;

        private float _nextRefresh;
        private bool _forceRefresh = true;
        private bool _visible;

        /// <summary>`ArenaBoundary` cache — avoids a scene scan every frame.</summary>
        private ArenaBoundary _boundary;
        private bool _boundarySearched;

        // Values currently on screen (TMP untouched unless changed → no garbage).
        private bool _shownKnown;
        private bool _shownExpelled;
        private string _shownIp = null;
        private int _shownPort = -1;
        private int _shownSeconds = -1;
        private int _shownAttempts = -1;
        private string _shownError = null;

        // ------------------------------------------------------------- bootstrap

        /// <summary>Installs the singleton. ⚠️ <b>Unconditional</b> — "is it needed this session"
        /// is <see cref="AppSingletons"/>'s call (rationale lives there).</summary>
        internal static void Install()
        {
            if (_instance != null)
            {
                return;
            }

            // World-space card on Quest (or any active XR device), screen-space on desktop.
            bool worldSpace = UnityEngine.XR.XRSettings.isDeviceActive ||
                              Application.platform == RuntimePlatform.Android;
            string path = worldSpace ? WorldResourcePath : ScreenResourcePath;

            var prefab = Resources.Load<ConnectionOverlay>(path);
            if (prefab == null)
            {
                Debug.LogError($"[ConnectionOverlay] '{path}' prefabı bulunamadı — bağlantı hata " +
                               "ekranı çizilemeyecek.");
                return;
            }

            ConnectionOverlay overlay = Instantiate(prefab);
            overlay.name = "[ConnectionOverlay]";
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

            if (_reconnectButton != null)
            {
                // No persistent onClick in the prefab: the button is interactable only when the
                // address is known (RefreshTexts) and the command goes through AdminCommands.
                _reconnectButton.onClick.RemoveAllListeners();
                _reconnectButton.onClick.AddListener(HandleReconnectPressed);
            }
        }

        /// <summary>
        /// Hides the screen for one requester (idempotent; same pattern as
        /// <c>ControllerModelHider.SetRayVisualsRequested</c> — per-requester, so one panel closing
        /// does not un-hide it for another still open).
        /// <para>⚠️ <b>Whoever opens a world UI in front of the player must call this</b>: the card
        /// lazy-follows the head and would sit on top of that UI. The request must be dropped when
        /// the panel closes AND when the requester is disabled, or the screen stays gone for good.</para>
        /// <para>Retrying is NOT affected — this only silences the presentation; <c>ArenaClient</c>
        /// keeps its backoff loop running.</para>
        /// </summary>
        public static void SetSuppressed(UnityEngine.Object requester, bool suppressed)
        {
            if (requester == null)
            {
                return;
            }

            for (int i = SuppressRequesters.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object existing = SuppressRequesters[i];
                if (existing == null || existing == requester)
                {
                    SuppressRequesters.RemoveAt(i);
                }
            }

            if (suppressed)
            {
                SuppressRequesters.Add(requester);
            }
        }

        /// <summary>Is anyone hiding the screen — destroyed requesters are pruned and do not count.</summary>
        private static bool IsSuppressed()
        {
            for (int i = SuppressRequesters.Count - 1; i >= 0; i--)
            {
                if (SuppressRequesters[i] == null)
                {
                    SuppressRequesters.RemoveAt(i);
                }
            }

            return SuppressRequesters.Count > 0;
        }

        private void OnEnable()
        {
            NetEvents.OnConnectionStateChanged += HandleConnectionStateChanged;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        }

        private void OnDisable()
        {
            NetEvents.OnConnectionStateChanged -= HandleConnectionStateChanged;
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

        // ------------------------------------------------------------------ loop

        private void Update()
        {
            if (_instance != this)
            {
                return;
            }

            // The event may have been missed: this singleton can be born BEFORE `ArenaClient` and
            // the first state change may fire before we subscribe → poll the state too.
            ArenaClient client = ArenaClient.Instance;
            TrackState(client != null ? client.State : ArenaConnectionState.Disconnected);

            // A requester is deliberately hiding the card (lobby IP numpad). The grace clock is
            // PAUSED meanwhile: otherwise the card would pop back the instant the panel closes,
            // right on top of the attempt the player just started.
            if (IsSuppressed())
            {
                if (_disconnectedSince >= 0f)
                {
                    _disconnectedSince = Time.unscaledTime;
                }

                SetVisible(false);
                return;
            }

            if (!ShouldShow())
            {
                SetVisible(false);
                return;
            }

            // SAFETY: while out of bounds, `ArenaBoundary`'s dim + warning stays dominant.
            if (IsOutOfBounds())
            {
                SetVisible(false);
                return;
            }

            // In VR HudFollow places the card in front of the camera; with no camera yet (early
            // camera-less scenes like Boot) defer showing — the panel must not hang at the origin.
            if (_group == null || (_worldSpace && Camera.main == null))
            {
                return;
            }

            SetVisible(true);
            Pulse();

            if (_forceRefresh || Time.unscaledTime >= _nextRefresh)
            {
                _forceRefresh = false;
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                RefreshTexts();
            }
        }

        private void HandleConnectionStateChanged(ArenaConnectionState state)
        {
            TrackState(state);
            _forceRefresh = true;
        }

        /// <summary>
        /// On scene change: drop the `ArenaBoundary` cache (the overlay outlives scenes and must
        /// re-find it) and restart `HudFollow` so the panel snaps to the new scene's camera instead
        /// of drifting from the old position.
        /// </summary>
        private void HandleActiveSceneChanged(Scene previous, Scene current)
        {
            _boundary = null;
            _boundarySearched = false;

            if (_hudFollow != null)
            {
                _hudFollow.enabled = false;
                _hudFollow.enabled = true; // OnEnable → resets _initialized
            }
        }

        /// <summary>Counter runs while disconnected; resets on `Connected`.</summary>
        private void TrackState(ArenaConnectionState state)
        {
            if (state == ArenaConnectionState.Connected)
            {
                _disconnectedSince = -1f;
                return;
            }

            if (_disconnectedSince < 0f)
            {
                _disconnectedSince = Time.unscaledTime;
            }
        }

        private bool ShouldShow()
        {
            return _disconnectedSince >= 0f &&
                   Time.unscaledTime - _disconnectedSince >= GraceSeconds;
        }

        private bool IsOutOfBounds()
        {
            if (!_boundarySearched)
            {
                _boundary = FindFirstObjectByType<ArenaBoundary>();
                _boundarySearched = true; // no re-search until the next scene change
            }

            return _boundary != null && _boundary.IsOutOfBounds;
        }

        private void SetVisible(bool visible)
        {
            if (_group == null || _canvas == null || _visible == visible)
            {
                return; // don't dirty the canvas by rewriting the same value every frame
            }

            _visible = visible;
            _canvas.enabled = visible; // zero draw cost while hidden
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible && !_worldSpace;
            _group.interactable = visible && !_worldSpace;

            if (visible)
            {
                _forceRefresh = true; // texts must be fresh when becoming visible
                EnsureClickableOnDesktop();
            }
        }

        /// <summary>Only animation: a soft pulse conveying "still retrying".</summary>
        private void Pulse()
        {
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / PulsePeriod));
            float alpha = Mathf.Lerp(0.55f, 1f, wave);

            if (_accentStrip != null)
            {
                Color c = _accentStrip.color;
                c.a = alpha;
                _accentStrip.color = c;
            }

            if (_badge != null)
            {
                Color c = _badge.color;
                c.a = alpha;
                _badge.color = c;
            }
        }

        // ------------------------------------------------------------------ text

        /// <summary>
        /// Address source: the one actually being dialed (`ArenaClient`) first, else the
        /// launcher-provided `AppSession` address. Neither → "no address" state.
        /// </summary>
        private static bool ResolveEndpoint(out string ip, out int port)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client != null && !string.IsNullOrEmpty(client.ServerIp) && client.ServerPort > 0)
            {
                ip = client.ServerIp;
                port = client.ServerPort;
                return true;
            }

            if (AppSession.HasServerEndpoint)
            {
                ip = AppSession.ServerIp;
                port = AppSession.ServerPort;
                return true;
            }

            ip = "";
            port = 0;
            return false;
        }

        private void RefreshTexts()
        {
            ArenaClient client = ArenaClient.Instance;
            bool known = ResolveEndpoint(out string ip, out int port);
            int seconds = _disconnectedSince >= 0f
                ? Mathf.Max(0, Mathf.FloorToInt(Time.unscaledTime - _disconnectedSince))
                : 0;
            int attempts = client != null ? client.ConnectAttempts : 0;
            string error = client != null ? client.LastError : "";
            int graceLeft = Mathf.Max(0, Mathf.CeilToInt(ArenaProtocol.RECONNECT_GRACE - seconds));
            bool expelled = known && IsPlayerRole && graceLeft == 0;

            // Title / address / hint: rewritten only when address state or the removal threshold changes.
            if (_shownIp == null || _shownKnown != known || _shownPort != port ||
                _shownExpelled != expelled ||
                !string.Equals(_shownIp, ip, StringComparison.Ordinal))
            {
                _shownKnown = known;
                _shownExpelled = expelled;
                _shownIp = ip;
                _shownPort = port;

                _titleText.text = BuildTitle(known, expelled);
                _addressText.text = known ? $"{ip}:{port}" : "adres yok";
                _addressText.color = known ? ColorAccent : ColorFaint;
                _hintText.text = BuildHint(known, expelled, graceLeft);

                ApplyButtonState(known);
                _shownAttempts = -1; // refresh the meta line too (attempt counter visibility changed)
            }

            // Meta: elapsed time (second resolution) + attempt counter (only with a known address).
            if (_shownSeconds != seconds || _shownAttempts != attempts)
            {
                _shownSeconds = seconds;
                _shownAttempts = attempts;

                _metaText.text = known && attempts > 0
                    ? $"{seconds} sn · {attempts}. deneme"
                    : $"{seconds} sn";

                // Time left until removal changes every second, and the title block only runs on
                // state change → refresh the countdown HERE (no extra counter field needed;
                // `_shownSeconds` is already at second resolution).
                if (known && IsPlayerRole && !expelled)
                {
                    _hintText.text = BuildHint(true, false, graceLeft);
                }
            }

            // Last error: small, faint, at the bottom.
            if (!string.Equals(_shownError, error, StringComparison.Ordinal))
            {
                _shownError = error;

                if (string.IsNullOrEmpty(error))
                {
                    _errorText.text = "";
                }
                else
                {
                    // Single-char symbols like "…" may be missing from TMP's default font
                    // (missing glyph renders □) → plain three dots.
                    string clipped = error.Length > MaxErrorChars
                        ? error.Substring(0, MaxErrorChars) + "..."
                        : error;
                    _errorText.text = $"Son hata: {clipped}";
                }
            }
        }

        /// <summary>
        /// Record lifetime belongs to PLAYERS: an admin record is dropped the moment it disconnects
        /// (§2), so there is no "N s until removal" countdown for admins.
        /// </summary>
        private static bool IsPlayerRole => AppSession.Role != AppSession.RoleAdmin;

        /// <summary>Unknown address = a CONFIGURATION problem, not a connection one — separate branch.</summary>
        private static string BuildTitle(bool known, bool expelled)
        {
            if (!known)
            {
                return "SUNUCU BULUNAMADI";
            }

            if (!IsPlayerRole)
            {
                return "SUNUCUYA BAĞLANILAMIYOR";
            }

            return expelled ? "OYUNDAN ÇIKARILDINIZ" : "BAĞLANTI KOPTU";
        }

        /// <summary>
        /// Two states (§8): "we're waiting for you" before <see cref="ArenaProtocol.RECONNECT_GRACE"/>
        /// expires, "you were removed" after. ⚠️ Retrying continues in both — the timer says when
        /// the server drops the record, not when the headset gives up.
        /// <para>Counted from the client's own drop instant (it cannot come from the server while
        /// offline). ⚠️ The two clocks don't always start together: a clean socket close moves the
        /// server to <c>reconnecting</c> at the same moment (no drift), but a silently dead Wi-Fi is
        /// only noticed after <c>HEARTBEAT_TIMEOUT</c>, so the client's countdown ends EARLIER.
        /// Harmless in that direction: the screen may say "removed" while the server still holds the
        /// record, never the reverse — the player is never promised more time than they have.</para>
        /// </summary>
        private static string BuildHint(bool known, bool expelled, int graceLeft)
        {
            if (AppSession.Role == AppSession.RoleAdmin)
            {
                return known
                    ? "Sunucu uygulamasını başlatın, sonra Yeniden Bağlan'a basın."
                    : $"Bu uygulama launcher'dan başlatılmalıdır ({AppBoot.ArgServerIp} <ip>).";
            }

            if (!known)
            {
                return "Sunucunun açık olduğundan emin olun.\n" +
                       "Adresi elle girmek için sağ kumandada joystick'e 1 sn basılı tutun.";
            }

            return expelled
                ? "Yeniden bağlanılıyor — ağ dönünce otomatik katılacaksın."
                : $"Yeniden bağlanılıyor · oyundan çıkarılmana {graceLeft} sn\n" +
                  "Maç istatistiklerin korunuyor.";
        }

        private void ApplyButtonState(bool addressKnown)
        {
            if (_reconnectButton == null)
            {
                return;
            }

            // With no address at all, pressing changes nothing → don't offer false hope.
            _reconnectButton.interactable = addressKnown;

            if (_reconnectLabel != null)
            {
                _reconnectLabel.color = addressKnown ? ColorOnAccent : ColorFaint;
            }
        }

        /// <summary>
        /// Manual reconnect. Without this button there is NO way back: `ArenaClient.Disconnect()`
        /// sets `_userDisconnect = true` and stops the auto-retry loop — the only way back is an
        /// explicit `Connect(...)`.
        /// </summary>
        private void HandleReconnectPressed()
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !ResolveEndpoint(out string ip, out int port))
            {
                return;
            }

            client.Connect(ip, port, AppSession.Role);
            _forceRefresh = true;
        }

        // -------------------------------------------------------------- UI setup

        /// <summary>
        /// EventSystem guarantee so the reconnect button stays clickable (desktop only). Admin
        /// leaves `Lobby` for arena scenes and <b>arena scenes have NO EventSystem</b> — without
        /// this the button silently dies there. <see cref="UiKit.EnsureEventSystem"/> installs a
        /// persistent one (module is `InputSystemUIInputModule` since we build with the Input
        /// System package; `StandaloneInputModule` touches `UnityEngine.Input` and throws).
        /// </summary>
        private void EnsureClickableOnDesktop()
        {
            if (_worldSpace)
            {
                return; // no button in VR mode
            }

            UiKit.EnsureEventSystem();
        }

        // Procedural element factories, layout helpers and the rounded-corner sprite live in
        // `UiKit` (one visual language and one implementation shared with the admin HUD).
    }
}
