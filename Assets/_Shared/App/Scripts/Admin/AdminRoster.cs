using System;
using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App.Admin
{
    /// <summary>Admin arayüzünün tek oyuncu satırı için ihtiyacı olan her şey.</summary>
    public class AdminPlayerView
    {
        public int playerId;
        public string name = "";
        public string role = AppSession.RolePlayer;
        public string team = "";
        public bool ready;
        public bool online = true;
        public bool alive = true;
        public float battery = -1f;
        public float hp = ArenaProtocol.PLAYER_MAX_HP;
        public int kills;
        public int deaths;

        /// <summary>Bireysel maç skoru (§10.2) — kills DEĞİLDİR; anlamı moda göre değişir.</summary>
        public int score;

        public string scene = "";

        /// <summary>Ölüm anı (<c>Time.unscaledTime</c>); -1 = ölmedi/bilinmiyor.</summary>
        public float diedAt = -1f;

        public bool IsPlayer => role == AppSession.RolePlayer;

        public float HpNormalized => Mathf.Clamp01(hp / ArenaProtocol.PLAYER_MAX_HP);

        /// <summary>Canlanmaya kalan saniye (0 = beklemiyor). Bkz. sınıf dokümanı.</summary>
        public float RespawnRemaining =>
            alive || diedAt < 0f
                ? 0f
                : Mathf.Max(0f, ArenaProtocol.RESPAWN_DELAY - (Time.unscaledTime - diedAt));
    }

    /// <summary>
    /// Sunucudan gelen her şeyin birleşik canlı modeli — admin arayüzünün veri katmanı.
    /// Hiçbir UI tipine dokunmaz; HUD ve paneller yalnız buradan okur.
    /// <para>
    /// <b>Kaynaklar ve otorite:</b> <c>lobby_state</c> TAM ve otoriter anlık görüntüdür
    /// (ad/rol/takım/hazır/çevrimiçi/batarya/sahne + <c>kills/deaths/hp/alive</c>); sunucu
    /// ölüm ve canlanmada onu tazeler. Aralarda <c>health_update</c> ve <c>kill_event</c> ile
    /// yerel olarak ilerletilir ki bar ve sayaçlar anında tepki versin. Sapma olursa bir
    /// sonraki <c>lobby_state</c> sunucunun dediğini yazar — sunucu her zaman kazanır.
    /// </para>
    /// <para>
    /// ⚠ <b><c>respawn</c> admin'e GELMEZ</b>: sunucu onu yalnız ölen oyuncunun bağlantısına
    /// yollar (§10.4). Bu yüzden canlanma geri sayımı <c>kill_event</c> zamanı +
    /// <see cref="ArenaProtocol.RESPAWN_DELAY"/> ile YEREL olarak hesaplanır; oyuncu tabanına
    /// girmezse gerçek canlanma <see cref="ArenaProtocol.REVIVE_GRACE"/>'e kadar sarkabilir —
    /// sayaç 0'a inince "TABANDA BEKLENİYOR"a döner, yanlış "canlandı" demez.
    /// </para>
    /// </summary>
    public class AdminRoster : MonoBehaviour
    {
        /// <summary>Ölüm akışında tutulan en fazla satır.</summary>
        public const int KillFeedMaxLines = 8;

        public static AdminRoster Instance { get; private set; }

        /// <summary>Roster/skor/faz verisi değiştiğinde (ana thread).</summary>
        public event Action Changed;

        private readonly Dictionary<int, AdminPlayerView> _players = new Dictionary<int, AdminPlayerView>();
        private readonly List<AdminPlayerView> _red = new List<AdminPlayerView>();
        private readonly List<AdminPlayerView> _blue = new List<AdminPlayerView>();
        private readonly List<AdminPlayerView> _all = new List<AdminPlayerView>();
        private readonly List<string> _killFeed = new List<string>();
        private readonly List<int> _removeScratch = new List<int>();

        /// <summary>Kırmızı takım (yalnız role=player, playerId sırasında).</summary>
        public IReadOnlyList<AdminPlayerView> Red => _red;

        /// <summary>Mavi takım (yalnız role=player, playerId sırasında).</summary>
        public IReadOnlyList<AdminPlayerView> Blue => _blue;

        /// <summary>Tüm oyuncular (yalnız role=player, playerId sırasında).</summary>
        public IReadOnlyList<AdminPlayerView> Players => _all;

        public IReadOnlyList<string> KillFeed => _killFeed;

        /// <summary>Bağlı admin sayısı (kendimiz dahil) — istatistik panelinde gösterilir.</summary>
        public int AdminCount { get; private set; }

        /// <summary>
        /// Arayüz tek kolona mı düşsün (takımsız mod)? <b>Otorite sunucudadır:</b> karar
        /// <see cref="ModeRuntime.Teams"/>'den, yani <c>load_match.rules</c>'tan gelir (§10.5).
        /// <para>
        /// Maç yokken (faz Lobby) henüz kural yayınlanmamıştır; o zaman ortak seçimin modu
        /// katalogdan okunur (<see cref="AdminSelection.ModeId"/>). Katalog da yoksa
        /// <b>sezgisel</b> yedeğe düşülür ("hiçbir çevrimiçi oyuncunun takımı yok") — bu eskiden
        /// TEK karar yoluydu ve lobide takımı henüz atanmamış TDM maçını da FFA gösteriyordu;
        /// artık yalnız arayüz boş kalmasın diye duruyor.
        /// </para>
        /// <para>
        /// Alan değil <b>hesaplanan özelliktir</b>: girdileri (faz, sunucu kuralı, ortak seçim)
        /// roster'dan bağımsız değişiyor — önbelleklenseydi mod değişince bayat kalırdı.
        /// </para>
        /// </summary>
        public bool IsFfa => ResolveIsFfa();

        /// <summary>Sezgisel yedeğin girdisi: çevrimiçi oyunculardan en az birinin takımı var mı
        /// (<see cref="Rebuild"/> hesaplar).</summary>
        private bool _anyOnlineTeam;

        public string Phase { get; private set; } = "Lobby";
        public float TimeRemaining { get; private set; }
        public int ScoreRed { get; private set; }
        public int ScoreBlue { get; private set; }

        /// <summary>Geri sayım saniyesi (Countdown fazı dışında 0).</summary>
        public int CountdownSeconds { get; private set; }

        /// <summary>Maç bitti mesajının kazanan TAKIMI ("red"/"blue"/"" = yok); faz End'de anlamlı.</summary>
        public string WinnerTeam { get; private set; } = "";

        /// <summary>Maç bitti mesajının kazanan OYUNCUSU (bireysel skorlu modlar); 0 = yok.
        /// İkisi birden dolu olmaz — arayüz dolu olana bakar (§5.3 <c>match_end</c>).</summary>
        public int WinnerPlayerId { get; private set; }

        public string ModeId { get; private set; } = "";
        public string SceneName { get; private set; } = "";
        public int ScoreLimit { get; private set; }
        public int RoundSeconds { get; private set; }

        /// <summary>Son snapshot'tan bu yana geçen süre (sn); hiç snapshot yoksa -1.</summary>
        public float SnapshotAge
        {
            get
            {
                RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
                if (registry == null || registry.LastSnapshotMs == 0)
                {
                    return -1f;
                }

                return Mathf.Max(0f, (Environment.TickCount - registry.LastSnapshotMs) / 1000f);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnEnable()
        {
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnDisconnected += HandleDisconnected;
            NetEvents.OnLobbyState += HandleLobbyState;
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnMatchState += HandleMatchState;
            NetEvents.OnCountdown += HandleCountdown;
            NetEvents.OnMatchEnd += HandleMatchEnd;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            NetEvents.OnHealthUpdate += HandleHealthUpdate;
            NetEvents.OnKillEvent += HandleKillEvent;
        }

        private void OnDisable()
        {
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnDisconnected -= HandleDisconnected;
            NetEvents.OnLobbyState -= HandleLobbyState;
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnMatchState -= HandleMatchState;
            NetEvents.OnCountdown -= HandleCountdown;
            NetEvents.OnMatchEnd -= HandleMatchEnd;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            NetEvents.OnHealthUpdate -= HandleHealthUpdate;
            NetEvents.OnKillEvent -= HandleKillEvent;
        }

        // -------------------------------------------------------------- sorgular

        public AdminPlayerView Find(int playerId)
        {
            return _players.TryGetValue(playerId, out AdminPlayerView view) ? view : null;
        }

        public string NameOf(int playerId)
        {
            AdminPlayerView view = Find(playerId);
            return view != null && !string.IsNullOrEmpty(view.name) ? view.name : $"Oyuncu {playerId}";
        }

        /// <summary>
        /// POV için sonraki uygun oyuncu (Tab): çevrimiçi oyuncular arasında playerId sırasında
        /// döner. Hiç oyuncu yoksa 0.
        /// </summary>
        public int NextPlayerId(int currentId)
        {
            if (_all.Count == 0)
            {
                return 0;
            }

            int index = -1;
            for (int i = 0; i < _all.Count; i++)
            {
                if (_all[i].playerId == currentId)
                {
                    index = i;
                    break;
                }
            }

            for (int step = 1; step <= _all.Count; step++)
            {
                AdminPlayerView candidate = _all[(index + step + _all.Count) % _all.Count];
                if (candidate.online)
                {
                    return candidate.playerId;
                }
            }

            return _all[0].playerId;
        }

        /// <summary>Takım toplamları (öldürme/ölüm/canlı sayısı).</summary>
        public void TeamTotals(string team, out int kills, out int deaths, out int aliveCount)
        {
            kills = 0;
            deaths = 0;
            aliveCount = 0;

            for (int i = 0; i < _all.Count; i++)
            {
                AdminPlayerView view = _all[i];
                if (view.team != team)
                {
                    continue;
                }

                kills += view.kills;
                deaths += view.deaths;
                if (view.online && view.alive)
                {
                    aliveCount++;
                }
            }
        }

        // ------------------------------------------------------- olay işleyiciler

        private void HandleConnected(WelcomeMsg msg)
        {
            if (msg?.match == null)
            {
                return;
            }

            Phase = string.IsNullOrEmpty(msg.match.phase) ? "Lobby" : msg.match.phase;
            TimeRemaining = msg.match.timeRemaining;
            ScoreRed = msg.match.scoreRed;
            ScoreBlue = msg.match.scoreBlue;
            ModeId = msg.match.modeId ?? "";
            SceneName = msg.match.sceneName ?? "";
            Raise();
        }

        private void HandleDisconnected()
        {
            _players.Clear();
            _killFeed.Clear();
            AdminCount = 0;
            Rebuild();
        }

        /// <summary>Sunucunun TAM görüntüsü: ekleme, güncelleme ve ayrılanların silinmesi.</summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            if (msg?.players == null)
            {
                return;
            }

            _removeScratch.Clear();
            foreach (KeyValuePair<int, AdminPlayerView> kv in _players)
            {
                _removeScratch.Add(kv.Key);
            }

            AdminCount = 0;

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info == null || info.playerId <= 0)
                {
                    continue;
                }

                _removeScratch.Remove(info.playerId);

                if (!_players.TryGetValue(info.playerId, out AdminPlayerView view))
                {
                    view = new AdminPlayerView { playerId = info.playerId };
                    _players.Add(info.playerId, view);
                }

                view.name = string.IsNullOrEmpty(info.name) ? $"Oyuncu {info.playerId}" : info.name;
                view.role = string.IsNullOrEmpty(info.role) ? AppSession.RolePlayer : info.role;
                view.team = info.team ?? "";
                view.ready = info.ready;
                view.online = info.online;
                view.battery = info.battery;
                view.scene = info.scene ?? "";

                // Sunucu sayaçları yereli EZER (§5.3) — sapma burada kapanır.
                view.kills = info.kills;
                view.deaths = info.deaths;
                view.score = info.score;
                view.hp = info.hp;

                if (view.alive != info.alive)
                {
                    view.alive = info.alive;
                    view.diedAt = info.alive ? -1f : Time.unscaledTime;
                }

                if (!view.IsPlayer)
                {
                    AdminCount++;
                }
            }

            for (int i = 0; i < _removeScratch.Count; i++)
            {
                _players.Remove(_removeScratch[i]);
            }

            Rebuild();
        }

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            ModeId = msg.modeId ?? "";
            SceneName = msg.sceneName ?? "";
            RoundSeconds = msg.roundSeconds;
            ScoreLimit = msg.scoreLimit;
            WinnerTeam = "";
            WinnerPlayerId = 0;
            Raise();
        }

        private void HandleMatchState(MatchStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            Phase = string.IsNullOrEmpty(msg.phase) ? Phase : msg.phase;
            TimeRemaining = msg.timeRemaining;
            ScoreRed = msg.scoreRed;
            ScoreBlue = msg.scoreBlue;

            if (Phase != "Countdown")
            {
                CountdownSeconds = 0;
            }

            Raise();
        }

        private void HandleCountdown(CountdownMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            CountdownSeconds = msg.seconds;
            Raise();
        }

        private void HandleMatchEnd(MatchEndMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            WinnerTeam = msg.winnerTeam ?? "";
            WinnerPlayerId = msg.winnerPlayerId;
            ScoreRed = msg.scoreRed;
            ScoreBlue = msg.scoreBlue;
            Phase = "End";
            Raise();
        }

        private void HandleReturnToLobby()
        {
            Phase = "Lobby";
            CountdownSeconds = 0;
            WinnerTeam = "";
            WinnerPlayerId = 0;
            TimeRemaining = 0f;
            _killFeed.Clear();

            foreach (KeyValuePair<int, AdminPlayerView> kv in _players)
            {
                kv.Value.hp = ArenaProtocol.PLAYER_MAX_HP;
                kv.Value.alive = true;
                kv.Value.diedAt = -1f;
                kv.Value.score = 0; // sunucu da lobiye dönerken sıfırlıyor (§10.2)
            }

            Raise();
        }

        private void HandleHealthUpdate(HealthUpdateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            AdminPlayerView view = Find(msg.playerId);
            if (view == null)
            {
                return;
            }

            view.hp = msg.hp;

            // hp>0 ⇒ canlı: sunucu ölümde 0, canlanmada tam can yayınlıyor (§10.4/3).
            bool alive = msg.hp > 0f;
            if (view.alive != alive)
            {
                view.alive = alive;
                view.diedAt = alive ? -1f : Time.unscaledTime;
            }

            Raise();
        }

        private void HandleKillEvent(KillEventMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            AdminPlayerView victim = Find(msg.victimId);
            if (victim != null)
            {
                victim.deaths++;
                victim.alive = false;
                victim.hp = 0f;
                victim.diedAt = Time.unscaledTime;
            }

            if (msg.killerId > 0 && msg.killerId != msg.victimId)
            {
                AdminPlayerView killer = Find(msg.killerId);
                if (killer != null)
                {
                    killer.kills++;

                    // Bireysel skorlu modda tablo anında tepki versin. Bu bir TAHMİN'dir
                    // (skoru mod yazar, öldürme başına 1 olmak zorunda değil) — bir sonraki
                    // lobby_state sunucunun dediğini yazar, `kills` deseninin aynısı.
                    if (ModeRuntime.Scoring == ModeScoreKind.Player)
                    {
                        killer.score++;
                    }
                }
            }

            // TMP varsayılan fontunda garantisi olmayan sembol kullanılmaz ("->" ile yazılır).
            string weapon = string.IsNullOrEmpty(msg.weaponId) ? "" : $" [{msg.weaponId}]";
            string line = msg.killerId > 0 && msg.killerId != msg.victimId
                ? $"{NameOf(msg.killerId)} -> {NameOf(msg.victimId)}{weapon}"
                : $"{NameOf(msg.victimId)} öldü{weapon}";

            _killFeed.Add(line);
            while (_killFeed.Count > KillFeedMaxLines)
            {
                _killFeed.RemoveAt(0);
            }

            Raise();
        }

        // ---------------------------------------------------------------- iç işler

        /// <summary>Takım listelerini ve FFA kararını yeniden kurar.</summary>
        private void Rebuild()
        {
            _all.Clear();
            _red.Clear();
            _blue.Clear();

            foreach (KeyValuePair<int, AdminPlayerView> kv in _players)
            {
                if (kv.Value.IsPlayer)
                {
                    _all.Add(kv.Value);
                }
            }

            _all.Sort(ComparePlayerId);

            bool anyTeam = false;
            for (int i = 0; i < _all.Count; i++)
            {
                AdminPlayerView view = _all[i];
                if (view.team == "red")
                {
                    _red.Add(view);
                }
                else if (view.team == "blue")
                {
                    _blue.Add(view);
                }

                if (view.online && !string.IsNullOrEmpty(view.team))
                {
                    anyTeam = true;
                }
            }

            _anyOnlineTeam = anyTeam;

            // Seçili oyuncu ayrıldıysa seçimi ilk uygun oyuncuya taşı (POV boşta kalmasın).
            if (AdminSession.SelectedPlayerId != 0 && Find(AdminSession.SelectedPlayerId) == null)
            {
                AdminSession.SelectedPlayerId = _all.Count > 0 ? _all[0].playerId : 0;
            }
            else if (AdminSession.SelectedPlayerId == 0 && _all.Count > 0)
            {
                AdminSession.SelectedPlayerId = _all[0].playerId;
            }

            Raise();
        }

        /// <summary>
        /// Takım kipi kararı — sırayla üç kaynak (bkz. <see cref="IsFfa"/>):
        /// (1) koşan maçın sunucudan gelen kuralı, (2) lobide ortak seçimin katalogdaki modu,
        /// (3) sezgisel yedek.
        /// </summary>
        private bool ResolveIsFfa()
        {
            // (1) Maç yüklendiyse kural sunucudan gelmiştir (load_match.rules / welcome.match.rules).
            if (Phase != "Lobby")
            {
                return ModeRuntime.Teams == ModeTeamMode.None;
            }

            // (2) Lobide henüz kural yok: operatörün seçtiği mod ne diyor?
            ModeDefinition selected = AdminContent.Catalog != null
                ? AdminContent.Catalog.FindMode(AdminSelection.ModeId)
                : null;
            if (selected != null)
            {
                return selected.TeamMode == ModeTeamMode.None;
            }

            // (3) Katalog/seçim yok — arayüz boş kalmasın.
            return !_anyOnlineTeam;
        }

        private static int ComparePlayerId(AdminPlayerView a, AdminPlayerView b)
        {
            return a.playerId.CompareTo(b.playerId);
        }

        private void Raise()
        {
            Changed?.Invoke();
        }
    }
}
