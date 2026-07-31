using System;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Tournament
{
    /// <summary>
    /// Turnuva HUD prefabının kökündeki SUNUM bileşeni. Faz/süre, geri sayım, can, ölüm ekranı,
    /// kill-feed ve kendi sayaçların ortak tabandan (<see cref="ModeHudBase"/>) gelir; bu sınıf
    /// yalnız turnuvaya ait olanı ekler: <b>tur numarası, tur skoru, ayakta kalan sayısı ve
    /// toplanma etiketi</b>.
    /// <para>
    /// ⚠️ <b>Skorun anlamı bu modda farklıdır:</b> <c>scoreRed</c>/<c>scoreBlue</c> öldürme değil
    /// <b>kazanılan tur</b> sayar (§10.5). Aynı sayı, farklı etiket — bu yüzden skor satırı TDM'den
    /// kopyalanmaz.
    /// </para>
    /// <para>
    /// Ayakta kalan sayısı <c>match_state</c>'te YOKTUR; <c>lobby_state</c>'teki
    /// <c>PlayerInfo.alive</c> + <c>team</c>'den türetilir (§10.2) ve roster her ölümde zaten
    /// tazelendiği için ayrı bir mesaja gerek kalmaz.
    /// </para>
    /// </summary>
    public class TournamentClientController : ModeHudBase
    {
        /// <summary>Son <c>lobby_state</c>'ten türetilen "3v2"; skor satırının kuyruğuna eklenir.</summary>
        private string _aliveLine = "";

        // Son bilinen tur skoru — roster ölümde tazelendiğinde satırı match_state'i beklemeden
        // yeniden çizebilmek için (match_state 1 Hz; "3v2" bir saniye geç gelirse ölüm anını kaçırır).
        private int _scoreRed;
        private int _scoreBlue;

        /// <summary>Ayakta kalan satırı tabanda YOKTUR (skor sunumu alt sınıfın işi), bu yüzden
        /// lobiye dönüşte onu temizlemek de burada: taban kendi alanlarını siler, bizimkini bilemez
        /// (FFA'daki sıralama satırıyla aynı desen).</summary>
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
            // Maç bitti: ayakta kalan sayısının anlamı kalmadı, yalnız tur skoru.
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

        /// <summary>
        /// Koşan maçta "MAÇ" yerine "TUR n" yazar (tur numarası <c>modeState</c>'ten, §10.1).
        /// Diğer tüm fazlar tabana bırakılır — faz/gerekçe sözlüğü orada tek yerde durur.
        /// </summary>
        protected override string PhaseLabel(string phase, string phaseReason, string modeState)
        {
            if (phase == ArenaProtocol.PHASE_PLAYING)
            {
                int round = ParseRound(modeState);
                return round > 0 ? $"TUR {round}" : "MAÇ";
            }

            return base.PhaseLabel(phase, phaseReason, modeState);
        }

        /// <summary>
        /// Mod duraklaması (<c>phaseReason == "mode"</c>) = turlar arası toplanma.
        /// <c>modeState</c> biçimi <c>regroup:&lt;hazır&gt;/&lt;toplam&gt;</c> (§10.1).
        /// </summary>
        protected override string ModeStateLabel(string modeState)
        {
            string counts = ValueAfter(modeState, "regroup:");
            return counts.Length > 0 ? $"TOPLANMA {counts}" : "TOPLANMA";
        }

        /// <summary>Roster tazelendi: ayakta kalan sayısı burada yaşıyor (§10.2).</summary>
        protected override void OnLobbyStateApplied(LobbyStateMsg msg)
        {
            _aliveLine = BuildAliveLine(msg);
            SetText(scoreText, BuildScoreLine());
        }

        // ---------------------------------------------------------------- iç işler

        private string BuildScoreLine()
        {
            string score = TeamScore(_scoreRed, _scoreBlue);
            return _aliveLine.Length > 0 ? $"{score}   ·   {_aliveLine}" : score;
        }

        private static string TeamScore(int scoreRed, int scoreBlue)
        {
            return $"KIRMIZI {scoreRed} — {scoreBlue} MAVİ";
        }

        /// <summary>"3v2" — kırmızı/mavi ayakta kalan. Hiç oyuncu yoksa boş.</summary>
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

        /// <summary><c>round:4</c> → 4; biçim tutmazsa 0 (etiket "MAÇ"a düşer).</summary>
        private static int ParseRound(string modeState)
        {
            string value = ValueAfter(modeState, "round:");
            return int.TryParse(value, out int round) ? round : 0;
        }

        /// <summary>"<paramref name="prefix"/>…" biçimindeki modeState'in kuyruğu; eşleşmezse boş.
        /// <para>Sözlük modun kendisine aittir (§10.1): çekirdek bu stringleri hiç ayrıştırmaz,
        /// yazan da okuyan da turnuvadır.</para></summary>
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
