#nullable enable
using VortexArena.Protocol;
using VortexArena.Server.Core.Modes;

namespace VortexArena.Server.Core;

/// <summary>
/// Maçın genel durumu (§10.1). <b>Tel formatıyla birebir aynıdır</b> — üç değer, fazlası yok.
/// <para>⚠️ Bu enum'un TEK yetkisi hasar kapısıdır: <c>hit_report</c> yalnız <see cref="Playing"/>
/// fazında işlenir. "Ateş edebilir miyim", "silahım nereden gelir", "hangi HUD" sorularının cevabı
/// buradan DEĞİL moddan gelir (<see cref="Modes.ModeRules"/>). Yeni bir mod ara durum isterse bu
/// enum büyümez — <see cref="PauseReason.Mode"/> + <c>modeState</c> kullanılır.</para>
/// </summary>
public enum Phase
{
    /// <summary>Maç koşmuyor: lobi, yükleme, geri sayım, duraklatma. Hasar KAPALI.</summary>
    Paused,

    /// <summary>Maç koşuyor. Hasarın işlendiği TEK faz.</summary>
    Playing,

    /// <summary>Maç bitti, skorlar kesin. Hasar KAPALI.</summary>
    Finished
}

/// <summary>
/// <see cref="Phase.Paused"/>'un gerekçesi (§10.1 <c>phaseReason</c>). Çekirdeğin iç durumu da
/// budur: tik döngüsü hangi işi yapacağını (yükleme kapısı mı, geri sayım mı) buradan bilir.
/// <para>Neden ayrı bir alan: turnuva "herkes tabana dönsün" derken (<see cref="Mode"/>) operatör
/// de duraklatırsa (<see cref="Operator"/>) ikisi karışmamalı — mod kendi durumunu
/// <c>modeState</c>'te korur, çekirdek gerekçeyi burada tutar.</para>
/// </summary>
public enum PauseReason
{
    /// <summary>Duraklı değil (<see cref="Phase.Playing"/> / <see cref="Phase.Finished"/>).</summary>
    None,

    /// <summary>Lobi türü açık, maç kurulmadı.</summary>
    Lobby,

    /// <summary>Sahne yükleme kapısı: oyuncuların set_ready'si bekleniyor.</summary>
    Loading,

    /// <summary>Geri sayım.</summary>
    Countdown,

    /// <summary>Operatör koşan maçı duraklattı.</summary>
    Operator,

    /// <summary>Mod duraklatma istedi; gerekçesi <c>modeState</c>'tedir.</summary>
    Mode
}

/// <summary>Lobi sahnelemesinin sonucu (§10.7) — operatöre gösterilecek duyuru bundan üretilir.</summary>
public enum StageOutcome
{
    /// <summary>Sahne değişti; tüm istemcilere <c>return_to_lobby</c> yollandı.</summary>
    Staged,

    /// <summary>Zaten o sahnedeydik ya da istenen sahne boştu — kimse yeniden yüklemedi.</summary>
    Unchanged,

    /// <summary>Yapılmadı; sebebi <see cref="StageSceneResult.Reason"/>'da.</summary>
    Rejected
}

