using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// The <b>team-agnostic</b> common base of the mode HUDs: phase/time, countdown, health, death
    /// overlay and status text, the big centre notice, kill feed, your own kill/death counter and the
    /// match end line. It is a presentation component — NO rules/authority, it computes nothing.
    ///
    /// <para><b>Nothing team-related lives here</b> (some modes have no teams): the score line, the
    /// team color and the team column are the subclass's job
    /// (<see cref="ScoreLine"/> / <see cref="WinnerLine"/>).</para>
    ///
    /// <para><b>Why in Core:</b> modes do not reference each other (CLAUDE.md). If the shared HUD code
    /// lived inside one mode, a second mode could not look at it and would rewrite the
    /// kill feed/health/countdown from scratch. Every mode asmdef already references Core.</para>
    ///
    /// <para>All UI bindings may be null (the prefab wiring may be incomplete); an unassigned field is
    /// silently not drawn.</para>
    /// </summary>
    public abstract class ModeHudBase : MonoBehaviour
    {
        [Header("Maç durumu")]
        [SerializeField] protected TMP_Text phaseText;
        [SerializeField] protected TMP_Text timeText;
        [Tooltip("Süre kutusunun kökü (arkaplan dahil) — süre YOKken gizlenir. Boş bırakılabilir.")]
        [SerializeField] protected GameObject timeFrame;
        [SerializeField] protected TMP_Text scoreText;

        [Header("Kill-feed")]
        [SerializeField] protected TMP_Text killFeedText;
        [Tooltip("Bir kill-feed satırının ekranda kalma süresi (saniye).")]
        [SerializeField] private float killFeedSeconds = 6f;
        [Tooltip("Aynı anda gösterilecek en fazla kill-feed satırı.")]
        [SerializeField] private int killFeedMaxLines = 5;

        [Header("Yerel oyuncu")]
        [SerializeField] protected TMP_Text healthText;
        [SerializeField] protected TMP_Text statusText;
        [SerializeField] protected GameObject deathOverlay;
        [Tooltip("Opsiyonel: ölüm ekranındaki katil satırı. Atanmazsa çizilmez.")]
        [SerializeField] protected TMP_Text deathKillerNameText;
        [Tooltip("Opsiyonel: ölüm ekranındaki durum/canlanma satırı — statusText'in kopyası.")]
        [SerializeField] protected TMP_Text deathStatusText;
        [Tooltip("Opsiyonel can barı (Image.type = Filled).")]
        [SerializeField] protected Image healthFill;
        [Tooltip("Opsiyonel: kendi öldürme/ölüm sayacın (lobby_state'ten). Atanmazsa çizilmez.")]
        [SerializeField] protected TMP_Text selfStatsText;
        [Tooltip("Ölüm ekranının açık kalma süresi (sn). 0 = canlanana kadar açık kalır.")]
        [SerializeField] private float deathOverlaySeconds;

        [Header("Merkez bildirimi")]
        [Tooltip("Opsiyonel: ekranın ortasındaki büyük bildirim kökü. Yazacak bir şey yokken kapanır.")]
        [SerializeField] protected GameObject centerNoticeRoot;
        [Tooltip("Opsiyonel: merkez bildiriminin metni. Geri sayım ve modun uyarısı AYNI elemanı kullanır.")]
        [SerializeField] protected TMP_Text centerNoticeText;
        [Tooltip("Opsiyonel: yalnız geri sayım boyunca açılan hafif karartma.")]
        [SerializeField] protected GameObject centerNoticeDim;

        /// <summary>A kill feed line + its expiry time (unscaled).</summary>
        private struct KillFeedLine
        {
            public string text;
            public float expireTime;
        }

        private readonly List<KillFeedLine> _killFeed = new List<KillFeedLine>();
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private readonly StringBuilder _sb = new StringBuilder();

        private PlayerCombatState _combat;

        /// <summary>The HUD's own canvas — while the match result overlay is open ONLY this component
        /// is disabled. ⚠️ The object itself is <b>not deactivated</b>: a deactivated object would
        /// unsubscribe from the network events in <c>OnDisable</c> and would never hear the message
        /// that would turn it back on (<c>load_match</c>).</summary>
        private Canvas _canvas;

        private string _combatStatus = "";
        private string _countdownLabel = "";
        private bool _countdownActive;
        private bool _killFeedDirty;

        /// <summary>The mode's own center notice (see <see cref="SetCenterNotice"/>).</summary>
        private string _modeNotice = "";

        /// <summary>Unscaled time the death screen closes at; negative = no timer running.</summary>
        private float _deathOverlayHideAt = -1f;

        // Who killed the local player, kept as an id (not a resolved name): a lobby_state arriving
        // later can still turn it into a real name. Cleared only on revive/lobby.
        private int _deathKillerId;
        private string _deathWeaponId = "";

        // ------------------------------------------------------------ subclass contract

        /// <summary>The score line — e.g. "KIRMIZI 5 — 3 MAVİ" or "SEN 7 · LİDER 9".
        /// <b>The notion of a team lives in the subclass, not here.</b></summary>
        protected abstract string ScoreLine(MatchStateMsg msg);

        /// <summary>The match end headline — e.g. "MAVİ KAZANDI" or "AHMET KAZANDI".</summary>
        protected abstract string WinnerLine(MatchEndMsg msg);

        /// <summary>The score line at match end; if it returns <c>null</c> the score field is left at
        /// the value left over from the last <c>match_state</c>.</summary>
        protected virtual string EndScoreLine(MatchEndMsg msg) => null;

        /// <summary>The roster was refreshed — the leaderboard of modes with individual scoring is fed
        /// from here (<c>PlayerInfo.score</c>, §10.2). The base only resolves the names.</summary>
        protected virtual void OnLobbyStateApplied(LobbyStateMsg msg) { }

        /// <summary>The match state was applied (phase/time/score already drawn) — a mode's OWN extra
        /// panels are fed from here. Symmetric with <see cref="OnLobbyStateApplied"/>, and the only
        /// place a subclass gets to see <c>modeState</c> outside a label: the base never interprets
        /// that string (§10.1).</summary>
        protected virtual void OnMatchStateApplied(MatchStateMsg msg) { }

        // ------------------------------------------------------------- Unity lifecycle

        protected virtual void OnEnable()
        {
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnKillEvent += HandleKillEvent;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            GameplayHudGate.HiddenChanged += ApplyHudGate;

            TryBindCombat();
            ApplyHudGate(GameplayHudGate.Hidden);
        }

        protected virtual void OnDisable()
        {
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnKillEvent -= HandleKillEvent;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            GameplayHudGate.HiddenChanged -= ApplyHudGate;

            UnbindCombat();
        }

        /// <summary>The match result overlay opened/closed: the in-game HUD leaves the drawing or
        /// comes back (<see cref="GameplayHudGate"/>). It keeps writing its texts — so that when the
        /// overlay closes the HUD comes back already carrying up-to-date values.</summary>
        private void ApplyHudGate(bool hidden)
        {
            if (_canvas == null)
            {
                _canvas = GetComponent<Canvas>();
            }

            if (_canvas != null)
            {
                _canvas.enabled = !hidden;
            }
        }

        protected virtual void Start()
        {
            // The persistent singleton (PlayerCombatState) may bootstrap after the scene objects.
            TryBindCombat();
            RedrawKillFeed();
            RefreshStatusText();
            RefreshCenterNotice();
        }

        protected virtual void Update()
        {
            // Keep trying until subscribed; once subscribed, never bother again.
            if (_combat == null)
            {
                TryBindCombat();
            }

            TickDeathOverlay();
            AgeKillFeed();

            if (_killFeedDirty)
            {
                RedrawKillFeed();
            }
        }

        // --------------------------------------------------------- network event handlers

        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg == null || msg.players == null)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo p = msg.players[i];
                if (p == null || string.IsNullOrEmpty(p.name))
                {
                    continue;
                }

                _names[p.playerId] = p.name;
            }

            RefreshSelfStats(msg);
            OnLobbyStateApplied(msg);
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (msg.phaseReason != ArenaProtocol.PAUSE_REASON_COUNTDOWN && _countdownActive)
            {
                _countdownActive = false;
                _countdownLabel = "";
                RefreshStatusText();
                RefreshCenterNotice();
            }

            SetText(phaseText, PhaseLabel(msg.phase, msg.phaseReason, msg.modeState));
            SetTimeText(FormatTime(msg.timeRemaining));
            SetText(scoreText, ScoreLine(msg));

            OnMatchStateApplied(msg);
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (msg.seconds > 0)
            {
                _countdownActive = true;
                _countdownLabel = msg.seconds.ToString();
                SetText(phaseText, PhaseLabel(ArenaProtocol.PHASE_PAUSED,
                    ArenaProtocol.PAUSE_REASON_COUNTDOWN, ""));
            }
            else
            {
                _countdownActive = false;
                _countdownLabel = "";
            }

            RefreshStatusText();
            RefreshCenterNotice();
        }

        private void HandleKillEvent(KillEventMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            // ⚠️ The wording rule lives in KillFeedText, not here: the admin roster shows the same
            // events and a second copy drifts into a different answer for the same death.
            _killFeed.Add(new KillFeedLine
            {
                text = KillFeedText.Line(msg, NameOf),
                expireTime = Time.unscaledTime + Mathf.Max(0.5f, killFeedSeconds)
            });

            int maxLines = Mathf.Max(1, killFeedMaxLines);
            while (_killFeed.Count > maxLines)
            {
                _killFeed.RemoveAt(0);
            }

            _killFeedDirty = true;

            // The death screen's killer line. ⚠️ Written here and NOT in HandleAliveChanged: death
            // arrives on health_update (targeted) while this is a broadcast, so the two orders are
            // not guaranteed. Whichever comes second fills in the line.
            int self = LocalPlayerId;
            if (self != 0 && msg.victimId == self)
            {
                _deathKillerId = msg.killerId;
                _deathWeaponId = msg.weaponId ?? "";
                RefreshDeathLine();
            }
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            _countdownActive = false;
            _countdownLabel = "";
            _modeNotice = "";
            RefreshStatusText();
            RefreshCenterNotice();

            SetText(phaseText, WinnerLine(msg));
            SetTimeText("00:00");

            string score = EndScoreLine(msg);
            if (score != null)
            {
                SetText(scoreText, score);
            }
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg _)
        {
            _countdownActive = false;
            _countdownLabel = "";
            _modeNotice = "";
            _killFeed.Clear();
            _killFeedDirty = false;

            SetText(phaseText, "");
            SetTimeText("");
            SetText(scoreText, "");
            SetText(killFeedText, "");
            SetText(selfStatsText, "");
            RefreshStatusText();

            _deathOverlayHideAt = -1f;
            ShowDeathOverlay(false);
            RefreshCenterNotice();

            ClearDeathLine();
        }

        // ------------------------------------------ local health/status binding

        /// <summary>Subscribes once to the persistent PlayerCombatState singleton (may be null).</summary>
        private void TryBindCombat()
        {
            if (_combat != null)
            {
                return;
            }

            PlayerCombatState combat = PlayerCombatState.Instance;
            if (combat == null)
            {
                return;
            }

            _combat = combat;
            _combat.HpChanged += HandleHpChanged;
            _combat.AliveChanged += HandleAliveChanged;
            _combat.StatusChanged += HandleStatusChanged;

            // Apply the state from before the subscription once.
            HandleHpChanged(_combat.Hp);
            HandleAliveChanged(_combat.IsAlive);
            HandleStatusChanged(_combat.StatusText);
        }

        private void UnbindCombat()
        {
            if (_combat == null)
            {
                return;
            }

            _combat.HpChanged -= HandleHpChanged;
            _combat.AliveChanged -= HandleAliveChanged;
            _combat.StatusChanged -= HandleStatusChanged;
            _combat = null;
        }

        private void HandleHpChanged(float hp)
        {
            float clamped = Mathf.Clamp(hp, 0f, ArenaProtocol.PLAYER_MAX_HP);
            SetText(healthText, $"CAN {Mathf.RoundToInt(clamped)}");

            if (healthFill != null)
            {
                healthFill.fillAmount = ArenaProtocol.PLAYER_MAX_HP > 0f
                    ? clamped / ArenaProtocol.PLAYER_MAX_HP
                    : 0f;
            }
        }

        private void HandleAliveChanged(bool alive)
        {
            ShowDeathOverlay(!alive);

            if (alive)
            {
                _deathOverlayHideAt = -1f;
                ClearDeathLine();
                RefreshCenterNotice();
                return;
            }

            // ⚠️ Measured from DEATH, not from the killer line arriving: kill_event may never come
            // (environmental death) and the screen would then never close.
            _deathOverlayHideAt = deathOverlaySeconds > 0f
                ? Time.unscaledTime + deathOverlaySeconds
                : -1f;

            // ⚠️ The killer is NOT cleared here — kill_event may already have arrived (see
            // HandleKillEvent). With no killer yet the line opens as a fallback and is rewritten.
            RefreshDeathLine();
            RefreshCenterNotice();
        }

        /// <summary>Closes the death screen once <c>deathOverlaySeconds</c> is up, leaving the centre
        /// to the mode's notice. With no revive (<c>reviveAnchor:none</c>) the screen would otherwise
        /// stay until the round ends and cover everything the player is actually waiting for.</summary>
        private void TickDeathOverlay()
        {
            if (_deathOverlayHideAt < 0f || Time.unscaledTime < _deathOverlayHideAt)
            {
                return;
            }

            _deathOverlayHideAt = -1f;
            ShowDeathOverlay(false);
            RefreshCenterNotice();
        }

        private void ShowDeathOverlay(bool visible)
        {
            if (deathOverlay != null)
            {
                deathOverlay.SetActive(visible);
            }
        }

        private void HandleStatusChanged(string status)
        {
            _combatStatus = status ?? "";
            RefreshStatusText();
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>While the countdown is active the big number is shown, otherwise the combat status
        /// text. The death screen carries its own copy: the overlay covers the HUD's own status line,
        /// and the revive countdown lives in exactly that text.
        /// <para>Where a centre notice IS wired the number belongs to that (bigger) element instead,
        /// and this line keeps the mode's guidance — otherwise the guidance would be swallowed by the
        /// number exactly while it matters ("stay in the base").</para></summary>
        private void RefreshStatusText()
        {
            string text = _countdownActive && centerNoticeText == null ? _countdownLabel : _combatStatus;
            SetText(statusText, text);
            SetText(deathStatusText, text);
        }

        /// <summary>The mode's own big centre line (empty clears). ONE element carries all of it — the
        /// countdown and the mode's notice must not jump in size or place between states.
        /// <para>Priority: countdown &gt; mode notice. While the death screen is up the notice waits —
        /// the killer line is read first (<c>deathOverlaySeconds</c>).</para>
        /// <para>⚠️ Presentation only: WHAT to write is the mode's decision, never this class's — no
        /// <c>if (modeId == …)</c> here (§10.5).</para></summary>
        public void SetCenterNotice(string notice)
        {
            notice ??= "";
            if (notice == _modeNotice)
            {
                return;
            }

            _modeNotice = notice;
            RefreshCenterNotice();
        }

        /// <summary>Draws the centre notice; the dim belongs to the COUNTDOWN alone — a permanent veil
        /// over a free-roam player walking back to their base would be a hazard, not an emphasis.</summary>
        private void RefreshCenterNotice()
        {
            bool deathScreenUp = deathOverlay != null && deathOverlay.activeSelf;
            string text = _countdownActive
                ? _countdownLabel
                : (deathScreenUp ? "" : _modeNotice);

            SetText(centerNoticeText, text);

            if (centerNoticeRoot != null)
            {
                centerNoticeRoot.SetActive(text.Length > 0);
            }

            if (centerNoticeDim != null)
            {
                centerNoticeDim.SetActive(text.Length > 0 && _countdownActive);
            }
        }

        /// <summary>The death screen's killer line. A missing killer (environmental death, or a
        /// kill_event that has not arrived yet) is not an error — it has its own text.
        /// <para>⚠️ Same CLASSIFICATION as <see cref="KillFeedText"/> but not the same wording: the
        /// feed talks about a player in the third person, this talks TO them. Adding a cause on one
        /// side means adding it on the other.</para></summary>
        private void RefreshDeathLine()
        {
            if (deathKillerNameText == null)
            {
                return;
            }

            string line;
            if (_deathKillerId > 0 && _deathKillerId != LocalPlayerId)
            {
                line = $"{NameOf(_deathKillerId)} tarafından öldürüldün!";
            }
            else if (_deathKillerId > 0 && _deathKillerId == LocalPlayerId)
            {
                // Own blast: the server sends killerId == victimId (§10.3 gate 5, friendly fire on).
                line = "Kendini havaya uçurdun";
            }
            else if (string.Equals(_deathWeaponId, ArenaProtocol.WEAPON_ID_OBSTACLE))
            {
                line = "Engelde kaldın";
            }
            else
            {
                line = "Öldün";
            }

            SetText(deathKillerNameText, line);
        }

        private void ClearDeathLine()
        {
            _deathKillerId = 0;
            _deathWeaponId = "";
            SetText(deathKillerNameText, "");
        }

        /// <summary>Your own kill/death counter — server-authoritative (§10.2), not counted locally.</summary>
        private void RefreshSelfStats(LobbyStateMsg msg)
        {
            if (selfStatsText == null)
            {
                return;
            }

            PlayerInfo self = FindSelf(msg);
            SetText(selfStatsText, self == null ? "" : $"{self.kills} öldürme · {self.deaths} ölüm");
        }

        /// <summary>Our own player id; <c>0</c> until the connection is up.</summary>
        protected static int LocalPlayerId =>
            PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;

        /// <summary>Our own row in the roster; null if there is no id or it cannot be found.</summary>
        protected PlayerInfo FindSelf(LobbyStateMsg msg)
        {
            int playerId = LocalPlayerId;
            if (playerId == 0 || msg?.players == null)
            {
                return null;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                if (msg.players[i] != null && msg.players[i].playerId == playerId)
                {
                    return msg.players[i];
                }
            }

            return null;
        }

        private void AgeKillFeed()
        {
            float now = Time.unscaledTime;
            while (_killFeed.Count > 0 && now >= _killFeed[0].expireTime)
            {
                _killFeed.RemoveAt(0);
                _killFeedDirty = true;
            }
        }

        private void RedrawKillFeed()
        {
            _killFeedDirty = false;

            if (killFeedText == null)
            {
                return;
            }

            if (_killFeed.Count == 0)
            {
                killFeedText.text = "";
                return;
            }

            _sb.Clear();
            for (int i = 0; i < _killFeed.Count; i++)
            {
                _sb.AppendLine(_killFeed[i].text);
            }

            killFeedText.text = _sb.ToString();
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>playerId → name (from <c>lobby_state</c>); "Oyuncu N" if unknown.</summary>
        protected string NameOf(int playerId)
        {
            return _names.TryGetValue(playerId, out string name) && !string.IsNullOrEmpty(name)
                ? name
                : $"Oyuncu {playerId}";
        }

        protected static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        /// <summary>
        /// Converts the state (§10.1) into on-screen text. <b>The phase alone is not enough:</b> a
        /// state that appears as a single <c>paused</c> on the wire may be the lobby, but also
        /// loading/countdown/pause — the reason (<c>phaseReason</c>) tells them apart. The mode's own
        /// intermediate state (<c>modeState</c>, e.g. the tournament gathering) kicks in while the
        /// reason is <c>mode</c>; the base is not expected to interpret it, the subclass writes it via
        /// <see cref="ModeStateLabel"/>.
        /// <para>
        /// <c>virtual</c>: a round-based mode may want to write "TUR 3" instead of "MAÇ" for a running
        /// match. The subclass overrides only the branch it cares about and leaves the rest to
        /// <c>base</c> — so the phase/reason vocabulary stays in one place.
        /// </para>
        /// </summary>
        protected virtual string PhaseLabel(string phase, string phaseReason, string modeState)
        {
            if (phase == ArenaProtocol.PHASE_PLAYING) return "MAÇ";
            if (phase == ArenaProtocol.PHASE_FINISHED) return "MAÇ BİTTİ";

            switch (phaseReason)
            {
                case ArenaProtocol.PAUSE_REASON_LOBBY: return "LOBİ";
                case ArenaProtocol.PAUSE_REASON_LOADING: return "YÜKLENİYOR";
                case ArenaProtocol.PAUSE_REASON_COUNTDOWN: return "HAZIRLAN";
                case ArenaProtocol.PAUSE_REASON_OPERATOR: return "DURAKLATILDI";
                case ArenaProtocol.PAUSE_REASON_MODE: return ModeStateLabel(modeState);
                default: return "BEKLEME";
            }
        }

        /// <summary>
        /// The text to show when the mode requests a pause (<c>phaseReason == "mode"</c>). The base
        /// does NOT interpret <c>modeState</c> — only the mode itself knows its meaning (like
        /// "everyone return to base" in a tournament). If the subclass writes nothing, a neutral text
        /// is shown.
        /// </summary>
        protected virtual string ModeStateLabel(string modeState) => "BEKLEME";

        /// <summary>Draws the clock and hides its FRAME when there is no clock to draw.</summary>
        /// <remarks>The frame is a panel, not bare text: an empty one hanging over the health bar in
        /// the lobby reads as a broken HUD. Only the base knows when the value is gone, so only the
        /// base can pull the frame with it.</remarks>
        private void SetTimeText(string value)
        {
            SetText(timeText, value);
            if (timeFrame != null)
            {
                timeFrame.SetActive(!string.IsNullOrEmpty(value));
            }
        }

        protected static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
