#nullable enable
using System.Net;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Bağlanmış (veya daha önce bağlanmış) tek bir cihazın sunucu tarafı görünümü.</summary>
public sealed class PlayerState
{
    public string DeviceId { get; init; } = "";

    /// <summary>Sunucunun welcome'da atadığı 1..PLAYER_ID_MAX kimliği (UDP paketlerinde 1 bayt).</summary>
    public int PlayerId { get; init; }

    public string Name { get; set; } = "";

    /// <summary>Forma numarası 1..99 (§2); 0 = atanmamış. Admin'de daima 0 (admin oynamaz).
    /// <b>Tüm kayıtlı cihazlar arasında benzersizdir</b> — yalnız çevrimiçiler arasında değil.</summary>
    public int Number { get; set; }

    /// <summary>"player" (VR/Quest) veya "admin" (Windows masaüstü).</summary>
    public string Role { get; set; } = "player";

    /// <summary>"red" | "blue"; admin oynamadığı için admin'de boş kalır.</summary>
    public string Team { get; set; } = "";

    public bool Ready { get; set; }
    public bool Online { get; set; }

    /// <summary>0–1 aralığı; -1 = bilinmiyor.</summary>
    public float Battery { get; set; } = -1f;

    public float Fps { get; set; }
    public string Scene { get; set; } = "";

    // ---- İstemcinin ölçüp status ile bildirdiği ağ telemetrisi (§6.7) ----
    // ⚠️ Bunlar ToPlayerInfo()'ya EKLENMEZ. Sürekli değişen sayılar oldukları için
    // PlayerRegistry.UpdateStatus'taki "görünen bir alan gerçekten değişti mi" kapısını her
    // status'ta açar ve çözülmüş bir hatayı geri getirirler (her status = bir tam roster yayını).
    // Fps tam bu sebeple PlayerInfo'da taşınmıyor; izlenecek emsal odur. Adminlere net_stats gider.

    /// <summary>İstemcinin ölçtüğü RTT (ms); -1 = bilinmiyor (eski sürüm ya da henüz yoklama yok).</summary>
    public int RttMs { get; set; } = -1;

    /// <summary>İstemcinin ölçtüğü downlink snapshot jitter'ı (ms); -1 = bilinmiyor.</summary>
    public float JitterMs { get; set; } = -1f;

    /// <summary>İstemcinin ölçtüğü downlink snapshot kaybı (%); -1 = bilinmiyor.</summary>
    public float LossPct { get; set; } = -1f;

    /// <summary>hello'da bildirilen build sahne listesi (admin katalog doğrulaması için).</summary>
    public List<string> Scenes { get; set; } = new();

    /// <summary>Son hello/status'un UTC zamanı (OFFLINE_TIMEOUT süpürmesi buna bakar).</summary>
    public DateTime LastSeen { get; set; }

    // ---- Maç durumu (§10.2) ----
    // Bu alanların TEK yazarı MatchDirector'dır ve hepsi onun _gate kilidi altında okunur/yazılır.
    // (StateHost yalnız Alive'ı kilitsiz okur — bool okuması atomik, bir tik gecikme önemsiz.)

    /// <summary>0..PLAYER_MAX_HP; Live'a girerken ve canlanmada tam cana çekilir.</summary>
    public float Hp { get; set; } = ArenaProtocol.PLAYER_MAX_HP;

    /// <summary>Snapshot flags bit0 (FLAG_ALIVE) bunu besler; Lobby fazında herkes canlıdır.</summary>
    public bool Alive { get; set; } = true;

    public int Kills { get; set; }
    public int Deaths { get; set; }

    /// <summary>Bireysel maç skoru (§10.2). Kills ile aynı şey DEĞİLDİR: yazarı IGameMode'dur
    /// (MatchDirector'ın skor defteri üzerinden, _gate altında) ve anlamı mod başına değişir.</summary>
    public int Score { get; set; }

    /// <summary>Son ölümün UTC zamanı (RESPAWN_DELAY + REVIVE_GRACE hesabı, §10.4).</summary>
    public DateTime DiedAt { get; set; }

    // ---- Kalibrasyon durumu (§10.6) ----
    // MAÇ DURUMU DEĞİL cihaz durumudur: yazarı MatchDirector değil PlayerRegistry'dir
    // (SetCalibration, registry'nin kendi _gate'i altında) ve maç sıfırlamalarında KORUNUR.
    // MatchDirector bunları kendi _gate'i altında yalnız OKUR — Team ile birebir aynı desen;
    // bool okuması atomik olduğu için iki kilidi birbirine bağlamaya gerek yoktur (bağlamak
    // kilitlenme riski üretir, kazancı sıfırdır).