/// <summary>Sahneleme sonucu + reddedildiyse insan okuyabilir sebebi (admin duyurusuna girer).</summary>
public readonly record struct StageSceneResult(StageOutcome Outcome, string Reason = "");

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

    private Phase _phase = Phase.Paused;
    private PauseReason _pauseReason = PauseReason.Lobby;

    /// <summary>Modun kendi ara durumu (§10.1 <c>modeState</c>). <b>Çekirdek bunu YORUMLAMAZ</b> —
    /// yalnız taşır; okuyanı istemcideki HUD'dur. Duraklatma boyunca korunur ki mod kaldığı yerden
    /// sürebilsin.</summary>
    private string _modeState = "";

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

    /// <summary>Bu işletmenin lobi sahnesi (§10.7, <c>server.json → lobbyScene</c>).
    /// <para>⚠️ <b>Boş olamaz:</b> sunucu açılışta bunu çözemezse hiç açılmaz (§11 fail-fast,
    /// <c>Program.cs</c>). Sunucunun açık sahnesi istemcinin TEK yönlendirme kaynağıdır.</para>
    /// <para>⚠️ Lobi bir MAÇ DEĞİLDİR, bir <b>tür</b>dür — bu alan <c>_sceneName</c>/<c>_modeId</c>'yi
    /// maç dışında doldurur. Hasar kapısı (§10.3) fazdan okumaya devam eder.</para></summary>
    private readonly string _lobbyScene;

    public MatchDirector(PlayerRegistry registry, MapTable maps, string lobbyScene = "")
    {
        _registry = registry;
        _maps = maps;
        // Boş bırakılırsa mekanın kendi lobi haritası devralır (§11) — her kurulumda elle
        // yazılması gereken bir alan olmasın; config yalnız istisna için doldurulur.
        _lobbyScene = string.IsNullOrWhiteSpace(lobbyScene) ? maps.ResolveLobbyScene() : lobbyScene.Trim();
        RegisterModes();
        // Durum zaten Paused/Lobby ile başlıyor; lobi profilini de başlangıçta yaz ki ilk welcome
        // (henüz hiç EnterLobbyLocked çalışmadan) doğru sahneyi/modId'yi/kuralı taşısın.
        _modeId = _lobbyScene.Length > 0 ? ArenaProtocol.LOBBY_MODE_ID : "";
        _sceneName = _lobbyScene;
        _rules = ModeRules.LobbyProfile;
    }

    /// <summary>Sunucunun yapılandırılmış lobi sahnesi — açılış logu ve doğrulaması için.</summary>
    public string LobbyScene => _lobbyScene;

    /// <summary>Bu oturumda oynatılan mekan (§11); mekan ayrımı yoksa boş.</summary>
    public string VenueId => _maps.Venue;

    /// <summary>Bu mekanın harita adları — <c>admin_state</c> ile adminlere gider ki harita
    /// seçicileri yalnız oynatılabilir arenaları göstersin.</summary>
    public IReadOnlyList<string> VenueScenes => _maps.SceneNames;

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
            // Dağıtım (faz, gerekçe) ikilisinden yapılır: telde tek bir `paused` görünen durum
            // çekirdekte üç ayrı iş olabiliyor (yükleme kapısı, geri sayım, öylece bekleme).
            switch (_phase)
            {
                case Phase.Paused when _pauseReason == PauseReason.Loading:
                    TickLoadingLocked(outbox, now);
                    break;
                case Phase.Paused when _pauseReason == PauseReason.Countdown:
                    TickCountdownLocked(outbox, now);
                    break;
                // Lobby/Operator/Mode: beklenecek bir şey yok, sayaç işlemez.
                case Phase.Playing:
                    modeToTick = TickLiveLocked(outbox, now, deltaSeconds);
                    break;
                case Phase.Finished:
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
            // ⚠️ REVIVE_GRACE'in TEK istisnası (§10.6): kalibresiz oyuncu ZORLA da canlandırılmaz.
            // Bu satır olmadan "kalibresiz oyuncu canlanamaz" kuralı işlevsizdir — HandleRevive-
            // RequestAsync reddetse bile oyuncu birkaç saniye sonra buradan canlanırdı.
            // Sonucu: kalibresiz ölü oyuncu kalibre olana dek ölü kalır; kalibrasyon gelince
            // grace zaten dolmuş olduğu için ilk tik'te kendiliğinden canlanır.
            if (!player.Calibrated) continue;
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
        // Tablo boşsa (dosya yok) bu adım tümüyle atlanır.
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
            if (_phase != Phase.Paused || _pauseReason != PauseReason.Lobby)
                Console.WriteLine($"[match] start_match: durum {PhaseWire(_phase)} — mevcut maç iptal edilip yenisi kuruluyor.");

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

            SetPhaseLocked(Phase.Paused, PauseReason.Loading, DateTime.UtcNow);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }

        // Takım dağılımı BalanceTeams/ClearTeams sonrasındaki GERÇEK durumdan sayılır
        // (players listesi PlayerState referansları tutuyor, SetTeam onları yerinde günceller).
        var blueCount = players.Count(p => p.Team == "blue");
        var teamInfo = teamless ? "takımsız" : $"kırmızı {players.Count - blueCount} / mavi {blueCount}";
        Console.WriteLine($"[match] start_match: mod '{mode.ModeId}', sahne '{sceneName}', " +
                          $"{appliedRound} sn / limit {appliedLimit}, {players.Count} oyuncu ({teamInfo}).");
        await FlushAsync(outbox);
    }

    /// <summary>
    /// <c>pause_match</c> (§5.2) — koşan maçı dondurur: <see cref="Phase.Playing"/> →
    /// <see cref="Phase.Paused"/> + <see cref="PauseReason.Operator"/>.
    /// <para>
    /// Süre kendiliğinden durur: sayaç yalnız <see cref="TickLiveLocked"/> içinde azalıyor ve o da
    /// yalnız <see cref="Phase.Playing"/>'de çağrılıyor. Skorlar, canlar ve
    /// <c>modeState</c> ELLENMEZ — duraklatmak maçtan çıkmak değildir (çıkış
    /// <c>abort_match</c>).
    /// </para>
    /// <para>Koşmayan maç duraklatılmaz; <c>false</c> döner ve durum değişmez.</para>
    /// </summary>
    public async Task<bool> PauseMatchAsync()
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Playing)
            {
                Console.WriteLine($"[match] pause_match yok sayıldı: faz {PhaseWire(_phase)} (yalnız koşan maç duraklatılır).");
                return false;
            }

            SetPhaseLocked(Phase.Paused, PauseReason.Operator, DateTime.UtcNow);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }
        await FlushAsync(outbox);
        return true;
    }

    /// <summary>
    /// <c>resume_match</c> (§5.2) — operatörün duraklattığı maçı sürdürür.
    /// <para>
    /// ⚠️ <b>Yalnız <see cref="PauseReason.Operator"/> kaldırılır.</b> Her duraklamayı kendi sahibi
    /// kaldırır: <see cref="PauseReason.Mode"/>'u kaldırmak modun ara durumunu bozar,
    /// <see cref="PauseReason.Loading"/>/<see cref="PauseReason.Countdown"/> zaten kendi
    /// koşullarıyla biter, <see cref="PauseReason.Lobby"/>'de sürdürülecek maç yoktur.
    /// </para>
    /// <para>
    /// <see cref="EnterLiveLocked"/> KULLANILMAZ: o maçı baştan kurar (süreyi tam raunda çeker,
    /// canları doldurur). Sürdürme kaldığı yerden devam etmektir.
    /// </para>
    /// </summary>
    public async Task<bool> ResumeMatchAsync()
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Paused || _pauseReason != PauseReason.Operator)
            {
                Console.WriteLine($"[match] resume_match reddedildi: durum {PhaseWire(_phase)}" +
                                  $"{(_pauseReason != PauseReason.None ? "/" + ReasonWire(_pauseReason) : "")} " +
                                  "(yalnız operatörün duraklattığı maç sürdürülür).");
                return false;
            }

            var now = DateTime.UtcNow;
            SetPhaseLocked(Phase.Playing, now);
            // 1 Hz match_state ritmi duraklamada kaydığı için yeniden çıpalanır; süre ve skorlar
            // olduğu gibi kalır.
            _nextSecondAt = now.AddSeconds(1);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }
        await FlushAsync(outbox);
        return true;
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

    /// <summary>
    /// <b>Lobi sahnelemesi (§10.7):</b> operatörün seçtiği haritayı lobideyken TÜM istemcilere
    /// yükletir — admin panelinde harita değiştirmek oyuncuların da o arenaya geçmesi demektir.
    /// <para>
    /// Bu bir MAÇ DEĞİLDİR: faz <see cref="Phase.Paused"/>'da kalır, hasar kapısı (§10.3) kapalı
    /// kalır, kimse <c>set_ready</c> göndermez ve süre/skor işlemez. Taşıyıcı mesaj
    /// <c>return_to_lobby</c>'dir (lobi profili + yeni sahne) — istemci tarafında "lobideyiz,
    /// şu sahneyi yükle" zaten o mesajın anlamı, ikinci bir mesaj tipi eklemek gerekmez.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yalnız Lobby fazında iş yapar.</b> Koşan bir maçın ortasında sahne değiştirmek maçı
    /// bozardı; maç sırasında harita değişimi diye bir şey yoktur, yeni harita <c>start_match</c>
    /// ile gelir.
    /// </para>
    /// <para>
    /// Doğrulama <c>start_match</c> ile aynıdır (§10.1): sahne harita tablosunda olmalı (tablo
    /// boşsa bu adım atlanır) ve TÜM çevrimiçi oyuncuların build listesinde bulunmalı — yoksa bir
    /// kısmı lobide kalır, operatör de bunu ekranında göremezdi.
    /// </para>
    /// </summary>
    public async Task<StageSceneResult> StageSceneAsync(string? sceneName)
    {
        var target = (sceneName ?? "").Trim();
        if (target.Length == 0) return new StageSceneResult(StageOutcome.Unchanged);

        // Erken çıkış: doğrulama (registry taraması) boşuna yapılmasın.
        lock (_gate)
        {
            // Kapı YALNIZ koşan maçtır (§10.7): `finished` iken operatör yeni haritayı seçebilmeli.
            if (_phase == Phase.Playing)
                return new StageSceneResult(StageOutcome.Rejected, "maç sürüyor");
            if (_sceneName == target) return new StageSceneResult(StageOutcome.Unchanged);
        }

        if (!_maps.IsEmpty && !_maps.TryGet(target, out _))
        {
            return new StageSceneResult(StageOutcome.Rejected,
                $"'{target}' harita tablosunda yok (bilinen: {string.Join(", ", _maps.SceneNames)})");
        }

        var missing = _registry.Snapshot()
            .Where(p => p.Online && p.Role == "player" && !p.Scenes.Contains(target))
            .Select(p => p.Name)
            .ToList();
        if (missing.Count > 0)
        {
            return new StageSceneResult(StageOutcome.Rejected,
                $"'{target}' şu istemcilerin build listesinde yok: {string.Join(", ", missing)}");
        }

        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            // Kilit yeniden alındı (doğrulama kilit DIŞINDA yapıldı, kilit sözleşmesi gereği):
            // arada start_match girmiş olabilir, kapı burada bir daha kontrol edilir.
            if (_phase == Phase.Playing)
                return new StageSceneResult(StageOutcome.Rejected, "maç sürüyor");
            if (_sceneName == target) return new StageSceneResult(StageOutcome.Unchanged);

            _sceneName = target;
            // TÜR lobi olarak kalır: sahne bir arena olsa da henüz maç yoktur (§10.7). Buraya
            // seçili maç modunu yazmak maç HUD'unu ve maç loadout'unu maç başlamadan açardı.
            // Tür ancak start_match ile değişir. Kural şekli de lobi profilinde kalır (serbest
            // atış); hasarı zaten faz kapatıyor.
            _modeId = ArenaProtocol.LOBBY_MODE_ID;
            _rules = ModeRules.LobbyProfile;

            // Sahneleme koşan maçı değil bekleyişi değiştirir: eğer `finished` iken sahnelendiyse
            // artık lobi bekleyişindeyiz.
            SetPhaseLocked(Phase.Paused, PauseReason.Lobby, DateTime.UtcNow);

            QueueBroadcastLocked(outbox, JsonUtil.Serialize(new ReturnToLobbyMsg
            {
                modeId = _modeId,
                sceneName = _sceneName,
                rules = _rules.ToInfo()
            }));
        }

        Console.WriteLine($"[match] lobi sahnesi -> '{target}' (tüm istemciler yüklüyor).");
        await FlushAsync(outbox);
        return new StageSceneResult(StageOutcome.Staged);
    }

    // ---- Savaş hattı (§10.3) ----

    /// <summary>shot_fired doğrulanmaz, yalnız relay edilir: faz Live/Lobby + atıcı hayatta player
    /// ise playerId eklenip ATAN HARİÇ herkese gönderilir.
    /// <para>Lobby fazının açık olması bilinçlidir (§10.7): lobide hedef tahtasına ateş edilebiliyor,
    /// dolayısıyla başkalarının namlu alevini görmesi doğrudur. <b>Bu kapı hasar hattının kapısı
    /// DEĞİLDİR</b> — <c>hit_report</c> yalnız Live'da işlenir, yani lobide oyuncuya hasar veremez.
    /// Ara fazlarda (Loading/Countdown/End) relay yoktur.</para></summary>
    public async Task HandleShotFiredAsync(PlayerState shooter, ShotFiredMsg msg)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            // Atış kapısı ile HASAR kapısı bilerek AYRI (§10.3): atış bir sunum olayı, vuruş bir
            // durum değişimidir. Ateş serbestliği MODdan gelir (rules.fireWhilePaused) — lobi
            // türünde hedef atışı yapılabildiği için namlu alevinin görünmesi doğrudur; hasar yine
            // yalnız `playing`'de işlenir (HandleHitReportAsync).
            if (_phase != Phase.Playing && !_rules.FireWhilePaused) return;
            // Kalibresizin atışı relay EDİLMEZ (§10.6): ateş edemediği hâlde başkalarının
            // ekranında namlu alevi çakması yanıltıcı olurdu.
            if (shooter.Role != "player" || !shooter.Alive || !shooter.Calibrated) return;

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
    /// YOKTUR ve eklenmez (meşru saçma/patlama/yaylım vuruşlarını sessizce düşürürler).</para></summary>
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
            // ⚠️ HASARIN TEK KAPISI (§10.1/§10.3). Lobi, yükleme, geri sayım, duraklatma ve maç
            // sonu — hepsi `playing` değildir, hiçbirinde hasar işlenmez.
            if (_phase != Phase.Playing)
            {
                RejectHit(shooter, msg.targetPlayerId, $"faz {PhaseWire(_phase)}");
                return;
            }
            if (!shooter.Online || shooter.Role != "player" || !shooter.Alive)
            {
                RejectHit(shooter, msg.targetPlayerId, "atıcı ölü/oyuncu değil");
                return;
            }
            // §10.6: kalibresiz oyuncu ateş edemez. Hizalaması bozuk olduğu için nişan aldığı yer
            // ile gerçekte gösterdiği yer farklıdır — vuruşu saymak haksız ölüm üretirdi.
            if (!shooter.Calibrated)
            {
                RejectHit(shooter, msg.targetPlayerId, "atıcı kalibresiz");
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
            // §10.6: kalibresiz oyuncu hasar YEMEZ. Avatarı fiziksel konumundan kaymış durumda
            // olduğu için ona nişan almak da vurmak da anlamlı değildir.
            if (!target.Calibrated)
            {
                RejectHit(shooter, msg.targetPlayerId, "hedef kalibresiz");
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
    /// aynen işler — <b>tek istisnası kalibrasyondur</b> (§10.6, bkz. TickLiveLocked).</para></summary>
    public async Task HandleReviveRequestAsync(PlayerState player)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Playing || player.Role != "player" || player.Alive) return;
            if (!player.Calibrated) return; // §10.6: kalibresiz oyuncu canlanamaz
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
        SetPhaseLocked(next, PauseReason.None, now);
    }

    /// <summary>Faz + gerekçeyi birlikte yazar. İkisi tek yerden değişir ki telde tutarsız bir
    /// ikili (ör. <c>playing</c> + <c>loading</c>) hiç doğmasın.</summary>
    private void SetPhaseLocked(Phase next, PauseReason reason, DateTime now)
    {
        // Gerekçe yalnız Paused'da anlamlıdır; diğer fazlarda zorla temizlenir.
        if (next != Phase.Paused) reason = PauseReason.None;

        if (_phase != next || _pauseReason != reason)
        {
            var from = Describe(_phase, _pauseReason);
            var to = Describe(next, reason);
            Console.WriteLine($"[match] durum {from} → {to}");
        }

        _phase = next;
        _pauseReason = reason;
        _phaseEnteredAt = now;

        static string Describe(Phase phase, PauseReason reason) =>
            phase == Phase.Paused && reason != PauseReason.None
                ? $"{PhaseWire(phase)}/{ReasonWire(reason)}"
                : PhaseWire(phase);
    }

    /// <summary>Faz → tel değeri (§10.1). Enum adı ile tel değeri BİLEREK ayrı tutulur: tel
    /// küçük harf sözleşmesini izler, C# adı C# sözleşmesini.</summary>
    private static string PhaseWire(Phase phase) => phase switch
    {
        Phase.Playing => ArenaProtocol.PHASE_PLAYING,
        Phase.Finished => ArenaProtocol.PHASE_FINISHED,
        _ => ArenaProtocol.PHASE_PAUSED
    };

    /// <summary>Duraklama gerekçesi → tel değeri; <see cref="PauseReason.None"/> boş string.</summary>
    private static string ReasonWire(PauseReason reason) => reason switch
    {
        PauseReason.Lobby => ArenaProtocol.PAUSE_REASON_LOBBY,
        PauseReason.Loading => ArenaProtocol.PAUSE_REASON_LOADING,
        PauseReason.Countdown => ArenaProtocol.PAUSE_REASON_COUNTDOWN,
        PauseReason.Operator => ArenaProtocol.PAUSE_REASON_OPERATOR,
        PauseReason.Mode => ArenaProtocol.PAUSE_REASON_MODE,
        _ => ""
    };

    private void EnterCountdownLocked(List<Outgoing> outbox, DateTime now)
    {
        SetPhaseLocked(Phase.Paused, PauseReason.Countdown, now);
        _countdownRemaining = ArenaProtocol.COUNTDOWN_SECONDS;
        _nextSecondAt = now.AddSeconds(1);
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(new CountdownMsg { seconds = _countdownRemaining }));
    }

    private void EnterLiveLocked(List<Outgoing> outbox, DateTime now)
    {
        SetPhaseLocked(Phase.Playing, now);
        _timeRemaining = _roundSeconds;
        _nextSecondAt = now.AddSeconds(1);
        // §10.2: playing'e girerken herkes tam can + canlı.
        foreach (var player in OnlinePlayersLocked()) ResetMatchStateLocked(player, keepScore: true);
        _matchStartPending = _mode != null;
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
    }

    private void EnterEndLocked(List<Outgoing> outbox, DateTime now, MatchOutcome outcome)
    {
        SetPhaseLocked(Phase.Finished, now);
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
        SetPhaseLocked(Phase.Paused, PauseReason.Lobby, now);
        _mode = null;
        _modeState = "";
        // Lobi TÜRÜ (§10.7): kural şekli varsayılandan yalnız serbest atışla ayrılır. Hasarı yine
        // faz kapatır (hit_report yalnız playing) — bu bayrak sadece namlu alevinin görünmesini
        // sağlar. modId de dolar, çünkü istemci silah loadout'unu/HUD'unu bu anahtarla çözüyor.
        _rules = ModeRules.LobbyProfile;
        _modeId = _lobbyScene.Length > 0 ? ArenaProtocol.LOBBY_MODE_ID : "";
        _sceneName = _lobbyScene;
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

        var returnMsg = new ReturnToLobbyMsg
        {
            modeId = _modeId,
            sceneName = _sceneName,
            rules = _rules.ToInfo()
        };
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(returnMsg));
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
    }

    /// <summary>Tick dışından (mod IsMatchOver) çağrılır; araya abort girmişse no-op.</summary>
    private async Task EnterEndAsync(MatchOutcome outcome)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Playing) return;
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
        phase = PhaseWire(_phase),
        phaseReason = ReasonWire(_pauseReason),
        modeId = _modeId,
        modeState = _modeState,
        sceneName = _sceneName,
        timeRemaining = _timeRemaining,
        scoreRed = _scoreRed,
        scoreBlue = _scoreBlue,
        rules = _rules.ToInfo()
    };

    private MatchStateMsg BuildMatchStateLocked() => new()
    {
        phase = PhaseWire(_phase),
        phaseReason = ReasonWire(_pauseReason),
        modeState = _modeState,
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
