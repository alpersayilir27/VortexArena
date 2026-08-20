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
    /// overlay and status text, kill feed, your own kill/death counter and the match end line.
    /// It is a presentation component — NO rules/authority, it computes nothing.
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
        [Tooltip("Opsiyonel can barı (Image.type = Filled).")]
        [SerializeField] protected Image healthFill;
        [Tooltip("Opsiyonel: kendi öldürme/ölüm sayacın (lobby_state'ten). Atanmazsa çizilmez.")]
        [SerializeField] protected TMP_Text selfStatsText;

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
        }

        protected virtual void Update()
        {
            // Keep trying until subscribed; once subscribed, never bother again.
            if (_combat == null)
            {
                TryBindCombat();
            }

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
            }

            SetText(phaseText, PhaseLabel(msg.phase, msg.phaseReason, msg.modeState));
            SetText(timeText, FormatTime(msg.timeRemaining));
            SetText(scoreText, ScoreLine(msg));
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
        }

        private void HandleKillEvent(KillEventMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            string victim = NameOf(msg.victimId);
            // The kill feed text only uses characters that EXIST in the TMP font: LiberationSans SDF
            // (+ fallback) does not contain symbols like arrows or skulls, and a missing glyph is
            // drawn on screen as □.
            string line;
            if (msg.killerId > 0 && msg.killerId != msg.victimId)
            {
                line = $"{NameOf(msg.killerId)} -> {victim}";
            }
            else if (string.Equals(msg.weaponId, ArenaProtocol.WEAPON_ID_OBSTACLE))
            {
                // §10.9 environmental death: since killerId is 0 the branch above does not match.
                // It is a separate line for the sake of the operator/player distinction — "öldü" did
                // not tell a player melting inside a wall apart from a server error.
                line = $"{victim} engelde kaldı";
            }
            else
            {
                line = $"{victim} öldü";
            }

            _killFeed.Add(new KillFeedLine
            {
                text = line,
                expireTime = Time.unscaledTime + Mathf.Max(0.5f, killFeedSeconds)
            });

            int maxLines = Mathf.Max(1, killFeedMaxLines);
            while (_killFeed.Count > maxLines)
            {
                _killFeed.RemoveAt(0);
            }

            _killFeedDirty = true;
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            _countdownActive = false;
            _countdownLabel = "";
            RefreshStatusText();

            SetText(phaseText, WinnerLine(msg));
            SetText(timeText, "00:00");

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
            _killFeed.Clear();
            _killFeedDirty = false;

            SetText(phaseText, "");
            SetText(timeText, "");
            SetText(scoreText, "");
            SetText(killFeedText, "");
            SetText(selfStatsText, "");
            RefreshStatusText();

            if (deathOverlay != null)
            {
                deathOverlay.SetActive(false);
            }
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
            if (deathOverlay != null)
            {
                deathOverlay.SetActive(!alive);
            }
        }

        private void HandleStatusChanged(string status)
        {
            _combatStatus = status ?? "";
            RefreshStatusText();
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>While the countdown is active the big number is shown, otherwise the combat status
        /// text.</summary>
        private void RefreshStatusText()
        {
            SetText(statusText, _countdownActive ? _countdownLabel : _combatStatus);
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

        /// <summary>Our own row in the roster; null if there is no id or it cannot be found.</summary>
        protected PlayerInfo FindSelf(LobbyStateMsg msg)
        {
            int playerId = PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;
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

        protected static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
