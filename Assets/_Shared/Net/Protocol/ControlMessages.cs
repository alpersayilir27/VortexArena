using System;

namespace VortexArena.Protocol
{
    // Tüm WS kontrol DTO'ları (Docs/ArenaNet-Protokol.md §5) + UDP beacon DTO'su (§4).
    // Kurallar: [Serializable], yalnız public alan, Dictionary/polimorfizm yok,
    // alan adları protokol dokümanındaki camelCase ile birebir.

    /// Zarf: alıcı önce yalnız type'ı parse eder, sonra tam DTO'ya geçer.
    [Serializable]
    public class MsgEnvelope
    {
        public string type;
    }

    // ---- İstemci → Sunucu ----

    [Serializable]
    public class HelloMsg
    {
        public string type = MessageTypes.Hello;
        public int protocolVersion;
        public string role;
        public string deviceId;
        public string deviceName;
        public string appVersion;
        public string currentScene;
        public string[] scenes;
    }

    [Serializable]
    public class StatusMsg
    {
        public string type = MessageTypes.Status;
        public string scene;
        public float battery;

        /// <summary>Sol/sağ kumandanın durumu: <c>ArenaProtocol.CONTROLLER_*</c> (§5.1).
        /// <c>0</c> = bildirilmedi (atanmamış <c>int</c> "sağlıklı" sayılmasın diye).
        /// <para>⚠️ <b>Bu bir pil YÜZDESİ DEĞİL, durumdur ve yüzde olamaz</b> — kumanda şarjı
        /// Quest'te OpenXR altında okunamıyor. <see cref="battery"/> <b>gözlüğün</b> pilidir.</para>
        /// <para>⚠️ Bu iki alan telemetri sayılarının aksine roster yayını TETİKLER (kesikli durum),
        /// bu yüzden <see cref="PlayerInfo"/>'da da taşınır — aşağıdaki "PlayerInfo'ya KONMAZ"
        /// notu <see cref="fps"/> ve ağ telemetrisi içindir.</para></summary>
        public int ctrlL;

        /// <inheritdoc cref="ctrlL"/>
        public int ctrlR;

        public float fps;

        /// <summary>İstemcinin UYGULADIĞI son <see cref="LobbyStateMsg.version"/> (§5.1). Sunucu
        /// geride kalmış istemciye — yalnız ona — tam bir lobby_state yollar; güncelse hiçbir şey
        /// yapmaz. Yedek uzlaştırma ağıdır, birincil yol değil: kontrol kanalı TCP olduğu için
        /// lobby_state kaybolmaz; bu alan istemcinin bir yayını uygulayamadığı pencereleri
        /// (sahne geçişi, kopma anı) kapatır. 0 = hiç uygulanmadı → sunucu tam snapshot yollar.</summary>
        public int rosterVersion;

        // ---- Ağ telemetrisi: İSTEMCİ ölçer, sunucu yalnız taşır ----
        // Ölçüm istemcide çünkü ikisi de bedavaya oradan çıkıyor: RTT 0x06 echo'sundan (iki damga da
        // istemcinin → saat senkronu gerekmez), downlink jitter/kaybı ise zaten gelen 20 Hz snapshot
        // akışından (EK PAKET YOK). Raporlama da bedava: status hâlihazırda 5 sn'de bir gidiyor ve
        // operatör göstergesi için bu fazlasıyla yeter.
        //
        // ⚠️ Bu üç alan PlayerInfo'ya (lobby_state roster'ına) KONMAZ. Sürekli değişen sayılar
        // oldukları için PlayerRegistry.UpdateStatus'taki "görünen bir alan gerçekten değişti mi"
        // kapısını her seferinde açarlar ve çözülmüş bir hatayı geri getirirler (her status bir tam
        // roster yayını → saniyede onlarca roster JSON'u). Fps tam bu sebeple PlayerInfo'da
        // taşınmıyor; izlenecek emsal odur. Adminlere ayrı, kaybı zararsız bir kanal gider: net_stats.

        /// <summary>Ölçülen gidiş-dönüş süresi (ms), <b>UDP state kanalı</b> üzerinden;
        /// <b>-1 = bilinmiyor</b> (henüz yoklama dönmedi ya da istemci eski sürüm).
        /// <para>0 değil -1: 0 ms gerçekten mümkün bir ölçüm gibi okunur, "bilinmiyor" ile
        /// karışırdı.</para></summary>
        public int rttMs = -1;

        /// <summary>Snapshot varış aralığının nominalden (50 ms) ortalama sapması (ms);
        /// -1 = bilinmiyor.</summary>
        public float jitterMs = -1f;

        /// <summary>Downlink snapshot kaybı yüzdesi (<c>serverTick</c> boşluğundan);
        /// -1 = bilinmiyor.</summary>
        public float lossPct = -1f;
    }

    /// Oyuncunun adı ve/veya forma numarası (§5.1). Boş string / 0 bırakılan alan MEVCUT değeri
    /// korur (set_selection ile aynı konvansiyon) → "yalnız numarayı değiştir" tek mesajdır.
    /// Yetki set_team ile aynı: oyuncu yalnız KENDİ playerId'si için, admin herkes için.
    [Serializable]
    public class SetIdentityMsg
    {
        public string type = MessageTypes.SetIdentity;
        public int playerId;
        public string name;
        public int number;
    }

