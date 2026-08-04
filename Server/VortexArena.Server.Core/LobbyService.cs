#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Lobi semantiği: hello→welcome yanıtı, roster her değiştiğinde herkese lobby_state
/// TAM anlık görüntüsü, set_identity/set_ready/set_team/kick/identify işleme (§5).
/// <para>
/// Roster yayını <b>tek bir yayıncı döngüden</b> gider (<c>MarkRosterDirty</c>) ve her yayın
/// <c>lobby_state.version</c>'ı artırır; <c>status.rosterVersion</c> geride kalan istemciye yalnız
/// ona tam snapshot yollatır (§5.1/5.3).
/// </para>
/// <para>
/// Ayrıca <b>adminler arası ortak durumun</b> sahibidir (§5.3 <c>admin_state</c>): bir sonraki
/// maçın mod/harita seçimi burada yaşar — admin arayüzündeki seçiciler yerel bir değişkeni değil
/// bunu değiştirir (<c>set_selection</c>), sunucu da değişikliği TÜM adminlere geri yayar.
/// Böylece iki operatör aynı ekranı görür. Görünüm tercihleri (kamera, halka, saydamlık) buraya
/// GİRMEZ — onlar her admin'in kendi makinesinde kalır.
/// </para></summary>
public sealed class LobbyService
{
    private readonly PlayerRegistry _registry;
    private readonly MatchDirector _director;

    /// <summary>Ortak seçimi koruyan kilit; WS işleyicileri farklı thread'lerden gelebilir.</summary>
    private readonly object _selectionGate = new();

    /// <summary>Ortak seçim (§5.3). ⚠️ <b>Hiçbir zaman boş olmaz:</b> kurucuda mekanın lobi
    /// haritasıyla tohumlanır (açık sahnenin açılış değeri, §10.7) ve <see cref="ApplySelection"/>
    /// boş alanı yok saydığı için bir daha boşalamaz. Tohumlanmasaydı ilk <c>admin_state</c>
    /// "hiç harita seçilmemiş" derdi ve panel mekan süzgecini uygulayacak veriyi de geç alırdı.</summary>
    private string _selectedModeId;
    private string _selectedSceneName;

    // ---- Roster yayıncısı (§5.3) ----
    // TEK yayıncı garantisi: aynı anda birden fazla lobby_state üretimi YOKTUR. Eski hâlde
    // OnRegistryChanged doğrudan `_ = BroadcastLobbyStateAsync()` çağırıyordu; arka arkaya iki
    // değişiklik iki eşzamanlı task açıyor, her biri kendi Snapshot()'ını farklı anda alıp
    // ClientConnection'ın gönderim semaforu için yarışıyordu. Semafor çerçevelerin iç içe
    // geçmemesini garanti eder ama YENİ olanın kazanmasını etmez → eski roster sonra yazılabilir
    // ve "atılan oyuncu hâlâ listede online" olarak kalır.
    private readonly object _broadcastGate = new();
    private bool _rosterDirty;
    private bool _broadcasting;
    private int _rosterVersion;

    /// <summary>Bir sonraki maçın ortak parametreleri (§5.2); <c>0</c> = seçilmedi, modun
    /// varsayılanı kullanılacak. Mod/harita ile AYNI kanaldan gider: parametreler yerel kalsaydı
    /// bir operatörün 5 dk sandığı maç diğerinin seçtiği 30 dk ile başlardı.</summary>
    private int _selectedRoundSeconds;
    private int _selectedScoreLimit;

    /// <summary>Ortak geri sayım seçimi (§5.2); 0 = hiç seçilmedi → COUNTDOWN_SECONDS.</summary>
    private int _selectedCountdownSeconds;

    public LobbyService(PlayerRegistry registry, MatchDirector director)
    {
        _registry = registry;
        _director = director;
        // Açılış seçimi = açık sahnenin açılış değeri (§10.7): mekanın lobi haritası. Sunucu
        // lobiyi çözemezse zaten hiç açılmıyor (§11 fail-fast), yani pratikte boş kalmaz.
        _selectedSceneName = director.LobbyScene;
        _selectedModeId = director.LobbyScene.Length > 0 ? ArenaProtocol.LOBBY_MODE_ID : "";
        _registry.Changed += OnRegistryChanged;
    }

