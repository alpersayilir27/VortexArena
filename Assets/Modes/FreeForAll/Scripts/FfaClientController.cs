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
    /// <summary>
    /// Herkes Tek (FFA) HUD prefabının kökündeki SUNUM bileşeni. Faz/süre, geri sayım, can, ölüm
    /// ekranı, kill-feed ve kendi sayaçların ortak tabandan (<see cref="ModeHudBase"/>) gelir; bu
    /// sınıf yalnız FFA'ya ait olanı ekler: <b>bireysel skor satırı, kazanan oyuncu ve ilk üç
    /// sıralama</b>.
    /// <para>
    /// Takım rengi/kolonu YOKTUR — bu modda takım kavramı hiç çizilmez (§10.5
    /// <c>teamMode:"none"</c>). Skor <c>match_state</c>'ten değil <c>lobby_state</c>'ten okunur:
    /// bireysel skor <c>PlayerInfo.score</c> alanında taşınır (§10.2), <c>scoreRed/scoreBlue</c>
    /// bu modda hep 0'dır.
    /// </para>
    /// </summary>
    public class FfaClientController : ModeHudBase
    {
        /// <summary>Sıralamada gösterilecek en fazla satır.</summary>
        private const int StandingsLines = 3;

        [Header("FFA — sıralama")]
        [Tooltip("İlk üç sıralama alanı (ad · skor). Atanmazsa çizilmez.")]
        [SerializeField] private TMP_Text standingsText;

        /// <summary>Son <c>lobby_state</c>'ten türetilen skor satırı; <c>match_state</c> 1 Hz
        /// geldiğinde yeniden hesaplamamak için saklanır.</summary>
        private string _scoreLine = "";

        /// <summary>Sıralama tamponları — saniyede birkaç kez çizildiği için yeniden ayrılmaz.</summary>
        private readonly List<PlayerInfo> _ranked = new List<PlayerInfo>();
        private readonly StringBuilder _sb = new StringBuilder();

        /// <summary>Sıralama alanı tabanda YOKTUR (takım/skor sunumu alt sınıfın işidir), bu yüzden
        /// lobiye dönüşte onu temizlemek de burada: taban kendi alanlarını siler, bizimkini bilemez.</summary>
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
            // Kazanan bireysel skorlu modlarda winnerPlayerId ile gelir (§5.3); 0 = berabere.
            if (msg.winnerPlayerId <= 0)
            {
                return "BERABERE";
            }

            int self = SelfPlayerId;
            return self != 0 && msg.winnerPlayerId == self
                ? "KAZANDIN"
                : $"{NameOf(msg.winnerPlayerId)} KAZANDI";
        }

        /// <summary>Roster tazelendi: bireysel skorlar burada yaşıyor (§10.2), skor satırı ve
        /// sıralama buradan beslenir.</summary>
        protected override void OnLobbyStateApplied(LobbyStateMsg msg)
        {
            RankPlayers(msg);
            _scoreLine = BuildScoreLine();
            SetText(scoreText, _scoreLine);
            SetText(standingsText, BuildStandings());
        }

        // ---------------------------------------------------------------- iç işler

        /// <summary>Yerel oyuncunun kimliği; bağlanmadıysa 0.</summary>
        private static int SelfPlayerId =>
            PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;

        /// <summary>Yalnız oyuncuları (admin gözlemciler roster'da <c>role</c> ile ayrılır) skora
        /// göre azalan sıralar; eşit skorda düşük playerId önce gelir (deterministik).</summary>
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

        /// <summary>"SEN 7 · LİDER 9 (ertu)" — lider kendimizsek ikinci parça yazılmaz.</summary>
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

            // Gözlemci (admin) ya da henüz kimliği olmayan istemci: yalnız lideri göster.
            if (!selfFound)
            {
                return $"LİDER {leader.score} ({NameOf(leader.playerId)})";
            }

            return leader.playerId == self
                ? $"SEN {selfScore} · LİDERSİN"
                : $"SEN {selfScore} · LİDER {leader.score} ({NameOf(leader.playerId)})";
        }

        /// <summary>İlk üç: "1. Ad 9" (azalan). Kimse yoksa boş.</summary>
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