    [Serializable]
    public class SetReadyMsg
    {
        public string type = MessageTypes.SetReady;
        public bool ready;
    }

    // ShotFiredMsg v4'te KALDIRILDI → UDP 0x03/0x04 (§6.4/6.5); 10 atış/sn/oyuncu otoriter WS
    // kanalını boğuyordu.

    [Serializable]
    public class HitReportMsg
    {
        public string type = MessageTypes.HitReport;
        public int seq;
        public int targetPlayerId;
        public string weaponId;
        public float damage;
        public float[] hitPos;
    }

    /// Ölü oyuncunun canlanma talebi (§10.4): respawn.delaySeconds dolmuş VE oyuncu kendi
    /// tabanındayken gönderilir; sunucu koşulları doğrulayıp canlandırır. Alan taşımaz.
    [Serializable]
    public class ReviveRequestMsg
    {
        public string type = MessageTypes.ReviveRequest;
    }

    /// Başlığın KENDİ hizalama durumunu bildirmesi (§10.6). Yalnız player gönderir.
    /// <para>
    /// <c>source</c> ∈ "manual" (kumandada elle: A basılıyken B'ye çift basış) | "anchor" (kayıtlı OVRSpatialAnchor'dan geri
    /// yükleme) | "cloud" (ileride: paylaşılan uzamsal anchor). Sunucu DOĞRULAMAZ, yalnız
    /// kaydedip roster'da yayar — weaponId gibi serbest etiket, yeni kaynak sunucuda iş çıkarmaz.
    /// Bu yüzden bilinçli olarak <b>string</b>, enum değil.
    /// </para>
    [Serializable]
    public class SetCalibrationMsg
    {
        public string type = MessageTypes.SetCalibration;
        public bool calibrated;
        public string source = "";

        /// <summary>Elle kalibrasyonda kumanda ucunun yakalama anındaki <b>tracking-yerel</b>
        /// yüksekliği (metre, işaretli) — sistemin zemin tahmininin gerçek zeminden sapması.
        /// Kayıtlı çapadan geri yüklemede <c>0</c> (ölçüm yok).
        /// <para>Sunucu yorumlamaz: roster'a yazar ve
        /// <see cref="ArenaProtocol.CALIB_FLOOR_WARN_METERS"/> eşiğini aşarsa operatörü uyarır
        /// (§10.6). ⚠️ <b>Bir kapı değildir</b> — sapma ne olursa olsun kalibrasyon kabul edilir.</para></summary>
        public float floorOffset;
    }

    /// Başlığın KENDİ gövde ölçeğini bildirmesi (§10.8). Yalnız player gönderir; <c>playerId</c>
    /// taşımaz, bağlantıdan çözülür (<see cref="SetCalibrationMsg"/> ile aynı sözleşme).
    /// <para>
    /// Ölçümü İSTEMCİ yapar (oyuncunun gözü ile karakterin göz hizasının oranı); sunucu
    /// yorumlamaz, yalnız <c>[BODY_SCALE_MIN, BODY_SCALE_MAX]</c> aralığına kırpıp roster'da yayar.
    /// </para>
    [Serializable]
    public class SetBodyScaleMsg
    {
        public string type = MessageTypes.SetBodyScale;
        public float scale;

        /// <summary>Ölçüm başarısızsa insan okuyabilir gerekçesi; boş = başarılı ölçüm.
        /// <b>Doluysa <see cref="scale"/> YOK SAYILIR ve kayıtlı ölçek değişmez</b> — gerekçe yalnız
        /// adminlere duyurulur ve roster'a yazılır (<see cref="PlayerInfo.scaleError"/>, §10.8).
        /// <para>⚠️ Doğrulanmayan serbest metindir (<c>calibrationSource</c> ile aynı sözleşme):
        /// hata kodu listesi YOKTUR — tek tüketicisi operatörün ekranıdır, yeni bir başarısızlık
        /// türü sunucuda iş çıkarmamalıdır.</para></summary>
        public string error = "";
    }

    // ---- Yalnız admin → Sunucu ----

    /// start_match (§5.2). roundSeconds/scoreLimit/countdownSeconds O MAÇA özeldir: ≤0 ya da
    /// eksikse modun varsayılanı (IGameMode.DefaultRoundSeconds/DefaultScoreLimit) — geri sayımda
    /// protokol varsayılanı (COUNTDOWN_SECONDS) — kullanılır.
    [Serializable]
    public class StartMatchMsg
    {
        public string type = MessageTypes.StartMatch;
        public string modeId;
        public string sceneName;
        public int roundSeconds;
        public int scoreLimit;

        /// <summary>Geri sayımın uzunluğu (sn); sunucuda [COUNTDOWN_SECONDS_MIN,
        /// COUNTDOWN_SECONDS_MAX] aralığına kırpılır. <c>0</c> = protokol varsayılanı.
        /// <para>Maç boyunca HER geri sayımda kullanılır — tur tabanlı modlarda (<c>tournament</c>)
        /// turlar arasındaki geri sayım da budur.</para></summary>
        public int countdownSeconds;
    }

