#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>UDP durum kanalı (statePort). 0x00 UdpHello kaydı — playerId↔udpToken doğrulanır,
/// endpoint kaydedilir, aynı 6 bayt ack olarak geri yollanır (§6.1). 0x01 PoseUpdate alımı —
/// yalnız kayıtlı endpoint'ten, u16 sarmalamalı seq kontrolüyle (§6.2). 0x02 Snapshot yayını —
/// 20 Hz, pozlu oyuncular tek pakette UDP kayıtlı herkese (admin dahil) yollanır (§6.3).
/// 0x03 FireEvent alımı + 0x04 EventBatch yayını — atış/atma olayları doğrulanmadan relay edilir
/// (§6.4/6.5).
/// <para><b>Thread sözleşmesi:</b> alım (recv) ve yayın (20 Hz timer) AYRI thread'lerdedir. Recv
/// thread'i <see cref="MatchDirector"/>'ın maç kilidine ASLA girmez (§10.3) — kapıyı
/// <see cref="MatchDirector.ShotRelayOpen"/> volatile bayrağından, oyuncu durumunu
/// <see cref="PlayerState.Alive"/>/<c>Calibrated</c>'dan kilitsiz okur. İki thread arasında
/// paylaşılan tek mutable yapı <see cref="_events"/> kuyruğudur.</para></summary>
public sealed class StateHost
{
    private readonly PlayerRegistry _registry;
    private readonly int _port;

    /// <summary>Atış olayı relay kapısı için (§6.5) — yalnız <c>ShotRelayOpen</c> okunur.</summary>
    private readonly MatchDirector _matchDirector;

    /// <summary>Relay'i geçen olaylar: recv thread'i yazar, 20 Hz yayın thread'i okur.
    /// <para>Kilit yerine <see cref="ConcurrentQueue{T}"/>: recv yolu 20 Hz poz akışıyla aynı
    /// thread'de koşuyor ve bir kilidin arkasında beklemesi o akışı da bekletirdi.</para></summary>
    private readonly ConcurrentQueue<FireEventEntry> _events = new();

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Task? _snapshotLoop;

    // ---- Telemetri sabitleri ----

    /// <summary>Nominal tik aralığı (ms) — tik kaymasının referansı.</summary>
    private const double NominalTickMs = 1000.0 / ArenaProtocol.SNAPSHOT_RATE_HZ;

    /// <summary>Bu eşikleri aşan oyuncu için saniyelik özete EK bir <c>[net]</c> satırı basılır.
    /// Her oyuncu için her saniye satır basmak (10 oyuncu = 10 satır/sn) konsolu okunamaz hâle
    /// getirirdi; sorunlu olanı görünür kılmak yeter.</summary>
    private const double JitterWarnMs = 25.0;
    private const double LossWarnPct = 2.0;

    /// <summary>Olay <c>seq</c> boşluğunun kayıp sayılabileceği üst sınır. Olay kanalında sıra
    /// garantisi YOKTUR (§6.4), yani sırası bozuk gelen bir paket <c>(ushort)</c> farkını ~65535
    /// yapar; tavan olmadan sayaç bunu "65 bin kayıp" diye raporlar. Bir saniyede bir oyuncunun
    /// üretebileceği olay sayısının çok üstünde, 65535'in ise çok altında olacak kadar seçildi.</summary>
    private const int EventGapMax = 512;

    /// <summary>
    /// İskelet blob'u bu kadar süre tazelenmezse yayından düşürülür (§6.10 "girdi yoksa paket yok").
    /// <para><b>Yerel sabit, protokol sabiti DEĞİL:</b> istemcinin bu sayıyı bilmesine gerek yok —
    /// gövdesi olmayan bir avatar zaten son karesinde donuyor ve avatarın kendi yaşam süresi
    /// snapshot'tan geliyor (§6.3). Protokole eklenirse iki tarafın uyması gereken bir sözleşme
    /// olurdu, oysa bu yalnız sunucunun bayat veri yayınlamama kararıdır.</para>
    /// <para>Süre, <see cref="ArenaProtocol.SKELETON_RATE_HZ"/> aralığının birkaç katıdır: normal
    /// paket kaybı gövdeyi düşürmesin, gerçekten susmuş bir gönderen ise yayında kalmasın.</para>
    /// </summary>
    private const double SkeletonStaleMs = 500.0;

    // ---- Telemetri sayaçları ----
    // ÇIKIŞ sayaçlarına yalnız yayın thread'i dokunur (yazan da okuyan da o) → kilit gerekmez.
    // GİRİŞ sayaçlarını recv thread'i yazar, yayın thread'i saniyede bir okuyup sıfırlar →
    // Interlocked şart. Oyuncu başına sayaçlar PlayerState'te (poz için PoseGate altında).

    private long _txSnapshotPackets, _txSnapshotBytes;
    private long _txEventPackets, _txEventBytes;
    private long _txSkeletonPackets, _txSkeletonBytes;
    private long _rxPackets, _rxBytes;

    /// <summary>Snapshot sayacının içindeki, olayları da taşıyan (<c>0x05</c>) datagram adedi —
    /// §6.8 birleştirmesinin ne kadar tuttuğunu görmek için. Ayrı bir kanal DEĞİL, alt kümedir.</summary>
    private long _txCombinedPackets;

    /// <summary>RECV thread'inden gönderilen yanıtlar: <c>0x00</c> ack'i ve <c>0x06</c> echo'su.
    /// <b>Yayın sayaçlarından ayrı durmalarının tek sebebi thread'idir</b> — yayın thread'i kendi
    /// sayaçlarını kilitsiz artırabilir, bunlar başka thread'den yazıldığı için Interlocked ister.</summary>
    private long _txAckPackets, _txAckBytes;

    /// <summary>Alındı ama İŞLENMEDİ: kayıtsız/yabancı endpoint, kısa paket, eski <c>seq</c>,
    /// bilinmeyen tip. "Geldi ama işlenmedi" ile "hiç gelmedi" sahada bambaşka iki teşhistir.</summary>
    private long _rxRejected;

