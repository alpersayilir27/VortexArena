using System;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Tournament
{
    /// <summary>PRESENTATION component at the root of the Tournament HUD prefab; adds only what is
    /// the tournament's: round number, round score, alive count, regroup label and the banner for the
    /// round that just closed.</summary>
    /// <remarks>Everything else comes from <see cref="ModeHudBase"/>.
    /// <para>⚠️ Score means something else here: <c>scoreRed</c>/<c>scoreBlue</c> count rounds won,
    /// not kills (§10.5) — same number, different label, so the score line is not copied from
    /// TDM.</para>
    /// <para>The alive count is not in <c>match_state</c>; it is derived from
    /// <c>PlayerInfo.alive</c> + <c>team</c> in <c>lobby_state</c> (§10.2), which is already refreshed
    /// on every death.</para></remarks>
    public class TournamentClientController : ModeHudBase
    {
        [Header("Tur paneli")]
        [Tooltip("Can barının yanındaki tur/skor paneli (HealthHud içindeki örnek).")]
        [SerializeField] private TeamScorePanel roundScorePanel;
        [Tooltip("Can barının altındaki tur sonucu şeridi (HealthHud içindeki örnek).")]
        [SerializeField] private RoundResultBanner roundResultBanner;

        /// <summary>"3v2" from the last <c>lobby_state</c>; appended to the score line.</summary>
        private string _aliveLine = "";

        // Last known round score, so a roster refresh on death can redraw the line without waiting for
        // the 1 Hz match_state (a "3v2" arriving a second late misses the death).
        private int _scoreRed;
        private int _scoreBlue;

        /// <summary>The <c>roundend:…</c> token the banner has already shown. The same
        /// <c>match_state</c> can be rebroadcast (an operator pause landing inside the regroup) and the
        /// banner must not restart on a result already read — which is why the token carries the round
        /// NUMBER: without it two rounds won by the same team would be the same string and the second
        /// would be swallowed.</summary>
        private string _shownRoundEnd = "";

        /// <summary>The alive line is not in the base, so clearing it on return to lobby lives here
        /// too (same pattern as FFA's standings line).</summary>
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
            _aliveLine = "";
            _scoreRed = 0;
            _scoreBlue = 0;
            _shownRoundEnd = "";

            if (roundScorePanel != null)
            {
                roundScorePanel.Clear();
            }

            if (roundResultBanner != null)
            {
                roundResultBanner.Hide();
            }
        }

        protected override string ScoreLine(MatchStateMsg msg)
        {
            _scoreRed = msg.scoreRed;
            _scoreBlue = msg.scoreBlue;
            return BuildScoreLine();
        }

        protected override string EndScoreLine(MatchEndMsg msg)
        {
            // Match over: the alive count no longer means anything, only the round score.
            return TeamScore(msg.scoreRed, msg.scoreBlue);
        }

        protected override string WinnerLine(MatchEndMsg msg)
        {
            if (msg.winnerTeam == "red")
            {
                return "KIRMIZI KAZANDI";
            }

            return msg.winnerTeam == "blue" ? "MAVİ KAZANDI" : "BERABERE";
        }

        /// <summary>Writes "TUR n" instead of "MAÇ" in a running match (round number from
        /// <c>modeState</c>, §10.1); other phases are left to the base, keeping the phase/reason
        /// vocabulary in one place.</summary>
        protected override string PhaseLabel(string phase, string phaseReason, string modeState)
        {
            if (phase == ArenaProtocol.PHASE_PLAYING)
            {
                int round = ParseRound(modeState);
                return round > 0 ? $"TUR {round}" : "MAÇ";
            }

            return base.PhaseLabel(phase, phaseReason, modeState);
        }

        /// <summary>Mode pause (<c>phaseReason == "mode"</c>) = between-rounds regroup;
        /// <c>modeState</c> is <c>regroup:&lt;ready&gt;/&lt;total&gt;</c> (§10.1). The broadcast that
        /// OPENS that pause still carries <c>roundend:…</c> — one tick later the regroup state
        /// overwrites it.</summary>
        protected override string ModeStateLabel(string modeState)
        {
            string counts = ValueAfter(modeState, "regroup:");
            if (counts.Length > 0)
            {
                return $"TOPLANMA {counts}";
            }

            return ValueAfter(modeState, "roundend:").Length > 0 ? "TUR BİTTİ" : "TOPLANMA";
        }

        /// <summary>Roster refreshed: the alive count lives here (§10.2).</summary>
        protected override void OnLobbyStateApplied(LobbyStateMsg msg)
        {
            _aliveLine = BuildAliveLine(msg);
            SetText(scoreText, BuildScoreLine());
        }

        /// <summary>Feeds the two panels riding the health strip: the running round score, and the
        /// result of a round that has just closed.</summary>
        protected override void OnMatchStateApplied(MatchStateMsg msg)
        {
            if (roundScorePanel != null)
            {
                roundScorePanel.SetScore(msg.scoreRed, msg.scoreBlue);

                // Only `round:<n>` carries the number. During the regroup the heading deliberately KEEPS
                // the last round instead of blanking: an empty line where a number was reads as a fault.
                int round = ParseRound(msg.modeState);
                if (round > 0)
                {
                    roundScorePanel.SetRoundLabel($"TUR {round}");
                }
            }

            ApplyRoundEnd(msg.modeState);
        }

        // ---------------------------------------------------------------- internals

        /// <summary>The finished round's result (<c>roundend:&lt;kazanan&gt;:&lt;n&gt;</c>, §10.1).</summary>
        /// <remarks>⚠️ LATCHED, not polled: the token rides exactly one broadcast (the one that opens the
        /// regroup) and the next server tick overwrites it with <c>regroup:…</c>. Reading it off a timer
        /// would miss it.</remarks>
        private void ApplyRoundEnd(string modeState)
        {
            string value = ValueAfter(modeState, "roundend:");
            if (value.Length == 0 || value == _shownRoundEnd)
            {
                return;
            }

            _shownRoundEnd = value;

            if (roundResultBanner == null)
            {
                return;
            }

            int sep = value.IndexOf(':');
            string winner = sep >= 0 ? value.Substring(0, sep) : value;

            if (winner == "draw")
            {
                roundResultBanner.Show("TUR BERABERE", RoundOutcome.Draw);
                return;
            }

            string ownTeam = TeamWire(PlayerCombatState.Instance != null
                ? PlayerCombatState.Instance.Team
                : Team.Neutral);

            // No side of our own (a player the server has not put on a team yet): naming the winner is
            // the only honest line — "you won" would be a guess.
            if (ownTeam.Length == 0)
            {
                roundResultBanner.Show(winner == "red" ? "TURU KIRMIZI ALDI" : "TURU MAVİ ALDI",
                    RoundOutcome.Draw);
                return;
            }

            bool won = winner == ownTeam;
            roundResultBanner.Show(won ? "TUR KAZANILDI" : "TUR KAYBEDİLDİ",
                won ? RoundOutcome.Won : RoundOutcome.Lost);
        }

        /// <summary>Team on the wire (§10.5); empty for <see cref="Team.Neutral"/>.</summary>
        private static string TeamWire(Team team)
        {
            switch (team)
            {
                case Team.Red: return "red";
                case Team.Blue: return "blue";
                default: return "";
            }
        }

        private string BuildScoreLine()
        {
            string score = TeamScore(_scoreRed, _scoreBlue);
            return _aliveLine.Length > 0 ? $"{score}   ·   {_aliveLine}" : score;
        }

        private static string TeamScore(int scoreRed, int scoreBlue)
        {
            return $"KIRMIZI {scoreRed} — {scoreBlue} MAVİ";
        }

        /// <summary>"3v2" — red/blue players still alive. Empty if there is no player at all.</summary>
        private static string BuildAliveLine(LobbyStateMsg msg)
        {
            if (msg?.players == null)
            {
                return "";
            }

            int red = 0;
            int blue = 0;
            bool any = false;

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info == null || info.role == "admin")
                {
                    continue;
                }

                bool isRed = info.team == "red";
                bool isBlue = info.team == "blue";
                if (!isRed && !isBlue)
                {
                    continue;
                }

                any = true;
                if (!info.alive)
                {
                    continue;
                }

                if (isRed)
                {
                    red++;
                }
                else
                {
                    blue++;
                }
            }

            return any ? $"{red}v{blue}" : "";
        }

        /// <summary><c>round:4</c> → 4; 0 on a format mismatch (label falls back to "MAÇ").</summary>
        private static int ParseRound(string modeState)
        {
            string value = ValueAfter(modeState, "round:");
            return int.TryParse(value, out int round) ? round : 0;
        }

        /// <summary>Tail of a modeState shaped "<paramref name="prefix"/>…"; empty on mismatch.</summary>
        /// <remarks>The vocabulary belongs to the mode (§10.1): the core never parses these strings,
        /// the tournament both writes and reads them.</remarks>
        private static string ValueAfter(string modeState, string prefix)
        {
            if (string.IsNullOrEmpty(modeState) ||
                !modeState.StartsWith(prefix, StringComparison.Ordinal))
            {
                return "";
            }

            return modeState.Substring(prefix.Length);
        }
    }
}
