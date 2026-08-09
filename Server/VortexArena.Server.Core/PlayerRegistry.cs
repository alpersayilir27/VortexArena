#nullable enable
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>deviceId → PlayerState kaydı: playerId tahsisi (1..PLAYER_ID_MAX), devices.json ad
/// kalıcılığı (ad havuzdan rastgele + 1..99 forma numarası, ikisi de otomatik), takım dengeleme
/// ve bağlantı süpürmesi (HEARTBEAT_TIMEOUT → RECONNECT_GRACE).
/// (Cosmos DeviceRegistry + DeviceNameStore desenlerinin birleşimi.)
/// <para>
/// <b>Rol başına kalıcılık farkı (§2):</b> oyuncu kaydı kalıcıdır (deviceId sabit, kopunca
/// Reconnecting olur ve geri beklenir, adı devices.json'a yazılır). Admin kaydı OTURUMLUKtur —
/// deviceId'si her oturumda benzersizdir, kopunca kayıt tümüyle silinir ve adı diske yazılmaz;
/// aksi hâlde admin'i her açıp kapatma roster'a hayalet satır ve tükenen playerId bırakırdı.
/// ⚠️ Admin bu yüzden Reconnecting durumuna HİÇ girmez: geri gelen admin yeni bir kimlikle gelir,
/// "yeniden bağlanıyor" satırı yalan olurdu.
/// </para>
/// <para>
/// <b>Kaydın ömrü (§2/§8):</b> soket düşer → <c>Reconnecting</c> → RECONNECT_GRACE dolar →
/// maç katılımcısıysa <c>Left</c> (kayıt maç sonuna kadar durur, §10.2), değilse kayıt SİLİNİR ve
/// playerId havuza döner. Ayrı bir playerId rezervasyon defteri gerekmez: <c>Left</c> kayıt
/// <c>_players</c>'ta durduğu sürece <see cref="NextFreePlayerIdLocked"/> onu zaten atlar.
/// </para></summary>
public sealed class PlayerRegistry : IDisposable
{
    private static readonly JsonSerializerOptions DevicesJsonOptions = new()
    {
        WriteIndented = true,
        // DeviceRecord PROPERTY taşır (alan değil) → camelCase policy ile {"name":…,"number":…}.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Türkçe karakterler dosyada okunur kalsın
    };
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Oyuncu adı havuzu (§2) — ilk bağlantıda buradan RASTGELE seçilir. Adlar benzersiz
    /// değildir: havuz tükenince tekrar eder, ayırt edici alan numaradır. Admin bu havuzu
    /// KULLANMAZ (adı PC adından gelir ve diske yazılmaz).</summary>
    private static readonly string[] NamePool =
    {
        "umut", "alper", "ertu", "yunus", "resul", "enver", "enes", "nisa", "ceren", "tuğba",
        "elif", "pınar", "taner", "yasemin", "hüseyin", "deniz", "selin", "kaan", "burcu", "emre"
    };

    private readonly ConcurrentDictionary<string, PlayerState> _players = new();
    private readonly object _gate = new();
    private readonly Timer _connectionTimer;
    private readonly string _devicesPath;

    /// <summary>devices.json'ın bellekteki kopyası (deviceId → ad + numara). Numara sahipliğinin
    /// TEK doğruluk kaynağıdır: hiç bağlanmamış bir cihazın da burada satırı vardır, bu yüzden
    /// çakışma sorgusu <c>_players</c>'a değil buraya bakar.</summary>
    private Dictionary<string, DeviceRecord> _devices = new();

    /// <summary>İşçi thread'lerden tetiklenir. TryRegisterHello İÇİN raise edilmez —
    /// LobbyService welcome'ı yolladıktan sonra Announce çağırır (welcome her zaman
    /// lobby_state'ten önce gitsin diye).</summary>
    public event Action<PlayerState, PlayerChangeKind>? Changed;