    [Serializable]
    public class AbortMatchMsg
    {
        public string type = MessageTypes.AbortMatch;
    }

    /// <summary>Koşan maçı dondurur: <c>playing</c> → <c>paused</c>/<c>operator</c> (§5.2).
    /// Yalnız <c>playing</c> iken iş yapar.</summary>
    [Serializable]
    public class PauseMatchMsg
    {
        public string type = MessageTypes.PauseMatch;
    }

    /// <summary>Operatörün duraklattığı maçı sürdürür (§5.2). Yalnız
    /// <c>phaseReason == "operator"</c> iken kabul edilir — her duraklamayı kendi sahibi kaldırır.</summary>
    [Serializable]
    public class ResumeMatchMsg
    {
        public string type = MessageTypes.ResumeMatch;
    }

    [Serializable]
    public class SetTeamMsg
    {
        public string type = MessageTypes.SetTeam;
        public int playerId;
        public string team;
    }

    [Serializable]
    public class KickMsg
    {
        public string type = MessageTypes.Kick;
        public int playerId;
    }

    /// Admin bir oyuncunun kalibrasyonunu SIFIRLAR (§10.6). Admin yalnız sıfırlayabilir,
    /// "kalibre oldu" diye işaretleyemez — onu yalnız başlık bilir (SetCalibrationMsg).
    /// <para>Admin → sunucu yönünde <c>playerId</c> dolu (<c>0</c> = TÜM oyuncular), sunucu →
    /// istemci yönünde alansız gider — <see cref="IdentifyMsg"/> ile aynı çift yönlü desen.</para>
    /// <para>⚠️ Sunucu komutu hedefe <b>koşulsuz</b> iletir: sıfırlanacak şeylerin hepsi roster'da
    /// görünmez — yarım kalmış elle kalibrasyon (A alındı, B alınmadı) yalnız başlıkta yaşar ve
    /// orada <c>calibrated</c> zaten <c>false</c>'tur (§10.6).</para>
    [Serializable]
    public class ClearCalibrationMsg
    {
        public string type = MessageTypes.ClearCalibration;
        public int playerId;
    }

    /// Gövde ölçümünü başlatır (§10.8). Admin → sunucu yönünde <c>playerId</c> dolu
    /// (<c>0</c> = TÜM oyuncular), sunucu → istemci yönünde alansız gider — <see cref="IdentifyMsg"/>
    /// ile birebir aynı çift yönlü desen.
    /// <para>Sunucu hiçbir şey hesaplamaz, yalnız iletir; ⚠️ <b>kalibresiz oyuncuya iletmez</b>
    /// (ölçü arena zeminine göredir).</para>
    [Serializable]
    public class MeasureBodyScaleMsg
    {
        public string type = MessageTypes.MeasureBodyScale;
        public int playerId;
    }

    /// Operatör ölü bir oyuncuyu elle canlandırır (§10.4). <c>playerId</c> <c>0</c> = o an ölü olan
    /// TÜM oyuncular — <see cref="ClearCalibrationMsg"/> ile aynı toplu-hedef deseni.
    /// <para>Komut <c>revive_request</c>'in yasaklarının yalnız bir kısmını taşır: modun canlanma
    /// şartı (<c>reviveAnchor:"none"</c>, §10.5) ve canlanma gecikmesi <b>GEÇİLİR</b> — düğmenin
    /// varlık sebebi şartı sağlayamayan oyuncuyu kurtarmaktır ve bekletmek operatörün işi değildir.
    /// ⚠️ Turnuvada tur sonucunu değiştirir; bu operatörün bilinçli kararıdır.</para>
    /// <para>Kalibrasyon (§10.6) ve engelin içinde olma (§10.9) kapıları ise <b>UYGULANIR</b>:
    /// kalibresiz oyuncu ateş edemez ve vurulamaz (canlandırmak onu yalnız tabloda canlı gösterir),
    /// engelin içinde canlanan oyuncu saniyede 30 HP kaybedip anında yeniden ölür (düğme bir ölüm
    /// döngüsü üretirdi). Ret istemciye bildirilmez — sunucu konsoluna gerekçesiyle bir satır yazar,
    /// operatör sonucu roster'da görür.</para>
    [Serializable]
    public class RevivePlayerMsg
    {
        public string type = MessageTypes.RevivePlayer;
        public int playerId;
    }

    /// Admin → sunucu yönünde playerId dolu; sunucu → istemci yönünde alansız gider.
    [Serializable]
    public class IdentifyMsg
    {
        public string type = MessageTypes.Identify;
        public int playerId;
    }

    /// Bir sonraki maçın ORTAK mod/harita/süre/limit seçimi (§5.2). Maçı başlatmaz; sunucudaki
    /// seçimi günceller ve sunucu onu admin_state ile tüm adminlere yayar.
    /// Boş string veya 0 bırakılan alan mevcut değeri korur.
    [Serializable]
    public class SetSelectionMsg
    {
        public string type = MessageTypes.SetSelection;
        public string modeId;
        public string sceneName;
        public int roundSeconds;
        public int scoreLimit;

