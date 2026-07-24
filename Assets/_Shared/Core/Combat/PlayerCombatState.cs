using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

// Takım enum'u için takma ad: sınıfta aynı adlı bir ÖZELLİK (Team) olduğu için
// enum üyelerine bu alias üzerinden erişilir (isim belirsizliği kalmasın).
using CoreTeam = VortexArena.Core.Team;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// YEREL oyuncunun maç/savaş durumu (kalıcı tekil): takım, spawn slot'u, can,
    /// hayatta mı, ateş yetkisi ve free-roam canlanma akışı.
    /// <para>
    /// Sunucu-otoriter: can yalnız <c>health_update</c>'ten, faz yalnız
    /// <c>match_state</c>/<c>countdown</c>/<c>load_match</c>'ten gelir. Bu sınıf hasar
    /// uygulamaz, skor tutmaz, faz değiştirmez (Docs/ArenaNet-Protokol.md §10).
    /// </para>
    /// <para>
    /// ⚠️ FREE-ROAM KURALI: fiziksel oyuncu IŞINLANAMAZ. Canlanma bir konum değil
    /// DURUM değişimidir — bu sınıf hiçbir koşulda rig'i/kamerayı taşımaz.
    /// <see cref="SpawnPoint"/> yalnız "hangi tabana dön" göstergesidir (§10.4).
    /// </para>
    /// <para>
    /// Sahnede DURMAZ: <c>load_match</c> sahne yüklenmeden ÖNCE gelir, bu yüzden
    /// ArenaClient/SceneRouter deseniyle kendini önyükler ve DontDestroyOnLoad olur.
    /// </para>
    /// </summary>
    public class PlayerCombatState : MonoBehaviour
    {
        // Faz adları protokolde string taşınır (Docs §10.1: Lobby → Loading → Countdown → Live → End).
        private const string PhaseLobby = "Lobby";
        private const string PhaseLoading = "Loading";
        private const string PhaseCountdown = "Countdown";
        private const string PhaseLive = "Live";
        private const string PhaseEnd = "End";

        /// <summary>Canlanma talebi tekrar aralığı (sunucu onaylayana dek, §10.4).</summary>
        private const float ReviveRepeatSeconds = 1f;

        /// <summary>Kendi tabanı bulunamadığında sahneyi yeniden tarama aralığı.</summary>
        private const float ZoneRescanSeconds = 1f;

        public static PlayerCombatState Instance { get; private set; }

        /// <summary>welcome'da atanan kimlik (0 = henüz bağlanılmadı).</summary>
        public int PlayerId { get; private set; }

        /// <summary>Takım: load_match.yourTeam (lobide lobby_state'ten de güncellenir).</summary>
        public Team Team { get; private set; } = CoreTeam.Red;

        /// <summary>Takım içi 0 tabanlı spawn slot'u (yalnız gösterge — ışınlanma YOK).</summary>
        public int SpawnSlot { get; private set; }

        /// <summary>Aktif maçın modId'si (load_match'ten).</summary>
        public string ModeId { get; private set; } = "";

        /// <summary>Sunucudan gelen son faz adı ("Lobby"/"Loading"/"Countdown"/"Live"/"End").</summary>
        public string Phase { get; private set; } = PhaseLobby;

        /// <summary>Yerel oyuncunun canı — YALNIZ health_update'ten set edilir.</summary>
        public float Hp { get; private set; } = ArenaProtocol.PLAYER_MAX_HP;

        /// <summary>Hayatta mı (hp &gt; 0; respawn mesajıyla da false olur).</summary>
        public bool IsAlive { get; private set; } = true;

        /// <summary>Ölüm/canlanma durum metni; canlıyken boş.</summary>
        public string StatusText { get; private set; } = "";

        public event Action<float> HpChanged;
        public event Action<bool> AliveChanged;
        public event Action<string> StatusChanged;

        /// <summary>
        /// Silah tetiği çekilebilir mi: hayatta + faz Lobby/Live (Loading/Countdown/End'de
        /// ateş yok; lobide test atışına izin verilir).
        /// <para>
        /// Bağlantı koşulu: bir kez bağlandıysak bağlantı kopukken ateş kilitlenir
        /// (mesajlar zaten sunucuya ulaşmaz). Hiç bağlanılmamışsa (sunucusuz Editor
        /// testi) silah çalışmaya devam eder — Faz 0-2 yerel test akışı bozulmasın.
        /// </para>
        /// </summary>
        public bool CanFire
        {
            get
            {
                if (!IsAlive)
                {
                    return false;
                }

                if (Phase != PhaseLobby && Phase != PhaseLive)
                {
                    return false;
                }

                if (!_hasEverConnected)
                {
                    return true;
                }

                ArenaClient client = ArenaClient.Instance;
                return client != null && client.IsConnected;
            }
        }

        // Her frame yeni DTO ayırmamak için tek örnek (alansız mesaj).
        private readonly ReviveRequestMsg _reviveMsg = new ReviveRequestMsg();

        private bool _hasEverConnected;
        private bool _awaitingRevive;
        private float _reviveAt;
        private float _nextReviveSendAt;

        private BaseZone[] _zones = Array.Empty<BaseZone>();
        private float _nextZoneScanAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[PlayerCombatState]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PlayerCombatState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // İkinci kopya (sahneye elle konmuş olabilir) kendini yok eder.
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Kalıcı tekiliz: OnEnable/OnDisable yerine Awake/OnDestroy'da abone oluruz,
            // böylece obje devre dışı bırakılsa bile sunucu olayları kaçmaz.
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnDisconnected += HandleDisconnected;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnHealthUpdate += HandleHealthUpdate;
            NetEvents.OnRespawn += HandleRespawn;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;

            SceneManager.sceneLoaded += HandleSceneLoaded;
            ScanZones();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnDisconnected -= HandleDisconnected;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnHealthUpdate -= HandleHealthUpdate;
            NetEvents.OnRespawn -= HandleRespawn;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;

            SceneManager.sceneLoaded -= HandleSceneLoaded;

            Instance = null;
        }

        private void Update()
        {
            if (Instance != this)
            {
                return;
            }

            TickRevive();
            RefreshStatusText();
        }

        // ------------------------------------------------------- free-roam canlanma

        /// <summary>
        /// §10.4: gecikme dolduktan VE oyuncu kendi <see cref="BaseZone"/>'una fiziken
        /// girdikten sonra <c>revive_request</c> gönderir; sunucu canlandırana
        /// (hp &gt; 0 health_update) dek saniyede bir tekrarlar. Sahnede kendi takımının
        /// tabanı yoksa (lobi/test sahnesi) bölge koşulu aranmaz.
        /// </summary>
        private void TickRevive()
        {
            if (IsAlive || !_awaitingRevive || Time.time < _reviveAt)
            {
                return;
            }

            BaseZone zone = FindOwnBaseZone();
            if (zone != null && !zone.IsPlayerInside)
            {
                return;
            }

            if (Time.time < _nextReviveSendAt)
            {
                return;
            }

            _nextReviveSendAt = Time.time + ReviveRepeatSeconds;
            ArenaClient.Instance?.Send(_reviveMsg);
        }

        private void RefreshStatusText()
        {
            string text = "";

            if (!IsAlive)
            {
                float remaining = _reviveAt - Time.time;
                if (remaining > 0f)
                {
                    text = $"Öldün — canlanmaya {Mathf.CeilToInt(remaining)} sn";
                }
                else
                {
                    BaseZone zone = FindOwnBaseZone();
                    text = zone == null || zone.IsPlayerInside ? "Canlanılıyor..." : "Tabanına dön ve canlan";
                }
            }

            if (text == StatusText)
            {
                return;
            }

            StatusText = text;
            StatusChanged?.Invoke(text);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ScanZones();
        }

        private void ScanZones()
        {
            _zones = FindObjectsByType<BaseZone>(FindObjectsSortMode.None);
            _nextZoneScanAt = Time.time + ZoneRescanSeconds;
        }

        /// <summary>Kendi takımının taban bölgesi; sahnede yoksa null (bölge koşulu aranmaz).</summary>
        private BaseZone FindOwnBaseZone()
        {
            BaseZone found = MatchZone();
            if (found != null)
            {
                return found;
            }

            // Bölge sahneye sonradan gelmiş olabilir — saniyede bir yeniden tara.
            if (Time.time >= _nextZoneScanAt)
            {
                ScanZones();
                found = MatchZone();
            }

            return found;
        }

        private BaseZone MatchZone()
        {
            for (int i = 0; i < _zones.Length; i++)
            {
                BaseZone zone = _zones[i];
                if (zone != null && zone.Team == this.Team)
                {
                    return zone;
                }
            }

            return null;
        }

        // -------------------------------------------------------- olay işleyicileri

        private void HandleConnected(WelcomeMsg msg)
        {
            _hasEverConnected = true;

            if (msg == null)
            {
                return;
            }

            PlayerId = msg.playerId;

            // Geç katılım: welcome'daki faz bilgisiyle senkronla.
            string phase = msg.match != null && !string.IsNullOrEmpty(msg.match.phase) ? msg.match.phase : PhaseLobby;
            if (msg.match != null && !string.IsNullOrEmpty(msg.match.modeId))
            {
                ModeId = msg.match.modeId;
            }

            ResetCombat(phase);
        }

        private void HandleDisconnected()
        {
            PlayerId = 0;
            ResetCombat(PhaseLobby);
        }

        /// <summary>Takımı lobide de bilelim (canlanma bölgesi ve arayüz renkleri buna bakar).</summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg == null || msg.players == null || PlayerId == 0)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info == null || info.playerId != PlayerId)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(info.team))
                {
                    Team = ParseTeam(info.team, this.Team);
                }

                return;
            }
        }

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            Team = ParseTeam(msg.yourTeam, this.Team);
            SpawnSlot = msg.spawnSlot;
            ModeId = msg.modeId ?? "";

            // Sahne yükleniyor: ateş kapalı, can tam, ölüm durumu temiz.
            ResetCombat(PhaseLoading);
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            Phase = PhaseCountdown;
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.phase))
            {
                return;
            }

            Phase = msg.phase;
        }

        private void HandleHealthUpdate(HealthUpdateMsg msg)
        {
            if (msg == null || PlayerId == 0 || msg.playerId != PlayerId)
            {
                return;
            }

            SetHp(msg.hp);

            if (msg.hp > 0f)
            {
                // Sunucu canlandırdı (veya hasar aldık ama hayattayız).
                if (!IsAlive)
                {
                    _awaitingRevive = false;
                    SetAlive(true);
                }

                return;
            }

            // hp ≤ 0: respawn mesajı henüz gelmediyse varsayılan gecikmeyle ölüm durumu başlat.
            BeginDeath(ArenaProtocol.RESPAWN_DELAY, false);
        }

        private void HandleRespawn(RespawnMsg msg)
        {
            if (msg == null || PlayerId == 0 || msg.playerId != PlayerId)
            {
                return;
            }

            // spawnSlot yalnız "hangi slota dön" göstergesidir — rig TAŞINMAZ.
            SpawnSlot = msg.spawnSlot;
            BeginDeath(msg.delaySeconds, true);
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            Phase = PhaseEnd;
        }

        private void HandleReturnToLobby()
        {
            ResetCombat(PhaseLobby);
        }

        // ---------------------------------------------------------------- yardımcı

        private void BeginDeath(float delaySeconds, bool authoritative)
        {
            float delay = Mathf.Max(0f, delaySeconds);

            if (IsAlive)
            {
                _reviveAt = Time.time + delay;
                _nextReviveSendAt = 0f;
                _awaitingRevive = true;
                SetAlive(false);
                return;
            }

            // Zaten ölüyüz: yalnız sunucunun respawn mesajı zamanlayıcıyı güncelleyebilir.
            if (authoritative)
            {
                _reviveAt = Time.time + delay;
                _nextReviveSendAt = 0f;
                _awaitingRevive = true;
            }
        }

        private void ResetCombat(string phase)
        {
            Phase = string.IsNullOrEmpty(phase) ? PhaseLobby : phase;
            _awaitingRevive = false;
            _nextReviveSendAt = 0f;
            SetHp(ArenaProtocol.PLAYER_MAX_HP);
            SetAlive(true);
            RefreshStatusText();
        }

        private void SetHp(float hp)
        {
            float clamped = Mathf.Clamp(hp, 0f, ArenaProtocol.PLAYER_MAX_HP);
            if (Mathf.Approximately(clamped, Hp))
            {
                return;
            }

            Hp = clamped;
            HpChanged?.Invoke(Hp);
        }

        private void SetAlive(bool alive)
        {
            if (IsAlive == alive)
            {
                return;
            }

            IsAlive = alive;
            AliveChanged?.Invoke(alive);
        }

        /// <summary>Protokoldeki "red"/"blue" değerini enum'a çevirir; tanımsızsa mevcut takım korunur.</summary>
        private static Team ParseTeam(string team, Team fallback)
        {
            if (string.Equals(team, "red", StringComparison.OrdinalIgnoreCase))
            {
                return CoreTeam.Red;
            }

            if (string.Equals(team, "blue", StringComparison.OrdinalIgnoreCase))
            {
                return CoreTeam.Blue;
            }

            return fallback;
        }
    }
}
