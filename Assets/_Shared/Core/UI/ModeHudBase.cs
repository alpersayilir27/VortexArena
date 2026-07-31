using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// Mod HUD'larının <b>takım-agnostik</b> ortak tabanı: faz/süre, geri sayım, can, ölüm ekranı
    /// ve durum metni, kill-feed, kendi öldürme/ölüm sayacın ve maç sonu satırı.
    /// Sunum bileşenidir — kural/otorite YOK, hiçbir şey hesaplamaz.
    ///
    /// <para><b>Takım ile ilgili hiçbir şey burada DEĞİLDİR</b> (bazı modlarda takım yoktur):
    /// skor satırı, takım rengi ve takım kolonu alt sınıfın işidir
    /// (<see cref="ScoreLine"/> / <see cref="WinnerLine"/>).</para>
    ///
    /// <para><b>Neden Core'da:</b> modlar birbirini referanslamaz (CLAUDE.md). Ortak HUD kodu bir
    /// modun içinde dursaydı ikinci mod ona bakamaz, kill-feed/can/geri sayımı baştan yazardı.
    /// Core'u zaten her mod asmdef'i referanslıyor.</para>
    ///
    /// <para>Tüm UI bağları null olabilir (prefab kablolaması eksik olabilir); atanmamış alan
    /// sessizce çizilmez.</para>
    /// </summary>
    public abstract class ModeHudBase : MonoBehaviour
    {
        [Header("Maç durumu")]
        [SerializeField] protected TMP_Text phaseText;
        [SerializeField] protected TMP_Text timeText;
        [SerializeField] protected TMP_Text scoreText;

        [Header("Kill-feed")]
        [SerializeField] protected TMP_Text killFeedText;
        [Tooltip("Bir kill-feed satırının ekranda kalma süresi (saniye).")]
        [SerializeField] private float killFeedSeconds = 6f;
        [Tooltip("Aynı anda gösterilecek en fazla kill-feed satırı.")]
        [SerializeField] private int killFeedMaxLines = 5;

        [Header("Yerel oyuncu")]
        [SerializeField] protected TMP_Text healthText;
        [SerializeField] protected TMP_Text statusText;
        [SerializeField] protected GameObject deathOverlay;
        [Tooltip("Opsiyonel can barı (Image.type = Filled).")]
        [SerializeField] protected Image healthFill;
        [Tooltip("Opsiyonel: kendi öldürme/ölüm sayacın (lobby_state'ten). Atanmazsa çizilmez.")]
        [SerializeField] protected TMP_Text selfStatsText;

        /// <summary>Kill-feed satırı + düşme zamanı (unscaled).</summary>
        private struct KillFeedLine
        {
            public string text;
            public float expireTime;
        }

        private readonly List<KillFeedLine> _killFeed = new List<KillFeedLine>();
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private readonly StringBuilder _sb = new StringBuilder();

        private PlayerCombatState _combat;
        private string _combatStatus = "";
        private string _countdownLabel = "";
        private bool _countdownActive;
        private bool _killFeedDirty;

        // -------------------------------------------------------- alt sınıf sözleşmesi

        /// <summary>Skor satırı — ör. "KIRMIZI 5 — 3 MAVİ" ya da "SEN 7 · LİDER 9".
        /// <b>Takım kavramı burada değil, alt sınıfta yaşar.</b></summary>
        protected abstract string ScoreLine(MatchStateMsg msg);

        /// <summary>Maç sonu başlığı — ör. "MAVİ KAZANDI" ya da "AHMET KAZANDI".</summary>
        protected abstract string WinnerLine(MatchEndMsg msg);

        /// <summary>Maç sonundaki skor satırı; <c>null</c> dönerse skor alanı son
        /// <c>match_state</c>'ten kalan değerde bırakılır.</summary>
        protected virtual string EndScoreLine(MatchEndMsg msg) => null;

        /// <summary>Roster tazelendi — bireysel skorlu modların lider tablosu buradan beslenir
        /// (<c>PlayerInfo.score</c>, §10.2). Taban yalnız adları çözer.</summary>
        protected virtual void OnLobbyStateApplied(LobbyStateMsg msg) { }

        // ---------------------------------------------------------- Unity yaşam döngüsü

        protected virtual void OnEnable()
        {
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnKillEvent += HandleKillEvent;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;

            TryBindCombat();
        }

        protected virtual void OnDisable()
        {
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnKillEvent -= HandleKillEvent;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;

            UnbindCombat();
        }

        protected virtual void Start()
        {
            // Kalıcı singleton (PlayerCombatState) sahne objelerinden sonra önyüklenebilir.
            TryBindCombat();
            RedrawKillFeed();
            RefreshStatusText();
        }

        protected virtual void Update()
        {
            // Abone olana dek dene; abone olduktan sonra bir daha uğraşma.
            if (_combat == null)
            {
                TryBindCombat();
            }

            AgeKillFeed();

            if (_killFeedDirty)
            {
                RedrawKillFeed();
            }
        }

        // -------------------------------------------------------- ağ olay işleyiciler

        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg == null || msg.players == null)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo p = msg.players[i];
                if (p == null || string.IsNullOrEmpty(p.name))
                {
                    continue;
                }

                _names[p.playerId] = p.name;
            }

            RefreshSelfStats(msg);
            OnLobbyStateApplied(msg);
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (msg.phaseReason != ArenaProtocol.PAUSE_REASON_COUNTDOWN && _countdownActive)
            {
                _countdownActive = false;
                _countdownLabel = "";
                RefreshStatusText();
            }

            SetText(phaseText, PhaseLabel(msg.phase, msg.phaseReason, msg.modeState));
            SetText(timeText, FormatTime(msg.timeRemaining));
            SetText(scoreText, ScoreLine(msg));
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (msg.seconds > 0)
            {
                _countdownActive = true;
                _countdownLabel = msg.seconds.ToString();
                SetText(phaseText, PhaseLabel(ArenaProtocol.PHASE_PAUSED,
                    ArenaProtocol.PAUSE_REASON_COUNTDOWN, ""));
            }
            else
            {
                _countdownActive = false;
                _countdownLabel = "";
            }

            RefreshStatusText();
        }

        private void HandleKillEvent(KillEventMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            string victim = NameOf(msg.victimId);
            // Kill feed metni yalnız TMP fontunda BULUNAN karakterleri kullanır: LiberationSans SDF
            // (+ fallback) ok/kuru kafa gibi sembolleri içermez, eksik glif ekranda □ olarak çizilir.
            string line = msg.killerId > 0 && msg.killerId != msg.victimId
                ? $"{NameOf(msg.killerId)} -> {victim}"
                : $"{victim} öldü";

            _killFeed.Add(new KillFeedLine
            {
                text = line,
                expireTime = Time.unscaledTime + Mathf.Max(0.5f, killFeedSeconds)
            });

            int maxLines = Mathf.Max(1, killFeedMaxLines);
            while (_killFeed.Count > maxLines)
            {
                _killFeed.RemoveAt(0);
            }

            _killFeedDirty = true;
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            _countdownActive = false;
            _countdownLabel = "";
            RefreshStatusText();

            SetText(phaseText, WinnerLine(msg));
            SetText(timeText, "00:00");

            string score = EndScoreLine(msg);
            if (score != null)
            {
                SetText(scoreText, score);
            }
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg _)
        {
            _countdownActive = false;
            _countdownLabel = "";
            _killFeed.Clear();
            _killFeedDirty = false;

            SetText(phaseText, "");
            SetText(timeText, "");
            SetText(scoreText, "");
            SetText(killFeedText, "");
            SetText(selfStatsText, "");
            RefreshStatusText();

            if (deathOverlay != null)
            {
                deathOverlay.SetActive(false);
            }
        }

        // --------------------------------------------------- yerel can/durum bağlama

        /// <summary>PlayerCombatState kalıcı singleton'ına bir kez abone olur (null olabilir).</summary>
        private void TryBindCombat()
        {
            if (_combat != null)
            {
                return;
            }

            PlayerCombatState combat = PlayerCombatState.Instance;
            if (combat == null)
            {
                return;
            }

            _combat = combat;
            _combat.HpChanged += HandleHpChanged;
            _combat.AliveChanged += HandleAliveChanged;
            _combat.StatusChanged += HandleStatusChanged;

            // Abone olmadan önceki durumu bir kez uygula.
            HandleHpChanged(_combat.Hp);
            HandleAliveChanged(_combat.IsAlive);
            HandleStatusChanged(_combat.StatusText);
        }

        private void UnbindCombat()
        {
            if (_combat == null)
            {
                return;
            }

            _combat.HpChanged -= HandleHpChanged;
            _combat.AliveChanged -= HandleAliveChanged;
            _combat.StatusChanged -= HandleStatusChanged;
            _combat = null;
        }

        private void HandleHpChanged(float hp)
        {
            float clamped = Mathf.Clamp(hp, 0f, ArenaProtocol.PLAYER_MAX_HP);
            SetText(healthText, $"CAN {Mathf.RoundToInt(clamped)}");

            if (healthFill != null)
            {
                healthFill.fillAmount = ArenaProtocol.PLAYER_MAX_HP > 0f
                    ? clamped / ArenaProtocol.PLAYER_MAX_HP
                    : 0f;
            }
        }

        private void HandleAliveChanged(bool alive)
        {
            if (deathOverlay != null)
            {
                deathOverlay.SetActive(!alive);
            }
        }

        private void HandleStatusChanged(string status)
        {
            _combatStatus = status ?? "";
            RefreshStatusText();
        }

        // ---------------------------------------------------------------- çizim

        /// <summary>Geri sayım aktifken büyük sayı, değilse savaş durum metni gösterilir.</summary>
        private void RefreshStatusText()
        {
            SetText(statusText, _countdownActive ? _countdownLabel : _combatStatus);
        }

        /// <summary>Kendi öldürme/ölüm sayacın — sunucu-otoriter (§10.2), yerelde sayılmaz.</summary>
        private void RefreshSelfStats(LobbyStateMsg msg)
        {
            if (selfStatsText == null)
            {
                return;
            }

            PlayerInfo self = FindSelf(msg);
            SetText(selfStatsText, self == null ? "" : $"{self.kills} öldürme · {self.deaths} ölüm");
        }

        /// <summary>Roster'daki kendi satırımız; kimlik yoksa/bulunamazsa null.</summary>
        protected PlayerInfo FindSelf(LobbyStateMsg msg)
        {
            int playerId = PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;
            if (playerId == 0 || msg?.players == null)
            {
                return null;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                if (msg.players[i] != null && msg.players[i].playerId == playerId)
                {
                    return msg.players[i];
                }
            }

            return null;
        }

        private void AgeKillFeed()
        {
            float now = Time.unscaledTime;
            while (_killFeed.Count > 0 && now >= _killFeed[0].expireTime)
            {
                _killFeed.RemoveAt(0);
                _killFeedDirty = true;
            }
        }

        private void RedrawKillFeed()
        {
            _killFeedDirty = false;

            if (killFeedText == null)
            {
                return;
            }

            if (_killFeed.Count == 0)
            {
                killFeedText.text = "";
                return;
            }

            _sb.Clear();
            for (int i = 0; i < _killFeed.Count; i++)
            {
                _sb.AppendLine(_killFeed[i].text);
            }

            killFeedText.text = _sb.ToString();
        }

        // ---------------------------------------------------------------- yardımcı

        /// <summary>playerId → ad (<c>lobby_state</c>'ten); bilinmiyorsa "Oyuncu N".</summary>
        protected string NameOf(int playerId)
        {
            return _names.TryGetValue(playerId, out string name) && !string.IsNullOrEmpty(name)
                ? name
                : $"Oyuncu {playerId}";
        }

        protected static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        /// <summary>
        /// Durumu (§10.1) ekran metnine çevirir. <b>Faz tek başına yetmez:</b> telde tek bir
        /// <c>paused</c> görünen durum lobi de olabilir, yükleme/geri sayım/duraklatma da —
        /// gerekçe (<c>phaseReason</c>) bunları ayırır. Modun kendi ara durumu
        /// (<c>modeState</c>, ör. turnuva toplanması) gerekçe <c>mode</c> iken devreye girer;
        /// tabanın onu yorumlaması beklenmez, alt sınıf <see cref="ModeStateLabel"/> ile yazar.
        /// <para>
        /// <c>virtual</c>: tur tabanlı bir mod koşan maça "MAÇ" yerine "TUR 3" yazmak isteyebilir.
        /// Alt sınıf yalnız ilgilendiği dalı ezer, gerisini <c>base</c>'e bırakır — faz/gerekçe
        /// sözlüğü tek yerde kalsın.
        /// </para>
        /// </summary>
        protected virtual string PhaseLabel(string phase, string phaseReason, string modeState)
        {
            if (phase == ArenaProtocol.PHASE_PLAYING) return "MAÇ";
            if (phase == ArenaProtocol.PHASE_FINISHED) return "MAÇ BİTTİ";

            switch (phaseReason)
            {
                case ArenaProtocol.PAUSE_REASON_LOBBY: return "LOBİ";
                case ArenaProtocol.PAUSE_REASON_LOADING: return "YÜKLENİYOR";
                case ArenaProtocol.PAUSE_REASON_COUNTDOWN: return "HAZIRLAN";
                case ArenaProtocol.PAUSE_REASON_OPERATOR: return "DURAKLATILDI";
                case ArenaProtocol.PAUSE_REASON_MODE: return ModeStateLabel(modeState);
                default: return "BEKLEME";
            }
        }

        /// <summary>
        /// Mod duraklatma istediğinde (<c>phaseReason == "mode"</c>) gösterilecek metin. Taban
        /// <c>modeState</c>'i YORUMLAMAZ — anlamı yalnız modun kendisi bilir (turnuvada
        /// "herkes tabana dönsün" gibi). Alt sınıf yazmazsa nötr bir metin gösterilir.
        /// </summary>
        protected virtual string ModeStateLabel(string modeState) => "BEKLEME";

        protected static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