        /// <summary>Geri sayım uzunluğu (sn); <c>0</c> = mevcut değeri koru (diğer alanlarla
        /// aynı sözleşme).</summary>
        public int countdownSeconds;
    }

    /// <summary>
    /// Dost ateşi anahtarı (§5.2) — <b>sunucu oturumu ayarı</b>, maç parametresi değil.
    /// <para>
    /// ⚠️ <b>Faz kapısı yoktur:</b> koşan maçta da geçerlidir ve etkisi anlıktır (<c>set_team</c> ile
    /// aynı gerekçe — sahadaki durumu operatör maçı iptal etmeden düzeltebilmeli). Sunucu açılışta
    /// <c>false</c> başlar; değer maç bitiminde/harita değişiminde sıfırlanmaz, yalnız operatör
    /// değiştirir.
    /// </para>
    /// <para>Neden <c>set_selection</c> alanı değil: o mesaj "boş/0 = değişme" sözleşmesiyle çalışır
    /// ve bir <c>bool</c> "dokunulmadı"yı ifade edemez.</para>
    /// </summary>
    [Serializable]
    public class SetFriendlyFireMsg
    {
        public string type = MessageTypes.SetFriendlyFire;
        public bool enabled;
    }

    /// <summary>
    /// Başlıkların <b>AÇILIŞTA</b> nasıl hizalanacağı (§5.2/§10.6) — dost ateşiyle aynı sınıf:
    /// <b>anlık komut</b>, <c>set_selection</c>'a binmez, seçim kilidine girmez, koşan maçta da
    /// değişir.
    /// <para>Değer <c>ArenaProtocol.CALIB_MODE_*</c>; sunucu açılış varsayılanı
    /// <see cref="ArenaProtocol.CALIB_MODE_TWO_ANCHOR"/>.</para>
    /// <para>⚠️ Bilinmeyen/boş değer <b>REDDEDİLİR</b> (kural değerlerinin "varsayılana düş"
    /// sözleşmesi burada geçmez): mod bir kural şekli değil operatör kararıdır.</para>
    /// <para>⚠️ Modun kapıladığı tek şey açılıştaki diskten çapa geri yüklemesidir; HARİTA
    /// DEĞİŞİMİNDEKİ geri yükleme moddan bağımsız her zaman koşar (§10.6).</para>
    /// </summary>
    [Serializable]
    public class SetCalibrationModeMsg
    {
        public string type = MessageTypes.SetCalibrationMode;
        public string mode = "";
    }

    // ---- Sunucu → İstemci ----

    /// <summary>
    /// Modun ŞEKLİ (§10.5) — SUNUCU-OTORİTER. İstemci modun ne olduğunu tahmin etmesin diye
    /// telden gelir: kural buradan okunursa istemcide "if (modeId == …)" zinciri hiç doğmaz.
    /// <para>Değerler bilerek string: <b>bilinmeyen/boş değer varsayılana (takımlı TDM) düşer</b>,
    /// bu yüzden yeni bir kural değeri eklemek PROTOCOL_VERSION'ı artırmaz.</para>
    /// </summary>
    [Serializable]
    public class ModeRulesInfo
    {
        /// <summary>"two" (kırmızı/mavi) | "none" (takımsız).</summary>
        public string teamMode = "two";

        /// <summary>"team" (match_state.scoreRed/scoreBlue) | "player" (PlayerInfo.score).</summary>
        public string scoring = "team";

        /// <summary>true = takım arkadaşı vurulabilir (§10.3/4).</summary>
        public bool friendlyFire;

        /// <summary>"base" (kendi BaseZone'una gir) | "standstill" (sabit dur), §10.4.</summary>
        public string reviveAnchor = "base";

        /// <summary>"weaponcanvas" (sahnede duran silah) | "random" (mod dağıtır) — tümüyle istemci
        /// sunumu. ⚠️ Eski adı <c>"rack"</c>'ti; okuyan taraf "random" değilse varsayılana düştüğü
        /// için iki yazım da doğru davranışı verir (§10.5).</summary>
        public string weaponSource = "weaponcanvas";

        /// <summary>respawn.delaySeconds; ArenaProtocol.RESPAWN_DELAY varsayılanı.</summary>
        public float respawnDelay = ArenaProtocol.RESPAWN_DELAY;

        /// <summary>
        /// Faz <c>playing</c> değilken silah ateşlenebilir mi (§10.5). <c>true</c> = lobi gibi
        /// serbest atış alanı: namlu alevi/ses relay edilir ama <b>hasar yine yoktur</b>
        /// (<c>hit_report</c> kapısı her hâlükârda <c>playing</c>'dir, §10.3).
        /// <para>Bu alan sayesinde istemcide <c>if (modeId == "lobby")</c> zinciri doğmaz.</para>
        /// </summary>
        public bool fireWhilePaused;
    }

    /// <summary>
    /// Maçın durumu (§10.1). <b>Dört alan, dört ayrı sahip:</b> <c>modeId</c> ne oynandığı,
    /// <c>phase</c> çekirdeğin genel durumu, <c>phaseReason</c> duraklamanın gerekçesi,
    /// <c>modeState</c> modun kendi ara durumu.
    /// </summary>
    [Serializable]
    public class MatchInfo
    {
        /// <summary>ArenaProtocol.PHASE_* — hasarın işlendiği TEK faz <c>playing</c>'dir.</summary>
        public string phase;