    private void OnRegistryChanged(PlayerState state, PlayerChangeKind kind)
    {
        MarkRosterDirty();

        // Admin geldi/gitti → adminCount değişti, kalan adminler tazelensin.
        if (state.Role == "admin" && kind != PlayerChangeKind.Updated)
        {
            var verb = kind switch
            {
                PlayerChangeKind.Added => "bağlandı",
                PlayerChangeKind.Reconnected => "yeniden bağlandı",
                _ => "ayrıldı"
            };
            _ = BroadcastAdminStateAsync($"{state.Name} {verb}");
        }
    }

    /// <summary>hello → kayıt + welcome (mevcut maç durumu ile; geç katılım senkronu §5.3).
    /// welcome gönderildikten SONRA Announce ile lobby_state yayını tetiklenir.</summary>
    public async Task HandleHelloAsync(ClientConnection connection, HelloMsg hello)
    {
        if (hello.protocolVersion != ArenaProtocol.PROTOCOL_VERSION)
            Console.WriteLine($"[Lobby] protokol sürüm uyumsuzluğu: istemci {hello.protocolVersion}, sunucu {ArenaProtocol.PROTOCOL_VERSION} — devam ediliyor.");

        if (!_registry.TryRegisterHello(hello, connection, out var state, out var kind))
        {
            Console.WriteLine($"[Lobby] playerId havuzu tükendi ({ArenaProtocol.PLAYER_ID_MAX}) — {hello.deviceName} reddedildi.");
            await SendSafeAsync(connection, JsonUtil.Serialize(new KickedMsg { reason = "Sunucu dolu" }), "(dolu)");
            // Ret de bir atmadır (§5.4): abortif kapanış `kicked`'i düşürüp istemciyi sonsuz
            // yeniden bağlanma döngüsüne sokardı.
            _ = connection.CloseAfterKickAsync();
            return;
        }
        connection.State = state;

        var welcome = new WelcomeMsg
        {
            protocolVersion = ArenaProtocol.PROTOCOL_VERSION,
            playerId = state.PlayerId,
            udpToken = state.UdpToken,
            match = _director.CurrentMatchInfo()
        };
        await SendSafeAsync(connection, JsonUtil.Serialize(welcome), state.Name);

        // Geç katılan admin ortak seçimi welcome'dan hemen sonra alır (§5.3): paneli açtığında
        // diğer operatörün seçtiği mod/harita yazıyor olmalı, kendi varsayılanı değil.
        if (state.Role == "admin")
            await SendSafeAsync(connection, BuildAdminStateJson(""), state.Name);

        // Seçili modun takım kipi ROL AYIRMADAN gider (§5.3): oyuncu lobide taban şeritlerini
        // buna göre çizer ve bağlandığı anda doğru görmelidir — yayını beklerse bir sonraki
        // seçim değişikliğine kadar yanlış kalırdı.
        await SendSafeAsync(connection, BuildSelectionStateJson(), state.Name);

        // Geç katılım maç defterine de yazılır (§10.2) — Announce'tan ÖNCE: aksi hâlde ilk
        // lobby_state satırı `inMatch:false` gider ve arayüz maç sonu kapsamını bir yayın boyunca
        // eksik çizer. Koşan maç yoksa hiçbir şey yapmaz.
        _director.MarkParticipantIfMatchRunning(state);

        _registry.Announce(state, kind); // konsol satırı + lobby_state yayını
    }

    /// <summary>status kalp atışı (§5.1): cihaz durumunu günceller ve <b>roster uzlaştırması</b>
    /// yapar — istemcinin <c>rosterVersion</c>'ı geride kalmışsa YALNIZ ona tam bir lobby_state
    /// gider. Yedek ağdır, birincil yol değil: kontrol kanalı TCP olduğu için yayın "kaybolmaz";
    /// bu yol istemcinin bir yayını uygulayamadığı pencereleri (sahne geçişi, kopma anı) kapatır.</summary>
    public async Task HandleStatusAsync(ClientConnection connection, StatusMsg msg)
    {
        var state = connection.State;
        if (state == null) return;

        _registry.UpdateStatus(state.DeviceId, msg);
        if (msg.rosterVersion >= Volatile.Read(ref _rosterVersion)) return;

        await SendSafeAsync(connection, BuildLobbyStateJson(), state.Name);
    }

