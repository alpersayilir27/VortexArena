#nullable enable
using VortexArena.Protocol;
using VortexArena.Server.Core.Modes;

namespace VortexArena.Server.Core;

/// <summary>Maç fazları (§5.3): Lobby → Loading → Countdown → Live → End → Lobby.</summary>
public enum Phase
{
    Lobby,
    Loading,
    Countdown,
    Live,
    End
}

/// <summary>Sunucu-otoriter maç akışı (§10): faz makinesi, kişisel load_match, countdown,
/// match_state yayını, vuruş doğrulama hattı, skor ve free-roam canlanma. İstemci sunum+girdidir;
/// hasar uygulamaz, skor tutmaz, faz değiştirmez.
///
/// <para><b>Kilit sözleşmesi:</b> tüm maç durumu (faz, skor, süre + PlayerState'in maç alanları)
/// <c>_gate</c> altında okunur/yazılır. Kilit altındayken ASLA await edilmez ve mesaj
/// GÖNDERİLMEZ: mesajlar kilit altında kurulup <c>outbox</c>'a yazılır, kilit bırakıldıktan sonra
/// yollanır. Aynı sebeple <c>IGameMode</c> kancaları ve event tetikleyen registry metodları
/// (SetTeam/SetReady) kilit DIŞINDA çağrılır — modların kullandığı public API (ScoreRed,
/// AddScore, OnlinePlayers…) kendi kilidini alır, yeniden giriş olmaz.
/// PlayerRegistry.Snapshot()/TryGetByPlayerId registry kilidini ALMAZ (ConcurrentDictionary),
/// bu yüzden _gate altından çağrılmaları güvenlidir.</para></summary>
public sealed class MatchDirector
{
    /// <summary>Maç tick'i 10 Hz: countdown/süre/zorla canlandırma çözünürlüğü için yeterli,
    /// snapshot döngüsünden (20 Hz) bağımsız.</summary>
    private const int TickIntervalMs = 100;

    /// <summary>Kilit altında kurulup kilit dışında yollanan tek gönderim.</summary>
    private readonly record struct Outgoing(ClientConnection Connection, string Json, string Who);

    private readonly object _gate = new();
    private readonly PlayerRegistry _registry;

    /// <summary>Harita kataloğu (config/maps.json — Unity export'u). BOŞ olabilir: o zaman
    /// harita doğrulaması ve spawn slot sınırı devre dışıdır (§10.1).</summary>
    private readonly MapTable _maps;

    private readonly Dictionary<string, IGameMode> _modes = new(StringComparer.Ordinal);

    /// <summary>Kilit altında toplanan "ready bayrağını sıfırla" işleri; registry.SetReady event
    /// tetiklediği için (lobby_state yayını) kilit DIŞINDA uygulanır.</summary>
    private readonly List<string> _readyClearQueue = new();

    /// <summary>Bu maç en az bir oyuncuyla mı başladı? Loading'de "oyuncu kalmadı" durumunun
    /// nasıl yorumlanacağını belirler (§10.1): oyuncusuz başlatılan maç admin harita
    /// önizlemesidir ve kendiliğinden lobiye DÖNMEZ.</summary>
    private bool _startedWithPlayers;

    /// <summary>Kilit altında işaretlenen "roster tazelensin" isteği. hp/alive/kills/deaths
    /// lobby_state ile taşınıyor (§5.3) ve admin istatistik tablosunun sağlama noktası bu.
    /// registry.Announce olay tetiklediği için kilit DIŞINDA (FlushRosterRefresh) uygulanır;
    /// Announce imzası bir PlayerState istediği için bayrak yerine son değişen oyuncu tutulur.</summary>
    private PlayerState? _rosterRefreshFor;

    /// <summary>Vuruş reddi logu için atıcı başına kısıtlama aralığı: ölü hedefe ateş sürerken
    /// (gerçek oyuncuda da olur) konsol boğulmasın.</summary>
    private const double RejectLogIntervalSeconds = 2.0;

    // Log durumu — MAÇ durumu DEĞİL, bu yüzden PlayerState'te değil burada ve kendi küçük kilidi
    // altında tutulur. _rejectLogGate ASLA _gate almaz (yalnız _gate → _rejectLogGate yönü var).
    private readonly object _rejectLogGate = new();
    private readonly Dictionary<int, DateTime> _lastRejectLogAt = new();
    private readonly Dictionary<int, int> _suppressedRejects = new();

    private Phase _phase = Phase.Lobby;
    private string _modeId = "";
    private string _sceneName = "";
    private float _timeRemaining;
    private int _scoreRed;
    private int _scoreBlue;
    private int _roundSeconds;
    private int _scoreLimit;
    private IGameMode? _mode;

    /// <summary>Koşan maçın kural şekli (§10.5). Maç yokken TDM varsayılanıdır — bu sayede
    /// lobide de anlamlı bir cevap vardır ve her okuyucunun null kontrolü yapması gerekmez.</summary>
    private ModeRules _rules = ModeRules.TeamDefault;

    private DateTime _phaseEnteredAt = DateTime.UtcNow;

    /// <summary>1 Hz işler (countdown geri sayımı + Live'da match_state) için sonraki eşik.</summary>
    private DateTime _nextSecondAt = DateTime.UtcNow;

    private int _countdownRemaining;

