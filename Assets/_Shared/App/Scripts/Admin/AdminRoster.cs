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

        /// <summary>Forma numarası 1..99 (§2); 0 = atanmamış, admin'de daima 0. Adlar benzersiz
        /// olmadığı için operatörün ayırt edici alanı budur.</summary>
        public int number;

        public string role = AppSession.RolePlayer;
        public string team = "";
        public bool ready;

        /// <summary>Bağlantı durumu (§2/§5.3): <c>connected</c> | <c>reconnecting</c> | <c>left</c>.
        /// ⚠️ <b>Bu string'i başka hiçbir yerde karşılaştırma</b> — okumanın tek yolu aşağıdaki üç
        /// kısayoldur; üç dosyaya dağılmış bir <c>== "reconnecting"</c> zinciri, bilinmeyen değerin
        /// <c>connected</c> sayılması kuralını er geç bir yerde kaçırır.</summary>
        public string connection = ArenaProtocol.CONNECTION_CONNECTED;

        /// <summary>Son <c>lobby_state</c>'te bildirilen "çıkarılmaya kalan saniye" ve o mesajın
        /// alındığı an. İkisi birlikte tutulur çünkü roster yayını OLAY tabanlıdır: sunucu saniyede
        /// bir güncelleme göndermez, sayacı yerelde biz ilerletiriz
        /// (<see cref="ReconnectSecondsLeft"/>).</summary>
        public int reconnectSeconds;

        /// <inheritdoc cref="reconnectSeconds"/>
        public float reconnectStampedAt = -1f;

        /// <summary>Koşan maçın katılımcısı mı (§10.2) — <c>left</c> satırın maç sonu tablosunda
        /// durmasının sebebi budur.</summary>
        public bool inMatch;

        /// <summary>Soket canlı mı. <b>Bilinmeyen/boş değer bağlı sayılır</b> (§5.3): eski/yeni
        /// sunucu karışımında roster'ı tümden söndürmemek için.</summary>
        public bool IsConnected => !IsReconnecting && !HasLeft;

        /// <summary>Bağlantı koptu, cihaz geri bekleniyor.</summary>
        public bool IsReconnecting => connection == ArenaProtocol.CONNECTION_RECONNECTING;

        /// <summary>Süre doldu, oyuncu oyundan çıkarıldı; satır yalnız maç istatistiği için duruyor.</summary>
        public bool HasLeft => connection == ArenaProtocol.CONNECTION_LEFT;

        /// <summary>Oyuncunun oyundan çıkarılmasına kalan saniye (0 = yok). Sunucudan gelen değer
        /// yerelde geçen süreyle düşülür — yayın olay tabanlı olduğu için aksi hâlde sayaç ancak
        /// başka bir roster değişikliğinde ilerlerdi.</summary>
        public int ReconnectSecondsLeft
        {
            get
            {
                if (!IsReconnecting || reconnectSeconds <= 0)
                {
                    return 0;
                }

                float elapsed = reconnectStampedAt < 0f ? 0f : Time.unscaledTime - reconnectStampedAt;
                return Mathf.Max(0, reconnectSeconds - Mathf.FloorToInt(elapsed));
            }
        }

        public bool alive = true;

        /// <summary>GÖZLÜĞÜN pili (0..1); -1 = bilinmiyor.</summary>
        public float battery = -1f;

        /// <summary>Sol/sağ kumandanın durumu (§5.1 <c>ArenaProtocol.CONTROLLER_*</c>).
        /// <para>⚠️ <b>Yüzde değil DURUM</b> — kumandanın şarjı Quest'te OpenXR altında okunamıyor;
        /// pil yüzdesi <see cref="battery"/> ile gelen <b>gözlüğün</b> pilidir.</para>
        /// <para>Varsayılan <c>0</c> = <c>CONTROLLER_UNKNOWN</c>, yani "bildirilmedi";
        /// <c>battery = -1f</c> ile aynı desen — bilinmeyen değer geçerli aralığın dışındadır ve
        /// asla "sağlıklı" sayılmaz. Admin kayıtlarında daima <c>0</c> kalır.</para></summary>
        public int ctrlL;

        /// <inheritdoc cref="ctrlL"/>
        public int ctrlR;

        public float hp = ArenaProtocol.PLAYER_MAX_HP;
        public int kills;
        public int deaths;

        /// <summary>Bireysel maç skoru (§10.2) — kills DEĞİLDİR; anlamı moda göre değişir.</summary>
        public int score;

        /// <summary>Başlık arena ile hizalı mı (§10.6). Varsayılan <b>true</b>: bilinmeyen durumu
        /// alarm gibi göstermek gürültü üretir, ayrıca admin'in kendi satırı asla "kalibresiz"
        /// sayılmamalıdır (sunucu admin için daima false gönderir).</summary>
        public bool calibrated = true;

        /// <summary>"manual" | "anchor" | "cloud" | "" — doğrulanmayan serbest etiket.</summary>
        public string calibrationSource = "";

        /// <summary>Gövde ölçeği (§10.8); <b>0 = ölçülmemiş</b>. Satırdaki ÖLÇ düğmesi bunu
        /// gösterir — operatör kimin ölçüldüğünü listeye bakarak görmeli.</summary>
        public float bodyScale;

        /// <summary>Operatörün ilgilenmesi gereken satır mı: yalnız OYUNCU ve kalibresiz.</summary>
        public bool NeedsCalibration => IsPlayer && !calibrated;

        public string scene = "";

        // ---- Ağ telemetrisi (§6.7): İSTEMCİ ölçer, sunucu net_stats ile taşır ----
        // ⚠️ Varsayılan -1 = "bilinmiyor" ve 0 ile karıştırılmamalı: 0 ms ping gerçekten mümkün bir
        // ölçüm gibi okunur. Eski sürüm bir gözlük hiç bildirmez ve satırı "-" kalır.
        // battery = -1f ile birebir aynı desen.

        /// <summary>Ölçülen gidiş-dönüş süresi (ms); -1 = bilinmiyor. Panelde PING kolonu.</summary>
        public int rttMs = -1;

        /// <summary>Downlink snapshot jitter'ı (ms); -1 = bilinmiyor. Panelde GÖSTERİLMEZ —
        /// operatörün eyleme çevirebileceği sayı ping'dir; bu teşhis verisidir.</summary>
        public float jitterMs = -1f;

        /// <summary>Downlink snapshot kaybı (%); -1 = bilinmiyor. Panelde gösterilmez (jitterMs ile
        /// aynı gerekçe).</summary>
        public float lossPct = -1f;

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
    /// (ad/rol/takım/hazır/bağlantı durumu/batarya/sahne + <c>kills/deaths/hp/alive</c>); sunucu
    /// ölüm ve canlanmada onu tazeler. Aralarda <c>health_update</c> ve <c>kill_event</c> ile
    /// yerel olarak ilerletilir ki bar ve sayaçlar anında tepki versin. Sapma olursa bir
    /// sonraki <c>lobby_state</c> sunucunun dediğini yazar — sunucu her zaman kazanır.
    /// </para>
    /// <para>
    /// ⚠ <b><c>respawn</c> admin'e GELMEZ</b>: sunucu onu yalnız ölen oyuncunun bağlantısına
    /// yollar (§10.4). Bu yüzden canlanma geri sayımı <c>kill_event</c> zamanı +
    /// <see cref="ArenaProtocol.RESPAWN_DELAY"/> ile YEREL olarak hesaplanır; oyuncu tabanına
    /// girmezse gerçek canlanma sarkar ve bunun bir üst sınırı YOKTUR (sunucu kimseyi zamanla
    /// canlandırmaz) — bu yüzden sayaç 0'a inince "TABANDA BEKLENİYOR"a döner, yanlış "canlandı"
    /// demez.
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
        /// katalogdan okunur (<see cref="AdminSelection.ModeId"/>). Katalog yoksa ya da seçim
        /// henüz açılış tohumundaki <b>lobi profilindeyse</b> (§5.3 — lobi bir maç modu değildir)
        /// <b>sezgisel</b> yedeğe düşülür ("hiçbir çevrimiçi oyuncunun takımı yok"). Bu yedek
        /// TEK BAŞINA yanıltıcıdır — lobide takımı henüz atanmamış TDM maçını FFA gösterir;
        /// yalnız arayüz boş kalmasın diye, son çare olarak durur.
        /// </para>
        /// <para>
        /// Alan değil <b>hesaplanan özelliktir</b>: girdileri (faz, sunucu kuralı, ortak seçim)
        /// roster'dan bağımsız değişiyor — önbelleklenseydi mod değişince bayat kalırdı.
        /// </para>
        /// </summary>
        public bool IsFfa => ResolveIsFfa();

        /// <summary>Sezgisel yedeğin girdisi: BAĞLI oyunculardan en az birinin takımı var mı
        /// (<see cref="Rebuild"/> hesaplar).</summary>
        private bool _anyConnectedTeam;

        /// <summary>Sunucudan gelen faz (§10.1): <c>paused</c> | <c>playing</c> | <c>finished</c>.</summary>
        public string Phase { get; private set; } = ArenaProtocol.PHASE_PAUSED;

        /// <summary>Duraklamanın gerekçesi (§10.1); duraklı değilken boş.</summary>
        public string PhaseReason { get; private set; } = ArenaProtocol.PAUSE_REASON_LOBBY;

        /// <summary>Modun kendi ara durumu (§10.1); çekirdek yorumlamaz.</summary>
        public string ModeState { get; private set; } = "";

        /// <summary>
        /// Mod/harita seçimi şu an değiştirilebilir mi? Harita seçmek TÜM istemcilere sahne
        /// yükletir (§10.7 sahneleme), o yüzden ölçüt <b>"maç kurulmuş mu"</b>dur — "koşuyor mu"
        /// değil. İzin verilen tam iki durum: maç bittiğinde (<c>finished</c>; operatör bir
        /// sonrakini seçebilmeli) ve lobide (maç hiç kurulmamış).
        /// <para>
        /// ⚠️ <b>Yükleme, geri sayım ve duraklatma da KAPALIDIR.</b> Hepsi <c>paused</c> fazında
        /// görünür ama maç çoktan kurulmuştur: yüklenirken sahne değiştirmek kurulmakta olan maçı
        /// yarıda keser, operatörün duraklattığı maç ise donmuş bir <i>canlı</i> maçtır. Çıkış
        /// yolu her ikisinde de İPTAL'dir (<c>abort_match</c>).
        /// </para>
        /// <para>Otorite sunucudadır — aynı kural <c>set_selection</c> işlenirken de uygulanır;
        /// buradaki kopya yalnız operatörü boşuna tıklatmamak içindir.</para>
        /// </summary>
        public bool CanChangeSelection
        {
            get
            {
                if (Phase == ArenaProtocol.PHASE_PLAYING) return false;
                if (Phase == ArenaProtocol.PHASE_FINISHED) return true;

                // paused: yalnız lobi serbest. Boş gerekçe lobi sayılır — sunucu her zaman
                // dolduruyor, ama bir eksiklik seçiciyi kalıcı kilitlemesin.
                return string.IsNullOrEmpty(PhaseReason) ||
                       PhaseReason == ArenaProtocol.PAUSE_REASON_LOBBY;
            }
        }

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
            NetEvents.OnNetStats += HandleNetStats;
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
            NetEvents.OnNetStats -= HandleNetStats;
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
        /// POV için sonraki uygun oyuncu (Tab): yalnız BAĞLI oyuncular arasında playerId
        /// sırasında döner (bağlantısı kopmuş ya da ayrılmış oyuncunun kamerası yoktur).
        /// Hiç oyuncu yoksa 0.
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
                if (candidate.IsConnected)
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
                if (view.IsConnected && view.alive)
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

            Phase = string.IsNullOrEmpty(msg.match.phase) ? ArenaProtocol.PHASE_PAUSED : msg.match.phase;
            PhaseReason = msg.match.phaseReason ?? "";
            ModeState = msg.match.modeState ?? "";
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
                view.number = info.number;
                view.role = string.IsNullOrEmpty(info.role) ? AppSession.RolePlayer : info.role;
                view.team = info.team ?? "";
                view.ready = info.ready;
                // Boş/bilinmeyen değer connected sayılır (§5.3) — kısayolların sözleşmesi budur.
                view.connection = string.IsNullOrEmpty(info.connection)
                    ? ArenaProtocol.CONNECTION_CONNECTED
                    : info.connection;
                view.reconnectSeconds = info.reconnectSeconds;
                view.reconnectStampedAt = Time.unscaledTime;
                view.inMatch = info.inMatch;
                view.battery = info.battery;

                // Kumanda durumu ATANMADAN ÖNCE bildirilir: karşılaştırmanın tek girdisi view'daki
                // önceki değer ve o değer atamayla kaybolur.
                if (view.IsPlayer)
                {
                    AnnounceControllerChange(view, "SOL", view.ctrlL, info.ctrlL);
                    AnnounceControllerChange(view, "SAĞ", view.ctrlR, info.ctrlR);
                }

                view.ctrlL = info.ctrlL;
                view.ctrlR = info.ctrlR;
                view.scene = info.scene ?? "";

                // Sunucu sayaçları yereli EZER (§5.3) — sapma burada kapanır.
                view.kills = info.kills;
                view.deaths = info.deaths;
                view.score = info.score;
                view.hp = info.hp;

                // §10.6 kalibrasyon durumu. Admin kaydında sunucu daima false gönderir; onu
                // "kalibresiz" saymamak için burada true'ya sabitlenir (bkz. NeedsCalibration).
                view.calibrated = view.IsPlayer ? info.calibrated : true;
                view.calibrationSource = info.calibrationSource ?? "";
                view.bodyScale = view.IsPlayer ? info.bodyScale : 0f;

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
            PhaseReason = msg.phaseReason ?? "";
            ModeState = msg.modeState ?? "";
            TimeRemaining = msg.timeRemaining;
            ScoreRed = msg.scoreRed;
            ScoreBlue = msg.scoreBlue;

            if (PhaseReason != ArenaProtocol.PAUSE_REASON_COUNTDOWN)
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
            Phase = ArenaProtocol.PHASE_FINISHED;
            PhaseReason = "";
            Raise();
        }

        private void HandleReturnToLobby(ReturnToLobbyMsg _)
        {
            Phase = ArenaProtocol.PHASE_PAUSED;
            PhaseReason = ArenaProtocol.PAUSE_REASON_LOBBY;
            ModeState = "";
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

        /// <summary>§6.7 — oyuncu başına ping/jitter/kayıp. Yalnız admin bağlantılarına gelir.
        /// <para>Mesajda olmayan oyuncunun değerleri <b>korunur</b>, sıfırlanmaz: sunucu yalnız
        /// çevrimiçi oyuncuları yazıyor ve eksik bir girdiyi "-" yapmak, panelde satırın bir saniye
        /// varıp bir saniye kaybolmasına yol açardı.</para></summary>
        private void HandleNetStats(NetStatsMsg msg)
        {
            if (msg?.players == null)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                NetStatsEntry entry = msg.players[i];
                if (entry == null)
                {
                    continue;
                }

                AdminPlayerView view = Find(entry.playerId);
                if (view == null)
                {
                    continue;
                }

                view.rttMs = entry.rttMs;
                view.jitterMs = entry.jitterMs;
                view.lossPct = entry.lossPct;
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

        /// <summary>
        /// Bir elin kumanda durumu değiştiğinde operatörü BİR KEZ bilgilendirir (§5.1). Kumanda
        /// düşünce el pozu bayatlar ve oyuncu bunu kendi göremez — haber operatöre gitmeli.
        /// <para>
        /// ⚠️ Kapı "durum kötü mü" değil <b>"durum DEĞİŞTİ mi"</b>dir: <c>lobby_state</c> tam bir
        /// anlık görüntüdür ve her yayında tekrarlanır — koşulsuz bildirim, pili biten kumandayı
        /// operatörün ekranına saniyede bir yazardı.
        /// </para>
        /// <para>
        /// ⚠️ <see cref="ArenaProtocol.CONTROLLER_UNKNOWN"/>'dan gelen geçiş bildirilmez: alanı
        /// doldurmayan bir istemcinin ilk raporu aksi hâlde her el için bir "durum değişti"
        /// üretirdi. Aynı sebeple yalnız <c>role=player</c> kayıtları bakılır — admin kumanda
        /// bildirmez, kaydı <c>UNKNOWN</c> kalır.
        /// </para>
        /// <para>
        /// ⚠️ <b>Bildirilen tek olay kaybın kendisidir:</b> düşüş
        /// (<see cref="ArenaProtocol.CONTROLLER_LOST"/>'a giriş) duyurulur, kapanışı da yalnız
        /// ondan ÇIKIŞ duyurur. <see cref="ArenaProtocol.CONTROLLER_UNTRACKED"/> bir arıza değil
        /// olağan bir andır (el arkaya gider, kumanda görüş dışına çıkar) ve onu haber saymak
        /// operatörün durum satırını kullanılamaz hâle getirirdi.
        /// </para>
        /// </summary>
        private static void AnnounceControllerChange(AdminPlayerView view, string hand,
            int previous, int current)
        {
            if (previous == current || previous == ArenaProtocol.CONTROLLER_UNKNOWN)
            {
                return;
            }

            if (current == ArenaProtocol.CONTROLLER_LOST)
            {
                string lost = $"{view.name} (#{view.playerId}) {hand} kumanda düştü — " +
                              "pil bitmiş olabilir; o elin pozu bayat çizilir.";
                AdminCommands.Note(lost);
                Debug.LogWarning(lost);
                return;
            }

            // ⚠️ Toparlanma YALNIZ düşmüş bir kumanda için bildirilir. UNTRACKED sahada sürekli
            // olur (el arkaya gider, kumanda görüş dışına çıkar, yere bırakılır) ve o geçişi
            // "geri geldi" saymak operatörün durum satırını hiç susmayan bir akışa çevirirdi —
            // hiç düşmemiş bir kumandanın izlenmeye dönmesi zaten bir haber değildir. Bildirimin
            // simetrisi bu yüzden OK'a değil LOST'a bakar: yalnız duyurulmuş bir kayıp kapanır.
            if (previous == ArenaProtocol.CONTROLLER_LOST)
            {
                string back = $"{view.name} (#{view.playerId}) {hand} kumanda geri bağlandı.";
                AdminCommands.Note(back);
                Debug.Log(back);
            }
        }

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

                if (view.IsConnected && !string.IsNullOrEmpty(view.team))
                {
                    anyTeam = true;
                }
            }

            _anyConnectedTeam = anyTeam;

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
            // "Maç yüklendi" = lobi bekleyişinde DEĞİLİZ; yükleme/geri sayım/duraklatma/koşma/bitiş
            // hepsinde kural gerçektir.
            if (PhaseReason != ArenaProtocol.PAUSE_REASON_LOBBY)
            {
                return ModeRuntime.Teams == ModeTeamMode.None;
            }

            // (2) Lobide henüz kural yok: operatörün seçtiği mod ne diyor?
            // ⚠️ Lobi profili ATLANIR: sunucu açılışta ortak seçimi mekanın lobi haritasıyla
            // tohumluyor (§5.3), yani hiçbir operatör mod seçmeden önce ModeId "lobby" olur.
            // Lobi bir MAÇ modu değildir (mod seçicisinde de yoktur) — "bir sonraki maçta takım
            // var mı" sorusunu cevaplayamaz, cevaplarsa boş lobiyi kendi takım kipiyle boyar.
            ModeDefinition selected = AdminContent.Catalog != null
                ? AdminContent.Catalog.FindMode(AdminSelection.ModeId)
                : null;
            if (selected != null && !selected.IsLobbyProfile)
            {
                return selected.TeamMode == ModeTeamMode.None;
            }

            // (3) Katalog/seçim yok — arayüz boş kalmasın.
            return !_anyConnectedTeam;
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