    /// <summary>set_identity (§5.1): ad ve/veya forma numarası. Yetki denetimi ClientConnection'da
    /// (oyuncu yalnız kendini, admin herkesi). Reddedilen numara operatöre admin_state.notice ile
    /// bildirilir — sessizce yutmak "verdim sandığı" numarayı görünmez kılardı.</summary>
    public void HandleSetIdentity(ClientConnection connection, SetIdentityMsg msg)
    {
        if (connection.State == null) return;
        var playerId = msg.playerId != 0 ? msg.playerId : connection.State.PlayerId;

        if (_registry.SetIdentity(playerId, msg.name, msg.number, out var error))
        {
            if (_registry.TryGetByPlayerId(playerId, out var target))
            {
                var label = target.Number > 0 ? $"{target.Number} · {target.Name}" : target.Name;
                Console.WriteLine($"[Lobby] set_identity: playerId {playerId} -> {label}.");
                if (connection.IsAdmin) _ = BroadcastAdminStateAsync(Notice(connection, $"kimlik -> {label}"));
            }
            return;
        }

        if (string.IsNullOrEmpty(error)) return; // değişiklik yok — sessiz
        Console.WriteLine($"[Lobby] set_identity reddedildi (playerId {playerId}): {error}.");
        if (connection.IsAdmin) _ = BroadcastAdminStateAsync(Notice(connection, $"kimlik reddedildi — {error}"));
    }

    public void HandleSetReady(ClientConnection connection, SetReadyMsg msg)
    {
        if (connection.State == null) return;
        _registry.SetReady(connection.State.DeviceId, msg.ready);
    }

    public void HandleSetTeam(ClientConnection connection, SetTeamMsg msg)
    {
        if (msg.team != "red" && msg.team != "blue")
        {
            Console.WriteLine($"[Lobby] set_team geçersiz takım '{msg.team}' — yok sayıldı.");
            return;
        }
        if (!_registry.TryGetByPlayerId(msg.playerId, out var target))
        {
            Console.WriteLine($"[Lobby] set_team: playerId {msg.playerId} bulunamadı.");
            return;
        }
        if (target.Role != "player")
        {
            Console.WriteLine($"[Lobby] set_team: {target.Name} admin — takım atanmaz.");
            return;
        }
        _registry.SetTeam(msg.playerId, msg.team);
        if (connection.IsAdmin)
            _ = BroadcastAdminStateAsync(Notice(connection, $"{target.Name} -> {msg.team}"));
    }

    public async Task HandleKickAsync(ClientConnection connection, KickMsg msg)
    {
        // Atma kaydı roster'dan SİLER (§5.4) — bağlantısı kopmuş bir kayıtta da iş yapmasının tek
        // yolu budur; yalnız soketi kapatmak, bağlantısı zaten olmayan satırı listede bırakırdı.
        // Katılımcı bayrağı ayrıca temizlenmez: kayıt tümüyle gittiği için onunla birlikte gider,
        // yani atılan oyuncu maç sonu tablosunda da yer almaz (§10.2).
        if (!_registry.RemoveByPlayerId(msg.playerId, out var target, out var targetConnection))
        {
            Console.WriteLine($"[Lobby] kick: playerId {msg.playerId} bulunamadı.");
            return;
        }
        var how = targetConnection == null ? "bağlantısı yoktu, kayıt silindi" : "bağlantı kapatılıyor";
        Console.WriteLine($"[Lobby] kick: {target.Name} (playerId {target.PlayerId}) — {how}.");
        await BroadcastAdminStateAsync(Notice(connection, $"{target.Name} atıldı"));
        if (targetConnection == null) return;
        await SendSafeAsync(targetConnection, JsonUtil.Serialize(new KickedMsg { reason = "" }), target.Name);
        // ⚠️ Abort DEĞİL: RST istemcinin daha okumadığı `kicked` çerçevesini düşürebilir ve o
        // zaman atılan başlık kopuşu sıradan bir kesinti sanıp geri bağlanırdı (§5.4).
        // Beklemiyoruz: pay bu (admin) bağlantının alma döngüsünü meşgul etmemeli.
        _ = targetConnection.CloseAfterKickAsync(); // recv döngüsü kapanınca Offline + lobby_state yayını gelir
    }

    public async Task HandleIdentifyAsync(ClientConnection connection, IdentifyMsg msg)
    {
        if (!_registry.TryGetByPlayerId(msg.playerId, out var target) || target.Socket == null)
        {
            Console.WriteLine($"[Lobby] identify: playerId {msg.playerId} bulunamadı/bağlantısı yok.");
            return;
        }
        // Sunucu→istemci yönünde istemci kendi kimlik overlay'ini gösterir (§5.3).
        await SendSafeAsync(target.Socket, JsonUtil.Serialize(new IdentifyMsg { playerId = target.PlayerId }), target.Name);
        await BroadcastAdminStateAsync(Notice(connection, $"{target.Name} kimlik gösterdi"));
    }

    // ---- Kalibrasyon durumu (§10.6) ----

