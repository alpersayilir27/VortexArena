using System;
using TMPro;
using UnityEngine;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>PRESENTATION component at the root of the Zombie HUD prefab; adds only what is Zombie's:
    /// the wave counter, the standing-zombie count, the next-wave countdown and the co-op score line.</summary>
    /// <remarks>⚠️ <b>The serialized field names and types MIRROR <c>BurgerClientController</c> one to
    /// one</b> (that mode's HUD has the same shape: a big centre label + a shared/own score line + a
    /// clock), so an existing Burger HUD prefab can be reused by swapping the script — Unity carries a
    /// reference over when the field name AND type match. The cells mean something else here, which is
    /// what the tooltips say:
    /// <list type="bullet">
    /// <item><c>sharedScoreText</c> → squad total · <c>selfScoreText</c> → your own kills</item>
    /// <item><c>happyCountText</c> → wave number · <c>unhappyCountText</c> → zombies standing</item>
    /// <item><c>customerCountsText</c> → the same two in one line, for a HUD with a single box</item>
    /// </list>
    /// Burger's three clock-skin fields are deliberately NOT mirrored: the alarm colours belong to the
    /// diner palette and the time is drawn by the base.
    /// <para>Score is <c>scoring:"shared"</c> (§10.5): the squad total rides
    /// <c>match_state.scoreRed</c> (<c>scoreBlue</c> always 0) and your own kills ride
    /// <c>lobby_state → PlayerInfo.score</c> (§10.2) — there is no winner.</para>
    /// <para>The wave state rides <c>modeState</c> (<c>"w:3;left:5;next:0"</c>); the core never
    /// interprets that string (§10.1), the mode both writes and reads it.</para></remarks>
    public class ZombieClientController : ModeHudBase
    {
        [Header("Zombi — dalga sayaçları")]
        [Tooltip("Tek satırlık dalga/ayakta sayacı. Atanmazsa çizilmez.")]
        [SerializeField] private TMP_Text customerCountsText;

        [Header("Skor şeridi")]
        [Tooltip("Ekip toplamı hücresi.")]
        [SerializeField] private TMP_Text sharedScoreText;
        [Tooltip("Kendi öldürme sayın hücresi.")]
        [SerializeField] private TMP_Text selfScoreText;
        [Tooltip("DALGA numarası hücresi (Burger HUD'unda mutlu müşteri hücresiydi).")]
        [SerializeField] private TMP_Text happyCountText;
        [Tooltip("AYAKTA zombi hücresi (Burger HUD'unda mutsuz müşteri hücresiydi).")]
        [SerializeField] private TMP_Text unhappyCountText;

        /// <summary>Own kills from the last <c>lobby_state</c>; cached so the 1 Hz <c>match_state</c> can
        /// redraw the score line without a roster.</summary>
        private int _selfScore;

        /// <summary>Squad total from the last <c>match_state</c>; cached so a roster refresh can redraw
        /// the same line.</summary>
        private int _sharedTotal;

        private int _wave;
        private int _standing;
        private int _nextWaveSeconds;
        private bool _lost;

        /// <summary>The counter cells are not the base's fields, so clearing them on return to lobby lives
        /// here too (same pattern as the other co-op HUD).</summary>
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
            _wave = 0;
            _standing = 0;
            _nextWaveSeconds = 0;
            _lost = false;
            SetText(customerCountsText, "");
            SetCenterNotice("");
            BuildScoreLine();
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

        /// <summary>No winner in a co-op run (§10.5): the honest headline is how it ended — the clock was
        /// survived, or the whole squad went down.</summary>
        protected override string WinnerLine(MatchEndMsg msg)
        {
            SetCenterNotice("");
            return _lost ? "KAYBEDİLDİ — tüm oyuncular düştü" : $"DAYANILDI — dalga {Mathf.Max(1, _wave)}";
        }

        /// <summary>"DALGA 3" instead of "MAÇ" while playing; other phases are left to the base so the
        /// phase/reason vocabulary stays in one place.</summary>
        protected override string PhaseLabel(string phase, string phaseReason, string modeState)
        {
            if (phase == ArenaProtocol.PHASE_PLAYING && _wave > 0)
            {
                return $"DALGA {_wave}";
            }

            return base.PhaseLabel(phase, phaseReason, modeState);
        }

        /// <summary>The mode pauses itself when there is no object host and writes the REASON into
        /// <c>modeState</c> as plain Turkish (§10.5) — it is shown as it is, since it is written for the
        /// person reading it.</summary>
        protected override string ModeStateLabel(string modeState)
        {
            return string.IsNullOrEmpty(modeState) || modeState.IndexOf(':') >= 0
                ? base.ModeStateLabel(modeState)
                : modeState;
        }

        /// <summary>Roster refreshed: own kills live here (§10.2).</summary>
        protected override void OnLobbyStateApplied(LobbyStateMsg msg)
        {
            PlayerInfo self = FindSelf(msg);
            _selfScore = self != null ? self.score : 0;
            SetText(scoreText, BuildScoreLine());
        }

        /// <summary>Feeds the wave cells and the between-waves countdown from <c>modeState</c>.</summary>
        protected override void OnMatchStateApplied(MatchStateMsg msg)
        {
            ParseState(msg.modeState);

            SetText(happyCountText, _wave.ToString());
            SetText(unhappyCountText, _standing.ToString());
            SetText(customerCountsText, $"Dalga {_wave} · Ayakta {_standing}");

            // Between waves the big centre line is the only thing that explains why nothing is coming.
            bool playing = msg.phase == ArenaProtocol.PHASE_PLAYING;
            SetCenterNotice(playing && _nextWaveSeconds > 0
                ? $"DALGA {_wave + 1} — {_nextWaveSeconds}"
                : "");
        }

        // ---------------------------------------------------------------- internals

        private string BuildScoreLine()
        {
            SetText(sharedScoreText, _sharedTotal.ToString());
            SetText(selfScoreText, _selfScore.ToString());

            // Two lines on purpose: one line does not fit the HUD's score box.
            return $"ÖLDÜRDÜĞÜN: {_selfScore}\nEKİP: {_sharedTotal}";
        }

        /// <summary><c>"w:3;left:5;next:0"</c> (+ optional <c>over:lost</c>) → the cached cells.</summary>
        /// <remarks>⚠️ Tolerant on purpose: an empty, partial or malformed string must leave the counters
        /// where they were rather than break the HUD — the mode's contract can gain tokens later and an
        /// unknown token is skipped, not treated as an error. The pause reason rides the same field as
        /// free text, which simply parses to nothing.</remarks>
        private void ParseState(string modeState)
        {
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
                string raw = token.Substring(sep + 1).Trim();

                if (string.Equals(key, ZombieKinds.StateOver, StringComparison.Ordinal))
                {
                    _lost = string.Equals(raw, ZombieKinds.OverLost, StringComparison.Ordinal);
                    continue;
                }

                if (!int.TryParse(raw, out int value) || value < 0)
                {
                    continue;
                }

                if (string.Equals(key, ZombieKinds.StateWave, StringComparison.Ordinal))
                {
                    _wave = value;
                }
                else if (string.Equals(key, ZombieKinds.StateLeft, StringComparison.Ordinal))
                {
                    _standing = value;
                }
                else if (string.Equals(key, ZombieKinds.StateNext, StringComparison.Ordinal))
                {
                    _nextWaveSeconds = value;
                }
            }
        }
    }
}
