using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core;
using VortexArena.Core.Arena;
using VortexArena.Core.Audio;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>Operator preferences panel with four tabs (<see cref="AdminPreferencesTab"/>):
    /// MAÇ (shared match settings), GÖRÜNÜM (this screen only), BAĞLANTI (session) and SES — this
    /// screen's own speakers (<see cref="AdminSession"/> → <c>AudioMix</c>), never the player. The tab
    /// choice lives for the session but never goes to disk — it is task context, not a preference.
    /// The card is translucent with no scrim: the live scene stays visible and nothing pauses.
    /// <para>⚠️ Match buttons (BAŞLAT · DURAKLAT/DEVAM · İPTAL) live in the HUD strip
    /// (<see cref="AdminMatchControls"/>) so they work with the panel closed; the selection state
    /// stays here as the single source (<see cref="StartSelectedMatch"/>).</para>
    /// <para>MAÇ is SHARED across admins: selectors send <c>set_selection</c> and the server
    /// broadcasts <c>admin_state</c> back (<see cref="AdminSelection"/>). This component stays
    /// active while the panel is hidden, so another operator's change still lands; the local cursor
    /// advances optimistically and the server value wins. GÖRÜNÜM is LOCAL
    /// (<see cref="AdminSession"/>).</para>
    /// <para>⚠️ Picking a map MOVES THE PLAYERS TOO (§10.7 staging) — it is an immediate scene
    /// command. Mode/map therefore change only while no match is set up
    /// (<see cref="CanChangeSelection"/>); duration/limit stay free since they load no scene.</para>
    /// <para>The lobby is the first row of the map selector (<see cref="LobbyRowLabel"/>), sending
    /// <c>return_to_lobby</c>. The same lock applies, so İPTAL is the way out of a set-up
    /// match.</para>
    /// <para>⚠️ The map cursor follows the OPEN SCENE, not the shared selection
    /// (<see cref="ApplyOpenScene"/>): they diverge after a match ends, and bound to the selection
    /// the operator could never restage that arena, since <see cref="TMP_Dropdown"/> fires no
    /// <c>onValueChanged</c> for the selected row. Hence also no "already selected" early-out.</para>
    /// <para>List choices are dropdowns, numbers are steppers: mode/map grow with the catalog, while
    /// duration/limit have no list to browse. The dropdown template lives in the prefab; this class
    /// only fills options and syncs the cursor.</para>
    /// </summary>
    public class AdminPreferencesPanel : MonoBehaviour
    {
        // ⚠️ Layout is NOT decided in code: panel size, tab bar and row stacking live in
        // `_Shared/App/Resources/UI/AdminPreferencesPanel.prefab`. Rows are hand-stacked by y under
        // the page roots (no Layout Group, 70 px step), so adding a row means shifting everything
        // below it in the prefab. The card aspect is tied to the `PanelBG` art, which stretches
        // badly — tabs exist precisely so the card stays a fixed size. See
        // `Docs/Gelistirici/Arayuz-Tasarimi.md`.

        /// <summary>Score-limit stepper threshold: ±1 below, ±5 above. Gives precision at low
        /// limits and speed at high ones without a four-button widget.</summary>
        private const int ScoreLimitFineThreshold = 20;

        private const int ScoreLimitMin = 1;
        private const int ScoreLimitMax = 999;

        /// <summary>Bottom rung of the score-limit stepper: unlimited
        /// (<see cref="ArenaProtocol.SCORE_LIMIT_UNLIMITED"/>), one step below <c>1</c>.
        /// <para>It sits at the end of the range rather than in a separate checkbox because it is a
        /// value of the "how many rounds" question, not a second switch — a checkbox next to a
        /// visible number leaves it unclear which one applies. In tournament mode unlimited means no
        /// win limit and no round cap: rounds run until the operator hits İPTAL.</para></summary>
        private const int ScoreLimitUnlimited = ArenaProtocol.SCORE_LIMIT_UNLIMITED;

        // ⚠️ Fields are [SerializeField]: the look comes from the prefab and this class only writes
        // data. An unbound field silently draws nothing, so never delete an element — disable it.

        [Header("Panel kökü")]
        [Tooltip("Açılıp kapanan kart — panel kapalıyken bu obje devre dışı bırakılır.")]
        [SerializeField] private GameObject _root;

        [SerializeField] private Button _closeButton;

        /// <summary>Fullscreen ↔ windowed toggle. ⚠️ Lives in the title bar next to KAPAT, not in
        /// GÖRÜNÜM: window mode is window chrome, expected at the window corner (same job as
        /// F11).</summary>
        [SerializeField] private Button _screenModeButton;

        [SerializeField] private TextMeshProUGUI _screenModeLabel;

        [Header("Sekmeler")]

        [Tooltip("Sekme düğmeleri — sıra AdminPreferencesTab ile aynı: MAÇ, GÖRÜNÜM, BAĞLANTI, SES.")]
        [SerializeField] private Button[] _tabButtons = new Button[4];

        [Tooltip("Sekme etiketleri — _tabButtons ile aynı sırada: MAÇ, GÖRÜNÜM, BAĞLANTI, SES.")]
        [SerializeField] private TextMeshProUGUI[] _tabLabels = new TextMeshProUGUI[4];

        [Tooltip("Sekme sayfaları (satırların kökleri) — aynı sırada: MAÇ, GÖRÜNÜM, BAĞLANTI, SES; " +
                 "yalnız etkin sekmenin sayfası açık kalır.")]
        [SerializeField] private GameObject[] _tabPages = new GameObject[4];

        [Header("Düğme zeminleri (görsel)")]

        [Tooltip("PASİF düğme zemini. Bunun ve VURGULU'nun İKİSİ birden bağlıysa düğmeler " +
                 "sprite değiştirir (tint beyaz kalır); biri boşsa renk tintine düşülür.")]
        [SerializeField] private Sprite _buttonIdleSprite;

        [Tooltip("SEÇİLİ/ETKİN düğme zemini (aktif sekme, yürürlükteki kalibre kipi, tam ekran).")]
        [SerializeField] private Sprite _buttonActiveSprite;

        [Tooltip("YIKICI eylemin kurulmuş hâli (çıkış onayı). Boşsa vurgulu zemin kullanılır.")]
        [SerializeField] private Sprite _buttonDangerSprite;

        /// <summary>Open tab. Persists for the session but never to <c>PlayerPrefs</c>: which page
        /// is open is task context, not a screen preference.</summary>
        private AdminPreferencesTab _tab = AdminPreferencesTab.Match;

        [Header("MAÇ bölümü (ortak)")]

        /// <summary>Mode selector. ⚠️ Options are filled by CODE from the catalog; the prefab list
        /// is a template and is cleared at runtime. Caption text is the dropdown's own.</summary>
        [SerializeField] private TMP_Dropdown _modeDropdown;

        /// <summary>Map selector — options are rebuilt from the selected mode plus the venue filter
        /// on every <see cref="RefreshMapList"/>.</summary>
        [SerializeField] private TMP_Dropdown _mapDropdown;

        [SerializeField] private TextMeshProUGUI _durationValue;
        [SerializeField] private Button _durationPrev;
        [SerializeField] private Button _durationNext;

        [SerializeField] private TextMeshProUGUI _scoreLimitValue;
        [SerializeField] private Button _scoreLimitPrev;
        [SerializeField] private Button _scoreLimitNext;

        // Countdown length (§5.2 countdownSeconds). In round-based modes this is also the
        // BETWEEN-rounds countdown; elsewhere it runs once at match start.
        [SerializeField] private TextMeshProUGUI _countdownValue;
        [SerializeField] private Button _countdownPrev;
        [SerializeField] private Button _countdownNext;

        // Friendly fire (§5.2 set_friendly_fire). ⚠️ Not a SELECTION but an immediate command that
        // applies mid-match, so ApplySelectionLock skips it — being pressable during a live match is
        // the whole point. Both buttons toggle, keeping the row pattern.
        [SerializeField] private TextMeshProUGUI _friendlyFireValue;
        [SerializeField] private Button _friendlyFirePrev;
        [SerializeField] private Button _friendlyFireNext;

        // ⚠️ No separate "LOBİYE DÖN" button and none is added back: the lobby is the first row of
        // the map selector, so the rule has one gate. Ending a running match is İPTAL.

        [Header("Kalibrasyon")]

        // Calibration mode (§5.2 set_calibration_mode): how headsets align at launch. Immediate
        // command like friendly fire, not under the selection lock.
        // ⚠️ THREE separate buttons, not prev/next: the third option is visible but disabled, and a
        // cycling cursor would skip it, hiding its existence.
        [Tooltip("Başlıklar açılışta İKİ ÇAPA ile elle kalibre edilsin (sunucu varsayılanı): " +
                 "diskteki kayıtlı çapa OKUNMAZ. Anlık komut, tüm adminlere yayılır.")]
        [SerializeField] private Button _calibModeTwoButton;
        [SerializeField] private TextMeshProUGUI _calibModeTwoLabel;

        [Tooltip("Başlıklar açılışta cihazda KAYITLI çapadan hizalansın — oyuncu her seansta " +
                 "yeniden kalibre etmez. Zemin işaretleri yerinden oynamadıysa kullanılır.")]
        [SerializeField] private Button _calibModeSavedButton;
        [SerializeField] private TextMeshProUGUI _calibModeSavedLabel;

        [Tooltip("Paylaşılan uzamsal çapa — REZERVE. Düğme hiçbir komut göndermez ve pasiftir; " +
                 "sunucu bu modu zaten reddeder. Seçenek yalnız görünür olsun diye durur.")]
        [SerializeField] private Button _calibModeCloudButton;
        [SerializeField] private TextMeshProUGUI _calibModeCloudLabel;

        /// <summary>Idle button background, distinct from the active <see cref="UiKit.Accent"/>.
        /// Used by <see cref="PaintButtonBackground"/> when no sprites are bound.</summary>
        private static readonly Color CalibModeIdleFill = UiKit.Hex(0x2A303B, 0xFF);

        [Header("Bağlantı")]
        [SerializeField] private TextMeshProUGUI _connectionText;
        [SerializeField] private Button _reconnectButton;
        [SerializeField] private Button _disconnectButton;

        /// <summary>Quits the admin app behind a two-step confirm (<see cref="ArmQuit"/>). Sits next
        /// to the connection row: both end the session and are looked for in the same place.</summary>
        [SerializeField] private Button _quitButton;

        [SerializeField] private TextMeshProUGUI _quitLabel;

        /// <summary>Quit confirm window (s): a misclick mid-match would close the admin and leave
        /// the operator blind, with no undo.</summary>
        private const float QuitConfirmSeconds = 3f;
        private float _quitArmedAt = -1f;

        [Header("GÖRÜNÜM bölümü (yalnız bu ekran)")]
        [SerializeField] private TextMeshProUGUI _markersValue;
        [SerializeField] private Button _markersPrev;
        [SerializeField] private Button _markersNext;

        [SerializeField] private TextMeshProUGUI _nameplatesValue;
        [SerializeField] private Button _nameplatesPrev;
        [SerializeField] private Button _nameplatesNext;

        // Violation alert sound (§10.9). Both buttons toggle, keeping the row pattern.
        // ⚠️ Lives in GÖRÜNÜM because it belongs to THIS SCREEN only (AdminSession/PlayerPrefs):
        // one operator muting it does not mute another's.
        [Tooltip("Fiziksel ihlal başlayınca uyarı sesi çalsın mı (yalnız bu admin PC'sinde).")]
        [SerializeField] private TextMeshProUGUI _violationSoundValue;
        [SerializeField] private Button _violationSoundPrev;
        [SerializeField] private Button _violationSoundNext;

        [SerializeField] private TextMeshProUGUI _speedValue;
        [SerializeField] private Button _speedPrev;
        [SerializeField] private Button _speedNext;

        [SerializeField] private TextMeshProUGUI _roofValue;
        [SerializeField] private Button _roofPrev;
        [SerializeField] private Button _roofNext;

        /// <summary>Audio output device selector (this screen only — <see cref="AdminSession"/>);
        /// a dropdown because the endpoint count is unknown per PC. ⚠️ Options are filled by CODE
        /// from Windows; the prefab list is a template and is cleared at runtime.</summary>
        [SerializeField] private TMP_Dropdown _audioDeviceDropdown;

        [Header("SES bölümü (yalnız bu ekran)")]

        // ⚠️ Rows are index-bound to AudioChannel: Ambiyans, Silah sesleri, Seslendirme, Müzik. An
        // unbound element silently draws nothing, so every read below is null-safe (At<T>).

        [Tooltip("Kanal seviyesi metinleri — sıra AudioChannel ile aynı: Ambiyans, Silah, " +
                 "Seslendirme, Müzik.")]
        [SerializeField] private TextMeshProUGUI[] _audioValues = new TextMeshProUGUI[AudioMix.ChannelCount];

        [Tooltip("Seviyeyi azaltan düğmeler — _audioValues ile aynı sırada.")]
        [SerializeField] private Button[] _audioPrev = new Button[AudioMix.ChannelCount];

        [Tooltip("Seviyeyi artıran düğmeler — aynı sırada.")]
        [SerializeField] private Button[] _audioNext = new Button[AudioMix.ChannelCount];

        [Tooltip("Tek dokunuşta sessize alan / eski seviyeye döndüren düğmeler — aynı sırada.")]
        [SerializeField] private Button[] _audioMuteButtons = new Button[AudioMix.ChannelCount];

        [Tooltip("Sessize alma düğmelerinin etiketleri — aynı sırada.")]
        [SerializeField] private TextMeshProUGUI[] _audioMuteLabels = new TextMeshProUGUI[AudioMix.ChannelCount];

        private readonly List<ModeDefinition> _modes = new List<ModeDefinition>();
        private readonly List<MapDefinition> _maps = new List<MapDefinition>();
        private int _modeIndex;
        private int _mapIndex;

        /// <summary>Output endpoints read from Windows. ⚠️ Not cached — refreshed on every panel
        /// open, because a stale list would offer a device that no longer exists.</summary>
        private readonly List<AudioOutputDevice> _audioDevices = new List<AudioOutputDevice>();

        /// <summary>Next match duration/limit (SHARED, sent with set_selection). Resets to the
        /// mode's <see cref="ModeDefinition"/> defaults when the mode changes.</summary>
        private int _roundSeconds;
        private int _scoreLimit;

        /// <summary>Countdown length (s); <c>0</c> = protocol default (§5.2).</summary>
        private int _countdownSeconds;

        private bool _dirty = true;

        /// <summary>Which venue filter built the current map list. <c>-1</c> = never filtered: the
        /// panel is built before connecting, so the first list is unavoidably unfiltered.</summary>
        private int _appliedVenueVersion = -1;

        private void Start()
        {
            WireButtons();

            if (_root != null)
            {
                _root.SetActive(false); // visibility is decided by Apply()
            }

            AdminContent.CollectModes(_modes);
            RebuildModeOptions(); // mode list comes from the catalog and never changes afterwards -> once
            RefreshMapList();     // builds the map options itself (mode + venue filter)
            RefreshAudioDeviceList();
            ResetMatchParametersToModeDefaults();
            Apply();
        }

        /// <summary>Wires behaviour onto the prefab's buttons and selectors. ⚠️ The prefab must
        /// carry NO persistent <c>onClick</c>/<c>onValueChanged</c> entries: most callbacks here are
        /// conditional, and an inspector-bound entry skips those conditions — e.g. quitting the
        /// admin in one click.</summary>
        private void WireButtons()
        {
            Wire(_closeButton, AdminSession.ClosePanel);
            Wire(_screenModeButton, AdminSession.ToggleScreenMode);
            WireTabs();

            WireDropdown(_modeDropdown, SelectMode);
            WireDropdown(_mapDropdown, SelectMap);
            Wire(_durationPrev, DurationPrev);
            Wire(_durationNext, DurationNext);
            Wire(_scoreLimitPrev, ScoreLimitDown);
            Wire(_scoreLimitNext, ScoreLimitUp);
            Wire(_countdownPrev, CountdownDown);
            Wire(_countdownNext, CountdownUp);
            Wire(_friendlyFirePrev, ToggleFriendlyFire);
            Wire(_friendlyFireNext, ToggleFriendlyFire);

            Wire(_calibModeTwoButton, () => AdminCommands.SetCalibrationMode(ArenaProtocol.CALIB_MODE_TWO_ANCHOR));
            Wire(_calibModeSavedButton, () => AdminCommands.SetCalibrationMode(ArenaProtocol.CALIB_MODE_SAVED_ANCHOR));
            // ⚠️ _calibModeCloudButton is NOT wired: reserved mode, the server rejects it. A wired
            // but inert button would make the operator think the command went through.

            Wire(_markersPrev, PrevMarkers);
            Wire(_markersNext, NextMarkers);
            Wire(_nameplatesPrev, ToggleNameplates);
            Wire(_nameplatesNext, ToggleNameplates);
            Wire(_violationSoundPrev, ToggleViolationSound);
            Wire(_violationSoundNext, ToggleViolationSound);
            Wire(_speedPrev, SpeedDown);
            Wire(_speedNext, SpeedUp);
            Wire(_roofPrev, PrevRoof);
            Wire(_roofNext, NextRoof);
            WireDropdown(_audioDeviceDropdown, SelectAudioDevice);
            WireAudioRows();

            Wire(_reconnectButton, AdminCommands.Reconnect);
            Wire(_disconnectButton, AdminCommands.Disconnect);
            Wire(_quitButton, ArmQuit);
        }

        /// <summary>Binds tab buttons to their indices. ⚠️ The index comes from a local copy, not
        /// the loop variable: a lambda captures the variable and all buttons would open the last
        /// tab.</summary>
        private void WireTabs()
        {
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] == null)
                {
                    continue;
                }

                var tab = (AdminPreferencesTab)i;
                _tabButtons[i].onClick.RemoveAllListeners();
                _tabButtons[i].onClick.AddListener(() => SelectTab(tab));
            }
        }

        private void SelectTab(AdminPreferencesTab tab)
        {
            _tab = tab;

            // Device list refreshes when entering GÖRÜNÜM, so a headset plugged in while the panel
            // is open does not require closing and reopening it.
            if (tab == AdminPreferencesTab.View)
            {
                RefreshAudioDeviceList();
            }

            Apply();
        }

        /// <summary>Binds the audio rows to their channels. ⚠️ The channel comes from a local copy,
        /// not the loop variable — a lambda captures the variable and every row would drive the last
        /// channel (same trap as <see cref="WireTabs"/>).</summary>
        private void WireAudioRows()
        {
            for (int i = 0; i < AudioMix.ChannelCount; i++)
            {
                var channel = (AudioChannel)i;
                Wire(At(_audioPrev, i), () => AdminSession.StepAudioLevel(channel, -1));
                Wire(At(_audioNext, i), () => AdminSession.StepAudioLevel(channel, 1));
                Wire(At(_audioMuteButtons, i), () => AdminSession.ToggleAudioMute(channel));
            }
        }

        /// <summary>Element of an index-bound array; null when the array is short or unbound.</summary>
        private static T At<T>(T[] array, int index) where T : class
        {
            return array != null && index >= 0 && index < array.Length ? array[index] : null;
        }

        private static void Wire(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private static void WireDropdown(TMP_Dropdown dropdown,
            UnityEngine.Events.UnityAction<int> action)
        {
            if (dropdown == null)
            {
                return;
            }

            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.onValueChanged.AddListener(action);
        }

        private void OnEnable()
        {
            AdminSession.Changed += MarkDirty;
            // ⚠️ AdminCommands.StatusChanged is NOT subscribed: the status line lives in the HUD
            // match strip and no field here depends on command status — subscribing would force a
            // full refresh on every command.
            NetEvents.OnConnectionStateChanged += HandleConnectionState;

            // The shared selection may change from another admin. This component stays active while
            // the panel is hidden (only the card is), so their map change still previews here.
            AdminSelection.Changed += HandleSharedSelectionChanged;

            // Open scene changed (§10.7) → move the map cursor. The scene command can arrive
            // independently of the shared selection (end-of-match lobby return), so
            // AdminSelection.Changed cannot be trusted for it.
            NetEvents.OnReturnToLobby += HandleOpenSceneChanged;
            NetEvents.OnLoadMatch += HandleOpenSceneChanged;

            // (Re)connect: welcome carries the open scene. Needed because a late-joining admin MISSED
            // the scene commands — the shared selection would still name the last arena and the
            // operator could not reselect it (a selected row fires no event).
            NetEvents.OnConnected += HandleWelcome;

            // Phase changes lock and unlock the mode/map selectors (§10.7).
            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed += MarkDirty;
            }
        }

        private void OnDisable()
        {
            AdminSession.Changed -= MarkDirty;
            NetEvents.OnConnectionStateChanged -= HandleConnectionState;
            AdminSelection.Changed -= HandleSharedSelectionChanged;
            NetEvents.OnReturnToLobby -= HandleOpenSceneChanged;
            NetEvents.OnLoadMatch -= HandleOpenSceneChanged;
            NetEvents.OnConnected -= HandleWelcome;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed -= MarkDirty;
            }
        }

        private void Update()
        {
            // The confirm window closes itself (AdminPlayerRow.Tick pattern).
            if (_quitArmedAt >= 0f && Time.unscaledTime - _quitArmedAt > QuitConfirmSeconds)
            {
                _quitArmedAt = -1f;
                _dirty = true;
            }

            if (_dirty)
            {
                _dirty = false;
                Apply();
            }
        }

        /// <summary>Quits the admin app behind a two-step confirm (same pattern as the destructive
        /// buttons in AdminPlayerRow). ⚠️ Nothing is announced to the server: the admin is an
        /// observer and its exit does not affect the match (the socket close is enough).</summary>
        private void ArmQuit()
        {
            if (_quitArmedAt < 0f)
            {
                _quitArmedAt = Time.unscaledTime;
                _dirty = true;
                return;
            }

            _quitArmedAt = -1f;
            _dirty = true;
            Quit();
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            // Application.Quit() is a no-op in the editor; stopping play means the same thing
            // (same contract as KickedShutdown).
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void HandleConnectionState(ArenaConnectionState state)
        {
            _dirty = true;
        }

        /// <summary>Server sent a scene command — the open scene changed (§10.7).</summary>
        private void HandleOpenSceneChanged(ReturnToLobbyMsg msg)
        {
            ApplyOpenScene(msg != null ? msg.sceneName : "");
        }

        private void HandleOpenSceneChanged(LoadMatchMsg msg)
        {
            ApplyOpenScene(msg != null ? msg.sceneName : "");
        }

        /// <summary>Connected — <c>welcome</c> carries the server's current open scene (§10.7).</summary>
        private void HandleWelcome(WelcomeMsg msg)
        {
            ApplyOpenScene(msg != null && msg.match != null ? msg.match.sceneName : "");
        }

        /// <summary>Moves the map cursor to the server's OPEN SCENE.
        /// <para>⚠️ This is a different source from the shared selection (<c>admin_state</c>) and
        /// the correct one for the selector. They diverge when a match ends and everyone returns to
        /// the lobby: the shared selection still names the last arena. Bound to the shared
        /// selection the operator could not restage that arena, because
        /// <see cref="TMP_Dropdown"/> fires no <c>onValueChanged</c> for the already-selected row.
        /// The server deliberately supports restaging the same arena (§10.7: the test is "is this
        /// the open scene", not "did the selection change"), so the client must not block it.</para>
        /// <para>The optimistic local preview does not pass through here
        /// (<c>SceneRouter.LoadPreview</c> leaves the open scene alone), so the cursor never jumps
        /// ahead of the server and snaps back.</para>
        /// </summary>
        private void ApplyOpenScene(string sceneName)
        {
            _dirty = true;

            if (string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            if (AdminContent.IsLobbyScene(sceneName))
            {
                _lobbyOpen = true;
                return;
            }

            _lobbyOpen = false;

            // Not in the list (another mode's arena is staged) → leave the cursor put so the
            // selector is never blank.
            int index = IndexOfMap(sceneName);
            if (index >= 0)
            {
                _mapIndex = index;
            }
        }

        // ------------------------------------------------------------------- actions

        /// <summary>Starts a match with the panel's selected mode/map/duration/limit/countdown.
        /// Called by <see cref="AdminMatchControls"/>: the button is elsewhere but the selection
        /// state lives here.
        /// <para>⚠️ Rejected while the lobby is open: no arena is staged and the server does not
        /// start a match on a lobby map (§10.7), so the reason is written to the status line instead
        /// of sending a command that is silently refused.</para></summary>
        public void StartSelectedMatch()
        {
            if (_lobbyOpen)
            {
                AdminCommands.Note("Lobi açık — önce bir arena seç, sonra BAŞLAT.");
                return;
            }

            AdminCommands.StartMatch(SelectedModeId, SelectedSceneName, _roundSeconds, _scoreLimit,
                _countdownSeconds);
        }

        /// <summary>Is BAŞLAT meaningful now: an arena is staged (lobby not open), no match set up
        /// (<see cref="CanChangeSelection"/>), mode and map selected. UI gate only — the server has
        /// authority.</summary>
        public bool CanStartMatch => !_lobbyOpen && CanChangeSelection &&
                                     !string.IsNullOrEmpty(SelectedModeId) &&
                                     !string.IsNullOrEmpty(SelectedSceneName);

        private string SelectedModeId =>
            _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex].ModeId : "";

        private string SelectedSceneName =>
            _mapIndex >= 0 && _mapIndex < _maps.Count ? _maps[_mapIndex].SceneName : "";

        private ModeDefinition SelectedMode =>
            _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex] : null;

        /// <summary>Mode selected (<c>onValueChanged</c>).
        /// <para>⚠️ The dropdown is closed FIRST: a map preview loads right after
        /// (<see cref="PreviewSelectedMap"/>) and an open list would hang on screen across the scene
        /// change.</para>
        /// <para>If the selection is refused (match running, stale index) <see cref="Apply"/> pulls
        /// the cursor back — the dropdown already moved its own value.</para></summary>
        private void SelectMode(int index)
        {
            HideDropdown(_modeDropdown);

            if (index == _modeIndex || index < 0 || index >= _modes.Count || !GuardSelectionChange())
            {
                Apply();
                return;
            }

            _modeIndex = index;
            RefreshMapList();
            _lobbyOpen = false; // a mode change also stages an arena (PublishSelection below)
            // Duration/limit fall back to each mode's own defaults, so a 10-minute TDM setting does
            // not silently carry into a mode meant to run 3 minutes.
            ResetMatchParametersToModeDefaults();
            PublishSelection(mapChanged: true); // mode changed -> map list reset, so the selected map changed too
        }

        // ---- match parameters (SHARED) ----

        /// <summary>Falls back to the selected mode's <see cref="ModeDefinition"/> defaults. With no
        /// catalog the server's own values already apply; this only resets the displayed
        /// number.</summary>
        private void ResetMatchParametersToModeDefaults()
        {
            ModeDefinition mode = SelectedMode;
            _roundSeconds = mode != null && mode.RoundSeconds > 0 ? mode.RoundSeconds : 0;
            _scoreLimit = mode != null ? Mathf.Clamp(mode.ScoreLimit, ScoreLimitMin, ScoreLimitMax) : 0;
            // ⚠️ Countdown has no ModeDefinition field and gets none: it is a match parameter, not
            // mode shape (§5.2). 0 = protocol default.
            _countdownSeconds = 0;
        }

        private void DurationPrev() { StepDuration(-1); }
        private void DurationNext() { StepDuration(1); }

        /// <summary>Cycles the duration options (§1 <c>ROUND_SECONDS_OPTIONS</c>). If the current
        /// value is not in the list (a mode default need not be) it continues from the nearest
        /// option, so the operator is not lost after two clicks.</summary>
        private void StepDuration(int delta)
        {
            int[] options = ArenaProtocol.ROUND_SECONDS_OPTIONS;
            if (options == null || options.Length == 0)
            {
                return;
            }

            int index = (NearestDurationIndex(options, _roundSeconds) + delta + options.Length) % options.Length;
            _roundSeconds = options[index];
            PublishSelection(mapChanged: false);
        }

        private static int NearestDurationIndex(int[] options, int seconds)
        {
            int best = 0;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < options.Length; i++)
            {
                int distance = Mathf.Abs(options[i] - seconds);
                if (distance >= bestDistance)
                {
                    continue;
                }

                best = i;
                bestDistance = distance;
            }

            return best;
        }

        private void ScoreLimitDown() { StepScoreLimit(-1); }
        private void ScoreLimitUp() { StepScoreLimit(1); }

        /// <summary>Score-limit stepper: ±1 below the threshold, ±5 above, with the unlimited rung
        /// (<see cref="ScoreLimitUnlimited"/>) at the bottom. Unlimited sits one step below
        /// <c>ScoreLimitMin</c>, so it is never hit by accident, and stepping up from it returns
        /// straight to <c>ScoreLimitMin</c>.</summary>
        private void StepScoreLimit(int direction)
        {
            // The unlimited rung is NOT part of the number axis (−1 in arithmetic would produce
            // 0/−2), so entering and leaving it are separate branches.
            if (_scoreLimit < 0)
            {
                if (direction > 0)
                {
                    _scoreLimit = ScoreLimitMin;
                    PublishSelection(mapChanged: false);
                }

                return; // unlimited is the bottom step: pressing down does nothing
            }

            // ⚠️ The gate is "== ScoreLimitMin", not "<=": 0 ("mode default") is UNKNOWN, not a
            // point on the axis — stepping down from it goes to the minimum, not to unlimited.
            if (direction < 0 && _scoreLimit == ScoreLimitMin)
            {
                _scoreLimit = ScoreLimitUnlimited;
                PublishSelection(mapChanged: false);
                return;
            }

            int step = _scoreLimit >= ScoreLimitFineThreshold ? 5 : 1;
            // Recompute the step against the threshold so stepping down does not overshoot it.
            if (direction < 0 && _scoreLimit - step < ScoreLimitFineThreshold)
            {
                step = _scoreLimit > ScoreLimitFineThreshold ? _scoreLimit - ScoreLimitFineThreshold : 1;
            }

            _scoreLimit = Mathf.Clamp(_scoreLimit + direction * step, ScoreLimitMin, ScoreLimitMax);
            PublishSelection(mapChanged: false);
        }

        private void CountdownDown() { StepCountdown(-1); }
        private void CountdownUp() { StepCountdown(1); }

        /// <summary>Countdown stepper: ±1 s within
        /// <c>[COUNTDOWN_SECONDS_MIN, COUNTDOWN_SECONDS_MAX]</c>. The range is the server's
        /// constraint, not a UI list (§5.2). From 0 ("default") the first touch jumps to the
        /// minimum; the way back is changing mode, same contract as the score limit.</summary>
        private void StepCountdown(int direction)
        {
            _countdownSeconds = Mathf.Clamp(_countdownSeconds + direction,
                ArenaProtocol.COUNTDOWN_SECONDS_MIN, ArenaProtocol.COUNTDOWN_SECONDS_MAX);
            PublishSelection(mapChanged: false);
        }

        /// <summary>Toggles friendly fire (§5.2). ⚠️ Not via <c>PublishSelection</c>: this is a live
        /// session setting, not part of the shared selection, and does not fit
        /// <c>set_selection</c>'s "0/empty = leave alone" contract.
        /// <para>No local field is kept — the wanted state is the inverse of the server's, and the
        /// panel shows the value broadcast back via <c>admin_state</c> so two operators cannot
        /// diverge.</para></summary>
        private void ToggleFriendlyFire()
        {
            AdminCommands.SetFriendlyFire(!AdminSelection.FriendlyFire);
        }

        /// <summary>Map selected — rationale in <see cref="SelectMode"/>.
        /// <para>⚠️ The first row is the LOBBY, not an arena (<see cref="LobbyRowLabel"/>): it sends
        /// <c>return_to_lobby</c> instead of <c>set_selection</c> and the cursor STAYS there,
        /// because the selector shows the open scene (<see cref="ApplyOpenScene"/>).</para>
        /// <para>⚠️ There is no "already selected" early-out. If the operator pressed a row the
        /// command goes; the server handles restaging idempotently (§10.7). An equality gate would
        /// lock the operator out whenever cursor and open scene diverge.</para></summary>
        private void SelectMap(int index)
        {
            HideDropdown(_mapDropdown);

            if (HasLobbyRow && index == LobbyRowIndex)
            {
                if (!GuardSelectionChange())
                {
                    Apply(); // the cursor snaps back to the staged scene
                    return;
                }

                _lobbyOpen = true; // optimistic: the server's return_to_lobby will confirm the same value
                AdminCommands.ReturnToLobby();
                Apply();
                return;
            }

            int mapIndex = index - MapRowOffset;
            if (mapIndex < 0 || mapIndex >= _maps.Count || !GuardSelectionChange())
            {
                Apply();
                return;
            }

            _mapIndex = mapIndex;
            _lobbyOpen = false;
            PublishSelection(mapChanged: true);
        }

        private static void HideDropdown(TMP_Dropdown dropdown)
        {
            if (dropdown != null)
            {
                dropdown.Hide();
            }
        }

        /// <summary>Can mode/map change right now (§10.7)? Picking a map loads a scene on ALL
        /// clients, so it is refused whenever a match is set up (running, loading, countdown,
        /// paused) — the test is <see cref="AdminRoster.CanChangeSelection"/>. The selectors are
        /// already disabled (<see cref="ApplySelectionLock"/>); this is the second guard and makes
        /// the refusal visible. The server enforces the same rule.
        /// <para>⚠️ The lobby row passes through this gate too — it is also a scene command, and
        /// İPTAL is the way out of a set-up match.</para></summary>
        private static bool GuardSelectionChange()
        {
            if (CanChangeSelection)
            {
                return true;
            }

            AdminCommands.Note("Maç kurulu — harita/mod değiştirilemez; önce İPTAL.");
            return false;
        }

        /// <summary>Is no match set up? Not blocked while <see cref="AdminRoster"/> is still missing
        /// (first frames before connecting) — the server has the last word anyway.</summary>
        private static bool CanChangeSelection
        {
            get
            {
                AdminRoster roster = AdminRoster.Instance;
                return roster == null || roster.CanChangeSelection;
            }
        }

        /// <summary>Publishes the local cursor (<c>set_selection</c>) and applies it optimistically
        /// so preview and UI react without waiting. When the server broadcasts it back,
        /// <see cref="HandleSharedSelectionChanged"/> sees the same value and does nothing, so there
        /// is no loop. Without a connection the command drops silently and the preview still runs.
        /// <para>⚠️ The map field is an IMMEDIATE scene command, not a note for the next match
        /// (§10.7): mode/map are only sent while <see cref="CanChangeSelection"/> is true, whereas
        /// duration/limit are free in every phase.</para></summary>
        /// <param name="mapChanged">Did the operator actually move the mode/map cursor. ⚠️ The
        /// mode/map fields are filled ONLY then (§5.2): the server tries to stage on any
        /// <c>set_selection</c> carrying a map, so filling them on a duration touch would move
        /// everyone. The local preview passes the same gate, else the operator alone would drop
        /// into that arena.</param>
        private void PublishSelection(bool mapChanged)
        {
            bool sendSelection = mapChanged && CanChangeSelection;
            AdminCommands.SetSelection(
                sendSelection ? SelectedModeId : "",
                sendSelection ? SelectedSceneName : "",
                _roundSeconds, _scoreLimit, _countdownSeconds);

            if (mapChanged)
            {
                PreviewSelectedMap();
            }

            Apply();
        }

        /// <summary>Shared selection arrived from the server (possibly from another admin): move the
        /// cursors and open the local preview. A no-op when unchanged, so the echo of our own
        /// <c>set_selection</c> does not re-trigger the preview. A selection with no matching row
        /// leaves the cursor put — this is normal, not a version skew: the server seeds the shared
        /// selection with the venue's lobby map at startup (§5.3), and the cursor follows the OPEN
        /// SCENE anyway (<see cref="ApplyOpenScene"/>).</summary>
        private void HandleSharedSelectionChanged()
        {
            _dirty = true;

            // The venue filter is applied BEFORE and independently of the selection: the map list
            // was built before connecting, so it still holds other venues' arenas. The server
            // announces the venue with the first admin_state; without filtering here, unplayable
            // arenas would stay visible until the operator touched the mode button.
            if (_appliedVenueVersion != AdminSelection.VenueVersion)
            {
                _appliedVenueVersion = AdminSelection.VenueVersion;
                RefreshMapList();
            }

            string sharedMode = AdminSelection.ModeId;
            string sharedScene = AdminSelection.SceneName;

            bool changed = false;
            bool sceneChanged = false; // the preview is refreshed ONLY when the mode/map changes

            if (!string.IsNullOrEmpty(sharedMode) && sharedMode != SelectedModeId)
            {
                int index = IndexOfMode(sharedMode);
                if (index >= 0)
                {
                    _modeIndex = index;
                    RefreshMapList(); // mode changed -> the compatible map list changed as well
                    ResetMatchParametersToModeDefaults();
                    changed = true;
                    sceneChanged = true;
                }
            }

            // ⚠️ The lobby scene must NOT land here: whether the selector shows the lobby is decided
            // by the OPEN SCENE, not the shared selection (ApplyOpenScene). They diverge after a
            // match ends, when the shared selection still names the last arena.
            if (!string.IsNullOrEmpty(sharedScene) && !AdminContent.IsLobbyScene(sharedScene) &&
                sharedScene != SelectedSceneName)
            {
                int index = IndexOfMap(sharedScene);
                if (index >= 0)
                {
                    _mapIndex = index;
                    changed = true;
                    sceneChanged = true;
                }
            }

            // Parameters are applied AFTER the mode: a mode change pulls them to local defaults, and
            // the server's shared value must have the last word (0 = never chosen).
            if (AdminSelection.RoundSeconds > 0 && AdminSelection.RoundSeconds != _roundSeconds)
            {
                _roundSeconds = AdminSelection.RoundSeconds;
                changed = true;
            }

            // ⚠️ The limit gate is "!= 0", not "> 0": a negative value from the server means
            // UNLIMITED, not "unset" (§5.2), and a positivity gate would swallow another operator's
            // unlimited choice.
            if (AdminSelection.ScoreLimit != 0 && AdminSelection.ScoreLimit != _scoreLimit)
            {
                _scoreLimit = AdminSelection.ScoreLimit < 0
                    ? ScoreLimitUnlimited
                    : Mathf.Clamp(AdminSelection.ScoreLimit, ScoreLimitMin, ScoreLimitMax);
                changed = true;
            }

            if (AdminSelection.CountdownSeconds > 0 && AdminSelection.CountdownSeconds != _countdownSeconds)
            {
                _countdownSeconds = Mathf.Clamp(AdminSelection.CountdownSeconds,
                    ArenaProtocol.COUNTDOWN_SECONDS_MIN, ArenaProtocol.COUNTDOWN_SECONDS_MAX);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            if (sceneChanged)
            {
                PreviewSelectedMap();
            }

            Apply();
        }

        private int IndexOfMode(string modeId)
        {
            for (int i = 0; i < _modes.Count; i++)
            {
                if (_modes[i] != null && _modes[i].ModeId == modeId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexOfMap(string sceneName)
        {
            for (int i = 0; i < _maps.Count; i++)
            {
                if (_maps[i] != null && _maps[i].SceneName == sceneName)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Rebuilds the map list from the selected mode plus the server's venue. If the
        /// selected map survives, the cursor stays on it: the list also rebuilds when the venue
        /// filter arrives, and resetting to the top would change the shown map for no visible
        /// reason.</summary>
        private void RefreshMapList()
        {
            string modeId = _modeIndex >= 0 && _modeIndex < _modes.Count ? _modes[_modeIndex].ModeId : "";
            string previous = SelectedSceneName;

            AdminContent.CollectMaps(modeId, _maps);

            // The lobby row refreshes WITH the list: when the venue filter changes the lobby changes
            // too, since each venue has its own (§10.7).
            _lobbyMap = AdminContent.ResolveLobbyMap();

            int index = string.IsNullOrEmpty(previous) ? -1 : IndexOfMap(previous);
            _mapIndex = index >= 0 ? index : 0;

            RebuildMapOptions();
        }

        // --------------------------------------------------------------- selectors

        /// <summary>Caption shown when the catalog is empty; the selector is disabled then
        /// (<see cref="ApplySelectionLock"/>), since a dropdown that opens to one row leaves the
        /// operator wondering whether the click registered.</summary>
        private const string NoModesLabel = "katalog yok";

        private const string NoMapsLabel = "harita yok";

        /// <summary>First row of the map selector; selecting it sends <c>return_to_lobby</c>
        /// (<see cref="SelectMap"/>).</summary>
        private const string LobbyRowLabel = "Lobi";

        /// <summary>⚠️ The lobby row stays at the TOP of the list: at the bottom its index would
        /// shift with mode/venue, breaking the operator's muscle memory.</summary>
        private const int LobbyRowIndex = 0;

        /// <summary>This venue's lobby map (§10.7); null when the catalog lacks it or the venue
        /// filter excludes it, in which case no lobby row is drawn.</summary>
        private MapDefinition _lobbyMap;

        /// <summary>Is the server's open scene the lobby (<see cref="ApplyOpenScene"/>). If so the
        /// selector shows the lobby row and BAŞLAT refuses — no arena is staged.</summary>
        private bool _lobbyOpen;

        private bool HasLobbyRow => _lobbyMap != null;

        /// <summary>Offset from selector index to <see cref="_maps"/> index: the lobby row shifts
        /// the list by one.</summary>
        private int MapRowOffset => HasLobbyRow ? 1 : 0;

        /// <summary>Scratch buffer for option labels — shareable because
        /// <see cref="TMP_Dropdown.AddOptions(List{string})"/> copies into its own list.</summary>
        private readonly List<string> _optionScratch = new List<string>();

        private void RebuildModeOptions()
        {
            _optionScratch.Clear();
            for (int i = 0; i < _modes.Count; i++)
            {
                _optionScratch.Add(DisplayOf(_modes[i].DisplayName, _modes[i].ModeId));
            }

            FillDropdown(_modeDropdown, _optionScratch, NoModesLabel);
            SyncDropdown(_modeDropdown, _modeIndex, _modes.Count);
        }

        private void RebuildMapOptions()
        {
            _optionScratch.Clear();
            if (HasLobbyRow)
            {
                _optionScratch.Add(LobbyRowLabel);
            }

            for (int i = 0; i < _maps.Count; i++)
            {
                _optionScratch.Add(DisplayOf(_maps[i].DisplayName, _maps[i].SceneName));
            }

            FillDropdown(_mapDropdown, _optionScratch, NoMapsLabel);
            SyncMapDropdown();
        }

        /// <summary>Pulls the map selector to the local cursor; <see cref="SyncDropdown"/> cannot be
        /// used directly because the lobby row shifts the list.</summary>
        private void SyncMapDropdown()
        {
            int count = Mathf.Max(1, _maps.Count + MapRowOffset);

            if (_lobbyOpen && HasLobbyRow)
            {
                SyncDropdown(_mapDropdown, LobbyRowIndex, count);
                return;
            }

            if (_maps.Count == 0)
            {
                // Only the lobby row (or "no maps"): the cursor has one place to be.
                SyncDropdown(_mapDropdown, 0, count);
                return;
            }

            SyncDropdown(_mapDropdown, _mapIndex + MapRowOffset, count);
        }

        /// <summary>Writes the options. An empty list gets one explanatory row so the caption is
        /// never blank.</summary>
        private static void FillDropdown(TMP_Dropdown dropdown, List<string> options, string emptyLabel)
        {
            if (dropdown == null)
            {
                return;
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(options.Count > 0 ? options : new List<string> { emptyLabel });
        }

        /// <summary>Pulls a selector's cursor to the local index.
        /// <para>⚠️ Uses <see cref="TMP_Dropdown.SetValueWithoutNotify"/>: assigning <c>value</c>
        /// fires <c>onValueChanged</c>, so every <c>admin_state</c> refresh would emit a new
        /// <c>set_selection</c> and two admins would trigger each other forever.</para></summary>
        private static void SyncDropdown(TMP_Dropdown dropdown, int index, int count)
        {
            if (dropdown == null)
            {
                return;
            }

            int target = count > 0 ? Mathf.Clamp(index, 0, count - 1) : 0;
            if (dropdown.value != target)
            {
                dropdown.SetValueWithoutNotify(target);
            }
        }

        /// <summary>Opens the selected arena on this screen immediately — an OPTIMISTIC load.
        /// <para>⚠️ This is not "admin only": the server stages the picked map to every client
        /// (§10.7). The local load only hides the latency (and is the only path in a serverless dev
        /// session); when the server names the same scene <see cref="SceneRouter"/> sees we are
        /// already there and does not reload.</para>
        /// <para>Skipped while a match runs (the server owns scene authority) and ⚠️ while the lobby
        /// is open, where previewing an arena would drop this operator alone into it.</para>
        /// </summary>
        private void PreviewSelectedMap()
        {
            if (_lobbyOpen || _mapIndex < 0 || _mapIndex >= _maps.Count || !CanChangeSelection)
            {
                return;
            }

            string sceneName = _maps[_mapIndex].SceneName;
            if (SceneRouter.Instance == null || string.IsNullOrEmpty(sceneName))
            {
                return;
            }

            SceneRouter.Instance.LoadPreview(sceneName);
            AdminCommands.Note($"Harita: {sceneName} (herkes yükleniyor; maç başlatılmadı)");
        }

        private static void PrevMarkers() { StepMarkers(-1); }
        private static void NextMarkers() { StepMarkers(1); }

        private static void StepMarkers(int delta)
        {
            var next = (int)AdminSession.Markers + delta;
            if (next < 0) next = 2;
            if (next > 2) next = 0;
            AdminSession.Markers = (AdminMarkerVisibility)next;
        }

        private static void ToggleNameplates()
        {
            AdminSession.Nameplates = !AdminSession.Nameplates;
        }

        private static void ToggleViolationSound()
        {
            AdminSession.ViolationSound = !AdminSession.ViolationSound;
        }

        private static void SpeedDown() { AdminSession.FreeSpeed -= 0.5f; }
        private static void SpeedUp() { AdminSession.FreeSpeed += 0.5f; }

        private static void PrevRoof() { StepRoof(-1); }
        private static void NextRoof() { StepRoof(1); }

        /// <summary>Roof mode: visible → hidden in top view → always hidden (same pattern as the
        /// player rings).</summary>
        private static void StepRoof(int delta)
        {
            var next = (int)AdminSession.Roof + delta;
            if (next < 0) next = 2;
            if (next > 2) next = 0;
            AdminSession.Roof = (AdminRoofMode)next;
            AdminSpectator.RefreshRoof(); // let the preference show up immediately, do not wait for a mode change
        }

        // -------------------------------------------------------------- audio output

        /// <summary>First row: leave it to Windows. Stays at the top — devices come and go, so the
        /// end of the list shifts but the start does not.</summary>
        private const string AudioSystemDefaultLabel = "sistem varsayılanı";

        private const int AudioSystemDefaultRowIndex = 0;

        /// <summary>Row added when the saved device is not currently connected. The preference is
        /// kept (the headset may come back), so the state must be visible — silently snapping to
        /// "system default" would look like the choice was lost.</summary>
        private const string AudioMissingDeviceLabel = "seçili cihaz bağlı değil";

        /// <summary>Saved device missing from the list — a warning row is appended and the cursor
        /// sits on it.</summary>
        private bool _audioDeviceMissing;

        /// <summary>Re-reads the device list from Windows and rebuilds the options. Called on panel
        /// open and when entering GÖRÜNÜM, never per frame — endpoint enumeration is a COM
        /// call.</summary>
        private void RefreshAudioDeviceList()
        {
            WindowsAudioDevices.Collect(_audioDevices);

            string selected = AdminSession.AudioOutputDeviceId;
            _audioDeviceMissing = !string.IsNullOrEmpty(selected) && IndexOfAudioDevice(selected) < 0;

            _optionScratch.Clear();
            _optionScratch.Add(AudioSystemDefaultLabel);
            for (int i = 0; i < _audioDevices.Count; i++)
            {
                _optionScratch.Add(_audioDevices[i].Name);
            }

            if (_audioDeviceMissing)
            {
                _optionScratch.Add(AudioMissingDeviceLabel);
            }

            FillDropdown(_audioDeviceDropdown, _optionScratch, AudioSystemDefaultLabel);
            SyncAudioDeviceDropdown();
        }

        private int IndexOfAudioDevice(string id)
        {
            for (int i = 0; i < _audioDevices.Count; i++)
            {
                if (_audioDevices[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Pulls the cursor to the saved preference (row 0 = system default, then devices,
        /// then the "not connected" row if present).</summary>
        private void SyncAudioDeviceDropdown()
        {
            int count = 1 + _audioDevices.Count + (_audioDeviceMissing ? 1 : 0);
            string selected = AdminSession.AudioOutputDeviceId;

            if (string.IsNullOrEmpty(selected))
            {
                SyncDropdown(_audioDeviceDropdown, AudioSystemDefaultRowIndex, count);
                return;
            }

            if (_audioDeviceMissing)
            {
                SyncDropdown(_audioDeviceDropdown, count - 1, count);
                return;
            }

            int index = IndexOfAudioDevice(selected);
            SyncDropdown(_audioDeviceDropdown, index >= 0 ? index + 1 : AudioSystemDefaultRowIndex, count);
        }

        /// <summary>Audio output selected. Only writes the preference; touching Windows is the job
        /// of the <see cref="AdminSession.AudioOutputDeviceId"/> setter (single gate).
        /// <para>⚠️ The "not connected" row is informational: clicking it snaps the cursor back via
        /// <see cref="Apply"/> and changes no preference.</para></summary>
        private void SelectAudioDevice(int index)
        {
            HideDropdown(_audioDeviceDropdown);

            if (index == AudioSystemDefaultRowIndex)
            {
                AdminSession.AudioOutputDeviceId = "";
                RefreshAudioDeviceList();
                Apply();
                return;
            }

            int deviceIndex = index - 1;
            if (deviceIndex < 0 || deviceIndex >= _audioDevices.Count)
            {
                Apply(); // the "bağlı değil" row or a stale cursor - let the dropdown revert to its own value
                return;
            }

            AdminSession.AudioOutputDeviceId = _audioDevices[deviceIndex].Id;
            RefreshAudioDeviceList();
            Apply();
        }

        // ------------------------------------------------------------------- refresh

        private void Apply()
        {
            if (_root == null)
            {
                return;
            }

            bool open = AdminSession.OpenPanel == AdminPanelKind.Preferences;
            if (_root.activeSelf != open)
            {
                _root.SetActive(open);

                // Re-read audio devices on every panel OPEN: the list changes during a session
                // (headset plugged in, HDMI display wakes). Nothing to do on close.
                if (open)
                {
                    RefreshAudioDeviceList();
                }
            }

            if (!open)
            {
                return;
            }

            // The visible text is the dropdown's own captionText (options were written in
            // RebuildModeOptions/RebuildMapOptions); only the cursor is synced here.
            SyncDropdown(_modeDropdown, _modeIndex, _modes.Count);
            SyncMapDropdown();

            ApplyTabs();
            ApplySelectionLock();
            ApplyScreenModeButton();
            ApplyQuitButton();

            // 0 = UI knows no value → the server uses the mode default.
            _durationValue.text = _roundSeconds > 0
                ? AdminCommands.FormatDuration(_roundSeconds)
                : "mod varsayılanı";
            // Three states: number · unlimited · mode default (0 = UI knows no value).
            _scoreLimitValue.text = AdminCommands.FormatScoreLimit(_scoreLimit);

            // An unbound field draws nothing; the rest of the panel keeps working.
            if (_countdownValue != null)
            {
                _countdownValue.text = _countdownSeconds > 0
                    ? $"{_countdownSeconds} sn"
                    : $"varsayılan ({ArenaProtocol.COUNTDOWN_SECONDS} sn)";
            }

            if (_friendlyFireValue != null)
            {
                // Read from the server, no local cursor: the switch can change mid-match, so "sent"
                // must not be shown as "in effect".
                bool friendlyFire = AdminSelection.FriendlyFire;
                _friendlyFireValue.text = friendlyFire ? "AÇIK" : "kapalı";
                // Highlighted when on: a match where teammates can be shot must not go unnoticed.
                _friendlyFireValue.color = friendlyFire ? UiKit.Bad : UiKit.Title;
            }

            ApplyCalibrationMode();

            _markersValue.text = AdminSession.Markers == AdminMarkerVisibility.Off ? "kapalı"
                : AdminSession.Markers == AdminMarkerVisibility.TopDownOnly ? "kuş bakışı" : "her zaman";
            _nameplatesValue.text = AdminSession.Nameplates ? "açık" : "kapalı";

            if (_violationSoundValue != null)
            {
                _violationSoundValue.text = AdminSession.ViolationSound ? "açık" : "kapalı";
            }

            _speedValue.text = $"{AdminSession.FreeSpeed:0.0} m/sn";
            _roofValue.text = AdminSession.Roof == AdminRoofMode.Visible ? "görünür"
                : AdminSession.Roof == AdminRoofMode.HideInTopDown ? "kuş bakışında gizli" : "hep gizli";

            ApplyAudioRows();

            SyncAudioDeviceDropdown();

            // Off Windows (and with no endpoints found) there is nothing to pick: a dropdown that
            // opens to one row leaves the operator wondering whether the click registered.
            SetInteractable(_audioDeviceDropdown,
                WindowsAudioDevices.Supported && _audioDevices.Count > 0);

            ArenaClient client = ArenaClient.Instance;
            string endpoint = AppSession.HasServerEndpoint
                ? $"{AppSession.ServerIp}:{AppSession.ServerPort}"
                : "adres yok (launcher'dan başlatılmalı)";
            string state = client == null ? "istemci yok"
                : client.IsConnected ? "bağlı"
                : client.State == ArenaConnectionState.Connecting ? "bağlanılıyor" : "bağlı değil";

            // Connected admin count: the operator must know they are not alone (all admins are
            // equally authoritative).
            string peers = AdminSelection.AdminCount > 1
                ? $" — {AdminSelection.AdminCount} admin bağlı"
                : "";
            _connectionText.text = $"{state} — {endpoint}{peers}";
        }

        /// <summary>Paints the window-mode button with the CURRENT mode: accented in fullscreen,
        /// dim when windowed (same language as the calibration-mode buttons). The label states the
        /// current mode, not what a click will do, and carries the F11 shortcut. The value comes
        /// from <see cref="AdminSession"/>, so F11 keeps the button correct via
        /// <c>Changed</c>.</summary>
        private void ApplyScreenModeButton()
        {
            bool full = AdminSession.FullScreen;

            if (_screenModeLabel != null)
            {
                _screenModeLabel.text = full ? "TAM EKRAN · F11" : "PENCERELİ · F11";
                _screenModeLabel.color = full ? UiKit.OnAccent : UiKit.Muted;
            }

            if (_screenModeButton != null && _screenModeButton.targetGraphic is Image image)
            {
                PaintButtonBackground(image, full, UiKit.Accent);
            }
        }

        /// <summary>Quit button: text and colour warn while the confirm window is open (same pattern
        /// as the destructive buttons in <see cref="AdminPlayerRow"/>).</summary>
        private void ApplyQuitButton()
        {
            if (_quitLabel == null)
            {
                return;
            }

            bool armed = _quitArmedAt >= 0f;
            _quitLabel.text = armed ? "EMİN? ÇIK" : "OYUNDAN ÇIK";
            _quitLabel.color = armed ? UiKit.OnAccent : UiKit.Bad;

            if (_quitButton != null && _quitButton.targetGraphic is Image image)
            {
                PaintButtonBackground(image, armed, UiKit.Bad, _buttonDangerSprite);
            }
        }

        /// <summary>Paints the calibration-mode buttons from the current value (§5.2). Read from the
        /// server, no local cursor: the command is immediate and another operator can change it, so
        /// "sent" must not be shown as "in effect". An empty value means <c>two_anchor</c> (the
        /// server's startup default).
        /// <para>⚠️ Fields are bound late in the prefab and read null-safely — a missing binding must
        /// not stop the rest of the panel from drawing.</para></summary>
        private void ApplyCalibrationMode()
        {
            string mode = AdminSelection.CalibrationMode;
            if (string.IsNullOrEmpty(mode))
            {
                mode = ArenaProtocol.CALIB_MODE_TWO_ANCHOR;
            }

            PaintCalibModeButton(_calibModeTwoButton, _calibModeTwoLabel,
                mode == ArenaProtocol.CALIB_MODE_TWO_ANCHOR, true);
            PaintCalibModeButton(_calibModeSavedButton, _calibModeSavedLabel,
                mode == ArenaProtocol.CALIB_MODE_SAVED_ANCHOR, true);
            // Reserved option: disabled and dim on every refresh. Its label comes from the prefab;
            // the code writes no text, the colour carries the state.
            PaintCalibModeButton(_calibModeCloudButton, _calibModeCloudLabel, false, false);
        }

        private void PaintCalibModeButton(Button button, TextMeshProUGUI label,
            bool active, bool usable)
        {
            SetInteractable(button, usable);

            if (label != null)
            {
                label.color = !usable ? UiKit.Faint : active ? UiKit.OnAccent : UiKit.Muted;
            }

            if (button != null && button.targetGraphic is Image image)
            {
                PaintButtonBackground(image, active, UiKit.Accent);
            }
        }

        /// <summary>Paints the audio rows: level percentage, muted state and the mute button. Values
        /// come from <see cref="AdminSession"/>, so a change from anywhere keeps the row correct via
        /// <c>Changed</c>.</summary>
        private void ApplyAudioRows()
        {
            for (int i = 0; i < AudioMix.ChannelCount; i++)
            {
                var channel = (AudioChannel)i;
                bool muted = AdminSession.AudioMuted(channel);
                int percent = Mathf.RoundToInt(AdminSession.AudioLevel(channel) * 100f);

                TextMeshProUGUI value = At(_audioValues, i);
                if (value != null)
                {
                    // The stored level stays visible while muted: the stepper still moves it and the
                    // operator must see what unmuting will restore.
                    value.text = muted ? $"sessiz (%{percent})" : $"%{percent}";
                    value.color = muted ? UiKit.Faint : UiKit.Title;
                }

                TextMeshProUGUI label = At(_audioMuteLabels, i);
                if (label != null)
                {
                    label.text = muted ? "SESİ AÇ" : "SESSİZ";
                    label.color = muted ? UiKit.OnAccent : UiKit.Muted;
                }

                Button mute = At(_audioMuteButtons, i);
                if (mute != null && mute.targetGraphic is Image image)
                {
                    PaintButtonBackground(image, muted, UiKit.Accent);
                }
            }
        }

        /// <summary>Paints a button background by state — every button with a selected/idle
        /// distinction goes through here (tabs, calibration mode, window mode, quit).
        /// <para>The look stays in the prefab: backgrounds are bound to
        /// <see cref="_buttonIdleSprite"/> / <see cref="_buttonActiveSprite"/> and the code only
        /// picks one. With both bound the sprite swaps and the tint goes white; if either is empty
        /// it falls back to a flat colour tint, so a half binding never leaves a mis-tinted
        /// sprite.</para></summary>
        private void PaintButtonBackground(Image image, bool active, Color activeTint,
            Sprite activeSprite = null)
        {
            if (image == null)
            {
                return;
            }

            Sprite on = activeSprite != null ? activeSprite : _buttonActiveSprite;

            if (_buttonIdleSprite != null && on != null)
            {
                image.sprite = active ? on : _buttonIdleSprite;
                image.type = Image.Type.Sliced;
                image.color = Color.white;
                return;
            }

            image.color = active ? activeTint : CalibModeIdleFill;
        }

        /// <summary>Disables the mode/map rows while a match is set up (§10.7). Duration/limit stay
        /// enabled — they are next-match parameters and load no scene.</summary>
        private void ApplySelectionLock()
        {
            bool open = CanChangeSelection;

            // Disabled on an empty list too: a dropdown that only shows "no catalog" looks like
            // there is something to pick. The lobby row alone still counts as pickable.
            SetInteractable(_modeDropdown, open && _modes.Count > 0);
            SetInteractable(_mapDropdown, open && (_maps.Count > 0 || HasLobbyRow));

            Color valueColor = open ? UiKit.Title : UiKit.Faint;
            SetCaptionColor(_modeDropdown, valueColor);
            SetCaptionColor(_mapDropdown, valueColor);
        }

        /// <summary>Paints the tab bar and shows the active page — same language as the
        /// calibration-mode buttons (background via <see cref="PaintButtonBackground"/>). The arrays
        /// may be under-bound in the prefab, so all reads are null-safe.</summary>
        private void ApplyTabs()
        {
            var active = (int)_tab;

            for (int i = 0; i < _tabButtons.Length; i++)
            {
                if (_tabButtons[i] != null && _tabButtons[i].targetGraphic is Image image)
                {
                    PaintButtonBackground(image, i == active, UiKit.Accent);
                }

                if (i < _tabLabels.Length && _tabLabels[i] != null)
                {
                    _tabLabels[i].color = i == active ? UiKit.OnAccent : UiKit.Muted;
                }
            }

            for (int i = 0; i < _tabPages.Length; i++)
            {
                if (_tabPages[i] != null && _tabPages[i].activeSelf != (i == active))
                {
                    _tabPages[i].SetActive(i == active);
                }
            }
        }

        /// <summary>Shared by buttons and dropdowns (<see cref="Selectable"/> is the base of both).</summary>
        private static void SetInteractable(Selectable selectable, bool value)
        {
            if (selectable != null)
            {
                selectable.interactable = value;
            }
        }

        /// <summary>Dims a dropdown's caption. <see cref="Selectable"/>'s own <c>disabledColor</c>
        /// only tints the background, so without this a locked row still looks enabled.</summary>
        private static void SetCaptionColor(TMP_Dropdown dropdown, Color color)
        {
            if (dropdown != null && dropdown.captionText != null)
            {
                dropdown.captionText.color = color;
            }
        }

        private static string DisplayOf(string displayName, string fallback)
        {
            return string.IsNullOrEmpty(displayName) ? fallback : displayName;
        }
    }
}