        /// <summary>ArenaProtocol.PAUSE_REASON_* ; yalnız <c>phase == paused</c> iken dolu.</summary>
        public string phaseReason;

        public string modeId;

        /// <summary>Modun kendi ara durumu (serbest string). <b>Çekirdek yorumlamaz</b>, yalnız
        /// HUD okur; asla bir kural/hasar kapısı değildir (§10.1).</summary>
        public string modeState;

        public string sceneName;
        public float timeRemaining;
        public int scoreRed;
        public int scoreBlue;

        /// <summary>Koşan maçın kural şekli (§10.5) — geç katılım aynı kurallarla bağlanır.</summary>
        public ModeRulesInfo rules;
    }

    [Serializable]
    public class WelcomeMsg
    {
        public string type = MessageTypes.Welcome;
        public int protocolVersion;
        public int playerId;
        public uint udpToken;

        /// <summary>Yürürlükteki kalibre modu (<c>ArenaProtocol.CALIB_MODE_*</c>, §10.6).
        /// <para>⚠️ <b>Oyuncu bu değeri bağlantıda BİR KEZ çeker:</b> modun kapıladığı karar
        /// (açılışta diskten çapa geri yüklenecek mi) <c>welcome</c> geldiğinde zaten verilmiştir —
        /// bağlı oyuncuya canlı yayılım YOKTUR ve eklenmez, uygulanacağı bir an olmazdı.</para></summary>
        public string calibrationMode = "";

        public MatchInfo match;
    }

    [Serializable]
    public class PlayerInfo
    {
        public int playerId;
        public string name;

        /// <summary>Forma numarası 1..99 (§2); 0 = atanmamış, admin'de daima 0. Ad benzersiz
        /// DEĞİLDİR (20'lik havuz tekrar eder) — ayırt edici alan budur, arayüzde "7 · ertu".</summary>
        public int number;

        public string role;
        public string team;
        public bool ready;

        /// <summary>ArenaProtocol.CONNECTION_* (§2/§5.3): <c>connected</c> | <c>reconnecting</c> |
        /// <c>left</c>. <b>Bilinmeyen/boş değer <c>connected</c> sayılır</b> — kural değerleriyle
        /// aynı sözleşme (§10.5), dördüncü bir durum sürüm artırmasın diye.
        /// <para>⚠️ Eski <c>online</c> (bool) alanının yerine geçer ve "çevrimdışı" diye bir durum
        /// YOKTUR: kopan cihaz ya geri beklenir ya oyundan çıkarılır.</para></summary>
        public string connection = ArenaProtocol.CONNECTION_CONNECTED;

        /// <summary>Cihazın oyundan çıkarılmasına kalan saniye; yalnız <c>reconnecting</c> iken
        /// anlamlıdır (0 = yok). <b>Geri sayımdır, zaman damgası değildir</b> ve her
        /// <c>lobby_state</c>'te yeniden hesaplanır — roster yayını olay tabanlı olduğu için
        /// arayüz onu YERELDE de tüketmelidir, yoksa sayaç ancak başka bir değişiklikte ilerler.</summary>
        public int reconnectSeconds;

        /// <summary>Bu kayıt koşan maçın katılımcısı mı (§10.2). Maç sonu tablosunun kapsamı budur:
        /// <c>left</c> bir satır yalnız bu bayrak yüzünden listede durur. Maç kapanınca hepsi
        /// <c>false</c> olur ve <c>left</c> kayıtlar silinir.</summary>
        public bool inMatch;

        public float battery;

        /// <summary>Sol/sağ kumandanın durumu: <c>ArenaProtocol.CONTROLLER_*</c> (değer tablosu
        /// §5.1). <c>0</c> = bildirilmedi; admin kaydında daima <c>0</c> kalır.
        /// <para>⚠️ <b>Pil YÜZDESİ DEĞİL, durumdur ve yüzde olamaz</b> (OpenXR kumanda şarjını
        /// okumuyor). <see cref="battery"/> <b>gözlüğün</b> pilidir.</para>
        /// <para>Roster'da taşınmasının gerekçesi kalibrasyon alanlarınınkiyle aynıdır: kesikli bir
        /// durumdur, değiştiği an roster'ın zaten tazelendiği andır.</para></summary>
        public int ctrlL;

        /// <inheritdoc cref="ctrlL"/>
        public int ctrlR;

        public string scene;

        // Maç sayaçları (§10.2) — SUNUCU-OTORİTER, admin gözlemci arayüzünün doğruluk kaynağı.
        // Yalnız kill_event/health_update sayılsa admin yeniden bağlandığında tablo sıfırlanırdı.
        // Lobby fazında: hp=PLAYER_MAX_HP, alive=true, sayaçlar 0. Oyuncu istemcisi yok sayar.
        public int kills;
        public int deaths;
        public float hp;
        public bool alive;

        /// <summary>BİREYSEL maç skoru (§10.2) — kills ile aynı şey DEĞİLDİR: yazarı IGameMode'dur
        /// ve mod başına anlamı değişir (FFA puanı, Silah Yarışı'nda seviye…). rules.scoring ==
        /// "player" olan modlarda anlamlıdır; takım skoru match_state'te kalır.</summary>
        public int score;