    /// <summary>Live'a girildi; IGameMode.OnMatchStart kilit dışında çağrılacak.</summary>
    private bool _matchStartPending;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public MatchDirector(PlayerRegistry registry, MapTable maps)
    {
        _registry = registry;
        _maps = maps;
        RegisterModes();
    }

    /// <summary>Sunucunun tanıdığı modların TEK kayıt yeri — yeni mod buraya bir satır eklenir
    /// (CLAUDE.md "Yeni mod" reçetesi). Kayıtlı olmayan modId'li start_match reddedilir.</summary>
    private void RegisterModes()
    {
        Register(new TdmMode());
        Register(new FfaMode());
    }

    private void Register(IGameMode mode) => _modes[mode.ModeId] = mode;

    /// <summary>Kayıtlı mod kimlikleri (açılış özeti / red mesajları için).</summary>
    public IReadOnlyCollection<string> ModeIds => _modes.Keys;

    public Phase CurrentPhase
    {
        get { lock (_gate) return _phase; }
    }

    // ---- IGameMode'ların kullandığı public API (hepsi kilit güvenli, kilit DIŞINDAN çağrılır) ----

    public int ScoreRed
    {
        get { lock (_gate) return _scoreRed; }
    }

    public int ScoreBlue
    {
        get { lock (_gate) return _scoreBlue; }
    }

    public float TimeRemaining
    {
        get { lock (_gate) return _timeRemaining; }
    }

    public int RoundSeconds
    {
        get { lock (_gate) return _roundSeconds; }
    }

    public int ScoreLimit
    {
        get { lock (_gate) return _scoreLimit; }
    }

    /// <summary>Koşan maçın kural şekli (§10.5); maç yokken TDM varsayılanı.</summary>
    public ModeRules Rules
    {
        get { lock (_gate) return _rules; }
    }

    // ---- Skor defteri (§10.2) ----
    // Modlar skoru YALNIZ buradan yazar/okur; iki kanal var ve hangisinin kullanılacağını
    // ModeRules.Scoring söyler: takım skoru (match_state) veya bireysel skor (lobby_state).
    // Tek bölümde toplanmalarının sebebi ileride ayrı bir Scoreboard sınıfına çıkarmanın
    // mekanik bir taşıma olması — faz makinesi bu yüzden büyütülmedi.

    /// <summary>Takım skoruna ekleme (kill/objektif kuralları modlardan gelir).</summary>
    public void AddScore(string team, int amount)
    {
        lock (_gate)
        {
            if (team == "red") _scoreRed += amount;
            else if (team == "blue") _scoreBlue += amount;
        }
    }

    /// <summary>Bireysel skora ekleme (§10.2 <c>score</c>). Yalnız "roster tazelensin" bayrağını
    /// koyar, yayını KENDİ yapmaz: bu metod mod kancasından (kilit dışı) çağrılıyor ve yayın
    /// registry olayı tetikliyor. Bayrak bir sonraki tik'te (≤100 ms) boşaltılır — skorun
    /// lobby_state'e ulaşması için ayrı bir mesaj tipi ya da yayın döngüsü gerekmez.</summary>
    public void AddPlayerScore(int playerId, int amount)
    {
        if (playerId <= 0 || amount == 0) return;
        lock (_gate)
        {
            if (!_registry.TryGetByPlayerId(playerId, out var player)) return;
            if (player.Role != "player") return;
            player.Score += amount;
            _rosterRefreshFor = player;
        }
    }

    /// <summary>Bir oyuncunun bireysel skoru; oyuncu yoksa 0.</summary>
    public int ScoreOf(int playerId)
    {
        lock (_gate)
        {
            return _registry.TryGetByPlayerId(playerId, out var player) ? player.Score : 0;
        }
    }

    /// <summary>Bireysel skorun lideri. <b>Eşitlikte false döner</b> (tek kazanan yok) — çağıran
    /// mod bunu "berabere" olarak yorumlar; sessizce ilk oyuncuyu seçmek yanlış kazanan ilan
    /// ederdi. Hiç çevrimiçi oyuncu yoksa da false.</summary>
    public bool TryGetLeader(out int playerId, out int score)
    {
        playerId = 0;
        score = 0;

        lock (_gate)
        {
            var tied = false;
            foreach (var player in OnlinePlayersLocked())
            {
                if (playerId == 0 || player.Score > score)
                {
                    playerId = player.PlayerId;
                    score = player.Score;
                    tied = false;
                    continue;
                }

                if (player.Score == score) tied = true;
            }

            if (playerId != 0 && !tied) return true;
        }

        playerId = 0;
        score = 0;
        return false;
    }

    /// <summary>Çevrimiçi oyuncuların (role=player) anlık kopyası — mod bunu kilit dışında
    /// gezer; PlayerState alanlarının okunması sırasında değişebilir (int/string okuması atomik).</summary>
    public IEnumerable<PlayerState> OnlinePlayers()
    {
        lock (_gate) return OnlinePlayersLocked();
    }

    /// <summary>welcome.match anlık görüntüsü — geç katılım senkronu bunu kullanır (§5.3).</summary>
    public MatchInfo CurrentMatchInfo()
    {
        lock (_gate) return BuildMatchInfoLocked();
    }

    // ---- Tick döngüsü ----

