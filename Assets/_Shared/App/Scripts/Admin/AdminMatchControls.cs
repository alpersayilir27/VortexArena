using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// The <b>match control bar</b> at the bottom center of the HUD: BAŞLAT · DURAKLAT/DEVAM ·
    /// BİTİR · İPTAL.
    ///
    /// <para>⚠️ <b>BİTİR and İPTAL are not the same button twice.</b> BİTİR finishes the match
    /// normally — result screen, scoreboard, the usual return to the lobby; İPTAL drops it and shows
    /// nothing. Both exist because a mode's own end condition may never fire (an unlimited tournament
    /// has no win limit and no round cap), and the operator should not have to pay the scoreboard to
    /// get out.</para>
    ///
    /// <para><b>Visuals come from the prefab</b> (<c>AdminHud.prefab</c> → <c>MatchBar</c>); this
    /// class only wires the buttons and switches their kind/label by phase
    /// (<see cref="UiButtonStyle"/>).</para>
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

        /// <summary>How long BİTİR stays armed after the first click (s).</summary>
        /// <remarks>⚠️ BİTİR is irreversible and its neighbour is the destructive İPTAL, so it asks twice:
        /// first click arms (label becomes "BİTİR?", surface goes amber), second click inside this window
        /// sends. Short on purpose — an armed button the operator has forgotten about is a trap, not a
        /// safeguard.</remarks>
        private const float EndArmSeconds = 3f;

        private const string LabelStart = "BAŞLAT";
        private const string LabelPause = "DURAKLAT";
        private const string LabelResume = "DEVAM";
        private const string LabelEnd = "BİTİR";
        private const string LabelEndArmed = "BİTİR?";
        private const string LabelAbort = "İPTAL";

        /// <summary>CSS <c>.mbar .btn .ic</c> — bigger than the builder's default for this height.</summary>
        private const float IconSize = 22f;

        /// <summary>CSS <c>.btn { gap }</c>.</summary>
        private const float IconGap = 8f;

        [Header("Seçim kaynağı")]
        [Tooltip("Mod/harita/süre seçimi ve lobi durumu bu panelde yaşar; BAŞLAT ona sorar. " +
                 "Boşsa Awake'te aynı canvas'ta aranır.")]
        [SerializeField] private AdminPreferencesPanel preferences;

        [Header("Düğmeler")]
        [SerializeField] private UiButtonStyle startButton;
        [Tooltip("DURAKLAT/DEVAM tek düğmedir: koşan maçta duraklatır, duraklatılmış maçta sürdürür.")]
        [SerializeField] private UiButtonStyle pauseButton;
        [Tooltip("BİTİR: maçı normal yoldan bitirir (sonuç ekranı çıkar). İPTAL'den farkı budur.")]
        [SerializeField] private UiButtonStyle endButton;
        [SerializeField] private UiButtonStyle abortButton;

        [Header("İkonlar")]
        [Tooltip("DURAKLAT/DEVAM düğmesinin ikon nesnesi — etiketle birlikte yeniden ortalanır.")]
        [SerializeField] private Image pauseIcon;
        [SerializeField] private Image startIcon;
        [SerializeField] private Image endIcon;
        [SerializeField] private Image abortIcon;
        [SerializeField] private Sprite playSprite;
        [SerializeField] private Sprite pauseSprite;

        [Header("Not")]
        [Tooltip("Şeridin üstündeki \"BİTİR: onaylamak için tekrar bas.\" satırı (CSS .mnote); " +
                 "yalnız BİTİR kurulu iken görünür.")]
        [SerializeField] private TextMeshProUGUI armedNote;

        private float _nextRefresh;
        private bool _dirty = true;

        /// <summary>When the armed BİTİR disarms itself; <c>&lt;= 0</c> = not armed.</summary>
        private float _endArmedUntil;

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
        /// (as in <see cref="AdminHud"/>): the commands are conditional (BAŞLAT refuses while the
        /// lobby is open, pause/resume differs by phase) and an inspector-wired call would skip
        /// those conditions.
        /// </summary>
        private void WireButtons()
        {
            Wire(startButton, StartMatch);
            Wire(pauseButton, TogglePause);
            Wire(endButton, EndMatch);
            Wire(abortButton, AdminCommands.AbortMatch);
        }

        private static void Wire(UiButtonStyle style, UnityEngine.Events.UnityAction action)
        {
            Button button = style != null ? style.TargetButton : null;
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

        /// <summary>Two-step BİTİR: the first click arms, a second one inside
        /// <see cref="EndArmSeconds"/> sends.</summary>
        /// <remarks>⚠️ Not the same weight as the other three: BİTİR declares a winner from the CURRENT
        /// score and throws away the round in progress, and the operator cannot take it back.</remarks>
        private void EndMatch()
        {
            if (!IsEndArmed)
            {
                _endArmedUntil = Time.unscaledTime + EndArmSeconds;
                AdminCommands.Note("BİTİR: onaylamak için tekrar bas.");
                _dirty = true;
                return;
            }

            _endArmedUntil = 0f;
            _dirty = true;
            AdminCommands.EndMatch();
        }

        private bool IsEndArmed => _endArmedUntil > 0f && Time.unscaledTime < _endArmedUntil;

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

        /// <summary>Switches the four buttons' kind, label and icon by phase (kit.html "MAÇ ŞERİDİ —
        /// FAZA GÖRE"). ⚠️ All fields are read null-safely so an unwired element does not take the
        /// rest of the bar down.</summary>
        private void Refresh()
        {
            AdminRoster roster = AdminRoster.Instance;

            // BAŞLAT: without the panel the gate is left open (the server rejects it anyway). Green
            // while it is the operator's next move, so the bar has exactly one obvious action.
            bool canStart = preferences == null || preferences.CanStartMatch;
            Apply(startButton, startIcon, LabelStart,
                canStart ? UiButtonKind.Go : UiButtonKind.Normal, canStart, null);

            // DURAKLAT/DEVAM: a single button; its label, icon and command come from the phase.
            bool paused = IsOperatorPaused;
            bool live = IsMatchLive;
            Apply(pauseButton, pauseIcon, paused ? LabelResume : LabelPause,
                paused ? UiButtonKind.Go : UiButtonKind.Normal, paused || live,
                paused ? playSprite : pauseSprite);

            // İPTAL returns to the lobby from any phase (abort_match, §10.1); nothing to abort when
            // already waiting in the lobby.
            bool inLobby = roster != null &&
                           roster.Phase == ArenaProtocol.PHASE_PAUSED &&
                           (string.IsNullOrEmpty(roster.PhaseReason) ||
                            roster.PhaseReason == ArenaProtocol.PAUSE_REASON_LOBBY);

            // BİTİR: a match that is running or paused can be finished; one already finished has its
            // result on screen and one in the lobby does not exist.
            bool canEnd = roster == null ||
                          (!inLobby && roster.Phase != ArenaProtocol.PHASE_FINISHED);
            if (!canEnd)
            {
                // A button that goes unusable while armed must not stay armed: the next time it lights
                // up, one click would end a different match.
                _endArmedUntil = 0f;
            }

            bool armed = IsEndArmed;
            Apply(endButton, endIcon, armed ? LabelEndArmed : LabelEnd,
                armed ? UiButtonKind.WarnFill : UiButtonKind.Normal, canEnd, null);

            Apply(abortButton, abortIcon, LabelAbort,
                inLobby ? UiButtonKind.Normal : UiButtonKind.Danger, !inLobby, null);

            if (armedNote != null && armedNote.gameObject.activeSelf != armed)
            {
                armedNote.gameObject.SetActive(armed);
            }
        }

        /// <summary>
        /// Applies one button's whole state. The icon+label block is re-centered because the label can
        /// change width mid-match (DURAKLAT → DEVAM, BİTİR → BİTİR?) while the box stays its CSS
        /// <c>min-width</c>.
        /// </summary>
        private static void Apply(UiButtonStyle style, Image icon, string label, UiButtonKind kind,
            bool interactable, Sprite iconSprite)
        {
            if (style == null)
            {
                return;
            }

            if (icon != null && iconSprite != null)
            {
                icon.sprite = iconSprite;
            }

            style.SetLabel(label);
            style.SetKind(kind);
            style.SetInteractable(interactable);
            Center(style, icon);
        }

        private static void Center(UiButtonStyle style, Image icon)
        {
            TMP_Text text = style.Label;
            if (text == null)
            {
                return;
            }

            var box = (RectTransform)style.transform;
            float width = box.rect.width;
            float height = box.rect.height;
            float labelWidth = Mathf.Ceil(text.GetPreferredValues(text.text).x);
            bool hasIcon = icon != null;
            float cursor = (width - (labelWidth + (hasIcon ? IconSize + IconGap : 0f))) * 0.5f;

            if (hasIcon)
            {
                RectTransform iconRect = icon.rectTransform;
                iconRect.sizeDelta = new Vector2(IconSize, IconSize);
                iconRect.anchoredPosition = new Vector2(cursor, 0f);
                cursor += IconSize + IconGap;
            }

            text.rectTransform.anchoredPosition = new Vector2(cursor, 0f);
            text.rectTransform.sizeDelta = new Vector2(labelWidth, height);
        }
    }
}