        // Kalibrasyon durumu (§10.6) — maç sayacı DEĞİL cihaz durumudur: yazarı MatchDirector
        // değil PlayerRegistry'dir ve maç sıfırlamalarında korunur. Kalibresiz oyuncu ateş edemez,
        // hasar yemez, canlanamaz; uzak avatarı parlar. Admin'de daima false/"" kalır.
        public bool calibrated;
        public string calibrationSource;

        /// <summary>Son <b>elle</b> kalibrasyonda bildirilen zemin sapması (metre, işaretli;
        /// §5.1/§10.6); <c>0</c> = ölçüm yok ya da temiz. Mutlak değeri
        /// <see cref="ArenaProtocol.CALIB_FLOOR_WARN_METERS"/>'i aşan satır arayüzde ⚠ ile
        /// gösterilir. <c>clear_calibration</c> sıfırlar.</summary>
        public float floorOffset;

        /// <summary>Son gövde ölçümü başarısızsa gerekçesi, boş = sorun yok (§10.8).
        /// <para>⚠️ <b>Başarılı ölçüm alanı temizler</b> — aksi hâlde bir kez başarısız olan
        /// oyuncunun satırında uyarı sonsuza kadar kalır ve operatör sorunun sürdüğünü sanardı.
        /// <c>clear_calibration</c> da sıfırlar.</para></summary>
        public string scaleError = "";

        /// <summary>Uzak avatara uygulanacak üniform gövde ölçeği (§10.8). <b><c>0</c> =
        /// ölçülmemiş → okuyan taraf <c>1</c> uygular</b> (kural değerleriyle aynı sözleşme:
        /// alanı hiç göndermeyen bir uç sessizce doğru davranır).
        /// <para>Kalibrasyon alanlarıyla aynı sınıftadır — cihaz durumudur, maç sayacı değil — ve
        /// aynı sebeple burada taşınır: değiştiği an roster'ın zaten tazelendiği andır.
        /// ⚠️ İskelet kanalına (<c>0x07</c>/<c>0x08</c>) GİRMEZ; orada her karede tekrar eden bir
        /// sabit olurdu.</para></summary>
        public float bodyScale;
    }

    [Serializable]
    public class LobbyStateMsg
    {
        public string type = MessageTypes.LobbyState;

        /// <summary>Monoton artan roster sürümü (§5.3); sunucu ömrü boyunca artar, yeniden
        /// başlarsa 0'dan. İstemci <c>version &lt;= uyguladığı son sürüm</c> olan mesajı ATAR ve
        /// sürümü her welcome'da sıfırlar. Sunucuda yayın tek yayıncıdan gittiği için sıra zaten
        /// korunur; bu guard ikinci emniyettir — sürümsüz ateşle-unut yayında eski bir anlık
        /// görüntü yeniyi ezer ve roster bir sonraki değişikliğe kadar bayat kalırdı.</summary>
        public int version;

        public PlayerInfo[] players;
    }

    [Serializable]
    public class LoadMatchMsg
    {
        public string type = MessageTypes.LoadMatch;
        public string modeId;
        public string sceneName;
        public int roundSeconds;
        public int scoreLimit;
        public string yourTeam;

        /// <summary>Bu maçın kural şekli (§10.5); istemci kendini BUNA göre kurar.</summary>
        public ModeRulesInfo rules;
    }

    [Serializable]
    public class CountdownMsg
    {
        public string type = MessageTypes.Countdown;
        public int seconds;
    }

    [Serializable]
    public class MatchStateMsg
    {
        public string type = MessageTypes.MatchState;

        /// <summary>ArenaProtocol.PHASE_* (§10.1).</summary>
        public string phase;

        /// <summary>ArenaProtocol.PAUSE_REASON_* ; yalnız <c>phase == paused</c> iken dolu.</summary>
        public string phaseReason;

        /// <summary>Modun kendi ara durumu; çekirdek yorumlamaz (§10.1).</summary>
        public string modeState;

        public float timeRemaining;
        public int scoreRed;
        public int scoreBlue;
    }

    /// <summary>
    /// Can değişimi (§10.3). ⚠️ <b>Broadcast DEĞİL:</b> yalnız <c>playerId</c>'nin sahibine ve
    /// adminlere gider. İki tüketicisi de dar — <c>PlayerCombatState</c> kendisine ait olmayan her
    /// mesajı zaten düşürüyor, admin tablosu ise herkesin canını çiziyor. Herkese yayınlandığı
    /// dönemde 10 oyunculu maçta her isabette 11 mesaj gidip 9'u çöpe atılıyordu ve bu, oyuncu
    /// sayısıyla <b>kare</b> büyüyen tek WS kanalıydı (Docs/Sistem-Ozeti.md §3.12).
    /// </summary>
    [Serializable]
    public class HealthUpdateMsg
    {
        public string type = MessageTypes.HealthUpdate;
        public int playerId;
        public float hp;