    /// <summary>set_calibration: başlık KENDİ hizalamasını bildirir (§5.1). Yalnız kendi kaydını
    /// yazabilir — playerId taşımaz, bağlantıdan çözülür.</summary>
    public void HandleSetCalibration(ClientConnection connection, SetCalibrationMsg msg)
    {
        var state = connection.State;
        if (state == null) return;
        if (!_registry.SetCalibration(state.PlayerId, msg.calibrated, msg.source)) return;
        var what = msg.calibrated ? $"kalibre oldu ({msg.source})" : "kalibrasyonunu bıraktı";
        Console.WriteLine($"[Lobby] set_calibration: {state.Name} {what}.");
    }

    /// <summary>clear_calibration: admin bir oyuncunun (playerId 0 = HERKES) kalibrasyonunu
    /// sıfırlar (§5.2). Admin yalnız SIFIRLAYABİLİR — "kalibre oldu" işaretini yalnız başlık
    /// koyar (§10.6), çünkü hizalamanın oturduğunu yalnız o bilir.</summary>
    public async Task HandleClearCalibrationAsync(ClientConnection connection, ClearCalibrationMsg msg)
    {
        if (msg.playerId == 0)
        {
            var affected = _registry.ClearAllCalibration();
            Console.WriteLine($"[Lobby] clear_calibration: TÜM oyuncular ({affected}) — {connection.State?.Name}.");
            await BroadcastAdminStateAsync(Notice(connection, $"tüm kalibrasyonlar sıfırlandı ({affected} oyuncu)"));
            return;
        }

        if (!_registry.TryGetByPlayerId(msg.playerId, out var target))
        {
            Console.WriteLine($"[Lobby] clear_calibration: playerId {msg.playerId} bulunamadı.");
            return;
        }
        if (target.Role != "player")
        {
            Console.WriteLine($"[Lobby] clear_calibration: {target.Name} admin — kalibrasyon yok, yok sayıldı.");
            return;
        }
        // false = zaten kalibresizdi (SetCalibration değişmediyse yayın yapmaz) → sessizce çık:
        // operatöre "sıfırlandı" duyurusu göndermek olmamış bir şeyi olmuş göstermek olurdu.
        if (!_registry.SetCalibration(target.PlayerId, false, null)) return;
        Console.WriteLine($"[Lobby] clear_calibration: {target.Name} (playerId {target.PlayerId}) — {connection.State?.Name}.");
        await BroadcastAdminStateAsync(Notice(connection, $"{target.Name} kalibrasyonu sıfırlandı"));
    }

    // ---- Ortak seçim (§5.2 set_selection / §5.3 admin_state) ----

