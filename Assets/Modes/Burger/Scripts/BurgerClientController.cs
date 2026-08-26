using System;
using TMPro;
using UnityEngine;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Burger
{
    /// <summary>PRESENTATION component at the root of the Burger HUD prefab; adds only what is
    /// Burger's: the co-op score line and the happy/unhappy customer counters.</summary>
    /// <remarks>Phase/time come from <see cref="ModeHudBase"/>. No weapon/health/revive part is drawn
    /// — this mode has none (<c>weaponSource:"none"</c>, <c>reviveAnchor:"none"</c>): those base fields
    /// are simply left UNASSIGNED on the prefab and the base then draws nothing for them.
    /// <para>Score is <c>scoring:"shared"</c> (§10.5): the shared total rides
    /// <c>match_state.scoreRed</c> (<c>scoreBlue</c> always 0) and the player's own contribution rides
    /// <c>lobby_state → PlayerInfo.score</c> (§10.2).</para>
    /// <para>The customer counters ride <c>modeState</c> (<c>"h:3;u:1"</c>) — the core never
    /// interprets that string (§10.1), the mode both writes and reads it.</para></remarks>
    public class BurgerClientController : ModeHudBase
    {
        [Header("Hamburgerci — müşteri sayaçları")]
        [Tooltip("Mutlu/mutsuz müşteri sayacı satırı. Atanmazsa çizilmez.")]
        [SerializeField] private TMP_Text customerCountsText;

        /// <summary>Own contribution from the last <c>lobby_state</c>; cached so the 1 Hz
        /// <c>match_state</c> can redraw the score line without a roster.</summary>
        private int _selfScore;

        /// <summary>Shared total from the last <c>match_state</c>; cached so a roster refresh can
        /// redraw the same line.</summary>
        private int _sharedTotal;

        /// <summary>The counter line is not the base's field, so clearing it on return to lobby lives
        /// here too (same pattern as FFA's standings line).</summary>
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
            _sharedTotal = 0;
            SetText(customerCountsText, "");
        }

        protected override string ScoreLine(MatchStateMsg msg)
        {
            _sharedTotal = msg.scoreRed;
            return BuildScoreLine();
        }

        protected override string EndScoreLine(MatchEndMsg msg)
        {
            _sharedTotal = msg.scoreRed;
            return BuildScoreLine();
        }

        /// <summary>There is no winner in a co-op mode — the honest headline is the end of the
        /// shift; the score line beside it carries the result.</summary>
        protected override string WinnerLine(MatchEndMsg msg) => "VARDİYA BİTTİ";

        /// <summary>"VARDİYA" instead of "MAÇ" while playing; other phases are left to the base so the
        /// phase/reason vocabulary stays in one place.</summary>
        protected override string PhaseLabel(string phase, string phaseReason, string modeState)
        {
            return phase == ArenaProtocol.PHASE_PLAYING
                ? "VARDİYA"
                : base.PhaseLabel(phase, phaseReason, modeState);
        }

        /// <summary>Roster refreshed: own contribution lives here (§10.2).</summary>
        protected override void OnLobbyStateApplied(LobbyStateMsg msg)
        {
            PlayerInfo self = FindSelf(msg);
            _selfScore = self != null ? self.score : 0;
            SetText(scoreText, BuildScoreLine());
        }

        /// <summary>Feeds the customer counters from <c>modeState</c>.</summary>
        protected override void OnMatchStateApplied(MatchStateMsg msg)
        {
            if (customerCountsText == null)
            {
                return;
            }

            ParseCounts(msg.modeState, out int happy, out int unhappy);
            SetText(customerCountsText, $"Mutlu {happy} · Mutsuz {unhappy}");
        }

        // ---------------------------------------------------------------- internals

        private string BuildScoreLine()
        {
            return $"SEN {_selfScore} · TOPLAM {_sharedTotal}";
        }

        /// <summary><c>"h:3;u:1"</c> → 3 / 1.</summary>
        /// <remarks>⚠️ Tolerant on purpose: an empty, partial or malformed string must leave the
        /// counters at 0 rather than break the HUD — the mode's own contract can gain tokens later and
        /// an unknown token is skipped, not treated as an error.</remarks>
        private static void ParseCounts(string modeState, out int happy, out int unhappy)
        {
            happy = 0;
            unhappy = 0;

            if (string.IsNullOrEmpty(modeState))
            {
                return;
            }

            string[] tokens = modeState.Split(';');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int sep = token.IndexOf(':');
                if (sep <= 0 || sep == token.Length - 1)
                {
                    continue;
                }

                string key = token.Substring(0, sep).Trim();
                if (!int.TryParse(token.Substring(sep + 1).Trim(), out int value) || value < 0)
                {
                    continue;
                }

                if (string.Equals(key, "h", StringComparison.Ordinal))
                {
                    happy = value;
                }
                else if (string.Equals(key, "u", StringComparison.Ordinal))
                {
                    unhappy = value;
                }
            }
        }
    }
}