    public PlayerRegistry(string devicesJsonPath)
    {
        _devicesPath = devicesJsonPath;
        LoadDevices();
        _connectionTimer = new Timer(_ => CheckConnections(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public IReadOnlyList<PlayerState> Snapshot() => _players.Values.ToList();

    public bool TryGet(string deviceId, out PlayerState state) => _players.TryGetValue(deviceId, out state!);

    public bool TryGetByPlayerId(int playerId, out PlayerState state)
    {
        foreach (var s in _players.Values)
        {
            if (s.PlayerId == playerId)
            {
                state = s;
                return true;
            }
        }
        state = null!;
        return false;
    }

    /// <summary>Bağlı admin sayısı (admin_state.adminCount).</summary>
    public int ConnectedAdminCount()
    {
        var count = 0;
        foreach (var state in _players.Values)
            if (state.IsConnected && state.Role == "admin") count++;
        return count;
    }

    /// <summary>Bağlı TÜM soketler — rol ayırmadan (selection_state yayını için anlık kopya).
    /// Admin'e özel olanlar için <see cref="ConnectedAdminConnections"/>.</summary>
    public List<ClientConnection> ConnectedConnections()
    {
        var result = new List<ClientConnection>();
        foreach (var state in _players.Values)
        {
            if (!state.IsConnected) continue;
            var socket = state.Socket;
            if (socket != null) result.Add(socket);
        }
        return result;
    }

    /// <summary>Bağlı adminlerin soketleri (admin_state yayını için anlık kopya).</summary>
    public List<ClientConnection> ConnectedAdminConnections()
    {
        var result = new List<ClientConnection>();
        foreach (var state in _players.Values)
        {
            if (!state.IsConnected || state.Role != "admin") continue;
            var socket = state.Socket;
            if (socket != null) result.Add(socket);
        }
        return result;
    }

    /// <summary>false = playerId havuzu tükendi (1..PLAYER_ID_MAX). Bu bir ürün kotası değil,
    /// u8 tel formatının tavanıdır. Aynı deviceId yeniden bağlanırsa eski soket kapatılır,
    /// playerId korunur (Docs/ArenaNet-Protokol.md §2).
    /// <para>⚠️ Mevcut kayıt HANGİ durumdan dönerse dönsün (<c>Reconnecting</c> ya da
    /// <c>Left</c>) Connected'a çekilir ve <b>ad/numara/takım/kills/deaths/score/MatchParticipant
    /// KORUNUR</b>: "kaldığı yerden devam" ve "eski satırına oturur" kuralı (§2) budur —
    /// sıfırlanırsa maç ortasında dönen oyuncu ikinci bir kimlik gibi görünür.</para></summary>
    public bool TryRegisterHello(HelloMsg hello, ClientConnection connection, out PlayerState state, out PlayerChangeKind kind)
    {
        ClientConnection? stale = null;
        lock (_gate)
        {
            if (_players.TryGetValue(hello.deviceId, out var existing))
            {
                state = existing;
                kind = PlayerChangeKind.Reconnected;
                if (existing.Socket != null && !ReferenceEquals(existing.Socket, connection))
                    stale = existing.Socket;
            }
            else
            {
                var playerId = NextFreePlayerIdLocked();
                if (playerId == 0)
                {
                    state = null!;
                    kind = PlayerChangeKind.Added;
                    return false;
                }
                state = new PlayerState { DeviceId = hello.deviceId, PlayerId = playerId };
                _players[hello.deviceId] = state;
                kind = PlayerChangeKind.Added;
            }

            state.Role = hello.role == "admin" ? "admin" : "player";
            ResolveIdentityLocked(state, hello.deviceName);
            state.Team = state.Role == "player"
                ? (string.IsNullOrEmpty(state.Team) ? SmallerTeamLocked() : state.Team)
                : ""; // admin oynamaz
            state.Scene = hello.currentScene ?? "";
            state.Scenes = hello.scenes != null ? new List<string>(hello.scenes) : new List<string>();
            state.Ready = false;
            // §10.6: sunucu yeniden bağlanan başlığın hizalamasını bilemez (uygulama yeniden
            // başlamış olabilir). Başlık kayıtlı anchor'dan geri yükleyince yeniden bildirir.
            state.Calibrated = false;
            state.CalibrationSource = "";
            // Hizalamaya ait teşhis alanları da onunla gider: sapma o hizalamanın, ölçüm hatası da
            // o oturumun bilgisiydi — taşınırsa operatör çözülmüş bir sorunu görmeye devam eder.
            state.FloorOffset = 0f;
            state.ScaleError = "";
            // §10.8: ölçü hizalamaya bağlı olduğu için o da bilinmiyor sayılır. Başlık kendi
            // kaydından ölçeği hemen yeniden bildirir (set_body_scale), yani operatör yeniden
            // ölçmek zorunda kalmaz.
            state.BodyScale = 0f;
            state.Connection = PlayerConnection.Connected;
            state.DisconnectedAt = default;
            state.LastSeen = DateTime.UtcNow;
            state.Socket = connection;
            // Her welcome yeni udpToken taşır; bayat UDP endpoint geçersizleşir
            // (istemci 0x00 UdpHello ile yeniden kaydolur).
            state.UdpToken = NextUdpToken();
            state.UdpEndpoint = null;
            // Bayat poz yeni oturuma taşınmasın; snapshot okuyucusu HasPose'a PoseGate altında
            // baktığı için sıfırlama da aynı kilit altında (PoseGate içinden asla _gate alınmaz).
            lock (state.PoseGate)
            {
                state.HasPose = false;
                state.LastSeq = 0;
                // §10.9: bayat ihlal bayrağı da yeni oturuma taşınmaz. Tazelik kapısı (LastPoseAt)
                // bunu zaten kapatıyor; alan yine de temizlenir ki "yeniden bağlandı ama hâlâ
                // duvarda görünüyor" gibi bir ara durum hiç doğmasın.
                state.InObstacle = false;
            }
        }

        stale?.Abort();
        return true;
    }

    /// <summary>TryRegisterHello'nun ertelenmiş bildirimi — LobbyService welcome'dan SONRA çağırır.</summary>
    public void Announce(PlayerState state, PlayerChangeKind kind) => Changed?.Invoke(state, kind);

    /// <summary>status kalp atışı (§5.1). ⚠️ <b>Koşulsuz Changed raise ETMEZ</b> — yalnız roster'da
    /// GÖRÜNEN bir alan (scene/battery/ctrlL/ctrlR/connection) gerçekten değiştiyse yayın tetikler.
    /// Koşulsuz yayın her status'u bir tam roster JSON'una çevirirdi: 18 istemci × 5 sn'de bir ×
    /// 18 alıcı ≈ saniyede 65 yayın, hiçbir şey değişmese bile. <c>Fps</c> PlayerInfo'da
    /// taşınmadığı için yayın tetiklemez.</summary>
    public void UpdateStatus(string deviceId, StatusMsg status)
    {
        if (!_players.TryGetValue(deviceId, out var state)) return;
        bool changed;
        lock (_gate)
        {
            var scene = status.scene ?? state.Scene;
            // Kumanda durumu Scene/Battery ile aynı taraftadır: KESİKLİ bir cihaz durumudur (üç
            // değerden biri), her status'ta oynayan bir sayı değil — yani yayını nadiren tetikler
            // ama düştüğü an operatörün ekranında görünmesi gerekir.
            changed = !state.IsConnected || state.Scene != scene || state.Battery != status.battery
                      || state.CtrlL != status.ctrlL || state.CtrlR != status.ctrlR;
            state.Scene = scene;
            state.Battery = status.battery;
            state.CtrlL = status.ctrlL;
            state.CtrlR = status.ctrlR;
            state.Fps = status.fps;
            // ⚠️ Ağ telemetrisi `changed`'e GİRMEZ — Fps ile birebir aynı gerekçe (§6.7): sürekli
            // değişen sayılar oldukları için her status'u bir tam roster yayınına çevirirlerdi.
            // Adminlere ayrı kanaldan gider (net_stats), roster'a hiç girmez.
            state.RttMs = status.rttMs;
            state.JitterMs = status.jitterMs;
            state.LossPct = status.lossPct;
            state.LastSeen = DateTime.UtcNow;
            state.Connection = PlayerConnection.Connected;
            state.DisconnectedAt = default;
        }
        if (changed) Changed?.Invoke(state, PlayerChangeKind.Updated);
    }

    /// <summary>set_identity (§5.1): ad ve/veya forma numarası. <b>Boş ad ve <c>0</c> numara mevcut
    /// değeri korur</b> (set_selection konvansiyonu) — "yalnız numarayı değiştir" tek çağrıdır.
    /// <para>
    /// Numara benzersizliği burada zorlanır ve <b>bağlantısız sahibi AYNI ANDA taşınır</b>: sahibi
    /// bağlıysa reddedilir (operatör onu roster'da görüp kendisi çözebilir), bağlantısızsa o
    /// cihaz 1'den itibaren ilk boş numaraya yazılır. Taşımayı sonraki bağlantıya ertelemek
    /// devices.json'ı o süre boyunca çift numaralı bırakırdı; bağlantısıza karşı da reddetmek ise
    /// operatörü kilitlerdi (numarayı tutan cihaz roster'da görünmez, serbest bırakılamaz).
    /// </para>
    /// <para>false = değişiklik olmadı ya da reddedildi; <paramref name="error"/> doluysa reddedildi
    /// ve metin operatöre <c>admin_state.notice</c> ile gösterilir.</para></summary>
    public bool SetIdentity(int playerId, string? name, int number, out string error)
    {
        error = "";
        if (!TryGetByPlayerId(playerId, out var state))
        {
            error = $"playerId {playerId} bulunamadı";
            return false;
        }

        var wantedName = name?.Trim();
        var setName = !string.IsNullOrEmpty(wantedName);
        var setNumber = number != 0;
        if (!setName && !setNumber) return false; // her iki alan da "koru" — sessizce çık

        if (setNumber)
        {
            if (number < ArenaProtocol.PLAYER_NUMBER_MIN || number > ArenaProtocol.PLAYER_NUMBER_MAX)
            {
                error = $"numara {number} geçersiz ({ArenaProtocol.PLAYER_NUMBER_MIN}-{ArenaProtocol.PLAYER_NUMBER_MAX})";
                return false;
            }
            if (state.Role == "admin")
            {
                error = "admin'e numara atanmaz";
                return false;
            }
        }

        var changed = false;
        lock (_gate)
        {
            if (setNumber && number != state.Number)
            {
                var holder = FindNumberHolderLocked(number, state.DeviceId);
                if (holder != null)
                {
                    if (_players.TryGetValue(holder, out var owner) && owner.IsConnected)
                    {
                        error = $"{number} numara {owner.Name}'da";
                        return false;
                    }
                    // Bağlantısız sahip: numarayı state almadan ÖNCE taşı ki ilk boş hesabı
                    // (devices.json hâlâ eski sahibi gösterirken) `number`'ı seçemesin.
                    var moved = NextFreeNumberLocked();
                    if (_devices.TryGetValue(holder, out var record)) record.Number = moved;
                    if (_players.TryGetValue(holder, out var absent)) absent.Number = moved;
                }
                state.Number = number;
                changed = true;
            }

            if (setName && state.Name != wantedName)
            {
                state.Name = wantedName!;
                changed = true;
            }

            // Admin deviceId'si oturumluk (§2) — diske yazmak devices.json'ı çöple doldururdu.
            if (changed && state.Role != "admin")
            {
                _devices[state.DeviceId] = new DeviceRecord { Name = state.Name, Number = state.Number };
                SaveDevicesLocked();
            }
        }

        if (changed) Changed?.Invoke(state, PlayerChangeKind.Updated);
        return changed;
    }

    /// <summary>Hazır bayrağını yazar. <b>Değişmediyse yayın yapmaz</b> (<see cref="SetIdentity"/>
    /// deseni): her <c>Changed</c> bir TAM <c>lobby_state</c> yayını demek, yani oyuncu sayısıyla
    /// çarpan bir fan-out. Bayrağın iki kullanıcısı da aynı değeri tekrar tekrar gönderebiliyor —
    /// yükleme kapısında istemcinin <c>set_ready</c>'si, tur toplanmasında oyuncunun taban
    /// bildirimi (§10.1).</summary>
    public void SetReady(string deviceId, bool ready)
    {
        if (!_players.TryGetValue(deviceId, out var state)) return;

        bool changed;
        lock (_gate)
        {
            changed = state.Ready != ready;
            state.Ready = ready;
        }

        if (changed) Changed?.Invoke(state, PlayerChangeKind.Updated);
    }

    public bool SetTeam(int playerId, string team)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        lock (_gate) state.Team = team;
        Changed?.Invoke(state, PlayerChangeKind.Updated);
        return true;
    }

    /// <summary>Kalibrasyon durumunu yazar (§10.6). Changed → lobby_state yayını; ayrı bir
    /// calibration mesajı YOKTUR, durum roster ile taşınır (§5.3).
    /// <para>Admin kalibre olmaz: <c>role != "player"</c> sessizce reddedilir, aksi hâlde admin
    /// arayüzünde kendisi "kalibresiz" diye sayılırdı.</para></summary>
    public bool SetCalibration(int playerId, bool calibrated, string? source, float floorOffset = 0f)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        if (state.Role != "player") return false;

        var nextSource = calibrated ? source ?? "" : "";
        // Sapma hizalamaya aittir (§10.6): hizalama düşünce o da düşer.
        var nextOffset = calibrated ? floorOffset : 0f;
        lock (_gate)
        {
            // Değişmediyse yayın YAPMA. Harita değişiminde her başlık kayıtlı anchor'dan geri
            // yükleyip aynı değeri yeniden bildirir; guard olmasa N oyuncu × N alıcı = N² gereksiz
            // lobby_state giderdi (16 oyuncuda 256 mesaj).
            // ⚠️ Sapma da karşılaştırmaya girer: aynı kaynakla yeniden kalibre olan oyuncunun yeni
            // sapması yoksa roster'da eski değer kalırdı.
            if (state.Calibrated == calibrated && state.CalibrationSource == nextSource
                && Math.Abs(state.FloorOffset - nextOffset) < 0.0001f) return false;
            state.Calibrated = calibrated;
            state.CalibrationSource = nextSource;
            state.FloorOffset = nextOffset;

            // §10.8: hizalama düşünce gövde ölçüsü de düşer — ölçü arena zeminine göre alınmıştı.
            // ⚠️ Kapı burasıdır, clear_calibration DEĞİL: başlığın kendi set_calibration{false}'u
            // da aynı sonucu doğurur ve tek yolu kapatmak kuralı işlevsiz bırakırdı.
            if (!calibrated)
            {
                state.BodyScale = 0f;
                // Ölçüm hatası da o hizalamanın bilgisiydi; bırakılırsa sıfırlanmış bir satır
                // hâlâ eski gerekçeyi gösterirdi.
                state.ScaleError = "";
            }
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
        return true;
    }

    /// <summary>
    /// Gövde ölçeğini yazar (§10.8). Sunucu sayıyı ÜRETMEZ, yalnız
    /// <c>[BODY_SCALE_MIN, BODY_SCALE_MAX]</c> aralığına kırpar: ölçümü istemci yapıyor ama sonuç
    /// herkesin ekranına gidiyor.
    /// <para>Kalibrasyon kapısı bilinçli olarak YOKTUR: yeniden bağlanan başlık kalibrasyonunu ve
    /// ölçeğini aynı anda bildiriyor ve iki mesajın sırasına bağlı bir kapı, ölçeği bazen sessizce
    /// düşürürdü. Hizalama gerçekten geçersizse ölçeği <see cref="SetCalibration"/> zaten siler.</para>
    /// <para>Değişmediyse yayın yapılmaz — <see cref="SetCalibration"/> ile aynı gerekçe.</para>
    /// <para>⚠️ <b>Başarılı ölçüm <see cref="PlayerState.ScaleError"/>'ı da temizler</b> ve bunu
    /// AYNI yayında yapar: iki ayrı çağrıya bölünseydi tek bir ölçüm iki tam roster yayını
    /// üretirdi (§10.8).</para>
    /// </summary>
    public bool SetBodyScale(int playerId, float scale)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        if (state.Role != "player") return false;

        var clamped = Math.Clamp(scale, ArenaProtocol.BODY_SCALE_MIN, ArenaProtocol.BODY_SCALE_MAX);
        lock (_gate)
        {
            var scaleChanged = Math.Abs(state.BodyScale - clamped) >= 0.0001f;
            var errorCleared = state.ScaleError.Length > 0;
            if (!scaleChanged && !errorCleared) return false;
            state.BodyScale = clamped;
            state.ScaleError = "";
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
        return true;
    }

    /// <summary>Başarısız gövde ölçümünün gerekçesini yazar (§10.8). <b>Ölçeğe DOKUNMAZ</b> —
    /// başarısız ölçüm kayıtlı değeri geçersiz kılmaz, yalnız operatöre neden olmadığını söyler.
    /// <para>Boş gerekçe alanı temizler; değişmediyse yayın yapılmaz
    /// (<see cref="SetCalibration"/> ile aynı gerekçe).</para></summary>
    public bool SetScaleError(int playerId, string? error)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        if (state.Role != "player") return false;

        var next = error ?? "";
        lock (_gate)
        {
            if (state.ScaleError == next) return false;
            state.ScaleError = next;
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
        return true;
    }

    // ⚠️ Toplu sıfırlama (clear_calibration playerId:0) burada DEĞİL LobbyService'tedir ve buraya
    // geri taşınmaz: sıfırlamanın asıl işi bayrağı düşürmek değil komutu her başlığa İLETMEKtir
    // (§10.6) ve registry soket görmez. "Zaten kalibresiz olanı atla" kısayolu da tam burada
    // doğuyordu — yarım kalmış elle kalibrasyondaki oyuncunun bayrağı zaten `false`'tur, yani
    // atlanan tek grup komuta en çok ihtiyacı olan gruptu.

    /// <summary>0x00 UdpHello doğrulaması: playerId↔udpToken eşleşirse endpoint kaydedilir (§6.1).
    /// Pozlar yalnız kayıtlı endpoint'ten kabul edilir (StateHost 0x01 alımı).</summary>
    public bool TryRegisterUdpEndpoint(byte playerId, uint udpToken, IPEndPoint endpoint)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return false;
        lock (_gate)
        {
            if (udpToken == 0 || state.UdpToken != udpToken) return false;
            state.UdpEndpoint = endpoint;
        }
        return true;
    }

    /// <summary>Bir bağlantının recv döngüsü kapanınca çağrılır. Cihaz daha yeni bir bağlantıya
    /// geçtiyse no-op (yeniden bağlanma yarışına karşı).
    /// <para>Oyuncu <c>Reconnecting</c>'e düşer ve RECONNECT_GRACE boyunca geri beklenir;
    /// admin <see cref="RetireLocked"/> ile tümden silinir (§2).</para></summary>
    public void NotifyDisconnected(ClientConnection connection)
    {
        PlayerState? affected = null;
        var removed = false;
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (ReferenceEquals(state.Socket, connection))
                {
                    state.Socket = null;
                    state.Connection = PlayerConnection.Reconnecting;
                    state.DisconnectedAt = DateTime.UtcNow;
                    state.Ready = false;
                    affected = state;
                    removed = RetireLocked(state);
                    break;
                }
            }
        }
        if (affected != null)
            Changed?.Invoke(affected, removed ? PlayerChangeKind.Removed : PlayerChangeKind.Reconnecting);
    }

    /// <summary>Koşan maçın defterine yazar/siler (§10.2). Yalnız değiştiyse yayın yapar —
    /// <c>inMatch</c> roster'da GÖRÜNEN bir alan, koşulsuz yayın her çağrıyı bir tam
    /// lobby_state'e çevirirdi.</summary>
    public void SetMatchParticipant(int playerId, bool value)
    {
        if (!TryGetByPlayerId(playerId, out var state)) return;
        lock (_gate)
        {
            if (state.MatchParticipant == value) return;
            state.MatchParticipant = value;
        }
        Changed?.Invoke(state, PlayerChangeKind.Updated);
    }

    /// <summary>Maç kurulurken o an BAĞLI olan her oyuncuyu katılımcı işaretler (§10.2);
    /// dönüş = etkilenen kayıt sayısı.
    /// <para>⚠️ Değişiklik başına değil, <b>tek bir</b> <c>Updated</c> yayınlanır: oyuncu başına
    /// yayın N tam roster JSON'u demek olurdu ve hepsi aynı anlık görüntüyü taşırdı.</para></summary>
    public int MarkConnectedPlayersAsParticipants() => SetParticipantsForAll(true);

    /// <summary>Maç kapanınca defteri temizler (§10.2); dönüş = etkilenen kayıt sayısı.
    /// Toplu yayın kuralı <see cref="MarkConnectedPlayersAsParticipants"/> ile aynıdır.</summary>
    public int ClearMatchParticipants() => SetParticipantsForAll(false);

    private int SetParticipantsForAll(bool value)
    {
        var affected = 0;
        PlayerState? last = null;
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (state.Role != "player" || state.MatchParticipant == value) continue;
                // İşaretlerken yalnız BAĞLI olanlar deftere girer (§10.2); temizlerken herkes.
                if (value && !state.IsConnected) continue;
                state.MatchParticipant = value;
                last = state;
                affected++;
            }
        }
        if (last != null) Changed?.Invoke(last, PlayerChangeKind.Updated);
        return affected;
    }

    /// <summary>Maç kapanışında <c>Left</c> kayıtları siler (playerId'leri havuza döner);
    /// dönüş = silinen kayıt sayısı.
    /// <para>⚠️ Çağrı anı bağlayıcıdır (§10.2): defter <c>finished</c> fazının TAMAMI boyunca
    /// durur ve ancak <b>lobiye dönerken</b>, <c>return_to_lobby</c> yayınından SONRA kapanır.
    /// <c>match_end</c>'de silmek maç sonu tablosunu tam da okunduğu anda boşaltırdı — ayrılmış
    /// oyuncuların orada görünmesi bu özelliğin var olma sebebi.</para></summary>
    public int PurgeLeftParticipants()
    {
        var purged = new List<PlayerState>();
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (state.Connection != PlayerConnection.Left) continue;
                _players.TryRemove(state.DeviceId, out _);
                purged.Add(state);
            }
        }
        // Kilit dışında: her Changed bir lobby_state yayını tetikliyor (kilit altında gönderim yok).
        foreach (var state in purged) Changed?.Invoke(state, PlayerChangeKind.Removed);
        return purged.Count;
    }

    /// <summary>
    /// Atma (§5.4): kaydı roster'dan <b>tümüyle siler</b>, varsa bağlantısını çağırana verir
    /// (kapatmak onun işi). Kopmadan farkı budur — kopan cihaz <c>reconnecting</c> olarak listede
    /// <b>durur</b> (aynı gözlük geri geldiğinde playerId'sini ve adını korusun diye), atılan
    /// cihaz listeden kalkar. Aksi hâlde atma bağlantısız bir kayıtta hiçbir şey yapmaz ve
    /// operatör "AT"a bastıkça duran bir satır görürdü.
    /// <para>⚠️ Atma <b>katılımcılıktan da düşürür</b> (§10.2): kayıt tümüyle silindiği için
    /// <c>MatchParticipant</c> bayrağı onunla birlikte gider — ayrıca temizlemek gerekmez.
    /// Operatör bilinçli attıysa satır maç sonu tablosunda da yer almaz.</para>
    /// <para>⚠️ <c>devices.json</c>'a DOKUNMAZ: atma bir yasak değildir, cihaz yeniden
    /// bağlanırsa adını ve forma numarasını korur (playerId havuza döner, yenisi verilir).</para>
    /// </summary>
    public bool RemoveByPlayerId(int playerId, out PlayerState state, out ClientConnection? connection)
    {
        lock (_gate)
        {
            if (!TryGetByPlayerId(playerId, out state!))
            {
                connection = null;
                return false;
            }

            connection = state.Socket;
            state.Socket = null;
            state.Connection = PlayerConnection.Left;
            state.Ready = false;
            state.MatchParticipant = false;
            _players.TryRemove(state.DeviceId, out _);
        }

        Changed?.Invoke(state, PlayerChangeKind.Removed);
        return true;
    }

    /// <summary>
    /// Bağlantı süpürmesi (§8) — <b>iki aşamalı</b>:
    /// (a) <c>Connected</c> ama HEARTBEAT_TIMEOUT boyunca status gelmedi → soket ölü sayılır,
    /// koparılır ve cihaz <c>Reconnecting</c>'e düşer;
    /// (b) <c>Reconnecting</c> RECONNECT_GRACE boyunca sürdü → oyuncu oyundan çıkarılır: maç
    /// katılımcısıysa <c>Left</c> (kayıt maç sonuna kadar durur, §10.2), değilse kayıt silinir ve
    /// playerId havuza döner.
    /// <para>Admin her iki aşamada da tümden silinir (§2 — <see cref="RetireLocked"/>).</para>
    /// <para>⚠️ Kilit altında ne gönderim ne olay var: iş listesi toplanır, <c>Abort</c> ve
    /// <c>Changed</c> kilit dışında koşar.</para>
    /// </summary>
    private void CheckConnections()
    {
        var now = DateTime.UtcNow;
        var heartbeat = TimeSpan.FromSeconds(ArenaProtocol.HEARTBEAT_TIMEOUT);
        var grace = TimeSpan.FromSeconds(ArenaProtocol.RECONNECT_GRACE);
        var pending = new List<(PlayerState State, ClientConnection? Socket, PlayerChangeKind Kind)>();
        lock (_gate)
        {
            foreach (var state in _players.Values)
            {
                if (state.IsConnected)
                {
                    if (now - state.LastSeen <= heartbeat) continue;

                    state.Connection = PlayerConnection.Reconnecting;
                    state.DisconnectedAt = now;
                    state.Ready = false;
                    var socket = state.Socket;
                    state.Socket = null;
                    pending.Add((state, socket,
                        RetireLocked(state) ? PlayerChangeKind.Removed : PlayerChangeKind.Reconnecting));
                    continue;
                }

                if (state.Connection != PlayerConnection.Reconnecting) continue;
                if (now - state.DisconnectedAt <= grace) continue;

                if (state.MatchParticipant)
                {
                    // Kayıt DURUR: adı ve sayaçları maç sonu tablosunda görünmeli (§10.2).
                    state.Connection = PlayerConnection.Left;
                    pending.Add((state, null, PlayerChangeKind.Left));
                    continue;
                }

                _players.TryRemove(state.DeviceId, out _);
                pending.Add((state, null, PlayerChangeKind.Removed));
            }
        }
        foreach (var (state, socket, kind) in pending)
        {
            socket?.Abort();
            Changed?.Invoke(state, kind);
        }
    }

    /// <summary>
    /// Bağlantısı düşmüş kaydı emekliye ayırır. <b>Admin kaydı tümüyle silinir</b> (deviceId'si
    /// zaten oturumluk — §2: geri gelen admin yeni bir kimlikle gelir, eski satır roster'da
    /// hayalet olarak kalırdı ve playerId'si havuza dönmezdi). Oyuncu kaydı DURUR: deviceId'si
    /// kalıcıdır, aynı gözlük geri geldiğinde playerId'sini ve adını korumalıdır.
    /// <para>true = silindi. Çağıran <c>_gate</c> kilidini tutuyor olmalıdır.</para>
    /// </summary>
    private bool RetireLocked(PlayerState state)
    {
        if (state.Role != "admin") return false;
        _players.TryRemove(state.DeviceId, out _);
        return true;
    }

    public void Dispose() => _connectionTimer.Dispose();

    // ---- playerId / takım / token tahsisi ----

    private int NextFreePlayerIdLocked()
    {
        var used = new HashSet<int>();
        foreach (var state in _players.Values) used.Add(state.PlayerId);
        for (var id = 1; id <= ArenaProtocol.PLAYER_ID_MAX; id++)
            if (!used.Contains(id)) return id;
        return 0; // u8 havuzu tükendi (ürün kotası değil, tel formatı tavanı)
    }

    /// <summary>Yeni oyuncuyu az kişili takıma koyar (eşitlikte red); admin set_team ile değiştirir.</summary>
    private string SmallerTeamLocked()
    {
        int red = 0, blue = 0;
        foreach (var state in _players.Values)
        {
            if (state.Role != "player") continue;
            if (state.Team == "red") red++;
            else if (state.Team == "blue") blue++;
        }
        return red <= blue ? "red" : "blue";
    }

    private static uint NextUdpToken()
    {
        Span<byte> bytes = stackalloc byte[4];
        uint token;
        do
        {
            Random.Shared.NextBytes(bytes);
            token = BitConverter.ToUInt32(bytes);
        } while (token == 0);
        return token;
    }

    // ---- devices.json kimlik kalıcılığı (ad + numara; UTF-8 BOM'suz) ----

    /// <summary>devices.json'ı okur. <b>İki biçimi de kabul eder:</b> v1 <c>deviceId → "ad"</c>
    /// (numara 0 sayılır, ilk bağlantıda atanır) ve v2 <c>deviceId → {name, number}</c>.
    /// Yalnız yapıcıdan çağrılır (tek thread).</summary>
    private void LoadDevices()
    {
        _devices = new();
        if (!File.Exists(_devicesPath)) return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_devicesPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                switch (entry.Value.ValueKind)
                {
                    case JsonValueKind.String: // v1 — yalnız ad, numara sonra atanır
                        _devices[entry.Name] = new DeviceRecord { Name = entry.Value.GetString() ?? "" };
                        break;
                    case JsonValueKind.Object: // v2 — ad + numara
                        _devices[entry.Name] = new DeviceRecord
                        {
                            Name = entry.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                                ? n.GetString() ?? ""
                                : "",
                            Number = entry.Value.TryGetProperty("number", out var num) && num.TryGetInt32(out var parsed)
                                ? parsed
                                : 0
                        };
                        break;
                }
            }
            ResolveDuplicateNumbersLocked();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerRegistry] devices.json okunamadı ({ex.Message}) — boş kimlik haritasıyla başlanıyor.");
            _devices = new();
        }
    }

    /// <summary>Yüklemede aynı numarayı taşıyan kayıtları ayıklar: ilk gelen numarayı korur,
    /// sonrakiler ilk boş numaraya taşınır. Gerekçesi elle düzenlenmiş dosyadır — "iki cihaz aynı
    /// numarayı taşımaz" değişmez kuralı dosyaya değil bu sınıfa aittir, o yüzden girişte zorlanır.</summary>
    private void ResolveDuplicateNumbersLocked()
    {
        var seen = new HashSet<int>();
        var repaired = 0;
        foreach (var record in _devices.Values)
        {
            if (record.Number == 0) continue;
            if (seen.Add(record.Number)) continue;

            record.Number = NextFreeNumberLocked();
            if (record.Number != 0) seen.Add(record.Number);
            repaired++;
        }
        if (repaired == 0) return;

        Console.WriteLine($"[PlayerRegistry] devices.json'da {repaired} çift numara bulundu — yeniden numaralandı.");
        SaveDevicesLocked();
    }

    /// <summary>
    /// <b>Oyuncu:</b> devices.json'da kaydı varsa ad+numara oradan gelir (kalıcı kimlik); yoksa ad
    /// havuzdan RASTGELE, numara 1'den itibaren ilk boş olarak atanıp dosyaya yazılır. v1'den
    /// yükseltilen (numarasız) kayda ilk görüşte numara verilir.
    /// <para>
    /// <b>Admin:</b> ad `hello.deviceName` (PC adı; boşsa "Admin"), numara YOK (0) — admin oynamaz.
    /// Diske YAZILMAZ: admin deviceId'si oturumluk olduğu için her açılış çöp bir satır eklerdi.
    /// Aynı ad başka bir bağlı admin'de kullanılıyorsa sonuna " (2)", " (3)"… eklenir.
    /// </para>
    /// </summary>
    private void ResolveIdentityLocked(PlayerState state, string? fallbackDeviceName)
    {
        if (state.Role == "admin")
        {
            state.Name = UniqueAdminNameLocked(state.DeviceId, fallbackDeviceName);
            state.Number = 0;
            return;
        }

        if (_devices.TryGetValue(state.DeviceId, out var record) && !string.IsNullOrWhiteSpace(record.Name))
        {
            state.Name = record.Name;
            state.Number = record.Number;
            if (state.Number != 0) return;

            state.Number = NextFreeNumberLocked(); // v1'den yükseltilen kayıt
            record.Number = state.Number;
            SaveDevicesLocked();
            return;
        }

        state.Name = PickPoolNameLocked();
        state.Number = NextFreeNumberLocked();
        _devices[state.DeviceId] = new DeviceRecord { Name = state.Name, Number = state.Number };
        SaveDevicesLocked();
    }

    /// <summary>Havuzdan RASTGELE ad (§2): önce hiçbir kayıtlı cihazın kullanmadığı adlar arasından,
    /// hepsi kullanımdaysa havuzun tamamından. <b>Adlar benzersiz değildir</b> — ayırt edici alan
    /// numaradır, bu yüzden havuzun tükenmesi hata değil normal işleyiştir.</summary>
    private string PickPoolNameLocked()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in _devices.Values)
            if (!string.IsNullOrEmpty(record.Name)) taken.Add(record.Name);

        var free = new List<string>(NamePool.Length);
        foreach (var candidate in NamePool)
            if (!taken.Contains(candidate)) free.Add(candidate);

        return free.Count > 0
            ? free[Random.Shared.Next(free.Count)]
            : NamePool[Random.Shared.Next(NamePool.Length)];
    }

    /// <summary>1'den itibaren hiçbir KAYITLI cihazın kullanmadığı ilk numara (§2). Sıralıdır,
    /// rastgele değil: işletmede küçük ve akılda kalır sayılar kullanılsın. <c>0</c> = havuz dolu
    /// (100+ kayıtlı cihaz) — cihaz numarasız kalır, operatör elle numaralar.</summary>
    private int NextFreeNumberLocked()
    {
        var used = new HashSet<int>();
        foreach (var record in _devices.Values)
            if (record.Number != 0) used.Add(record.Number);

        for (var n = ArenaProtocol.PLAYER_NUMBER_MIN; n <= ArenaProtocol.PLAYER_NUMBER_MAX; n++)
            if (!used.Contains(n)) return n;

        Console.WriteLine($"[PlayerRegistry] {ArenaProtocol.PLAYER_NUMBER_MIN}-{ArenaProtocol.PLAYER_NUMBER_MAX} " +
                          $"numara havuzu dolu ({_devices.Count} kayıtlı cihaz) — yeni cihaz numarasız (0) kalıyor.");
        return 0;
    }

    /// <summary>Verilen numarayı tutan cihazın deviceId'si (kendisi hariç), yoksa null.
    /// Sorgu <c>_players</c>'a DEĞİL <c>_devices</c>'a yapılır: numarayı hiç bağlanmamış (bellekte
    /// PlayerState'i olmayan) bir cihaz da tutuyor olabilir.</summary>
    private string? FindNumberHolderLocked(int number, string exceptDeviceId)
    {
        foreach (var entry in _devices)
            if (entry.Value.Number == number && entry.Key != exceptDeviceId) return entry.Key;
        return null;
    }

    /// <summary>Başka bir admin kaydının kullanmadığı ilk ad ("Ofis-PC", "Ofis-PC (2)", …).</summary>
    private string UniqueAdminNameLocked(string deviceId, string? fallbackDeviceName)
    {
        var baseName = string.IsNullOrWhiteSpace(fallbackDeviceName) ? "Admin" : fallbackDeviceName!.Trim();

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in _players.Values)
        {
            // Kendi kaydımız (yeniden bağlanma) adı serbest bırakır; oyuncu adları da çakışmasın.
            if (state.DeviceId == deviceId) continue;
            if (!string.IsNullOrEmpty(state.Name)) taken.Add(state.Name);
        }

        if (!taken.Contains(baseName)) return baseName;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} ({n})";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private void SaveDevicesLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(_devicesPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_devicesPath, JsonSerializer.Serialize(_devices, DevicesJsonOptions), Utf8NoBom);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PlayerRegistry] devices.json yazılamadı: {ex.Message}");
        }
    }
}
