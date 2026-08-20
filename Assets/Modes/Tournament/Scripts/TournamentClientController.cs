using System;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Tournament
{
    /// <summary>PRESENTATION component at the root of the Tournament HUD prefab; adds only what is
    /// the tournament's: round number, round score, alive count, regroup label.</summary>
    /// <remarks>Everything else comes from <see cref="ModeHudBase"/>.
    /// <para>⚠️ Score means something else here: <c>scoreRed</c>/<c>scoreBlue</c> count rounds won,
    /// not kills (§10.5) — same number, different label, so the score line is not copied from
    /// TDM.</para>
    /// <para>The alive count is not in <c>match_state</c>; it is derived from
    /// <c>PlayerInfo.alive</c> + <c>team</c> in <c>lobby_state</c> (§10.2), which is already refreshed
    /// on every death.</para></remarks>
    public class TournamentClientController : ModeHudBase
    {
        /// <summary>"3v2" from the last <c>lobby_state</c>; appended to the score line.</summary>
        private string _aliveLine = "";

        // Last known round score, so a roster refresh on death can redraw the line without waiting for
        // the 1 Hz match_state (a "3v2" arriving a second late misses the death).
        private int _scoreRed;
        private int _scoreBlue;

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
        /// <c>modeState</c> is <c>regroup:&lt;ready&gt;/&lt;total&gt;</c> (§10.1).</summary>
        protected override string ModeStateLabel(string modeState)
        {
            string counts = ValueAfter(modeState, "regroup:");
            return counts.Length > 0 ? $"TOPLANMA {counts}" : "TOPLANMA";
        }

        /// <summary>Roster refreshed: the alive count lives here (§10.2).</summary>
        protected override void OnLobbyStateApplied(LobbyStateMsg msg)
        {
            _aliveLine = BuildAliveLine(msg);
            SetText(scoreText, BuildScoreLine());
        }

        // ---------------------------------------------------------------- internals

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
