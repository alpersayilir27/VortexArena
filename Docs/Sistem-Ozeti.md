# VortexArena — Sistem Özeti (ne yapıldı, nasıl çalışıyor, nasıl kullanılır)

> **Bu doküman ne?** Bugünkü sistemin tek sayfalık haritası: mimari, ağ mantığı, hangi
> bileşen ne yapıyor ve günlük iş akışı ("şunu eklemek istiyorum, nereye dokunacağım?").
>
> **Bu doküman ne DEĞİL?** Protokolün tanımı değil. Mesaj alanları, sabitler ve doğrulama
> kuralları için **`Docs/ArenaNet-Protokol.md`** (TEK doğruluk kaynağı — davranış değişecekse
> ÖNCE orası güncellenir). Diğer başvurular:
>
> | Konu | Dosya |
> |---|---|
> | Protokol (mesajlar, sabitler, kurallar) | `Docs/ArenaNet-Protokol.md` |
> | Mimari talimatlar + ekleme reçeteleri | `CLAUDE.md` |
> | İşletmeye kurulum kontrol listesi | `Docs/Isletme-Kurulum.md` |
> | Operatörün günlük kullanım kılavuzu (teknik olmayan) | `Docs/Kullanim-Kilavuzu.md` |
> | Sunucu çalıştırma / config / konsol logları | `Server/README.md` |
> | Sıradaki planlanmış işler (mod altyapısı, FFA) | `plan/README.md` |

---

## 1. Ürün ve roller

**VortexArena** = işletmelere (LBE) kurulan **free-roam VR PvP arena**. Oyuncular fiziksel alanda
**1:1 yürür** (lokomosyon yok), hedef cihaz **yalnız Quest 3 / 3S**. Aynı Unity projesinden iki
build çıkar:

| Build | Rol | Ne yapar |
|---|---|---|
| **Android (Quest)** | `player` | Lobi → maç; oynar, poz gönderir, ateş eder |
| **Windows** | `admin` | `--server-ip` ile açılır → **oyuncularla aynı sahneye** girer (gözlemci): 3 kamera kipi + sahne üstü yönetim HUD'ı (roster, mod+harita, start/abort, istatistik) |
| **Windows** | launcher | `launcher/` — .NET 10 WPF operatör uygulaması: sunucu/admin exe yollarını, sunucu IP'sini ve **mekanı** tutar; sunucuyu `--venue`, oyunu `--server-ip` ile başlatır |

Üçüncü bileşen: **`Server/` — kendi .NET 10 konsol sunucumuz** (standalone exe, tamamen offline
LAN). Mirror/NGO gibi hazır netcode **kullanılmıyor**; hem oyun kurallarının sunucuda koşması hem
de işletmede internetsiz çalışma şartı bunu gerektiriyor.

---

## 2. Repo haritası ve assembly grafiği

```
D:\Games\vortexarena\
  Assets\
    _Shared\                 ← "ikinci bir mod/arena bunu aynen kullanır mı?" → EVET olan her şey
      Core\                  VortexArena.Core        (arena, savaş, oyuncu, UI, katalog SO'ları, FX)
        Editor\              VortexArena.Core.Editor (Configure All Build Elements, Template
                             Temellerini Yükle, ölçü maketi üretimi/geri okuması)
      Net\Protocol\          VortexArena.Protocol    (SAF C# — sunucu aynı dosyaları derler)
      Net\Scripts\           VortexArena.Net         (bağlantı/keşif/senkron; oyun bilgisi YOK)
        Editor\              VortexArena.Net.Editor  (Network Parent, sceneId bekçisi)
      App\Scripts\           VortexArena.App         (Boot yönlendirme, Lobi, UiKit, köprüler)
        Admin\               (aynı asmdef) admin gözlemci: kamera kipleri, HUD, paneller
        Editor\              VortexArena.App.Editor  (Dev penceresi: rol/hedef + sunucu derleme)
        Resources\UI\        ARAYÜZÜN TAMAMI prefab olarak (admin HUD+paneller, oyuncu satırı,
                             oyuncu halkası, bağlantı ekranı ×2, cephane, kimlik kartı).
                             Sahneye KONMAZ, Resources.Load ile yüklenir — elle düzenlenir
        UI\Sprites\          o prefabların 9-slice köşe/halka görselleri
      Arsenal\ FX\ Environments\ Scenes\             (kod-dışı ortak içerik + Boot/Lobby)
      Data\Resources\        GameCatalog.asset — admin arayüzü Resources.Load ile okur
    Arenas\Template\         referans arena — OYNANMAZ (export/Build Settings/katalog dışı)
      Scenes\Default12x12\   dizayn taşımayan referans: "ağa bağlı sahne neye benzer"
    Arenas\Venues\<İşletme>\ MEKAN = kutuların kabı; kökünde YALNIZ Art\ Prefabs\ Data\ Scenes\
                             durur (ilk üçü o mekanın TÜM sahnelerinin paylaştığı içerik)
      Scenes\<SahneAdı>\     arena kutusu: <SahneAdı>.unity + Data\<SahneAdı>.asset (MapDefinition)
                             + istenirse Art\ Prefabs\ — klasör/sahne/asset adı ÜÇÜ DE aynı;
                             arena-özel KOD yazılmaz. Lobi de bir kutudur, farkı
                             MapDefinition.supportedModeIds = ["lobby"]
    Arenas\Venues\Outdoor12x12\  Scenes\IceWorld (elle modellenmiş tematik arena +
                             Art\{Materials,Textures} + Prefabs\FX_SnowStorm) · Scenes\Arena12x12
                             · Scenes\Lobby12x12
                             + Data\Outdoor12x12_dimensions.json — mekanın boyut dosyası
    Arenas\Venues\VortexAntep\   Scenes\Hangar (planlı asimetrik arena) · Scenes\LobbyVortexAntep
                             (aynı fiziksel oda)
                             + Data\vortexantep_dimensions.json — mekanın boyut dosyası (ölçünün
                             TEK kaynağı), ikisi de kullanır
    Modes\TeamDeathmatch\    mod kutusu: {Scripts → VortexArena.Modes.Tdm, Data, UI}
    Modes\FreeForAll\        mod kutusu: {Scripts → VortexArena.Modes.Ffa, Data, UI} — takımsız
                             "Herkes Tek"; arena-özel hiçbir iş gerektirmez
  Server\                    .NET 10 çözümü (Core kütüphanesi + App konsolu)
  launcher\                  .NET 10 WPF Windows launcher — operatör giriş noktası
    VortexArena.Launcher\    App/MainWindow (tek ekran: Sunucu / Bağlantı / Yönetim oyunu),
                             LauncherConfig (ayarlar + argümanlar), VenueCatalog (maps.json →
                             mekan listesi), Theme\Dark.xaml
    VortexArena.Launcher.Tests\  argüman sözleşmesi + maps.json ayrıştırma testleri
  docs-serve.bat             dokuman sitesini localhost:1111'de sunar (Quartz; icerik = Docs\,
                             motor repo DISINDA ..\vortexarena-docs-site — git'e girmez)
  scripts\                   deploy-admin-game.bat · deploy-player-apk.bat · deploy-server.bat
                             deploy-launcher.bat
                             docs-setup.bat (doküman sitesini yeni PC'de bir kez kurar)
  deploy\                    ÜRETİLEN çalıştırılabilirler: admin\ player\ server\ launcher\
                             (git'e girmez)
  dev-targets.json           dev penceresinin adlandırılmış sunucu hedefleri (COMMIT'Lİ;
                             seçim EditorPrefs'te kişisel kalır — bkz. §6.2)
  Docs\                      dokumantasyon (docs-serve.bat bunu sunar)
    Gelistirici\             OYUN GELISTIRICISI icin giris kapisi: Ilk-Adimlar · Yemek-Kitabi
                             (receteler) · API-Referansi · Sahne-Kurulumu · Yapma-Listesi
  plan\  .claude\rules\  CLAUDE.md
```

**Bağımlılıklar hep aşağı akar; modlar birbirini referanslamaz:**

```
        VortexArena.Protocol      saf C#, noEngineReferences  ─┐
                 ▲                                            │ aynı .cs dosyaları
        VortexArena.Net           taşıma/keşif/senkron         │ Server csproj'da
                 ▲                                            │ <Compile Include>
        VortexArena.Core          oyun kodu (arena, savaş)     │ ile derlenir
           ▲          ▲                                       │
  VortexArena.App   VortexArena.Modes.Tdm / .Ffa           ──┘
```

Bunun iki sert sonucu var:

1. **`Protocol` içine `UnityEngine` giremez.** Girerse sunucu derlemesi kırılır — bu bilinçli bir
   bekçi: istemci ile sunucunun DTO'ları yapısal olarak **sapamaz**.
2. **`Net` katmanı sahne yüklemez, oyun bilmez.** Olay yayınlar (`NetEvents`), `App` dinleyip
   sahne yükler. Böylece ağ katmanı başka bir oyuna taşınabilir kalır.

---

## 3. Ağ sisteminin mantığı

### 3.1 Üç kanal

| Kanal | Port | Taşıma | Ne taşır |
|---|---|---|---|
| **Beacon** | UDP **47820** | broadcast, 2 sn'de bir | "buradayım": ip + portlar + serverId |
| **Kontrol** | TCP **47821** `/ws` | WebSocket, JSON | lobi, maç akışı, atış/vuruş, can, skor — **güvenilir olması gereken her şey** |
| **State** | UDP **47822** | binary, little-endian | pozlar (istemci→sunucu 20 Hz) + snapshot (sunucu→herkes 20 Hz) |

Portlar `vortexcosmos`'un 47800/47801'i ile bilerek çakışmaz — aynı LAN'de iki ürün koşabilir.

### 3.2 Otorite bölünmesi — sistemin kalbi

```
POZLAR              → İSTEMCİ OTORİTER   (fiziksel tracking = gerçek; sunucu düzeltemez, düzeltmemeli)
CAN / SKOR / FAZ    → SUNUCU OTORİTER    (istemci hasar uygulamaz, skor tutmaz, faz değiştirmez)
KURALLAR (mod)      → SUNUCU OTORİTER    (.NET IGameMode; Unity'de mod kutusu yalnız SUNUM)
MODUN ŞEKLİ         → SUNUCU OTORİTER    (ModeRules → load_match.rules → ModeRuntime; §3.9)
```

Neden böyle: oyuncu gerçekten fiziksel alanda yürüyor, pozu için tek doğru kaynak başlığın kendisi.
Ama can/skor iki istemcide sapmamalı → onlar sunucuda. İstemcide **can tutan hiçbir bileşen
yoktur**: can yalnız `health_update` ile gelir ve `PlayerCombatState` üzerinden okunur. Aynı kural
kırılabilir/yıkılabilir sahne objeleri için de geçerli olacak — hasar alabilen her şey ağsaldır
(`NetIdentity`), yerel bir can havuzu açılmaz.

### 3.3 Arena uzayı (koordinat çerçevesi)

Ağa giden **her poz arena-yerel uzaydadır**: origin = sahnedeki `SpawnPoint` (arena zemininde sabit
bir referans nokta), eksenler duvarlara hizalı.

```
Quest'in kendi dünya uzayı ──(ArenaCalibrator: 2 nokta + OVRSpatialAnchor)──► arena uzayı
                                     │
                       ArenaSpace.WorldToArena/ArenaToWorld
```

- Origin'i sahnedeki `SpawnPoint` `ArenaSpace`'e kaydeder; Lobby'de origin yoktur → dünya = arena
  (kimlik dönüşümü, `ArenaSpace` sahne başına bir kez uyarır — lobide normaldir). Origin muhafazadan
  (`ArenaBoundary`) **bağımsızdır**: duvarı büyütmek/kaydırmak ağ koordinatlarının sıfırını oynatmasın
  diye ikisi ayrıldı.
- Dönüşüm **istemcide** yapılır (`PlayerPoseTracker`); sunucu ve admin ham arena koordinatı görür.
- Bütün başlıklar aynı fiziksel alana kalibre olduğu için, arena uzayı **tüm cihazlarda aynı fiziksel
  noktayı** gösterir — çakışan avatar / yanlış yerde görünen rakip sorununun çözümü budur.
- Hizalama **6DOF**'tur: yaw + yatay konum A→B çiftinden, **zemin yüksekliği B noktasında yakalanan
  kumanda ucundan**. Zemin tracking origin'den alınmaz çünkü başlıklar **guardian/alan kurulumu
  olmadan** çalışır (§7.29). Yakalanan nokta kumandanın pivotu değil ucudur
  (`ArenaCalibrator.tipLocalOffset`); iki noktanın Y farkı **eğim telafisi için kullanılmaz**, ölçüm
  sağlığı olarak denetlenir (>10 cm → yakalama reddedilir).

### 3.4 Bağlantı yaşam döngüsü

```
İstemci açılır
  └─ adres zinciri (ROLDEN BAĞIMSIZ, AppBoot komut satırını her rolde okur):
       komut satırı --server-ip [--server-port] > elle girilen IP (PlayerPrefs)
       > beacon (5 sn dinle) > StreamingAssets/arena.json
  └─ VR (player): pratikte beacon — VR build'ine argüman geçilmez
                 → bulunan adrese OTOMATİK bağlanır; oyuncuya sorulmaz
                 → hiç bulunamazsa 8 sn sonra "joystick'e 1 sn basılı tut" ipucu (gizli IP paneli)
  └─ admin: adres launcher'ın geçtiği `--server-ip`'ten gelir; argüman yoksa bağlanmaz,
            sebebini ekranda yazar (editörde adres/rol dev penceresinden gelir → §6.2)
            → admin de Lobby sahnesinden bağlanır (ayrı dashboard sahnesi YOK)
  └─ ws://ip:47821/ws  →  hello{role, deviceId, scenes}  →  welcome{playerId, udpToken, match}
  └─ UDP kaydı: 0x00 UdpHello (ack gelene dek 1 sn'de bir tekrar)
  └─ status kalp atışı 5 sn  +  (player ise) poz döngüsü 20 Hz
  └─ welcome.match.phase ≠ Lobby ise → GEÇ KATILIM: maç sahnesine yetiş (admin dahil)
  └─ maç başlarken load_match → oyuncular + ADMİNLER aynı sahneyi yükler
       (admin'de yourTeam=""; admin set_ready GÖNDERMEZ)

Kopma → 1 → 2 → 5 sn backoff ile keşiften itibaren baştan (sonsuz, otomatik)
      → bağlantısızlık ~3 sn sürerse ConnectionOverlay hata ekranı (§4)
Sunucu → 15 sn status gelmezse çevrimdışı işaretle + bağlantıyı kapat + lobby_state yayınla
```

Elle girilen IP her zaman beacon'ı ezer ve `PlayerPrefs`'e kalıcı yazılır — işletmede beacon'ı
kesen/izole eden AP'lerde kurtarıcı budur. Açıkça verilen komut satırı adresi ise zincirin en
üstündedir: `LobbyController` onu `_manualEntry` sayar, böylece gelen bir beacon adresi EZMEZ.

### 3.5 Poz akışı (20 Hz)

```
PlayerPoseTracker (kafa + 2 el, dünya→arena) + HeldItems (elde ne var)
        │ IPoseSource
        ▼
UdpStateChannel ──0x01 PoseUpdate (95 B)──► StateHost ──0x02 Snapshot (≤1414 B)──► TÜM kayıtlı endpoint'ler
                                                       (olay varsa ve sığıyorsa 0x05 birleşik, §6.8)
                                                                                          │ (admin dahil)
                                                                     RemotePlayerRegistry ─┘
                                                          │ poz: 100 ms tamponla interpolasyon
                                                          │ eşya: interpole EDİLMEZ, son değer geçerli
                                                                    RemoteAvatar
```

Eşya baytları (`itemL`/`itemR` + kavrama bitleri) pozla **aynı pakette** gider: ikisi de
istemci-otoriter sunum bilgisidir. Duruş telde GİTMEZ — eşyanın ele göre pozu her istemcinin
APK'sındaki `ItemDefinition`'dan gelir (ön koşul: kanonik kavrama). → `Docs/ArenaNet-Protokol.md` §6.6

### 3.5b Atış/atma olay akışı (olay başına yukarı, 20 Hz batch aşağı)

```
Weapon.Fire() ──► ArenaCombat.ReportShot (dünya→arena YÖN çevirimi)
        │
        ▼
UdpStateChannel ──0x03 FireEvent (12 B, HEMEN)──► StateHost (kapı: faz + canlı + kalibre;
        │                                          kopya bastırma: seq)
        │                                                   │ ConcurrentQueue
        │                                                   ▼
        │                          0x04 EventBatch (6+9n B, snapshot ile aynı tik) ──► TÜM endpoint'ler
        ▼                                                                                     │
  yerel FX + tracer: Weapon.Fire (anında — sunucu atana        RemoteShotFx ────────────────────┘
  kendi olayını GERİ YOLLAMAZ, ShotTracer.Shared havuzu)
                                                             │ kendi playerId'sini süzer
                                                             │ tik halkasıyla kopya ayıklar
                                                             │ serverTick'in oynatma anına kadar BEKLETİR
                                                       namlu alevi + ses + ShotTracer
```

⚠️ Atış **UDP**'de (kaybı kozmetik), `hit_report` **WS**'te (otoriter hasar). Kanal ayrımı
bilinçli — gerekçe §7'de ve `Docs/ArenaNet-Protokol.md` §10.3'te.

**Olay neden bekletiliyor:** uzak avatarın pozu bilerek `INTERP_DELAY_MS` (100 ms) geriden
çizilir, ama sunucu snapshot'ı ile olay batch'ini AYNI tik'te yayınlar. Olay geldiği anda
oynatılsa alev/ses/tracer elin **100 ms öncesindeki** yerinden çıkardı (kol 2 m/s ise ~20 cm
kayma). Bu yüzden `RemotePlayerRegistry` snapshot'ların `serverTick → alım zamanı` eşlemesini
tutar (global bir halka — tik başına bir snapshot var, eşleme oyuncu başına değil) ve
`TryGetPlaybackTimeMs` her olayın oynatılacağı yerel anı verir: `alım + INTERP_DELAY_MS`.
Sonuç olarak 20 Hz batch'leme **algılanan gecikmeye eklenmez** — ≤50 ms'lik batch beklemesi
100 ms'lik tamponun içinde erir. Eşleme yoksa (henüz snapshot gelmemiş / tik halkadan düşmüş)
olay **hemen** oynatılır: geciken tracer kabul edilebilir, kaybolan tracer edilemez.
⚠️ **Geçmiş pozu örnekleyen bir kapı YOK ve gerekmiyor** — tracer'ın orijini telden gelen bir
konum değil, o karede ÇİZİLMİŞ silahın namlusu (§6.4 "tutarlılık > sadakat"); olay doğru anda
oynayınca çizili namlu zaten o tik'in namlusudur.

- Kendi pozunu snapshot'tan **çizmezsin** (yerelden çizersin) — gecikme sıfır kalır.
- Uzak oyuncular `INTERP_DELAY_MS = 100` tamponuyla yumuşatılır; paket kaybı tolere edilir
  (son gelen kazanır, eski `seq` atılır).
- **Eşzamanlı oyuncu/admin sınırı YOKTUR** (kota ileride lisanslamayla gelecek). Tek tavan
  `PLAYER_ID_MAX = 255` ve o bir ürün kotası değil, `playerId`'nin UDP'de `u8` olmasıdır.
  16'dan fazla pozlu oyuncu olduğunda snapshot MTU'ya sığan parçalara bölünür
  (`SNAPSHOT_MAX_ENTRIES_PER_PACKET = 16` → 1414 B); **istemcide birleştirme yoktur ve
  gerekmez** — her paket taşıdığı girdileri bağımsız uygular, düşürme kararı zaman aşımıdır.
- Bir `playerId` ~1.5 sn snapshot'larda görünmezse avatarı kaldırılır (sunucunun 15 sn'lik
  offline eşiğini beklemez).

### 3.6 Vuruş hattı

```
Weapon.Fire() / balta savurma / ok isabeti / bomba patlaması
   │
   ├─ 0x03 atış olayı (UDP) → sunucu DOĞRULAMAZ, sadece relay eder (0x04 batch); istemcide
   │                `RemoteShotFx` tüketir (uzak namlu alevi + konumsal atış sesi + tracer,
   │                itemId profiliyle). Kaybı KOZMETİKTİR — güvenilirlik aranmaz (§6.4)
   └─ hit_report (WS) → sunucu 5 tutarlılık kontrolü yapar:
        faz Live? · atıcı canlı? · hedef canlı? (çift ölüm olmasın) ·
        hedef başkası + takım arkadaşı DEĞİL mi? · damage sonlu ve pozitif mi?
                            ↓ geçerse
        hp -= damage → health_update (herkese) → hp ≤ 0 ise kill_event + IGameMode.OnKill + respawn
```

**Hasarı istemci hesaplar, sunucu aynen uygular.** Sunucuda silah tablosu, `weaponId` beyaz listesi
ve atış hızı denetimi **yoktur** — ürün gözetimli özel alanda (işletme, turnuva) çalıştığı için
hile koruması bilinçli olarak eklenmez; böyle bir denetim meşru saçma/patlama/yaylım vuruşlarını
sessizce düşürür. Yukarıdaki beş kontrol hile denetimi değil **durum tutarlılığı**dır.

Pratik sonucu: **yeni bir hasar kaynağı eklemek sıfır sunucu işidir.** Bomba = etkilenen her hedef
için bir `hit_report` (mesafeye göre düşen hasarı istemci hesaplar); yay çekiş gücü, düşme/tuzak
hasarı da aynı şekilde `damage` alanına yazılır. **Kafa vuruşu çarpanı bugün uygulanır:** `Weapon`,
isabet `RemoteHitBox.IsHead` ise hasarı `WeaponDefinition.headshotMultiplier` (vars. 4×) ile çarpıp
öyle bildirir. `weaponId` yalnız kill feed etiketidir, doğrulanmaz.

⚠️ **Dost ateşi kapısı tek bir yardımcıdan geçer** (`MatchDirector.AreTeammates`) ve **boş takım
asla takım arkadaşı sayılmaz**: takımsız modda herkesin takımı `""` olduğu için düz
`a.Team == b.Team` karşılaştırması `"" == ""` ile TÜM vuruşları reddederdi. Kapının açık/kapalı
olmasını ise **operatör** belirler (`set_friendly_fire`, §3.9) — `ModeRules.FriendlyFire` yürürlükteki
anahtarı taşır, açıkken kapı hiç uygulanmaz.

⚠️ **Dost ateşiyle gelen takımdaş öldürme skor YAZMAZ:** öldürücü vuruş takım arkadaşınaysa
`IGameMode.OnKill` hiç çağrılmaz, yani ne takım skoru ne bireysel skor işler. `kills`/`deaths` ve
kill feed satırı normal işler — olay gerçekleşti, yalnız ödülü yok; ceza (−1) bilinçli olarak yok.
Kapı modun içine değil **çağrı yerine** konur: skorun tek yazarı `OnKill` olduğu için kural tek
noktada durur ve her yeni mod ona kendiliğinden uyar.

### 3.7 Free-roam canlanma (respawn) — ürünün en özel kuralı

Fiziksel oyuncu **ışınlanamaz**. Bu yüzden respawn bir **konum değil, DURUM değişimidir**:

```
ölüm → respawn{delaySeconds} → ölüm ekranı, ateş yok, avatar yarı saydam
     → süre dolar VE MODUN CANLANMA ŞARTI sağlanır → revive_request (~1 sn'de bir tekrar)
     → sunucu doğrular → health_update{hp:100} → canlı
     → talep 20 sn (REVIVE_GRACE) gelmezse sunucu ZORLA canlandırır (maç kilitlenmesin)
```

Şart `ModeRules.Revive`'dan gelir (§3.9) ve istemcide `ModeRuntime.Revive` olarak okunur —
`PlayerCombatState` içinde mod adına bakan hiçbir dal yoktur:

| `reviveAnchor` | Şart | Ölüm ekranı |
|---|---|---|
| `base` (varsayılan, TDM) | Oyuncu bir **taban bölgesine** (`BaseZone` — arenadaki kırmızı/mavi şerit) fiziken girer | "Tabanına dön ve canlan" |
| `standstill` | Ölüm anındaki HMD çapasından `REVIVE_HOLD_RADIUS` (1 m) içinde `REVIVE_HOLD_SECONDS` (3 sn) kesintisiz durur | "Canlanmak için sabit dur — N sn" |
| `none` (turnuva) | **Canlanma yok.** İstemci `revive_request` hiç göndermez, sunucu gelirse reddeder ve `REVIVE_GRACE` çalışmaz. Ölü oyuncuyu yalnız modun başlattığı yeni tur canlandırır — ve yeni tur **herkes tabanına dönene kadar** açılmaz, yani ölü kalma süresinin üst sınırı yoktur (§3.8.2) | "Elendin — takımın turu bitirene kadar bekle" |

⚠️ **Bir canlanma yasağı, canlandırmanın İKİ yolunu birden kapatmadıkça yoktur.** Yollar ayrıdır:
talep tabanlı (`revive_request` → `HandleReviveRequestAsync`) ve zamanlayıcı tabanlı
(`REVIVE_GRACE` → `TickLiveLocked`). Yalnız birini kapatmak oyuncuyu 20 sn sonra yine canlandırır.
Bugün iki yasak var ve ikisi de her iki yolu kapatıyor: **kalibrasyon** (§3.11) ve
**`reviveAnchor:"none"`**.

**Taban bölgesi eşleşmesi:** bölge oyuncuya açıktır eğer takımı aynıysa, bölge `Neutral` ise
(herkese açık joker) ya da oyuncunun takımı boşsa (takımsız mod). Aynı takımdan birden çok bölge
varsa **herhangi birine** girmek yeter. Kapalı bileşen açık sayılmaz — `BaseZone.Update` koşmadığı
için `IsPlayerInside` donar, açık sayılsaydı oyuncu bölgeye girse de yalnız `REVIVE_GRACE`'i
beklerdi.

⚠️ **Şartı SUNUCU doğrulamaz** (§10.3 felsefesi: hakemlik değil defter tutar) — karar istemcinindir,
sunucu faz + ölü + gecikme kontrolüyle yetinir. Şart ölçülemiyorsa (sahnede açık taban bölgesi yok,
kamera yok) istemci onu sağlanmış sayar: bu sınıf hiçbir koşulda oyuncuyu kalıcı ölü bırakmaz.

⚠️ **Kod kuralı:** hiçbir bileşen rig'i/kamerayı taşımaz — ne canlanmada, ne harita değişiminde.
Protokolde konum/slot taşıyan bir alan **yoktur**; sunucu sahne geometrisini bilmez. Arena başına
sahnedeki tek `SpawnPoint` maç öncesi yerleşim göstergesidir ve oyuncuyu hiçbir yere taşımaz —
ama arena uzayının **sıfırıdır** (§3.3), bu yüzden bir kez yerleştirilir ve sonra oynatılmaz
(`GameObject > VortexArena > Spawn Point` ile eklenir, elle yerleştirilir).

⚠️ **Harita değişimi kalibrasyonu sıfırlamaz.** `load_match` oyuncu için yalnız bir sahne
değişimidir: kimse "yeniden doğmaz". Yeni sahnenin `ArenaCalibrator`'ı `Start`'ta kayıtlı
`OVRSpatialAnchor`'dan hizalamayı geri yükler (yükleme geçici düşerse 3 kez denenir). Ön koşul:
**aynı işletmede oynanan arenaların zemin işaretleri aynı yerde olmalı** — anchor fiziksel dünyada
sabittir, sanal işaretler sahneden gelir.

⚠️ **Poz gönderimi kalibrasyonu BEKLEMEZ.** `PlayerPoseTracker` rig anchor'larını bulur bulmaz
`IPoseSource` olarak kaydolur; hizalama gelmeden gönderilen pozlar arena ile örtüşmez (rig henüz
hizalanmadığı için ofsetlidir) ama gönderilir — oyuncunun bağlı ve hareket hâlinde olduğu ağdan
görülebilsin diye. Hizalama oturunca aynı kaynak kendiliğinden doğru uzayda poz verir; yeniden
kaydolma yoktur. Sonucu: uzak avatarlarda kalibrasyon öncesi konum
**kaymış görünür**, bu bir hata değildir.

### 3.8 Maçın durumu (sunucuda) — dört alan, dört sahip

Durum tek bir enum değildir. Dört alan taşınır ve her birinin **tek** sahibi vardır:

| Alan | Sahibi | Değerler | Anlamı |
|---|---|---|---|
| `modeId` | operatör seçimi | `lobby` · `tdm` · `ffa` · `tournament` | Ne oynanıyor. **Lobi de bir türdür** (§3.8.1) |
| `phase` | çekirdek | `paused` · `playing` · `finished` | Maçın genel durumu |
| `phaseReason` | çekirdek | `lobby` · `loading` · `countdown` · `operator` · `mode` | Neden duraklı |
| `modeState` | mod (`IGameMode`) | serbest string | Modun kendi ara durumu; çekirdek yorumlamaz |

```
              start_match                 herkes set_ready | 20 sn
paused ─────────────────────► paused ──────────────────────────────► paused
(lobby)                       (loading)                              (countdown 5 sn)
   ▲                                                                    │
   │ return_to_lobby                                                    ▼
   └──────────── finished ◄──── süre bitti | skor limiti ◄───────────  playing
        (operatör seçer; ~16,5 dk emniyet)                            ▲    │
                                                                      └────┘ duraklat / devam
                                                                  paused(operator|mode)
```

⚠️ **Kazanan ekranı kendiliğinden kapanmaz — operatörü bekler.** `finished` fazından çıkaran şey
fazı değiştiren her komuttur: harita seçmek (ya da harita listesindeki lobi satırı), `start_match`,
`abort_match`/`return_to_lobby`. `MATCH_END_SECONDS` (999 sn) bunların hiçbiri gelmezse devreye
giren **emniyettir, akış değil**: turnuvada tur/maç aralarını sahada hakem yönetir ve 10 sn'lik
otomatik dönüş kazanan ekranını hakemin elinden alıyordu.

⚠️ **`phase`'in tek yetkisi hasar kapısıdır:** `hit_report` yalnız `playing`'de işlenir. Başka
hiçbir kural doğrudan faza bakmaz — "ateş edebilir miyim", "silahım nereden gelir", "hangi HUD"
sorularının cevabı **moddan** gelir (§3.9). Sebebi ileriye dönük: turnuva gibi kendi ara durumu
olan bir mod, çekirdek enum'unu büyütmek yerine `phaseReason:"mode"` + `modeState` kullanır.

⚠️ **`modeState` asla kural/hasar kapısı olamaz.** Çekirdek onu okumaz, yalnız HUD okur. Mod
duraklatmak isterse çekirdekten `paused` + `phaseReason:"mode"` ister, gerekçesini `modeState`'e
yazar; operatör duraklatması (`operator`) ile karışmaz, o yüzden mod kaldığı yerden sürebilir.

**Operatör duraklatması** `pause_match` / `resume_match` ile gelir (§5.2). Duraklatmada süre
kendiliğinden durur (sayaç yalnız `playing` tikinde azalıyor), hasar kapanır, skor/can/`modeState`
ellenmez. ⚠️ **`resume_match` yalnız `phaseReason == "operator"` iken kabul edilir** — her
duraklamayı kendi sahibi kaldırır: modun istediği duraklamayı operatörün kaldırması modun ara
durumunu bozar, geri sayımı elle bitirmek yükleme kapısını atlar. Duraklatmak maçtan çıkmak
değildir; çıkış hâlâ `abort_match`/`return_to_lobby`'dir ve ikisi duraklı maçta da çalışır.