        /// <summary>Vuran oyuncu; <c>0</c> = saldırı değil (canlanma).
        /// <para><b>Bugün okuyan yok</b> — yönlü hasar göstergesi için ayrılmıştır ve mesaj artık
        /// zaten yalnız kurbana gittiği için doğal yeri burasıdır. Kaldırılacaksa göstergenin de
        /// hiç yapılmayacağına karar verilmiş olmalı.</para></summary>
        public int attackerId;
    }

    [Serializable]
    public class KillEventMsg
    {
        public string type = MessageTypes.KillEvent;
        public int killerId;
        public int victimId;
        public string weaponId;
    }

    [Serializable]
    public class RespawnMsg
    {
        public string type = MessageTypes.Respawn;
        public int playerId;
        public float delaySeconds;
    }

    /// Kazanan İKİ kanaldan biriyle ifade edilir (rules.scoring, §10.5): takım skorlu modlarda
    /// winnerTeam ("red"|"blue"|""), bireysel skorlu modlarda winnerPlayerId (0 = yok/berabere).
    /// Bir mod ikisini de doldurmaz; okuyan istemci dolu olana bakar.
    [Serializable]
    public class MatchEndMsg
    {
        public string type = MessageTypes.MatchEnd;
        public string winnerTeam;
        public int winnerPlayerId;
        public int scoreRed;
        public int scoreBlue;
    }

    /// <summary>Lobiye dönüş (§10.7). Şekli <see cref="LoadMatchMsg"/> ile aynıdır: lobi de bir
    /// sahne + bir profil taşır.
    /// <para><c>sceneName</c> işletmenin lobi sahnesidir (<c>server.json → lobbyScene</c>);
    /// <b>boş gelirse</b> istemci kendi kabuk <c>Lobby</c> sahnesine döner — eski sunucuyla ve
    /// lobisi yapılandırılmamış kurulumla uyum bu sayede korunur.</para>
    /// <para><c>modeId</c> lobide <c>"lobby"</c>dir: istemci silah loadout'unu bu anahtarla
    /// çözer. Kayıtlı bir maç modu DEĞİLDİR (§10.5) — <c>start_match</c> ile başlatılamaz.</para></summary>
    [Serializable]
    public class ReturnToLobbyMsg
    {
        public string type = MessageTypes.ReturnToLobby;
        public string modeId;
        public string sceneName;

        /// <summary>Lobi profilinin kural şekli (§10.5); lobide bugünkü varsayılandır.</summary>
        public ModeRulesInfo rules;
    }

    [Serializable]
    public class PingMsg
    {
        public string type = MessageTypes.Ping;
    }

    [Serializable]
    public class KickedMsg
    {
        public string type = MessageTypes.Kicked;
        public string reason;
    }

    /// Yalnız role=admin bağlantılara (§5.3): adminler arası ORTAK durumun tek doğruluk kaynağı.
    /// modeId/sceneName ortak seçimdir (arayüz kendi yerelini değil bunu gösterir); notice son
    /// admin eyleminin "<ad>: <eylem>" özetidir; adminCount çevrimiçi admin sayısıdır.
    /// Görünüm tercihleri (kamera, halka, saydamlık…) BURAYA GİRMEZ — her admin'in kendi ekranı.
    [Serializable]
    public class AdminStateMsg
    {
        public string type = MessageTypes.AdminState;
        public string modeId;
        public string sceneName;

        /// <summary>Bir sonraki maçın ortak parametreleri; 0 = hiç seçilmedi (mod varsayılanı).</summary>
        public int roundSeconds;
        public int scoreLimit;

        /// <summary>Geri sayım uzunluğu (sn); 0 = hiç seçilmedi (COUNTDOWN_SECONDS).</summary>
        public int countdownSeconds;

        /// <summary>Dost ateşi anahtarının O ANKİ değeri (§5.2). Seçim değil <b>yürürlükteki
        /// durum</b>dur: koşan maçta da geçerlidir, bu yüzden diğer alanların "0 = seçilmedi"
        /// sözleşmesine girmez.</summary>
        public bool friendlyFire;

        /// <summary>Kalibre modunun O ANKİ değeri (<c>ArenaProtocol.CALIB_MODE_*</c>, §5.2).
        /// <see cref="friendlyFire"/> ile aynı sınıf: seçim değil <b>yürürlükteki durum</b>, bu
        /// yüzden "0 = seçilmedi" sözleşmesine ve seçim kilidine girmez.
        /// <para>Oyuncuya buradan GİTMEZ — o değeri <see cref="WelcomeMsg.calibrationMode"/> ile
        /// bir kez alır.</para></summary>
        public string calibrationMode = "";

        public string notice;
        public int adminCount;

        /// <summary>Bu oturumda açılan mekan (§11) — sunucu başlarken seçilir, çalışırken
        /// değişmez. Mekan ayrımı yoksa (maps.json boş) boş gelir.</summary>
        public string venueId;

        /// <summary>Bu mekanda oynatılabilen sahne adları. <b>Admin harita seçicisi kendi yerel
        /// kataloğunu BUNUNLA süzer</b>: katalog tüm projeyi tanır, oynatılabilir olan ise
        /// sunucunun o an açtığı mekandır. Boş gelirse (mekan ayrımı yok) süzme yapılmaz.</summary>
        public string[] venueScenes;
    }

