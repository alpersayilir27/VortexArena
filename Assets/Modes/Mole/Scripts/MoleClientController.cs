using System;
using TMPro;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Mole
{
    /// <summary>PRESENTATION component at the root of the Mole HUD prefab; adds only what is Mole's: the
    /// team score panel and the player's own right/wrong counters beside it.</summary>
    /// <remarks>Time and status come from <see cref="ModeHudBase"/>. No health/death/kill-feed part is
    /// drawn — this mode has none; those base fields stay UNASSIGNED and the nested
    /// <c>HealthHud</c>'s bar is switched off on the instance.
    /// <para>Team totals ride <c>match_state.scoreRed</c>/<c>scoreBlue</c> (§10.5); the hit counters ride
    /// <c>modeState</c> (<c>"p12:7/1;p13:5/0"</c>) — the core never interprets that string (§10.1).</para></remarks>
    public class MoleClientController : ModeHudBase
    {
        private const string CorrectColor = "#3DDC5A";
        private const string WrongColor = "#FF4040";

        [Header("Takım skoru")]
        [Tooltip("Saatin altındaki takım skoru paneli (HealthHud içindeki örnek).")]
        [SerializeField] private TeamScorePanel scorePanel;

        [Header("Köstebek Ezme — vuruş sayaçları")]
        [Tooltip("Kırmızı skorun yanındaki D/Y yuvası; oyuncu kırmızıysa yazılır.")]
        [SerializeField] private TMP_Text redSideHitsText;
        [Tooltip("Mavi skorun yanındaki D/Y yuvası; oyuncu maviyse yazılır.")]
        [SerializeField] private TMP_Text blueSideHitsText;

        private int _correct;
        private int _wrong;

        protected override void OnEnable()
        {
            base.OnEnable();
            NetEvents.OnReturnToLobby += HandleReturnToLobbyLocal;
            PlayerCombatState.LocalTeamChanged += HandleLocalTeamChanged;
            DrawCounts();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            NetEvents.OnReturnToLobby -= HandleReturnToLobbyLocal;
            PlayerCombatState.LocalTeamChanged -= HandleLocalTeamChanged;
        }

        private void HandleReturnToLobbyLocal(ReturnToLobbyMsg _)
        {
            _correct = 0;
            _wrong = 0;
            DrawCounts();

            if (scorePanel != null)
            {
                scorePanel.Clear();
            }
        }

        private void HandleLocalTeamChanged(Team _) => DrawCounts();

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

        /// <summary>Feeds the score panel and the player's own counters.</summary>
        protected override void OnMatchStateApplied(MatchStateMsg msg)
        {
            if (scorePanel != null)
            {
                scorePanel.SetScore(msg.scoreRed, msg.scoreBlue);
            }

            ParseSelfCounts(msg.modeState, LocalPlayerId, out _correct, out _wrong);
            DrawCounts();
        }

        // ---------------------------------------------------------------- internals

        private static string TeamScore(int scoreRed, int scoreBlue) => $"KIRMIZI {scoreRed} — {scoreBlue} MAVİ";

        /// <summary>Counters go on the local team's side only; Neutral (not yet assigned) draws neither.</summary>
        private void DrawCounts()
        {
            Team team = ArenaCombat.LocalTeam;
            string counts = $"<color={CorrectColor}>D {_correct}</color>  <color={WrongColor}>Y {_wrong}</color>";

            SetText(redSideHitsText, team == Team.Red ? counts : "");
            SetText(blueSideHitsText, team == Team.Blue ? counts : "");
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