    /// <summary>Bir sonraki maçın ortak mod/harita seçimi. Maçı BAŞLATMAZ; boş alan mevcut
    /// değerini korur. Değişiklik tüm adminlere yayılır — çoklu operatör aynı ekranı görsün.
    /// <para>
    /// <b>Harita seçmek aynı zamanda SAHNELEMEktir (§10.7):</b> lobideyken seçilen arena
    /// <see cref="MatchDirector.StageSceneAsync"/> ile TÜM istemcilere anında yüklenir. Operatör
    /// haritayı yalnız kendi ekranında değil oyuncuların başlıklarında da değiştirir.
    /// </para>
    /// <para>
    /// ⚠️ Bu yüzden <b>mod/harita yalnız maç KURULMAMIŞKEN değiştirilebilir</b>
    /// (<see cref="MatchDirector.CanChangeSelection"/>): lobi bekleyişinde ve <c>finished</c>'da —
    /// maç bittiğinde operatör bir sonrakini seçebilmelidir. Kurulmuş maç (koşan, yüklenen, geri
    /// sayan ya da duraklatılmış) kapalıdır: sahne komutu herkese gittiği için maçın altından
    /// sahne çekmek onu bozardı. Reddedilen alanlar düşürülür, komutun geri kalanı (süre/limit)
    /// işlenmeye devam eder — onlar bir sonraki maçın parametreleridir, sahne yüklemezler.
    /// </para></summary>
    public async Task HandleSetSelectionAsync(ClientConnection connection, SetSelectionMsg msg)
    {
        var requestedModeId = msg.modeId ?? "";
        var requestedSceneName = msg.sceneName ?? "";

        // Faz kapısı (§10.7): kurulmuş maç engeller — koşan, yüklenen, geri sayan ya da
        // duraklatılmış. `finished` ve lobi bekleyişi serbesttir. Otorite sunucudadır — arayüz
        // seçicileri aynı kuralla zaten pasiftir, burası bayat/yarışan bir panelin komutunu keser.
        var rejection = "";
        if (!_director.CanChangeSelection && (requestedModeId.Length > 0 || requestedSceneName.Length > 0))
        {
            rejection = "maç kurulu — harita/mod değiştirilemez, önce İPTAL";
            Console.WriteLine($"[Lobby] set_selection reddedildi ({connection.State?.Name}): {rejection}.");
            requestedModeId = "";
            requestedSceneName = "";
        }

        string previousModeId;
        lock (_selectionGate) previousModeId = _selectedModeId;

        var changed = ApplySelection(requestedModeId, requestedSceneName,
            msg.roundSeconds, msg.scoreLimit, msg.countdownSeconds);

        // Taban şeritleri seçili modun takım kipine bağlı (§10.7) — MOD değiştiyse herkese
        // bildirilir. Harita/süre/limit değişimi bu yayını üretmez.
        bool modeChanged;
        lock (_selectionGate) modeChanged = _selectedModeId != previousModeId;
        if (modeChanged) await BroadcastSelectionStateAsync();

        // Reddedildiyse DEĞİŞMESE de yayın yapılır: komutu gönderen panel imlecini iyimser olarak
        // ilerletmiş olabilir, sunucunun değeri onu geri çeksin (tek doğruluk kaynağı, §5.3).
        // Harita alanı doluysa da çıkılmaz: seçim aynı kalmış olsa bile o sahne AÇIK olmayabilir
        // (maç bitip lobiye dönüldüğünde seçim hâlâ arenayı gösterir) — sahneleme denenmelidir.
        if (!changed && requestedSceneName.Length == 0 && rejection.Length == 0) return;

        string modeId, sceneName;
        int roundSeconds, scoreLimit;
        lock (_selectionGate)
        {
            modeId = _selectedModeId;
            sceneName = _selectedSceneName;
            roundSeconds = _selectedRoundSeconds;
            scoreLimit = _selectedScoreLimit;
        }

        // Sahneleme (§10.7): harita alanı DOLU geldiyse herkes o arenayı yükler. Ölçüt "seçim
        // değişti mi" değil "istendi mi": maç bitip lobiye dönüldükten sonra seçim hâlâ o arenayı
        // gösterdiği için, aynı arenayı tekrar seçen operatör aksi hâlde hiçbir şey olmadığını
        // görürdü. İstenen sahne zaten açıksa StageSceneAsync Unchanged döner (idempotent).
        // ⚠️ Bu yüzden panel harita alanını YALNIZ imleci oynattığında doldurur (§5.2) — süre/limit
        // dokunuşunda dolduran bir istemci herkesi arenaya taşırdı.
        var stageNote = "";
        if (requestedSceneName.Length > 0)
        {
            var staged = await _director.StageSceneAsync(sceneName);
            stageNote = staged.Outcome switch
            {
                StageOutcome.Staged => " (herkes yüklüyor)",
                StageOutcome.Rejected => $" — SAHNELENEMEDİ: {staged.Reason}",
                _ => ""
            };
        }

        var parameters = roundSeconds > 0 || scoreLimit > 0
            ? $", {(roundSeconds > 0 ? roundSeconds + " sn" : "mod süresi")} / " +
              $"{(scoreLimit > 0 ? "limit " + scoreLimit : "mod limiti")}"
            : "";
        Console.WriteLine($"[Lobby] set_selection: mod '{modeId}', harita '{sceneName}'{parameters} ({connection.State?.Name}).");

        var action = rejection.Length > 0
            ? rejection
            : $"seçim -> {sceneName} / {modeId}{parameters}{stageNote}";
        await BroadcastAdminStateAsync(Notice(connection, action));
    }

    /// <summary>true = seçim gerçekten değişti. Boş/null string ve <c>0</c> sayı mevcut değeri
    /// korur (§5.2) — arayüz yalnız değiştirdiği alanı doldurabilsin.
    /// <para>⚠️ Bu koruma aynı zamanda "seçim asla boşalmaz" garantisidir: kurucudaki lobi
    /// tohumundan sonra hiçbir komut mod/haritayı boşa çekemez.</para></summary>
    private bool ApplySelection(string? modeId, string? sceneName, int roundSeconds, int scoreLimit,
        int countdownSeconds)
    {
        lock (_selectionGate)
        {
            var changed = false;
            if (!string.IsNullOrEmpty(modeId) && _selectedModeId != modeId)
            {
                _selectedModeId = modeId;
                changed = true;
            }
            if (!string.IsNullOrEmpty(sceneName) && _selectedSceneName != sceneName)
            {
                _selectedSceneName = sceneName;
                changed = true;
            }
            if (roundSeconds > 0 && _selectedRoundSeconds != roundSeconds)
            {
                _selectedRoundSeconds = roundSeconds;
                changed = true;
            }
            if (scoreLimit > 0 && _selectedScoreLimit != scoreLimit)
            {
                _selectedScoreLimit = scoreLimit;
                changed = true;
            }
            if (countdownSeconds > 0 && _selectedCountdownSeconds != countdownSeconds)
            {
                _selectedCountdownSeconds = countdownSeconds;
                changed = true;
            }
            return changed;
        }
    }