    /// <summary>
    /// Seçilen modun SUNUMA ait tek alanı (§5.3) — <b>herkese</b> gider, adminlere değil.
    /// <para>
    /// ⚠️ <b>Kural mesajı DEĞİLDİR:</b> <see cref="ModeRulesInfo"/> gibi <c>ModeRuntime</c>'a
    /// uygulanmaz. Aktif kuralların kaynağı <c>load_match</c>/<c>welcome</c>/<c>return_to_lobby</c>
    /// olarak kalır; bu mesaj yalnız "henüz başlamamış maçın şekli"ni anlatır ve tek tüketicisi
    /// <c>BaseZoneVisibility</c>'dir (taban şeritleri görünsün mü — §10.7).
    /// </para>
    /// <para>Eski sunucu bu mesajı hiç yollamaz; istemci o zaman aktif kuralın takım kipine düşer,
    /// bu yüzden <c>PROTOCOL_VERSION</c> artmadı.</para>
    /// </summary>
    /// <summary>
    /// Koşan maçın kural şekli DEĞİŞTİ (§5.3) — <b>herkese</b> yayınlanır.
    /// <para>
    /// Bugün tek tetikleyicisi dost ateşi anahtarıdır (<see cref="SetFriendlyFireMsg"/>): kurallar
    /// normalde <c>welcome</c>/<c>load_match</c> ile geldiği için maç ORTASINDA değişen bir kuralı
    /// taşıyacak kanal yoktu. Bu mesaj o farkı taşır; geç bağlanan istemci doğru değeri yine
    /// <c>welcome.match.rules</c>'tan alır (sunucu <c>_rules</c>'ü güncel tutuyor).
    /// </para>
    /// <para>⚠️ <c>SelectionStateMsg</c>'in aksine bu <b>gerçek bir kural mesajıdır</b> ve
    /// <c>ModeRuntime</c>'a uygulanır. Eski sunucu hiç yollamaz, eski istemci tanımadığı tipi yok
    /// sayar → <c>PROTOCOL_VERSION</c> artmadı.</para>
    /// </summary>
    [Serializable]
    public class RulesUpdateMsg
    {
        public string type = MessageTypes.RulesUpdate;

        /// <summary>O an geçerli tür (<c>match_state.modeId</c> ile aynı) — <c>ModeRuntime</c> kuralı
        /// modla birlikte sakladığı için mesaj ikisini birden taşır.</summary>
        public string modeId;

        public ModeRulesInfo rules;
    }

    [Serializable]
    public class SelectionStateMsg
    {
        public string type = MessageTypes.SelectionState;

        /// <summary>Ortak seçim (<c>admin_state.modeId</c> ile aynı değer; açılışta "lobby").
        /// Maç türünü DEĞİŞTİRMEZ — o `start_match`'i bekler.</summary>
        public string modeId;

        /// <summary>"two" | "none" — seçili modun takım kipi (§10.5). Tanınmayan modda "two".</summary>
        public string teamMode;
    }

    /// <summary>Tek oyuncunun ağ telemetrisi (<see cref="NetStatsMsg"/> girdisi). Değerler
    /// İSTEMCİDEN gelir (<see cref="StatusMsg"/>), sunucu yalnız taşır; -1 = bilinmiyor.</summary>
    [Serializable]
    public class NetStatsEntry
    {
        public int playerId;
        public int rttMs = -1;
        public float jitterMs = -1f;
        public float lossPct = -1f;
    }

    /// <summary>
    /// Yalnız <c>role=admin</c> bağlantılara, 1 Hz: oyuncu başına ping/jitter/kayıp.
    /// <para>⚠️ <b>Broadcast EDİLMEZ.</b> Herkese yayınlamak, oyuncu sayısıyla kare büyüyen bir
    /// fan-out üretirdi — yani bu telemetrinin ölçmek için var olduğu sorunun aynısını. Hedef kuralı
    /// <c>admin_state</c> ile aynıdır.</para>
    /// <para>⚠️ <b>Roster'ın (<c>lobby_state</c>) alternatifi değil, bilinçli olarak ayrı bir
    /// kanaldır:</b> roster'ın bir <c>version</c>'ı ve uzlaştırma protokolü var (§5.1); saniyede bir
    /// değişen telemetriyi oraya koymak versiyonu sürekli çevirip uzlaştırmayı anlamsızlaştırırdı.
    /// Bu mesajın kaybı ise zararsızdır (teşhis verisi, bir sonraki saniye yenisi gelir).</para>
    /// <para><b>Bant/bayt alanı YOKTUR ve eklenmez</b> — hacim sayıları sunucu konsolunda kalır
    /// (`[state]` satırı); operatörün eyleme çevirebileceği sayı ping'dir.</para>
    /// </summary>
    [Serializable]
    public class NetStatsMsg
    {
        public string type = MessageTypes.NetStats;
        public NetStatsEntry[] players;
    }

    // ---- UDP beacon (§4; WS mesajı değildir, alıcı app alanını doğrular) ----

    [Serializable]
    public class BeaconMsg
    {
        public string app;
        public int ver;
        public string ip;
        public int controlPort;
        public int statePort;
        public string serverId;
    }
}
