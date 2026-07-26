using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Tdm
{
    /// <summary>
    /// TDM HUD prefabının kökünde duran SUNUM bileşeni: sunucudan gelen maç
    /// olaylarını (faz/süre/skor, geri sayım, kill-feed, maç sonu) ve yerel
    /// oyuncunun can/durum değişimlerini metne çevirir. Kural/otorite YOK —
    /// hiçbir şey hesaplamaz, yalnız gösterir. Tüm UI bağları null olabilir
    /// (prefab kablolaması Unity MCP ile yapılır).
    /// </summary>
    public class TdmClientController : MonoBehaviour
    {
        [Header("Maç durumu")]
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private TMP_Text timeText;
        [SerializeField] private TMP_Text scoreText;

        [Header("Kill-feed")]
        [SerializeField] private TMP_Text killFeedText;
        [Tooltip("Bir kill-feed satırının ekranda kalma süresi (saniye).")]
        [SerializeField] private float killFeedSeconds = 6f;
        [Tooltip("Aynı anda gösterilecek en fazla kill-feed satırı.")]
        [SerializeField] private int killFeedMaxLines = 5;

        [Header("Yerel oyuncu")]
        [SerializeField] private TMP_Text healthText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private GameObject deathOverlay;
        [Tooltip("Opsiyonel can barı (Image.type = Filled).")]
        [SerializeField] private Image healthFill;

        /// <summary>Kill-feed satırı + düşme zamanı (unscaled).</summary>
        private struct KillFeedLine
        {
            public string text;
            public float expireTime;
        }

        private readonly List<KillFeedLine> _killFeed = new List<KillFeedLine>();
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();

        private PlayerCombatState _combat;
        private string _combatStatus = "";
        private string _countdownLabel = "";
        private bool _countdownActive;
        private bool _killFeedDirty;

        private void OnEnable()
        {
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnKillEvent += HandleKillEvent;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;

            TryBindCombat();
        }

        private void OnDisable()
        {
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnKillEvent -= HandleKillEvent;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;

            UnbindCombat();
        }

        private void Start()
        {
            // Kalıcı singleton (PlayerCombatState) sahne objelerinden sonra önyüklenebilir.
            TryBindCombat();
            RedrawKillFeed();
            RefreshStatusText();
        }

        private void Update()
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
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (msg.phase != "Countdown" && _countdownActive)
            {
                _countdownActive = false;
                _countdownLabel = "";
                RefreshStatusText();
            }

            SetText(phaseText, PhaseLabel(msg.phase));
            SetText(timeText, FormatTime(msg.timeRemaining));
            SetText(scoreText, ScoreLine(msg.scoreRed, msg.scoreBlue));
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
                SetText(phaseText, PhaseLabel("Countdown"));
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

            SetText(phaseText, WinnerLine(msg.winnerTeam));
            SetText(timeText, "00:00");
            SetText(scoreText, ScoreLine(msg.scoreRed, msg.scoreBlue));
        }

        private void HandleReturnToLobby()
        {
            _countdownActive = false;
            _countdownLabel = "";
            _killFeed.Clear();
            _killFeedDirty = false;

            SetText(phaseText, "");
            SetText(timeText, "");
            SetText(scoreText, "");
            SetText(killFeedText, "");
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

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _killFeed.Count; i++)
            {
                sb.AppendLine(_killFeed[i].text);
            }

            killFeedText.text = sb.ToString();
        }

        // ---------------------------------------------------------------- yardımcı

        private string NameOf(int playerId)
        {
            return _names.TryGetValue(playerId, out string name) && !string.IsNullOrEmpty(name)
                ? name
                : $"Oyuncu {playerId}";
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }

        private static string ScoreLine(int scoreRed, int scoreBlue)
        {
            return $"KIRMIZI {scoreRed} — {scoreBlue} MAVİ";
        }

        private static string WinnerLine(string winnerTeam)
        {
            if (winnerTeam == "red")
            {
                return "KIRMIZI KAZANDI";
            }

            return winnerTeam == "blue" ? "MAVİ KAZANDI" : "BERABERE";
        }

        /// <summary>Protokol faz adını (§10.1) ekran metnine çevirir; bilinmeyen faz aynen gösterilir.</summary>
        private static string PhaseLabel(string phase)
        {
            switch (phase)
            {
                case "Lobby": return "LOBİ";
                case "Loading": return "YÜKLENİYOR";
                case "Countdown": return "HAZIRLAN";
                case "Live": return "MAÇ";
                case "End": return "MAÇ BİTTİ";
                default: return phase ?? "";
            }
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