    // ---- Maç komutları (yalnız admin; doğrulama + yayınlar MatchDirector'da, §10.1). ----

    /// <summary>start_match ortak seçimi de günceller: maç başladığında tüm admin panelleri
    /// aynı mod/haritayı göstersin (komutu kim gönderdiyse gönderdi).</summary>
    public async Task HandleStartMatchAsync(ClientConnection connection, StartMatchMsg msg)
    {
        string previousModeId;
        lock (_selectionGate) previousModeId = _selectedModeId;

        ApplySelection(msg.modeId, msg.sceneName, msg.roundSeconds, msg.scoreLimit, msg.countdownSeconds);

        bool modeChanged;
        lock (_selectionGate) modeChanged = _selectedModeId != previousModeId;
        if (modeChanged) await BroadcastSelectionStateAsync();

        await BroadcastAdminStateAsync(Notice(connection, $"maç başlatılıyor: {msg.sceneName} / {msg.modeId}"));
        await _director.StartMatchAsync(msg.modeId, msg.sceneName, msg.roundSeconds, msg.scoreLimit,
            msg.countdownSeconds);
    }

    public async Task HandleAbortMatchAsync(ClientConnection connection)
    {
        await BroadcastAdminStateAsync(Notice(connection, "maç iptal edildi"));
        await _director.AbortMatchAsync();
    }

    public async Task HandleReturnToLobbyAsync(ClientConnection connection)
    {
        await BroadcastAdminStateAsync(Notice(connection, "lobiye dönülüyor"));
        await _director.ReturnToLobbyAsync();
    }

    /// <summary>pause_match (§5.2). Duyuru YALNIZ gerçekten duraklatıldıysa yayılır — reddedilen
    /// komut diğer operatörlerin ekranına "duraklattı" yazmamalı.</summary>
    public async Task HandlePauseMatchAsync(ClientConnection connection)
    {
        if (await _director.PauseMatchAsync())
            await BroadcastAdminStateAsync(Notice(connection, "maç duraklatıldı"));
    }

    /// <summary>resume_match (§5.2) — yalnız operatörün duraklattığı maçı sürdürür.</summary>
    public async Task HandleResumeMatchAsync(ClientConnection connection)
    {
        if (await _director.ResumeMatchAsync())
            await BroadcastAdminStateAsync(Notice(connection, "maç sürdürüldü"));
    }

    /// <summary>
    /// <c>set_friendly_fire</c> (§5.2) — dost ateşi anahtarı, <b>faz kapısı yok</b>: koşan maçta da
    /// geçerli ve etkisi anlık.
    /// <para>Değer değişmediyse sunucu iş yapmaz; yine de <c>admin_state</c> yayınlanır ki iyimser
    /// davranmış bir panel sunucunun değerine çekilsin (§5.3 tek doğruluk kaynağı).</para>
    /// </summary>
    public async Task HandleSetFriendlyFireAsync(ClientConnection connection, SetFriendlyFireMsg msg)
    {
        var changed = await _director.SetFriendlyFireAsync(msg.enabled);
        var label = msg.enabled ? "AÇIK" : "kapalı";
        await BroadcastAdminStateAsync(changed
            ? Notice(connection, $"dost ateşi {label}")
            : "");
    }

    /// <summary>Duyuru satırı: "<admin adı>: <eylem>" — tüm adminlerin durum satırında görünür.</summary>
    private static string Notice(ClientConnection connection, string action) =>
        $"{connection.State?.Name ?? "Admin"}: {action}";