`start_match` doğrulaması (sırayla): mod kayıtlı mı → sahne adı boş değil → sahne `maps.json`'da
var ve modu destekliyor mu (**tablo boşsa bu adım atlanır**) → sahne TÜM oyuncuların
`hello.scenes` listesinde. Geçerse takımlar **modun şekline göre** kurulur (takımlıda dengelenir,
takımsızda temizlenir) ve herkese **kişisel** `load_match` (kendi `yourTeam` + maçın `rules`'ü) gider.

**Süre, skor limiti ve geri sayım o maça özel olabilir:**
`start_match.roundSeconds`/`scoreLimit`/`countdownSeconds` doluysa onlar koşar, boş/`0` ise modun
(geri sayımda protokolün) varsayılanı. Yani `ModeDefinition`/`IGameMode` sayıları **kilit değil
varsayılandır** — operatör raundu kısaltıp uzatabilir. Seçim mod/harita ile aynı ortak kanaldan
gider (`set_selection` → `admin_state`), çünkü parametreler yerel kalsaydı bir operatörün 5 dk
sandığı maç diğerinin seçtiği 30 dk ile başlardı. `countdownSeconds` sunucuda 5–30 sn aralığına
kırpılır ve maçın **her** geri sayımında kullanılır — tur tabanlı modda turlar arasındaki bekleme
de odur (§3.8.2).

### 3.8.2 Tur tabanlı modlar — çekirdek TUR diye bir şey bilmez

`tournament` bir maçı **turlara** böler ama `Phase` enum'u bunun için büyümedi. Turun tamamı
`TournamentMode` içinde yaşar; çekirdek yalnız dört yetenek sunar ve hiçbirini yorumlamaz:

| Çekirdek API | İş |
|---|---|
| `TryPauseForMode(modeState)` | `playing` → `paused`/`mode`; süre 0'lanır, `ready` bayrakları temizlenir |
| `SetModeState(modeState)` | Ara durumu yazar, **yalnız değiştiyse** `match_state` yayınlar |
| `TryStartRound()` | `paused`/`mode` → geri sayım → `playing` (çekirdeğin normal yolu) |
| `TryCancelCountdownForMode(modeState)` | Geri sayımı geri alır: `paused`/`countdown` → `paused`/`mode` |

Akış: tur `playing`'de koşar → mod turu bitirir (eleme ya da süre) → maç bitmediyse
`paused`/`mode` + `modeState:"regroup:2/6"` → herkes kendi tabanına yürüyüp `set_ready{true}`
yollar → geri sayım → yeni tur. **Geri sayım her koşulda geri alınabilir:** biri tabanından çıkıp
bayrağını düşürürse mod geri sayımı iptal eder ve toplanmaya döner; istisnası yoktur. Kural
"tabanda **bekle**"dir, "tabana uğra" değil — şart girişte bir kez değil, tur açılana kadar
**sürekli** ölçülür.

**Toplanma zaman aşımı YOKTUR: tur, herkes kendi tabanına girmeden başlamaz.** Eksik oyuncuyla
zorla başlatılan bir tur turnuvada hakemin istemediği bir turdur; beklemenin çıkışı da zaten
operatördedir — takılan oyuncuyu **atar** (`kick`) ya da `abort_match` yapar. Atılan/kopan oyuncu
toplamdan düştüğü için kalanlar hazırsa tur o an başlar: sayım her tikte çevrimiçi oyunculardan
yeniden yapılır, "kaç kişiydik" diye bir kayıt tutulmaz. Bekleme uzarsa sunucu konsoluna 30 sn'de
bir *"toplanma bekleniyor (4/6) — tabanına dönmeyenler: …"* satırı düşer; bu bir **teşhis**
satırıdır, tur başlatmaz.

Dört şey bunu mümkün kılıyor ve ilk üçü **zaten vardı**:

1. **`phaseReason:"mode"`** — duraklamanın sahibi mod. `resume_match` onu kaldıramaz (§3.8), yani
   operatör modun ara durumunu kazara bozamaz.
2. **`set_ready` bayrağı** — yükleme kapısında zaten "hazırım" demek. Toplanma kapısı onu yeniden
   kullanır; **yeni protokol mesajı yok**. "Tabanda mıyım" kararı istemcinindir (§3.7 felsefesi).
3. **Mod tik'i duraklamada da koşar** — `paused`/`mode` **ve** `paused`/`countdown`'da. Operatör
   duraklatmasında (`operator`) mod tik ALMAZ: donmuş maç donmuş kalır.
4. **Mod kendi aşamasını hatırlar** (`RoundStage`). `phaseReason` yetmiyor: maçın ilk geri sayımı
   da `paused`/`countdown`'dur ama toplanmadan gelmez, orada iptal edilecek bir şey yoktur.
   Aynı ayrımı istemci de kendi tarafında yapar (raporlayıcı yalnız toplanmadan gelen geri sayımda
   çalışır). Çekirdeğin `PauseReason`'ını dışarı açmak ikinci bir okuma yolu olurdu.

⚠️ **`TryStartRound` `ready` bayraklarını TEMİZLEMEZ.** Toplanmada o bayrak "şu anda tabanımdayım"
demektir ve geri sayım boyunca canlı kalması gerekir — iptal kararının tek dayanağı odur.
Temizleyen tek yer toplanmanın **başıdır** (`TryPauseForMode`).

⚠️ **Tur başında sunucu herkese `health_update` yollar** (`RevivePlayerLocked`). Sunucu içi
alanları sessizce sıfırlamak yetmez: maç içi tur geçişinde `load_match` yoktur, yani istemcinin
kendini sıfırlayacağı ikinci bir yol da yoktur — mesaj gitmezse tur içinde ölmüş oyuncu ölüm
ekranında donar.

⚠️ **Mod kancaları `await` edemez** (`OnTick` `void`). Bu yüzden yukarıdaki üç API mesajı doğrudan
göndermez: kilit altında bir bekleyen kutuya yazar, tik döngüsü kanca dönüşünde yollar. Tek
gönderici → sıra korunur, kilit altında gönderim olmaz.

### 3.8.1 Lobi — bir FAZ değil, bir TÜR

Lobi "hiçbir şey olmuyor" durumu değil, **işletmenin kendi odası**dır. Maç koşmadığı sürece
oyuncular ve admin orada durur: birbirlerini görürler, **kalibrasyonlarını orada yaparlar**
(harita değişimi kalibrasyonu sıfırlamadığı için maça hazır girilir), silah çerçevesinden silah
seçip hedeflere ateş edebilirler.

- **Lobi bir türdür** (`modeId:"lobby"`), faz `paused` + `phaseReason:"lobby"`dir. Tür yalnız lobi
  haritasında olur ve o türdeyken **maç başlatılamaz** — ikisi de `maps.json`'daki `modes`
  alanından gelir (`supportedModeIds == ["lobby"]`), ayrıca bir kural yazılmaz.
- **Sunucunun her zaman bir açık sahnesi vardır** ve istemcinin tek yönlendirme kaynağı odur:
  `welcome.match.sceneName` / `return_to_lobby.sceneName`. Açılışta bu, mekanın lobi haritasıdır
  (`server.json → lobbyScene`, boşsa otomatik bulunur). ⚠️ **Çözülemezse sunucu açılmaz** — sessizce
  boş sahneyle açılmak hatayı sahaya taşırdı.
  **Adminlerin ortak seçimi de (`admin_state`) aynı değerle tohumlanır**, yani sunucu ayaktayken
  "harita seçilmemiş" diye bir durum yoktur. Lobi mod/harita seçicilerinde bulunmadığı için panel
  imleci yerinde kalır ve o an açık olanı MAÇ bölümünün başlığı yazar — imleç "bir sonraki maçın
  adayı", açık sahne "şu an yüklü olan"dır; ikisi maç sonrası ayrışır (herkes lobiye döner,
  imleç son arenada kalır).
- **Operatör arena "sahneler":** admin panelinden harita seçmek `set_selection` gönderir, sunucu da
  o arenayı `return_to_lobby` ile **tüm istemcilere** yükletir (`MatchDirector.StageSceneAsync`).
  Oyuncular maç başlamadan arenaya girip kalibrasyonunu yapar ve yerini alır. Faz `paused` kalır,
  tür `lobby` kalır; doğrulama `start_match` ile aynıdır (sahne tabloda + her oyuncunun build
  listesinde). ⚠️ **Koşan maçta olmaz** — sahne komutu herkese gittiği için maçın ortasında harita
  değiştirmek maçı bozardı; `finished` iken ise serbesttir (operatör bir sonrakini seçebilsin).
- **Oyuncuya hasar imkânsızdır:** `hit_report` yalnız `playing` fazında işlenir. Hasarı kapatan şey
  bir kural bayrağı değil, **fazın kendisidir**.
- **Atış görünür:** `shot_fired` relay'inin kapısı `playing` **veya** `rules.fireWhilePaused`'dur.
  İki kapı bilerek ayrı: atış bir sunum olayı, vuruş bir durum değişimidir. Yani **hasarı faz,
  ateşi mod** kapatır — lobiyi "hasarsız atış alanı" yapan tam olarak bu ayrım.
- **Taban şeritleri seçili moda göre görünür/gizlenir** (`selection_state.teamMode` → `ModeSelection`
  → `BaseZoneVisibility`): admin TDM/turnuva seçtiyse şeritler durur, FFA seçtiyse maç başlamadan
  kaybolur. Aktif kural bu sırada hâlâ lobi profilidir — değişen yalnız sunumdur. Kapının silah
  kaynağı OLMAMASININ sebebi de bu: lobide silah her hâlükârda rastgeledir.
- **Silah gelir** çünkü `modeId` boş değil `"lobby"`dir — istemci loadout'u
  `GameCatalog.FindMode(ModeRuntime.ModeId)` ile çözüyor. Lobinin kaynağı
  `weaponSource:"random"`: grip'e basılı tutulan elde rastgele bir silah durur, bırakınca yok olur.
  `ModeRules.LobbyProfile` bilinçli olarak `RandomGrant` taşır — `WeaponCanvas` seçilseydi her
  lobi sahnesine elle silah yerleştirmek gerekirdi.
- **Takımı yalnız admin atar** (`set_team`), üstelik **her fazda** — koşan maçın ortasında da.
  Oyuncunun kendi takımını seçmesi için protokol mesajı yoktur.

> ⚠️ **Lobi bir maç DEĞİLDİR ve yapılmayacaktır.** `playing`'e taşınsaydı hasar kapısı açılır,
> ayrıca yükleme/geri sayım/tur sayacı/`finished` yaşam döngüsü ve `return_to_lobby`'nin kendini
> çağırması gibi lobide karşılığı olmayan bir makine devralınırdı. "Maç koşuyor mu?" sorusunun tek
> cevabı `phase == playing`'dir. Ayrıntı: `Docs/ArenaNet-Protokol.md` §10.7.

### 3.9 Mod kuralları (`ModeRules`) — modun şekli

Bir modun "ne tür bir oyun olduğu" **sunucu-otoriter** bir şekil tanımıyla anlatılır ve telden
gelir. Amaç tek: **istemci modun ne olduğunu TAHMİN ETMESİN.**

```
IGameMode.Rules ─┐
                 ├─► MatchDirector ─►  load_match.rules / welcome.match.rules
dost ateşi ──────┘   (ApplyRulesLocked) rules_update  (maç ORTASINDA değişirse)
anahtarı (operatör)                          │
                                             ▼
                                          ModeRuntime (istemcide TEK okuma noktası)
                                          ├─ PlayerCombatState  (canlanma şartı, gecikme)
                                          ├─ ModeHudBase        (skor satırı alt sınıfta)
                                          └─ AdminRoster        (tek kolon mu, çift kolon mu)
```

| Kural | Değerler | Varsayılan | Ne değişir |
|---|---|---|---|
| `Teams` | `TwoTeams` / `None` | `TwoTeams` | Sunucu: takımları dengele mi temizle mi. İstemci: avatar rengi, admin kolonu, taban bölgesi eşleşmesi |
| `Scoring` | `Team` / `Player` | `Team` | Skor `match_state.scoreRed/Blue`'ya mı `PlayerInfo.score`'a mı yazılır |
| `FriendlyFire` | bool | `false` | `hit_report` dost ateşi kapısı. ⚠️ **Modun değil operatörün alanı** — aşağı bak |
| `Revive` | `OwnBase` / `StandStill` / `None` | `OwnBase` | Canlanma şartı (§3.7). `None` = tur içinde canlanma yok |
| `Weapons` | `WeaponCanvas` / `RandomGrant` | `WeaponCanvas` | Silah sahnedeki **çerçevelerden** mi seçilir (ele klonlanır, tükenmez) yoksa mod mu dağıtır. **Yalnız istemci sunumu** — sunucuda karşılığı yok. ⚠️ `WeaponCanvas`'ta silahı sahneye koyan bileşen YOKTUR: yerleşim arena kararıdır, harita tasarlanırken elle konur. FFA ve lobi `RandomGrant` kullanır |
| `RespawnDelay` | saniye | `RESPAWN_DELAY` (5) | `respawn.delaySeconds` + sunucudaki gecikme eşiği |
| `FireWhilePaused` | bool | `false` | Faz `playing` değilken ateş edilebilir mi (§3.8.1). Lobi türünde `true`; **hasar yine yok** — onu faz kapatır. Bu alan sayesinde istemcide `if (modeId == "lobby")` zinciri doğmaz |

**Kayıtlı modlar** (somut örnek — TDM tüm varsayılanları alır, FFA beş alanı, turnuva yalnız
**iki** alanı farklı yazar):

