using VortexArena.Core.UI;
using VortexArena.Protocol;

namespace VortexArena.Modes.Tdm
{
    /// <summary>
    /// TDM HUD prefabının kökündeki SUNUM bileşeni. Faz/süre, geri sayım, can, ölüm ekranı,
    /// kill-feed ve kendi sayaçların ortak tabandan (<see cref="ModeHudBase"/>) gelir; bu sınıf
    /// yalnız TDM'e ait olanı ekler: <b>takım skoru satırı ve takım kazananı</b>.
    /// <para>
    /// Prefab bağları (<c>[SerializeField]</c>) tabana taşındı ama alan ADLARI değişmedi —
    /// Unity alanları düz adla serialize ettiği için <c>TdmHud.prefab</c>'ın referansları aynen
    /// korunur.
    /// </para>
    /// </summary>
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
