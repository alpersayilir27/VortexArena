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
    }

    // ---- Yalnız admin → Sunucu ----

    /// start_match (§5.2). roundSeconds/scoreLimit O MAÇA özeldir: ≤0 ya da eksikse modun
    /// varsayılanı (IGameMode.DefaultRoundSeconds/DefaultScoreLimit) kullanılır.
    [Serializable]
    public class StartMatchMsg
    {
        public string type = MessageTypes.StartMatch;
        public string modeId;
        public string sceneName;
        public int roundSeconds;
        public int scoreLimit;
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
    /// <para><c>playerId == 0</c> = TÜM oyuncular (toplu sıfırlama).</para>
    [Serializable]
    public class ClearCalibrationMsg
    {
        public string type = MessageTypes.ClearCalibration;
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

        /// <summary>"rack" (sahnedeki raf) | "random" (mod dağıtır) — tümüyle istemci sunumu.</summary>
        public string weaponSource = "rack";

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
        public bool online;
        public float battery;
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
