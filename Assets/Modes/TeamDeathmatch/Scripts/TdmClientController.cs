using UnityEngine;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Tdm
{
    /// <summary>PRESENTATION component at the root of the TDM HUD prefab; adds only what is TDM's:
    /// team score line and winning team.</summary>
    /// <remarks>Everything else comes from <see cref="ModeHudBase"/>. The prefab bindings
    /// (<c>[SerializeField]</c>) moved to the base but kept their NAMES — Unity serializes fields by
    /// plain name, so <c>TdmHud.prefab</c>'s references still resolve.</remarks>
    public class TdmClientController : ModeHudBase
    {
        [Header("Takım skoru")]
        [Tooltip("Can barının yanındaki takım skoru paneli (HealthHud içindeki örnek).")]
        [SerializeField] private TeamScorePanel scorePanel;

        /// <summary>The panel is not the base's field, so clearing it on return to lobby lives here
        /// (same pattern as the tournament's and FFA's own lines).</summary>
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

        protected override string ScoreLine(MatchStateMsg msg)
        {
            return TeamScore(msg.scoreRed, msg.scoreBlue);
        }

        protected override string EndScoreLine(MatchEndMsg msg)
        {
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

        /// <summary>Feeds the strip beside the health bar. ⚠️ No round line here — TDM has no rounds,
        /// and an empty heading is the honest one.</summary>
        protected override void OnMatchStateApplied(MatchStateMsg msg)
        {
            if (scorePanel != null)
            {
                scorePanel.SetScore(msg.scoreRed, msg.scoreBlue);
            }
        }

        private void HandleReturnToLobbyLocal(ReturnToLobbyMsg _)
        {
            if (scorePanel != null)
            {
                scorePanel.Clear();
            }
        }

        private static string TeamScore(int scoreRed, int scoreBlue)
        {
            return $"KIRMIZI {scoreRed} — {scoreBlue} MAVİ";
        }
    }
}
