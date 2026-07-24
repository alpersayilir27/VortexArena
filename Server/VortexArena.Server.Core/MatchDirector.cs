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
    private readonly WeaponTable _weapons;

    /// <summary>Harita kataloğu (config/maps.json — Unity export'u). BOŞ olabilir: o zaman
    /// harita doğrulaması ve spawn slot sınırı devre dışıdır (§10.1).</summary>
    private readonly MapTable _maps;

    private readonly Dictionary<string, IGameMode> _modes = new(StringComparer.Ordinal);

    /// <summary>Kilit altında toplanan "ready bayrağını sıfırla" işleri; registry.SetReady event
    /// tetiklediği için (lobby_state yayını) kilit DIŞINDA uygulanır.</summary>
    private readonly List<string> _readyClearQueue = new();

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
    private DateTime _phaseEnteredAt = DateTime.UtcNow;

    /// <summary>1 Hz işler (countdown geri sayımı + Live'da match_state) için sonraki eşik.</summary>
    private DateTime _nextSecondAt = DateTime.UtcNow;

    private int _countdownRemaining;

    /// <summary>Live'a girildi; IGameMode.OnMatchStart kilit dışında çağrılacak.</summary>
    private bool _matchStartPending;

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public MatchDirector(PlayerRegistry registry, WeaponTable weapons, MapTable maps)
    {
        _registry = registry;
        _weapons = weapons;
        _maps = maps;
        Register(new TdmMode());
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

    /// <summary>Takım skoruna ekleme (kill/objektif kuralları modlardan gelir).</summary>
    public void AddScore(string team, int amount)
    {
        lock (_gate)
        {
            if (team == "red") _scoreRed += amount;
            else if (team == "blue") _scoreBlue += amount;
        }
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

        // Mod kancaları kilit DIŞINDA (yukarıdaki kilit sözleşmesi).
        modeToStart?.OnMatchStart(this);
        if (modeToTick == null) return;
        modeToTick.OnTick(this, deltaSeconds);
        if (modeToTick.IsMatchOver(this, out var winnerTeam))
            await EnterEndAsync(winnerTeam);
    }

    /// <summary>Tüm çevrimiçi oyuncular "sahne yüklendi" (set_ready) dediğinde veya
    /// LOADING_TIMEOUT dolduğunda Countdown'a geçilir. Çevrimdışı oyuncu beklenmez.</summary>
    private void TickLoadingLocked(List<Outgoing> outbox, DateTime now)
    {
        var players = OnlinePlayersLocked();
        if (players.Count == 0)
        {
            Console.WriteLine("[match] loading: çevrimiçi oyuncu kalmadı — lobiye dönülüyor.");
            EnterLobbyLocked(outbox, now);
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
    /// faz DEĞİŞMEZ, konsola sebep yazılır.</summary>
    public async Task StartMatchAsync(string? modeId, string? sceneName)
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

        if (players.Count == 0)
        {
            Console.WriteLine("[match] start_match reddedildi: çevrimiçi oyuncu yok.");
            return;
        }

        var missing = players.Where(p => !p.Scenes.Contains(sceneName)).Select(p => p.Name).ToList();
        if (missing.Count > 0)
        {
            Console.WriteLine($"[match] start_match reddedildi: '{sceneName}' sahnesi şu istemcilerin build listesinde yok — {string.Join(", ", missing)}.");
            return;
        }

        if (players.Count == 1)
            Console.WriteLine("[match] uyarı: tek oyuncuyla maç başlatılıyor (yalnız test amaçlı).");
        else
            BalanceTeams(players); // registry.SetTeam event tetikler → kilit DIŞINDA

        // Lobi ready bayrakları Loading'e GİRMEDEN sıfırlanır: Loading'de aynı bayrak "sahne
        // yüklendi" anlamına geliyor, bayat true kalsaydı countdown anında başlardı (§10.1).
        foreach (var player in players.Where(p => p.Ready).ToList())
            _registry.SetReady(player.DeviceId, false);

        var outbox = new List<Outgoing>();
        int red = 0, blue = 0;
        // Harita biliniyorsa takım başına slot sayısı sahnedeki SpawnPoint sayısıdır; 0 = sınır yok.
        var slotsPerTeam = map?.spawnSlotsPerTeam ?? 0;
        lock (_gate)
        {
            if (_phase != Phase.Lobby)
                Console.WriteLine($"[match] start_match: faz {_phase} — mevcut maç iptal edilip yenisi kuruluyor.");

            _mode = mode;
            _modeId = mode.ModeId;
            _sceneName = sceneName;
            _roundSeconds = mode.DefaultRoundSeconds;
            _scoreLimit = mode.DefaultScoreLimit;
            _scoreRed = 0;
            _scoreBlue = 0;
            _timeRemaining = _roundSeconds;
            _matchStartPending = false;

            foreach (var player in players)
            {
                ResetMatchStateLocked(player);
                // Takım içi 0 tabanlı sıra; harita slot sayısını biliyorsak modulo ile sarılır —
                // sahnede olmayan bir slota atama yapılmasın (kalabalık takımda slotlar paylaşılır).
                var slot = player.Team == "blue" ? blue++ : red++;
                player.SpawnSlot = slotsPerTeam > 0 ? slot % slotsPerTeam : slot;

                var connection = player.Connection;
                if (connection == null) continue;
                // load_match YALNIZ oyunculara gider; admin fazı match_state'ten öğrenir (§10.1).
                var load = new LoadMatchMsg
                {
                    modeId = _modeId,
                    sceneName = _sceneName,
                    roundSeconds = _roundSeconds,
                    scoreLimit = _scoreLimit,
                    yourTeam = player.Team,
                    spawnSlot = player.SpawnSlot
                };
                outbox.Add(new Outgoing(connection, JsonUtil.Serialize(load), player.Name));
            }

            SetPhaseLocked(Phase.Loading, DateTime.UtcNow);
            QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        }

        var mapInfo = map == null ? "" : $" ({map.sizeX:0.#}×{map.sizeZ:0.#}, {map.spawnSlotsPerTeam} slot/takım)";
        Console.WriteLine($"[match] start_match: mod '{mode.ModeId}', sahne '{sceneName}'{mapInfo}, {players.Count} oyuncu (kırmızı {red} / mavi {blue}).");
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

    /// <summary>hit_report doğrulama hattı (§10.3, sırayla): faz → atıcı → hedef → takım → silah →
    /// atış hızı → hasar. Herhangi biri düşerse tek satır log + sessiz ret (istemciye yanıt yok).</summary>
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
            if (target.Team == shooter.Team)
            {
                RejectHit(shooter, msg.targetPlayerId, "dost ateşi yok");
                return;
            }
            if (!_weapons.TryGet(msg.weaponId, out var weapon))
            {
                RejectHit(shooter, msg.targetPlayerId, $"bilinmeyen silah '{msg.weaponId}'");
                return;
            }

            var now = DateTime.UtcNow;
            var minInterval = WeaponTable.MinShotInterval(weapon);
            if ((now - shooter.LastHitAcceptedAt).TotalSeconds < minInterval)
            {
                RejectHit(shooter, msg.targetPlayerId, $"atış hızı ihlali (< {minInterval:0.000} sn)");
                return;
            }

            // Hasar HER ZAMAN tablodan; istemci farklı bildirmişse uyumsuzluk loglanır (§10.3/7).
            appliedDamage = weapon.damage;
            if (MathF.Abs(msg.damage - weapon.damage) > weapon.damage * 0.01f)
                Console.WriteLine($"[match] hasar uyumsuz: {shooter.Name} '{weapon.weaponId}' için {msg.damage:0.#} bildirdi, tablo {weapon.damage:0.#} — tablo uygulandı.");

            shooter.LastHitAcceptedAt = now;
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

                QueueBroadcastLocked(outbox, JsonUtil.Serialize(new KillEventMsg
                {
                    killerId = shooter.PlayerId,
                    victimId = target.PlayerId,
                    weaponId = weapon.weaponId
                }));

                var victimConnection = target.Connection;
                if (victimConnection != null)
                {
                    var respawn = new RespawnMsg
                    {
                        playerId = target.PlayerId,
                        spawnSlot = target.SpawnSlot,
                        delaySeconds = ArenaProtocol.RESPAWN_DELAY
                    };
                    outbox.Add(new Outgoing(victimConnection, JsonUtil.Serialize(respawn), target.Name));
                }
            }

            weaponId = weapon.weaponId;
            mode = _mode;
        }

        await FlushAsync(outbox);

        // Kabul edilen hasar konsola YAZILMAZ (saniyede onlarca satır olur) — yalnız öldürme + ret.
        mode?.OnHitApplied(this, shooter.PlayerId, target.PlayerId, appliedDamage, killed);
        if (!killed) return;

        mode?.OnKill(this, shooter.PlayerId, target.PlayerId, weaponId);
        Console.WriteLine($"[match] öldürme: {shooter.Name} → {target.Name} ({weaponId}) — skor kırmızı {ScoreRed} : mavi {ScoreBlue}");
        // Maç sonu kontrolü tick döngüsünde (≤100 ms) yapılır; burada faz değiştirmiyoruz.
    }

    /// <summary>revive_request (§10.4): faz Live + oyuncu ölü + RESPAWN_DELAY dolmuş ise canlandırır.
    /// Koşul tutmazsa SESSİZ yok sayılır — istemci canlanana dek ~1 sn'de bir tekrarlar, loglasak
    /// konsolu doldururdu.</summary>
    public async Task HandleReviveRequestAsync(PlayerState player)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Live || player.Role != "player" || player.Alive) return;
            if ((DateTime.UtcNow - player.DiedAt).TotalSeconds < ArenaProtocol.RESPAWN_DELAY) return;
            RevivePlayerLocked(outbox, player);
            Console.WriteLine($"[match] canlandı: {player.Name}");
        }
        await FlushAsync(outbox);
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

    private void EnterEndLocked(List<Outgoing> outbox, DateTime now, string winnerTeam)
    {
        SetPhaseLocked(Phase.End, now);
        Console.WriteLine($"[match] maç sonu — kazanan: {(string.IsNullOrEmpty(winnerTeam) ? "berabere" : winnerTeam)} " +
                          $"(kırmızı {_scoreRed} : mavi {_scoreBlue})");
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(new MatchEndMsg
        {
            winnerTeam = winnerTeam,
            scoreRed = _scoreRed,
            scoreBlue = _scoreBlue
        }));
    }

    private void EnterLobbyLocked(List<Outgoing> outbox, DateTime now)
    {
        SetPhaseLocked(Phase.Lobby, now);
        _mode = null;
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
            player.SpawnSlot = 0;
            QueueReadyClearLocked(player);
        }

        QueueBroadcastLocked(outbox, JsonUtil.Serialize(new ReturnToLobbyMsg()));
        QueueBroadcastLocked(outbox, JsonUtil.Serialize(BuildMatchStateLocked()));
    }

    /// <summary>Tick dışından (mod IsMatchOver) çağrılır; araya abort girmişse no-op.</summary>
    private async Task EnterEndAsync(string? winnerTeam)
    {
        var outbox = new List<Outgoing>();
        lock (_gate)
        {
            if (_phase != Phase.Live) return;
            EnterEndLocked(outbox, DateTime.UtcNow, winnerTeam ?? "");
        }
        await FlushAsync(outbox);
    }

    // ---- Yardımcılar ----

    private static void ResetMatchStateLocked(PlayerState player, bool keepScore = false)
    {
        player.Hp = ArenaProtocol.PLAYER_MAX_HP;
        player.Alive = true;
        player.LastHitAcceptedAt = DateTime.MinValue;
        player.DiedAt = DateTime.MinValue;
        if (keepScore) return;
        player.Kills = 0;
        player.Deaths = 0;
    }

    private void RevivePlayerLocked(List<Outgoing> outbox, PlayerState player)
    {
        player.Hp = ArenaProtocol.PLAYER_MAX_HP;
        player.Alive = true;
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

    private List<PlayerState> OnlinePlayersLocked() =>
        _registry.Snapshot().Where(p => p.Online && p.Role == "player").OrderBy(p => p.PlayerId).ToList();

    private MatchInfo BuildMatchInfoLocked() => new()
    {
        phase = _phase.ToString(),
        modeId = _modeId,
        sceneName = _sceneName,
        timeRemaining = _timeRemaining,
        scoreRed = _scoreRed,
        scoreBlue = _scoreBlue
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