    /// <summary>
    /// Seçili modun takım kipini <b>HERKESE</b> yollar (§5.3 <c>selection_state</c>).
    /// <para>
    /// ⚠️ <c>admin_state</c>'e binmemesinin sebebi hedef kitledir: o mesaj roster/duyuru/telemetri
    /// taşır ve yalnız adminlere gider. Oyuncunun ihtiyacı tek bir sunum alanı — taban şeritleri
    /// görünsün mü (§10.7).
    /// </para>
    /// <para>
    /// ⚠️ Çağrı yerleri dar tutulur: <c>welcome</c> sonrası ve <b>seçili MOD değiştiğinde</b>.
    /// Harita/süre/limit dokunuşunda yayınlansaydı, operatör imleci oynattıkça her oyuncuya
    /// gereksiz mesaj giderdi.
    /// </para>
    /// </summary>
    public async Task BroadcastSelectionStateAsync()
    {
        try
        {
            var connections = _registry.ConnectedConnections();
            if (connections.Count == 0) return;
            var json = BuildSelectionStateJson();
            foreach (var connection in connections)
                await SendSafeAsync(connection, json, "(selection)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] selection_state yayını hatası: {ex.Message}");
        }
    }

    private string BuildSelectionStateJson()
    {
        string modeId;
        lock (_selectionGate)
        {
            modeId = _selectedModeId;
        }

        return JsonUtil.Serialize(new SelectionStateMsg
        {
            modeId = modeId,
            teamMode = _director.TeamModeOf(modeId) == Modes.TeamMode.None ? "none" : "two"
        });
    }

    /// <summary>Ortak durumu YALNIZ bağlı adminlere yollar (§5.3).</summary>
    public async Task BroadcastAdminStateAsync(string notice)
    {
        try
        {
            var admins = _registry.ConnectedAdminConnections();
            if (admins.Count == 0) return;
            var json = BuildAdminStateJson(notice);
            foreach (var connection in admins)
                await SendSafeAsync(connection, json, "(admin)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] admin_state yayını hatası: {ex.Message}");
        }
    }

    // ---- net_stats yayıncısı (§6.7): yalnız adminlere, 1 Hz ----
    // ⚠️ Ayrı bir döngü olmasının sebebi ritmi: admin_state OLAY tabanlıdır (seçim/komut değişince),
    // telemetri ise periyodiktir. admin_state'e bindirilse ya telemetri seyrek kalırdı ya da her
    // saniye bir "duyuru" yayını üretilirdi.

    private CancellationTokenSource? _netStatsCts;
    private Task? _netStatsLoop;

    public void Start()
    {
        if (_netStatsLoop is { IsCompleted: false }) return;
        _netStatsCts = new CancellationTokenSource();
        _netStatsLoop = Task.Run(() => NetStatsLoopAsync(_netStatsCts.Token));
    }

    public void Stop()
    {
        _netStatsCts?.Cancel();
        _netStatsCts = null;
        _netStatsLoop = null;
    }