| | `tdm` — Takım Ölüm Maçı | `ffa` — Herkes Tek | `tournament` — Turnuva |
|---|---|---|---|
| `Teams` | `TwoTeams` | **`None`** | `TwoTeams` |
| `Scoring` | `Team` | **`Player`** | `Team` (skor = **kazanılan tur**) |
| `Revive` | `OwnBase` | **`StandStill`** (3 sn / 1 m) | **`None`** (tur içinde canlanma yok) |
| `Weapons` | `WeaponCanvas` (çerçeveden seçilir, kalıcı) | **`RandomGrant`** (grip'e basınca elde rastgele silah) | `WeaponCanvas` (şarjör/yedek şarjör işlesin diye) |
| `RespawnDelay` | `5` | **`0`** (bekleme yerine sabit durma şartı) | **`0`** (canlanma yok, sayaç göstermek yalan olurdu) |
| Süre / limit | 300 sn / 30 | 300 sn / 20 | 120 sn (**turun** süresi) / 4 tur |

⚠️ **Turnuvada `roundSeconds` TURUN süresidir, maçın değil** ve `scoreLimit` maçı kazanmak için
gereken tur sayısıdır (tavan `2 × limit − 1` tur). Tur kavramı `ModeRules`'a **girmez** — turlar
modun iç durumudur (§3.8.2).

- **Varsayılan = bugünkü TDM.** Yeni mod yalnız FARKLI olduğu alanı yazar (`TdmMode.Rules =>
  ModeRules.TeamDefault`).
- **Dost ateşi takımsız modu KİLİTLEMEZ:** boş takım asla takım arkadaşı sayılmaz, FFA'da
  herkesin takımı `""` olduğu için kapı hiç kapanmaz (§3.6).

- **Bilinmeyen/boş değer varsayılana düşer** (değerler bilerek string) → yeni bir kural değeri
  eklemek `PROTOCOL_VERSION`'ı artırmaz.
- **Kurallar telde gelmediğinde** (`rules == null` — kuralları taşımayan bir sunucu) `ModeDefinition`'ın
  önizleme alanları fallback olarak devreye girer; **sapmada sunucu kazanır** — kural taşıyan bir
  `load_match` bunları ezer.
- Tam semantik: `Docs/ArenaNet-Protokol.md` §10.5.

**⚠️ `FriendlyFire` bu tablonun tek istisnasıdır: mod kuralı değil OPERATÖR ANAHTARIDIR.** Değeri
sunucuda yaşar (`MatchDirector`, açılışta kapalı), yalnız admin komutu `set_friendly_fire`
değiştirir ve etkisi anlıktır — koşan maçta da geçerlidir, çünkü sahadaki durumu (takımlar karıştı,
antrenman yapılıyor) operatörün maçı iptal etmeden düzeltebilmesi gerekir. Maç başlangıcı, harita
sahneleme ve lobiye dönüş anahtarı **sıfırlamaz**; sıfırlayan tek şey sunucunun yeniden
başlatılmasıdır (süre/limit seçimiyle aynı sözleşme).

- **Modlar bu alanı bildirmez.** Bir modun kendi kuralında ona değer yazması operatörün anahtarını
  sessizce ezerdi. `ModeRules.FriendlyFire` telde "o an geçerli değer"i taşır, "bu modun tercihi"ni
  değil.
- **Yürürlükteki şekli yazan tek kapı `MatchDirector.ApplyRulesLocked`'tır:** modun (ya da lobinin)
  kural şeklini alır, üstüne anahtarı damgalar.
- **Maç ortasında değişince `rules_update` herkese yayınlanır** ve istemcide `ModeRuntimePump` onu
  `ModeRuntime`'a uygular. `selection_state`'in aksine bu **gerçek bir kural mesajıdır**: o yalnız
  sunuma (taban şeritleri) dokunur, bu aktif kuralı değiştirir. Geç bağlanan doğru değeri yine
  `welcome.match.rules`'tan alır — mesajın kaybı kalıcı sapma üretmez.
- Adminler arası senkron `admin_state.friendlyFire` ile sağlanır: seçim değil **yürürlükteki
  durum** olduğu için diğer alanların "0/boş = değişmedi" sözleşmesine girmez ve panelde maç
  kuruluyken de basılabilir (mod/harita gibi kilitlenmez).

### 3.11 Kalibrasyon durumu — operatörün kaldıracı

Bir başlığın hizalı olup olmadığı **sunucuda** tutulur (`lobby_state.calibrated`) ve sahadaki en
sık sorunu çözer: bir oyuncunun kalibrasyonu kayar, avatarı fiziksel konumundan sapar.

**Akış:** operatör admin ekranında o satırın **KAL** düğmesiyle kalibrasyonu sıfırlar → oyuncu
**ateş edemez, hasar yemez, canlanamaz**, diğer herkesin ekranında **avatarı parlar** ve vuruş
kutuları kapanır → oyuncu elle (ya da kayıtlı anchor'dan) yeniden kalibre olur → gözlük
`set_calibration` yollar → tik geri yanar ve oyuncu **kaldığı yerden devam eder** (can/K-D/skor
korunur, bu bir ceza değil geçici dondurmadır).

**Asimetri kasıtlıdır:** admin yalnız SIFIRLAYABİLİR, "kalibre oldu" diye işaretleyemez —
hizalamanın gerçekten oturduğunu yalnız başlık bilir. Admin elle işaretleyebilseydi, sunucunun
hizalı sandığı ama fiilen kaymış bir oyuncuya ateş ve hasar açılırdı.

**Kalibreliyken elle kalibrasyon kilitlidir:** oyuncu kendi hizalamasını kazara bozamaz, kapıyı yalnız operatör
açar. Hiç bağlanılmamışsa (sunucusuz editör testi) kapı açıktır ve silah çalışır.

⚠️ **Poz gönderimi buna bağlı DEĞİLDİR** — kalibresiz oyuncu poz göndermeye devam eder (§3.5).
Bilinçlidir: operatörün "avatar kaymış" teşhisini koyabilmesi ve parlayan avatarın hareket ettiğini
görebilmesi için pozun akıyor olması gerekir.

⚠️ **Parlamayı çizen liste `RemoteAvatar.bodyRenderers`'tır** — takım rengini taşıyan
`teamRenderers`'tan AYRI tutulur, çünkü takım rengi karakter mesh'ine bilerek uygulanmaz (düşmanı
işaretlemek duvar arkasından okunabilen bir avantaj olurdu). İkisi tek listeye bağlanamaz; liste
boş bırakılırsa **uyarı sessizce hiç çizilmez** ve konumu yalan söyleyen avatar normal görünür —
sahada tam olarak bu yaşandı (§7).

Tam semantik: `Docs/ArenaNet-Protokol.md` §10.6.

### 3.12 Bant, paket ve airtime bütçesi

**Sonuç önce: bant genişliği bu üründe hiçbir zaman darboğaz değil, paket sayısı olabilir.**
Kablosuzda maliyet bayt başına değil **çerçeve başına** ödenir; ürünün trafiği ise "az bayt, çok
çerçeve" desenindedir (20 Hz × hedef sayısı kadar minik datagram).

**Ağ tick'i 20 Hz'dir** (`SNAPSHOT_RATE_HZ` / `POSE_RATE_HZ`). `MatchDirector`'ın 10 Hz maç tick'i
(`TickIntervalMs`) ağa hiçbir şey yazmaz — faz/süre/zorla canlanma çözünürlüğüdür.

**Hesap formülleri** (`N` = pozlu oyuncu, `A` = admin, hedef sayısı `N+A`; snapshot her hedefe
**ayrı unicast** gider, multicast yoktur):

```
snapshot   (sunucu TX) = SNAPSHOT_RATE_HZ × (6 + 88×N) × (N+A)     ← N² büyür
                         olay varken tek pakette birleşir (0x05, §6.8): (7 + 88×N + 9×E/20) × (N+A)
pose       (sunucu RX) = POSE_RATE_HZ × 95 × N
skeleton   (sunucu RX) = SKELETON_RATE_HZ × (34 + B) × N            ← B = blob boyu
           (sunucu TX) = SKELETON_RATE_HZ × ⌈(31+B)×N / 1200⌉ datagram × (N+A)   ← N² büyür
health_update (sunucu TX, TCP) = isabet/sn × (1 + admin sayısı) × ~140 B   ← N ile büyümez
rttProbe   (her iki yön) = 1 Hz × (N+A) × 2 datagram
```

⚠️ **İskelet kanalı bu bütçenin en duyarlı kalemidir** ve tasarımı ona göredir: oyuncu başına ayrı
datagram yollansaydı `SKELETON_RATE_HZ × N × (N+A)` paket ederdi, batch'leme (§6.10) onu
`⌈(31+B)×N / 1200⌉` ile böler. `B` bilinmeden hesap yapılmaz — **ölçülür** (sunucu konsolundaki
`[state]` satırında `iskelet … p/s`). Kanal ayrıca poz kanalından **daha düşük hızda** akar; oradaki
sayı bir akıcılık ayarı değildir, alıcıda SDK'nın kendi interpolasyonu koşuyor.

Snapshot'ın `N²` olmasının sebebi bilinçli: her oyuncu diğerlerinin pozunu **ve kendi pozunu**
alır (kendi pozunu yok sayar — §3.5). Kazancı hedef başına serileştirme yapmamaktır.
`health_update`'in `N` ile büyümemesi ise sonradan kazanıldı (aşağıya bak).

**Örnek: 10 oyuncu + 1 admin, aktif çatışma** (10 oyuncu da ~600 RPM = 100 atış/sn toplam,
~%25 isabet). IP+UDP/TCP başlıkları dahil, 802.11 çerçevelemesi hariç. ⚠️ **Hesaplanmış
değerlerdir** — sunucu konsolundaki `[state]` satırı bunları artık ölçüyor; sapma olursa doğru olan
ölçümdür:

| Kanal | Yön | Bant | paket/sn | (önce) |
|---|---|---|---|---|
| `0x05` snapshot+olay birleşik (932 B/tik/hedef) | ↓ | 1,69 Mbps | 220 | 440 |
| WS `health_update` + `kill_event` | ↓ | 0,06 Mbps | 50 | 275 |
| WS `match_state` (1 Hz) | ↓ | 0,02 Mbps | 11 | 11 |
| `0x06` RTT echo | ↓ | ~0 | 11 | — |
| WS `net_stats` (1 Hz, admin) | ↓ | ~0 | 1 | — |
| `0x01` poz | ↑ | 0,20 Mbps | 200 | 200 |
| `0x03` atış | ↑ | 0,03 Mbps | 100 | 100 |
| WS `hit_report` | ↑ | 0,03 Mbps | 25 | 25 |
| `0x06` RTT yoklaması | ↑ | ~0 | 11 | — |
| **Toplam** | | **~2,0 Mbps** | **~630** | **~1.050** |

Sakin durumda (lobi, atış yok) `0x02` + poz + yoklama kalır: **~1,8 Mbps, ~450 paket/sn**.

**1 Gbps'lik bir AP'ye göre bant %0,2'dir.** Buna karşılık ~730 çerçeve/sn (TCP ACK'leri dahil),
802.11ax'te küçük unicast çerçeve başına ~120–200 µs havayı tuttuğu için **tek radyoda ~%10–12
airtime** demektir (fan-out kesilmeden önce ~%15–20 idi). Yani darboğaz bant değil airtime;
konforlu üst sınır kabaca **2.500–3.500 çerçeve/sn**, bugünün ~4–5 katı. (AP'nin DL/UL OFDMA'sı
çalışıyorsa bu tavan belirgin yükselir — bu iş yükü tam OFDMA'nın tasarım hedefi; ama consumer
AP'lerde garanti sayılmaz.)

**Paket sayısı nasıl düştü** (~1.050 → ~630, %40):

1. **`health_update` broadcast olmaktan çıktı** → kurban + adminler (§10.3). İki tüketicisi de dardı:
   istemci kendisine ait olmayanı **zaten düşürüyordu**, admin ise tabloyu çiziyor. 10 oyunculu
   maçta her isabette 11 mesaj gidip 9'u çöpe atılıyordu. **−225 paket/sn.**
2. **`0x02` + `0x04` tek datagramda birleşti** (`0x05`, §6.8) — sığdığı sürece. **−220 paket/sn.**
3. **RTT yoklaması eklendi** (§6.7): **+22 paket/sn** — bu planın paket EKLEYEN tek parçası, ve
   bilinçli olarak 1 Hz'de tutuluyor.

⚠️ **Sunucu PC kabloyla bağlanmalıdır.** Wi-Fi'daysa her downstream paket havayı iki kez geçer
(sunucu→AP, AP→istemci) → airtime ikiye katlanır. Sahadaki AP/ağ kontrol listesi:
`Docs/Isletme-Kurulum.md` "Ağ" bölümü.

**Ne ölçülüyor** (§6.7): uplink jitter/kayıp ve sunucu tik kayması **sunucuda** (konsol `[state]`
satırı), downlink jitter/kayıp ve RTT **istemcide** (snapshot varışlarından — RTT dışında ek paket
yok) → `status` → `net_stats` → admin panelinde **PING** kolonu. Yön asimetriktir: 802.11'de yukarı
ve aşağı simetrik bozulmaz, bu yüzden iki taraf ayrı ölçülür. Hacim sayıları bilinçli olarak yalnız
konsoldadır, panelde gösterilmez.

⚠️ **Sunucu PC kabloyla bağlanmalıdır.** Wi-Fi'daysa her downstream paket havayı iki kez geçer
(sunucu→AP, AP→istemci) → airtime ikiye katlanır. Sahadaki AP/ağ kontrol listesi:
`Docs/Isletme-Kurulum.md` "Ağ" bölümü.

**Ölçeklendiğinde ilk çarpılacak tavanlar, sırayla:**

1. **Airtime / paket-sn** — yukarıdaki tavan. Bağlayıcı kısıt budur.
2. **MTU 1500** → `SNAPSHOT_MAX_ENTRIES_PER_PACKET = 16` ve `COMBINED_MAX_BYTES = 1200`. Girdi
   büyürse (ör. tam gövde izleme) ikisi de düşer; ayrıca 16 girdiyi aşan tik **birleştirilemez**
   (§6.8) ve o tik'te paket sayısı bugünkü iki katına döner.
3. **`u8 playerCount`** ve `PLAYER_ID_MAX = 255`.
4. **Quest 3 CPU'su** — 20 Hz'de 10 uzak avatarı interpole etmek bedava; 60 Hz tam gövde değil.

Karşılaştırma için: 60 Hz snapshot 10 oyuncuda 4,7 Mbps / 660 paket-sn; 20 oyuncu bugünkü formatta
5,9 Mbps / 420 paket-sn; 32 oyuncu 14,9 Mbps / 1.320 paket-sn (16 girdi sınırı aşıldığı için tik
başına iki datagram **ve birleştirme devre dışı**). **Hiçbiri bant sorunu değil, hepsi airtime
sorunu.**

---

## 4. Bileşen sözlüğü — kim ne yapıyor

### İstemci: `VortexArena.Net` (ağ, oyun bilmez)

| Sınıf | Görevi |
|---|---|
| `ArenaClient` | Kalıcı tekil; WS bağlantısı (arka plan Task + `ConcurrentQueue` → ana thread köprüsü), hello/welcome, status kalp atışı, otomatik reconnect. **Tüm mesaj gönderimi buradan.** Teşhis için `ConnectAttempts` (son başarılı bağlantıdan beri kaçıncı deneme; bağlanınca 0) + `LastError` (son bağlanma hatası) — `ConnectionOverlay` bunları gösterir. `Disconnect()` otomatik yeniden denemeyi **durdurur** (dönüş yalnız açık `Connect` ile) |
| `ServerDiscovery` | Beacon dinleme (Android'de MulticastLock), elle girilen adresin `PlayerPrefs`'e yazılması, `arena.json` fallback |
| `UdpStateChannel` | UDP kaydı (`0x00`), 20 Hz poz + eşya gönderimi (`0x01`), snapshot alımı (`0x02` ve birleşik `0x05`), atış/atma olayı gönderimi (`SendFireEvent` → `0x03`, olay başına, hemen) ve olay bloğu alımı (`0x04`/`0x05`, **tek** `serverTick` halkasıyla kopya ayıklama). Ayrıca **ağ telemetrisini ölçer** (§6.7): 1 Hz RTT yoklaması (`0x06`) + snapshot varışlarından downlink jitter/kayıp; `SampleTelemetry` ile `ArenaClient`'a verir, o da `status`'a yazar |
| `RemotePlayerRegistry` | Snapshot → oyuncu başına halka tampon → `GetInterpolatedPose`, `IsAlive`, `OnRemoteJoined/Left`; ayrıca **eşya durumu** `TryGetHeldItems` (interpole edilmez — ayrık veri, son gelen geçerli) ve **`serverTick` → yerel oynatma zamanı** eşlemesi `TryGetPlaybackTimeMs` (§3.5b). ⚠️ Tik eşlemesi **global** bir halkada durur, oyuncu başına halkada değil: tik başına bir snapshot var ve hiç pozu olmayan bir oyuncunun olayı da zamanlanabilmeli. Damga `playerCount = 0` snapshot'ında da yazılır (o da meşru bir yayın) ve parçalanmış snapshot'ta yalnız İLK parçadan alınır |
| `NetEvents` | **Statik olay merkezi** — sunucu mesajları buradan ana thread'de yayınlanır (`OnRemoteFireEvent` dahil) |
| `RemoteFireEvent` | Uzak atış/atma olayının istemci-içi taşıyıcısı: `kind`, `rightHand`, `itemId`, arena-uzayı yönü, `magnitude` (atışta mesafe m / atmada hız m·sn⁻¹), `serverTick` |
| `IPoseSource` | 20 Hz döngüye arena-uzayı pozu **ve elde tutulan eşya baytlarını** sağlayan arayüz (`GetHeldItems`) — Net katmanı Core'u göremediği için eşya bilgisi buradan sızar |
| `NetIdentity` / `NetSpawnCatalog` | Sahne objesi kimliği (`sceneId`) ve id→prefab kataloğu — **dinamik obje senkronu altyapısı** (v1'de oyuncu senkronu playerId ile gider) |

### İstemci: `VortexArena.App` (akış ve köprüler)

| Sınıf | Görevi |
|---|---|
| `AppBoot` | Rol çözümü: Android → player; masaüstü → `--role` > `VORTEX_ROLE` > admin. **Sahne her rolde `Lobby`'dir** (admin'in ayrı kabuğu yok). **Adres çözümü:** `--server-ip` / `--server-port`'u **her rolde** okuyup `AppSession`'a yazar (player'da keşif zincirinin en üstü; admin'de tek kaynak — yoksa uyarı loglar). `AppSession.RoleResolved` doluysa hiçbir şey yazmaz → editörde `DevSession` kazanır. **Inspector'da rol/IP override alanı YOKTUR** (kaldırıldı: sahneyi kirletiyordu) |
| `SceneRouter` | `load_match` / `return_to_lobby` / geç katılım → sahne yükleme. **Rolden bağımsız** (admin de oyuncuların sahnesine gider); rol yalnız TEK yerde ayrışır — `set_ready` sadece player'dan gider (admin "hazır" görünmemeli). **Lobi sahnesi de sunucudan gelir** (§3.8.1): `LobbyScene` alanı `return_to_lobby`/`welcome`'dan beslenir, sahne bu build'in listesinde yoksa kabuk `Lobby`'ye düşer ve sebebini loglar. Lobi bir maç sahnesi olmadığı için `LastMatchScene` boş kalır → `set_ready` gönderilmez. **Yükleme asenkrondur** (`LoadSceneAsync`): geçiş boyunca oyun döngüsü aktığı için `LoadingOverlay` çizilebilir ve ilerleme gösterilebilir; `set_ready` kapısı DEĞİŞMEZ — `sceneLoaded` aktivasyon sırasında tetiklenir ve bildirim yine oradan gider (tek kapı). ⚠️ Asenkron yükleme **iptal edilemez**: yükleme sürerken gelen yeni hedef (ör. maç ortasında `load_match`) **sıraya alınır** ve mevcut yükleme biter bitmez yüklenir — hedef sessizce düşürülmez. ⚠️ Geçiş boyunca `Application.backgroundLoadingPriority` **`High`'a çekilir ve sonra eski değerine geri konur**: aksi hâlde asenkron yükleme senkrondan gözle görülür biçimde yavaştır (Tuzaklar: "`LoadSceneAsync` varsayılan ayarla yavaştır") |
| `LobbyController` | VR lobi: roster, ready/takım + otomatik bağlanma; **gizli IP paneli** (varsayılan kapalı, sağ kumandada `OVRInput.Button.PrimaryThumbstick` **1 sn basılı tutularak** açılır/kapanır — beacon'ı kesen ağlar için kurtarma yolu; jest tetiklendiğinde kumanda titrer). Admin de bu sahneden bağlanır (`Connect(..., AppSession.Role)`); world-space paneli admin'de `AdminSpectator` gizler |
| `UiKit` | **Arayüz paleti + çalışma zamanı yardımcıları** (statik). Arayüz prefaba taşındıktan sonra geriye kalan iş: renk paleti (durum renkleri — HP eşikleri, seçim vurgusu, kalibresiz kenarlık, bağlantı noktası), `TeamColor`/`Dim`/`WithAlpha`, dinamik yerleşim (`Block` — havuzlanan satırların konumu), `SetBarFill` ve **EventSystem garantisi** (`EnsureEventSystem`/`TakeOverEventSystem`: arena sahnelerinde EventSystem YOK, edilmezse HUD düğmeleri sessizce ölür). ⚠️ Öge fabrikaları (`Panel`/`Button`/`Text`…) hâlâ durur ama **yeni arayüz onlarla kurulmaz** — görünüm prefabta yaşar |
| `ConnectionOverlay` | **Bağlantı hata ekranı** — kalıcı tekil, `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` ile kendini önyükler ve **görünümü prefabtan alır**: `Resources/UI/ConnectionOverlayScreen` (masaüstü) ya da `…World` (VR), seçimi XR aygıtı/platform belirler. Prefab sahneye KONMAZ (yeni arena eklerken unutulacak adım doğmasın). ~3 sn **grace** (anlık kopmada yanıp sönmesin; açılışı da maç ortasındaki kopmayı da kapsar). İki durum: adres biliniyor → "SUNUCUYA BAĞLANILAMIYOR" + adres + `N sn · M. deneme` + son hata; adres yok → "SUNUCU BULUNAMADI". Rol'e göre ipucu (player: joystick 1 sn / admin: launcher). Masaüstü varyantı: screen-space + scrim + **"Yeniden Bağlan"** (adres yoksa devre dışı; `Disconnect()` otomatik denemeyi durdurduğu için tek kurtarma yolu). VR varyantı: world-space kart + `HudFollow`, scrim YOK, **düğme YOK** (o yüzden `_reconnectButton` alanı orada boştur — normaldir). ⚠️ `ArenaBoundary.IsOutOfBounds` iken **tamamen gizlenir** — alan-dışı uyarısı her zaman baskın |
| `LoadingOverlay` | **Sahne geçişi yükleme ekranı** — `ConnectionOverlay` ile birebir aynı desen: kalıcı tekil, `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` ile kendini önyükler, görünümü prefabtan alır (`Resources/UI/LoadingOverlayScreen` masaüstü / `…World` VR, seçimi XR aygıtı/platform belirler), prefab sahneye KONMAZ. **Açılışta önyüklenir, ilk gösterimde değil:** prefabı tam geçiş anında `Resources.Load` etmek yüklemenin en kötü karesinde takılma üretirdi. Yalnız `SceneRouter` sürer: `Show(sceneName)` / `SetProgress(0..1)` / `Hide()`. Bar dolumu `UiKit.SetBarFill` desenidir (`anchorMax.x`) ve hedefe **yumuşayarak** gider — yükleme kareleri düzensizdir, ham `progress` sıçrardı. Başlık ve ipucu metinleri prefabta SABİTTİR; kod yalnız sahne adını, yüzdeyi ve barı yazar. ⚠️ VR varyantında **scrim YOKTUR** (`ConnectionOverlay` ile aynı güvenlik gerekçesi: free-roam'da görüşü karartmak tehlikeli). ⚠️ **World-space kart kamera bulunana kadar ÇİZİLMEZ** ve kamera değişince `HudFollow` yeniden başlatılır (Tuzaklar: "dünya-uzayı overlay'i kamera yokken çizme") — sahne geçişi tam da kameranın öldüğü/doğduğu andır, bu kapı olmadan ekran masaüstünde görünüp VR'da hiç çizilmez. Prefab bulunamazsa hata loglanır ve ekran hiç çizilmez — **sahne geçişi yine de tamamlanır** |
| `DevSession` | **Yalnız editör** (dosyanın tamamı `#if UNITY_EDITOR`): dev penceresinin `EditorPrefs` seçimini Play'e uygular. (a) `BeforeSceneLoad` → rol + adres `AppSession`'a, `RoleResolved = true`; (b) `AfterSceneLoad` → "Açık sahneden" kipinde ve aktif sahne bir ARENA sahnesiyse, bir kare sonra **sunucuya bağlanır**. **Bağlanmayı neden o üstleniyor:** `Connect` normalde kabuk controller'larından gelir, arena sahnelerinde onlar YOKTUR — bağlanmazsa can/skor/faz gelmez ve `CanFire` hiç açılmaz. Takım/mod/süre/limit/faz **yalnız sunucudan** gelir: `welcome.match` geç-katılım senkronu ya da gerçek `load_match`; sunucuda maç koşmuyorsa istemci maç verisi almaz ve bir **admin** maçı başlatmalıdır. **Sandbox kipinde bağlanmaz:** adresi siler (tek başarılı bağlantı `_hasEverConnected`'i kalıcı açar ve kalibrasyon kapısını kapatırdı) ve bağlanmak yerine `ModeRuntime.Apply` ile seçilen `modeId` + `fireWhilePaused = true` yazar, ayrıca `WeaponGranter.SequentialGrant`'i açar — sunucudan gelmiş gibi mesaj **üretmez** (§6.2). Pencerede "Dev enjeksiyonu" kapatılırsa üretim yolu birebir koşar |
| `AppSession` | Oturum: rol + sunucu adresi (`ServerIp`/`ServerPort`/`HasServerEndpoint`) — `AppBoot` yazar, controller'lar okur |
| `PlayerPoseTracker` | *(`VA_PoseSync` prefabı)* Rig anchor'larını bulur, **dünya→arena** çevirip `IPoseSource` olarak kaydolur (kalibrasyon BEKLENMEZ; hizalanana dek pozlar ofsetli gider) |
| `RemotePlayerSpawner` | *(aynı `VA_PoseSync` prefabı)* Katılan/ayrılan uzak oyuncular için `RemoteAvatar` yaratır/yok eder; her `lobby_state`'te ad/takım/**kalibrasyon** bilgisini besler. Ayrıca roster'da KENDİ id'sini bulup yerel takımı çözer ve her avatara **dost mu** olduğunu bildirir (`SetFriendly`) — takımsız modda (FFA) ve admin gözlemcide yerel takım boştur, kimse dost işaretlenmez |
| `ModeHudSpawner` | *(`VA_ModeHud` prefabı)* Aktif modun HUD prefabını katalogdan örnekler — **App, mod assembly'lerini referanslamaz** (prefab yalnız `GameObject` olarak taşınır) |
| `IdentifyOverlay` | Admin `identify` yollayınca o başlıkta büyük kimlik overlay'i. Kalıcı dinleyicidir; kartın kendisi **geçicidir** ve `IdentifyDisplay` prefabından örneklenir (`Resources/UI/IdentifyDisplay`), birkaç saniye sonra yok edilir |
| `IdentifyDisplay` | O kartın **görünümü** (prefab): world-space canvas + `CanvasGroup` (sönme) + yazı. İçeriği `IdentifyOverlay` yazar, biçimi prefabta |
| `KickedShutdown` | `kicked` gelince **oyuncu uygulamasını kapatır** (kendini önyükleyen kalıcı tekil; sahnede işi yoktur). Atmanın karşılığı "lobiye dön" değil "oturumdan çık"tır — başlık açık kalırsa operatör panelde düşmüş ama sahada hâlâ oynayan bir oyuncu görür. ~1,5 sn pay (soketin kapanması + log), editörde Play'i durdurur. ⚠️ **Admin kapanmaz** — operatör penceresi sahadaki tek yönetim aracıdır, yalnız bağlantısız duruma düşer. Kapanış dizisi: `Docs/ArenaNet-Protokol.md` §5.4 |

### İstemci: `VortexArena.App.Admin` (admin gözlemci — masaüstü)

Rol `admin` değilse **hiçbiri çalışmaz** (`AdminSpectator` kendini yok eder); Quest build'inde ölü koddur.

> ⚠️ **Arayüzün görünümü bu sınıflarda DEĞİL, prefablarda yaşar** (`_Shared/App/Resources/UI/`).
> Bu sınıflar yalnız veri yazar (metin, renk, görünürlük, dinamik konum); yerleşim/punto/sprite
> elle düzenlenir. Prefablar **sahneye konmaz**, `Resources.Load` ile yüklenir — yeni arena
> eklerken kurulum adımı doğmasın diye. Alanlar `[SerializeField]`; prefabta bağlanmayan alan
> **hata vermeden sessizce çizilmez**, bu yüzden öge silinmez (gizlenecekse devre dışı bırakılır).
> Düğmelerin `onClick`'i prefabta **boştur** ve doldurulmamalıdır: geri çağrıların çoğu koşulludur
> (iki adımlı onay, kilitli satır, faza göre değişen komut) ve kalıcı bir inspector kaydı o
> koşulları atlar. → `Docs/Gelistirici/Arayuz-Tasarimi.md`

> **Çoklu admin desteklenir ve sınırsızdır** (aynı PC'de birden çok pencere dahil — admin `deviceId`'si
> oturumluktur, §5.2 protokol). Hepsi **eş yetkilidir**; ayrım şudur: **operasyonel durum ortaktır**
> (mod/harita seçimi sunucuda yaşar, `set_selection`/`admin_state` ile senkronlanır → `AdminSelection`),
> **görünüm tercihleri yereldir** (kamera kipi, seçili oyuncu, halkalar, saydamlıklar → `AdminSession`,
> `PlayerPrefs`). Her admin eylemi diğerlerinin HUD'ında "kim ne yaptı" satırı olarak belirir.

| Sınıf | Görevi |
|---|---|
| `AdminSpectator` | Gözlemcinin kökü: kendini önyükler (`AfterSceneLoad` + `DontDestroyOnLoad`), rol çözülünce etkinleşir, kamerayı/HUD'ı/işaretçileri yaratır ve **her `sceneLoaded`'da sahneyi devralır**: `VA_CameraRig` kökünü kapatır (üç kamerası da `MainCamera` etiketli → `Camera.main` belirsiz kalırdı), `ArenaCalibrator` + `BaseZone`'ları kapatır, **`ArenaBoundary`'yi KAPATMADAN** `SetSpectatorMode(true)` ile susturur, world-space canvas'ları gizler, EventSystem'i devralır. Kısayollar: `1/2/3` kip · `Tab` sonraki oyuncu · `F` POV · `P`/`I` panel · `Esc` kapat |
| `AdminSpectatorCamera` | Üç kip: **POV** (seçili oyuncunun baş pozu; poz yoksa son konumda kalır) · **Serbest** (WASD + Q/E + **sağ tuş basılı** fare bakışı, Shift ×3, tekerlek hız; imleç KİLİTLENMEZ → HUD tıklanabilir kalır) · **Kuş bakışı** (ortografik, arena yaw'ına hizalı; kadrajın **tek kaynağı** sahnedeki `ArenaBoundary` — ölçü `HalfExtents`, merkez `LocalCenter`'dan gelir (yamuk arenada kutunun ortası transformun üstüne düşmez), varsayılan ölçü YOKTUR — sınır bulunamazsa kamera dünya origin'inin üstünde kalır, ölçü değişmez ve konsola sahne başına bir uyarı düşer (lobide susar); tekerlek zoom). Kip değişiminde `AdminSpectator.RefreshRoof()` çağrılır → sahnede `ArenaRoof` varsa çatı kuş bakışında kalkar |
| `AdminPlayerMarker` | Tek işaretçinin **görünümü** (prefab: `Resources/UI/AdminPlayerMarker`) — halka canvas'ı, halka görseli, ad etiketi. Seçim görseli iki ayrı sprite alanından gelir (`ringNormal`/`ringSelected`): ⚠️ halka sprite'ı çalışırken **ÜRETİLMEZ** — üretilseydi sanatçının prefabta seçtiği görsel her seçim değişiminde ezilirdi. ⚠️ **Bugün ikisi de aynı görseldir** (`Ring_16`), yani seçim yalnız **boyut** artışıyla anlatılıyor (`SelectedScale`); prefab öncesi kod ek olarak halkayı kalınlaştırıyordu. Kalınlık ipucu isteniyorsa `ringSelected`'a daha kalın bir halka sprite'ı konur — kod değişikliği gerekmez |
| `AdminPlayerMarkers` | Oyuncu başına **zeminde halka + altında ad etiketi** (kuş bakışı isteği). İşaretçiyi `AdminPlayerMarker` prefabından örnekler; konum/renk/seçim bu sınıfta kalır. Halka baş pozunun x/z'sinden arena zeminine indirilir; etiket kameraya döner ve kameranın yukarı vektörünün tersine kaydırılarak her kipte "dairenin altında" okunur. `RemoteAvatar`'a dokunmaz |
| `AdminHud` | **Kalıcı** ekran-uzayı HUD'ı (`sortingOrder = 4000`; hata ekranı 5000'de üstte kalır): üst orta takım skorları + **ortada istatistik chip'i** (faz/süre de gösterir), sol üst tercihler, sağ üst mod·harita + bağlantı/poz yaşı + **çoklu admin satırı** (kaç admin bağlı · son admin eylemi; tek admin varken boş kalır), yanlarda takım kolonları (**FFA'da tek kolon** — karar veriden gelir), alt orta kamera şeridi + seçili oyuncu, alt sağ ölüm akışı. **Görünüm prefabtan gelir** (`_Shared/App/Resources/UI/AdminHud.prefab`) — sınıfın kendisi yalnız veri bağlama/tazelemedir, yerleşim ve renk elle düzenlenir. Prefab **sahneye konmaz**, `AdminSpectator` onu `Resources.Load` ile yükleyip kendi altına örnekler (gözlemci kalıcı → arayüz de kalıcı, lobi ↔ arena geçişinde yeniden kurulmaz). ⚠️ Prefabtaki ögeler silinirse alan boşalır ve **hata vermeden sessizce çizilmez** |
| `AdminPlayerRow` | Oyuncu satırı: takım şeridi, ad + `#id`, HP barı, `K/D · batarya · durum`, eylemler POV/**KAL**/TAKIM/KİMLİK/**AT**. `KAL` ve `AT` **iki adımlı onay** ister (oyuncuyu savaş dışı bırakan/atan eylem tek tıkla olmamalı). `KAL` hem gösterge hem düğmedir (`KAL` yeşil / `KAL !` kırmızı — sembol değil renk+ünlem, çünkü TMP varsayılan fontunda ✓/✗ garantisi yok) ve **yalnız sıfırlar** — geri açmayı gözlük yapar (§3.11); kalibresiz satırın kenarlığı kırmızıya döner. Satıra tıklamak seçer. **Görünüm prefabtan gelir** (`_Shared/App/Resources/UI/AdminPlayerRow.prefab`); `AdminHud` onu kolona örnekler ve havuzlar. Satır **yüksekliği prefabtan okunur** → sanatçı satırı büyütünce kolon yerleşimi kendiliğinden uyar. ⚠️ Düğmelerin `onClick`'i prefabta BOŞTUR ve doldurulmamalıdır: hedef oyuncu her `Bind` ile değişiyor, kalıcı bir inspector kaydı yanlış oyuncuya komut gönderir (ve iki adımlı onayı atlar) |
| `AdminPreferencesPanel` | Eski dashboard'un işi. **MAÇ bölümü ORTAK** (başlıkta yazar): mod/harita seçicileri yerel alana değil `set_selection` ile sunucudaki ortak seçime yazar → tüm adminlerde aynı anda değişir; tıklamada yerel imleç de iyimser ilerletilir, sunucudan gelen değer son sözü söyler. **Harita değişince o arenayı HERKES yükler** (§10.7 sahneleme — sunucu `return_to_lobby` yayar, faz `Lobby` kalır, maç başlamaz); panel ayrıca sahneyi yerel olarak da açar (`SceneRouter.LoadPreview`) ama bu yalnız gecikmeyi gizler. ⚠️ **Mod/harita satırları maç KURULMUŞKEN PASİFTİR** (`AdminRoster.CanChangeSelection`): izin yalnız lobide (maç kurulmamış) ve maç bittiğinde (`finished`, operatör bir sonrakini seçiyor) vardır — koşan maç, yükleme, geri sayım ve **duraklatma** kapalıdır (donmuş maç da kurulmuş maçtır; çıkış yolu `abort_match`). Bölüm başlığı sebebini yazar, tıklanırsa durum satırına uyarı düşer; süre/limit her fazda açıktır. Aynı kural sunucuda da uygulanır ve otorite orasıdır (`MatchDirector.CanChangeSelection`) — buradaki kopya yalnız operatörü boşuna tıklatmamak içindir. Bu bileşen panel **kapalıyken de etkin** olduğu için başka bir operatörün harita değişikliği panel açılmadan da yansır. ⚠️ **Harita listesi mekan süzgeci her değiştiğinde yeniden kurulur** (`AdminSelection.VenueVersion`) — panel bağlantıdan ÖNCE kurulduğu için ilk liste kaçınılmaz olarak süzgeçsizdir ve orada bırakılırsa operatör başka işletmelerin arenalarını görür. Yeniden kurulumda **seçili harita hayatta kalıyorsa imleç onda bırakılır**. Bölüm başlığı ayrıca sunucunun **açık sahnesini** yazar (`SceneRouter.OpenScene`): harita satırı bir sonraki maçın adayı, açık sahne şu an yüklü olandır. **GÖRÜNÜM bölümü YEREL** (halkalar, ad etiketleri, kamera hızı, **çatı**) + bağlantı (yeniden bağlan/kes, bağlı admin sayısı). MAÇ bölümünde ayrıca **Süre** (`ROUND_SECONDS_OPTIONS`: 2.5/5/10/15/20/30 dk · 1 saat) ve **Skor limiti** (eşiğin altında ±1, üstünde ±5) seçicileri vardır — ikisi de ORTAK; **mod değişince o modun `ModeDefinition` varsayılanına dönerler**. Bir de **Dost ateşi** satırı: tek değer + iki düğme, ikisi de aç/kapa yapar (satır deseni diğerleriyle aynı kalsın diye) ve değer AÇIK iken kırmızı vurguyla gösterilir. ⚠️ **Bu satır seçim kilidine takılmaz** — mod/harita gibi maç kuruluyken pasifleşmez, çünkü işin özü tam olarak koşan maçta basılabilmesidir (takım arkadaşlarını vuran oyuncu için operatör maçı iptal etmek zorunda kalmasın). Yerel bir alan tutulmaz: gönderilen istek sunucudakinin tersidir, panel `admin_state.friendlyFire`'ı gösterir — iki operatör sapmasın. Yarı saydam, **scrim YOK**. **Liste tabanlı seçim açılır listedir** (mod/harita → `TMP_Dropdown`): seçenekleri kod doldurur (katalogdan; boş katalogda tek satır "katalog yok"/"harita yok" yazar ve seçici pasifleşir), imleç `SetValueWithoutNotify` ile eşitlenir — `value` ataması `onValueChanged`'i tetikleyeceği için sunucudan gelen her tazeleme yeni bir `set_selection` doğururdu. Şablon hiyerarşisi (viewport/item/scrollbar) prefabtadır ve **kapalı durur**. ⚠️ **Harita listesinin ilk satırı "Lobi"dir:** ayrı bir "LOBİYE DÖN" düğmesi yoktur, satır seçilince `set_selection` değil `return_to_lobby` gider. Lobi haritası katalogdan `AdminContent.ResolveLobbyMap` ile çözülür (`supportedModeIds == ["lobby"]` + mekan süzgeci; sunucudaki `MapTable.ResolveLobbyScene` ile aynı ölçüt, birden çok adayda alfabetik ilki → iki taraf aynı sahneyi seçer) ve **arena listesine karışmaz**. ⚠️ **Harita seçicisinin imleci ortak seçimi değil AÇIK SAHNEYİ izler** (`ApplyOpenScene`, kaynak: `welcome` + `return_to_lobby` + `load_match`): ikisi ayrışır — maç bitip lobiye dönüldüğünde ortak seçim hâlâ son arenayı gösterir, açık sahne lobidir. İmleci ortak seçime bağlamak operatörü **o arenaya geri dönemez** hâle getiriyordu: `TMP_Dropdown` seçili satıra tıklamayı olay saymaz (`value == m_Value` iken `onValueChanged` ateşlenmez), yani sahneleme komutu hiç gönderilemiyordu. Aynı sebeple seçicide **"zaten seçili" erken çıkışı yoktur** (sunucu aynı sahneyi tekrar sahnelemeyi zaten idempotent karşılar). Lobi açıkken **BAŞLAT reddeder** ve sebebini durum satırına yazar — sahnelenmiş arena yoktur, sunucu lobi türünde maç başlatmaz. Sayısal değerler (süre, skor limiti, numara) `[<] değer [>]` adımlayıcı kalır: gezilecek bir listeleri yok, asıl jest komşu değere gitmektir. Maç düğmelerinin altında **tek** DURAKLAT/DEVAM ET düğmesi: hangi komutu göndereceğine yerel bir bayrakla değil sunucudan gelen faza bakarak karar verir (`playing` → `pause_match`, `paused`/`operator` → `resume_match`), diğer her durumda pasiftir — çoklu admin'de duraklatmayı başkası da yapmış olabilir, yerel bayrak iki paneli birbirine ters düşürürdü |
| `AdminStatsPanel` | Takım toplamları + oyuncu tablosu (ad/takım/**SKOR**/K/D/K-D/HP/batarya/durum/sahne/**PING**) + maç bilgisi. **FFA'da tablo skora göre azalan sıralanır**, başlık lideri yazar. Tablo **kolon kolon** çizilir (TMP fontu eşit genişlikli değil, boşlukla hizalama kayar). ⚠️ Kolon eklemek koda yetmez, prefabta (`AdminHud.prefab`) bir TMP objesi açıp `_columns` dizisine bağlamak gerekir — bağlanmazsa kolon sessizce hiç çizilmez. Protokolde olmayan metrik (hasar/isabet oranı) **gösterilmez**; jitter/kayıp protokolde VAR ama panelde bilinçli olarak yok (operatörün eyleme çevirebileceği sayı ping'dir) |
| `AdminRoster` | Admin arayüzünün veri katmanı: `lobby_state` (otoriter tam görüntü + `kills/deaths/hp/alive/score`) + `health_update`/`kill_event` (anlık) + `match_state`/`countdown`/`match_end` birleşimi; takım listeleri, takım kipi kararı, ölüm akışı, snapshot yaşı. **`IsFfa` OTORİTER:** maç yüklüyse `ModeRuntime.Teams`, lobide ortak seçimin katalogdaki modu, ikisi de yoksa eski sezgisel yedek ("kimsenin takımı yok"). ⚠️ `respawn` admin'e GELMEZ (yalnız ölen oyuncuya gider) → geri sayım `kill_event` + `RESPAWN_DELAY` ile yerel hesaplanır |
| `AdminSession` | **YEREL** seçimler (kamera kipi, seçili oyuncu, açık panel) + görünüm tercihleri (`PlayerPrefs`'te kalıcı, admin PC'sine özel — halkalar, ad etiketleri, kamera hızı, **çatı kipi**). Tek doğruluk noktası; `Changed` ile HUD/kamera/işaretçiler senkron kalır. `RoofAlphaNow()` tercih + kamera kipinden çatı alfasını türetir |
| `AdminSelection` | **ORTAK** durumun aynası (`admin_state`, §5.3): mod/harita seçimi, **maç süresi + skor limiti**, çevrimiçi admin sayısı, son admin eyleminin duyurusu, **mekan süzgeci** (`venueId`/`venueScenes` + her değişiminde artan `VenueVersion`), **dost ateşi anahtarının yürürlükteki değeri** (`FriendlyFire` — seçim değil durum, §3.9). Statik durum + statik `Changed` (bileşen kurulum sırası dinleyiciyi ilgilendirmesin); bileşenin kendisi yalnız ağ olayı pompasıdır. Otorite sunucudadır — buraya yerelden yazılmaz |
| `AdminCommands` | Admin komutlarının tek çıkış kapısı (§5.2) + son işlemin durum metni. "Gönderildi" der, "oldu" demez — kabul/ret sunucuda. `SetSelection` ortak seçimi (mod/harita/süre/limit) değiştirir, maçı başlatmaz; `StartMatch` süre/limit taşır (`0` = mod varsayılanı); `PauseMatch`/`ResumeMatch` koşan maçı dondurur/sürdürür; `SetFriendlyFire` dost ateşi anahtarını çevirir (**faz kapısı yok** — koşan maçta da geçerli, §3.9) |
| `AdminContent` | `Resources.Load<GameCatalog>("GameCatalog")` (asset: `_Shared/Data/Resources/`) → mod/harita listeleri. **Statik** yardımcıdır (`[SerializeField]` taşıyamaz), katalogu bu yüzden `Resources`'tan okur |

### Editör: `VortexArena.App.Editor` (dev araç seti — yalnız Editor)

| Sınıf | Görevi |
|---|---|
| `DevWindow` | `Tools > VortexArena > Development > Dev` penceresi: "Dev enjeksiyonu açık" onayı · **Rol** (Player/Admin) · **Sunucusuz sandbox** (+ mod seçicisi; açıkken Hedef bloğu devre dışı) · **Hedef** (`dev-targets.json` + "Özel…" IP/Port + Tazele) · **Başlangıç** (Boot'tan / Açık sahneden) · alttaki "Seçim: …" özeti. **Sunucuya hiç dokunmaz** — ne başlatır, ne durdurur, ne derler (§6.1). Maç parametresi taşımaz — mod/takım/süre/limit sunucudan gelir. **Modal dialog kullanmaz** (Unity CLI doğrulamasını kilitliyor); geri bildirim konsol + `HelpBox` |
| `DevTargets` | Repo kökündeki `dev-targets.json` okuyucusu (`defaultTarget`/`defaultRole` + adlandırılmış hedefler). Dosya yok/bozuksa bellekte `Local` + `Kesif (beacon)` varsayılanına düşer ve **dosyayı OLUŞTURMAZ** (commit kirletmemek için). Bir hedefin `ip`'si boşsa adres yazılmaz → keşif zinciri devralır |
| `DevBootstrap` | Editör kancaları: "Boot'tan" kipinde `EditorSceneManager.playModeStartScene`'i Boot sahnesine ayarlar (sahne **Build Settings'ten** bulunur, sabit yol gömülmez); `Ctrl+Alt+R` kısayolunu kurar (rol player↔admin). **Hiçbir süreç öldürmez** — sunucu kasıtlı olarak yaşar (üretimde de ayrı makinede sürekli açık) |

### İstemci: `VortexArena.Core` (oyun kodu)

`ArenaBoundary` (muhafaza: kenara/kolona olan mesafeden karartma + uyarı — kenara `warnDistance`
kala hafif bir rampa başlar (`warnFadeAlpha`), sınır aşılınca tam karartmaya gider; iki dal sınırda
aynı değeri verdiği için geçiş süreklidir. ⚠️ **Yarı saydam duvar göstergesi KALDIRILDI**
(`wallRenderers`/`minWallAlpha`/`maxWallAlpha` yok): arenanın duvarları environment sanatına ait ve
mekanizma oraya taşınamıyordu — alfa yazımı yalnız Transparent malzemede iş görür, üstelik alfa
düşünce Renderer'ı kapatıyordu. Uyarı bu yüzden HMD'ye bağlı karartma quad'ına taşındı, arena
geometrisinden tümden bağımsız;
`HalfExtents`/`LocalCenter`'ı admin kuş bakışı kadrajı okur — ikisi de plandaki sınır çokgeninin
sınırlayıcı kutusundan türer, ölçü bileşende TUTULMAZ. Planın **tek kaynağı** `dimensionsJson`
alanına bağlanan boyut dosyasıdır; ikinci bir kip yoktur. Plan çözülünce mesafe **çokgene işaretli
mesafe ⊓ kolonlar ⊓ sahnedeki `ArenaObstacle`'lar** olur — en yakın tehlike kazanır, kolonun içi
alan-dışı sayılır. JSON kare başına ayrıştırılmaz (referans değişmedikçe önbelleklenir).
⚠️ Dosya bağlı değilse ya da okunamıyorsa **açık başarısızlık**: bir kez `Debug.LogError` basılır ve
muhafaza tümden susar (karartma ve alan-dışı uyarısı çalışmaz). Gerekçe: ölçüsü
bilinmeyen bir arenada doğru bir muhafaza zaten üretilemez, kapalı başarısızlık (ör. her karede
ekranı karartmak) işletmede oyunu tümden oynanamaz kılardı — bu bir KURULUM hatasıdır, editörde/
QA'da yakalanmalıdır. Arena origin'i bu bileşende DEĞİLDİR, devre dışı bırakılabilir),
`ArenaDimensions` (`Core/Arena` — **arena ölçüsünün TEK doğruluk kaynağı**: elle yazılabilir bir
JSON dosyası (`TextAsset`), çalışma anında okunur ve **MEKAN başınadır** — bir işletmenin bütün
sahneleri (arenalar + lobi) aynı dosyayı gösterir, sahne başına kopya kaçınılmaz olarak sapardı.
Alanlar `plane`/`columns`/`defaultColumnHeight`; noktalar `ArenaBoundary`
transformunun yerel XZ'sinde ve JSON'daki `y` dünya Z'sidir. Halkalar **kapalı**dır (ilk nokta sona
tekrarlanmaz), köşe yönü önemsizdir. ⚠️ **Taban da kolon da TEK sıralı köşe halkasıdır**: parçalardan
birleştirme (union) yoktur — içbükeylik için gerekmez ve `ArenaBoundary` yüzünden çalışma anında da
koşmak zorunda kalırdı. Aynı sebeple "dikdörtgense şu hızlı yol" ayrımı ve ona ait bileşen alanları
da yoktur. ⚠️ **`wallHeight` alanı YOKTUR**: duvar üretimi de duvar göstergesi de kalktığı için
okuyanı olmayan bir ölçü olurdu. ⚠️ Kolondaki `{"points": […]}` sarmalayıcısı zorunlu —
`JsonUtility` iç içe dizi serialize etmiyor. `Parse`/`FromTextAsset` **exception fırlatmaz** — bozuk
dosyada `null` + hata metni döner, çünkü çağıran yer sahne yükleme yolu; `FromJsonOverwrite`
kullanıldığı için **yazılmayan alan varsayılanında kalır** (aksi hâlde eksik bir
`defaultColumnHeight` hiç çizilmeyen kolonlar demekti). ⚠️ **Kolonun muhafazaya girip girmeyeceğini
seçen bir anahtar YOKTUR ve eklenmez** — kolon binanın taşıyıcısıdır, oyuncu ona her hâlükârda
çarpar. `calibration: {a, b}` = zemine yapıştırılan A/B bantlarının yeri; **mekan başınadır**
(aynı odadaki tüm sahneler aynı iki fiziksel işareti kullanır) ve `HasCalibration` iki nokta
arasında en az `MinCalibrationSpan` (0,5 m) ister — daha yakın bir çift yön tanımlamaz, yaw
hatası mesafeyle ters orantılı büyür. ⚠️ Kalibrasyon noktaları `IsValid`'in parçası **değildir**:
noktasız bir dosya muhafazayı çalıştırmaya yeter, yalnız işaretçiler kendiliğinden yerleşmez.
Dosya olmasının kazancı: ölçüyü sahadan alan kişi Unity açmadan güncelleyebilir),
`Polygon2D` (`Core/Arena` — halkalara sorulan her geometrik sorunun tek yeri: `Contains`,
`DistanceToRing`, `SignedDistance` (alan: içeride +) / `ObstacleDistance` (engel: dışarıda +),
`Bounds`, `Centroid`, `IsSelfIntersecting`. İki mesafe sözleşmesi muhafazanın ikisini tek
`Mathf.Min` ile birleştirebilmesi içindir — her ikisinde de "artı = güvenli pay". Metotlar tahsis
yapmaz: muhafaza her karede çağırıyor),
`ArenaDimensionMesh` + `DimensionPolygon` + `DimensionAnchor` (ölçü maketinin işaretçileri; kökte
mekan adı + kaynak `TextAsset` + geri yazma taşıyıcıları, çokgenlerde yalnız
`Kind { Plane, Column }`, kalibrasyon küplerinde yalnız `Kind { A, B }`. ⚠️ Nokta/ad/
yükseklik işaretçide TUTULMAZ — kaynakları sırasıyla mesh (kalibrasyon noktasında transform),
`GameObject` adı ve mesh'in Y aralığı;
kopyalamak sahnede düzenlenen değerden sapan ikinci bir kaynak üretirdi. Maketin kökü `EditorOnly`
etiketlidir, build'e girmez. ⚠️ Kalibrasyon küpleri sahnedeki işaretçilerle **aynı adı** taşır
(`anchor_a`/`anchor_b` — tek sabit `ArenaCalibrator.AnchorAName`); ikisini ayıran şey ad değil
`DimensionAnchor` bileşenidir, kalibratörün ad araması onu taşıyan objeleri atlar),
`ArenaObstacle` (sahneye ELLE konan engelin muhafaza dikdörtgeni; konum/dönüş transformdan,
ölçü `size`'dan gelir — ⚠️ **collider eklemez, fizik yapmaz**: free-roam'da oyuncuyu durduran şey
gerçek nesnedir, bileşenin tek işi uyarıyı erken tetiklemektir),
`ArenaCalibrator` (`VA_CalibrationManager` prefabıyla gelir; 2 nokta → 6DOF hizalama +
OVRSpatialAnchor kalıcılığı + recenter onarımı; nokta alma jesti **sağ kumandada A basılıyken
B'ye çift basış** (`doubleTapSeconds` penceresi; basılı tutma süresi yoktur) ve **yalnız sunucu
"kalibresiz" derken açılır**,
§3.11. Sahnedeki `anchor_a`/`anchor_b` işaretçileri **kurulum aracıdır, dekor değil**: yalnız elle
kalibrasyon sürerken görünürler ve hizalamadan `markerVisibleSeconds` sonra gizlenirler — kayıtlı
anchor'dan geri yükleme yolunda hiç gösterilmezler, yoksa harita değişiminde maçın ortasında ekrana
obje düşerdi. **Yerleri sahneden değil boyut dosyasından gelir**: `Start` işaretçileri
`ArenaBoundary.TryGetCalibrationMarks` üzerinden `calibration.a`/`.b` noktalarına oturtur
(`PlaceMarkerAtFloor`), dosyada nokta yoksa dokunmaz ve uyarır. `anchorA`/`anchorB` alanları boş
bırakılırsa objeler **adlarından** çözülür (`AnchorAName`/`AnchorBName`) — `EditorOnly` etiketli
kökler taranmaz, ölçü maketinin küpleri yakalanmasın diye.
**Tamamlanma İKİ biçimde bildirilir** ve iki tamamlanma yolu da (elle yakalama + kayıtlı
anchor'dan geri yükleme) tek kapıdan geçer: statik `Calibrated` olayı (dinleyicisi
`CalibrationState`) ve statik **`CalibrationGeneration`** sayacı. Sayaç abonelik istemez — geç
uyanan ya da arada kapatılan bir dinleyici olayı sessizce kaçırır (yerel gövde avatarı rig'i
kaybedince kapanıyor ve harita değişiminde kayıtlı anchor tam o aralıkta geri yükleniyor), sayacı
kaçıramaz: sonraki karede farkı görür. Tüketicisi `LocalBodyAvatar`'dır: her hizalamadan sonra
gövde oranını yeniden ölçtürür (`CharacterRetargeter.Calibrate()`, gecikmeli).
⚠️ **İki kalibrasyon ayrı şeydir:** bu sınıf rig'i fiziksel arenaya hizalar (sunucu-otoriter durum,
§10.6), SDK'nınki karakterin gövde oranını oyuncununkine sabitler (tamamen yerel, ağda karşılığı
yok). Aralarındaki tek bağ zamanlamadır — arena hizalamasından sonra oyuncu eğilip doğrulmuştur,
yani boy ölçmek için doğru andır.
⚠️ **Sıra A → B'dir ve geometrik olarak doğrulanamaz**: iki nokta hangisinin önce alındığını
söylemez, mesafe kontrolü de simetriktir. Garanti prosedüreldir — ilk yakalama A sayılır, o anda
A işaretçisi yanar ve log `1/2 — A yakalandı` yazar. Karıştırılırsa arena 180° ters döner.
⚠️ İşaretçinin **mesh'inin alt noktası arena zeminine oturur**: `VirtualFloorY`
görselin mesh bounds'undan ölçülür (`MeasureFloorDrop`), yerleştirme de aynı ölçüyü kullanır —
görseli değiştiren kişi bir şey yapmak zorunda değildir, ama pivot ile mesh tabanı arasına
`Renderer` taşımayan bir ara obje sokarsa zemin yüksekliği sessizce kayar),
`CalibrationState` (kalıcı tekil — kalibrasyon durumunun sunucu ile iki yönlü köprüsü: hizalanınca
`set_calibration` yollar, operatör sıfırlayınca `ArenaCalibrator.Invalidate()` çağırır),
`ArenaSpace` (dünya↔arena dönüşümü; origin YOKKEN kimlik dönüşümü yapar ama **sahne başına bir kez
uyarır** — lobide normal, arenada "her şey çalışıyor ama koordinatlar kaymış" tablosunun tek işareti),
`BaseZone` (**taban bölgesi** — kırmızı/mavi şerit, canlanma
kapısı; `Neutral` = herkese açık), `SpawnPoint` (arena başına **tek** marker: hem maç öncesi
yerleşim göstergesi hem **arena uzayının sıfırı** — `OnEnable`'da `ArenaSpace`'e origin olarak
kaydolur, birden çoksa ilk kaydolan geçerlidir. Oyuncuyu taşımaz, protokolde karşılığı yoktur),
`MapDefinition` / `ModeDefinition` / `GameCatalog` (içerik SO'ları),
`Weapon` (ISDK ile tutulan hitscan tüfek; tetik **silahı tutan elin** kumandasından okunur — çift
silahta tetikler bağımsız; şarjör+yedek şarjör durumu taşır, boş şarjörde **otomatik reload YOK**
(kuru tetik sesi), reload **bel-altı jestiyle** başlar; `reserveMode=DiscardMagazine`'de erken
reload'da şarjörde kalan mermi **yanar** (ürün kuralı; `PoolRounds` = CS2 havuzu SO'dan seçilebilir);
spread atış sürdükçe açılır (bloom) ve boşta toparlar; yerel canlanmada tutulan silah tam dolar;
vuruş/atış bildirimi `ArenaCombat` üzerinden gider — protokol DTO'su bu sınıfta YOK. **İkinci tutuş
yolu:** `WeaponGranter` silahı doğrudan ele verir (`GrantTo(hand, kind)` — §3.9); verilen silah
tanım gereği tutuluyordur (ISDK kavraması işletilmez), gerisini `WeaponGrantKind` ayırır —
`Disposable` (FFA'nın rastgele silahı) tek elli/rezervsiz ve reload'u KAPALIDIR, `Persistent`
(çerçeveden seçilen silah) tam rezervle gelir, reload'u AÇIKTIR ve ön kabzası tutulabilir) +
`WeaponDefinition` (SO — hasar/HS çarpanı/RPM/şarjör/reload/spread/recoil/ses profili + verilen
**tek denge kaynağı**, sunucuya export edilmez; el duruşu tabandaki `ItemDefinition.primaryGrip*`'te,
burada DEĞİL) + `WeaponAudio` (Meta XR spatializer'lı namlu AudioSource:
ateş/şarjör çıkar-tak/kuru tetik/alma) + `WeaponAnimator` (Animator'sız kod-güdümlü parça
animasyonu: atışta bolt tepmesi, reload'da `*_Mag` child'ı çıkar-takılır; şarjör seslerini de bu
zaman çizgisi çalar — görüntü/ses tek kaynaktan) + `WeaponReloadGesture` (silah bel hizasının
altına inince `TryStartReload`; bel çizgisi = kafa − `waistDropMeters` (0.62 m vars.) — ORAN DEĞİL:
fark matematiği zemin/kalibrasyon ofsetlerinden etkilenmez; kavradıktan sonra bir kez bel üstüne
çıkmadan devreye girmez — alçakta duran bir silahı seçer seçmez yanlış tetiklemeyi önler) + `WeaponCatalog` (SO, `_Shared/Data/Resources/` — `weaponId`→tanım araması;
`Resources.Load` ile okunduğu için klasöründen çıkarılmaz) + `RemoteShotFx` (kendini önyükler,
sahne kurulumu istemez; UDP atış olayını (§6.4/6.5) tüketip uzak oyuncunun namlu alevi + konumsal
atış sesini havuzlu çalar, tracer'ı çizer ve silahın **geri tepmesini** tetikler —
`RemoteAvatar.ApplyShotRecoil`, olayın kendi tik'inin oynatma anında) + `ShellEjector` (`Weapon.Fired` olayına abone; ateşte namlunun yanındaki `Eject`
noktasından kalibreye göre (`Casing_762x39`/`Casing_556x45`) bir kovan fırlatır — 10'luk round-robin
havuz, süre kontrolü coroutine değil `Update`'te `Time.time` ile; havuz+`MuzzleFlash` altındaki
"Smoke" sub-emitter'ı da dahil tüm bu kit `WeaponKitBuilder` tarafından üretilir/güncellenir) +
`AmmoHud` (`Core/UI` — kendini önyükler ve görünümünü **`Resources/UI/AmmoHud` prefabından**
alır; tutulan silah(lar)ın adı/mermisi/yedek şarjörleri görüş alanının sağ altına düşen
`HudFollow`'lu tembel-takip panelinde; silah tutulmuyorken gizli, yalnız
`Weapon.Active`/`ActiveChanged` + silah olaylarıyla yenilenir — mermi göstergesi silahın
ÜSTÜNE koyulmaz; punto/konum prefabta düzenlenir) + `ArenaCombat` / `WeaponGranter` (aşağıdaki
tabloda), `PlayerCombatState`
(yerel oyuncunun takım/can/ateş yetkisi/canlanma akışı), `RemoteAvatar` + `RemoteHitBox`
(uzak oyuncu gövdesi ve isabet kutusu; `RemoteAvatar` ayrıca çizdiği silahın **geri tepmesini**
yerelin eğrisiyle üretir — kavrama örneğin KÖKÜNÜ, geri tepme `Model` ÇOCUĞUNU yazar, bu yüzden
yarışmazlar; telde geri tepme diye bir alan yoktur ve eklenmeyecek, §6.4),
`ProximityWarning` (`Core/Player` — free-roam çarpışma önleme: `RemotePlayerRegistry` pozlarını
yerel HMD ile karşılaştırır; 1.2 m'de uzak oyuncunun konumunda **duvar arkasından da görünen**
halka (`VortexArena/ProximityHalo`, ZTest Always), 0.8 m'de tehlikenin geldiği **taraftaki**
kumandada haptik. Ölü oyuncular ELENMEZ — respawn durum değişimi olduğu için ölünün bedeni sahada
durmaya devam eder, çarpışma riski aynıdır. **Henüz hiçbir sahnede bağlı değil**: bileşen elle
eklenir, `head` ve `haloMaterial` (`_Shared/FX/M_ProximityHalo`) alanları Inspector'dan verilir),
`ControllerModelHider` (`Core/Player` — **`VA_CameraRig` kökünde**; Meta Building Blocks kamera
rigine BİRDEN FAZLA yerde (`Controller Tracking Left/Right` VE ayrıca
`OVRComprehensiveInteractionRig` altında) fiziksel Touch controller modeli + el görseli koyuyor.
Rig kökünden TÜM alt ağacı **bileşen tipiyle** tarar ve gizler: her `OVRControllerHelper` ile tip
adı `HandVisual` olan her `MonoBehaviour`'ın GameObject'i kapatılır. `LateUpdate`'te her kare
yeniden çalışır — kontrolcü bırakılıp-tutulduğunda Meta bunları yeniden aktifleştiriyor.
⚠️ İsim deseniyle çalışan eski sürüm hedefi ıskalıyordu (§7, "rig görselleri isimle değil bileşen
tipiyle gizlenir"). ⚠️ **`SyntheticHand`, `OVRHand`, interactor'lar, retiküller ve `HandSphereMap`'e
DOKUNULMAZ** — kavrama onlara bağlıdır; kapatılırsa silah hiç tutulamaz. Sonuç: kumanda modeli de
Meta eli de **hiçbir yerde** (lobi ve kalibrasyon dahil) çizilmez; oyuncu kendinden `LocalBodyAvatar`
gövdesini görür. Kozmetiktir, `OVRInput` girdisini etkilemez),
`WeatherVolumeFollow` (`Core/FX` — ambiyans parçacık hacmini yerel kameranın üstünde tutar; bağlı
sistemler **World** simülasyon uzayında olmalı, `Start` sapmayı uyarır. Yalnız kendi transform'unu
taşır, rig'e dokunmaz), `WeatherWindDriver` (`Core/FX` — kök objeye takılır, altındaki tüm
sistemlerin `Velocity over Lifetime` XZ'sini ve Noise şiddetini tek Perlin kanalından salındırır:
rüzgar şiddeti + yönü + türbülans birlikte nefes alır. Temel değerler `Awake`'te alınır,
katmanların göreli hız farkı korunur).

> **Gövde Meta Movement SDK ile çözülür ve yerel/uzak AYNI yoldan geçer.** Oyuncu kendi gövdesini
> omuzlarından aşağı görür (`LocalBodyAvatar`), başkaları onu uzak avatarda görür — ikisi de **aynı
> prefab, aynı retarget config, aynı kod**. Tek fark
> `ArenaNetCharacterBehaviour.HasInputAuthority`'dir: yerelde `true` (gövde body tracking'den çözülür
> ve ağa akar), uzakta `false` (gelen iskelet uygulanır, sensör hiç koşmaz).
>
> ⚠️ **Üç noktadan gövde TÜRETMEK bırakıldı.** Sebep yapısaldır: Meta'nın body tracking'i bir cihaz
> servisidir ve dışarıdan poz kabul etmez, yani uzak avatara "aynı body tracking'i takmak" her
> avatarın YEREL gövdeyi oynatması demektir. Doğru çözüm gövdeyi sahibinin cihazında çözüp sonucu
> akıtmaktır (`0x07`/`0x08`, §6.9/6.10) — SDK'nın kendi çözümü de budur.
>
> ⚠️ **Bacak izleme YOKTUR ve gelmeyecek:** Quest 3'te bacakta sensör yok. `BodyJointSet.FullBody`
> seçilse bile alt gövde ÜRETİLİR (generative legs), yani "full body" adı izlemeyi değil eklem
> sayısını anlatır.

| Sınıf | Görevi |
|---|---|
| `Arena/BaseZoneVisibility` | Taban şeritlerinin (`BaseZone`) görünür/etkin olup olmadığına karar veren **tek yer**. **Kendini önyükleyen kalıcı tekil** — sahneye konmaz. Kapı **takım kipidir**: takımlı modda (TDM/turnuva) şeritler durur — biri canlanma kapısı, diğeri tur arası toplanma kapısıdır; takımsızda (FFA) gizlenir, çünkü orada canlanma şartı sabit durmaktır ve renkli şerit olmayan bir kuralı anlatırdı. Hangi mod? **Öncelik `ModeSelection`** (seçili mod): admin lobide bir arena sahnelediğinde herkes o arenaya geçer ama aktif kural hâlâ lobi profilidir, yani koşan kurala bakan bir kapı "hangi maç kurulacak" sorusunu göremezdi. Seçim bilinmiyorsa `ModeRuntime`'ın takım kipine düşer. **Bileşen kapatılır, GameObject KAPATILMAZ** (altındaki `SpawnPoint` statik kayıttan düşerdi) + Renderer'lı doğrudan çocuklar gizlenir; alt ağacında `SpawnPoint` bulunan çocuğa dokunulmaz. ⚠️ **Yalnız kendi kapattığını geri açar** — aynı bileşenleri `AdminSpectator` de kapatıyor. Eskiden bu iş `WeaponGranter`'ın süpürmesindeydi ve kapısı `weaponSource`'tu; FFA'da ikisi birlikte değiştiği için doğru görünüyordu, lobinin silahı rastgeleye alınınca lobideki tabanlar da kayboldu. **İkinci işi duvar-arkası görünürlüktür (x-ray):** şeritler görünürken oyuncunun **kendi** takımının şerit renderer'ına ikinci bir materyal slotu eklenir (`M_BaseZoneXRay` → `VortexArena/BaseZoneXRay`, `ZTest Greater`) — aynı mesh bir kez daha çizilir ve yalnız **önünde başka geometri olan** piksellerde görünür, yani arena dekorla dolsa da ölen oyuncu canlanma noktasını görür. Yeni GameObject / URP renderer feature / katman gerekmez, arena başına kurulum adımı doğmaz. ⚠️ **Rakip taban hiçbir koşulda çizilmez** (slot eklenmez) ve takım `Neutral` ise (takım atanmadı, admin gözlemci) hiç eklenmez; takım rengi **şeridin kendi materyalinden** okunur, ikinci bir renk tanımı yoktur. Slotu **çalışma anında** ekler çünkü `TemplateBasicsLoader` her renderer'a tek `sharedMaterial` yazıyor — asset'e konan ikinci slot o araç her çalıştığında silinirdi; ayrıca çalışma anı yolu mevcut tüm arenaları sahne düzenlemesi olmadan kapsar. Takım değişimini `PlayerCombatState.LocalTeamChanged` ile dinler |
| `ModeSelection` | **Henüz başlamamış** maçın seçili modu (`selection_state`, §5.3) — yalnız sunum. `ModeRuntime` ile karıştırılmaz: orası **koşan** maçın kuralıdır. Statik `HasValue`/`ModeId`/`IsTeamless` + `Changed`; besleyicisi `ModeRuntimePump`. ⚠️ Hiçbir kuralı/HUD'ı/loadout'u değiştirmez (maç türü `start_match`'i bekler) ve **tüketicisi olmayan alan eklenmez** — bugün tek tüketicisi `BaseZoneVisibility` |
| `ModeRuntime` (+ `ModeRuntimePump`) | Aktif maçın kurallarının **tek okuma noktası** (§3.9). `load_match.rules` / `welcome.match.rules` / `return_to_lobby.rules` besler, **`rules_update` maç ortasında tazeler** (bugün tek tetikleyicisi dost ateşi anahtarı — §3.9); kurallar telde yoksa (`rules == null`) `ModeDefinition` önizlemesi fallback olarak devralır. Lobiye dönüşte SIFIRLANMAZ, **lobi profili uygulanır** (`modeId:"lobby"`, §3.8.1) — lobideki silah seçimi loadout'unu bu anahtarla buluyor. Statik durum + statik `Changed`; pompa kendini önyükler (`BeforeSceneLoad` + `DontDestroyOnLoad`). Tüketiciler: `PlayerCombatState`, `ModeHudBase`, `AdminRoster` |
| `UI/ModeHudBase` | Mod HUD'larının **takım-agnostik** tabanı: faz/süre, geri sayım, can barı, ölüm ekranı + durum metni, kill-feed (ad çözümü `lobby_state`'ten), kendi öldürme/ölüm sayacın, maç sonu satırı. **Takıma ait hiçbir şey burada değil** — skor satırı (`ScoreLine`) ve kazanan metni (`WinnerLine`) alt sınıfın işi. Core'da durur çünkü modlar birbirini referanslamaz. `PhaseLabel`/`ModeStateLabel` `virtual`'dır: tur tabanlı mod "MAÇ" yerine "TUR 3", mod duraklamasında da "TOPLANMA 2/6" yazabilsin diye — taban `modeState`'i **yorumlamaz** |
| `Combat/ItemDefinition` | Elde tutulabilen her şeyin (silah, ileride bomba) **dar** tabanı: `netItemId` (telde giden kimlik, §6.6), prefab, `holdMode`, kanonik kavrama pozları, tracer görünümü. Davranış alanı (hasar/şarjör/fitil) **girmez** — `RemoteAvatar` eşyayı ne YAPTIĞINI bilmeden çizer; Net katmanının "oyun bilgisi içermez" ilkesinin sunumdaki karşılığı. ⚠️ `primaryGrip` = eşyanın **ele göre** pozu, `secondaryGrip` = ön kabzanın **eşyaya göre** pozu — uzayları terstir (§6.6). Soket çizimi ana noktanın **eşyaya göre** yerini ister ve o `PrimaryGripPointOnItem` olarak **türetilir** (`Inverse(R)·(−P)`), ayrı bir alan olarak tutulmaz — aynı nokta iki yerde yaşasa biri güncellenip diğeri unutulurdu |
| `Combat/ItemGripSockets` | Kavrama noktalarını **soket** yapar: el yaklaşınca (`0.30 m`) gösterge belirir, soketin üstünde (`0.12 m`) grip'e basılınca kavrama doğar. Kapı ISDK'nın **kendi** uzatma noktasıdır — bileşen bir `IGameObjectFilter`'dır ve `GrabInteractable._interactorFilters`'a yazılır (`WeaponKitBuilder` bağlar), yani kavramanın ALGISI ISDK'da kalır ve tel yolu (`Grabbable` → `Weapon` → `HeldItems`) hiç değişmez. Çizim ile kapı **aynı** açıklık kuralını kullanır (`IsSocketOpen`): ana soket eşya tutulmuyorsa açık, ön kabza yalnız tutuluyor + çift elli + soran el ana el değilken açık — tek elli eşyada ön kabza hiç açılmaz, bu ikinci elin aynı tabancayı kavramasını da engeller. Gösterge `WeaponCatalog.GripSocketPrefab`, yoksa prosedürel halka. Kavrama yarıçapı **silah başınadır** (`ItemDefinition.primary/secondaryGripRadius`) — tabanca kabzası ile tüfek ön kabzası aynı büyüklükte değil; hover (0.30 m) global sabit ama etkin değeri `Max(hover, yarıçap)`, yoksa yarıçap hover'ı geçtiğinde oyuncu soketi hiç görmeden kavrardı. ⚠️ El çözülemezse **fail-open** (editörde kavrama mümkün kalsın) ve bu tek seferlik loglanır — sessiz bırakılsa özellik "çalışıyor gibi görünüp" hiçbir şey yapmazdı |
| `Combat/GripSocketMarker` | Kavrama noktasını sahnede **sürüklenerek** ayarlamak için işaretçi (`kind` + `radius`). **Çalışma anı davranışı YOKTUR ve oyun onu OKUMAZ** — tek doğruluk kaynağı `ItemDefinition`'dır; bu yalnız bir yazma aracıdır (`GripSocketAuthoring`). `OnDrawGizmos` sarı saydam dolu küre + opak tel kenar + el yönünü gösteren eksen çizgileri (soketin yalnız konumu değil DÖNÜŞÜ de yazılıyor). ⚠️ Sarı = yazılmayı bekleyen, camgöbeği (`ItemGripSockets`) = SO'nun dediği: **çakışmıyorlarsa henüz yazmadın** — iki temsilin sapması böylece gözle görünür bir kontrol olur |
| `Combat/NetItemCatalog` | `netItemId` → `ItemDefinition` eşlemesi (`Resources`, ilk sorguda sözlük kurar). `Tools > VortexArena > Weapons > Rebuild Net Item Catalog` projedeki TÜM `ItemDefinition`'lardan yazar — silah tablosundan değil, ki yeni bir eşya TÜRÜ (bomba) eklenince sessizce eksik kalmasın. `Resources/` altından çıkarılmaz |
| `Combat/HeldItems` | Yerel oyuncunun "hangi elde hangi eşya" durumunun tek buluşma noktası (statik). **Yazan** `Weapon`/`WeaponGranter` (`Weapon.ActiveChanged` üzerinden toplanır — çift tabanca mümkün olduğu için bildirim per-instance olamaz), **okuyan** `PlayerPoseTracker`. Hiçbir şey göndermez |
| `Combat/ShotTracer` | Havuzlu `LineRenderer` mermi izi — ömrü boyunca **sönerek** kaybolur (alfa düşer + çizgi incelir; eskiden ömrün sonunda `enabled=false` ile bir anda kesiliyordu, göz bunu sönme değil "pat" olarak okuyordu). Ayrı bir *sönme süresi* alanı YOK: sönme `tracerLifetime`'ın kendisine yayılır, yoksa iki sayıdan hangisinin diğerini kestiği sessiz bir tuzak olurdu. Üstüne **yol boyunca duman izi**. **İKİ çağıranı vardır ve olmak zorundadır:** atanın kendi izini `Weapon.Fire` çizer (sunucu atış olayını atana geri yollamaz, istemci de kendi `playerId`'sini süzer — §6.5), uzaktakileri `RemoteShotFx`. Havuz ikisi arasında **paylaşılır** (`ShotTracer.Shared`, kendini önyükleyen DDOL tekil): silah başına havuz açmak, silahların sürekli üretilip yok edildiği modlarda materyali + `Update`'i silah sayısınca çoğaltırdı. Görünüm `ItemDefinition`'dan, sıklık `tracerEveryNthRound`'dan — iki yol da aynı alanları okur (ayrı okusalar aynı silah kendi ekranında başka, karşı ekranda başka görünürdü). Sayaç yerelde silah başına, uzakta oyuncu başına (paylaşılan sayaç izleri rastgele namlulara dağıtırdı). Her mermide çizmek lazer ışını gibi durur + konumu fazla ifşa eder; asıl maliyet bayt değil GC/draw call. **Duman `Play`'in İÇİNDEDİR**, ayrı bir giriş noktası değil — ikinci bir `PlaySmoke` kapısı olsa iki çağırandan biri onu unutabilir, yani aynı silah kendi ekranında dumanlı, karşı ekranda dumansız görünürdü. Puf'lar TEK paylaşılan `ParticleSystem`'e manuel `Emit` edilir (sistemin kendi parçacık dizisi zaten havuz; atış başına `TrailRenderer` objesi üretmek Quest'te hem GC hem draw call olurdu) ve namludan isabete doğru **sönümlenir**: alfa düşer, boy büyür, ömür kısalır. Ömür `tracerLifetime`'dan TÜRETİLİR ama birebir değil — 0.06 sn'lik duman tek karelik gri lekedir, o yüzden ×katsayı + kullanılabilir banda kırpma. Eşyaya ayrı duman alanı **eklenmedi**: duman tracer'a biniyor, `tracerEveryNthRound` tracer'ı kapattığında duman da kapanır. Materyal/doku (yumuşak radyal puf) çalışma anında üretilir — hazır duman materyali `Resources/` altında değil ve serialize alan açmak bu tekili sahneye konması gereken bir bileşene çevirirdi |
| `Combat/ArenaCombat` | **Oyun kodunun ağa açılan tek kapısı** (statik). `ReportShot` / `ReportThrow` / `ReportHit` / `ReportRaycastHit` / `ReportAreaHit` + `TryGetTargetPlayerId` / `IsHeadshot` / `CanFire` / `LocalPlayerId`. ⚠️ `ReportShot`/`ReportThrow` **UDP olay kanalına** (`0x03`) gider, `ReportHit` **WS**'te kalır — kaybı kozmetik olan ile otoriter olanın kanalı ayrıdır (§10.3). Bir vuruşu doğru bildirmek dört şeyi bilmeyi gerektiriyor (arena uzayı, yön≠nokta, `RemoteHitBox` ile hedef çözme, hasarı istemcinin belirlemesi) — bunlar `Weapon` içinde gömülü kalsaydı ikinci bir hasar kaynağı yazan herkes aynı dördünü yeniden keşfederdi. `Weapon` de bu kapıyı kullanır (tek doğruluk kaynağı). Bağlantı yokken sessizce no-op. Reçeteler: `Gelistirici/Yemek-Kitabi.md` |
| `Combat/WeaponFrame` | Sahnedeki silahın **çerçevesi** — `VA_WeaponFrame` prefabı olarak her `WPN_*` kökünün çocuğudur (`WeaponKitBuilder` bağlar). Kaynak silahı **dondurur** (Rigidbody kinematik + yerçekimsiz, `Grabbable`/`GrabInteractable`/`ItemGripSockets` kapatılır) → yakın kavrama tümden kapalıdır, silah **yalnız uzaktan** alınır ve çerçeveden hiç ayrılmaz. ISDK `DistanceGrabInteractable`'ın pointer olaylarını dinler: ≤`maxGrabDistance` (2 m) nişan alınınca mavi ışın çizilir, `Select` gelince `WeaponGranter.SelectWeapon(...)` çağrılır — yani oyuncunun eline giden şey kaynağın bir **KLONU**dur. Aynı zamanda bir `IGameObjectFilter`'dır (mesafe kapısı `_interactorFilters`'a bağlanır). Çerçeve görselinin görünürlüğü `isFrameVisible` ile **örnek başına** (sahneden sahneye) ayarlanır — reçete `Gelistirici/Yemek-Kitabi.md`. ⚠️ **Çerçeve yalnız silah SABİT dururken vardır:** `Weapon.HeldChanged` dinlenir ve silah hangi yoldan tutulursa tutulsun (ele verildi ya da ISDK ile kavrandı) çerçevenin GameObject'i kapanır, bırakılınca geri gelir. Kural olayda durur, çağrı noktalarında değil — "silahı ele alan" birden çok yol var ve her birine ayrı ayrı "çerçeveyi de kapat" eklemek yeni bir yol açıldığında unutulacak bir adım olurdu. Abonelik bu yüzden `Awake`/`OnDestroy`'dadır: `OnDisable`'da olsaydı çerçeve kendini kapattığı anda "bırakıldı" sinyalini duyamaz ve bir daha geri gelmezdi |
| `Combat/WeaponGranter` | Silahın ele geldiği **tek nokta** (§3.9). **Kendini önyükleyen kalıcı tekil** — sahneye konmaz, bu yüzden yeni arenaya ek kurulum adımı doğurmaz. İki kaynağı vardır: (a) **`RandomGrant`** kuralında sahne süpürülür (çerçevedeki silahlar gizlenir — ⚠️ **taban bölgeleri BURAYA AİT DEĞİL**, onlar `BaseZoneVisibility`'dedir) ve grip basılıyken o elde rastgele bir silah durur, bırakılınca **yok olur**, tekrar basınca **yenisi** gelir (`Disposable`); (b) **`WeaponCanvas`** kuralında `WeaponFrame`'in çağırdığı `SelectWeapon` ile seçilen silahın **kalıcı klonu** tutulur — grip bırakılınca yalnız gizlenir, tekrar basınca arenanın neresinde olursa olsun aynı silah aynı mermiyle geri gelir (`Persistent`). Sahnedeki silah **tükenmez**, sınırsız kez seçilir. Oyuncu başına **tek** silah; seçim ancak başka bir çerçeveden alınarak değişir ve **harita başına** sıfırlanır. ⚠️ **(b) yolunda silahı sahneye koyan bileşen YOKTUR ve yazılmaz** — yerleşim arena kararıdır, harita tasarlanırken elle konur (`BaseZone` gibi prefab örneği olarak); silah konmamış bir arenada bu yol sessizce boş döner. Canlanmada seçim korunup silah `RefillFull` ile tam şarjör + rezervle döner — dolum yeri burasıdır. **İkinci dolum kapısı `countdown` mesajıdır:** her geri sayımın başında eldeki silah dolar. Tek başına canlanma yetmiyor — tur tabanlı modda turu **sağ bitiren** oyuncu canlanmaz ve yarım şarjörle yeni tura girerdi (§3.8.2). Kapı geri sayım olduğu için mod-agnostiktir. Admin'de rig kapalı olduğu için silah verme yolu kendiliğinden kapalı, süpürme ise çalışır. Dağıtım normalde rastgeledir; **yalnız editörde** `SequentialGrant` bayrağı (dev sandbox yazar, `#if UNITY_EDITOR`) onu loadout sırasına çevirir — bütün silahları tek tek gözden geçirmek için. Üretim davranışı değişmez |
| `Combat/WeaponGrantKind` | `None` / `Disposable` / `Persistent` — silahın **nasıl** verildiği (`Weapon.GrantTo`'nun ikinci argümanı). `Disposable` = FFA'nın rastgele silahı: rezerv yok, reload kapalı, her zaman tek elli. `Persistent` = çerçeveden seçilen silah: tam rezerv, reload AÇIK, ikinci el ön kabzayı tutabilir. **Neden tek bayrak değil:** `IsGranted` üç ayrı kuralı birbirine kilitliyordu ("elde sabit" + "reload kapalı" + "tek el/rezervsiz"); çerçeve silahı yalnız ilkini ister. ⚠️ Serialize EDİLMEZ (çalışma anı durumu), o yüzden "yeni değer sona" kuralı burada bağlayıcı değildir |
| `Combat/SimpleWeaponDissolve` | *(her `WPN_*` kökünde; `WeaponKitBuilder` takar ve `DissolveEffect.mat`'i bağlar)* Silah ele geldiğinde **çözülerek belirir**: model geçici olarak çözülme materyaline çevrilir, `_Dissolve` 1→0 sürülür (SmoothStep, `appearSeconds`), sonra özgün materyaller geri konur. **Yalnız beliriş vardır** — bırakışta efekt yoktur, silah anında gider ve yerinde kalan bir kopya bırakmaz. Kapı **`Weapon.HeldChanged`**'dir, çağrı noktaları değil — üç tutma yolu da (rastgele verilen silah, çerçeve klonu, ISDK kavraması) tek yerden karşılansın, yeni bir yol açıldığında sessizce unutulmasın (`WeaponFrame` aynı olayı aynı sebeple dinliyor). Silahın **kendi albedosu** (`_BaseMap`/`_MainTex` + `_BaseColor`/`_Color`) özgün materyalden okunup `MaterialPropertyBlock` ile taşınır: çözülme materyali TEK bir asset ve hangi silaha takıldığını bilmiyor, taşınmasaydı silah düz renkli bir siluet olarak çözülürdü. Materyal `.sharedMaterials` ile takılır (`.materials` her renderer için toplanmayan bir kopya üretirdi). Hedefler `Awake`'te bir kez toplanır: yalnız `MeshRenderer`/`SkinnedMeshRenderer` (namlu alevi/duman ve nişan ışını kendi materyalleriyle çizilir), `WeaponFrame`'in alt ağacı atlanır. ⚠️ `OnDisable` materyalleri geri koyar: obje kapanınca coroutine ölüyor — geri konmasaydı silah bir dahaki çağrılışında yarı çözülmüş belirir, üstelik property block'lu renderer SRP Batcher dışında kalmaya devam ederdi. ⚠️ **Kenar rengi/kalınlığı, desen sıklığı gibi görünüm ayarları bileşende YOKTUR ve eklenmez** — onların tek doğruluk kaynağı **materyaldir** (`_Edge_Color`, `_Edge_Width`, `_NoiseScale`, `_DissolveAxis`, `_DirectionStrength` orada ayarlanır); bileşen yalnız `_Dissolve`'u ve albedoyu yazar, materyalin geri kalanına dokunmaz. Serialize edilen alan bu yüzden yalnız iki tane: `dissolveMaterial` ve `appearSeconds` (süreyi `WeaponKitBuilder` her koşuda prefaba geri yazar). **İki materyal seçeneği var:** `DissolveEffect` (Simple Noise — yumuşak lekeler) ve `VoronoiDissolveEffect` (Voronoi — hücresel, "parçalara ayrılıyor"); ikisi de aynı property setini konuşur, yani bileşende yalnız materyal alanı değişir |
| `Combat/FrozenGrabTransformer` | Hiçbir şey yapmayan ISDK `ITransformer`'ı: kavranan nesneyi **yerinde dondurur**. Çerçevedeki kaynak silahın `Grabbable._oneGrabTransformer`/`_twoGrabTransformer` alanlarına bağlanır. ⚠️ **Alanları boş bırakmak hareketsizlik değil, SERBEST hareket demektir** — `Grabbable.Start` ikisi de boşsa kendisi bir `GrabFreeTransformer` üretir |
| `Player/ArenaNetCharacterBehaviour` | Movement SDK'nın ağ katmanı ile ArenaNet arasındaki **tek köprü** (§6.9/6.10). SDK'nın `INetworkCharacterBehaviour`'ını uygular: ürettiği blob'u `0x07` olarak yollar, gelen blob'u `NetworkCharacterHandler.ReceiveData`'ya verir, karakterin kökünü `LateUpdate`'te arena uzayına oturtur. **Rol ayrımının uygulandığı TEK yer**: `HasInputAuthority` yerelde `true` (sensör kaynağı `MetaSourceDataProvider` açık, gövde body tracking'den çözülür ve akar), uzakta `false` (kaynak KAPATILIR — açık bırakılsaydı her uzak avatar aynı yerel sensörü okurdu). ⚠️ Kaynak bileşen prefabdan **silinmez, yalnız kapatılır**: `CharacterRetargeter.Awake` onu kendi GameObject'inden `GetComponent` ile arıyor ve yoksa assert atıyor — tek prefabın hem yerel hem uzak çalışabilmesi bileşenin orada durmasına bağlı. ⚠️ **Kökü SDK değil bu sınıf yazar**: blob'un 0. eklemi gönderenin dünya uzayındadır ve blob opak olduğu için içeriden çevrilemez, o yüzden kök arena uzayında ayrıca taşınır (§6.9). ⚠️ `NetworkTime`/`RenderTime` **sunucunun tik saatinden** gelir (`RemotePlayerRegistry.TryGetServerTimeSeconds`), `Time.unscaledTime`'dan DEĞİL: SDK'nın interpolasyonu gönderenin damgasıyla alıcının render zamanını karşılaştırıyor, iki uç aynı epoch'ta olmazsa gövde 12 Hz basamaklarla oynar. ⚠️ `ReceiveStreamAck` **bilerek boştur** — ack yalnız delta sıkıştırma içindir ve delta kapalıdır (§6.9) |
| `Player/HandGripConvention` | Anchor (kumanda) uzayındaki el pozunu karakterin el kemiğinin bind eksenine çeviren **statik köprü**. Kemik anatomisi (parmak yönü = hand→MiddleProximal, avuç normali = parmak×başparmak) **modelden çalışma anında ölçülür**, sabit derece yazılmaz: karakter değişince burada tek satır değişmez. Sabit olan tek şey anchor tarafındaki el anatomisidir — **tek ayar noktası** budur ve bugünkü değeri ergonomik bir TAHMİNDİR; kesin değeri `HandGripCalibrationProbe` ölçer. Sol ve sağ ayrı hesaplanır; ortak bir ofset iki eli birden düzeltemez (§7). ⚠️ **Kapsamı daraldı: gövde artık buradan geçmez.** Kol/bilek zinciri Movement SDK retargeting'inden geliyor ve SDK kendi eşlemesini kendi yapıyor; bu köprünün bugünkü tek işi **eşyanın ele oturmasıdır** (kavrama soketi + uzak çizim), çünkü `ItemDefinition.primaryGrip` ölçüsü anchor uzayında alınmış. Buraya gövdeyle ilgili bir tüketici geri eklenirse retargeting ile ikinci bir eşleme kaynağı doğar |
| `Player/HandGripCalibrationProbe` | Yukarıdaki tahmini sabitin **kesin** değerini cihazda ölçen geliştirici aracı (`VA_CameraRig`'de durur): bir kez log basıp kendini kapatır, çıkan iki satır `HandGripConvention`'a yapıştırılır. Ölçüm kaynağı **BB rig'inin kumandadan sürdüğü el iskeletidir** (`OVRHandVisualLeft/Right → OculusHand_* → b_*_wrist`) — oyuncu kendi elini doğru yerde gördüğü için o iskelet "anchor'a göre el nerede" sorusunun canlı cevabıdır. ⚠️ Denenip elenen iki kaynak: `OVRInput.Controller.LHand/RHand` **multimodal** ister (projede kapalı), mesafeli kavrama önizlemesindeki kopyalar ise `ControllerModelHider` tarafından kapatılır (kapalı kemik sürülmez, bind pozu ölçülürdü). Oyun kodu onu OKUMAZ. ⚠️ Bugün ölçüm kaynağı da `ControllerModelHider` tarafından kapatılıyor (el görselleri tip eşleşmesiyle gizleniyor) — probe kullanılacaksa gizleyici geçici olarak devre dışı bırakılmalıdır |
| `Player/LocalBodyAvatar` | Oyuncunun **kendi gövdesi** — uzak avatarlarla **aynı prefabı** `Owner = Host` olarak kurar (`ArenaNetCharacterBehaviour.Initialize(playerId, hasInputAuthority: true)`), yani "kendi gördüğüm gövde" ile "başkalarının gördüğü gövde" tek doğruluk kaynağıdır. **Kendini önyükleyen kalıcı tekil** (`WeaponGranter` kalıbı): prefabı `Resources.Load("LocalBodyAvatar")` ile yükleyip sahne köküne kurar, sahneye elle KONMAZ → yeni arena bir kurulum adımı doğurmaz. Gövde ancak **iki koşul** birden sağlanınca kurulur: etkin bir `OVRCameraRig` (yani rol gerçekten oyuncu) ve sunucudan alınmış bir `playerId` (blob onunla etiketleniyor, §6.9); o ana kadar gizli durur — kurulmamış bir retargeter oyuncunun yüzüne dikilmiş bir T-poz mankeni olurdu. **Gövde kalibrasyonunun tetikleyicisi de buradadır:** `ArenaCalibrator.CalibrationGeneration` değişince 3 sn sonra `CharacterRetargeter.Calibrate()` çağrılır — gecikme zorunludur, oyuncu arena kalibrasyonunu zemine EĞİLEREK yapıyor ve o andaki poza sabitlenen gövde oranı maçın kalanı boyunca yanlış boy demektir. ⚠️ Avatar **sahne kökünde** durur, rig'in altına konmaz (§7, "retarget avatarı hareket eden kökün altına konmaz"). ⚠️ Gövdede **collider yoktur** — `Weapon`'ın atış raycast'i maskesiz, kendi gövden kendi atışını yerdi. Admin'de çizilmez ve bu rol kontrolüyle DEĞİL, etkin rig yoksa hiç çalışmayarak sağlanır (`AppSession` App asmdef'indedir, Core onu göremez) |
| `Player/LocalAvatarBoneHider` | Yerel gövdede oyuncunun görmemesi gereken kemikleri `Animator.GetBoneTransform` ile bulup **sıfıra yakın ölçekler** — mesh tek `SkinnedMeshRenderer` olduğu için renderer kapatmak seçenek değil. Varsayılan gizlenenler **Head, Neck, LeftUpperLeg, RightUpperLeg**: kamera kafa/boyunun içinde durur; bacaklar ise izlenmiyor (Quest 3'te bacakta sensör yok — `FullBody` seçilse bile alt gövde ÜRETİLİR), aşağı bakan oyuncu uydurma adımlar görürdü. ⚠️ Liste bir tercih değil **izlenmeyen uzuvların listesidir**: gerçek bacak izlemesi gelirse buradan çıkarılırlar. Kalan görüntü: omuzlardan aşağı kollar + gövde. `DefaultExecutionOrder` yüksektir — retargeting kemikleri her kare yazıyor, ölçek EN SON basılmalı. Ayrıca yerel gövdenin `SkinnedMeshRenderer.quality`'si **Bone4'e sabittir** (Auto değil): Quest'in "Mobile" seviyesi vertex başına 2 kemik veriyor ve 30 cm'den bakılan bilek onunla çöküyor (§7) |
| `Team` | `Red` / `Blue` / **`Neutral`**. `BaseZone`'da `Neutral` = herkese açık joker. ⚠️ Yeni değer SONA eklenir: `BaseZone`/`Weapon` bu enum'u serialize ediyor, başa ekleme her arenanın taban takımlarını kaydırır |

**`ArenaRoof`** (çatılı arenalar için, **isteğe bağlı**): çatı hiyerarşisinin köküne konur
(`GameObject > VortexArena > Arena Roof`), altındaki **tüm** Renderer'lar çatı sayılır ve
`ArenaRoof` katmanı (user layer 8) damgalanır — hangi geometrinin gizleneceği sahne görünümündeki
Layers süzgecinden görülsün. Katman yalnız ayıklama içindir; davranış Renderer listesinden gelir,
damga unutulsa da çalışır. Gizleme `MaterialPropertyBlock` üstünden `_BaseColor` alfasıyla;
tam gizlemede Renderer **kapatılmaz**, `ShadowsOnly`'ye alınır → çatı çizilmez ama gölgesini atar
(kapatılsaydı iç mekân aydınlanıp kuş bakışı okunmaz olurdu). Son uygulanan alfa statik tutulur,
yeni sahnedeki çatı `OnEnable`'da devralır → kuş bakışındayken arena değiştirilince çatı bir kare
bile görünmez. Oyuncu tarafında etkisi YOKTUR — yalnız `AdminSpectator.RefreshRoof()` tetikler.
**Yapımcıya verilecek tek parça teknik not: [`Cati-Gizleme.md`](Cati-Gizleme.md).**

### Editör: `VortexArena.Core.Editor` (içerik araçları — yalnız Editor)

Menü öğelerinin tam listesi ve "ne zaman çalıştırılır" tablosu `CLAUDE.md`'de; burada arena
geometrisini üreten iki araç + kavrama ayarı:

| Sınıf | Görevi |
|---|---|
| `GripSocketAuthoring` | Kavrama noktalarını **sahnede sürükleyerek** ayarlama aracı: `GameObject > VortexArena > Grip Socket (Primary/Secondary)` işaretçi üretir (mevcut SO değerlerinden başlatarak — araç ayarı sıfırlamaz), `Tools > VortexArena > Weapons > Write Grip Sockets To Definition` onu `WD_*.asset`'e yazar. **Var olma sebebi asimetri:** aynı sürüklenmiş poz `primaryGrip` için TERS bileşimle (`R=Inverse(localRot)`, `P=−(R·localPos)`), `secondaryGrip` için DÜZ yazılır — elle yapıldığında bu fark sessiz bir işaret hatası üretiyordu. Round trip birebir: geri okuma işaretçiyi aynı yere koyar. Yalnız **bulunan** işaretçinin alanları yazılır (yarısı ayarlı silahı sıfırlamasın); ölçek bulaşmasın diye `InverseTransformPoint` yerine elle bileşim |
| `DimensionMeshBuilder` | `JSON'dan DimensionMesh Üret`: boyut dosyasından **ölçü maketi** üretir — tek `Plane` (ProBuilder çokgeni, extrude 0) + kolon başına bir prizma (pivotu ayak izinin ağırlık merkezinde, sürüklemek doğal olsun diye) + iki kalibrasyon küpü (`anchor_a` kırmızı / `anchor_b` mavi, merkezleri noktanın üstünde — Inspector'daki konum dosyadaki nokta ile birebir aynı okunsun diye; dosyada nokta yoksa üretilmez ve uyarılır). ⚠️ **Duvar ÜRETMEZ ve maket oynanan geometri değildir**: kök `EditorOnly` etiketlenir, build'e girmez; arena sanatı maketin üstüne kurulur. ⚠️ **Kök SAHNEDEN BAĞIMSIZ kurulur**: sahne kökünde, dünya orijininde, dönüşsüz ve 1 ölçekte — hiçbir şeyin altına parent'lanmaz, böylece dosyadaki ölçü sahnede birebir okunur. Arenanın üstüne oturtmak isteyen elle taşır/döndürür; geri okuma maketin kendi kökünü referans aldığı için etkilenmez. **İdempotent**: aynı mekanın maketi varsa silinip yeniden kurulur. Üretimden önce halkayı `Polygon2D.IsSelfIntersecting` ile denetler |
| `DimensionMeshReader` | `DimensionMesh'i JSON'a Çevir`: maketi okuyup **kaynak dosyanın üstüne** yazar (hedef sorulmaz, maketin işaretçisinden gelir). Ayak izi çıkarımı: yatay yüzler (`\|normal.y\| > 0.9`) Y seviyesine göre gruplanır, **en alt** grup alınır (prizmada alt yüz kazanır), kenarlar XZ'ye izdüşürülüp kaynaştırılır, **yalnız bir kez geçen** kenar sınır sayılır, halka yürünür ve doğrusal ara köşeler ayıklanır. Noktalar dünya üstünden kök uzayına çevrilir — kolonu sürüklemek/döndürmek doğru yazılır. ⚠️ Kenarlar köşe **indeksiyle değil konumla** anahtarlanır: ProBuilder sert normaller için köşeleri yüz başına ayırıyor, indeksle bakan tespit tüm mesh'i sınır sanar. Kalibrasyon noktaları `DimensionAnchor` küplerinin transformundan okunur; ⚠️ küp yoksa dosyadaki `calibration` **KORUNUR** (sıfırlanmaz — eski bir maketi çevirmek mekanın zemin bandı ölçüsünü silerdi). Yazmadan önce çıktı geri ayrıştırılır; doğrulanamazsa dosyaya **dokunulmaz** |
| `TemplateBasicsLoader` | `Template Temellerini Yükle`: aktif sahneye altyapıyı **prefab örneği** olarak koyar (`VA_ArenaRoot`, `VA_CameraRig`, `VA_PoseSync`, `VA_CalibrationManager`; seçime bağlı `VA_ModeHud` · taban bölgeleri · `SpawnPoint`), `ArenaCalibrator`'ın sahneye bakan alanlarını bağlar, `ArenaBoundary`'nin rig'e bakan alanlarını (`head`/`fadeRenderer`/`warningText`) `VA_CameraRig` içinden bağlar ve mekanın boyut dosyasını `ArenaBoundary.dimensionsJson`'a takar. `anchor_a`/`anchor_b` işaretçilerini dosyadaki `calibration` noktalarına oturtur (`ArenaCalibrator.PlaceMarkerAtFloor` — çalışma anındaki yerleştirmenin aynısı; sahne yalan söylemesin diye. Otorite dosyadadır, sahnede taşımanın kalıcı etkisi yoktur). Taban bölgelerini takım malzemesiyle boyar (tek `VA_BaseZone` prefabı iki takıma da hizmet ediyor; şerit rengini çalışma anında kimse yazmıyor). **İdempotent** — var olan örneği asset yoluyla tanır ve atlar; dolu bir alanın üstüne YAZMAZ |
| `BuildElementsConfigurator` | `Configure All Build Elements`: kayıt listelerini **klasör ağacından eşitler** — `Venues/*/Scenes/*/` taranır ve klasör tek doğruluk kaynağı sayılır. **Hepsini Yapılandır** önce aktif sahnenin `MapDefinition`'ını yazar/günceller, sonra eşitler; **Yalnız Senkronize Et** sahne açık olmadan da eşitler (silinmiş bir arenanın kalıntısını temizlemenin yolu budur). Eşitleme: eksik olan **uyarı** üretir (kutuda sahne yok / birden çok sahne var / sahne adı klasör adıyla uyuşmuyor / `Data/<Sahne>.asset` MapDefinition yok ya da yanlış yerde / mekan kökünde `Art,Data,Prefabs,Scenes` dışında klasör), fazla olan **silinir** (Build Settings'te mekan ağacında olmayan ya da diskte bulunmayan satırlar; `GameCatalog.maps` ve `ModeDefinition.maps` içindeki ölü ve artık taranmayan referanslar). `Boot.unity` index 0'da kalır, mekan-dışı sahneler (`_Shared/Scenes/*`) korunur, `Template/` sahneleri listeye hiç girmez. `ModeDefinition.maps` **boşsa** dokunulmaz (boş = kısıtsız); doluysa o modu destekleyen haritalarla birebir eşitlenir — hedef küme boş çıkarsa liste boşaltılmaz (boş liste "kısıtsız" demek olurdu), yalnız uyarı basılır. Sonda `ServerConfigExporter.Export(false)` + **sağlık raporu** (`SpawnPoint` sayısı, `dimensionsJson` dolu mu, maket `EditorOnly` etiketli mi). ⚠️ **MapDefinition kendiliğinden ÜRETİLMEZ:** `supportedModeIds` boş bırakmak "kısıtsız" demek olduğu için üretilen boş bir tanım lobiyi sessizce her modda oynanır kılardı — sahne açılıp modlar araçtan seçilir. Ayrı bir "Arena Id" alanı yoktur: MapDefinition'ın adı sahne adıdır |

### Sunucu: `Server/VortexArena.Server.Core`

| Sınıf | Görevi |
|---|---|
| `ControlHost` | Kestrel WebSocket host (`/ws`), bağlantı başına `ClientConnection` |
| `BeaconService` | 2 sn'de bir broadcast |
| `StateHost` | UDP kaydı, poz alımı, 20 Hz snapshot yayını (16 girdiden fazlası MTU'ya sığan parçalara bölünür; olay varsa ve sığıyorsa `0x05` ile tek datagramda birleşir), `0x06` RTT echo'su. **Telemetriyi burada üretir:** saniyelik `[state]` satırı — gerçek bayt-sn/paket-sn, tik kayması, uplink jitter + poz/olay kaybı; eşiği aşan oyuncu için ek `[net]` satırı |
| `PlayerRegistry` | Oyuncu listesi, `playerId` tahsisi (1..255), `devices.json` ile kalıcı **kimlik** (ad + forma numarası), çevrimiçi/çevrimdışı. **Kimlik:** ilk bağlantıda ad 20'lik havuzdan rastgele (kullanılmayanlar arasından), numara 1'den itibaren ilk boş (1..99); `set_identity` ikisini de değiştirir. Adlar tekrar edebilir, **numara tüm KAYITLI cihazlar arasında benzersizdir** — sahiplik sorgusu `_players`'a değil `_devices`'a bakar (hiç bağlanmamış cihaz da numara tutar). Çevrimiçi sahipten numara istenirse reddedilir; çevrimdışı sahip **aynı anda** yeniden numaralanır. **Rol başına kalıcılık farkı:** oyuncu kaydı kopunca Offline işaretlenir ama DURUR (deviceId kalıcı); **admin kaydı tümüyle SİLİNİR** (deviceId oturumluk — yoksa her açıp kapatma roster'da hayalet satır ve tükenen playerId bırakırdı) ve admin adı diske yazılmaz. Aynı PC'de iki admin varsa ad " (2)" ile ayrıştırılır. **Atma bunun istisnasıdır** (`RemoveByPlayerId`): oyuncu kaydı da silinir — kopma "çevrimdışı" bırakır, atma bırakmaz; `devices.json`'a dokunulmaz, yani atılan cihaz geri bağlanırsa adını/numarasını korur (§5.4) |
| `LobbyService` | Roster yayını (`lobby_state`) — **tek yayıncı döngüden**, kirli bayrakla birleştirilerek, her yayında `version` artarak (Tuzaklar: "ateşle-unut yayın sıra garantisi vermez"); `status.rosterVersion` geride kalan istemciye yalnız ona tam snapshot yollatır. Ayrıca ready/takım/kick/`set_identity` + **adminler arası ortak durumun sahibi**: mod/harita seçimi burada yaşar, `set_selection` ile değişir, `admin_state` ile yalnız adminlere yayılır. Her admin komutu "kim ne yaptı" duyurusu üretir |
| `MatchDirector` | **Faz makinesi (10 Hz tick), vuruş hattı, can/skor, canlanma, zorla canlandırma.** Mod kaydı tek yerde (`RegisterModes()` — yeni mod buraya bir satır). **Skor defteri:** `AddScore(team,…)` (takım) + `AddPlayerScore/ScoreOf/TryGetLeader` (bireysel); modlar skoru YALNIZ buradan yazar. **Mod komutları** (§3.8.2): `TryPauseForMode` / `SetModeState` / `TryStartRound` / `TryCancelCountdownForMode` — modun fazı doğrudan yazmasını (ikinci otorite) ve kendi mesajını yollamasını (ikinci gönderici) gereksiz kılar. **Dost ateşi anahtarının da sahibi burasıdır** (§3.9): açılışta kapalı, yalnız `set_friendly_fire` çevirir, yürürlükteki kural şekline `ApplyRulesLocked` damgalar. ⚠️ **Takımdaş öldürmede `OnKill` çağrılmaz** — skor yazılmaz, `kills`/`deaths` ve kill feed işler |
| `MapTable` | `maps.json` (Unity export'undan) — sunucunun okuduğu tek içerik tablosu. Girdi başına yalnız `sceneName` + `modes`; **arena ÖLÇÜSÜ yoktur** (sunucu metre kullanmaz, §7.30) |
| `Modes/IGameMode` + `TdmMode` + `FfaMode` | Mod kuralları: skor, kazanma koşulu, tur süresi. Yeni kancalar **varsayılan gövdeyle** eklenir (default interface method) → mevcut modların hiçbiri değişmez; **tüketicisi olmayan kanca EKLENMEZ**. `FfaMode` yüzeyin ilk tüketicisidir: takımsız + bireysel skor + sabit durma canlanması, `MatchDirector`'a tek satır kayıt dışında hiçbir dokunuş yok. `OnRoundStart` ikinci örnektir: Live'a HER girişte çağrılır, tur kavramı olmayan modlar hiç yazmaz |
| `Modes/TournamentMode` | **Tur tabanlı takım elemesi** (§3.8.2). Kural olarak TDM'den tek farkı `Revive = None`'dır; turun tamamı bu sınıfın iç durumudur (`_round`, `_roundLive`, `_matchOver`). Eleme `OnKill`'de değil **`OnTick` taramasında** ölçülür — takım bağlantı kopmasıyla da boşalır ve o yolda `OnKill` çağrılmaz; tek tarama tek doğruluk kaynağıdır. Süre dolunca **savaşabilir** (canlı **ve** kalibreli) sayısı fazla olan tur alır, eşitse kimseye puan yok. Toplanma kapısı `set_ready` bayrağını yeniden kullanır ve **zaman aşımı yoktur**: tur herkes tabanına girmeden başlamaz, geri sayım her koşulda iptal edilebilir, çıkış operatörün `kick`/`abort_match` komutudur. Bekleme uzarsa 30 sn'de bir konsola teşhis satırı basar (tur başlatmaz). ⚠️ `IsMatchOver`'da `TimeRemaining <= 0` dalı YOKTUR: bu modda o sayaç **turun**dur |
| `Modes/ModeRules` | Modun ŞEKLİ (§3.9): takım kipi, skor kanalı, dost ateşi, canlanma, silah kaynağı, canlanma gecikmesi. `ToInfo()` ile tele çıkar. Varsayılanı (`TeamDefault`) bugünkü TDM'dir. ⚠️ `FriendlyFire` alanını **modlar yazmaz** — onu `MatchDirector` operatörün anahtarından damgalar |
| `Modes/MatchOutcome` | Maç sonucunun tek tipi: kazanan takım **veya** kazanan oyuncu (`match_end`'in iki kanalı) |

---

## 5. Nasıl kullanılır — kod reçeteleri

### 5.1 Sunucudan gelen bir olayı dinlemek

```csharp
using VortexArena.Net;
using VortexArena.Protocol;

private void OnEnable()  => NetEvents.OnHealthUpdate += HandleHealth;
private void OnDisable() => NetEvents.OnHealthUpdate -= HandleHealth;

private void HandleHealth(HealthUpdateMsg msg)
{
    // Ana thread'de çağrılır — Unity API'sini doğrudan kullanabilirsin.
    if (msg.playerId == ArenaClient.Instance.PlayerId) { /* kendi canım */ }
}
```

Mevcut olaylar: `OnConnected`, `OnDisconnected`, `OnConnectionStateChanged`, `OnLobbyState`,
`OnLoadMatch`, `OnMatchState`, `OnCountdown`, `OnHealthUpdate`, `OnKillEvent`, `OnRespawn`,
`OnMatchEnd`, `OnReturnToLobby`, `OnRemoteFireEvent`, `OnIdentify`, `OnKicked`,
`OnRulesUpdate` (koşan maçın kural şekli değişti — `ModeRuntimePump` dinler),
`OnSelectionState`, `OnAdminState`, `OnNetStats` (son ikisi yalnız admin bağlantısında).

> **Abonelikleri simetrik yaz** (`OnEnable`/`OnDisable`) — `NetEvents` statiktir, sahne değişse de
> yaşar; unutulan abonelik yok olmuş nesneye erişir.

### 5.2 Sunucuya mesaj göndermek

```csharp
ArenaClient.Instance.Send(new SetReadyMsg { ready = true });
ArenaClient.Instance.Send(new StartMatchMsg { modeId = "tdm", sceneName = "Arena12x12" }); // admin
```

`Send<T>` fire-and-forget'tır: soket kapalıysa **no-op**, hata loglanır ve yutulur (reconnect
döngüsü zaten kurtarır). Bu yüzden "bağlı mıyım?" kontrolüyle kod kirletmeye gerek yok — ama
yerel sunum (ses/VFX) gönderimden bağımsız çalışmalı.

### 5.3 Yeni bir mesaj tipi eklemek (5 adım, sırası önemli)

1. **`Docs/ArenaNet-Protokol.md`'yi güncelle** (tek doğruluk kaynağı kuralı — kod önce yazılmaz).
2. `MessageTypes.cs` → yeni `const string`.
3. `ControlMessages.cs` → `[Serializable]` DTO. **Kurallar:** yalnız public alan (property değil),
   Dictionary/polimorfizm yok, camelCase alan adları protokoldeki ile birebir, `UnityEngine` yok.
4. Sunucu: `ClientConnection` (giriş) veya `MatchDirector`/`LobbyService` (yayın) tarafında işle.
5. İstemci: `ArenaClient`'ın mesaj switch'ine parse satırı + `NetEvents.RaiseX` + `NetEvents`'e
   `public static event`.

### 5.4 Yerel oyuncunun durumunu okumak

```csharp
var s = PlayerCombatState.Instance;
if (s != null && s.CanFire) { /* ateş serbest (faz Live + canlı) */ }
```

`Weapon` zaten bu kapıyı kullanır; can/faz/takım bilgisi hep sunucudan gelir, yerel olarak
değiştirilmez.

### 5.5 Uzak bir oyuncunun pozunu okumak

```csharp
if (RemotePlayerRegistry.Instance.GetInterpolatedPose(playerId, out Pose head, out Pose l, out Pose r))
{
    // Pozlar ARENA uzayında → dünyaya çevir:
    transform.SetPositionAndRotation(ArenaSpace.ArenaToWorld(head.position),
                                     ArenaSpace.ArenaToWorld(head.rotation));
}
```

### 5.6 Yeni bir poz kaynağı eklemek

`IPoseSource` uygula, arena uzayına **kendin çevir** (`ArenaSpace.WorldToArena`) ve
`UdpStateChannel`'a kaydol — `PlayerPoseTracker` bunun referans uygulamasıdır.

### 5.7 Sahne objesine ağ kimliği vermek

Hiyerarşide sağ tık → **`VortexArena > Network Parent`**: objeye `NetIdentity` ekler ve benzersiz
`sceneId` atar. Sahne kaydedilirken `SceneIdGuard` 0 kalan/çakışan id'leri onarır (sahne kopyalayınca
oluşan çakışmalar otomatik düzelir). Bu altyapı dinamik obje senkronu (kapı, pickup) içindir;
oyuncular `playerId` ile senkronlanır, `NetIdentity` gerekmez.

---

## 6. Günlük iş akışı

### 6.1 Sunucuyu çalıştırmak

```powershell
dotnet run --project Server/VortexArena.Server.App
```

İlk kurulumda **bir kez**: `Server/firewall-kur.cmd` → sağ tık → yönetici olarak çalıştır.
Ağ profilini Private yapar, otomatik ENGELLE kurallarını siler, UDP 47820 / TCP 47821 / UDP 47822
izinlerini ekler ve teşhis basar (adaptörler, IP'ler, dinlenen portlar). **Admin console çalıştıran
diğer PC'lerde de çalıştırılır** — beacon broadcast olduğu için istemcide de inbound izin gerekir.
Detay: `Server/README.md`.

Açılışta **hangi mekanın oynatılacağı sorulur** (§11.1 — `maps.json`'daki `venue` alanı, o da
Unity'deki klasör yerleşiminden gelir). Seçim oturum boyunca sabittir: yalnız o mekanın haritaları
`start_match` ile başlatılabilir ve admin harita seçicisinde yalnız onlar görünür
(`admin_state.venueScenes`). Soruyu atlamak için `server.json → venue` doldurulur ya da
`--venue <ad>` verilir; konsol etkileşimli değilse sunucu bloklanmaz, ilk mekanla açılır.
Mekanı değiştirmek = sunucuyu yeniden başlatmak.

Sunucuyu **operatör launcher'ı da başlatabilir** ve başlatırsa mekanı her seferinde `--venue` ile
geçer — sessiz "ilk mekan" yoluna hiç girilmez (launcher mekan seçilmeden başlatmaz). Launcher
sunucuyu **kapatmaz**: ömrü operatör uygulamasına bağlı değildir, kapatma sunucunun kendi
penceresinde Ctrl+C'dir. Sunucu istenirse eskisi gibi elle de çalıştırılır.

Bu **geliştirirken de geçerlidir**: dev penceresinin (`Tools > VortexArena > Development > Dev`) sunucuyla hiç
işi yoktur — ne başlatır, ne öldürür, ne derler (elle başlatılmış bir sunucunun Play çıkışında ya
da editör kapanışında ölme riski kalmasın diye). Derleme `dotnet build Server/VortexArena.Server.sln
-c Release` (ya da `scripts\deploy-server.bat`), çalıştırmak yine elle:
`deploy\server\VortexArena.Server.App.exe`.

### 6.2 Quest olmadan test (loopback) — `Tools > VortexArena > Development > Dev`

Editörde rol ve sunucu adresi **Inspector'dan DEĞİL** dev penceresinden seçilir. İki katmanlı:
**hedef kataloğu** repo'da commit'lidir (`dev-targets.json`: `Local`, `Kesif (beacon)`, `Ornek-PC`
+ `defaultTarget`/`defaultRole` — işletme PC'leri buraya eklenir), **hangi hedefin seçili olduğu**
kişiseldir ve
`EditorPrefs`'te durur (`VortexArena.Dev.*`). Kazanç: rol/hedef değiştirmek hiçbir dosyayı
kirletmez, `git status` temiz kalır. Bir hedefin `ip`'si **boşsa** adres yazılmaz → istemci
üretimdeki keşif zincirini kullanır (`Kesif (beacon)` hedefi bilinçli olarak böyledir).

| Pencerede | Ne yapar |
|---|---|
| **Rol** (Player / Admin) | `AppSession.Role`'ü Boot koşmadan önce yazar. **Kısayol `Ctrl+Alt+R`** — pencere kapalıyken de çalışır (sahne görünümünde bildirim + konsol satırı) |
| **Hedef** + "Tazele" / "Özel…" | Adres; `Özel…` seçilirse IP/Port elle girilir (IP'yi boş bırakmak = keşif zinciri) |
| **Başlangıç: Boot'tan** | `playModeStartScene` = Boot sahnesi → hangi sahne açık olursa olsun Play gerçek akıştan başlar (sahne Build Settings'ten bulunur) |
| **Başlangıç: Açık sahneden** | Arena sahnesine doğrudan Play. Bir kare sonra `DevSession` **seçili hedefe bağlanır** — arena sahnesinde `LobbyController` olmadığı için bunu başka kimse yapmaz; bağlanmazsa can/skor/faz gelmez ve `CanFire` hiç açılmaz. Maç verisi (takım / mod / süre / limit / faz) **yalnız sunucudan** gelir: `welcome.match` geç-katılım senkronu (`SceneRouter`) ya da gerçek `load_match`. Sunucuda koşan bir maç yoksa istemci maç verisi almaz — bu beklenen davranıştır, bir **admin** maçı başlatmalıdır. Hedef "keşif" kipindeyse (ip boş) bağlanılmaz ve sebebi loglanır — arena sahnesinde adres girecek arayüz yok |
| **Sunucusuz sandbox** (+ mod) | Sunucuya **hiç bağlanılmaz**: sunucu açma, admin'den harita seçme ve elle kalibrasyon üçü de atlanır. `DevSession` bağlanmak yerine `ModeRuntime.Apply` ile iki alan yazar — seçilen `modeId` (**silah loadout'u buradan okunur**; onsuz silah gelmez) ve `fireWhilePaused = true` (faz sunucusuz `paused` kaldığı için tetiği açan tek kapı). **Tek seçim moddur;** silah kaynağı seçilmez: sandbox her zaman grant yolunu kullanır (`weaponSource:"random"`) ve `WeaponGranter.SequentialGrant` ile loadout **SIRAYLA** dağıtılır — grip'e her basışta bir sonraki silah. Çerçeve yolu sandbox'ta kullanılmaz (amaç silahı hemen ele almak). Yalnız **"Açık sahneden"** başlangıcıyla ve **kabuk dışı** bir sahnede (arena ya da mekan lobisi) çalışır; ikisi de sağlanmazsa konsola uyarı düşer. ⚠️ Hasar/skor/faz **YOKTUR** — üçünün de otoritesi sunucudadır; bu kip yerel şeyler (silah duruşu, namlu, ses, kavrama) içindir |
| **"Dev enjeksiyonu" onayı** | Kapatılırsa üretim yolu **birebir** koşar (rol `AppBoot`'tan, adres keşif zincirinden) — beacon keşfini editörde denemenin yolu |

Editör **player** rolündeyken ortamda maçı başlatacak bir admin kalmaz: ikinci bir istemciyi
**admin** rolünde bağla (ikinci bir editör/Windows admin build'i) ya da rolü `Ctrl+Alt+R` ile
çevirip maçı oradan başlat. ⚠️ Bu adım **gerçek maç akışı için** zorunludur — editörün maç verisi
üreten bir kısa yolu yoktur, maçı her zaman bir admin başlatır.

**Maç akışı gerekmiyorsa sandbox kipi bu üçlüyü tümden atlar.** Silah duruşu/namlu/ses/kavrama
gibi tümüyle YEREL şeyleri denemek için sunucu, admin ve kalibrasyon gerekmez: sandbox açıkken
sunucuya hiç bağlanılmadığı için kalibrasyon kapısı da kendiliğinden açık kalır
(`CalibrationState.IsCalibrated` = `!_hasEverConnected`) ve `ArenaCombat` UDP kanalı yokken
sessiz no-op olduğu için silahlar olduğu gibi çalışır. ⚠️ Sandbox **sunucudan gelmiş gibi mesaj
üretmez** ve bir maç simülasyonu değildir: hasar, skor, faz ve canlanma yoktur; takım/skor/canlanma
kuralları `ModeRulesInfo` varsayılanında (TDM) kalır. Gerçek kural davranışı test edilecekse
sunucu + admin yolu kullanılır — sapmada sunucu kazanır (§10.5).

### 6.3 Build ve dağıtım

Dört bileşenin her biri kendi script'iyle `deploy/` altına üretilir:

| Komut | Ne yapar | Çıktı |
|---|---|---|
| `scripts\deploy-admin-game.bat` | Unity batch-mode Windows build (`PlayerBuildTool.BuildWindowsAdmin`) | `deploy\admin\VortexArena.exe` |
| `scripts\deploy-player-apk.bat` | Unity batch-mode Android build (`PlayerBuildTool.BuildQuestPlayer`) | `deploy\player\game.apk` + `install_game.bat` |
| `scripts\deploy-server.bat` | `dotnet publish -r win-x64 --self-contained` + `config/` kopyası | `deploy\server\VortexArena.Server.App.exe` |
| `scripts\deploy-launcher.bat` | `dotnet publish -r win-x64 --self-contained` | `deploy\launcher\VortexArena.Launcher.exe` |

**İki Unity build'i tek sahne listesini paylaşır.** Windows build'i admin, Android build'i Quest
oyuncusudur; ikisi de Build Settings'teki etkin sahneleri aynen kullanır. Liste platforma göre
ayrıştırılmaz — ayrışsaydı bir arenayı admin bilir oyuncu bilmez olurdu ve `start_match` sessizce
reddedilirdi (sahne TÜM oyuncuların `hello.scenes` listesinde aranır). `PlayerBuildTool` build'e
girmeden önce **diskte olmayan sahne satırlarını** yakalayıp adlarıyla iptal eder: silinmiş bir
arenanın satırı Build Settings'te kalabiliyor ve o hâlde `BuildPipeline` sebebi görünmeyen bir
yığın iziyle düşerdi.

- **İki Unity build'i de canlı ilerleme basar.** İkisi de Unity'yi doğrudan değil
  `scripts\lib\watch-unity-build.ps1` üzerinden çalıştırır: izleyici kendi log'unu
  (`deploy\admin-build.log` / `deploy\player-build.log`) akarken okur ve tek satırlık durum
  gösterir (aşama · Bee yüzdesi · o an çalışan araç · log
  boyutu · CPU). Batch-mode Unity konsola hiçbir şey yazmadığı için "takıldı mı ilerliyor mu"
  başka türlü görünmüyordu. Log ~3 dk büyümez ve CPU da harcanmazsa uyarı basılır; hata satırları
  (proje kilidi, `error CS…`) anında ekrana düşer. Post-mortem: aynı betik `-ReplayLog <log>` ile
  bitmiş bir log'un aşama haritasını çıkarır. Süre `<log>.last`'a yazılır ve sonraki
  koşuda "~mm:ss" referansı olarak gösterilir. Ayrıntı: `scripts/README.md`.
- **Unity build'leri editör AÇIKKEN alınamaz** — batch-mode Unity proje kilidine takılır. Script
  bunu **kontrol etmez** (bilinçli: editör kapatıldıktan sonra bile AI motoru gibi alt süreçlerin
  `Unity.exe`'si arka planda yaşıyor, `tasklist` kontrolü yanlış alarm veriyordu). Build
  ilerlemiyorsa Ctrl+C ile iptal edip süreçleri kapat (izleyici Unity'yi de kapatır). Önceki
  `deploy\admin-build.log` silinemezse script uyarır — o dosyayı hâlâ bir Unity süreci tutuyor
  demektir.
- **Sunucu ve launcher build'i yalnız .NET 10 SDK ister.** İkisi de self-contained tek klasör
  üretir; işletme PC'sine .NET kurmak gerekmez. Launcher açıkken `deploy\launcher\` kilitli olur —
  script bunu kontrol edip uyarır.
- **Hedef platform betikte sabittir, aktif platformdan türetilmez.** İki Unity betiği de Unity'yi
  kendi hedefiyle **başlatır** (`deploy-admin-game.bat` → `-buildTarget Win64`,
  `deploy-player-apk.bat` → `-buildTarget Android`); bayrak açılışta verilir çünkü platformu
  `-executeMethod`'un içinden çevirmek `SwitchActiveBuildTarget` ile domain reload tetikleyip
  çalışan metodu yarıda bırakıyor. Projede hangi platform açık kalmış olursa olsun her betik kendi
  çıktısını üretir. Aktif platform hedefe eşit değilse geçiş açılışta olur ve **o koşu tam
  reimport** demektir (texture'lar yeniden sıkıştırılır: Quest'te ASTC, Windows'ta DXT — 20-40 dk);
  sonrakiler hızlıdır. Platform build sonunda **geri alınmaz**, geri almak ikinci bir tam reimport
  daha olurdu. `deploy-player-apk.bat` ayrıca Android Build Support modülünü build'e girmeden önce
  doğrular (`Data\PlaybackEngines\AndroidPlayer`): modül yoksa Unity platformu çeviremez, sessizce
  Windows'ta devam edip `.exe` üretirdi.
- **İki Unity build'i birbirinin cache'ini ısıtmaz.** `Library/` ortaktır ama içindeki şeritler
  platform başınadır: shader varyantları grafik API'sine göre anahtarlanır (admin d3d11, oyuncu
  vulkan), asset artifact'ları sıkıştırma biçimine göre, script derleme çıktıları hedefe göre ayrı
  klasörlerde. Her platform kendi cache'ini bir kez ısıtır; **soğuk cache'te build süresinin büyük
  kısmı shader varyantı derlemektir.** Pratik sonuç: `Library/`'yi silme, ve aynı gün ikisi de
  gerekiyorsa önce admin sonra APK al.
- **APK kurulumu:** `install_game.bat` → `adb install -r -g`. Betik APK'yı **sırayla kendi yanında,
  `deploy\player\` ve `Builds\player\` altında** arar, ilk bulduğunu kurar — bu yüzden repo
  kökündeki kopya da `deploy-player-apk.bat`'in APK yanına bıraktığı kopya da çalışır ve dosya
  taşımak gerekmez. **Aynı APK her gözlüğe kurulur** — rol ve sunucu adresi gömülü değildir,
  oyuncu build'i sunucuyu UDP beacon ile kendi bulur.
  Cihaz `unauthorized` görünüyorsa betik kuruluma **hiç girmez**: sebebi yazar (gözlük bu PC'nin
  RSA anahtarını kabul etmemiş — geliştirici modu açık olsa bile bu ayrı bir onaydır, kablo/sürücü
  sorunu değildir), izin isteyip `adb kill-server` + `adb start-server` ile onay penceresini
  yeniden tetikler, gözlükte izni verdin mi diye sorar ve cevabı `adb devices`'a tekrar sorarak
  **teyit eder** — yalnız teyit geçerse kurar. Hâlâ yetkisizse yetkilendirmeleri iptal etme /
  `adbkey` sıfırlama yolunu yazıp tekrar dener.
  ⚠️ Onay penceresinde **"bu bilgisayardan her zaman izin ver" işaretlenmezse** yetki yalnız o
  bağlantı için geçerlidir: adb server'ı her yeniden başladığında cihaz `unauthorized`'a döner ve
  onay yeniden istenir.
- Boot sahnesi build listesinde **index 0** olmalı; tüm arena sahneleri listede olmalı
  (`Configure All Build Elements` sahneyi listeye kendisi ekler).

**Operatör akışı (işletmede):** launcher'ı aç → (bir kez) sunucu exe'si + **mekan**, sunucu IP'si,
admin exe'si → **Sunucuyu Başlat** → **Yönetimi Başlat**. Sunucu `--venue <mekan>` ile açılır; oyun
`--server-ip` ile açılır, IP sormaz, doğrudan dashboard'a düşer. Ayrıntı: `deploy/README.md`,
`launcher/README.md`.

### 6.4 İçerik eklemek (özet — tam reçeteler `CLAUDE.md`'de)

| İstek | Yol |
|---|---|
| **Yeni arena** | Altı adım, tek düğmeli sihirbaz YOK: boş sahne → arena kutusuna kaydet (`Venues/<İşletme>/Scenes/<SahneAdı>/<SahneAdı>.unity` — klasör adı = sahne adı) → `Template Temellerini Yükle` (altyapı prefab örnekleri + boyut dosyası bağlama) → `JSON'dan DimensionMesh Üret` (mekanın ölçü maketi — sahneden bağımsız, dönüşsüz kurulur; sırası serbesttir) → ölçü tutmuyorsa köşeleri ProBuilder ile düzeltip `DimensionMesh'i JSON'a Çevir` → environment sanatı + tek `SpawnPoint` (zemin seviyesinde) + bake → **`Configure All Build Elements`** (MapDefinition + katalog + mod listeleri + Build Settings + `maps.json`, tek geçişte). ⚠️ **Ölçekleme yoktur**; maket build'e girmez ve duvar üretmez — arenanın duvarları environment sanatına aittir ve fiziksel sınırla çakışmalıdır |
| **Yeni silah** | `WeaponKitBuilder` tablosuna satır ekle (istatistik + ses profili + pack modeli = köken kaydı) → `Tools > VortexArena > Weapons > Build Weapon Prefabs` → `WD_*.asset` üretir, **mevcut** `WPN_*.prefab`'ı yerinde günceller (ses + namlu alevi/dumanı + kovan kiti dahil), `WeaponCatalog`'u tazeler → gerekiyorsa `ModeDefinition.loadout` + sahneye yerleştir. **Export GEREKMEZ** (sunucuda silah tablosu yok). ⚠️ Araç **mevcut prefabların `Muzzle`/`Model` yerleşimine DOKUNMAZ**, yalnız definition bağlarını + ses/VFX/kovan kitini tazeler — VR'da elle ayarlanmış tutuş/namlu konumu tekrar çalıştırmakla bozulmaz. Paylaşılan şablon yoktur: sıfırdan farklı gövde için mevcut bir `WPN_*` prefabını kopyalayıp `Model` altındaki pack prefabını ve `definition`'ı değiştir, sonra *…(Yalnız Kataloğu Tazele)* çalıştır. ⚠️ **Ses klipleri yalnız alan BOŞSA yazılır** (elle sürüklenen klip korunsun diye): mevcut bir silahın sesini tablodan değiştiriyorsan önce `WD_*.asset`'teki klip alanlarını boşalt, yoksa değişiklik sessizce hiç inmez (Tuzaklar: "elle atanmışsa ezme"). Diğer alanlar (hasar/rpm/menzil/saçılım/kimlik) her koşuda ezilir |
| **Yeni mod** | Unity: `Assets/Modes/<Ad>/Scripts/VortexArena.Modes.<Ad>.asmdef` (refs: Core, Net, Protocol) + Sunucu: `Modes/<Ad>Mode.cs : IGameMode` → `MatchDirector` ctor'unda `Register(new <Ad>Mode())` + protokol dokümanına `modId` |
| **Hazır bir sahneyi arenaya çevirmek** | Aşağıdaki adımlar — araçları kullanmadan, elle |

**Hazır/dışarıdan gelmiş bir sahneyi ağa bağlama** (araçların ne yaptığını elle yapmak; normal yol
yukarıdaki altı adımdır):

1. Sahneyi arena kutusuna taşı: `Assets/Arenas/Venues/<İşletme>/Scenes/<Ad>/<Ad>.unity`
   (+ `Data/<Ad>.asset`; sahneye özel sanat/prefab varsa `Art/`, `Prefabs/` — mekanın tümüne aitse
   mekan kökündeki ortak klasörlere). **Klasör adı, sahne adı ve MapDefinition asset adı ÜÇÜ DE
   aynıdır**; sahne adı katalog anahtarıdır — sonradan değiştirme.
   ⚠️ Mekan klasörü dışına konan arena export'a girmez, yani sunucuda hiç görünmez.
2. Arena çerçevesini kur: alana hizalı bir objeye **`ArenaBoundary`**
   (`head` = `CenterEyeAnchor`, `fadeRenderer`/`warningText` = rig altındaki
   `OutOfBoundsFade`/`BoundaryWarningText`; duvar Renderer'ı diye bir alan YOKTUR).
   **Ölçüyü mekanın boyut dosyasına yaz** (`Venues/<İşletme>/Data/<İşletme>_dimensions.json`) ve
   `dimensionsJson` alanına bağla — alan tam kare olsa bile dört köşeli bir `plane` halkası olarak
   girilir, ölçü için bileşende ayrı bir alan YOKTUR. Dosya bağlanmazsa muhafaza hata basıp kendini
   kapatır. Plan koordinatları bu transformun yerel XZ'sindedir; ölçüyü bir köşeden alıyorsan o köşe
   `(0,0)` olur (plan sıfırının arena origin'i olması gerekmez). Zemin bandının A/B noktalarını da
   aynı dosyanın `calibration` alanına yaz — işaretçilerin sahnedeki yeri oradan gelir. Elle konmuş
   kolon/kasa varsa üstlerine `ArenaObstacle` ekle.
3. Taban bölgeleri: iki `BaseZone` (Red/Blue, karşı kenarlarda; `Neutral` = herkese açık).
   Ölen oyuncu bunlardan birine fiziken girince canlanır — rig ASLA taşınmaz.
   Ayrıca **tek** başlangıç noktası: `GameObject > VortexArena > Spawn Point` ile ekle ve elle
   yerleştir. Bu marker **arena origin'idir** — tüm ağ pozları buna göre çevrilir, bu yüzden
   **zemin seviyesinde** durur ve sonradan taşınmaz.
4. Ağ objeleri: `_Shared/App/Prefabs/` altındaki prefabları sahne köküne **örnek olarak** sürükle —
   `VA_CameraRig` (kamera/kumanda + etkileşim rig'i + yerel gövde avatarı), `VA_PoseSync`
   (`PlayerPoseTracker` + `RemotePlayerSpawner`), `VA_CalibrationManager` (`ArenaCalibrator`),
   `VA_ModeHud` (`ModeHudSpawner`). **Başka bir arenadan kopyalama** (kopya prefab bağını kaybeder,
   rig/kalibrasyon düzeltmeleri o sahneye ulaşmaz). Sonra sahneye bakan referansları elle bağla:
   `VA_CalibrationManager`'ın `rigRoot`'u ile `ArenaBoundary`/`BaseZone`'un `head` alanı → sahnenin
   `CenterEyeAnchor`'ı. Boş bırakılırsa sahne sessizce çalışmaz; Unity kopuk sahneler-arası
   referansı sessizce null yapar. (`anchorA`/`anchorB` istisnadır: boş bırakılabilir, kalibratör
   `anchor_a`/`anchor_b` objelerini adlarından çözer.)
5–6. **`Tools > VortexArena > Build > Configure All Build Elements` → Hepsini Yapılandır** —
   `MapDefinition` (sceneName + görünen ad + desteklenen modlar) yazılır, ardından `GameCatalog.maps`,
   dolu `ModeDefinition.maps`, **Build Settings** ve `maps.json` klasör ağacına göre eşitlenir;
   sonunda sağlık raporu basar. Arena silindiyse/taşındıysa **Yalnız Senkronize Et** yeter.
7. **Arenanın çatısı/tavanı varsa** (isteğe bağlı, açık tavanlı arenalarda atlanır): çatı
   hiyerarşisinin kökünü seç → `GameObject > VortexArena > Arena Roof`. Bileşen eklenir ve
   altındaki tüm Renderer'lara `ArenaRoof` katmanı damgalanır → admin kuş bakışına geçince çatı
   kalkar, gölgesi kalır. Sonradan mesh eklersen bileşene sağ tık → *Çatı katmanını uygula*.
   **Tam not (davranış, tuzaklar, test, sorun giderme): [`Cati-Gizleme.md`](Cati-Gizleme.md).**

### 6.5 `Tools > VortexArena > Server > Export Server Config` — ne zaman?

**Harita (`MapDefinition`) SO'su ekledin/değiştirdin mi → çalıştır.** Menü, `MapDefinition`
SO'larından `Server/config/maps.json` üretir; çıktı deterministiktir (alfabetik, LF, UTF-8
BOM'suz) → git diff temiz kalır.

Dışa aktarılan tek şey **`sceneName` + `supportedModeIds`** — yani sunucunun `start_match`'te
gerçekten sorduğu iki soru. **Arena ölçüsü gitmez** — ölçü yalnız arenanın **boyut dosyasında**
yaşar (Tuzaklar, "sunucu metre bilmez"). Pratik sonuç: bir
arenanın boyutunu değiştirdiysen ama mod listesine dokunmadıysan **export'a gerek yoktur**
(çıktı zaten aynı).

**Silah için GEREKMEZ:** sunucu silah tablosu tutmaz (§3.6), hasarı istemci bildirir. Yeni silah
eklerken export çalıştırmaya gerek yoktur.

Unutursan ne olur: bilinmeyen `sceneName` → **`start_match` reddedilir** (maç başlamaz), sunucu
konsoluna tek satır sebep yazar.

---

## 7. Tuzaklar (pahalıya öğrenilmiş kurallar)

1. **`maps.json` ELLE DÜZENLENMEZ** — bir sonraki export ezer. Tek doğruluk kaynağı Unity
   SO'larıdır. (`server.json` elle, `devices.json` sunucu üretir; `weapons.json` diye bir dosya yoktur.)
2. **Meta umbrella paketi (`com.meta.xr.sdk.all`) ASLA eklenmez** — Meta Project Setup Tool önerse
   bile. Çektiği `voice` paketi Android namespace çakışmasıyla build'i kırıyor. Bireysel paketler:
   core + interaction + interaction.ovr @203.0.0, audio @85.0.0 (spatializer, pinli).
3. **Rig'i/kamerayı asla taşıma** — free-roam'da oyuncu fiziksel; canlanma durum değişimidir.
   Ayrıca **sahneye Building Blocks rig'i eklenmez, `VA_CameraRig` prefabı kullanılır**: BB kurulum
   yordamı (`CameraRigBBBlockData`) prefabı örnekledikten sonra **otomatik unpack ediyor** — yani
   her arena rig'in birbirinden habersiz donmuş bir kopyasını taşırdı ve tek bir rig düzeltmesi
   (tracking origin, el görselleri, gövde avatarı) arena sayısı kadar elle iş doğururdu. Rig'e bakan
   sahne referansları (`ArenaBoundary.head/fadeRenderer/warningText`, `BaseZone.head`,
   `WeaponReloadGesture.head`, `ArenaCalibrator.rigRoot`) rig değiştirilirken **yeniden bağlanmalı**;
   boş kalırlarsa sahne sessizce çalışmaz hâle gelir.
   **Yapay hareket bu yüzden `VA_CameraRig`'de kapatılmıştır ve açılmaz:** Meta'nın paket prefabı
   `OVRComprehensiveInteractionRig` tam bir locomotion yığınıyla gelir — thumbstick'i okuyan
   Slide/Step/Turn yayıncıları, turner interactor'ları, teleport ve bunları uygulayan
   `FirstPersonLocomotor` + `CharacterController`. Free-roam'da bunların hiçbiri istenmez: sanal
   öteleme/dönme kalibrasyonu bayatlatır (rig fiziksel zemine hizalanmıştır), arenayı fiziksel
   alandan kaydırır ve arena-uzayı poz akışını yalanlar — oyuncu duvarın içinde görünür.
   Prefab bir PAKET içinde olduğu için düzeltilemez; `VA_CameraRig` örneği üzerinde **yedi
   GameObject kapatılarak** susturulur (`Locomotor` + sol/sağ `LocomotionControllerInteractorGroup`,
   `MicroGesturesLocomotionHandInteractorGroup`, `LocomotionOutput`). Bu bir prefab override'ıdır,
   yani **tüm arenalara tek kaynaktan yansır** ve hiçbir sahne onu geri açmaz. Pratik sonuç:
   sahneye elle BB rig'i eklemek yalnız rig'i kopyalamaz, **yapay hareketi de geri getirir**.
4. **Sahne adı = katalog anahtarı.** `load_match` string gönderir; Build Settings'teki adla
   boşluk/typo dahil birebir eşleşmeli.
5. **`_Shared` köküne asmdef'siz gevşek script koyma** (Assembly-CSharp'a düşer, kimse göremez).
6. **Serialize edilen ikincil tipler kendi dosyasında** (`Team.cs` gibi) — gömülü tip
   "referenced script missing" üretebiliyor.
7. **Protokol dosyalarına `UnityEngine` sokma** — sunucu derlemesi kırılır (bilinçli bekçi).
8. **Doğrulamayı batch'le:** derleme/build/play testini işin sonunda tek geçişte yap.
9. **Kök `.gitignore`'a sabitlenmemiş Unity deseni ekleme.** Repo üç proje tipi barındırıyor
   (Unity + `Server/` .NET + `launcher/` .NET WPF) ve her birinin kendi `.gitignore`'u var.
   Windows'ta `core.ignorecase=true` olduğu için `*.app` deseni `Server/VortexArena.Server.App/`
   klasörünü, `*.sln`/`*.csproj` de sunucunun gerçek proje dosyalarını sessizce yutar — bunlar
   `/*.app`, `/*.sln`, `/*.csproj` diye köke sabitlenmiştir. Yeni desen eklendikten sonra
   `git ls-files -c -i --exclude-standard` (izlenen ama artık ignore'lu dosyalar) **boş dönmeli**.
10. **Batch değişkenlerine kısa genel ad verme (`RC`, `CC`, `SRC` …)** — çocuk süreçlere miras
    kalıyor: `set "RC=0"` CMake'in resource compiler değişkeniyle çakışıp bir masaüstü build'ini
    kırdı; MSBuild de ortam değişkenlerini global property olarak okur (Unity → IL2CPP → MSVC
    dahil). `scripts/*.bat` içinde tüm betik-içi değişkenler `VA_` öneklidir.
11. **Öldüreceğin bir süreci `dotnet run` ile başlatma** — `dotnet run` asıl exe'yi ÇOCUK süreç
    olarak doğurur; parent öldürülünce `VortexArena.Server.App.exe` **yetim** kalır, 47821'i
    tutmaya devam eder ve PID takibinde olmadığı için öldürülemez → sonraki sunucu porta bind
    olamaz (yaşandı). Programatik başlatmada **her zaman doğrudan exe** (launcher böyle yapar); bir
    süreci PID'inden geri bulup öldüreceksen kaydı **ad doğrulamalı** tut (PID'ler geri dönüşür).
12. **Okunmayan boru süreci kilitler** — `RedirectStandardOutput/Error = true` yapıp boruyu
    okumazsan çocuk süreç, tampon dolduğunda yazma çağrısında donar (süreç canlı görünür ama
    çalışmaz). Launcher'ın başlattığı sunucu bu yüzden çıktı yönlendirmeden, **kendi konsol
    penceresinde** koşar — boru yok, log canlı okunur.
13. **Muhafazayı susturmak bileşeni KAPATMAKLA yapılmaz — `SetSpectatorMode(true)` ile yapılır.**
    Kapatılan `ArenaBoundary` alan-dışı karartmasını son yazdığı değerde dondurur **ve planı
    çözmeyi bırakır**; susturma kipi ise yalnız uyarıyı keser, bileşeni ayakta tutar. Admin
    gözlemci bu yolu kullanır: başlığı olmadığı için muhafaza mesafesi onda anlamsız veri üretir,
    ama kuş bakışı kadrajı bileşenin `HalfExtents`/`LocalCenter` değerlerini okumaya devam eder.
    (Arena origin'i artık bu bileşende değil `SpawnPoint`'te olduğu için kapatmak koordinatları
    bozmaz.)
14. **Arena sahnelerinde EventSystem YOK** (yalnız Lobby'de bir tane var) — masaüstü admin oraya
    girdiğinde HUD düğmeleri sessizce ölürdü. `UiKit.EnsureEventSystem()` kalıcı bir tane kurar,
    `TakeOverEventSystem()` sahnedekini kapatır: **iki etkin EventSystem** Unity uyarısı basar ve
    girdiyi ikisi arasında böler. Ayrıca proje Input System-only → modül
    `InputSystemUIInputModule` olmalı (`StandaloneInputModule` runtime'da patlar).
15. **`VA_CameraRig`'in ÜÇ kamerası da `MainCamera` etiketli** (Left/Right/CenterEye) → `Camera.main`
    hangisini döndüreceği garanti değil ve `RemoteAvatar` ad etiketleri yanlış kameraya döner.
    Sahnede kendi kamerasını kuran her şey (admin gözlemci) rig kökünü kapatmalı ve
    **kendi `AudioListener`'ını** eklemelidir (rig kapanınca sahnede dinleyici kalmaz).
    Masaüstü XR ayarı: Standalone `Initialize XR on Startup` **AÇIK kalmalı**.
    Sebep: **editör Play modu Android sekmesini değil PC/Standalone ayarını okur** (editörün kendisi
    bir PC uygulamasıdır) → kapalıyken Quest Link ile Play'e basmak gözlüğe hiçbir şey göndermez,
    `XRSettings.enabled` false kalır. Kapatılırsa VR'ı denemenin tek yolu her seferinde APK almaktır.
    Bedeli: Windows admin build'i de açılışta XR başlatmaya çalışır (başlıksız PC'de başlatma
    sessizce düşer); admin gözlemcinin rig kökünü kapatıp kendi `AudioListener`'ını kurması bu
    yüzden şarttır. Admin dağıtımında sorun çıkarsa çözüm ayarı topluca kapatmak değil, XR'ı
    role göre kod tarafında başlatmaktır (player rolünde `InitializeLoader`).
16. **`Shader.Find` build'de null dönebilir** — hiçbir materyalin referanslamadığı shader
    (`Universal Render Pipeline/Unlit` gibi) strip edilir. Runtime'da üretilen görseller bu yüzden
    UI/TMP shader'ları üzerinden çizilir (world-space canvas + sprite), mesh + Unlit materyal ile
    değil. Admin oyuncu halkası bu yüzden `AdminPlayerMarker` prefabında bir UI `Image`'dır;
    görseli artık çalışırken üretilmez, `UI/Sprites/Ring_16.png` asset'inden gelir.
17. **Serialize edilen enum'a yeni değer SONA eklenir.** Unity enum'ları sayısal indeksle saklar:
    `Team`'e başa/ortaya bir değer eklemek sahnelerdeki tüm `BaseZone`/`Weapon`
    takımlarını kaydırır (`Neutral` bu yüzden `= 2`). Aynısı `ModeTeamMode`/`ModeScoreKind`/
    `ModeReviveAnchor`/`ModeWeaponSource` için de geçerli — hepsi `ModeDefinition`'da serialize.
18. **Boş takım takım arkadaşı DEĞİLDİR.** Dost ateşi kontrolünü düz `a.Team == b.Team` yazma:
    takımsız modda herkesin takımı `""` olduğu için `"" == ""` **tüm vuruşları reddeder** ve kimse
    kimseyi vuramaz. Tek kapı `MatchDirector.AreTeammates` (§3.6).
19. **İstemcide `if (modeId == "...")` zinciri yazma.** Modun davranışı telden gelir
    (`ModeRules` → `ModeRuntime`, §3.9). Zincir yazılırsa her yeni mod istemci kodunu değiştirir
    ve dört ayrı yerde ayrı ayrı bayatlar — `ModeRuntime` tam bu yüzden **tek** okuma noktasıdır.
20. **Yeni `IGameMode` kancası varsayılan gövdeyle eklenir** (default interface method) ve
    **tüketicisi yoksa hiç eklenmez.** Ölü kanca, her modun boş uygulamak zorunda kaldığı bir
    vergidir; varsayılan gövde sayesinde sonradan eklemek ücretsizdir.
21. **Quest'te "Soft Particles" YOK** — `Mobile_RPAsset.supportsCameraDepthTexture = false`
    (PC asset'te açık, bu yüzden editörde çalışıp cihazda çalışmaz). Parçacığın geometriyi
    kesmesini yumuşatmak için derinlik dokusu gerekmeyen iki araç kullanılır: materyalde
    **Camera Fading** (`_FADING_ON`, ekran uzayı w'sinden hesaplar) ve Collision modülünde
    **tek düzlem + `lifetimeLoss = 1`** (zemine değince öl). Ayrıca `renderScale = 1.6` +
    `MSAA 4` yüzünden darboğaz parçacık SAYISI değil **saydam overdraw**'dur: büyük yakın
    quad'ları `ParticleSystemRenderer.maxParticleSize` ile kırp.
22. **Ambiyans parçacığını arenadan çok geniş hacme yayma.** IceWorld'ün ilk kar sistemi
    12×12 m arenanın üstünde **50×50 m** kutuya 1500 parçacık saçıyordu → görünür alana
    ~%6'sı düşüyor, kalan bütçe boşa gidiyordu. Emisyon kutusu arena boyutu + ~3 m pay
    olmalı; derinlik hissi kutuyu büyütmekle değil, farklı boyut/hız/yoğunlukta
    **katmanlarla** kurulur.
23. **Beyaz parçacık beyaz sahnede görünmez.** Yumuşak gradyan sprite'lar 6 px'te arka plana
    karışır, büyütülünce renkli bulanıklığa döner. Çözüm: **opak çekirdek + ince smoothstep
    kenar** dokusu ve parçacığı arka plandan PARLAK tutmak (mermer duvar ~0.85 → parçacık
    1.0). Beyaz zeminde okunan tek şey **additive** katmandır (alpha katman kaybolur).
24. **Kar/pus katmanı kalibrasyonu iki farklı arka plana göre yapılır.** Gökyüzü (koyu) ve
    mermer duvar/zemin (parlak) zıt yönde çalışır: gökyüzünde iyi görünen alpha değeri
    duvarda kaybolur, duvarda iyi görünen değer gökyüzünde bulanık perde olur. Her ayardan
    sonra **hem yukarı hem duvara** bakan iki kare al. `Snow_G_Haze` yalnız gökyüzüne karşı
    okunur — bu bilinçli.
25. **`BaseZone`'un GameObject'ini kapatma — bileşenini kapat.** Altına konmuş
    marker'lar (`SpawnPoint`) `OnDisable`'da statik kayıttan düşer. Ama YALNIZ bileşeni kapatmak
    da yarım çözümdür: görsel taban şeridi (Renderer'lı doğrudan çocuk) ekranda kalır.
    Doğrusu ikisi birlikte — `zone.enabled = false` + `SpawnPoint` taşımayan Renderer'lı çocukları
    `SetActive(false)` (`BaseZoneVisibility`). Kontrol `GetComponent` değil
    **`GetComponentInChildren`** olmalı: marker şeridin torunu olabilir.
    İkinci yüzü: **kapalı bir `BaseZone` canlanma için AÇIK SAYILMAZ** — `Update` koşmadığı
    için `IsPlayerInside` donar (`PlayerCombatState.EvaluateZones`).
26. **Verilen silah kavrama sistemine SOKULMAZ ve tetiği ayrı okunur.** `Weapon.IsHeld` yalnız
    `grabbable.SelectingPointsCount`'a bakarsa el anchor'ının altına örneklenen silah **hiç ateş
    edemez** (`GrantedHand` bu yüzden var). İkinci tuzak tetikte: `Player/Attack` tek bir Button
    action'dır ve `<XRController>/{PrimaryAction}` ile İKİ kumandayı da toplar → iki elde iki
    silahla tek tetiğe basmak ikisini birden ateşlerdi. Verilen silah bu yüzden kendi elinin
    tetiğini `OVRInput` ile okur. Bu yol çerçeveden seçilen silah için de aynıdır — o da
    `GrantTo` ile ele verilir (`Persistent`), yalnız reload/rezerv/ön kabza kapıları açıktır.
27. **Free-roam'da tracking origin `Stage`'dir, `FloorLevel` değil.** İkisi de aynı zemin
    seviyesini verir (`TrackingOriginModeFlags.Floor`) ama OpenXR loader'da `FloorLevel`
    **recentering'i zorla açar** (`OVRManager.cs`: `SetAllowRecentering(true)`), `Stage` kapatır.
    OVRManager'daki `AllowRecenter` alanı bunu ezmez — o yalnız OVR'ın kendi manuel recenter
    çağrısını keser. Recenter olursa origin kayar, rig'in hizalama transform'u eski kalır ve
    **arena kayar**; operatöre "sebepsiz bozuldu" gibi görünür. İkinci savunma: `ArenaCalibrator`
    `RecenteredPose` + `TrackingAcquired` olaylarında kayıtlı anchor'dan yeniden hizalar.
28. **Guardian kurulu olmadığı için sistemin zemin seviyesi güvenilmez.** İşletme başlıklarında
    alan kurulumu bilinçli olarak yapılmaz (serbest dolaşım) → "floor level" ölçülmüş değil
    tahmindir: gözlük havadayken açılırsa yanlış başlar. Bu yüzden kalibrasyon zemini de ölçer
    (§3.3) ve yakalanan nokta kumandanın **ucudur**, pivotu değil — pivot gövdenin içindedir,
    fark doğrudan dikey hataya dönüşürdü. **Eğim telafisi yoktur ve eklenmemelidir:** iki nokta
    bir düzlem tanımlamaz (roll bilinemez) ve sanal dünyayı eğmek görsel "yukarı" ile
    yerçekimini ayrıştırıp VR'da mide bulantısı yapar. Ayrıca ölçülebilecek eğim (~5–10 mm),
    operatörün kumanda tutuş farkının (±10–20 mm) altında kalır.
29. **Sunucu metre bilmez — `maps.json`'a arena ölçüsü koyulmaz.** Sebep ölçünün kullanılmaması
    değil, **ölçülemez** olması: her işletmenin alanı farklı ve çoğu kare/dikdörtgen bile değil,
    yani tek bir ölçü çifti o arenayı tarif etmez — sunucuya gönderilen sayı, doğru sandığın
    yanlış bir sayı olurdu. Arenanın gerçek sınırı zaten arenanın **boyut dosyasındaki**
    çokgendir ve yalnız istemciyi ilgilendirir (alan-dışı uyarısı,
    gözlemci kamerasının kuş bakışı kadrajı). Genel kural: **sunucuya yalnız sunucunun
    karar vermek için okuduğu alan gider** — "ileride lazım olur" diye alan taşımak, iki uçta
    sessizce sapan ikinci bir doğruluk kaynağı üretir.
30. **Kalibrasyon kapısı `REVIVE_GRACE` zorla canlandırmasını da kapsamalı.** "Kalibresiz oyuncu
    canlanamaz" kuralını yalnız `HandleReviveRequestAsync`'e koymak işe yaramaz:
    `MatchDirector.TickLiveLocked` talep gelmese de grace süresi dolunca herkesi canlandırıyor,
    yani oyuncu birkaç saniye sonra kendiliğinden geri gelirdi. İki yer de kapatılmalı. Genel
    kural: **bir oyuncu durumuna kapı koyarken o durumu değiştiren TÜM yolları ara** — talep
    tabanlı olan ile zamanlayıcı tabanlı olan ayrı kod yollarıdır.
31. **Arayüzde "kaç px sığar" hesabı elle YAPILMAZ.** Yerleşim Layout Group değil **sabit anchor**
    ile kurulu (öngörülebilir yerleşim) — bedeli: bir satıra düğme eklemek kalanların genişliğini
    sessizce daraltır. Oyuncu satırına `KAL` eklenince düğme başına 94 px'ten ~70 px'e düşüldü ve
    `KIRMIZIYA` etiketi `KIRMIZ…` diye kırpılır oldu. Çözüm iki katmanlı: (a) düğme etiketleri
    **aşağı yönlü autosize** yapar (tavan = istenen punto, taban %70) → sığmayan etiket
    kırpılmadan önce küçülür; (b) etiketler kısaltıldı (`MAVİ`/`KIRMIZI`).
    ⚠️ Arayüz prefaba taşındıktan sonra bu ayarlar **prefabtaki TMP bileşenlerinde** durur
    (Auto Size + Min/Max) — bir satıra öge eklerken hâlâ geçerlidir, ama artık koddan değil
    inspector'dan yönetilir. → `Docs/Gelistirici/Arayuz-Tasarimi.md`
    Aynı sebeple **panel yüksekliği de elle yığılan `y`'ye bağlıdır** —
    `AdminPreferencesPanel`'e bölüm eklerken `PanelHeight` de büyütülür (taşma hata vermez,
    alt kısmı ekran dışına atar).
32. **Arayüz metninde ✓ ✗ gibi sembol kullanma** (`UiKit` sınıf dokümanı zaten söylüyor): TMP
    varsayılan fontunda glif garantisi yok, eksik glif **□** çizilir — çalışmayan ama hata da
    vermeyen bir görsel. Kalibrasyon düğmesi bu yüzden `KAL` / `KAL !` + renk kullanıyor.
    Türkçe harfler ve `·` / `—` güvenlidir (mevcut kodda kullanılıyor).
33. **`MaterialPropertyBlock` shader keyword'ü AÇAMAZ.** `RemoteAvatar`'daki kalibresiz parlaması
    `_EmissionColor` ile yazılsaydı, paylaşılan materyalde `_EMISSION` önceden açık olmadığı için
    **sessizce hiçbir şey yapmazdı** — çalışmayan ama hata da vermeyen bir görsel. Bu yüzden
    parlama `_BaseColor` nabzıyla yapılıyor (mevcut takım rengi yolunun aynısı). Emission gerekirse
    materyalde keyword'ü açmak ayrı bir adımdır; ikinci bir materyal örneği yaratmak da Quest'te
    SRP batch'ini bozar.
34. **İstemcide can havuzu tutulmaz.** "Kırılabilir obje ağa girmiyor, canını yerelde
    tutalım" yolu iki istemcide sapma üretir (herkes kendi hasarını görür) ve ikinci bir doğruluk
    kaynağı doğurur. Bu yüzden yerel `Health` bileşeni tamamen kaldırıldı: `ReportRaycastHit`
    `false` dönünce hiçbir şey olmaz (dönüş değeri yalnız sunum kararıdır), ağa bağlı olmayan
    geometri hasar almayan **dekor**dur. Hasar alması gereken her şey ağsal olur
    (`NetIdentity` → `plan/agsal-kirilabilir-objeler.md`).

35. **Ateşle-unut yayın SIRA garantisi vermez — durum yayınları tek yayıncıdan gider.**
    `lobby_state` eskiden her registry değişiminde `_ = BroadcastLobbyStateAsync()` ile
    yayınlanıyordu. Arka arkaya iki değişiklik iki eşzamanlı task açıyor, her biri kendi
    `Snapshot()`'ını farklı anda alıp `ClientConnection`'ın gönderim semaforu için yarışıyordu.
    Semafor **çerçevelerin iç içe geçmemesini** garanti eder, **yeni olanın kazanmasını etmez** →
    eski roster sonra yazılabilir ve istemcide bir sonraki değişikliğe kadar bayat kalır.
    Belirtisi çok yanıltıcıdır: *"atılan oyuncu hâlâ listede online görünüyor"* — insanın aklına
    ilk olarak paket kaybı gelir, oysa kanal TCP'dir ve hiçbir şey kaybolmamıştır. Kural: durum
    yayınları **kirli bayrak + tek yayıncı döngü** ile gider (`LobbyService.MarkRosterDirty`) ve
    mesaj **monoton bir sürüm** taşır (`lobby_state.version`); istemci eski sürümü atar.
    Yan kazanç: birleştirme — 16 oyuncu aynı anda bağlanınca 16 değil 2 yayın olur.

36. **Kalp atışı mesajı koşulsuz yayın tetiklemez.** `status` her 5 sn'de bir gelir ve eskiden
    koşulsuz `Changed(Updated)` raise ediyordu → 18 istemcide **saniyede ~65 tam roster JSON'u**,
    hiçbir şey değişmese bile. Kural: yayın yalnız roster'da **görünen** bir alan gerçekten
    değiştiğinde tetiklenir (`Fps` PlayerInfo'da taşınmadığı için tetiklemez). Kaçırılmış bir
    yayın varsa onu `status.rosterVersion` uzlaştırması kapatır — periyodik körlemesine yayın
    değil, geride kalana hedefli tek mesaj.

37. **`SpawnPoint` arena uzayının SIFIRIDIR — yerleştirdikten sonra taşınmaz.** Marker göze
    zararsız bir gösterge gibi görünür ("maçtan önce şurada toplanın"), oysa `ArenaSpace` origin'i
    odur: birkaç metre kaydırmak arenadaki **tüm** oyuncuların ağ koordinatını aynı miktarda
    kaydırır ve hata yalnız birden çok başlık aynı sahnede buluşunca görünür. İkinci yüzü dikeydir:
    uzak avatarların kökü `ArenaSpace.ArenaToWorld` ile yerleştirilir → marker havada bırakılırsa
    herkes o yükseklik kadar havada durur. Sahnede hiç
    marker yoksa dönüşüm kimliğe düşer; bunun tek işareti `ArenaSpace`'in sahne başına bir kez
    bastığı uyarıdır (lobide o uyarı normaldir).

38. **Arena ölçüsünün TEK temsili boyut dosyasıdır — dikdörtgen alan için kısa yol açılmaz.**
    Alan tam kare bile olsa dört köşeli bir `plane` halkası olarak yazılır. Bileşende ölçü tutan alanlar
    (yarım genişlik/derinlik + merkez) bilinçli olarak kaldırıldı ve geri eklenmez: aynı ölçünün
    iki ayrı ifadesi kaçınılmaz olarak birbirinden sapar — biri düzeltilir, öteki eski değeriyle
    kalır ve hangisinin okunduğu koda gömülü bir öncelik sırasına bağlı olurdu. Kolayına kaçıp
    "burası zaten dikdörtgen" demek de aldatıcıdır: gerçek işletme alanlarının çoğu kare değil,
    duvarları eksenlere paralel değil. Sahneye elle konan kolon/kasa ayrıca `ArenaObstacle` ile
    işaretlenir; o bileşen fizik yapmaz, yalnız uyarıyı erken tetikler.

39. **Referanslanmayan boyut dosyası build'e GİRMEZ.** `ArenaDimensions` çalışma anında okunur;
    Unity bir `TextAsset`'i yalnız bir sahne/prefab onu referansladığı için paketler. Dosyayı
    `Assets/` altına yazıp `ArenaBoundary.dimensionsJson` alanına **bağlamamak** dosyayı build'in
    dışında bırakır. Kural: JSON'u yazdıktan sonra alana bağla; bağlamadıysan o plan yok sayılmalıdır.

40. **Boyut dosyası bağlanmazsa muhafaza tümden devre dışı kalır — sessizce değil, yüksek sesle.**
    `ArenaBoundary` dosyayı çözemezse bir kez `Debug.LogError` basar ve kapanır: yaklaşma rampası,
    alan-dışı karartması ve uyarı çalışmaz. Bu bilinçli bir seçimdir — ölçüsü bilinmeyen bir arenada
    doğru bir muhafaza zaten üretilemez, kapalı başarısızlık (ör. her karede ekranı karartmak)
    işletmede oyunu tümden oynanamaz kılardı. Yani bu bir KURULUM hatasıdır ve editörde/QA'da
    yakalanmalıdır: yeni bir arena sahnesini ilk açtığında konsolu oku, guardian kapalı olduğu için
    sahada başka fren yoktur.

41. **Meta'nın CCD çözücüsü kemik dizisini UÇ→KÖK bekler, toleransı da KARE alır.**
    `IKUtilities.SolveCCDIK` effector olarak dizinin **0. elemanını** kullanır ve zincirin kökünü
    son elemanın *ebeveyninden* bulur; kök→uç verilirse çözücü sessizce ters çalışır — eli hedefe
    götürmek yerine omzu/kalçayı hedefe sürükler (diz kalçanın üstüne çıkar, ayak havada kalır).
    İkinci yüzü: döngü koşulu `sqrMagnitude > tolerance`, yani parametre **metre değil metrekare**;
    `0.01` yazmak 1 cm değil **10 cm** slop demektir. Kural: zinciri `{ uç, orta, kök }` sırala,
    toleransı karesiyle geç.

42. **Kemiklerini kodla süren avatar her kare bind pozuna dönmelidir.** Sahnedeki karakterlerde
    AnimatorController yok (animasyon klibi kullanılmıyor), yani pozu sıfırlayan hiçbir şey yok:
    `rotation = delta * rotation` gibi **birikimli** bir yazım (omurga eğimi) her karede biraz daha
    büker, avatar dakikalar içinde katlanır. İkinci kural aynı aileden: el/kafa kemiğine **konum
    yazılmaz, yalnız rotasyon** — konum yazmak kemiği ebeveyninden koparır ve aradaki mesh gerilir;
    hedef erişilemez olduğunda (kumanda pozu bileğin değil avucun ötesindedir) bu her kare olur.
    Üçüncüsü: model **sabit boyda**, oyuncu değil — avatar oyuncunun kafa yüksekliğine
    ölçeklenmezse kollar ele yetişmez, ayaklar zemine değmez. Belirti üçünde de aynı görünür:
    "uzamış/kopmuş uzuvlar".

43. **Editör AÇIKKEN `ProjectSettings/*.asset` dosyasını elle düzenleme — Unity onu okumaz, üstüne
    yazar.** Editör bu dosyaları açılışta belleğe alır; diskteki değişikliği görmez ve bir sonraki
    kaydında kendi hâlini geri yazar. Sinsi tarafı sessiz olmasıdır: dosyada `ArenaRoof` katmanı
    **yazılı dururken** editörde (ve ondan üretilen build'de) o katman yoktur ve bağlı özellik
    (çatı gizleme) kurulumunu atlar. Katman/tag/kalite ayarı
    editörden ya da `SerializedObject` ile yapılır; git'e giden dosya o yolun çıktısıdır.

44. **Poz kanalının "eski `seq`'i at" kuralı OLAY kanalına kopyalanmaz.** İkisi aynı alan adını
    taşır, kuralları taban tabana zıttır: poz bir **durum**dur (son gelen kazanır, geç kalan eski poz
    oyuncuyu geri zıplatır → atılmalı), atış/atma bir **olgu**dur (sırası bozuk gelen atış gerçekten
    olmuş bir atıştır → atmak sessizce bir tracer ve bir ses siler). Olay kanalında `seq` yalnız
    **birebir kopyayı** bastırır (UDP paket çoğaltabilir → çift ses/çift tracer) ve kayıp ölçer.
    Aşağı yönde karşılığı `serverTick` halkasıdır, aynı kuralla: eski tik'li ama görülmemiş batch
    OYNATILIR. → `Docs/ArenaNet-Protokol.md` §6.4/§6.5

45. **Serialize edilen `int` alana `[Range(min,…)]` koymak varsayılan `0`'ı SESSİZCE min'e çeker.**
    Unity'nin Range drawer'ı `IntSlider` ile çizerken değeri clamp'ler **ve asset'i dirty yapar** —
    yani Inspector'da açılan her asset kendiliğinden değişir. `netItemId` (varsayılan `0` =
    "atanmamış", §6.6) için bu, Inspector'ı açılan altı silahın hepsinin `netItemId=1` olması, yani
    altı kimliğin birbiriyle çakışması demekti. Aralık denetimi koda (`HasNetItemId`) ve editör
    bekçisine (`Tools > VortexArena > Weapons > Rebuild Net Item Catalog`) ait; `[Range]` "0 = atanmamış"
    semantiği taşıyan alanlarda kullanılmaz.

46. **Alanı yeniden adlandırırken `[FormerlySerializedAs]` yoksa değer sessizce sıfırlanır.** Unity
    alanı **isimle** saklar: `grantedHoldPosition` → `primaryGripPosition` geçişi attribute olmadan
    yapılsaydı altı `WD_*.asset`'in kavrama pozu boşalırdı ve bunu ancak VR'da "silah elde ters
    duruyor" olarak görürdük. (Alanı türetilmiş sınıftan **tabana taşımak** isim aynı kaldığı sürece
    güvenlidir — kaybettiren şey taşıma değil **yeniden adlandırma**dır.)

47. **Uzak oyuncuda çizilen eşya bir GÖRSELDİR; prefabın bileşenleri sterilize edilmeli — ve
    `enabled=false` YETMEZ.** `Awake` devre dışı bırakılmış bileşende de koşar, `AudioSource`
    `playOnAwake` ile ses çalar. Sterilize edilmemiş bir uzak silah kendi sesini çalar, fizik yapar,
    hatta raycast atar; teşhisi en zor hata sınıfıdır çünkü **yalnız başkalarının ekranında** olur —
    atıcının kendi görüntüsü kusursuzdur. Çözüm: örneği **pasif** bir kökün altında `Instantiate`
    edip (hiç `Awake` koşmaz) sonra bileşenleri toptan kaldırmak. Tipe göre liste tutmak da tuzak:
    prefaba yeni bileşen eklenince liste güncellenmeyi unutur.

48. **Uzak bir olayı GELDİĞİ ANDA oynatmak, pozu bilerek geciktirmiş olmakla çelişir.** Uzak
    avatar `INTERP_DELAY_MS` geriden çizilir; sunucu snapshot'ı ile olay batch'ini aynı tik'te
    yayınladığı için "hemen oynat" demek efekti elin **100 ms öncesindeki** yerine koymak demektir
    (kol 2 m/s ise ~20 cm). Kaymanın sinsiliği şu: görüntü kendi içinde tutarlı görünür, çünkü
    tracer o karede çizilmiş namludan çıkar — yani yanlış olan efektin YERİ değil ZAMANI, ve gözle
    "biraz geriden çıkıyor" diye okunur, hata gibi değil. Kural: interpolasyon tamponu olan her
    sistemde olayın da **kendi tik'inde** oynaması gerekir; tamponun büyüklüğü kadar bekle.
    ⚠️ Bunun tersi de tuzak: bekletmek için "geçmiş pozu örnekleyen" bir API yazmak. Gerekmiyor —
    doğru anda oynayınca zaten çizili poz doğru pozdur (§3.5b).

49. **Bir affordance'ı yalnız SEÇİM tarafından kesmek onu ekrandan kaldırmaz.** ISDK'nın
    `_interactorFilters` kapısı `CanBeSelectedBy`'ı süzer, **hover'ı süzmez**: soket kapısı bağlı
    bir `DistanceGrabInteractable` bırakılsaydı oyuncu silahı odanın öbür ucundan hâlâ vurgulu
    görür, nişan alır, grip'e basar ve **hiçbir şey olmazdı**. Yalan söyleyen bir affordance,
    hiç olmayandan kötüdür. Bu yüzden bileşen filtrelenmedi, **kaldırıldı** (`WeaponKitBuilder`) —
    tek referansçısı olduğu `MoveTowardsTargetProvider` ile birlikte: yetim bileşen davranışsızdır
    ama sonraki okuyucuya "burada mesafe kavraması var" diye yalan söyler.

50. **Kablosuzda maliyet BAYT değil ÇERÇEVE'dir — "bant genişliği bol" ölçeklenebilirlik demek
    değildir.** Bu ürün 1 Gbps'lik bir AP'nin binde ikisini kullanıyor ama tek radyonun
    airtime'ının ~%15–20'sini tutuyor (§3.12): trafiği "az bayt, çok çerçeve" desenindedir ve
    802.11'de her minik unicast çerçeve preamble + SIFS + ACK + backoff olarak sabit bir hava
    bedeli öder. Pratik sonuçları:
    - **Bant üzerinden yapılan kapasite planı yanlış sonuç verir.** "%0,2 kullanıyoruz, 50 kat
      büyüyebiliriz" cümlesi bant için doğru, airtime için yanlıştır (paket tarafında pay ~2,5 kat).
    - ⚠️ **Donanım datasheet'i bu profili hiç ölçmez.** "1000 Mbps / AX3000" rakamı **büyük TCP
      paketleriyle** üretilir; bu yükte tıkanan şey radyo değil AP'nin **küçük paket iletim hızı
      (pps) / CPU'su**dur ve o sayı yazılmaz. Yani AP seçimi bir hız kıyaslaması değil bir
      **yönetilebilirlik** kıyaslamasıdır (OFDMA DL+UL, sabit kanal, DTIM, elle WMM/QoS
      ayarlanabiliyor mu) → seçim ölçütleri `Docs/Isletme-Kurulum.md` "Ön koşullar / Donanım".
    - ⚠️ **Sıkıştırma bu darboğaza DOKUNMAZ.** Quaternion sıkıştırma / delta snapshot bandı ~2,5×
      düşürür, **paket sayısını hiç düşürmez** — yani bugün hiçbir şey çözmez. Paketi kesen tek
      şey **kanal birleştirmek**tir (durumu zaten giden bir pakete bindirmek), baytı kısmak değil.
    - **Fan-out'u N² olan her kanal önce paket olarak patlar.** `health_update` bant olarak
      0,3 Mbps'lik hiçbir şeydir ama çatışmada sistemin en çok paket üreten kanalıdır — üstelik
      TCP olduğu için retransmit/ACK ile katlanır. Yeni bir "olay başına herkese haber ver"
      kanalı eklerken sorulacak soru "kaç bayt" değil **"tik başına kaç datagram"**dır.

51. **Pozunu KENDİN sürdüğün bir gövdede fizik motorunun "hız" tahmini anlamsızdır.** ISDK'nın
    `Grabbable._throwWhenUnselected` bayrağı (varsayılan AÇIK) bırakış anında gövdeye elin izlenen
    hızını uygular — makul bir varsayıma dayanır: eşyayı tutuş boyunca ISDK taşımıştır. Silahta bu
    varsayım YANLIŞ: kökü `Weapon.ApplyCanonicalGrip` her kare kanonik kavramadan **ışınlıyor**
    (§6.6). Işınlanan gövdeden türetilen hız fiziksel bir büyüklük değil kare farkının artığıdır ve
    bırakınca silah elden fırlıyordu. `WeaponKitBuilder` bayrağı kapatıyor; `_kinematicWhileSelected`
    ise AÇIK bırakılıyor (tutuşta yerçekimi hız biriktirmesin) → "bırakınca yere düşer, fırlamaz".
    Genel kural: **bir üçüncü parti bileşenin varsayılanı, o bileşenin senaryosunda geçerli olan bir
    varsayıma dayanıyor olabilir — pozu/durumu devraldığın her yerde o varsayımı yeniden sına.**
    ⚠️ Fırlatma gereken eşya (bomba) bu kapıdan geçmez: atılışı telde bildirilen kendi balistiğidir
    (`ArenaCombat.ReportThrow`), ISDK'nın fizik impulsu değil.

52. **Bir gövdeyi üç noktadan TÜRETMEK bir çözücü sorunu değil, bir VERİ sorunudur.** Kafa + iki
    elden gövde uyduran her çözücü dirsek, omuz ve bacak yönünü tahmin eder; tahmin ne kadar
    incelirse incelsin gerçek olmaz ve ayar alanları (bilek roll'ü, kalça ofseti, adım eşiği) er geç
    "hangi sayı hangi bozukluğu düzeltiyor" sorusuna dönüşür. Meta'nın body tracking'i aynı üç
    noktadan çalışır ama arkasında öğrenilmiş bir model + IOBT kameraları vardır — yani fark
    algoritmada değil **girdide**dir ve o girdiye uygulama erişemez (servis dışarıdan poz kabul
    etmez). Genel kural: **tahmin üreten bir bileşeni iyileştirmeden önce, tahmini gereksiz kılan
    bir veri kaynağı olup olmadığına bak.** Burada cevabı vardı: gövdeyi sahibinin cihazında çöz,
    sonucu akıt (§6.9). ⚠️ Bunun bedeli tel formatıdır ve **bu bir kısıt değildir** — protokol
    gerektiğinde değişir.

53. **Kendini düzeltmeyen bir tahmin, tek bozuk kareyi KALICI hataya çevirir.** Sabitlenen (mandallı)
    her değer için kural: **tetikleyicisi ve geri dönüş yolu ADI KONMUŞ olmalı.** Bugünkü örneği
    gövde oranıdır — `CharacterRetargeter.Calibrate()` onu o andaki poza sabitler ve bir daha
    kendiliğinden değişmez; tetikleyicisi `ArenaCalibrator.CalibrationGeneration`, geri dönüş yolu da
    aynı sayaçtır. ⚠️ Bu yüzden çağrı **gecikmelidir**: oyuncu arena kalibrasyonunu zemine
    EĞİLEREK yapıyor, o andaki poza sabitlenen oran maçın kalanı boyunca yanlış boy demektir.
    Adı konmamış bir mandal ile "yalnız büyüyen" bir değişken aynı kapıya çıkar: er geç bir gürültü
    örneğine kilitlenir ve geri dönmez.

54. **Sıfır quaternion telde meşru görünür, Unity'de geçersizdir.** Dört sıfır bayt geçerli bir
    poz gibi okunur; bir Transform'a yazılınca "Quaternion To Matrix conversion failed" basar ve o
    kemik o kareden sonra bozuk kalır. Poz zincirinde (`RemoteAvatar` → eşya/etiket sunumu) gelen
    rotasyon normalize edilemiyorsa kimliğe düşürülür, NaN/∞ taşıyan poz ise hiç uygulanmaz.
    ⚠️ Aynı denetim **iskelet kökü için de gereklidir** (`0x07`'nin `root` alanı): o da doğrudan bir
    Transform'a yazılıyor. ⚠️ Sunucu poz İÇERİĞİNİ doğrulamaz ve doğrulamayacak (istemci-otoriter,
    §3.2) — denetim çizen tarafın işidir.

55. **Rig kökü arena zemininde durmalıdır — tracking origin `Stage` onu FİZİKSEL ZEMİN sayar.**
    VortexAntep sahnelerinde `VA_CameraRig` Y=7.40, arena zemini Y=7.05 idi: kalibrasyon yapılmadan
    oynayan her oyuncu 35 cm havada duruyor, bu da uzak avatarını 1.32× dev gösteriyordu (yukarıdaki
    boy tahmini üzerinden). Arena dünya orijininden yüksekte kurulduğunda (burada ~7 m) bu tür her
    dikey hata o kadar büyür — "uzayda duran oyuncu" görüntüsünün ölçeği oradan gelir. Yeni arena
    kurarken rig kökünün Y'si `SpawnPoint`'in Y'si ile aynı olmalıdır.

56. **Movement SDK retarget avatarı hareket eden bir kökün altına KONMAZ — çıktı dünya
    uzayındadır.** Sezgi "avatar rig'in çocuğu olsun, rig'le gelsin" der; SDK'nın sözleşmesi
    bunun tam tersidir. `SkeletonUtilities.GetPosesFromTheTracker` verilen ofseti
    `OVRCameraRig.trackingSpace.localToWorldMatrix` ile çarpıp **her kemiğe** baskılar, ardından
    `ConvertWorldToLocalPoseJob` **kök eklemi** ebeveynine göre yerelleştirmeden bırakır ve
    `ApplyPoseJob` onu `SetLocalPositionAndRotation` ile yazar. Bu projede kök eklem avatarın
    KENDİ transformudur → avatar rig'in altındayken rig transformu **iki kez** uygulanıyordu.
    Belirtisi gecikmelidir ve bu yüzden pahalıdır: rig birimken (arena origin'inde, kalibrasyon
    alınmamışken) hiçbir şey görünmez; kalibrasyon rig'e bir dönüşüm yazar yazmaz avatar
    oyuncudan tam **bir kalibrasyon ofseti** kadar uzağa oturur — arena etrafında dönmüş, zemin
    düzeltmesi kadar yükselmiş, oyuncunun hareketlerini birebir yapan "ikinci bir gövde" gibi; ve
    oyuncu kendi kollarını göremez, çünkü kollar da o kopyadadır.

    **Kural bugün de bağlayıcıdır ve tasarımın kendisi oldu:** karakter — yerel de uzak da — sahne
    kökünde durur, hiçbir şeyin altına parent'lanmaz. Uzak tarafta kökü `ArenaNetCharacterBehaviour`
    yazar (`ArenaSpace.ArenaToWorld`, `LateUpdate`), yerelde ise kök zaten izleme uzayından gelir,
    yani kalibrasyon rig'i kaydırınca gövde kendiliğinden onunla gelir. ⚠️ Uzak tarafta blob'un
    KENDİ kök eklemi **kullanılmaz** (gönderenin dünya uzayındadır ve blob opaktır) — kök arena
    uzayında ayrı bir alanda taşınır, §6.9. Bu iki maddeyi birleştiren tek cümle: **retarget çıktısı
    dünya uzayındadır; onu bir uzaydan diğerine taşımanın yolu parenting değil, kökü açıkça
    yazmaktır.**

57. **"Mesajı yolla, sonra soketi kopar" mesajı YOLLAMAMAKLA aynı şey olabilir.** Atma yolu
    `kicked` JSON'unu yollayıp hemen `Abort()` ediyordu; abortif kapanış (RST) istemcinin henüz
    okumadığı çerçeveyi tamponundan silebilir. `SendAsync`'in dönmesi *işletim sistemine teslim
    edildi* demektir, *karşı taraf okudu* demek değildir. Sonuç sinsidir: atılan başlık kopuşu
    sıradan bir kesinti sanıp yeniden bağlanma backoff'unu çalıştırır — operatör panelde "atıldı"
    yazısını görür ama oyuncu sahada oynamaya devam eder. Kural: **son mesajdan sonra kapanış el
    sıkışması** (`CloseOutputAsync`, cevapsız istemci için üst sınırlı) ve niyet **kapanış
    sebebine de** yazılır (§5.4), yani JSON kaybolsa bile karşı taraf ne olduğunu bilir. İkinci
    yüzü: aynı kapanış sebebi sıradan kopmalarda kullanılmamalıdır (bayat soket değişimi,
    `OFFLINE_TIMEOUT` temizliği) — yoksa istemci kendini atılmış sanıp uygulamayı kapatır.

58. **İzleme/ağ uzayından gelen bir rotasyon humanoid kemiğe DOĞRUDAN yazılmaz — iki iskeletin
    bind ekseni farklıdır.** Telde giden el rotasyonu izleme uzayındadır
    (`OVRCameraRig.left/rightHandAnchor`, yani kumandanın pozu); karakterin el kemiği ise modelin
    kendi bind eksenindedir. `hand.rotation = handPose.rotation` bu iki sözleşmeyi eşit sayar ve
    bilekler ters çizilir. Ölçüldü: Ch15'in el kemiğinde parmaklar kemik-yerel `+Y`, avuç normali
    `+Z`; Meta'nın el iskeletinde parmaklar `∓X`, avuç normali `±Y/±Z` — aradaki fark sol elde
    ~115°, sağ elde ~128°, yani **iki el aynı ofsetle bile düzelmez** (Mixamo'nun sol/sağ el
    kemikleri parmak ekseni etrafında 180° farkla duruyor; "bilek hep ters" belirtisi buradan
    gelir). Köprü `HandGripConvention`'dır: kemik anatomisi modelden çalışma anında ölçülür, anchor
    tarafındaki anatomi tek ayar noktası olarak sabittir, düzeltme
    `anchorRotation * anchorBasis * Inverse(boneBasis)`. ⚠️ **Kafanın doğru görünmesi bir doğrulama
    değil TESADÜFTÜR:** Ch15'te Hips/Neck/Head bind ekseni kimliktir (0° sapma), yani aynı hatalı
    kalıp orada belirti vermez — ayak kemikleri ~177° sapıyor ama onlara rotasyon yazılmıyor.
    **Karakter değişirse kafa da aynı sebeple kırılır.** Meta'nın kendi body tracking'inde sorun
    çıkmamasının sebebi de aynı köprünün orada VAR olmasıdır: `CharacterRetargeter` per-joint T-poz
    ofsetlerini retarget config JSON'undan (`ThirdPartyPackages/MixamoCharacters/Ch15_nonPBR.json`)
    okur; elle yazılan bir IK'nın karşılığını kendisi kurması gerekir. ⚠️ Düzeltme yalnız **çizim**
    tarafındadır, protokol değişmez: telde ham anchor pozu kalır (§6.2 — fiziksel gerçek odur ve
    `ItemDefinition` kavramaları da anchor'a göre tanımlıdır). Yan kanıt: uzak **silah** zaten doğru
    duruyordu, çünkü `RemoteAvatar.ApplyGrip` anchor pozunu `PrimaryGripRotation` ile çarpıyor — o
    yolda ofset vardı, bilek yolunda yoktu.

59. **Uzak sunum davranışı eşya prefabına BİLEŞEN eklenerek yazılmaz — `SterilizeVisual` tüm
    MonoBehaviour'ları toptan söker** (bilinçli: unutulan tek bileşen = sahada kendi sesini çalan,
    hatta `hit_report` üreten bir uzak silah). Uzak silahın geri tepmesi bu yüzden `RemoteAvatar`'ın
    kendisinde yaşıyor. Genel kural: **çizilen kopya salt görseldir; onu SÜREN kod avatarın
    tarafında durur.** ⚠️ İkinci yarısı da aynı derste: "yerelde görünen ama uzakta görünmeyen"
    her animasyon önce *telde eksik bir alan* gibi görünür — oysa girdisi zaten geliyorsa
    (olay + `itemId`) doğru cevap **alıcıda türetmektir**, protokole alan eklemek değil (§6.4).
    Silahın geri tepmesi tam olarak buydu: yön (spray + kick dahil) ilk günden telde gidiyordu,
    eksik olan tek şey onu çizilen silaha bağlayan koddu.

60. **Silah çerçevesinin görseli prefabda PASİF durur; onu AÇAN tek yer `WeaponFrame.Awake`'tir.**
    `RemoteAvatar.SterilizeVisual` kopyadaki tüm MonoBehaviour'ları siler ama **GameObject'leri
    kapatmaz** — görsel aktif başlasaydı `WeaponFrame` bileşeni silinmiş olmasına rağmen çerçeve
    uzak oyuncuların elindeki silahta ve oyuncunun kendi eline gelen klonda çizilmeye devam ederdi
    (bir sahne kaynağının süsü, tutulan silahın üstünde). Kural: "yalnız çalışan bileşenle anlamı
    olan" bir görsel prefabda kapalı doğar, onu bileşenin kendisi açar — sterilizasyon bileşeni
    aldığında süs de kendiliğinden gitmiş olur.

61. **ISDK interactor filtresi SEÇİMİ keser, HOVER'ı kesmez — mesafe testi İKİ yerde yapılır.**
    Çerçevenin 2 m'lik kavrama menzili yalnız `IGameObjectFilter`'a yazılsaydı oyuncu 5 m'den de
    mavi ışını ve vurguyu görür, nişan alır, grip'e basar ve hiçbir şey olmazdı; bu yüzden mesafe
    testi hem filtrede hem **ışın çiziminde** koşar. Yukarıdaki "yalan söyleyen affordance"
    tuzağının aynısıdır — orada çözüm bileşeni kaldırmaktı çünkü `WPN_*` **kökünde** mesafeden
    kavrama soket tasarımının zıddıdır; o yasak hâlâ geçerli ama **yalnız kök için**: çerçeve ayrı
    bir objedir ve silah zaten oradan, uzaktan alınır.

62. **Transformer'sız bir `Grabbable` hareketsiz DEĞİLDİR — en serbest hâlidir.** `Grabbable.Start`
    tek ve çift el transformer alanlarının İKİSİ de boşsa kendisi bir `GrabFreeTransformer` üretip
    bağlar ("create missing defaults"), yani çerçevedeki silah seçilir seçilmez oyuncunun eline
    doğru kaymaya başlar. Çözüm no-op bir `ITransformer` (`FrozenGrabTransformer`) yazıp
    `_oneGrabTransformer`/`_twoGrabTransformer` alanlarına **açıkça** bağlamaktır. "Her karede pozu
    geri yaz" alternatifi ISDK'nın transformer'ıyla kare kare yarışırdı — kimin sonra yazdığı
    Unity'nin çağrı sırasına kalır ve silah titrer; kıpırdamamanın tek kesin yolu kıpırdatacak
    kodu devreden çıkarmaktır.

61. **"Elle atanmışsa ezme" koruması, tabloyu SESSİZCE hiç uygulanmamış bir niyet hâline
    getirebilir.** `WeaponKitBuilder` ses kliplerini yalnız alan boşsa yazıyor
    (`SetClipArrayIfEmpty` / `SetObjectRefIfEmpty`) — gerekçesi doğru: Inspector'dan sürüklenen
    klip bir sonraki koşuda silinmesin. Ama silahlar ilk üretildiğinde alanlar iki ESKİ ortak
    klip setiyle dolmuştu, dolayısıyla tabloya sonradan yazılan silaha özgü setler
    (`SFX_<Ad>_Shot_*`) **hiçbir koşuda inmedi**: dosyalar diskte duruyor, tablo onları
    gösteriyor, asset'ler başka bir şey çalıyordu. Araç uyarı basmaz çünkü "dolu alanı atlamak"
    onun için normal davranıştır. Ders: koşullu yazan bir alan, tabloyu tek doğruluk kaynağı
    saymanı engeller — o alanları değiştirdiğinde **önce asset'te boşalt**, sonra aracı koş; ve
    aracın çıktısına değil ASSET'in kendisine bak.

62. **`Rebuild Net Item Catalog` modal dialog açar — CLI/MCP'den çalıştırılınca komut timeout
    verir.** `NetItemIdGuard` sonda `EditorUtility.DisplayDialog` gösteriyor
    (`ServerConfigExporter` ile aynı tuzak). İş **yapılır ve loglanır**, sonra ana thread dialog
    kapanana kadar kilitlenir; ardından her MCP çağrısı (konsol okuma dahil) timeout verir.
    Komutu TEKRAR DENEME — yeni bir dialog kuyruğa girer. Editörde pencereyi kapat, sonra devam
    et. Kalıcı çözümü diğer araçlardaki gibi dialog'u kaldırmaktır (`Debug.Log` yeter).

63. **Bir asset'in GUID'i değişirse asset silinmemiş olsa bile HERKES için yok olur — ve o
    GUID'i onarmak EDİTÖR KAPALIYKEN yapılır.** `.meta`'daki `guid` asset'in kimliğidir; dosya
    yerinde dursa da GUID değişince ona bakan her referans (mod `loadout`'ları, `WeaponCatalog` /
    `NetItemCatalog`, prefabın `definition` alanı, sahnedeki örnekler) boşluğa bakar — Unity
    "Missing" gösterir, `git status` ise yalnız iki satırlık masum bir `.meta` değişikliği. Teşhis
    ucuz: eski ve yeni GUID'i repoda ara; **yeni GUID'e hiçbir dosya referans vermiyorsa** değişim
    kazadır, doğru onarım eski GUID'i geri yazmaktır (önce başka bir `.meta` onu sahiplenmiyor mu
    diye bak — sahipleniyorsa gerçek bir çakışma vardır). ⚠️ **Ama `.meta`'yı Unity açıkken
    düzenleme:** editör GUID değişimini canlı yakalayıp asset'i yeniden içe aktarıyor ve prefabı
    **boş bir GameObject olarak diske geri yazıyor** (385 KB → 914 byte). Onarım sırası: Unity'yi
    kapat → GUID'i yaz → editörü aç. Aynısı GUID'e dokunan her toplu düzenleme için geçerlidir.

64. **`LoadSceneAsync` varsayılan ayarla `LoadScene`'den YAVAŞTIR — sebebi asenkronluk değil,
    `Application.backgroundLoadingPriority`.** Unity kare hızını korumak için yükleme
    entegrasyonuna kare başına yalnızca küçük bir dilim ayırır (proje varsayılanı genelde
    `BelowNormal`); senkron yükleme ise her şeyi tek karede bitirir. Yani "yükleme ekranı koyduk,
    geçiş uzadı" tanısı yanlıştır: uzatan ekran değil, önceliktir. Geçiş boyunca `High`'a çekip
    **eski değerine** geri koy (sabit bir değere değil — ayarı başka bir yer değiştirmiş olabilir).
    ⚠️ Ödünç açıktır: `High` kare süresini uzatır, yani VR'da geçiş boyunca titreme artar. Kabul
    edilebilir olmasının sebebi tam da yükleme ekranıdır — kapatılan şey zaten o karelerdir.

65. **Dünya-uzayı bir overlay'i kamera YOKKEN çizme — orijinde asılı kalır ve kimse görmez.**
    `HudFollow` paneli `Camera.main`'in önüne koyar; kamera null iken panel dünya orijininde
    (arenanın ortasında, ayakların dibinde ya da geometrinin içinde) durur. Bu, sahne geçişinde
    bir istisna değil KURALDIR: eski sahnenin kamerası aktivasyonda ölür, yenisi bir sonraki
    karede gelir. Aynı sebeple kamera **değiştiğinde** `HudFollow` yeniden başlatılmalıdır
    (`enabled = false; enabled = true`) — yoksa panel eski kameranın konumundan yenisine doğru
    süzülür ve geçişin yarısı boyunca yanlış yerde durur. Belirti sinsidir: **masaüstü
    (screen-space) varyantı sorunsuz görünür, yalnız VR'da hiç çizilmez** — "sadece adminde
    gözüküyor" tarifi neredeyse her zaman budur.

66. **Rig görselleri İSİMLE değil BİLEŞEN TİPİYLE gizlenir — GameObject adı donanım varyantına göre
    değişir, tip değişmez.** BB rig'i kumandanın üç modelini birden taşır (`Touch`, `Touch Pro`,
    `Touch Plus`) ve `OVRControllerHelper` çalışma anında bağlı olanı açar. `ControllerModelHider`
    isim deseniyle çalışırken hedefi ıskalıyordu: aradığı `questController_animrig` deseni rig'de
    24 objeyle eşleşiyor ama **hiçbiri aktif değil** (Quest 1 / Rift S varyantı), Quest 3'te açılan
    varyant ise `MetaQuestTouchPlus_Left/Right → oculus_controller_l/r_MeshX` adıyla desene hiç
    uymuyor — yani kumanda modeli sahada çiziliyordu ve bu iki kez tekrarlandı. Doğru ölçüt tiptir:
    `OVRControllerHelper` ve tip adı `HandVisual` olan her `MonoBehaviour`. ⚠️ Aynı tarama
    `SyntheticHand`, `OVRHand`, interactor'lar, retiküller ve `HandSphereMap`'e DOKUNMAZ — kavrama
    onlara bağlıdır, kapatılırsa silah hiç tutulamaz. ⚠️ Gizleme tek seferlik değil `LateUpdate`
    başınadır: Meta bırak-tut'ta bu objeleri yeniden aktifleştirir. Genel ders: SDK'nın **adı** bir
    sözleşme değildir, bileşen tipi öyledir.
67. **Sunucudaki maç durumunu sessizce sıfırlamak, istemciyi ölüm ekranında DONDURUR.**
    `ResetMatchStateLocked` `Hp`/`Alive` alanlarını yazar ama telde hiçbir şey üretmez. Tek turlu
    modlarda zararsızdı: istemci zaten `load_match` geldiğinde kendini sıfırlıyor. Tur tabanlı modda
    (§3.8.2) turlar arası `load_match` **yoktur** → mesaj gitmeyince ölü oyuncu `playing` fazına ölü
    ekranıyla girer ve ateş edemez. Kural: **oyuncunun görebileceği bir durum değişimi telde de
    görünmelidir** — canlandırma `RevivePlayerLocked` (`health_update`) ile yapılır.
68. **Bir oyuncu durumuna yasak koyarken o durumu değiştiren TÜM yolları kapat.** Canlandırmanın iki
    yolu var ve ayrı kod yollarıdır: talep tabanlı (`revive_request`) ve zamanlayıcı tabanlı
    (`REVIVE_GRACE`). `reviveAnchor:"none"` eklenirken yalnız talebi kapatmak "tur içinde canlanma
    yok" kuralını 20 saniyelik bir gecikmeye çevirirdi — kural işlemez, hata da vermezdi. Aynı tuzağa
    kalibrasyon yasağında bir kez düşülmüştü (§3.7).
69. **Aynı değeri tekrar tekrar yazan bir bayrak, yayın tetikliyorsa fan-out'a dönüşür.**
    `PlayerRegistry.SetReady` koşulsuz `Changed` yayınlasa her çağrı bir TAM `lobby_state`
    broadcast'i olurdu; toplanma kapısı bu bayrağı yeniden kullandığı için (§3.8.2) "tabandayım"
    bildirimleri roster'ı saniyede birkaç kez herkese yollardı. Kural iki taraflıdır: registry
    **değişmediyse yayınlamaz**, istemci de yalnız **kenarda** gönderir. Soru her zaman "kaç bayt"
    değil **"kaç datagram"**dır (§3.12).
70. **Mod kancaları `await` edemez — kancadan mesaj yollamak sıra bozar.** `IGameMode.OnTick`
    `void`'dir; mod bir mesaj göndermek isterse ya kilit altında gönderim yapar (kilit sözleşmesi
    ihlali) ya da ateşle-unut bir `Task` bırakır — ikincisinde iki gönderim yarışır ve WebSocket'e
    ters sırada düşebilir. Çözüm: kanca mesajı **kilit altında bir bekleyen kutuya** yazar, tik
    döngüsü kanca dönüşünde yollar. Tek gönderici kalır, sıra korunur.
71. **Elle yazılan bir asset referansı, Unity'nin ürettiği `.meta` GUID'iyle tutmuyorsa SESSİZCE
    null olur.** Bir `.asset`/`.prefab` metin olarak (editör dışından) üretildiğinde `.meta`'yı
    Unity yazar ve **kendi rastgele GUID'ini** verir; ona elle yazılmış `guid:` referansları
    (katalog satırı, `hudPrefab` alanı) o an kırılır. Kırık referans hata basmaz — alan yalnızca
    boş görünür. En pahalı biçimi **prefab referansı**dır: prefab bulunamayınca örneklenmez, onunla
    birlikte köküne asılı bileşenler de hiç doğmaz — yani bozulan tek bir alan değil, o bileşenin
    taşıdığı bütün davranıştır ve hiçbir yerde hata görünmez.
    **Kural:** asset'i metin olarak üretiyorsan referansı yazmadan ÖNCE hedefin `.meta`'sındaki
    gerçek GUID'i oku; işin sonunda üretilen dosyalardaki her `guid:`'i projedeki `.meta`'larla
    karşılaştır (çözülmeyen varsa iş bitmemiştir). Editörde sürükleyip bırakmak bu tuzağı hiç
    doğurmaz — elle GUID yazmak yalnız editör kapalıyken bir çaredir, varsayılan yol değildir.
72. **Bir şartı "girişte bir kez" ölçmek, şartı sürdürmez.** Girişte ölçülen bir kapı, oyuncunun
    koşulu bir saniye sağlayıp bırakmasına açıktır — turnuvada bu "tabana değip kaç, turu sahanın
    ortasında karşıla" demek olurdu. Kapıyı açan koşul kararın **yürürlüğe girmesine kadar**
    ölçülmeli ve bozulursa karar geri alınabilmeli (`TryCancelCountdownForMode`). Bunun kaçınılmaz
    ikinci yarısı: kapının dayandığı bayrak (`ready`) o pencere boyunca **temizlenmez**, yoksa geri
    alma kararının dayanağı kalmaz.
73. **Elle ayarlanan bir yerleşimi editör aracı HER KOŞUDA yeniden hesaplamaz.** `Muzzle`/
    `MuzzleFlash` için zaten geçerli olan kural kovan çıkışına (`Eject`) da uygulanır:
    `WeaponKitBuilder` onu **yalnız yoksa** üretir, varsa **taşımaz**. Sebebi iki katlı:
    (a) yeri gözle ayarlanan bir şeydir, (b) hesabın kendisi güvenilmez — silah ölçüsü alt
    ağaçtaki tüm Renderer'lardan çıkarılıyordu ve **kapalı bir `Renderer`'ın `bounds`'u
    BAYATTIR** (Unity güncellemez, en son çizildiği yerdeki **dünya** kutusunu döndürür), yani
    prefabdaki kapalı çerçeve `VA_WeaponFrame` metrelerce ötede bir kutu sızdırıp kovanı silahın
    2.5 m önünde doğuruyordu. Ölçüm artık kapalı Renderer'ı ve çerçeve alt ağacını atlıyor, ama
    asıl koruma **yeniden yazmamaktır**: hesap düzelse bile elle yapılan ince ayarı geri getirmez.
    Aynı desen ses kliplerinde de var (yalnız alan boşsa yazılır). Genel kural: **bir asset'e
    hesaplanmış sayı yazan araç, o alana insan dokunmuşsa yazmaz** — sapma gözle fark edilmeden
    commit'lenir.
74. **Bir alanın "iki yazarı" varsa, yazan her yol AYNI kapıdan geçmelidir.** Kural şekli
    (`_rules`) hem modun/lobinin tanımından hem operatörün dost ateşi anahtarından besleniyor;
    ikincisini unutan tek bir atama anahtarı sessizce düşürür — harita sahnelemek ya da lobiye
    dönmek dost ateşini kapatırdı ve operatör bunu ancak sahada, yanlış vuruşta fark ederdi.
    Bu yüzden `_rules`'ü yazan tek kapı `MatchDirector.ApplyRulesLocked`'tır: taban şekli argüman
    olarak gelir, anahtar orada damgalanır. ⚠️ İşlevsel yükü yalnız `start_match` taşır (koşan maçın
    kapısını o besler); diğer çağrı yerleri (lobi profili, sahneleme) `welcome.match.rules`'un
    **doğru görünmesi** içindir — bugün onu okuyan bir tüketici olmasa da, olmadığı gün fark
    edilmeyecek bir yalan bırakırdı.
75. **Bir görsel uyarı mekanizması, süreceği malzemeyi de kısıtlıyorsa "başka bir Renderer'a
    bağlarız" diye taşınamaz.** Muhafazanın yarı saydam duvarı `_BaseColor.a` yazıyor ve alfa
    düşünce Renderer'ı kapatıyordu — ikisi de yalnız o iş için üretilmiş bir geometride masumdur.
    Environment'ın gerçek duvarlarına bağlanınca birincisi **hiç iş görmez** (opak malzemede alfa
    yazımının görsel karşılığı yok), ikincisi **duvarı yok eder** (oyuncu uzaktayken kapanır).
    Doğru hamle mekanizmayı taşımak değil, uyarıyı malzemeden bağımsız bir kanala almaktı: HMD'ye
    bağlı karartma quad'ı. ⚠️ Bunun bedeli bir kurulum kuralıdır — **environment'ın duvarları
    fiziksel sınırla çakışmalıdır**, çünkü oyuncunun gördüğü sınır artık yalnız o duvardır.
76. **Bir editör aracının yanlış seçimle çalıştırılması, çıktısı DOSYAYSA sessizce kalıcı olur.**
    `Build Arena From TestMesh` "seçili kök"ten plan çıkarıyordu; bir silah prefabı seçiliyken
    çalıştırıldığında 20 cm'lik bir "arena" yazdı ve o dosya bir mekanın boyut dosyası olarak
    aylarca durdu (`"name": "WPN_M4A4"`, kolonu `AR_B_Mag`). Hiçbir yerde hata yok: dosya geçerli
    JSON, muhafaza onu okuyup çalışıyor — yalnız yanlış. Kural: **çıktısı üzerine yazan bir araç,
    girdisini tür olarak değil ANLAM olarak doğrulamalı** (bu yüzden yeni maket aracı hedef dosyayı
    sormaz — maketin kendi işaretçisinden okur, yanlış dosyaya yazma yolu hiç açılmaz).

77. **Bir OBJE ADI, o objeyi üreten aracın imzası değildir — ada bakarak toplu silme yapılmaz.**
    `Wall_N/S/E/W` hem kaldırılan muhafaza duvarlarının hem de bir arenanın gerçek environment
    duvarlarının adı (IceWorld'de ProBuilder ile modellenmiş, köşe parçalarıyla birlikte duran
    duvarlar tam olarak bu adı taşır). Ada bakan bir temizlik arenanın kendisini siler; ada bakan
    bir "kalıntı var" uyarısı ise her açılışta yanlış alarm verir ve sağlık raporunun tamamını
    okunmaz kılar. Ayırt eden şey **bileşen izidir** (üretilen duvar: `MeshFilter` + kutu mesh'i;
    sanat duvarı: `ProBuilderMesh`). Bu yüzden `Configure All Build Elements` ada bakan bir kontrol
    TAŞIMAZ.

78. **Ölçü gösteren bir çıktı, DÖNDÜRÜLMÜŞ bir ebeveynin altına kurulmaz.** Inspector, seçim
    kutusu ve ProBuilder ölçü göstergesi — hepsi **dünya eksenine hizalı** kutuyu gösterir. Ölçü
    maketi bir zamanlar `ArenaBoundary`'nin (48,72° döndürülmüş) altına kuruluyordu: dosyada 12×12
    yazan kusursuz bir kare orada `12 × (cos θ + sin θ) ≈ 16,93` okunuyor ve araç ölçeği bozuyor
    sanılıyordu. Geometri doğru olsa bile **okunamayan ölçü bozuk ölçüdür**; maket bu yüzden
    sahneden bağımsız, dönüşsüz üretilir ve konumlandırma kullanıcıya bırakılır. Genel kural:
    çıktısı *ölçü* olan bir araç, o ölçünün okunacağı çerçeveyi de garanti etmelidir.

79. **Bir API başarısızlığını exception ile bildirmiyorsa dönüş değerini okumak ZORUNLUDUR.**
    ProBuilder'ın `CreateShapeFromPolygon`'u üçgenleme düştüğünde `ActionResult.Failure` döner ve
    geriye **boş bir mesh** bırakır — sahnede adı doğru, geometrisi olmayan bir obje. Sessizce
    devam eden kod bunu hata saymaz; eksiklik ancak "maketimde taban yok" diye çok sonra fark
    edilir. Düşen çokgen silinir, taban düşerse üretim tümden başarısız sayılır.

80. **`EditorOnly` etiketi build'den siler, EDİTÖR PLAY KİPİNDEN silmez — ada bakan her arama onu
    da görür.** Ölçü maketi build'e girmiyor diye "sahnede yok" sayılamaz: Play tuşuna basıldığında
    hiyerarşidedir. Maketin kalibrasyon küpleri sahnedeki işaretçilerle aynı adı taşır
    (`anchor_a`/`anchor_b` — aynı şeye iki ad vermek daha büyük bir kötülük), yani `ArenaCalibrator`
    ada bakarak arasa arenayı görünmeyen bir küpe göre hizalayabilirdi. Çözüm adı bozmak değil,
    **ayırt edici bir bileşen**: küpte `DimensionAnchor` vardır ve arama onu taşıyan objeleri atlar
    (+ `EditorOnly` etiketli kökleri hiç gezmez). Genel kural: ada göre obje çözen kodun kapsamı
    "build'e giren" değil **"sahnede duran"** kümesidir, ve iki şeyi ayırmanın doğru yolu adı
    çeşitlendirmek değil türü işaretlemektir.

81. **İzlemeden gelen "kafa" GÖZÜN pozudur; humanoid kafa KEMİĞİ oraya oturtulmaz.**
    `centerEyeAnchor` (hem yerel rig'de hem telde) gözün yeridir, kafa kemiği ise Ch15'te gözün
    ~12 cm altında ve ~9 cm gerisindedir. İkisini eşit saymak bütün iskeleti bir kafa yarısı kadar
    yukarı + öne kaydırır ve belirti **gövdenin neresinden baktığına göre değişir**: uzaktan
    bakanda görünmez, oyuncunun KENDİ birinci şahıs görüşünde ölümcüldür — yaka kemiği gözün
    18-20 cm altında olması gerekirken 6-7 cm altına çıkar, yani ana kameranın near-clip'inin
    (0.1 m) **içine** girer ve oyuncu aşağı bakınca kendi gövdesinin içini görür. İkinci yüzü
    ölçektir: oyuncunun göz yüksekliğini modelin **kafa kemiği** yüksekliğine bölmek avatarı
    sistematik olarak ~%8 büyütür (büyüyen gövde = yüze daha yakın göğüs, yani aynı sorunun
    beslemesi). Kural: **ölçülen büyüklüğün model tarafındaki karşılığı aynı nokta olmalı** —
    göz/göz. ⚠️ Bugün bu eşlemeyi Movement SDK'nın retarget config'i yapıyor ve ölçek tarafını
    `SkeletonRetargeter.ApplyHeadScale` (kafayı 0.95 ile daha az büyütür) taşıyor — yani madde bir
    uygulama talimatı değil **retarget config'i hazırlarken kontrol edilecek bir ölçüttür**: birinci
    şahısta aşağı bak, yaka near-clip'in içinde mi.

82. **Bilek 30 cm'den bakıldığında `skinWeights` bir görsel ayar değil, DOĞRULUK ayarıdır.**
    `QualitySettings`'te Android varsayılanı **"Mobile" seviyesidir ve `skinWeights: 2`** (PC'de 4),
    yani vertex başına yalnız iki kemik. Bilek gibi çok kemikli bir bölge bununla lineer blend
    skinning altında "şeker ambalajı" gibi çöker — büküldükçe incelip kalınlaşır. Uzaktan bakan bunu
    görmez; oyuncunun KENDİ birinci şahıs görüşünde belirgindir. Kural: oyuncunun 30 cm'den baktığı
    **yerel** gövdede `SkinnedMeshRenderer.quality` Bone4'e **sabitlenir** (Auto bırakılmaz).
    ⚠️ Uzak avatarlarda Auto kalır ve bu bilinçlidir: eşzamanlı oyuncu kotası yoktur, N avatarın
    hepsini Bone4 yapmak bedava değildir ve mesafeden fark edilmez.

83. **URP overlay kamerasının near-clip'i XR'da SESSİZCE yok sayılır.** "Gövdeyi ayrı bir katmana
    alıp daha büyük near-clip'li bir overlay kamerayla çiz" kalıbı masaüstünde çalışır, Quest'te
    çalışmaz: `XRLayout.AddCamera` z-aralığını **yalnız base kameradan** alır
    (`SetDisplayZRange(camera.nearClipPlane, ...)`) ve URP her overlay kamerada
    `UpdateCameraStereoMatrices(overlayCamera, xrPass)` ile projeksiyonu XR pass'inkiyle **ezer**.
    Yani overlay kameraya yazılan `nearClipPlane` hiçbir işe yaramaz. Sinsi tarafı editörde
    (XR display yokken) doğru çalışmasıdır — bileşen "kapalı duruyor, gerekirse açılır" diye
    aylarca durabilir. Genel kural: **XR'da kamera projeksiyonu senin değil, display subsystem'in
    kararıdır**; near-clip'e dayanan her çözüm ana kameranın near-clip'i üzerinden kurulur.
    Bu projede bileşen (`LocalBodyOverlayCamera`) bu yüzden **silindi**; asıl neden zaten
    maddede 81'deki ofset eksikliğiydi. ⚠️ `LocalBody` katmanı `TagManager`'da bırakıldı (artık
    kullanılmıyor): katman silmek ProjectSettings'e dokunmaktır ve o dosyanın kendi tuzağı var.

84. **Bir prefabın TEK sürücü bileşenini silmek, prefabı bozuk değil SESSİZ bırakır — mesh çizilir,
    hiçbir şey onu sürmez.** Bir görsel bileşen (`RemoteAvatar`, `LocalBodyAvatar`) alanlarının
    yalnız bir kısmını doldurup gerisini sürücüye bırakabilir: uzak avatarda `head`/`handL`/`handR`/
    `body` alanlarının **hepsi boş** olabilir, çünkü gövdeyi kemik kemik süren ayrı bir bileşen
    vardır. O bileşen silindiğinde geriye kalan "yedek yol" hiçbir şey yapmaz (her `Apply` çağrısı
    `null` hedefe düşer), avatar dünya orijininde T-pozunda donar ve sahada **"ağ çalışmıyor, admin
    oyuncuyu görmüyor"** diye okunur — teşhis protokole/sunucuya yönelir, oysa eksik olan tek bir
    prefab bağıdır. İki kural: (1) bir bileşeni silmeden önce onu **referanslayan prefabların
    alanlarına bak** — kod derleniyor olması prefabın çalıştığı anlamına gelmez, eksik script
    derlemeyi kırmaz; (2) sürücüsü olmayan görsel yolu **sessiz bırakma**, `LogError` bas ve kökü
    yine de doğru yere taşı — yanlış pozda ama doğru yerde duran avatar teşhis edilebilir, hiç
    görünmeyen avatar edilemez.

85. **Rol ayrımı `Initialize`'da yapılıyorsa, role bağlı bileşen prefabda AÇIK gelemez —
    `OnEnable` `Instantiate` anında koşar.** "Bileşeni prefabda bırak, kurulumda rolüne göre
    aç/kapat" kalıbı sezgisel ama bir kare geç kalır: `Instantiate` dönmeden `Awake`+`OnEnable`
    çalışır, `Initialize` ise ondan sonra çağrılır. Aradaki pencerede bileşen **bir kez tam
    yetkiyle** koşmuştur. `MetaSourceDataProvider` (`OVRBody`) örneğinde bu, her uzak avatar
    doğarken bir `StartBodyTracking` çağrısıdır: HMD'siz admin'de spawn başına bir hata satırı,
    Quest'te oyuncunun kendi izlemesini uzak bir avatarın yeniden başlatması. Doğrusu tersidir —
    **bileşen prefabda KAPALI gelir, sahibi olan taraf onu açar**; "kapalı doğup açılan" yolda
    yanlış çalışan bir pencere yoktur. Aynı kural `AudioSource.playOnAwake`, `ParticleSystem`,
    fizik ve abonelik kuran her bileşen için geçerlidir (uzak eşya örneklerinin PASİF kuluçka
    kökünde kurulmasının sebebi de budur).

86. **85'in AYNASI: pasif objede `Awake` HİÇ koşmaz — kurulum çağrısı objeyi açmadan yapılmaz.**
    "Kurulana kadar gizli tut" deseni (`visualRoot.SetActive(false)`) ile "kurulumu `Initialize`
    yapar" deseni yan yana geldiğinde sessizce çakışırlar: gizlenen kök kurulacak bileşenin
    KENDİ objesiyse, o objenin `Awake`'i hiç çalışmamıştır ve `Initialize` içindeki her referans
    null'dur. Belirtisi teşhisi saptırır — `NullReferenceException` kurulum satırını gösterir,
    asıl sebep ise iki satır yukarıdaki `SetActive` sırasıdır; üstelik SDK tarafında sonuç
    "sahiplik None, karakter T-pozunda" gibi tamamen alakasız görünen ikinci bir hata üretir.
    İki kural: (1) `SetActive(true)` **önce** çağrılır — `Awake`'leri kendi çağrısı içinde senkron
    koşturur, yani o satırdan sonra obje kurulmaya hazırdır; (2) dışarıdan çağrılan kurulum
    metotları referanslarını **`Awake`'ten ayrı, idempotent** bir çözücüden alır (`Awake` koşmamış
    olabilir) — böylece sıra hatası patlamaya değil, en fazla bir kez fazladan `GetComponent`'e
    mal olur.

87. **Görünürlüğü `SetActive` ile yönetmek, altındaki bileşenlerin `OnEnable`'ını TEKRAR
    koşturur — ve bazı bileşenler başarısızlıkta kendilerini KALICI kapatır.** "Gizle/göster"
    için objeyi kapatmak masumdur sanılır, oysa her gizle/göster çevrimi tam bir yaşam döngüsü
    turudur. `OVRBody` bunun en pahalı örneği: `OnDisable` açık son örnekte
    `StopBodyTracking` çağırıyor, `OnEnable` yeniden başlatmayı deniyor ve
    **başaramazsa `enabled = false` yapıp bir daha DENEMİYOR**. Yani rig'in bir an kaybolduğu
    her harita geçişi, oyuncunun gövdesini oturumun geri kalanı boyunca sessizce öldürebilecek
    bir kumardır — geriye konsolda tek bir `[OVRBody]` satırı kalır ve o satır "gövdem neden
    yok" sorusuna bağlanmaz. Kural: **kurulmuş bir alt ağacın görünürlüğü renderer düzeyinde
    yönetilir** (`Renderer.enabled`), `SetActive` yalnız kurulumdan ÖNCE meşrudur (orada henüz
    başlatılmış bir şey yoktur). Kendini kapatabilen bir bileşene bağımlıysan durumunu kurulumdan
    sonra **oku ve bildir**; sessiz kalırsa teşhis sensöre/SDK'ya gider, oysa sebep senin
    gizleme yöntemindir.
88. **Ters derinlik testi (`ZTest Greater`) oyuncunun KENDİ silahını, elini ve gövdesini de
    "engel" sayar.** Duvar arkasından görünen bir işaret (taban şeridinin x-ray çizimi) yalnız
    arena dekorunun arkasında değil, **eldeki silahın ve `LocalBodyAvatar` bacaklarının** arkasında
    da geçerlidir: oyuncu kendi tabanının içinde durup aşağı baktığında hayalet doğrudan silahının
    üstüne çizilir ve "silahım şeffaflaştı" gibi görünür. Çözüm efekti kapatmak değil **yakın
    mesafe sönümüdür** — `M_BaseZoneXRay`'in `_NearFadeStart`/`_NearFadeEnd` alanları hayaleti
    birkaç metrenin altında tamamen söndürür; zaten o mesafede gerçek şerit görünüyordur, yani
    sönüm hiçbir bilgi kaybettirmez. Aynı sorun ileride duvar arkasından çizilecek her işaret için
    (takım arkadaşı halkası, hedef işareti) tekrar eder.

89. **Bir `.meta` guid'i değişince o asset'e yapılan TÜM referanslar sessizce ölür — ne derleyici
    ne konsol bunu tam olarak söyler.** Asset silinip yeniden import edilirse (ya da `.meta`
    kaybolursa) yeni bir guid üretilir; eski guid'e bakan her yer artık `None` görür. Tehlikesi
    kapsamıdır: tek bir silah için bu aynı anda **sahnedeki prefab örneği**, **SO alanı**
    (`Weapon.definition`), **SO dizileri** (`ModeDefinition.loadout`) ve **kataloglar**
    (`NetItemCatalog`, `WeaponCatalog`) demektir — belirtiler de o kadar dağınık çıkar: silah
    sahnede yok, uzak oyuncunun elinde çizilmiyor, atış sesi/alevi gelmiyor, alınınca hasar
    vermiyor. Hepsi tek sebepten ama hiçbiri sebebi göstermez; Unity yalnız sahnedeki prefab
    örneği için "Missing Prefab" basar, dizideki boş slot için hiçbir şey demez.
    **Tespit:** tüm `*.unity`/`*.prefab`/`*.asset` dosyalarındaki `guid:` değerlerini toplayıp
    `AssetDatabase.GUIDToAssetPath` ile çözülüyor mu diye bak — çözülemeyen her guid ölü bir
    referanstır ve bu tarama saniyeler sürer. **Onarım:** ölü guid'in taşıdığı `fileID`'ler yeni
    asset'te de duruyorsa (asset aynı, yalnız kimliği değişmiş) guid'i metinsel değiştirmek birebir
    doğrudur; tutmuyorsa referanslar Unity API'siyle tek tek bağlanır — kopuk alanı boş alandan
    ayırmak için `objectReferenceValue == null && objectReferenceInstanceIDValue != 0` bak.
    ⚠️ Editörde **açık ve kirli** bir sahnenin dosyasına diskten dokunma: kaydedilmemiş iş ezilir.

90. **Kayıt listeleri elle değil klasör taramasından EŞİTLENİR — "ekleyen" bir araç silineni
    temizleyemez.** Bir arena silindiğinde ya da taşındığında geride üç ölü kayıt kalır: Build
    Settings satırı, `GameCatalog.maps` girdisi ve onu destekleyen `ModeDefinition.maps`
    referansı. Üçü de sessizdir — katalogdaki `Missing` referans Inspector'da boş bir satır, Build
    Settings'teki ölü yol ise build'i sebebi görünmeyen bir hatayla düşürür (sahne dosyası yok ama
    liste onu istiyor). Bu yüzden `Configure All Build Elements` tek doğruluk kaynağı olarak
    **`Venues/*/Scenes/*/` klasör ağacını** alır: ağaçta olmayan her kaydı siler, ağaçta olup
    kaydı eksik olanı uyarı olarak bildirir. Pratik sonucu şudur: **arena silmek/taşımak bir
    senkronizasyon adımı ister** (*Yalnız Senkronize Et*), kayıtları elle temizlemek değil.

---

## 8. Durum ve sıradaki işler

**Bugün çalışan sistem** (ayrıntı §2–§7): lobi + 20 Hz poz senkronu + **yerel gövde görseli**
(oyuncu omuzlarından aşağı kendi kollarını ve gövdesini görür; kumanda modeli/Meta eli çizilmez) +
**elde tutulan eşya senkronu**
(uzak oyuncuların silahı kanonik kavramayla çizilir) + **çerçeveden silah seçimi** (sahnedeki silah
çerçevesinden ayrılmaz; ≤2 m'den nişan alınıp grip'e basılınca ele bir klonu gelir, bırakılınca
gizlenir ve aynı silah aynı mermiyle geri çağrılır — oyuncu başına tek silah, harita başına
sıfırlanır) + **soket tabanlı kavrama** (elde tutulan eşyada: el yaklaşınca gösterge belirir,
soketin üstünde grip'e basılınca kavranır) + **UDP
atış/atma olay kanalı** (namlu alevi, ses, mermi izi — her olay kendi `serverTick`'inde,
interpolasyon saatiyle oynatılır) + sunucu-otoriter maç
(faz makinesi, vuruş hattı, free-roam canlanma, kill-feed/HUD) · **üç oyun modu** — `tdm` (Takım
Ölüm Maçı), `ffa` (Herkes Tek: takımsız, bireysel skor, sabit durma canlanması, grip'e basınca
elde rastgele silah) ve `tournament` (Turnuva: tur tabanlı takım elemesi — tur içinde canlanma yok,
tur bitince herkes tabanında toplanıp yeni tur başlar; §3.8.2) · **çok mod altyapısı** (`ModeRules`
şekil tanımı §3.9, bireysel skor, `MatchOutcome`, takım-agnostik `ModeHudBase`, admin'den maç
süresi/skor limiti/geri sayım) · **iki mekan** (`Outdoor12x12`, `VortexAntep`) ve her birinin kendi lobisi —
sunucu açılışta hangisinin oynatılacağını sorar (§3.8) + arena kurulum araç zinciri
(`Template Temellerini Yükle` → ölçü maketi → `Configure All Build Elements`) ·
admin **sahne-içi gözlemci** (üç kamera kipi + sahne üstü yönetim HUD'ı,
çoklu admin) · geliştirici araç seti (`Tools > VortexArena > Development > Dev`, `dev-targets.json`,
`Ctrl+Alt+R`) · rolden bağımsız adres zinciri + `ConnectionOverlay` bağlantı hata ekranı ·
**sunucu-otoriter kalibrasyon durumu** (§3.11: admin sıfırlar → oyuncu savaş dışı + avatarı parlar;
geri açmayı gözlük yapar) · **ağ telemetrisi** (§3.12/§6.7: sunucu konsolunda gerçek bayt-sn +
paket-sn + tik kayması + uplink jitter/kayıp; gözlükte ölçülen RTT/downlink jitter/kayıp → admin
istatistik panelinde **PING** kolonu) · WPF operatör launcher'ı (sunucuyu `--venue` ile başlatır) +
dört dağıtım betiği.

> **Sıradaki büyük iş: bulut kalibrasyonu** (Meta grup / paylaşılan uzamsal anchor ile toplu
> hizalama). Altyapısı hazır bırakıldı — `set_calibration.source` `"cloud"`'u kabul ediyor,
> `clear_calibration{playerId:0}` toplu sıfırlıyor, `ArenaCalibrator.AlignRigToAnchorPose`
> `internal` seam olarak açık. Protokol değişikliği gerekmiyor.

> **Yeni bir modun maliyeti ne olmalı:** `ffa` bugün protokolde **tek bir alan bile tutmuyor** ve
> TDM ile hiçbir kod paylaşmıyor — sunucuda bir `IGameMode` dosyası + tek satır kayıt, istemcide
> bir mod kutusu + bir paylaşımlı bileşen (`WeaponGranter`). Sonraki modlar (turnuva, silah
> yarışı, bölge kontrolü, zombi) aynı ucuzlukta gelmeli; gelmiyorsa eksik olan `ModeRules`'te bir
> kuraldır, istemcide bir `if` değil.

**Sıradaki planlanmış işler `plan/` altındadır** (`plan/README.md` tablosu); biten işin dosyası
silinir.

**Kapsam dışı — bilinçli kararlar** (yeniden gündeme gelirse bu gerekçeler tartışılmalı):

- **"Oyuncunun gözünden izleme" video akışıyla DEĞİL, oyun datasıyla.** MJPEG/video akışı (cosmos
  `CameraStreamer` portu) **kapsam dışıdır**: admin zaten sahneyi kendi makinesinde render ediyor ve
  poz/can/skor/olay verisi ağdan geliyor. İstenen görüntü bu mevcut datadan üretilecek (admin
  kamerasını hedef oyuncunun poz'una kilitlemek); protokole yeni binary kare tipi, sunucuya kare
  relay'i ve Quest'te encode maliyeti **girmeyecek**.
- **Kayıt/replay, ısı haritası, hasar istatistiği** — protokolde veri yok, ayrı iş.
- **Admin'in POV kipinde oyuncunun mod HUD'ını görmesi** — mod HUD'ı player-only; istenirse
  `ModeHudSpawner`'a "gözlemci kipi" eklenir.
- **Admin'den oyuncuya ses/mesaj** — `identify` dışında kanal yok.
- **Hile koruması** — ürün gözetimli özel alanda çalıştığı için bilinçli olarak yok
  (`Docs/ArenaNet-Protokol.md` §10.3).
- **Quaternion sıkıştırma / delta snapshot** — bir zamanlar "kalabalık maçta bant gerekirse"
  diye ufukta duruyordu; **listeden çıkarıldı.** Bant hiçbir zaman darboğaz değil (§3.12) ve
  sıkıştırma bağlayıcı kısıt olan paket sayısına dokunmuyor. Doğru kaldıraç **kanal
  birleştirmek**tir ve o yapıldı: `0x05` (§6.8) + `health_update`'in hedefli gönderimi paket
  sayısını ~%40 düşürdü, sıkıştırma bunun yanında hiçbir şey getirmezdi (§7, "kablosuzda maliyet
  BAYT değil ÇERÇEVE" maddesi).

**Planlanmamış ufuk:** yeni modlar (bölge kontrolü, turnuva, silah yarışı, zombi), dinamik obje senkronu
(`NetIdentity` + `NetSpawnCatalog` üzerinden), Meta colocation/paylaşımlı anchor araştırması
(offline çalışma şartıyla), launcher ekranından APK dağıtımı, eşzamanlı oyuncu kotası (lisanslama
katmanı geldiğinde).
