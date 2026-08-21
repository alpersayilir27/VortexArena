using UnityEngine;
using UnityEngine.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// The <b>match control bar</b> at the bottom center of the HUD: three icon buttons
    /// (▶ START · ⏸/▶ PAUSE-RESUME · ■ ABORT).
    ///
    /// <para><b>Visuals come from the prefab</b> (<c>AdminHud.prefab</c> → <c>MatchBar</c>); this
    /// class only wires, colors and disables the buttons by phase.</para>
    ///
    /// <para><b>Why separate:</b> the buttons live in the HUD's <b>persistent</b> layer (clickable
    /// while the panel is closed) but the selection state lives in the preferences panel. ⚠️ This
    /// bar starts via <see cref="AdminPreferencesPanel.StartSelectedMatch"/> and keeps NO selection
    /// field of its own — a second source would silently drift.</para>
    ///
    /// <para>⚠️ Disabling is only a UI gate against pointless clicks; authority is on the server, so
    /// while data is missing (<see cref="AdminRoster"/> null) the buttons stay ENABLED.</para>
    ///
    /// <para>Refresh is event driven plus a <see cref="RefreshInterval"/> safety tick: the panel's
    /// lobby flag updates after the scene command, so one event frame can mis-color a button.</para>
    /// </summary>
    public class AdminMatchControls : MonoBehaviour
    {
        /// <summary>Safety refresh interval (s) — same rhythm as <see cref="AdminHud"/>.</summary>
        private const float RefreshInterval = 0.25f;

        [Header("Seçim kaynağı")]
        [Tooltip("Mod/harita/süre seçimi ve lobi durumu bu panelde yaşar; BAŞLAT ona sorar. " +
                 "Boşsa Awake'te aynı canvas'ta aranır.")]
        [SerializeField] private AdminPreferencesPanel preferences;

        [Header("Düğmeler")]
        [SerializeField] private Button startButton;
        [SerializeField] private Image startIcon;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Image pauseIcon;
        [SerializeField] private Button abortButton;
        [SerializeField] private Image abortIcon;

        [Header("İkonlar")]
        [Tooltip("DURAKLAT/DEVAM ET tek düğmedir: koşan maçta pauseSprite, operatörün duraklattığı " +
                 "maçta playSprite gösterir.")]
        [SerializeField] private Sprite playSprite;
        [SerializeField] private Sprite pauseSprite;

        private float _nextRefresh;
        private bool _dirty = true;

        private void Awake()
        {
            if (preferences == null)
            {
                // ⚠️ No `??=`: Unity's fake-null makes a destroyed reference look non-null, so the
                // lookup would never run.
                preferences = transform.root.GetComponentInChildren<AdminPreferencesPanel>(true);
            }

            WireButtons();
        }

        /// <summary>
        /// Wires behaviour onto the prefab's buttons. ⚠️ <b>No persistent onClick in the prefab</b>
        /// (as in <see cref="AdminHud"/>): the commands are conditional (START refuses while the
        /// lobby is open, pause/resume differs by phase) and an inspector-wired call would skip
        /// those conditions.
        /// </summary>
        private void WireButtons()
        {
            Wire(startButton, StartMatch);
            Wire(pauseButton, TogglePause);
            Wire(abortButton, AdminCommands.AbortMatch);
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

        private void OnEnable()
        {
            NetEvents.OnConnectionStateChanged += HandleConnectionState;
            NetEvents.OnReturnToLobby += HandleOpenSceneChanged;
            NetEvents.OnLoadMatch += HandleOpenSceneChanged;
            NetEvents.OnConnected += HandleWelcome;

            if (AdminRoster.Instance != null)
            {
                AdminRoster.Instance.Changed += MarkDirty;
            }
        }

        private void OnDisable()
        {
            NetEvents.OnConnectionStateChanged -= HandleConnectionState;
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
            bool tick = Time.unscaledTime >= _nextRefresh;
            if (tick)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
            }

            if (_dirty || tick)
            {
                _dirty = false;
                Refresh();
            }
        }

        private void MarkDirty()
        {
            _dirty = true;
        }

        private void HandleConnectionState(ArenaConnectionState state)
        {
            _dirty = true;
        }

        private void HandleOpenSceneChanged(ReturnToLobbyMsg msg)
        {
            _dirty = true;
        }

        private void HandleOpenSceneChanged(LoadMatchMsg msg)
        {
            _dirty = true;
        }

        private void HandleWelcome(WelcomeMsg msg)
        {
            _dirty = true;
        }

        // ------------------------------------------------------------------- actions

        /// <summary>Starts the match with the panel's settings. Without the panel nothing is sent —
        /// mode and map live there and there is no default to invent.</summary>
        private void StartMatch()
        {
            if (preferences == null)
            {
                AdminCommands.Note("Tercihler paneli yok; maç başlatılamadı.");
                return;
            }

            preferences.StartSelectedMatch();
        }

        /// <summary>
        /// Pauses while <c>playing</c>, resumes an operator-paused match.
        /// <para>⚠️ Decided from the server's phase, not a local flag: with multiple admins somebody
        /// else may have paused, and a local flag would make the two screens contradict.</para>
        /// </summary>
        private static void TogglePause()
        {
            if (IsOperatorPaused)
            {
                AdminCommands.ResumeMatch();
            }
            else
            {
                AdminCommands.PauseMatch();
            }
        }

        /// <summary>Is the match running (§10.1: only <c>phase == playing</c>).</summary>
        private static bool IsMatchLive
        {
            get
            {
                AdminRoster roster = AdminRoster.Instance;
                return roster != null && roster.Phase == ArenaProtocol.PHASE_PLAYING;
            }
        }

        /// <summary>Was the match paused by the OPERATOR. ⚠️ Mode/countdown pauses do not count —
        /// the server does not lift those with <c>resume_match</c> (§5.2).</summary>
        private static bool IsOperatorPaused
        {
            get
            {
                AdminRoster roster = AdminRoster.Instance;
                return roster != null &&
                       roster.Phase == ArenaProtocol.PHASE_PAUSED &&
                       roster.PhaseReason == ArenaProtocol.PAUSE_REASON_OPERATOR;
            }
        }

        // ------------------------------------------------------------------ refresh

        /// <summary>Colors the three buttons by phase. ⚠️ All fields are read null-safely so an
        /// unwired element does not take the rest of the bar down.</summary>
        private void Refresh()
        {
            AdminRoster roster = AdminRoster.Instance;

            // START: without the panel the gate is left open (the server rejects it anyway).
            bool canStart = preferences == null || preferences.CanStartMatch;
            if (startButton != null)
            {
                startButton.interactable = canStart;
            }

            if (startIcon != null)
            {
                startIcon.color = canStart ? UiKit.Good : UiKit.Faint;
            }

            // PAUSE/RESUME: a single button, its icon and its command come from the phase.
            bool paused = IsOperatorPaused;
            bool live = IsMatchLive;
            if (pauseButton != null)
            {
                pauseButton.interactable = paused || live;
            }

            if (pauseIcon != null)
            {
                Sprite icon = paused ? playSprite : pauseSprite;
                if (icon != null)
                {
                    pauseIcon.sprite = icon;
                }

                pauseIcon.color = paused ? UiKit.Good : live ? UiKit.Title : UiKit.Faint;
            }

            // ABORT returns to the lobby from any phase (abort_match, §10.1); nothing to abort when
            // already waiting in the lobby.
            bool inLobby = roster != null &&
                           roster.Phase == ArenaProtocol.PHASE_PAUSED &&
                           (string.IsNullOrEmpty(roster.PhaseReason) ||
                            roster.PhaseReason == ArenaProtocol.PAUSE_REASON_LOBBY);
            if (abortButton != null)
            {
                abortButton.interactable = !inLobby;
            }

            if (abortIcon != null)
            {
                abortIcon.color = !inLobby ? UiKit.Bad : UiKit.Faint;
            }
        }
    }
}
