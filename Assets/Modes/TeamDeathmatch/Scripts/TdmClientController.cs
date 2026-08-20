using VortexArena.Core.UI;
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

        private static string TeamScore(int scoreRed, int scoreBlue)
        {
            return $"KIRMIZI {scoreRed} — {scoreBlue} MAVİ";
        }
    }
}