    /// <summary>Başarılı UDP kayıt bildirimi (konsol satırı için).</summary>
    public event Action<byte, IPEndPoint>? UdpRegistered;

    /// <param name="matchDirector">Atış relay kapısının kaynağı (§6.5). <b>Neden constructor:</b>
    /// <c>Program.cs</c>'te director StateHost'tan ÖNCE kuruluyor, yani döngüsel bağımlılık yok —
    /// sonradan set edilen bir property "kurulumu unutunca olaylar sessizce düşer" tuzağını
    /// üretirdi, zorunlu parametre unutulamaz.</param>
    public StateHost(PlayerRegistry registry, int port, MatchDirector matchDirector)
    {
        _registry = registry;
        _port = port;
        _matchDirector = matchDirector;
    }

    public void Start()
    {
        if (_loop is { IsCompleted: false } || _snapshotLoop is { IsCompleted: false }) return;
        var udp = new UdpClient(_port);
        _udp = udp;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(() => ReceiveLoopAsync(udp, token));
        _snapshotLoop = Task.Run(() => SnapshotLoopAsync(udp, token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _udp?.Close();
        _udp = null;
        _cts = null;
        _loop = null;
        _snapshotLoop = null;
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException)
            {
                // Windows'ta ulaşılamayan hedefe gönderim sonrası recv 10054 fırlatabilir — döngü ölmesin.
                continue;
            }

            var data = result.Buffer;
            if (data.Length == 0) continue;

            // Giriş hacmi: tip ayrımı yapılmadan sayılır (kanal başına ayrıştırmanın teşhis değeri
            // yok — yukarı yönde zaten neredeyse tamamı 0x01'dir).
            Interlocked.Increment(ref _rxPackets);
            Interlocked.Add(ref _rxBytes, data.Length);

            switch (data[0])
            {
                case UdpPacketType.UdpHello:
                    if (data.Length < UdpHello.SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    await HandleUdpHelloAsync(udp, data, result.RemoteEndPoint, token);
                    break;
                case UdpPacketType.PoseUpdate:
                    if (data.Length < PoseUpdate.SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    HandlePoseUpdate(data, result.RemoteEndPoint);
                    break;
                case UdpPacketType.FireEvent:
                    if (data.Length < FireEvent.SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    HandleFireEvent(data, result.RemoteEndPoint);
                    break;
                case UdpPacketType.RttProbe:
                    if (data.Length < RttProbe.SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    await HandleRttProbeAsync(udp, data, result.RemoteEndPoint, token);
                    break;
                case UdpPacketType.SkeletonUpdate:
                    if (data.Length < SkeletonUpdate.HEADER_SIZE) { Interlocked.Increment(ref _rxRejected); break; }
                    HandleSkeletonUpdate(data, result.RemoteEndPoint);
                    break;
                default:
                    // Bilinmeyen paket tipi — yok sayılır (ileri sürüm uyumluluğu).
                    Interlocked.Increment(ref _rxRejected);
                    break;
            }
        }
    }

    private async Task HandleUdpHelloAsync(UdpClient udp, byte[] data, IPEndPoint remote, CancellationToken token)
    {
        UdpHello hello;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // tip baytı dispatcher'da tüketildi sayılır
            hello = UdpHello.Read(reader);
        }

        if (!_registry.TryRegisterUdpEndpoint(hello.playerId, hello.udpToken, remote))
        {
            Console.WriteLine($"[StateHost] udp_hello reddedildi: playerId {hello.playerId} ({remote}) token eşleşmedi.");
            return;
        }

        try
        {
            // Ack = aynı 6 baytın geri yollanması; istemci ack gelene dek 1 sn arayla tekrarlar.
            await udp.SendAsync(data.AsMemory(0, UdpHello.SIZE), remote, token);
            Interlocked.Increment(ref _txAckPackets);
            Interlocked.Add(ref _txAckBytes, UdpHello.SIZE);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StateHost] ack gönderimi başarısız ({remote}): {ex.Message}");
            return;
        }
        UdpRegistered?.Invoke(hello.playerId, remote);
    }

    /// <summary>0x06 RttProbe: gelen 6 baytı <b>aynen</b> geri yollar (§6.7). Sunucu tarafında durum
    /// YOKTUR ve damga OKUNMAZ — yorumlayan taraf istemcidir (saat senkronu bu yüzden gerekmiyor).
    /// <para>Doğrulama poz/olay yolundaki kuralın aynısı: yalnız <c>0x00</c> ile kaydedilmiş
    /// endpoint'ten. Ret sessizdir — 1 Hz × oyuncu hızında log bile gereksiz gürültüdür.</para>
    /// <para>⚠️ Bu yol <b>recv thread'inde</b> koşar; <see cref="MatchDirector"/> kilidine girmez ve
    /// hiçbir oyuncu alanına yazmaz (ölçümün tamamı istemcide).</para></summary>
    private async Task HandleRttProbeAsync(UdpClient udp, byte[] data, IPEndPoint remote, CancellationToken token)
    {
        // playerId doğrudan okunur: damgayı çözmeye gerek yok, tek ihtiyaç endpoint eşleşmesi.
        var playerId = data[1];
        if (!_registry.TryGetByPlayerId(playerId, out var state)
            || state.UdpEndpoint == null || !state.UdpEndpoint.Equals(remote))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        try
        {
            await udp.SendAsync(data.AsMemory(0, RttProbe.SIZE), remote, token);
            Interlocked.Increment(ref _txAckPackets);
            Interlocked.Add(ref _txAckBytes, RttProbe.SIZE);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Ulaşılamayan hedef (10054 vb.) — echo kaybı zararsız, bir sonraki yoklama gelir.
        }
    }

    /// <summary>0x01 PoseUpdate alımı: yalnız 0x00 ile kaydedilmiş endpoint'ten kabul edilir,
    /// eski/yinelenen seq atılır, kabul edilen poz PoseGate altında saklanır.
    /// 20 Hz akış olduğu için konsola satır basılmaz; ret de sessizdir.</summary>
    private void HandlePoseUpdate(byte[] data, IPEndPoint remote)
    {
        PoseUpdate pose;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // tip baytı dispatcher'da tüketildi sayılır
            pose = PoseUpdate.Read(reader);
        }

        if (!_registry.TryGetByPlayerId(pose.playerId, out var state))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        // Kayıtsız/yabancı kaynaktan poz kabul edilmez (spoof koruması, §6.1).
        if (state.UdpEndpoint == null || !state.UdpEndpoint.Equals(remote))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        var stamp = Stopwatch.GetTimestamp();

        lock (state.PoseGate)
        {
            // u16 sarmalamalı sıra kontrolü: (short) farkı 65535→0 geçişini doğru sıralar.
            if (state.HasPose && (short)(pose.seq - state.LastSeq) <= 0)
            {
                Interlocked.Increment(ref _rxRejected);
                return;
            }

            // ---- Uplink telemetrisi (§6.2 "seq boşluğu = kayıp") ----
            // ⚠️ Yalnız SAYAR. §6.4'ün yasağı burada da geçerli: sıra zorlaması eklenmez, ölçüm
            // kararı hiçbir paketi düşürmez.
            if (state.HasPose)
            {
                // Boşluk = atlanmış seq sayısı. 1 = boşluk yok (beklenen ardıl).
                int gap = (ushort)(pose.seq - state.LastSeq);
                if (gap > 1) state.PoseLost += gap - 1;

                if (state.LastPoseStamp != 0)
                {
                    double intervalMs = StampToMs(stamp - state.LastPoseStamp);
                    // Kayıp paket varış aralığını katları kadar uzatır; jitter'ı kaybın üstüne
                    // yazmamak için beklenen aralık boşlukla ölçeklenir (2 paket kaybı 100 ms'lik
                    // bir aralığı "50 ms jitter" diye raporlamasın).
                    double expectedMs = NominalTickMs * Math.Max(1, gap);
                    long deviationMicros = (long)(Math.Abs(intervalMs - expectedMs) * 1000.0);
                    state.PoseJitterSumMicros += deviationMicros;
                    state.PoseJitterSamples++;
                    if (deviationMicros > state.PoseJitterMaxMicros)
                        state.PoseJitterMaxMicros = deviationMicros;
                }
            }

            state.PoseAccepted++;
            state.LastPoseStamp = stamp;

            state.LastPose = pose;
            state.LastSeq = pose.seq;
            state.HasPose = true;
            state.LastPoseAt = DateTime.UtcNow;

            // §10.9: engel ihlali bayrağı burada AYRI bir alana çıkarılır (LastPose'da da duruyor)
            // çünkü okuyucusu MatchDirector'dır ve o PoseGate'i ALMAZ — 88 B'lik bir struct'ı
            // kilitsiz okumak tearing demektir, tek bool okumak değildir.
            state.InObstacle = (pose.gripFlags & SnapshotEntry.FLAG_IN_OBSTACLE) != 0;
            // Alan-dışı aynı gerekçeyle ayrı alana çıkarılır. ⚠️ Bu bit CEZA ÜRETMEZ (§10.9);
            // yalnız admin görünürlüğünü ve ihlal defterini besler.
            state.OutOfBounds = (pose.gripFlags & SnapshotEntry.FLAG_OUT_OF_BOUNDS) != 0;
        }
    }

    /// <summary>
    /// 0x07 SkeletonUpdate alımı (§6.9): kapı <c>0x01</c> ile birebir aynıdır (yalnız <c>0x00</c> ile
    /// kaydedilmiş endpoint, u16 sarmalamalı eskilik kontrolü, sessiz ret).
    /// <para>⚠️ <b>Blob AÇILMAZ.</b> Sunucu onu bir bayt dizisi olarak saklar ve batch'e kopyalar;
    /// içeriğini yorumlamak sunucuya ikinci bir iskelet doğruluk kaynağı eklemek olurdu (§10.3
    /// felsefesi — hasarı bile istemci hesaplıyor).</para>
    /// <para>⚠️ Eskilik sayacı poz kanalınınkinden <b>ayrıdır</b> (<c>LastSkeletonSeq</c>): iki kanal
    /// farklı kadanslarda akıyor, ortak sayaç birinin paketini diğerinin adına eskitirdi.</para>
    /// </summary>
    private void HandleSkeletonUpdate(byte[] data, IPEndPoint remote)
    {
        SkeletonUpdate msg;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // tip baytı dispatcher'da tüketildi sayılır
            msg = SkeletonUpdate.Read(reader);
        }

        // Boş blob = bozuk/kırpılmış datagram (Read sınırı aşan uzunlukta boş döner).
        if (msg.blobLength == 0)
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        if (!_registry.TryGetByPlayerId(msg.playerId, out var state))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        if (state.UdpEndpoint == null || !state.UdpEndpoint.Equals(remote))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        lock (state.PoseGate)
        {
            if (state.HasSkeleton && (short)(msg.seq - state.LastSkeletonSeq) <= 0)
            {
                Interlocked.Increment(ref _rxRejected);
                return;
            }

            // ⚠️ Referans DEĞİŞTİRİLİR, dizi yerinde yazılmaz: yayın thread'i referansı kilit altında
            // alıp kilit DIŞINDA serileştiriyor (kilit süresini uzatmamak için).
            state.LastSkeleton = msg.blob;
            state.LastSkeletonRoot = msg.root;
            state.LastSkeletonSeq = msg.seq;
            state.HasSkeleton = true;
            state.LastSkeletonStamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>0x03 FireEvent alımı (§6.4): yalnız 0x00 ile kaydedilmiş endpoint'ten kabul edilir,
    /// birebir kopya bastırılır, relay kapısını geçen olay <see cref="_events"/> kuyruğuna girer ve
    /// bir sonraki 20 Hz tik'te 0x04 batch'i olarak yayınlanır.
    /// <para><b>İçerik DOĞRULANMAZ</b> (§10.3 felsefesi): yön, mesafe ve <c>itemId</c> serbesttir —
    /// sunucuda silah tablosu yoktur. Doğrulanan tek şey <b>kimin</b> attığıdır.</para>
    /// <para>Ret sessizdir: 10 atış/sn/oyuncu hızında tek satır log bile konsolu boğar.</para></summary>
    private void HandleFireEvent(byte[] data, IPEndPoint remote)
    {
        FireEvent msg;
        using (var ms = new MemoryStream(data, 0, data.Length, writable: false))
        using (var reader = new BinaryReader(ms))
        {
            reader.ReadByte(); // tip baytı dispatcher'da tüketildi sayılır
            msg = FireEvent.Read(reader);
        }

        if (!_registry.TryGetByPlayerId(msg.entry.playerId, out var state))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        // Kayıtsız/yabancı kaynaktan olay kabul edilmez — poz yolundaki kuralın aynısı (§6.1).
        if (state.UdpEndpoint == null || !state.UdpEndpoint.Equals(remote))
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        // Kopya bastırma (§6.4): UDP paket ÇOĞALTABİLİR ve birebir tekrar çift tracer + çift ses
        // olarak görünür. Alanlara yalnız bu thread dokunduğu için kilit gerekmez.
        // ⚠️ SIRA ZORLAMASI YAPILMAZ: yukarıdaki poz filtresini — (short)(seq - LastSeq) <= 0 —
        // buraya KOPYALAMA. Poz bir DURUMdur (son gelen kazanır, eskisi değersiz), olay bir
        // OLGUdur: sırası bozuk gelen atış gerçekten olmuş bir atıştır ve atmak sessizce bir
        // tracer ile bir sesi silmektir. Yalnız BİREBİR tekrar düşer.
        if (state.HasEventSeq && msg.seq == state.LastEventSeq)
        {
            Interlocked.Increment(ref _rxRejected);
            return;
        }

        // ---- Olay kanalı telemetrisi (§6.4 "seq boşluğu = kayıp") ----
        // ⚠️ Kayıp sayımı SIRA ZORLAMASI DEĞİLDİR: aşağıdaki hiçbir dal paketi düşürmez.
        // ⚠️ Sıra garantisi olmadığı için boşluk hesabına tavan konur: sırası bozuk gelen bir olayda
        // (ushort) farkı ~65535 çıkar ve tavansız bir sayaç bunu "65 bin kayıp" diye raporlar.
        // Tavanı aşan fark "sırasız geldi" demektir, kayıp değil — sayılmaz.
        if (state.HasEventSeq)
        {
            int gap = (ushort)(msg.seq - state.LastEventSeq);
            if (gap > 1 && gap <= EventGapMax) Interlocked.Add(ref state.EventLost, gap - 1);
        }

        Interlocked.Increment(ref state.EventAccepted);

        state.LastEventSeq = msg.seq;
        state.HasEventSeq = true;

        // Relay kapısı (§6.5) — hepsi KİLİTSİZ okunur; bu yol MatchDirector'ın _gate'ine giremez
        // (§10.3: girerse 20 Hz poz alımını maç kilidinin arkasında bekletir). Kalibresizin atışı
        // relay EDİLMEZ (§10.6): ateş edemediği hâlde başkalarının ekranında namlu alevi çakması
        // yanıltıcı olurdu.
        if (!state.IsConnected || state.Role != "player" || !state.Alive || !state.Calibrated) return;
        if (!_matchDirector.ShotRelayOpen) return;

        var entry = msg.entry;
        // playerId'yi SUNUCU yazar: endpoint doğrulaması kimliği zaten bağladı, telden gelen baytı
        // olduğu gibi taşımak başkasının adına olay yayınlamaya açık kapı bırakırdı.
        entry.playerId = (byte)state.PlayerId;
        _events.Enqueue(entry);
    }

    /// <summary>20 Hz snapshot yayını: pozlu ve BAĞLI oyuncular pakete yazılır, UDP kayıtlı
    /// ve bağlı HERKESE (admin dahil — birden çok admin varsa her biri ayrı hedef) aynı
    /// buffer yollanır. Girdi yokken hedef varsa count=0 snapshot gider (istemci uzak avatar
    /// kalmadığını böyle anlar); ikisi de yoksa (ve olay kuyruğu da boşsa) gönderilmez ve
    /// serverTick artmaz.
    /// <para>
    /// <b>Olay batch'i (§6.5):</b> aynı tik'te, snapshot'tan sonra, aynı hedeflere ve aynı
    /// <c>serverTick</c> ile ayrı bir 0x04 datagramı gider — <b>yalnız olay varsa</b>.
    /// </para>
    /// <para>
    /// <b>MTU parçalama (§6.3):</b> girdi sayısı <see cref="ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET"/>'i
    /// aşarsa aynı tik birden çok datagrama bölünür (hepsi aynı serverTick'i taşır, hepsi aynı
    /// hedeflere gider). İstemcide birleştirme YOKTUR ve gerekmez: her paket taşıdığı girdileri
    /// bağımsız uygular, oyuncu düşürme kararı zaman aşımıdır. Bu yüzden tel formatı değişmedi.
    /// </para>
    /// Saniyede bir konsola özet basılır.</summary>
    private async Task SnapshotLoopAsync(UdpClient udp, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / ArenaProtocol.SNAPSHOT_RATE_HZ));
        uint serverTick = 0;
        var summaryDue = DateTime.UtcNow.AddSeconds(1);
        var entries = new List<SnapshotEntry>(ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET);
        var targets = new List<IPEndPoint>();
        var packets = new List<byte[]>(1);
        // Olay tamponu döngü dışında: tik başına yeniden ayırmamak için (20 Hz × oturum boyu).
        var eventBuffer = new List<FireEventEntry>(ArenaProtocol.EVENT_MAX_ENTRIES_PER_PACKET);
        var eventsThisSecond = 0;

        // ---- İskelet kanalı (§6.10) ----
        // Snapshot döngüsünün İÇİNDE ama AYRI kadansta koşar: ayrı bir timer/thread açmak yalnız
        // serverTick'i iki sahibe bölerdi (batch onu taşıyor). Kadans tam sayı bölücüyle kurulur:
        // her tik'te SKELETON_RATE_HZ eklenir, SNAPSHOT_RATE_HZ'ye ulaşınca ateşlenip düşülür —
        // 20 tikte tam 12 kez, kayan noktalı birikim ve sürüklenme olmadan.
        var skeletonEntries = new List<SkeletonEntry>(ArenaProtocol.SKELETON_MAX_ENTRIES_PER_PACKET);
        var skeletonPackets = new List<byte[]>(1);
        var skeletonAccumulator = 0;

        // ---- Tik kayması ölçümü ----
        // ⚠️ Monotonik saat şart: PeriodicTimer gecikmeyi TELAFİ ETMEZ (bir tik'te gönderimler
        // yavaşlarsa sonraki tik kayar ve bu istemcide jitter olur), ama DateTime.UtcNow'un
        // Windows'taki ~15,6 ms çözünürlüğü 50 ms'lik aralığın sapmasını ölçmeye yetmez.
        long lastTickStamp = 0;
        double tickDriftSumMs = 0, tickDriftMaxMs = 0, sendMaxMs = 0;
        var tickDriftSamples = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token)) break;
            }
            catch (OperationCanceledException) { break; }

            var tickStamp = Stopwatch.GetTimestamp();
            if (lastTickStamp != 0)
            {
                var drift = Math.Abs(StampToMs(tickStamp - lastTickStamp) - NominalTickMs);
                tickDriftSumMs += drift;
                tickDriftSamples++;
                if (drift > tickDriftMaxMs) tickDriftMaxMs = drift;
            }

            lastTickStamp = tickStamp;

            skeletonAccumulator += ArenaProtocol.SKELETON_RATE_HZ;
            var skeletonDue = skeletonAccumulator >= ArenaProtocol.SNAPSHOT_RATE_HZ;
            if (skeletonDue) skeletonAccumulator -= ArenaProtocol.SNAPSHOT_RATE_HZ;

            entries.Clear();
            targets.Clear();
            skeletonEntries.Clear();
            var onlinePlayers = 0;
            // ⚠️ Duvar saati tik başına BİR kez okunur, oyuncu başına değil: aynı snapshot'taki
            // girdilerin farklı anlara göre yargılanması için sebep yok ve UtcNow ucuz değil.
            var nowUtc = DateTime.UtcNow;
            foreach (var state in _registry.Snapshot())
            {
                if (!state.IsConnected) continue;
                if (state.UdpEndpoint != null) targets.Add(state.UdpEndpoint);
                if (state.Role != "player") continue;
                onlinePlayers++;
                // flags bit0 = alive (§10.2): MatchDirector kilidi altında yazılır, burada kilitsiz
                // okunur (bool okuması atomik; bir tik gecikme snapshot için önemsiz).
                var alive = state.Alive;
                // flags bit6 = doğma koruması (§10.4): MatchDirector kilidi altında yazılır, burada
                // kilitsiz okunur (bir tik gecikme snapshot için önemsiz). ⚠️ `alive` ile AND'lenir:
                // ölü oyuncunun korumalı görünmesi anlamsız ve kalkanı hayaletin üstüne çizerdi.
                var spawnProtected = alive && nowUtc < state.SpawnProtectedUntil;
                lock (state.PoseGate)
                {
                    // İskelet girdisi (§6.10): poz ile AYNI kilit, AYRI kadans. Poz kapısından ÖNCE
                    // toplanır — aşağıdaki `continue` pozsuz oyuncuyu atlıyor ve iki kanalın
                    // birbirini düşürmesi için bir sebep yok.
                    // ⚠️ Bayat blob yayınlanmaz: susmuş bir gönderenin gövdesini sonsuza kadar
                    // tazelemek, donmuş bir avatarı bant harcayarak canlı göstermek olurdu.
                    if (skeletonDue && state.HasSkeleton && state.LastSkeleton != null
                        && StampToMs(tickStamp - state.LastSkeletonStamp) <= SkeletonStaleMs)
                    {
                        skeletonEntries.Add(new SkeletonEntry
                        {
                            playerId = (byte)state.PlayerId,
                            // Kök arena uzayında taşınır; blob'un kendi kökü kullanılmaz (§6.9).
                            root = state.LastSkeletonRoot,
                            blob = state.LastSkeleton,
                            blobLength = state.LastSkeleton.Length
                        });
                    }

                    if (!state.HasPose) continue;
                    var pose = state.LastPose;
                    entries.Add(new SnapshotEntry
                    {
                        playerId = (byte)state.PlayerId,
                        // Eşya baytları istemci-otoriter sunum bilgisidir: doğrulanmaz, kopyalanır
                        // (§6.2/6.3) — sunucuda eşya tablosu YOKTUR.
                        itemL = pose.itemL,
                        itemR = pose.itemR,
                        // ⚠️ flags TEK BAYT ama İKİ YAZARLI: bit0 ve bit6 sunucunun (otoriter alive
                        // + doğma koruması), bit1-5 ve bit7 istemcinin. GRIP_FLAG_MASK ŞART — maskesiz
                        // kopyalanırsa istemci bit0'ı set ederek kendini canlı ilan eder (ölü
                        // oyuncu kendini diriltir); aynı gerekçeyle bit6 de maskenin dışındadır.
                        flags = (byte)((alive ? SnapshotEntry.FLAG_ALIVE : 0)
                                       | (spawnProtected ? SnapshotEntry.FLAG_SPAWN_PROTECTED : 0)
                                       | (pose.gripFlags & SnapshotEntry.GRIP_FLAG_MASK)),
                        head = pose.head,
                        handL = pose.handL,
                        handR = pose.handR
                    });
                }
            }

            // Boş döngü — gönderme, tik ilerletme. ⚠️ Kuyrukta olay varsa DEVAM edilir: olayın tel
            // kimliği serverTick'tir (§6.5) ve tik ilerlemeden beklemek aynı tik'e ikinci bir batch
            // yazmaya götürür (istemci onu birebir tekrar sanıp düşürürdü). Bu durumda hedef zaten
            // yoktur (kimse UDP kaydı yapmamış) — olaylar aşağıda çekilip düşer; çizecek kimse
            // olmadığı için kuyrukta biriktirmek yalnız bayat bir atış borcu üretirdi.
            if (entries.Count == 0 && targets.Count == 0 && _events.IsEmpty) continue;

            serverTick++;

            // ⚠️ Olaylar snapshot'tan ÖNCE çekilir: birleştirme kararı (§6.8) olay sayısını bilmeyi
            // gerektiriyor. Gönderim sırası değişmedi — snapshot hâlâ önce gider.
            var eventCount = DrainEvents(eventBuffer);

            // §6.8 birleştirme kapısı. Üç koşulun HEPSİ gerekli:
            //   1) olay var — yoksa birleştirilecek bir şey yok, düz 0x02 gider,
            //   2) snapshot tek parçaya sığıyor — parçalanmışsa olay bloğu hangi parçaya girse
            //      "tik başına en fazla bir olay datagramı" değişmezi kırılır (§6.5),
            //   3) toplam boyut COMBINED_MAX_BYTES altında.
            var combinedBytes = SnapshotWithEvents.HEADER_SIZE
                                + entries.Count * SnapshotEntry.SIZE
                                + eventCount * FireEventEntry.SIZE;
            var combine = eventCount > 0
                          && entries.Count <= ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET
                          && combinedBytes <= ArenaProtocol.COMBINED_MAX_BYTES;

            packets.Clear();
            if (combine)
            {
                packets.Add(BuildCombinedPacket(entries, eventBuffer, serverTick));
            }
            else
            {
                BuildPackets(entries, serverTick, packets);
            }

            // Gönderim döngüsünün süresi ayrı ölçülür: tik kaymasının sebebi bu sıralı await zinciri
            // mi, yoksa thread zamanlaması mı — sahada bu ikisi ayrılabilsin.
            var sendStart = Stopwatch.GetTimestamp();

            foreach (var packet in packets)
            {
                foreach (var target in targets)
                {
                    try
                    {
                        await udp.SendAsync(packet, target, token);
                        // ⚠️ Sayaç GERÇEK gönderim başına artar (packets×targets diye hesaplanmaz):
                        // catch'e düşen gönderim gitmemiştir ve onu saymak telemetriyi yalancı yapar.
                        _txSnapshotPackets++;
                        _txSnapshotBytes += packet.Length;
                        if (combine) _txCombinedPackets++;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception)
                    {
                        // Windows'ta ulaşılamayan hedef 10054 vb. fırlatabilir — yayın döngüsü ölmesin.
                    }
                }
            }

            // 0x04 EventBatch — snapshot'tan SONRA, AYNI tik ve AYNI hedeflerle (§6.5).
            // ⚠️ YALNIZ birleştirilemediğinde: aynı tik için hem 0x05 hem 0x04 çıkarsa istemci
            // ikincisini birebir tekrar sanıp düşürür (kimlik serverTick, §6.5).
            if (!combine && eventCount > 0)
            {
                // Atan SÜZÜLMEZ: kendi olayını geri alır ve kendisi yok sayar (snapshot'ta kendi
                // pozunu yok saymasıyla birebir aynı desen). Hedef başına ayrı batch üretmek tik
                // başına N serileştirme demek olurdu; kazancı oyuncu başına ~90 B/sn.
                var eventPacket = BuildEventPacket(eventBuffer, serverTick);
                foreach (var target in targets)
                {
                    try
                    {
                        await udp.SendAsync(eventPacket, target, token);
                        _txEventPackets++;
                        _txEventBytes += eventPacket.Length;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception)
                    {
                        // Snapshot yayınıyla aynı gerekçe — döngü ölmesin.
                    }
                }
                eventsThisSecond += eventCount;
            }
            // Olay yoksa PAKET YOK (§6.5): bu kanal lobide/geri sayımda/sessiz anlarda tümüyle
            // susar. Snapshot'ın count=0 yayınından farklı — orada istemcinin bayat avatarı
            // temizlemesi gerekiyor, burada temizlenecek durum yok (olaylar anlıktır).

            // 0x08 SkeletonBatch (§6.10) — snapshot/olaydan SONRA, aynı serverTick ve aynı hedeflere,
            // ama yalnız kendi kadansında. Girdi yoksa paket yok.
            // ⚠️ Gönderen SÜZÜLMEZ: kendi girdisini geri alır ve kendisi yok sayar (kendi gövdesini
            // sensörden çiziyor). Hedefe özel batch üretmek tik başına N serileştirme demek olurdu —
            // §6.5 olay batch'i de aynı gerekçeyle atanı süzmüyor.
            if (skeletonEntries.Count > 0 && targets.Count > 0)
            {
                BuildSkeletonPackets(skeletonEntries, serverTick, skeletonPackets);
                foreach (var packet in skeletonPackets)
                {
                    foreach (var target in targets)
                    {
                        try
                        {
                            await udp.SendAsync(packet, target, token);
                            _txSkeletonPackets++;
                            _txSkeletonBytes += packet.Length;
                        }
                        catch (OperationCanceledException) { return; }
                        catch (Exception)
                        {
                            // Snapshot yayınıyla aynı gerekçe — döngü ölmesin.
                        }
                    }
                }
            }

            var sendMs = StampToMs(Stopwatch.GetTimestamp() - sendStart);
            if (sendMs > sendMaxMs) sendMaxMs = sendMs;

            var now = DateTime.UtcNow;
            if (now >= summaryDue)
            {
                summaryDue = now.AddSeconds(1);

                var perTickBytes = 0;
                foreach (var packet in packets) perTickBytes += packet.Length;

                PrintSummary(onlinePlayers, entries.Count, targets.Count, perTickBytes, packets.Count,
                    eventsThisSecond, tickDriftSamples > 0 ? tickDriftSumMs / tickDriftSamples : 0,
                    tickDriftMaxMs, sendMaxMs);

                eventsThisSecond = 0;
                tickDriftSumMs = 0;
                tickDriftSamples = 0;
                tickDriftMaxMs = 0;
                sendMaxMs = 0;
            }
        }
    }

    private static double StampToMs(long stampDelta) => stampDelta * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Saniyelik telemetri özeti (Faz 1).
    /// <para><b>Neden iki ayrı boyut yazılıyor:</b> <c>B/tik</c> tek bir datagramın boyutu,
    /// <c>kB/s</c> ise gerçek hacim (hedef sayısıyla çarpılmış). Eski satır yalnız ilkini basıp
    /// "snapshot 886 B" diyordu ve saniyelik hacim sanılıyordu — 10 oyuncuda aradaki fark 220 kattır.
    /// İkisi de gerekli, bu yüzden ikisi de <b>etiketli</b> duruyor.</para>
    /// <para>Oyuncu başına satır yalnız eşik aşılınca basılır (<see cref="JitterWarnMs"/> /
    /// <see cref="LossWarnPct"/>): 10 oyuncu × saniye = okunamaz bir konsol.</para>
    /// </summary>
    private void PrintSummary(int onlinePlayers, int posedPlayers, int targetCount, int perTickBytes,
        int fragments, int eventsThisSecond, double tickDriftAvgMs, double tickDriftMaxMs, double sendMaxMs)
    {
        // Yayın çıkışına yalnız bu thread dokunur → düz okuma+sıfırlama yeter. Ack/echo çıkışı ise
        // recv thread'inden yazılıyor, o yüzden atomik okunur.
        var txPackets = _txSnapshotPackets + _txEventPackets + _txSkeletonPackets
                        + Interlocked.Exchange(ref _txAckPackets, 0);
        var txBytes = _txSnapshotBytes + _txEventBytes + _txSkeletonBytes
                      + Interlocked.Exchange(ref _txAckBytes, 0);
        var txCombined = _txCombinedPackets;
        var txSkeleton = _txSkeletonPackets;
        _txSnapshotPackets = 0; _txSnapshotBytes = 0;
        _txEventPackets = 0; _txEventBytes = 0;
        _txSkeletonPackets = 0; _txSkeletonBytes = 0;
        _txCombinedPackets = 0;

        // Giriş sayaçlarını recv thread'i yazıyor → oku-ve-sıfırla atomik olmalı.
        var rxPackets = Interlocked.Exchange(ref _rxPackets, 0);
        var rxBytes = Interlocked.Exchange(ref _rxBytes, 0);
        var rxRejected = Interlocked.Exchange(ref _rxRejected, 0);

        int poseAccepted = 0, poseLost = 0;
        long eventAccepted = 0, eventLost = 0;

        foreach (var state in _registry.Snapshot())
        {
            if (state.Role != "player") continue;

            int accepted, lost, jitterSamples;
            long jitterSum, jitterMax;
            lock (state.PoseGate)
            {
                accepted = state.PoseAccepted;
                lost = state.PoseLost;
                jitterSum = state.PoseJitterSumMicros;
                jitterSamples = state.PoseJitterSamples;
                jitterMax = state.PoseJitterMaxMicros;
                state.PoseAccepted = 0;
                state.PoseLost = 0;
                state.PoseJitterSumMicros = 0;
                state.PoseJitterSamples = 0;
                state.PoseJitterMaxMicros = 0;
            }

            var evAccepted = Interlocked.Exchange(ref state.EventAccepted, 0);
            var evLost = Interlocked.Exchange(ref state.EventLost, 0);

            poseAccepted += accepted;
            poseLost += lost;
            eventAccepted += evAccepted;
            eventLost += evLost;

            var jitterAvgMs = jitterSamples > 0 ? jitterSum / 1000.0 / jitterSamples : 0;
            var playerLossPct = accepted + lost > 0 ? 100.0 * lost / (accepted + lost) : 0;
            if (jitterAvgMs >= JitterWarnMs || playerLossPct >= LossWarnPct)
            {
                Console.WriteLine($"[net] {state.Name} #{state.PlayerId}: jitter ort {jitterAvgMs:0.0} ms " +
                                  $"maks {jitterMax / 1000.0:0.0} ms · poz kaybı %{playerLossPct:0.0}" +
                                  (evLost > 0 ? $" · olay kaybı {evLost}" : ""));
            }
        }

        var posePct = poseAccepted + poseLost > 0 ? 100.0 * poseLost / (poseAccepted + poseLost) : 0;
        var eventPct = eventAccepted + eventLost > 0 ? 100.0 * eventLost / (eventAccepted + eventLost) : 0;
        var fragmentNote = fragments > 1 ? $" ×{fragments} parça" : "";
        var rejectNote = rxRejected > 0 ? $" (red {rxRejected})" : "";

        Console.WriteLine(
            $"[state] oyuncu {onlinePlayers} · pozlu {posedPlayers} · hedef {targetCount}" +
            $" | paket {perTickBytes} B/tik{fragmentNote}" +
            $" | çıkış {txBytes / 1024.0:0} kB/s {txPackets} p/s" +
            $" | giriş {rxBytes / 1024.0:0} kB/s {rxPackets} p/s{rejectNote}" +
            $" | tik sapma ort {tickDriftAvgMs:0.0} maks {tickDriftMaxMs:0.0} ms (gönderim maks {sendMaxMs:0.0} ms)" +
            $" | olay {eventsThisSecond}" +
            (txCombined > 0 ? $" (birleşik {txCombined})" : "") +
            (txSkeleton > 0 ? $" | iskelet {txSkeleton} p/s" : "") +
            $" | kayıp poz %{posePct:0.0} olay %{eventPct:0.0}");
    }

    /// <summary>Kuyruktan bu tik'in batch'ine girecek olayları çeker.
    /// <para>⚠️ Sınırı (<see cref="ArenaProtocol.EVENT_MAX_ENTRIES_PER_PACKET"/>) aşan olay
    /// <b>ATILMAZ, kuyrukta kalır ve sonraki tik'e kayar</b>: "tik başına en fazla BİR batch"
    /// değişmezi istemcinin kopya korumasının dayanağıdır (batch kimliği <c>serverTick</c>, §6.5).
    /// Aynı tik için ikinci bir datagram üretilirse istemci onu birebir tekrar sanıp düşürür —
    /// yani taşmayı "ikinci paket" ile çözmek olayları gerçekten kaybettirir.</para></summary>
    private int DrainEvents(List<FireEventEntry> output)
    {
        output.Clear();
        while (output.Count < ArenaProtocol.EVENT_MAX_ENTRIES_PER_PACKET
               && _events.TryDequeue(out var entry))
        {
            output.Add(entry);
        }
        return output.Count;
    }

    /// <summary>Tek 0x05 datagramı: snapshot + olaylar birlikte (§6.8). Çağıran, birleştirme
    /// kapısının üç koşulunu zaten doğruladı.</summary>
    private static byte[] BuildCombinedPacket(List<SnapshotEntry> entries,
        List<FireEventEntry> events, uint serverTick)
    {
        var combined = new SnapshotWithEvents
        {
            serverTick = serverTick,
            players = entries.ToArray(),
            events = events.ToArray()
        };
        using var ms = new MemoryStream(SnapshotWithEvents.HEADER_SIZE
                                        + entries.Count * SnapshotEntry.SIZE
                                        + events.Count * FireEventEntry.SIZE);
        using var writer = new BinaryWriter(ms);
        combined.Write(writer);
        return ms.ToArray();
    }

    /// <summary>
    /// İskelet girdilerini datagramlara böler (§6.10). Snapshot parçalamasından (<see cref="BuildPackets"/>)
    /// tek farkı, girdilerin <b>değişken uzunluklu</b> olmasıdır: bölme kararı girdi sayısına DEĞİL
    /// <b>bayt bütçesine</b> bakar (<see cref="ArenaProtocol.COMBINED_MAX_BYTES"/>), girdi tavanı ise
    /// yalnız <c>count</c> alanının <c>u8</c> olmasının sınırıdır.
    /// <para>⚠️ Snapshot'tan bir fark daha: <b>girdi yoksa boş paket üretilmez</b>. Boş snapshot bir
    /// bildirimdir (istemci bayat avatarı onunla temizler); boş iskelet batch'inin karşılığı yoktur —
    /// avatarın yaşam süresi snapshot'tan gelir (§6.3), gövde yalnız son karesinde donar.</para>
    /// <para>⚠️ Tek bir girdi bütçeyi tek başına aşarsa yine <b>kendi paketinde</b> gönderilir:
    /// gönderim tarafı <see cref="ArenaProtocol.SKELETON_MAX_BLOB_BYTES"/>'ı zaten uyguluyor, yani bu
    /// durum ancak sınır büyütülürse oluşur ve sessizce girdi düşürmek gövdeyi kalıcı kaybettirirdi.</para>
    /// </summary>
    private static void BuildSkeletonPackets(List<SkeletonEntry> entries, uint serverTick, List<byte[]> output)
    {
        output.Clear();

        var offset = 0;
        while (offset < entries.Count)
        {
            var bytes = SkeletonBatch.HEADER_SIZE;
            var count = 0;

            while (offset + count < entries.Count
                   && count < ArenaProtocol.SKELETON_MAX_ENTRIES_PER_PACKET)
            {
                var size = entries[offset + count].Size;
                // İlk girdi koşulsuz alınır: aksi hâlde bütçeyi aşan tek bir blob sonsuz döngü
                // (hiç girdi almayan paket) üretirdi.
                if (count > 0 && bytes + size > ArenaProtocol.COMBINED_MAX_BYTES) break;
                bytes += size;
                count++;
            }

            var chunk = new SkeletonEntry[count];
            entries.CopyTo(offset, chunk, 0, count);

            using var ms = new MemoryStream(bytes);
            using var writer = new BinaryWriter(ms);
            SkeletonBatch.Write(writer, serverTick, chunk, 0, count);
            output.Add(ms.ToArray());

            offset += count;
        }
    }

    /// <summary>Tek 0x04 datagramı: 6 + count×9 B (§6.5).</summary>
    private static byte[] BuildEventPacket(List<FireEventEntry> events, uint serverTick)
    {
        var batch = new EventBatch { serverTick = serverTick, events = events.ToArray() };
        using var ms = new MemoryStream(6 + events.Count * FireEventEntry.SIZE);
        using var writer = new BinaryWriter(ms);
        batch.Write(writer);
        return ms.ToArray();
    }

    /// <summary>
    /// Girdileri MTU'ya sığan datagramlara böler (§6.3). Girdi yoksa tek bir count=0 paketi
    /// üretir — istemciler bayat avatarı bununla da temizleyebilsin. Tüm parçalar aynı
    /// <paramref name="serverTick"/>'i taşır.
    /// </summary>
    private static void BuildPackets(List<SnapshotEntry> entries, uint serverTick, List<byte[]> output)
    {
        output.Clear();
        var perPacket = ArenaProtocol.SNAPSHOT_MAX_ENTRIES_PER_PACKET;

        for (var offset = 0; offset == 0 || offset < entries.Count; offset += perPacket)
        {
            var count = Math.Min(perPacket, entries.Count - offset);
            var chunk = new SnapshotEntry[count];
            entries.CopyTo(offset, chunk, 0, count);

            var snapshot = new Snapshot { serverTick = serverTick, players = chunk };
            using var ms = new MemoryStream(6 + count * SnapshotEntry.SIZE);
            using var writer = new BinaryWriter(ms);
            snapshot.Write(writer);
            output.Add(ms.ToArray());
        }
    }
}
