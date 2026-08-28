using System;
using TMPro;
using UnityEngine;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Mole
{
    /// <summary>PRESENTATION component at the root of the Mole HUD prefab; adds only what is Mole's: the
    /// team score line and the player's own right/wrong counters.</summary>
    /// <remarks>Phase and time come from <see cref="ModeHudBase"/>. No weapon/health/revive/kill-feed
    /// part is drawn — this mode has none (<c>weaponSource:"none"</c>, <c>reviveAnchor:"none"</c>): those
    /// base fields are simply left UNASSIGNED on the prefab and the base then draws nothing for them.
    /// <para>Score is <c>scoring:"team"</c> (§10.5): the team totals ride
    /// <c>match_state.scoreRed</c>/<c>scoreBlue</c> and the player's own contribution rides
    /// <c>lobby_state → PlayerInfo.score</c> (§10.2). ⚠️ The two are NOT each other's sum — the team
    /// score is floored at 0 on the server and the contribution is not.</para>
    /// <para>The hit counters ride <c>modeState</c> (<c>"p12:7/1;p13:5/0"</c>) — the core never
    /// interprets that string (§10.1), the mode both writes and reads it.</para></remarks>
    public class MoleClientController : ModeHudBase
    {
        [Header("Köstebek Ezme — vuruş sayaçları")]
        [Tooltip("Kendi doğru/yanlış vuruş ve katkı satırı. Atanmazsa çizilmez.")]
        [SerializeField] private TMP_Text hitCountsText;

        /// <summary>Own contribution from the last <c>lobby_state</c>; cached so a <c>match_state</c> can
        /// redraw the counter line without a roster.</summary>
        private int _selfScore;

        private int _correct;
        private int _wrong;

        /// <summary>The counter line is not the base's field, so clearing it on return to lobby lives
        /// here too (same pattern as Burger's counters).</summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            NetEvents.OnReturnToLobby += HandleReturnToLobbyLocal;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            NetEvents.OnReturnToLobby -= HandleReturnToLobbyLocal;
        }

        private void HandleReturnToLobbyLocal(ReturnToLobbyMsg _)
        {
            _selfScore = 0;
            _correct = 0;
            _wrong = 0;
            SetText(hitCountsText, "");
        }

        protected override string ScoreLine(MatchStateMsg msg) => TeamScore(msg.scoreRed, msg.scoreBlue);

        protected override string EndScoreLine(MatchEndMsg msg) => TeamScore(msg.scoreRed, msg.scoreBlue);

        protected override string WinnerLine(MatchEndMsg msg)
        {
            if (msg.winnerTeam == "red")
            {
                return "KIRMIZI KAZANDI";
            }

            return msg.winnerTeam == "blue" ? "MAVİ KAZANDI" : "BERABERE";
        }

        /// <summary>"OYUN" instead of "MAÇ" while playing; other phases are left to the base so the
        /// phase vocabulary stays in one place.</summary>
        protected override string PhaseLabel(string phase, string phaseReason, string modeState)
        {
            return phase == ArenaProtocol.PHASE_PLAYING
                ? "OYUN"
                : base.PhaseLabel(phase, phaseReason, modeState);
        }

        /// <summary>Roster refreshed: own contribution lives here (§10.2).</summary>
        protected override void OnLobbyStateApplied(LobbyStateMsg msg)
        {
            PlayerInfo self = FindSelf(msg);
            _selfScore = self != null ? self.score : 0;
            DrawCounts();
        }

        /// <summary>Feeds the player's own counters from <c>modeState</c>.</summary>
        protected override void OnMatchStateApplied(MatchStateMsg msg)
        {
            ParseSelfCounts(msg.modeState, LocalPlayerId, out _correct, out _wrong);
            DrawCounts();
        }

        // ---------------------------------------------------------------- internals

        private static string TeamScore(int scoreRed, int scoreBlue) => $"KIRMIZI {scoreRed} — {scoreBlue} MAVİ";

        private void DrawCounts()
        {
            SetText(hitCountsText, $"DOĞRU {_correct} · YANLIŞ {_wrong} · KATKI {_selfScore}");
        }

        /// <summary><c>"p12:7/1;p13:5/0"</c> → this player's 7 / 1.</summary>
        /// <remarks>⚠️ Tolerant on purpose: an empty, partial or malformed string must leave the counters
        /// at 0 rather than break the HUD — the mode's own contract can gain tokens later and an unknown
        /// one is skipped, not treated as an error.</remarks>
        private static void ParseSelfCounts(string modeState, int playerId, out int correct, out int wrong)
        {
            correct = 0;
            wrong = 0;

            if (string.IsNullOrEmpty(modeState) || playerId <= 0)
            {
                return;
            }

            string wanted = MoleKinds.ModeStatePlayerPrefix + playerId.ToString();

            string[] tokens = modeState.Split(';');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int sep = token.IndexOf(':');
                if (sep <= 0 || sep == token.Length - 1)
                {
                    continue;
                }

                if (!string.Equals(token.Substring(0, sep).Trim(), wanted, StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = token.Substring(sep + 1).Split('/');
                if (parts.Length != 2)
                {
                    return;
                }

                int.TryParse(parts[0].Trim(), out correct);
                int.TryParse(parts[1].Trim(), out wrong);
                return;
            }
        }
    }
}
