using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Controller of the shell <c>Lobby</c> scene. <b>Its only job is connecting:</b> status text
    /// and the **hidden** IP panel (manual address entry via numpad).
    ///
    /// <para>
    /// <b>This scene is a waiting room, NOT a play area.</b> The player only waits to connect here;
    /// on connect they move to the server's <b>open scene</b> (<c>SceneRouter</c>, §10.7) which is
    /// the real lobby. Hence NO roster, ready button or team picker here: teams are assigned only by
    /// admin (§5.2) and <c>set_ready</c> is a loading gate sent by <c>SceneRouter</c>. Adding game
    /// UI here would create two lobbies with no clear authority on site.
    /// </para>
    /// <para>
    /// <b>The normal flow asks the player nothing:</b> the address is resolved by a priority chain
    /// (command line <c>--server-ip</c> &gt; PlayerPrefs &gt; beacon &gt;
    /// StreamingAssets/arena.json) and connected <b>automatically</b>. The IP panel starts CLOSED.
    /// The command-line address at the head of the chain is written by <see cref="AppBoot"/> (in the
    /// editor the <c>Tools &gt; VortexArena &gt; Development &gt; Dev</c> target arrives this way) —
    /// an explicit address wins.
    /// </para>
    /// <para>
    /// <b>Recovery path:</b> on networks that block/isolate the beacon, <b>holding the right
    /// controller's joystick for 1 second</b> opens the IP panel for manual entry (the entered
    /// address is persisted to <c>PlayerPrefs</c> and overrides the beacon). The same gesture closes
    /// it; the controller vibrates when it fires. No conflict with the calibration gesture (double
    /// B while holding A) — no shared button.
    /// </para>
    /// All scene links are [SerializeField] and may be null; button onClicks bind to public methods.
    /// </summary>
    public class LobbyController : MonoBehaviour
    {
        private const int MaxIpTextLength = 21; // "255.255.255.255:65535"

        /// <summary>Holding the joystick this long uninterrupted toggles the IP panel.</summary>
        private const float IpPanelHoldDuration = 1f;

        /// <summary>Recovery hint appears if no address is found within this long.</summary>
        private const float DiscoveryHintDelay = 8f;

        /// <summary>
        /// IP panel deviating this far (m) from the canvas plane logs an error — see
        /// <see cref="WarnIfPanelOffCanvasPlane"/>. Deviation must be zero; the tolerance only
        /// absorbs float rounding.
        /// </summary>
        private const float PanelPlaneTolerance = 0.01f;

        private static readonly OVRInput.Controller Hand = OVRInput.Controller.RTouch;

        [Header("Durum")]
        [SerializeField] private TMP_Text statusText;

        [Header("IP paneli (gizli — sağ kumandada joystick 1 sn basılı tutularak açılır)")]
        [SerializeField] private GameObject ipPanel;
        [SerializeField] private TMP_Text ipText;
        [SerializeField] private Button connectButton;
        [SerializeField] private Button disconnectButton;

        private string _ipBuffer = "";
        private bool _manualEntry; // manual entry (or saved IP) overrides the beacon
        private bool _beaconSubscribed;

        private bool _ipPanelVisible;
        private float _joystickHoldTimer;
        private bool _joystickHoldFired; // no second trigger while still held
        private float _discoveryTimer;
        private bool _autoConnectDone;
        private bool _hintShown;
        private bool _planeChecked;

        private void Awake()
        {
            if (!AppSession.RoleResolved)
            {
                // Lobby played without Boot (editor test) — this scene is the player shell.
                AppSession.Role = AppSession.RolePlayer;
                AppSession.RoleResolved = true;
            }
        }

        private void OnEnable()
        {
            NetEvents.OnConnectionStateChanged += HandleConnectionStateChanged;
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnKicked += HandleKicked;
            TrySubscribeBeacon();
        }

        private void OnDisable()
        {
            NetEvents.OnConnectionStateChanged -= HandleConnectionStateChanged;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnKicked -= HandleKicked;

            if (_beaconSubscribed && ServerDiscovery.Instance != null)
            {
                ServerDiscovery.Instance.OnBeacon -= HandleBeacon;
            }

            _beaconSubscribed = false;

            // The ray is asked for only while the panel is open — leaving the scene with the panel
            // open would carry it into the arena. Same for the error screen: a request left behind
            // would keep it hidden for the rest of the session.
            ControllerModelHider.SetRayVisualsRequested(this, false);
            ConnectionOverlay.SetSuppressed(this, false);
        }

        private void Start()
        {
            // Persistent singletons may bootstrap after scene objects — retry here.
            TrySubscribeBeacon();

            SetIpPanelVisible(false); // players are never asked for an address; recovery is the hold gesture

            if (AppSession.HasServerEndpoint)
            {
                // Address given explicitly on the command line (or dev window): head of the chain.
                // Flagged as _manualEntry so the beacon does NOT override it.
                _ipBuffer = FormatEndpoint(AppSession.ServerIp, AppSession.ServerPort);
                _manualEntry = true;
            }
            else if (ServerDiscovery.TryGetSavedEndpoint(out string ip, out int port))
            {
                _ipBuffer = FormatEndpoint(ip, port);
                _manualEntry = true;
            }
            else if (ServerDiscovery.Instance != null &&
                     ServerDiscovery.Instance.TryGetPreferredEndpoint(out ip, out port))
            {
                _ipBuffer = FormatEndpoint(ip, port);
            }

            RefreshIpText();
            TryAutoConnect(); // connect right away if an address exists, else wait for a beacon
            RefreshStatus();
        }

        private void Update()
        {
            DetectIpPanelCombo();

            // `ArenaClient`/`ServerDiscovery` singletons spawn on AfterSceneLoad and may not exist
            // in Start(). With a saved address (PlayerPrefs) and no beacon at all, the single
            // attempt would be missed — catch it on the first frame they are ready.
            TryAutoConnect();
            TrySubscribeBeacon();

            // Still no address → surface the recovery path (beacon listening continues).
            if (_autoConnectDone || _hintShown || _ipPanelVisible)
            {
                return;
            }

            _discoveryTimer += Time.unscaledDeltaTime;
            if (_discoveryTimer >= DiscoveryHintDelay)
            {
                _hintShown = true;
                SetStatus("Sunucu bulunamadı. Adresi elle girmek için sağ kumandada joystick'e 1 sn basılı tut.");
            }
        }

        /// <summary>
        /// Right controller joystick held 1 s → toggle the IP panel (hidden recovery path).
        /// Releasing resets the timer; one vibration per trigger.
        /// </summary>
        private void DetectIpPanelCombo()
        {
            if (!OVRInput.Get(OVRInput.Button.PrimaryThumbstick, Hand))
            {
                _joystickHoldTimer = 0f;
                _joystickHoldFired = false;
                return;
            }

            if (_joystickHoldFired)
            {
                return; // still held — no re-trigger before release
            }

            _joystickHoldTimer += Time.unscaledDeltaTime;
            if (_joystickHoldTimer < IpPanelHoldDuration)
            {
                return;
            }

            _joystickHoldFired = true;
            SetIpPanelVisible(!_ipPanelVisible);
            OVRInput.SetControllerVibration(0.5f, 0.3f, Hand);
        }

        private void SetIpPanelVisible(bool visible)
        {
            _ipPanelVisible = visible;

            if (ipPanel != null)
            {
                ipPanel.SetActive(visible);
            }

            // The numpad is pointed at with the ISDK ray; that ray is hidden by default
            // (ControllerModelHider) — without asking for it the player aims blind.
            ControllerModelHider.SetRayVisualsRequested(this, visible);

            // ⚠️ `ConnectionOverlay`'s VR card lazy-follows the head and stands right in front of
            // it — it covers this very numpad, i.e. the way OUT of the error it reports. Hidden
            // while the panel is open; retrying keeps running behind it.
            ConnectionOverlay.SetSuppressed(this, visible);

            if (visible)
            {
                _hintShown = true; // don't rewrite the hint while the panel is open
                WarnIfPanelOffCanvasPlane();
                RefreshIpText();
            }

            // Opening drops the "server not found" hint, closing puts the live state back.
            RefreshStatus();
        }

        /// <summary>
        /// Checks the panel sits ON the canvas plane; logs once if not.
        /// <para>
        /// ⚠️ <b>Why a dedicated check:</b> on a world-space canvas a child off the plane <b>keeps
        /// rendering but becomes unclickable</b> — neither the ISDK ray nor the mouse reaches it.
        /// Graphic raycasting uses a camera built on the canvas plane, so anything in front of or
        /// behind it falls behind that camera and <c>RectangleContainsScreenPoint</c> returns false.
        /// Without this console line it looks like "the button is broken" and costs hours.
        /// </para>
        /// <para>
        /// Easy to hit: with canvas scale 0.0012, nudging the panel's z by 1 m in the scene view is
        /// a ~830 unit deviation in local space.
        /// </para>
        /// The check <b>only reads</b> — it does not fix the position, which would make the scene
        /// value and the code value two sources of truth.
        /// </summary>
        private void WarnIfPanelOffCanvasPlane()
        {
            if (_planeChecked || ipPanel == null)
            {
                return;
            }

            _planeChecked = true;

            Canvas canvas = ipPanel.GetComponentInParent<Canvas>(true);
            if (canvas == null)
            {
                return;
            }

            Transform plane = canvas.rootCanvas.transform;
            float offset = Vector3.Dot(ipPanel.transform.position - plane.position, plane.forward);
            if (Mathf.Abs(offset) <= PanelPlaneTolerance)
            {
                return;
            }

            Debug.LogError($"[LobbyController] '{ipPanel.name}' canvas düzleminden {offset:0.###} m " +
                           "sapmış — panel çizilir ama hiçbir tuşuna basılamaz. RectTransform'un " +
                           "Pos Z'sini 0 yap.", ipPanel);
        }

        /// <summary>
        /// Auto-connects once to a known address. Harmless to call repeatedly: retries after a drop
        /// are handled by <c>ArenaClient</c>'s backoff loop, so we don't restart that loop by
        /// calling Connect on every beacon.
        /// </summary>
        private void TryAutoConnect()
        {
            if (_autoConnectDone || ArenaClient.Instance == null)
            {
                return;
            }

            // No address yet → retry the priority chain: the `ServerDiscovery` singleton can be
            // null in Start(), which would miss the arena.json fallback (PlayerPrefs is read
            // statically and never missed).
            if (string.IsNullOrEmpty(_ipBuffer) && ServerDiscovery.Instance != null &&
                ServerDiscovery.Instance.TryGetPreferredEndpoint(out string chainIp, out int chainPort))
            {
                _ipBuffer = FormatEndpoint(chainIp, chainPort);
                RefreshIpText();
            }

            if (!ServerDiscovery.TryParseEndpoint(_ipBuffer, out string ip, out int port))
            {
                return;
            }

            _autoConnectDone = true;
            ArenaClient.Instance.Connect(ip, port, AppSession.Role);
        }

        private void TrySubscribeBeacon()
        {
            if (_beaconSubscribed || ServerDiscovery.Instance == null)
            {
                return;
            }

            ServerDiscovery.Instance.OnBeacon += HandleBeacon;
            _beaconSubscribed = true;
        }

        // -------------------------------------------------------- UI button methods

        /// <summary>Numpad input: "0".."9", "." (passed as the button parameter).</summary>
        public void AppendChar(string c)
        {
            if (string.IsNullOrEmpty(c) || c.Length != 1 || "0123456789.:".IndexOf(c[0]) < 0)
            {
                return;
            }

            if (_ipBuffer.Length >= MaxIpTextLength)
            {
                return;
            }

            _ipBuffer += c;
            _manualEntry = true;
            RefreshIpText();
        }

        public void Backspace()
        {
            if (_ipBuffer.Length == 0)
            {
                return;
            }

            _ipBuffer = _ipBuffer.Substring(0, _ipBuffer.Length - 1);
            _manualEntry = true;
            RefreshIpText();
        }

        public void ClearIp()
        {
            _ipBuffer = "";
            _manualEntry = true;
            RefreshIpText();
        }

        public void ConnectPressed()
        {
            if (!ServerDiscovery.TryParseEndpoint(_ipBuffer, out string ip, out int port))
            {
                SetStatus($"Geçersiz adres: '{_ipBuffer}'");
                return;
            }

            ServerDiscovery.SaveManualEndpoint(ip, port);
            _manualEntry = true;

            if (ArenaClient.Instance == null)
            {
                SetStatus("İstemci hazır değil.");
                return;
            }

            _autoConnectDone = true; // connected manually; the beacon must not take over
            ArenaClient.Instance.Connect(ip, port, AppSession.Role);

            // The address is entered — the numpad has done its job. Closing it also releases the
            // `ConnectionOverlay` request, so the normal connection screens take over again.
            SetIpPanelVisible(false);
        }

        public void DisconnectPressed()
        {
            if (ArenaClient.Instance != null)
            {
                ArenaClient.Instance.Disconnect();
            }
        }

        // ---------------------------------------------------------- event handlers

        private void HandleConnectionStateChanged(ArenaConnectionState state)
        {
            RefreshStatus();
        }

        private void HandleConnected(WelcomeMsg msg)
        {
            RefreshStatus();
        }

        private void HandleKicked(KickedMsg msg)
        {
            string reason = msg != null && !string.IsNullOrEmpty(msg.reason) ? $" ({msg.reason})" : "";
            SetStatus($"Sunucudan atıldınız{reason}.");
        }

        private void HandleBeacon(BeaconMsg beacon, string ip)
        {
            // A manual/saved address is never overwritten by a beacon.
            if (_manualEntry && !string.IsNullOrEmpty(_ipBuffer))
            {
                return;
            }

            int port = beacon != null && beacon.controlPort > 0 ? beacon.controlPort : ArenaProtocol.CONTROL_PORT;
            _ipBuffer = FormatEndpoint(ip, port);
            RefreshIpText();
            TryAutoConnect(); // connect by itself to the beacon-discovered server
        }

        // ------------------------------------------------------------------ render

        private void RefreshIpText()
        {
            if (ipText != null)
            {
                ipText.text = _ipBuffer;
            }

            // ⚠️ The connect button follows the TYPED ADDRESS, not the connection STATE. This panel
            // is opened exactly while the client keeps retrying a stale/wrong address, and
            // `ArenaClient` sits in `Connecting` for seconds (WS timeout) — a state-driven button
            // would be greyed out precisely when needed. `Connect` cancels the running loop and
            // starts a new one, so pressing mid-attempt is safe. Side benefit: the button lights up
            // as soon as the address is complete and stays dim while it is partial.
            if (connectButton != null)
            {
                connectButton.interactable = ServerDiscovery.TryParseEndpoint(_ipBuffer, out _, out _);
            }
        }

        private void RefreshStatus()
        {
            ArenaConnectionState state = ArenaClient.Instance != null
                ? ArenaClient.Instance.State
                : ArenaConnectionState.Disconnected;

            switch (state)
            {
                case ArenaConnectionState.Connected:
                    SetStatus($"Bağlı — oyuncu {ArenaClient.Instance.PlayerId} ({ArenaClient.Instance.ServerIp}:{ArenaClient.Instance.ServerPort})");
                    break;
                case ArenaConnectionState.Connecting:
                    SetStatus($"Bağlanılıyor... ({_ipBuffer})");
                    break;
                default:
                    SetStatus(string.IsNullOrEmpty(_ipBuffer)
                        ? "Sunucu aranıyor..."
                        : $"Bağlı değil ({_ipBuffer})");
                    break;
            }

            // `connectButton` is driven in `RefreshIpText`, NOT here (rationale there).

            if (disconnectButton != null)
            {
                disconnectButton.interactable = state != ArenaConnectionState.Disconnected;
            }
        }

        private void SetStatus(string text)
        {
            if (statusText != null)
            {
                statusText.text = text;
            }
        }

        private static string FormatEndpoint(string ip, int port)
        {
            // Show only the IP on the default port (so ':' isn't a required numpad key).
            return port == ArenaProtocol.CONTROL_PORT ? ip : $"{ip}:{port}";
        }
    }
}
