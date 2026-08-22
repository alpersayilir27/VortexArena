using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Tournament
{
    /// <summary>Between-rounds regroup reporting: sends <c>set_ready{true}</c> on entering the own
    /// base zone and <c>set_ready{false}</c> on leaving; the server starts the new round's countdown
    /// once everyone is ready (§10.1 "round based modes").</summary>
    /// <remarks>
    /// <para>Reporting continues through the countdown too: the rule is "WAIT in the base", not "drop
    /// by" — one player leaving delays the round (the server cancels and returns to regroup). So this
    /// runs in <c>paused/mode</c> and the <c>paused/countdown</c> that follows it, but not in the
    /// match's FIRST countdown.</para>
    /// <para>A player who dies mid-round is guided too (no revive — <c>reviveAnchor:none</c>): the
    /// prompt sends them to the base early, which shortens the regroup. ⚠️ <c>set_ready</c> is NOT
    /// sent on that path — the flag's regroup meaning (§10.1) must not be polluted mid-round, and the
    /// server resets the flags on entering the regroup anyway.</para>
    /// <para>Base entry is also confirmed by hand: three pulses on both controllers
    /// (<see cref="ControllerHaptics"/>) on the ENTRY edge, so the eye need not be on the prompt.
    /// The criterion is the REAL zone entry, not the fail-open "counted as ready" — with no base
    /// found the vibration would be a lie.</para>
    /// <para>No new protocol message: the <c>ready</c> flag already means "I am ready" at the loading
    /// gate, and the operator sees who returned in the admin roster.</para>
    /// <para>Not in the HUD class because <see cref="Core.UI.ModeHudBase"/> is presentation only. On
    /// the same prefab root it still needs no scene setup and shares the HUD's lifetime: the HUD is
    /// instantiated only for <c>role=player</c>, so an admin never sends <c>set_ready</c>.</para>
    /// <para>"Am I in the base" is the client's decision — the server keeps the ledger rather than
    /// refereeing (§10.3, same contract as <c>reviveAnchor</c>); its safety net is its own
    /// timeout.</para>
    /// <para>Two texts, two altitudes: the HUD's status line carries the DETAILED instruction (which
    /// base, what happens next) and <see cref="ModeHudBase.SetCenterNotice"/> the one-line headline the
    /// player reads without looking for it. The headline shares its element with the countdown.</para>
    /// </remarks>
    public class TournamentRegroupReporter : MonoBehaviour
    {
        // Single instance so the DTO is not reallocated every frame.
        private readonly SetReadyMsg _msg = new SetReadyMsg();

        /// <summary>The HUD on the same prefab root — the centre notice's only drawer.</summary>
        private ModeHudBase _hud;

        /// <summary>Players still missing from their base, split by side (<c>lobby_state</c>). ⚠️ Only
        /// meaningful inside the regroup: outside it the server has cleared every <c>ready</c> flag,
        /// which would read as "everybody is missing".</summary>
        private int _teammatesMissing;

        /// <inheritdoc cref="_teammatesMissing"/>
        private int _opponentsMissing;

        /// <summary>Regroup reporting active (only <c>paused/mode</c> and its countdown);
        /// <c>set_ready</c> is sent only while set.</summary>
        private bool _active;

        private bool _reported;

        /// <summary>Are we writing the prompt (regroup OR in-round death guidance), so
        /// <see cref="Leave"/> clears only its own text.</summary>
        private bool _guiding;

        /// <summary>Was inside the own base last frame — for the entry-edge vibration.</summary>
        private bool _wasInsideBase;

        private void Awake()
        {
            _hud = GetComponent<ModeHudBase>();
            if (_hud == null)
            {
                // Loud on purpose: silently the flow still works and only the big text never appears —
                // a fault nobody notices until a match is running on the headsets.
                Debug.LogWarning("[Regroup] Aynı kökte ModeHudBase yok — merkez bildirimi çizilmeyecek.");
            }
        }

        private void OnEnable()
        {
            NetEvents.OnLobbyState += HandleLobbyState;
        }

        private void OnDisable()
        {
            NetEvents.OnLobbyState -= HandleLobbyState;

            // Scene/HUD going away: drop the prompt, else it stays stuck on the persistent singleton.
            Leave();
        }

        private void Update()
        {
            PlayerCombatState combat = PlayerCombatState.Instance;
            if (combat == null)
            {
                return;
            }

            bool paused = combat.Phase == ArenaProtocol.PHASE_PAUSED;

            // Only the mode sets the core mode pause (§10.1) — and we are the only mode running.
            bool modePause = paused && combat.PhaseReason == ArenaProtocol.PAUSE_REASON_MODE;
            bool countdown = paused && combat.PhaseReason == ArenaProtocol.PAUSE_REASON_COUNTDOWN;

            // Death within a round: no revive, the player waits dead until the server ends the round —
            // guided during that wait too (see the class doc).
            bool deadInRound = combat.Phase == ArenaProtocol.PHASE_PLAYING && !combat.IsAlive;

            if (modePause)
            {
                if (!_active)
                {
                    // The server clears ALL ready flags on entering the regroup (§10.1) — the local
                    // start state must match, so the first "I am in the base" is an EDGE.
                    _active = true;
                    _reported = false;
                }
            }
            else if (countdown && _active)
            {
                // ⚠️ Continue in a countdown ONLY if we came from a regroup. The match's FIRST
                // countdown is `paused/countdown` too, but has no regroup before it, so _active is
                // false and it falls to the branches below — nobody is called to a base there.
            }
            else if (deadInRound)
            {
                _active = false; // prompt yes, set_ready no (see the class doc)
            }
            else
            {
                Leave();
                return;
            }

            _guiding = true;

            // Base tracking is needed even when alive: in the regroup everyone returns to their base.
            combat.RequestBaseTracking();

            // No open base zone in the scene (incomplete setup): count as ready rather than lock the
            // player out.
            bool inBase = combat.IsInsideOwnBase || !combat.HasOpenBaseZone;

            // ⚠️ The prompt must name the team: the player has no other way to learn it (no own body,
            // the score line shows both teams, the strips are at opposite ends). Without the name they
            // walk to the NEAREST strip; if that is the opponent's base the gate never opens and the
            // server just shows "waiting for regroup" — no error, no reason.
            string baseName = BaseLabel(combat.Team);
            if (_active)
            {
                combat.SetModePrompt(countdown
                    ? (inBase ? "Tur başlıyor — tabandan çıkma" : $"{baseName} dön — geri sayım iptal oluyor")
                    : (inBase ? "Tabandasın — diğerleri bekleniyor" : $"Yeni tur — {baseName} dön"));
            }
            else
            {
                combat.SetModePrompt(inBase
                    ? "Tabandasın — turun bitmesini bekle"
                    : $"Öldün — {baseName} dön, yeni tur orada başlayacak");
            }

            SetNotice(CenterNotice(inBase));

            // Base ENTRY edge: three pulses on both controllers (criterion is the REAL entry).
            bool insideBase = combat.IsInsideOwnBase;
            if (insideBase && !_wasInsideBase)
            {
                ControllerHaptics.PulseBoth(this);
            }
            _wasInsideBase = insideBase;

            if (!_active)
            {
                return; // in-round death guidance: nothing to report
            }

            // ⚠️ Sent only on the EDGE, never periodically: each set_ready triggers a FULL lobby_state
            // broadcast (fan-out × player count). The WS/TCP channel does not lose it, so no repeat.
            if (inBase == _reported)
            {
                return;
            }

            _reported = inBase;
            _msg.ready = inBase;
            ArenaClient.Instance?.Send(_msg);

            // The gate's full state on one line (edge only, so a few lines per round). Without it the
            // three causes of a "waiting for regroup" fault — wrong strip · base not found · flag
            // never reached the server — look identical on the headset.
            Debug.Log($"[Regroup] takım={combat.Team} kendiTabanında={combat.IsInsideOwnBase} " +
                      $"açıkTabanVar={combat.HasOpenBaseZone} → set_ready({inBase})");
        }

        /// <summary>The big centre line: WHAT is being waited for, in one glance. Empty = nothing to
        /// say — the countdown then takes the same element over (<see cref="ModeHudBase"/>).</summary>
        /// <remarks>Order is deliberate: the player's OWN duty comes first. Naming who else is missing
        /// while the player is still outside would send them looking at other people instead of
        /// walking.</remarks>
        private string CenterNotice(bool inBase)
        {
            if (!inBase)
            {
                return "BASE'E BEKLENİYORSUNUZ";
            }

            // Mid-round death: the round has not closed, so no one else is being called to a base and
            // the ready flags carry no answer (see _teammatesMissing).
            if (!_active)
            {
                return "";
            }

            if (_teammatesMissing > 0)
            {
                return "TAKIM ARKADAŞLARINIZ BASE'DE BEKLENİYOR";
            }

            return _opponentsMissing > 0 ? "RAKİP BASE'DE BEKLENİYOR" : "";
        }

        /// <summary>Roster refresh: how many are still out of their base, split by side. Fed by the SAME
        /// <c>ready</c> flag the server's gate counts (§10.1) — no second ledger, so the text can never
        /// disagree with the gate that actually opens the round.</summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            _teammatesMissing = 0;
            _opponentsMissing = 0;

            if (msg?.players == null)
            {
                return;
            }

            PlayerCombatState combat = PlayerCombatState.Instance;
            int selfId = combat != null ? combat.PlayerId : 0;
            string ownTeam = TeamWire(combat != null ? combat.Team : Team.Neutral);

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];

                // Same scope as the server's gate: connected PLAYERS only, and our own state is the
                // inBase flag, not a roster row.
                if (info == null || info.role == "admin" || info.playerId == selfId)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(info.connection) &&
                    info.connection != ArenaProtocol.CONNECTION_CONNECTED)
                {
                    continue;
                }

                if (info.ready)
                {
                    continue;
                }

                if (ownTeam.Length > 0 && info.team == ownTeam)
                {
                    _teammatesMissing++;
                }
                else
                {
                    _opponentsMissing++;
                }
            }
        }

        /// <summary>Team on the wire (§10.5); empty for <see cref="Team.Neutral"/> — a teamless player
        /// has no "own side", so everyone missing reads as an opponent.</summary>
        private static string TeamWire(Team team)
        {
            switch (team)
            {
                case Team.Red: return "red";
                case Team.Blue: return "blue";
                default: return "";
            }
        }

        private void SetNotice(string notice)
        {
            if (_hud != null)
            {
                _hud.SetCenterNotice(notice);
            }
        }

        /// <summary>Name of the base to head for ("KIRMIZI tabanına"); no color for
        /// <see cref="Team.Neutral"/>, since every base is open to them
        /// (<c>PlayerCombatState.EvaluateZones</c>).</summary>
        private static string BaseLabel(Team team)
        {
            switch (team)
            {
                case Team.Red: return "KIRMIZI tabanına";
                case Team.Blue: return "MAVİ tabanına";
                default: return "tabanına";
            }
        }

        /// <summary>Leaves the flow: clears the prompt so the next entry starts clean.</summary>
        private void Leave()
        {
            _active = false;
            _reported = false;
            _wasInsideBase = false;

            if (!_guiding)
            {
                return;
            }

            _guiding = false;
            PlayerCombatState.Instance?.SetModePrompt("");
            SetNotice("");
        }
    }
}