    /// <summary>Başlığın arena ile hizalı olduğunu bildirip bildirmediği. hello'da false'a
    /// çekilir (§10.6): sunucu yeniden bağlanan bir başlığın hizalamasını bilemez.
    /// false iken oyuncu ateş edemez, hasar yemez ve canlanamaz.</summary>
    public bool Calibrated { get; set; }

    /// <summary>"manual" | "anchor" | "cloud" | "" — doğrulanmayan serbest etiket (§5.1).</summary>
    public string CalibrationSource { get; set; } = "";

    /// <summary>welcome'da verilen UDP kayıt jetonu; her yeni hello'da yenilenir.</summary>
    public uint UdpToken { get; set; }

    /// <summary>0x00 UdpHello ile doğrulanmış UDP endpoint'i; kayıt öncesi null.</summary>
    public IPEndPoint? UdpEndpoint { get; set; }

    /// <summary>Poz yaz/oku kilidi — UDP recv thread'i ile snapshot timer'ı farklı thread'ler;
    /// ~90 B'lik PoseUpdate struct'ında tearing olmasın.</summary>
    public object PoseGate { get; } = new();

    /// <summary>En az bir geçerli PoseUpdate alındı mı (LastPose ancak o zaman okunur).</summary>
    public bool HasPose { get; set; }

    /// <summary>Son kabul edilen poz (arena uzayında; PoseGate altında oku/yaz).</summary>
    public PoseUpdate LastPose { get; set; }

    /// <summary>Son kabul edilen pozun sıra numarası (u16 sarmalamalı eskilik kontrolü için).</summary>
    public ushort LastSeq { get; set; }

    /// <summary>Son kabul edilen pozun UTC zamanı. <b>Canlılık</b> ölçütüdür (OFFLINE/bayatlık);
    /// jitter için KULLANILMAZ — <c>DateTime.UtcNow</c>'un Windows'taki varsayılan çözünürlüğü
    /// ~15,6 ms olduğu için 50 ms'lik bir aralığın sapmasını ölçmeye yetmez. Jitter
    /// <see cref="LastPoseStamp"/> (monotonik) üzerinden hesaplanır.</summary>
    public DateTime LastPoseAt { get; set; }

    // ---- İskelet kanalı (0x07, §6.9) ----
    // ⚠️ PoseGate ALTINDA okunur/yazılır — poz ile aynı kilit, çünkü ikisi de aynı iki thread
    // arasında paylaşılıyor (recv yazar, 20 Hz yayın okur) ve ikinci bir kilit yalnız kilitlenme
    // sırası sorusu üretirdi. Yeni kilit açılmaz.

    /// <summary>
    /// Son kabul edilen iskelet blob'u — <b>OPAK</b>. Sunucu içeriğini açmaz, doğrulamaz, yalnız
    /// batch'e kopyalar (§6.9); sunucuda iskelet tablosu YOKTUR ve eklenmez.
    /// <para>⚠️ Bu dizi <b>yerinde DEĞİŞTİRİLMEZ</b>, her pakette yenisiyle değiştirilir: yayın
    /// thread'i referansı kilit altında alıp kilit dışında serileştiriyor. Yerinde yazmak yarı
    /// güncellenmiş bir blob yayınlamak olurdu.</para>
    /// </summary>
    public byte[]? LastSkeleton { get; set; }

    /// <summary>Karakter kökünün arena uzayı pozu (§6.9) — blob'un kendi kökü kullanılmaz.</summary>
    public PoseData LastSkeletonRoot { get; set; }

    /// <summary>İskelet kanalının sıra numarası. ⚠️ <see cref="LastSeq"/>'ten AYRIDIR: iki kanal
    /// farklı kadanslarda akıyor ve ortak bir sayaç birinin paketini diğerinin adına eskitir.</summary>
    public ushort LastSkeletonSeq { get; set; }

    /// <summary>En az bir geçerli iskelet alındı mı (<see cref="LastSkeletonSeq"/> ancak o zaman
    /// anlamlı — yoksa <c>seq=0</c> ile gelen ilk paket eski sanılıp düşerdi).</summary>
    public bool HasSkeleton { get; set; }

