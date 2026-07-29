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

    /// İstemci → sunucu yönünde seq dolu; sunucu relay'inde playerId dolu (seq taşınmaz).
    [Serializable]
    public class ShotFiredMsg
    {
        public string type = MessageTypes.ShotFired;
        public int seq;
        public int playerId;
        public string weaponId;
        public float[] muzzlePos;
        public float[] muzzleDir;
    }

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
    /// <c>source</c> ∈ "manual" (kumandada A+B) | "anchor" (kayıtlı OVRSpatialAnchor'dan geri
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

    [Serializable]
    public class HealthUpdateMsg
    {
        public string type = MessageTypes.HealthUpdate;
        public int playerId;
        public float hp;
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