    public void Start()
    {
        if (_loop is { IsCompleted: false }) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => TickLoopAsync(token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _loop = null;
    }

    private async Task TickLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickIntervalMs));
        var last = DateTime.UtcNow;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token)) break;
            }
            catch (OperationCanceledException) { break; }

            var now = DateTime.UtcNow;
            var delta = (float)(now - last).TotalSeconds;
            last = now;
            try
            {
                await TickAsync(now, delta);
            }
            catch (Exception ex)
            {
                // Tek bir tik hatası maç döngüsünü öldürmesin.
                Console.WriteLine($"[match] tick hatası: {ex.Message}");
            }
        }
    }

    private async Task TickAsync(DateTime now, float deltaSeconds)
    {
        var outbox = new List<Outgoing>();
        IGameMode? modeToStart;
        IGameMode? modeToTick = null;

        lock (_gate)
        {
            switch (_phase)
            {
                case Phase.Loading:
                    TickLoadingLocked(outbox, now);
                    break;
                case Phase.Countdown:
                    TickCountdownLocked(outbox, now);
                    break;
                case Phase.Live:
                    modeToTick = TickLiveLocked(outbox, now, deltaSeconds);
                    break;
                case Phase.End:
                    TickEndLocked(outbox, now);
                    break;
            }
            modeToStart = _matchStartPending ? _mode : null;
            _matchStartPending = false;
        }

        await FlushAsync(outbox);
        FlushReadyClear();
        FlushRosterRefresh();

        // Mod kancaları kilit DIŞINDA (yukarıdaki kilit sözleşmesi).
        modeToStart?.OnMatchStart(this);
        if (modeToTick == null) return;
        modeToTick.OnTick(this, deltaSeconds);
        if (modeToTick.IsMatchOver(this, out var outcome))
            await EnterEndAsync(outcome);
    }

    /// <summary>Tüm çevrimiçi oyuncular "sahne yüklendi" (set_ready) dediğinde veya
    /// LOADING_TIMEOUT dolduğunda Countdown'a geçilir. Çevrimdışı oyuncu beklenmez.</summary>
    private void TickLoadingLocked(List<Outgoing> outbox, DateTime now)
    {
        var players = OnlinePlayersLocked();
        if (players.Count == 0)
        {
            // Ayrım önemli (§10.1): oyuncularla BAŞLAMIŞ maçta son oyuncu da düştüyse maçı
            // bırakıp lobiye dönmek doğru. Oyuncusuz BAŞLATILMIŞ maç (admin harita önizlemesi)
            // ise beklenecek kimse olmadığı için doğrudan Countdown'a geçer; çıkış operatörün
            // abort_match/return_to_lobby komutudur.
            if (_startedWithPlayers)
            {
                Console.WriteLine("[match] loading: çevrimiçi oyuncu kalmadı — lobiye dönülüyor.");
                EnterLobbyLocked(outbox, now);
                return;
            }

            EnterCountdownLocked(outbox, now);
            return;
        }

        var notReady = players.Where(p => !p.Ready).ToList();
        if (notReady.Count == 0)
        {
            EnterCountdownLocked(outbox, now);
            return;
        }

        if ((now - _phaseEnteredAt).TotalSeconds >= ArenaProtocol.LOADING_TIMEOUT)
        {
            Console.WriteLine($"[match] loading zaman aşımı ({ArenaProtocol.LOADING_TIMEOUT:0} sn) — " +
                              $"hazır olmayanlar: {string.Join(", ", notReady.Select(p => p.Name))}");
            EnterCountdownLocked(outbox, now);
        }
    }

    private void TickCountdownLocked(List<Outgoing> outbox, DateTime now)
    {
        if (now < _nextSecondAt) return;
        _nextSecondAt = now.AddSeconds(1);
        _countdownRemaining--;
        if (_countdownRemaining > 0)
        {
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(new CountdownMsg { seconds = _countdownRemaining }));
            return;
        }
        EnterLiveLocked(outbox, now);
    }

    /// <summary>Süreyi işletir, zorla canlandırmaları uygular, 1 Hz match_state yayınlar.
    /// Dönüş: OnTick/IsMatchOver için kilit dışında kullanılacak mod (yoksa null).</summary>
    private IGameMode? TickLiveLocked(List<Outgoing> outbox, DateTime now, float deltaSeconds)
    {
        _timeRemaining = MathF.Max(0f, _timeRemaining - deltaSeconds);

        // §10.4/4: talep gelmese de REVIVE_GRACE sonunda canlandır (takılan istemci maçı kilitlemesin).
        foreach (var player in OnlinePlayersLocked())
        {
            if (player.Alive) continue;
            if ((now - player.DiedAt).TotalSeconds < ArenaProtocol.REVIVE_GRACE) continue;
            RevivePlayerLocked(outbox, player);
            Console.WriteLine($"[match] zorla canlandırma: {player.Name}");
        }

        if (now >= _nextSecondAt)
        {
            _nextSecondAt = now.AddSeconds(1);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }
        return _mode;
    }

    private void TickEndLocked(List<Outgoing> outbox, DateTime now)
    {
        if ((now - _phaseEnteredAt).TotalSeconds < ArenaProtocol.MATCH_END_SECONDS) return;
        EnterLobbyLocked(outbox, now);
    }

    // ---- Admin komutları ----

    /// <summary>start_match doğrulaması + kişisel load_match yayını (§10.1). Doğrulama geçmezse
    /// faz DEĞİŞMEZ, konsola sebep yazılır.
    /// <para><paramref name="roundSeconds"/>/<paramref name="scoreLimit"/> O MAÇA özeldir:
    /// <c>≤ 0</c> ise modun varsayılanı kullanılır (§5.2). Operatör raundu kısaltıp uzatabilsin
    /// diye <see cref="IGameMode"/> üzerindeki sayılar kilit değil varsayılandır.</para></summary>
    public async Task StartMatchAsync(string? modeId, string? sceneName, int roundSeconds = 0, int scoreLimit = 0)
    {
        modeId ??= "";
        sceneName ??= "";

        if (!_modes.TryGetValue(modeId, out var mode))
        {
            Console.WriteLine($"[match] start_match reddedildi: '{modeId}' modu kayıtlı değil (kayıtlı: {string.Join(", ", _modes.Keys)}).");
            return;
        }
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Console.WriteLine("[match] start_match reddedildi: sceneName boş.");
            return;
        }

        // Harita tablosu (config/maps.json — Unity export'u) doluysa sahne + mod uyumu doğrulanır.
        // Tablo boşsa (dosya yok) bu adım tümüyle atlanır → Faz 3 davranışı korunur.
        MapEntry? map = null;
        if (!_maps.IsEmpty)
        {
            if (!_maps.TryGet(sceneName, out var known))
            {
                Console.WriteLine($"[match] start_match reddedildi: '{sceneName}' harita tablosunda yok (bilinen: {string.Join(", ", _maps.SceneNames)}).");
                return;
            }
            if (!MapTable.SupportsMode(known, modeId))
            {
                Console.WriteLine($"[match] start_match reddedildi: '{sceneName}' haritası '{modeId}' modunu desteklemiyor (desteklenen: {string.Join(", ", known.modes)}).");
                return;
            }
            map = known;
        }

        var players = _registry.Snapshot()
            .Where(p => p.Online && p.Role == "player")
            .OrderBy(p => p.PlayerId)
            .ToList();

        var missing = players.Where(p => !p.Scenes.Contains(sceneName)).Select(p => p.Name).ToList();
        if (missing.Count > 0)
        {
            Console.WriteLine($"[match] start_match reddedildi: '{sceneName}' sahnesi şu istemcilerin build listesinde yok — {string.Join(", ", missing)}.");
            return;
        }

        // Oyuncusuz başlatmaya İZİN VERİLİR: admin gözlemci haritayı boş arenada açabilsin (§10.1).
        if (players.Count == 0)
            Console.WriteLine("[match] uyarı: hiç oyuncu yok — maç yalnız admin gözlemci için başlatılıyor (harita önizleme).");
        else if (players.Count == 1)
            Console.WriteLine("[match] uyarı: tek oyuncuyla maç başlatılıyor (yalnız test amaçlı).");

        // Takım kurulumu modun şeklinden gelir (§10.5). registry.SetTeam event tetiklediği için
        // ikisi de kilit DIŞINDA çağrılır. Dengeleme yalnız 2+ oyuncuda anlamlıdır.
        var rules = mode.Rules;
        if (rules.Teams == TeamMode.None)
            ClearTeams(players);
        else if (players.Count > 1)
            BalanceTeams(players);

        // Lobi ready bayrakları Loading'e GİRMEDEN sıfırlanır: Loading'de aynı bayrak "sahne
        // yüklendi" anlamına geliyor, bayat true kalsaydı countdown anında başlardı (§10.1).
        foreach (var player in players.Where(p => p.Ready).ToList())
            _registry.SetReady(player.DeviceId, false);

        var outbox = new List<Outgoing>();
        var teamless = rules.Teams == TeamMode.None;
        // Admin verdiyse o maça özel değer, vermediyse modun varsayılanı (§5.2).
        var appliedRound = roundSeconds > 0 ? roundSeconds : mode.DefaultRoundSeconds;
        var appliedLimit = scoreLimit > 0 ? scoreLimit : mode.DefaultScoreLimit;

        lock (_gate)
        {
            if (_phase != Phase.Lobby)
                Console.WriteLine($"[match] start_match: faz {_phase} — mevcut maç iptal edilip yenisi kuruluyor.");

            _mode = mode;
            _rules = rules;
            _modeId = mode.ModeId;
            _sceneName = sceneName;
            _roundSeconds = appliedRound;
            _scoreLimit = appliedLimit;
            _scoreRed = 0;
            _scoreBlue = 0;
            _timeRemaining = _roundSeconds;
            _matchStartPending = false;
            _startedWithPlayers = players.Count > 0;

            var rulesInfo = _rules.ToInfo();

            foreach (var player in players)
            {
                ResetMatchStateLocked(player);

                var connection = player.Connection;
                if (connection == null) continue;
                // load_match kişiselleştirilir: her oyuncuya kendi takımı (§10.1). Konum/slot
                // taşınmaz — oyuncu fiziksel olarak nerede duruyorsa orada kalır (§10.4).
                var load = new LoadMatchMsg
                {
                    modeId = _modeId,
                    sceneName = _sceneName,
                    roundSeconds = _roundSeconds,
                    scoreLimit = _scoreLimit,
                    yourTeam = player.Team,
                    rules = rulesInfo
                };
                outbox.Add(new Outgoing(connection, JsonUtil.Serialize(load), player.Name));
            }

            // Adminler de aynı sahneyi yükler (gözlemci görünümü, §2): takım anlamsız olduğu için
            // boş gider ve admin karşılığında set_ready GÖNDERMEZ — Loading kapısı yalnız
            // role=player bağlantılarını sayar (OnlinePlayersLocked). Kurallar admin'e de gider:
            // takım kipi admin arayüzünün tek/çift kolon kararını besler.
            var adminLoad = JsonUtil.Serialize(new LoadMatchMsg
            {
                modeId = _modeId,
                sceneName = _sceneName,
                roundSeconds = _roundSeconds,
                scoreLimit = _scoreLimit,
                yourTeam = "",
                rules = rulesInfo
            });
            foreach (var admin in _registry.Snapshot())
            {
                if (!admin.Online || admin.Role != "admin") continue;
                var adminConnection = admin.Connection;
                if (adminConnection == null) continue;
                outbox.Add(new Outgoing(adminConnection, adminLoad, admin.Name));
            }

            SetPhaseLocked(Phase.Loading, DateTime.UtcNow);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }

        var mapInfo = map == null ? "" : $" ({map.sizeX:0.#}×{map.sizeZ:0.#})";
        // Takım dağılımı BalanceTeams/ClearTeams sonrasındaki GERÇEK durumdan sayılır
        // (players listesi PlayerState referansları tutuyor, SetTeam onları yerinde günceller).
        var blueCount = players.Count(p => p.Team == "blue");
        var teamInfo = teamless ? "takımsız" : $"kırmızı {players.Count - blueCount} / mavi {blueCount}";
        Console.WriteLine($"[match] start_match: mod '{mode.ModeId}', sahne '{sceneName}'{mapInfo}, " +
                          $"{appliedRound} sn / limit {appliedLimit}, {players.Count} oyuncu ({teamInfo}).");
        await FlushAsync(outbox);
    }

    /// <summary>abort_match — her fazdan Lobby'ye (§10.1).</summary>
    public Task AbortMatchAsync() => BackToLobbyAsync("abort_match");

    /// <summary>return_to_lobby — abort ile aynı iş (§10.1).</summary>
    public Task ReturnToLobbyAsync() => BackToLobbyAsync("return_to_lobby");

    private async Task BackToLobbyAsync(string reason)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            Console.WriteLine($"[match] {reason} → lobiye dönülüyor (faz {_phase}).");
            EnterLobbyLocked(outbox, DateTime.UtcNow);
        }
        await FlushAsync(outbox);
        FlushReadyClear();
        FlushRosterRefresh();
    }

    // ---- Savaş hattı (§10.3) ----

    /// <summary>shot_fired doğrulanmaz, yalnız relay edilir: faz Live + atıcı hayatta player ise
    /// playerId eklenip ATAN HARİÇ herkese gönderilir.</summary>
    public async Task HandleShotFiredAsync(PlayerState shooter, ShotFiredMsg msg)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Live) return;
            if (shooter.Role != "player" || !shooter.Alive) return;

            var relay = new ShotFiredMsg
            {
                playerId = shooter.PlayerId, // relay'de seq taşınmaz (§5.3)
                weaponId = msg.weaponId ?? "",
                muzzlePos = msg.muzzlePos ?? new float[3],
                muzzleDir = msg.muzzleDir ?? new float[3]
            };
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(relay), exceptPlayerId: shooter.PlayerId);
        }
        await FlushAsync(outbox);
    }

    /// <summary>hit_report hattı (§10.3, sırayla): faz → atıcı → hedef → takım → hasar sayısı.
    /// Herhangi biri düşerse tek satır log + sessiz ret (istemciye yanıt yok).
    /// <para>Bunlar HİLE denetimi değil, durum tutarlılığı kontrolleridir — ürün gözetimli özel
    /// alanda çalıştığı için hile koruması bilinçli olarak yoktur (§10.3). Hasarı istemci hesaplar
    /// ve sunucu aynen uygular; silah tablosu, weaponId beyaz listesi ve atış hızı denetimi
    /// KALDIRILDI (meşru saçma/patlama/yaylım vuruşlarını düşürüyordu).</para></summary>
    public async Task HandleHitReportAsync(PlayerState shooter, HitReportMsg msg)
    {
        // Registry araması kilitsiz (ConcurrentDictionary) — kilit almadan önce hallediyoruz.
        if (!_registry.TryGetByPlayerId(msg.targetPlayerId, out var target))
        {
            RejectHit(shooter, msg.targetPlayerId, $"hedef {msg.targetPlayerId} bulunamadı");
            return;
        }

        var outbox = new List<Outgoing>();
        IGameMode? mode;
        float appliedDamage;
        var killed = false;
        string weaponId;

        lock (_gate)
        {
            if (_phase != Phase.Live)
            {
                RejectHit(shooter, msg.targetPlayerId, $"faz {_phase}");
                return;
            }
            if (!shooter.Online || shooter.Role != "player" || !shooter.Alive)
            {
                RejectHit(shooter, msg.targetPlayerId, "atıcı ölü/oyuncu değil");
                return;
            }
            if (target.PlayerId == shooter.PlayerId)
            {
                RejectHit(shooter, msg.targetPlayerId, "kendini hedefledi");
                return;
            }
            if (!target.Online || target.Role != "player" || !target.Alive)
            {
                RejectHit(shooter, msg.targetPlayerId, "hedef ölü/çevrimdışı");
                return;
            }
            if (!_rules.FriendlyFire && AreTeammates(shooter, target))
            {
                RejectHit(shooter, msg.targetPlayerId, "dost ateşi yok");
                return;
            }
            // Hasarı İSTEMCİ hesaplar (mesafeye göre düşen patlama, yay çekiş gücü, kafa vuruşu…)
            // ve sunucu aynen uygular. Tek kontrol sayının kullanılabilir olması: NaN/∞ canı kalıcı
            // bozar (NaN'a düşen hp bir daha 0'ın altına inemez → oyuncu ölümsüz kalır), negatif
            // hasar da can doldurur. Bu bir hile denetimi değil, sayı denetimidir.
            if (!float.IsFinite(msg.damage) || msg.damage <= 0f)
            {
                RejectHit(shooter, msg.targetPlayerId, $"geçersiz hasar {msg.damage}");
                return;
            }

            var now = DateTime.UtcNow;
            weaponId = msg.weaponId ?? "";
            appliedDamage = msg.damage;
            target.Hp = MathF.Max(0f, target.Hp - appliedDamage);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(new HealthUpdateMsg
            {
                playerId = target.PlayerId,
                hp = target.Hp,
                attackerId = shooter.PlayerId
            }));

            if (target.Hp <= 0f)
            {
                killed = true;
                target.Alive = false;
                target.DiedAt = now;
                target.Deaths++;
                shooter.Kills++;
                _rosterRefreshFor = target; // K/D + alive değişti → lobby_state tazelenir (§5.3)

                QueueBroadcastLocked(outbox, JsonUtil.Serialize(new KillEventMsg
                {
                    killerId = shooter.PlayerId,
                    victimId = target.PlayerId,
                    weaponId = weaponId // doğrulanmayan serbest etiket (kill feed / istatistik)
                }));

                var victimConnection = target.Connection;
                if (victimConnection != null)
                {
                    var respawn = new RespawnMsg
                    {
                        playerId = target.PlayerId,
                        delaySeconds = _rules.RespawnDelay
                    };
                    outbox.Add(new Outgoing(victimConnection, JsonUtil.Serialize(respawn), target.Name));
                }
            }

            mode = _mode;
        }

        await FlushAsync(outbox);
        FlushRosterRefresh(); // ölüm olduysa K/D + alive'ı roster'a yansıtır (düz hasarda no-op)

        // Kabul edilen hasar konsola YAZILMAZ (saniyede onlarca satır olur) — yalnız öldürme + ret.
        mode?.OnHitApplied(this, shooter.PlayerId, target.PlayerId, appliedDamage, killed);
        if (!killed) return;

        mode?.OnKill(this, shooter.PlayerId, target.PlayerId, weaponId);
        Console.WriteLine($"[match] öldürme: {shooter.Name} → {target.Name} ({weaponId}) — skor kırmızı {ScoreRed} : mavi {ScoreBlue}");
        // Maç sonu kontrolü tick döngüsünde (≤100 ms) yapılır; burada faz değiştirmiyoruz.
    }

    /// <summary>revive_request (§10.4): faz Live + oyuncu ölü + gecikme dolmuş ise canlandırır.
    /// Koşul tutmazsa SESSİZ yok sayılır — istemci canlanana dek ~1 sn'de bir tekrarlar, loglasak
    /// konsolu doldururdu.
    /// <para><b><see cref="ReviveAnchor"/> burada DOĞRULANMAZ</b> (§10.4 notu): "tabanda mı / sabit
    /// mi durdu" kararı istemcinindir — sunucu hakemlik değil defter tutar (§10.3 felsefesi).
    /// <see cref="ArenaProtocol.REVIVE_GRACE"/> zorla canlandırma güvenlik ağı her iki şartta da
    /// aynen işler.</para></summary>
    public async Task HandleReviveRequestAsync(PlayerState player)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Live || player.Role != "player" || player.Alive) return;
            if ((DateTime.UtcNow - player.DiedAt).TotalSeconds < _rules.RespawnDelay) return;
            RevivePlayerLocked(outbox, player);
            Console.WriteLine($"[match] canlandı: {player.Name}");
        }
        await FlushAsync(outbox);
        FlushRosterRefresh(); // hp/alive değişti → admin tablosu için roster tazelenir (§5.3)
    }

    // ---- Faz geçişleri (hepsi _gate altında çağrılır) ----

    private void SetPhaseLocked(Phase next, DateTime now)
    {
        if (_phase != next) Console.WriteLine($"[match] faz {_phase} → {next}");
        _phase = next;
        _phaseEnteredAt = now;
    }

    private void EnterCountdownLocked(List<Outgoing> outbox, DateTime now)
    {
        SetPhaseLocked(Phase.Countdown, now);
        _countdownRemaining = ArenaProtocol.COUNTDOWN_SECONDS;
        _nextSecondAt = now.AddSeconds(1);
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(new CountdownMsg { seconds = _countdownRemaining }));
    }

    private void EnterLiveLocked(List<Outgoing> outbox, DateTime now)
    {
        SetPhaseLocked(Phase.Live, now);
        _timeRemaining = _roundSeconds;
        _nextSecondAt = now.AddSeconds(1);
        // §10.2: Live'a girerken herkes tam can + canlı.
        foreach (var player in OnlinePlayersLocked()) ResetMatchStateLocked(player, keepScore: true);
        _matchStartPending = _mode != null;
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
    }

    private void EnterEndLocked(List<Outgoing> outbox, DateTime now, MatchOutcome outcome)
    {
        SetPhaseLocked(Phase.End, now);
        Console.WriteLine($"[match] maç sonu — kazanan: {DescribeOutcomeLocked(outcome)} " +
                          $"(kırmızı {_scoreRed} : mavi {_scoreBlue})");
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(new MatchEndMsg
        {
            winnerTeam = outcome.WinnerTeam,
            winnerPlayerId = outcome.WinnerPlayerId,
            scoreRed = _scoreRed,
            scoreBlue = _scoreBlue
        }));
    }

    /// <summary>Konsol satırı için kazananın okunabilir hâli (takım adı / oyuncu adı / berabere).</summary>
    private string DescribeOutcomeLocked(MatchOutcome outcome)
    {
        if (!string.IsNullOrEmpty(outcome.WinnerTeam)) return outcome.WinnerTeam;
        if (outcome.WinnerPlayerId <= 0) return "berabere";
        return _registry.TryGetByPlayerId(outcome.WinnerPlayerId, out var winner)
            ? $"{winner.Name} (#{winner.PlayerId}, {winner.Score} puan)"
            : $"oyuncu {outcome.WinnerPlayerId}";
    }

    private void EnterLobbyLocked(List<Outgoing> outbox, DateTime now)
    {
        SetPhaseLocked(Phase.Lobby, now);
        _mode = null;
        // Kurallar TDM varsayılanına döner: lobide de anlamlı bir cevap olsun (welcome.match.rules).
        _rules = ModeRules.TeamDefault;
        _modeId = "";
        _sceneName = "";
        _timeRemaining = 0f;
        _scoreRed = 0;
        _scoreBlue = 0;
        _roundSeconds = 0;
        _scoreLimit = 0;
        _countdownRemaining = 0;
        _matchStartPending = false;

        foreach (var player in _registry.Snapshot())
        {
            if (player.Role != "player") continue;
            ResetMatchStateLocked(player);
            QueueReadyClearLocked(player);
        }

        QueueBroadcastLocked(outbox, JsonUtil.Serialize(new ReturnToLobbyMsg()));
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
    }

    /// <summary>Tick dışından (mod IsMatchOver) çağrılır; araya abort girmişse no-op.</summary>
    private async Task EnterEndAsync(MatchOutcome outcome)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Live) return;
            EnterEndLocked(outbox, DateTime.UtcNow, outcome);
        }
        await FlushAsync(outbox);
        FlushRosterRefresh();
    }

    // ---- Yardımcılar ----

    private void ResetMatchStateLocked(PlayerState player, bool keepScore = false)
    {
        player.Hp = ArenaProtocol.PLAYER_MAX_HP;
        player.Alive = true;
        player.DiedAt = DateTime.MinValue;
        _rosterRefreshFor = player;
        if (keepScore) return;
        player.Kills = 0;
        player.Deaths = 0;
        player.Score = 0;
    }

    /// <summary>Dost ateşi kararının TEK yeri. <b>Boş takım asla takım arkadaşı sayılmaz:</b>
    /// takımsız modda herkesin takımı <c>""</c> olduğu için düz <c>a.Team == b.Team</c>
    /// karşılaştırması "" == "" ile TÜM vuruşları reddederdi (§10.3/4).</summary>
    private static bool AreTeammates(PlayerState a, PlayerState b) =>
        !string.IsNullOrEmpty(a.Team) && a.Team == b.Team;

    private void RevivePlayerLocked(List<Outgoing> outbox, PlayerState player)
    {
        player.Hp = ArenaProtocol.PLAYER_MAX_HP;
        player.Alive = true;
        _rosterRefreshFor = player;
        // attackerId=0: canlanma bir saldırı sonucu değildir (§10.4/3).
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(new HealthUpdateMsg
        {
            playerId = player.PlayerId,
            hp = player.Hp,
            attackerId = 0
        }));
    }

    /// <summary>Boş takım kalmasın diye kalabalık taraftan yarısını karşıya taşır; takımsız
    /// oyuncuyu az kişili tarafa koyar (§10.1). registry.SetTeam event tetiklediği için bu metod
    /// YALNIZ kilit dışından çağrılır.</summary>
    private void BalanceTeams(List<PlayerState> players)
    {
        var red = players.Where(p => p.Team == "red").ToList();
        var blue = players.Where(p => p.Team == "blue").ToList();

        foreach (var player in players.Where(p => p.Team != "red" && p.Team != "blue").ToList())
        {
            var team = red.Count <= blue.Count ? "red" : "blue";
            _registry.SetTeam(player.PlayerId, team);
            (team == "red" ? red : blue).Add(player);
        }

        if (red.Count != 0 && blue.Count != 0) return;

        var full = red.Count == 0 ? blue : red;
        var emptyTeam = red.Count == 0 ? "red" : "blue";
        var moveCount = full.Count / 2;
        for (var i = 0; i < moveCount; i++)
        {
            var player = full[full.Count - 1];
            full.RemoveAt(full.Count - 1);
            _registry.SetTeam(player.PlayerId, emptyTeam);
        }
        Console.WriteLine($"[match] takım dengeleme: {moveCount} oyuncu '{emptyTeam}' takımına taşındı.");
    }

    /// <summary>Takımsız mod (§10.5 <c>teamMode:"none"</c>): lobide atanmış takımlar temizlenir,
    /// kimse kırmızı/maviye bölünmez. <see cref="BalanceTeams"/> gibi registry.SetTeam event
    /// tetiklediği için YALNIZ kilit dışından çağrılır.</summary>
    private void ClearTeams(List<PlayerState> players)
    {
        var cleared = 0;
        foreach (var player in players)
        {
            if (string.IsNullOrEmpty(player.Team)) continue;
            _registry.SetTeam(player.PlayerId, "");
            cleared++;
        }

        if (cleared > 0)
            Console.WriteLine($"[match] takımsız mod: {cleared} oyuncunun takımı temizlendi.");
    }

    private List<PlayerState> OnlinePlayersLocked() =>
        _registry.Snapshot().Where(p => p.Online && p.Role == "player").OrderBy(p => p.PlayerId).ToList();

    private MatchInfo BuildMatchInfoLocked() => new()
    {
        phase = _phase.ToString(),
        modeId = _modeId,
        sceneName = _sceneName,
        timeRemaining = _timeRemaining,
        scoreRed = _scoreRed,
        scoreBlue = _scoreBlue,
        rules = _rules.ToInfo()
    };

    private MatchStateMsg BuildMatchStateLocked() => new()
    {
        phase = _phase.ToString(),
        timeRemaining = _timeRemaining,
        scoreRed = _scoreRed,
        scoreBlue = _scoreBlue
    };

    /// <summary>Çevrimiçi tüm bağlantılara (admin dahil) kuyruklar; exceptPlayerId dolu ise o
    /// oyuncu atlanır (shot_fired relay'i atana gönderilmez).</summary>
    private void QueueBroadcastLocked(List<Outgoing> outbox, string json, int exceptPlayerId = 0)
    {
        foreach (var player in _registry.Snapshot())
        {
            if (!player.Online) continue;
            if (exceptPlayerId != 0 && player.PlayerId == exceptPlayerId) continue;
            var connection = player.Connection;
            if (connection == null) continue;
            outbox.Add(new Outgoing(connection, json, player.Name));
        }
    }

    private void QueueReadyClearLocked(PlayerState player)
    {
        if (player.Ready) _readyClearQueue.Add(player.DeviceId);
    }

    /// <summary>Kilit dışında: registry.SetReady → Changed → lobby_state yayını.</summary>
    /// <summary>Kilit dışında: registry.Announce → Changed → lobby_state yayını. `Updated` türü
    /// konsola satır BASMAZ (Program.cs), yalnız roster'ı tazeler.</summary>
    private void FlushRosterRefresh()
    {
        PlayerState? player;
        lock (_gate)
        {
            player = _rosterRefreshFor;
            _rosterRefreshFor = null;
        }

        if (player != null) _registry.Announce(player, PlayerChangeKind.Updated);
    }

    private void FlushReadyClear()
    {
        string[] devices;
        lock (_gate)
        {
            if (_readyClearQueue.Count == 0) return;
            devices = _readyClearQueue.ToArray();
            _readyClearQueue.Clear();
        }
        foreach (var deviceId in devices) _registry.SetReady(deviceId, false);
    }

    private static async Task FlushAsync(List<Outgoing> outbox)
    {
        foreach (var item in outbox)
        {
            try
            {
                await item.Connection.SendTextAsync(item.Json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[match] gönderim başarısız ({item.Who}): {ex.Message}");
            }
        }
    }

    /// <summary>Reddedilen hit_report: sebep AYNEN korunur ama atıcı başına en fazla
    /// RejectLogIntervalSeconds'da bir satır yazılır (ölü hedefe ateş sürerken konsol boğulmasın).
    /// Bastırılan satırlar yutulmaz: sayıları bir sonraki yazılan satırın sonuna
    /// "(+N bastırıldı)" olarak eklenir. Faz/öldürme/maç sonu/zorla canlandırma satırları bu
    /// kısıtlamaya GİRMEZ (nadirdirler).</summary>
    private void RejectHit(PlayerState shooter, int targetPlayerId, string reason)
    {
        int suppressed;
        lock (_rejectLogGate)
        {
            var now = DateTime.UtcNow;
            if (_lastRejectLogAt.TryGetValue(shooter.PlayerId, out var last) &&
                (now - last).TotalSeconds < RejectLogIntervalSeconds)
            {
                _suppressedRejects[shooter.PlayerId] = _suppressedRejects.GetValueOrDefault(shooter.PlayerId) + 1;
                return;
            }
            _lastRejectLogAt[shooter.PlayerId] = now;
            suppressed = _suppressedRejects.GetValueOrDefault(shooter.PlayerId);
            _suppressedRejects.Remove(shooter.PlayerId);
        }
        var tail = suppressed > 0 ? $" (+{suppressed} bastırıldı)" : "";
        Console.WriteLine($"[match] hit_report reddedildi ({shooter.Name} → {targetPlayerId}): {reason}.{tail}");
    }
}