    /// <summary>Son iskeletin monotonik damgası (<c>Stopwatch.GetTimestamp()</c>) — bayatlamış
    /// gövdenin yayından düşürülmesi için. 0 = henüz iskelet yok.</summary>
    public long LastSkeletonStamp { get; set; }

    // ---- Uplink telemetrisi (istemci → sunucu) ----
    // ⚠️ Bu alanların TAMAMI PoseGate altında yazılır ve okunur — yani telemetri için YENİ KİLİT
    // AÇILMAZ. Gerekçe StateHost'un thread sözleşmesi: recv thread'i maç kilidine giremez ve 20 Hz
    // poz alım yolu bir teşhis sayacı için bekletilemez. Poz yolu bu kilidi zaten alıyor.

    /// <summary>Son kabul edilen pozun monotonik damgası (<c>Stopwatch.GetTimestamp()</c>);
    /// 0 = henüz poz yok. Jitter iki ardışık damganın farkından çıkar.</summary>
    public long LastPoseStamp { get; set; }

    /// <summary>Bu özet penceresinde kabul edilen poz sayısı (kayıp yüzdesinin paydası).</summary>
    public int PoseAccepted { get; set; }

    /// <summary><c>seq</c> boşluğundan sayılan kayıp poz (§6.2). Kayıp = boşluk − 1.</summary>
    public int PoseLost { get; set; }

    /// <summary>Varış aralığının nominalden (50 ms) sapmalarının toplamı, mikrosaniye.</summary>
    public long PoseJitterSumMicros { get; set; }

    /// <summary>Sapma örnek sayısı (ortalama için) ve gördüğü en büyük sapma, mikrosaniye.</summary>
    public int PoseJitterSamples { get; set; }
    public long PoseJitterMaxMicros { get; set; }

    // ---- Atış olayı kanalı (0x03, §6.4) ----
    // ⚠️ Bu iki alana YALNIZ UDP recv thread'i dokunur (tek yazar + tek okuyucu aynı thread) →
    // kilit gerekmez; PoseGate'e de girilmez (poz ile ilgisi yoktur).
    // ⚠️ LastSeq'ten AYRIDIR ve öyle kalmalı: LastSeq POZ kanalınındır ve SIRA ZORLAR (durum:
    // son gelen kazanır); bu alan OLAY kanalınındır ve yalnız BİREBİR KOPYAYI bastırır.

    /// <summary>Son işlenen atış olayının <c>seq</c>'i — kopya bastırma için (§6.4): UDP paket
    /// çoğaltabilir, aynı <c>seq</c> ikinci kez gelirse relay edilmez (çift tracer + çift ses).</summary>
    public ushort LastEventSeq { get; set; }

    /// <summary>En az bir atış olayı işlendi mi (<see cref="LastEventSeq"/> ancak o zaman anlamlı —
    /// yoksa <c>seq=0</c> ile gelen ilk olay kopya sanılıp düşerdi).</summary>
    public bool HasEventSeq { get; set; }

    /// <summary>Olay kanalı telemetrisi: bu pencerede alınan ve <c>seq</c> boşluğundan kayıp
    /// sayılan olay adedi.
    /// <para>⚠️ Poz sayaçlarının aksine bunlar <b>PoseGate altında DEĞİL</b> — olay yolu o kilidi
    /// hiç almıyor ve teşhis için almaya başlamak 20 Hz poz alımını olay trafiğine bağlamak olurdu.
    /// Bu yüzden yazma/okuma <c>Interlocked</c> ile yapılır (recv thread'i yazar, 1 Hz özet okur).
    /// Üç sayacın grup hâlinde atomik olmaması telemetri için önemsizdir.</para></summary>
    public long EventAccepted;
    public long EventLost;

    public ClientConnection? Connection { get; set; }

    /// <summary>lobby_state için tel formatı anlık görüntüsü.</summary>
    public PlayerInfo ToPlayerInfo() => new()
    {
        playerId = PlayerId,
        name = Name,
        number = Number,
        role = Role,
        team = Team,
        ready = Ready,
        online = Online,
        battery = Battery,
        scene = Scene,
        // §10.2 sayaçları: admin istatistik tablosu bunları okur (§5.3 lobby_state).
        kills = Kills,
        deaths = Deaths,
        hp = Hp,
        alive = Alive,
        score = Score,
        // §10.6 — admin gözlemci arayüzündeki kalibrasyon tik'i bunu okur.
        calibrated = Calibrated,
        calibrationSource = CalibrationSource
    };
}
