using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Ffa
{
    /// <summary>PRESENTATION component at the root of the FFA HUD prefab; adds only what is FFA's:
    /// individual score line, winning player, top three standings.</summary>
    /// <remarks>Phase/time, countdown, health, death screen, kill feed and own counters come from
    /// <see cref="ModeHudBase"/>. No team color/column — teams are never drawn here (§10.5
    /// <c>teamMode:"none"</c>). Score is read from <c>lobby_state</c>, not <c>match_state</c>: it is
    /// carried in <c>PlayerInfo.score</c> (§10.2), and <c>scoreRed/scoreBlue</c> are always 0 in this
    /// mode.</remarks>
    public class FfaClientController : ModeHudBase
    {
        /// <summary>Maximum number of lines to show in the standings.</summary>
        private const int StandingsLines = 3;

        [Header("FFA — sıralama")]
        [Tooltip("İlk üç sıralama alanı (ad · skor). Atanmazsa çizilmez.")]
        [SerializeField] private TMP_Text standingsText;

        /// <summary>Score line from the last <c>lobby_state</c>; cached so 1 Hz <c>match_state</c>
        /// does not recompute it.</summary>
        private string _scoreLine = "";

        /// <summary>Standings buffers — reused, drawn several times per second.</summary>
        private readonly List<PlayerInfo> _ranked = new List<PlayerInfo>();
        private readonly StringBuilder _sb = new StringBuilder();

        /// <summary>The standings field is not in the base, so clearing it on return to lobby lives
        /// here too — the base clears its own fields and cannot know ours.</summary>
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
            _ranked.Clear();
            _scoreLine = "";
            SetText(standingsText, "");
        }

        protected override string ScoreLine(MatchStateMsg msg)
        {
            return _scoreLine;
        }

        protected override string EndScoreLine(MatchEndMsg msg)
        {
            return _scoreLine;
        }

        protected override string WinnerLine(MatchEndMsg msg)
        {
            // In modes with individual scores the winner arrives via winnerPlayerId (§5.3); 0 = draw.
            if (msg.winnerPlayerId <= 0)
            {
                return "BERABERE";
            }

            int self = SelfPlayerId;
            return self != 0 && msg.winnerPlayerId == self
                ? "KAZANDIN"
                : $"{NameOf(msg.winnerPlayerId)} KAZANDI";
        }

        /// <summary>Roster refreshed: individual scores live here (§10.2), feeding score line and
        /// standings.</summary>
        protected override void OnLobbyStateApplied(LobbyStateMsg msg)
        {
            RankPlayers(msg);
            _scoreLine = BuildScoreLine();
            SetText(scoreText, _scoreLine);
            SetText(standingsText, BuildStandings());
        }

        // ---------------------------------------------------------------- internals

        /// <summary>Local player's id; 0 if not connected.</summary>
        private static int SelfPlayerId =>
            PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;

        /// <summary>Sorts players only (admins are filtered by <c>role</c>) by descending score;
        /// ties break on lower playerId (deterministic).</summary>
        private void RankPlayers(LobbyStateMsg msg)
        {
            _ranked.Clear();
            if (msg?.players == null)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info != null && info.role != "admin")
                {
                    _ranked.Add(info);
                }
            }

            _ranked.Sort(CompareByScore);
        }

        private static int CompareByScore(PlayerInfo a, PlayerInfo b)
        {
            int byScore = b.score.CompareTo(a.score);
            return byScore != 0 ? byScore : a.playerId.CompareTo(b.playerId);
        }

        /// <summary>"SEN 7 · LİDER 9 (ertu)" — the second part is dropped if we are the leader.</summary>
        private string BuildScoreLine()
        {
            if (_ranked.Count == 0)
            {
                return "";
            }

            int self = SelfPlayerId;
            PlayerInfo leader = _ranked[0];
            int selfScore = 0;
            bool selfFound = false;

            for (int i = 0; i < _ranked.Count; i++)
            {
                if (_ranked[i].playerId != self)
                {
                    continue;
                }

                selfScore = _ranked[i].score;
                selfFound = true;
                break;
            }

            // Admin spectator or a client without an id yet: show only the leader.
            if (!selfFound)
            {
                return $"LİDER {leader.score} ({NameOf(leader.playerId)})";
            }

            return leader.playerId == self
                ? $"SEN {selfScore} · LİDERSİN"
                : $"SEN {selfScore} · LİDER {leader.score} ({NameOf(leader.playerId)})";
        }

        /// <summary>Top three: "1. Name 9" (descending). Empty if there is nobody.</summary>
        private string BuildStandings()
        {
            if (standingsText == null || _ranked.Count == 0)
            {
                return "";
            }

            _sb.Clear();
            int count = Mathf.Min(StandingsLines, _ranked.Count);
            for (int i = 0; i < count; i++)
            {
                PlayerInfo info = _ranked[i];
                _sb.AppendLine($"{i + 1}. {NameOf(info.playerId)} {info.score}");
            }

            return _sb.ToString();
        }
    }
}