    /// <summary>Oyuncu başına ping/jitter/kayıp — İSTEMCİNİN ölçüp <c>status</c> ile bildirdiği
    /// değerler (§6.7); sunucu yalnız taşır.
    /// <para>⚠️ <b>Broadcast değil:</b> hedef yalnız bağlı adminler. Herkese yayınlamak oyuncu
    /// sayısıyla kare büyüyen bir fan-out üretirdi — yani bu telemetrinin ölçmek için var olduğu
    /// sorunun aynısını.</para>
    /// <para>Kaybı zararsızdır: bir sonraki saniye yenisi gelir, uzlaştırma gerekmez. Roster'a
    /// (<c>lobby_state</c>) bu yüzden hiç girmiyor — orada bir <c>version</c> ve uzlaştırma
    /// protokolü var, saniyede bir çevrilirse anlamsızlaşır.</para></summary>
    private async Task NetStatsLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var entries = new List<NetStatsEntry>();

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token)) break;
            }
            catch (OperationCanceledException) { break; }

            try
            {
                var admins = _registry.ConnectedAdminConnections();
                // Kimse bakmıyorsa serileştirme bile yapılmaz: telemetrinin tek tüketicisi operatör
                // ekranıdır ve boşa üretmek boşa pakettir.
                if (admins.Count == 0) continue;

                entries.Clear();
                foreach (var state in _registry.Snapshot())
                {
                    if (state.Role != "player" || !state.IsConnected) continue;
                    entries.Add(new NetStatsEntry
                    {
                        playerId = state.PlayerId,
                        rttMs = state.RttMs,
                        jitterMs = state.JitterMs,
                        lossPct = state.LossPct
                    });
                }

                if (entries.Count == 0) continue;

                var json = JsonUtil.Serialize(new NetStatsMsg { players = entries.ToArray() });
                foreach (var connection in admins)
                    await SendSafeAsync(connection, json, "(admin)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Lobby] net_stats yayını hatası: {ex.Message}");
            }
        }
    }

    private string BuildAdminStateJson(string notice)
    {
        lock (_selectionGate)
        {
            return JsonUtil.Serialize(new AdminStateMsg
            {
                modeId = _selectedModeId,
                sceneName = _selectedSceneName,
                roundSeconds = _selectedRoundSeconds,
                scoreLimit = _selectedScoreLimit,
                countdownSeconds = _selectedCountdownSeconds,
                // Seçim DEĞİL, yürürlükteki durum (§5.2): anahtar koşan maçta da geçerli olduğu için
                // seçim kilidine (CanChangeSelection) girmez ve buradan olduğu gibi yayılır.
                friendlyFire = _director.FriendlyFire,
                notice = notice,
                adminCount = _registry.ConnectedAdminCount(),
                // Mekan bu oturum boyunca sabittir (açılışta seçilir), ama admin_state ile
                // taşınır: geç bağlanan admin de ilk mesajda hangi arenaları görebileceğini öğrenir.
                venueId = _director.VenueId,
                venueScenes = _director.VenueScenes.ToArray()
            });
        }
    }

    /// <summary>Roster'ı kirli işaretler; yayıncı koşmuyorsa başlatır (§5.3).
    /// <para>Bu aynı zamanda <b>birleştiricidir</b>: bir yayın uçarken gelen N değişiklik tek bir
    /// ek yayına çöker — 16 oyuncu aynı anda bağlanınca 16 tam roster yayını değil 2 tane olur.</para></summary>
    private void MarkRosterDirty()
    {
        lock (_broadcastGate)
        {
            _rosterDirty = true;
            if (_broadcasting) return;
            _broadcasting = true;
        }
        _ = RunRosterBroadcastLoopAsync();
    }

    /// <summary>Kirli oldukça yayınlar, temizlenince durur. <b>Aynı anda tek örnek koşar</b> —
    /// lobby_state sürümünün monotonluğu ve sıra garantisi buradan gelir.</summary>
    private async Task RunRosterBroadcastLoopAsync()
    {
        try
        {
            while (true)
            {
                lock (_broadcastGate)
                {
                    if (!_rosterDirty)
                    {
                        _broadcasting = false;
                        return;
                    }
                    _rosterDirty = false;
                }
                await BroadcastLobbyStateAsync();
            }
        }
        catch (Exception ex)
        {
            // Buraya düşmek beklenmez (yayın kendi içinde yutuyor) ama düşerse bayrağı bırak:
            // aksi hâlde _broadcasting takılı kalır ve roster bir daha HİÇ yayınlanmaz.
            lock (_broadcastGate) _broadcasting = false;
            Console.WriteLine($"[Lobby] roster yayıncısı durdu: {ex.Message}");
        }
    }

    /// <summary>Roster'ın TAM anlık görüntüsünü tüm bağlı soketlere yollar (§5.3 lobby_state)
    /// ve sürümü artırır. ⚠️ <b>Yalnız yayıncı döngüden çağrılır</b> — doğrudan çağırmak eşzamanlı
    /// yayın demektir ve sıra garantisini bozar.</summary>
    private async Task BroadcastLobbyStateAsync()
    {
        try
        {
            var snapshot = _registry.Snapshot();
            var msg = new LobbyStateMsg
            {
                version = Interlocked.Increment(ref _rosterVersion),
                players = snapshot.OrderBy(p => p.PlayerId).Select(p => p.ToPlayerInfo()).ToArray()
            };
            var json = JsonUtil.Serialize(msg);
            foreach (var state in snapshot)
            {
                var connection = state.Socket;
                if (connection == null || !state.IsConnected) continue;
                await SendSafeAsync(connection, json, state.Name);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] lobby_state yayını hatası: {ex.Message}");
        }
    }

    /// <summary>Tek bir bağlantıya yollanacak roster (uzlaştırma yolu). ⚠️ Sürümü <b>artırmaz</b>:
    /// geride kalan istemciye mevcut sürümü göndeririz, yeni bir sürüm üretmeyiz.</summary>
    private string BuildLobbyStateJson() => JsonUtil.Serialize(new LobbyStateMsg
    {
        version = Volatile.Read(ref _rosterVersion),
        players = _registry.Snapshot().OrderBy(p => p.PlayerId).Select(p => p.ToPlayerInfo()).ToArray()
    });

    private static async Task SendSafeAsync(ClientConnection connection, string json, string who)
    {
        try
        {
            await connection.SendTextAsync(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Lobby] gönderim başarısız ({who}): {ex.Message}");
        }
    }
}
