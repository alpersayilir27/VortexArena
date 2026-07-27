# VortexArena — Sistem Özeti (ne yapıldı, nasıl çalışıyor, nasıl kullanılır)

> **Bu doküman ne?** Faz 0–6'da kurulan sistemin tek sayfalık haritası: mimari, ağ mantığı, hangi
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
> | Faz faz uygulama planı ve durum | `plan/README.md` |

---

## 1. Ürün ve roller

**VortexArena** = işletmelere (LBE) kurulan **free-roam VR PvP arena**. Oyuncular fiziksel alanda
**1:1 yürür** (lokomosyon yok), hedef cihaz **yalnız Quest 3 / 3S**. Aynı Unity projesinden iki
build çıkar:

| Build | Rol | Ne yapar |
|---|---|---|
| **Android (Quest)** | `player` | Lobi → maç; oynar, poz gönderir, ateş eder |
| **Windows** | `admin` | `--server-ip` ile açılır → **oyuncularla aynı sahneye** girer (gözlemci): 3 kamera kipi + sahne üstü yönetim HUD'ı (roster, mod+harita, start/abort, istatistik) |
| **Windows** | launcher | `launcher/` — Flutter operatör uygulaması: admin exe yolu + sunucu IP'sini tutar, oyunu `--server-ip` ile başlatır |

Üçüncü bileşen: **`Server/` — kendi .NET 10 konsol sunucumuz** (standalone exe, tamamen offline
LAN). Mirror/NGO gibi hazır netcode **kullanılmıyor**; hem oyun kurallarının sunucuda koşması hem
de işletmede internetsiz çalışma şartı bunu gerektiriyor.

### Fazlarda ne yapıldı

| Faz | Çıktı | Commit |
|---|---|---|
| **Faz 0** | Feature-first klasör yapısı + asmdef katmanları, Meta umbrella paketi → bireysel paketler, `CLAUDE.md` + `.claude/rules/` + protokol dokümanı | `7e0d402` |
| **Faz 1** | Paylaşılan `VortexArena.Protocol`, .NET sunucu iskeleti (beacon + WS + roster), `ArenaClient`, Boot/Lobby/AdminConsole sahneleri — **lobi E2E** | `5eb1f54` |
| **Faz 2** | UDP poz kanalı (20 Hz), snapshot yayını, uzak avatarlar + interpolasyon, arena kalibrasyonu → arena uzayı, admin taktik görünüm | `128f903` |
| **Faz 3** | Sunucu-otoriter maç akışı (faz makinesi), TDM modu (`IGameMode`), silah/can senkronu, vuruş doğrulama, free-roam canlanma, kill-feed/HUD | `e643b76` |
| **Faz 4** | Editor SDK (Network Parent, `NetIdentity`, sceneId bekçisi, spawn kataloğu), `Export Server Config`, **arena şablon sihirbazı** (A12x12 + DemoVenue üretildi), sunucu harita tablosu, işletme kurulum listesi | `fc1a9bb` |
| **Faz 5** | Geliştirici araç seti (`Tools > VortexArena > Dev` + `dev-targets.json` + tek tıkla sunucu/bot süreçleri, `Ctrl+Alt+R`), rolden bağımsız adres zinciri, **bağlantı hata ekranı** (`ConnectionOverlay` — VR + masaüstü) | `d6ad78c` + bu commit (yeni dosyalar) |
| **Faz 6** | **Admin sahne-içi gözlemci:** dashboard sahnesi tasfiye (admin artık sunucudaki aktif sahnede), 3 kamera kipi (POV/serbest/kuş bakışı), oyuncu halkaları + ad etiketleri, sahne üstü yönetim HUD'ı + tercihler/istatistik panelleri, `UiKit` prosedürel arayüz kiti, `lobby_state`'e `kills/deaths/hp/alive` | bu commit |

---

## 2. Repo haritası ve assembly grafiği

```
D:\Games\vortexarena\
  Assets\
    _Shared\                 ← "ikinci bir mod/arena bunu aynen kullanır mı?" → EVET olan her şey
      Core\                  VortexArena.Core        (arena, savaş, oyuncu, UI, katalog SO'ları, FX)
        Editor\              VortexArena.Core.Editor (Export Server Config, Arena şablon sihirbazı)
      Net\Protocol\          VortexArena.Protocol    (SAF C# — sunucu aynı dosyaları derler)
      Net\Scripts\           VortexArena.Net         (bağlantı/keşif/senkron; oyun bilgisi YOK)
        Editor\              VortexArena.Net.Editor  (Network Parent, sceneId bekçisi)
      App\Scripts\           VortexArena.App         (Boot yönlendirme, Lobi, UiKit, köprüler)
        Admin\               (aynı asmdef) admin gözlemci: kamera kipleri, HUD, paneller
        Editor\              VortexArena.App.Editor  (Dev penceresi: rol/hedef + sunucu/bot süreçleri)
      Arsenal\ FX\ Environments\ Scenes\             (kod-dışı ortak içerik + Boot/Lobby)
      Data\Resources\        GameCatalog.asset — prosedürel admin arayüzü Resources.Load ile okur
    Arenas\Standard\A10x10\  arena kutusu: {Scenes, Data, Prefabs} — arena-özel KOD yazılmaz
    Arenas\Standard\A12x12\  (sihirbazla üretildi)
    Arenas\Standard\IceWorld\ (elle modellenmiş 12×12 tematik arena + Art/{Materials,Textures}
                               + Prefabs\FX_SnowStorm — 7 katmanlı kar fırtınası)
    Arenas\Venues\DemoVenue\ (sihirbazla üretildi — 11×8 asimetrik)
    Modes\TeamDeathmatch\    mod kutusu: {Scripts → VortexArena.Modes.Tdm, Data, UI}
  Server\                    .NET 10 çözümü (Core kütüphanesi + App konsolu + PoseBot test istemcisi)
  launcher\                  Flutter Windows launcher (vortex_launcher) — operatör giriş noktası
    lib\main.dart            uygulama kabuğu
    lib\launcher_config.dart kalıcı ayarlar (admin exe yolu + IP:port) + doğrulama
    lib\launcher_page.dart   tek ekran: Sunucu / Ayarlar / Yönetimi Başlat
  scripts\                   deploy-admin-game.bat · deploy-server.bat · deploy-launcher.bat
  deploy\                    ÜRETİLEN çalıştırılabilirler: admin\ server\ launcher\ (git'e girmez)
  dev-targets.json           dev penceresinin adlandırılmış sunucu hedefleri (COMMIT'Lİ;
                             seçim EditorPrefs'te kişisel kalır — bkz. §6.2)
  Docs\  plan\  .claude\rules\  CLAUDE.md
```

**Bağımlılıklar hep aşağı akar; modlar birbirini referanslamaz:**

```
        VortexArena.Protocol      saf C#, noEngineReferences  ─┐
                 ▲                                            │ aynı .cs dosyaları
        VortexArena.Net           taşıma/keşif/senkron         │ Server csproj'da
                 ▲                                            │ <Compile Include>
        VortexArena.Core          oyun kodu (arena, savaş)     │ ile derlenir
           ▲          ▲                                       │
  VortexArena.App   VortexArena.Modes.Tdm                   ──┘
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
```

Neden böyle: oyuncu gerçekten fiziksel alanda yürüyor, pozu için tek doğru kaynak başlığın kendisi.
Ama can/skor iki istemcide sapmamalı → onlar sunucuda. İstemci `health_update` gelene kadar canını
değiştirmez; `Health.TakeDamage` artık yalnız yerel hedefler (dummy) içindir, oyuncu canı
`Health.ApplyServerHealth` ile sunucudan set edilir.

### 3.3 Arena uzayı (koordinat çerçevesi)

Ağa giden **her poz arena-yerel uzaydadır**: origin = arena zemin merkezi, eksenler duvarlara
hizalı.

```
Quest'in kendi dünya uzayı ──(ArenaCalibrator: 2 nokta + OVRSpatialAnchor)──► arena uzayı
                                     │
                       ArenaSpace.WorldToArena/ArenaToWorld
```

- `ArenaBoundary` sahnede origin'i `ArenaSpace`'e kaydeder; Lobby'de origin yoktur → dünya = arena
  (kimlik dönüşümü).
- Dönüşüm **istemcide** yapılır (`PlayerPoseTracker`); sunucu ve admin ham arena koordinatı görür.
- Bütün başlıklar aynı fiziksel alana kalibre olduğu için, arena uzayı **tüm cihazlarda aynı fiziksel
  noktayı** gösterir — çakışan avatar / yanlış yerde görünen rakip sorununun çözümü budur.

### 3.4 Bağlantı yaşam döngüsü

```
İstemci açılır
  └─ adres zinciri (ROLDEN BAĞIMSIZ, AppBoot komut satırını her rolde okur):
       komut satırı --server-ip [--server-port] > elle girilen IP (PlayerPrefs)
       > beacon (5 sn dinle) > StreamingAssets/arena.json
  └─ VR (player): pratikte beacon — VR build'ine argüman geçilmez
                 → bulunan adrese OTOMATİK bağlanır; oyuncuya sorulmaz
                 → hiç bulunamazsa 8 sn sonra "sağ kumandada A×2" ipucu (gizli IP paneli)
  └─ admin: adres launcher'ın geçtiği `--server-ip`'ten gelir; argüman yoksa bağlanmaz,
            sebebini ekranda yazar (editörde adres/rol dev penceresinden gelir → §6.2)
            → admin de Lobby sahnesinden bağlanır (ayrı dashboard sahnesi YOK)
  └─ ws://ip:47821/ws  →  hello{role, deviceId, scenes}  →  welcome{playerId, udpToken, match}
  └─ UDP kaydı: 0x00 UdpHello (ack gelene dek 1 sn'de bir tekrar)
  └─ status kalp atışı 5 sn  +  (player ise) poz döngüsü 20 Hz
  └─ welcome.match.phase ≠ Lobby ise → GEÇ KATILIM: maç sahnesine yetiş (admin dahil)
  └─ maç başlarken load_match → oyuncular + ADMİNLER aynı sahneyi yükler
       (admin'de yourTeam="" / spawnSlot=-1; admin set_ready GÖNDERMEZ)

Kopma → 1 → 2 → 5 sn backoff ile keşiften itibaren baştan (sonsuz, otomatik)
      → bağlantısızlık ~3 sn sürerse ConnectionOverlay hata ekranı (§4)
Sunucu → 15 sn status gelmezse çevrimdışı işaretle + bağlantıyı kapat + lobby_state yayınla
```

Elle girilen IP her zaman beacon'ı ezer ve `PlayerPrefs`'e kalıcı yazılır — işletmede beacon'ı
kesen/izole eden AP'lerde kurtarıcı budur. Açıkça verilen komut satırı adresi ise zincirin en
üstündedir: `LobbyController` onu `_manualEntry` sayar, böylece gelen bir beacon adresi EZMEZ.

### 3.5 Poz akışı (20 Hz)

```
PlayerPoseTracker (kafa + 2 el, dünya→arena)
        │ IPoseSource
        ▼
UdpStateChannel ──0x01 PoseUpdate (92 B)──► StateHost ──0x02 Snapshot (≤1382 B)──► TÜM kayıtlı endpoint'ler
                                                                                          │ (admin dahil)
                                                                     RemotePlayerRegistry ─┘
                                                                          │ 100 ms tamponla interpolasyon
                                                          RemoteAvatar / TacticalView
```

- Kendi pozunu snapshot'tan **çizmezsin** (yerelden çizersin) — gecikme sıfır kalır.
- Uzak oyuncular `INTERP_DELAY_MS = 100` tamponuyla yumuşatılır; paket kaybı tolere edilir
  (son gelen kazanır, eski `seq` atılır).
- **Eşzamanlı oyuncu/admin sınırı YOKTUR** (kota ileride lisanslamayla gelecek). Tek tavan
  `PLAYER_ID_MAX = 255` ve o bir ürün kotası değil, `playerId`'nin UDP'de `u8` olmasıdır.
  16'dan fazla pozlu oyuncu olduğunda snapshot MTU'ya sığan parçalara bölünür
  (`SNAPSHOT_MAX_ENTRIES_PER_PACKET = 16` → 1382 B); **istemcide birleştirme yoktur ve
  gerekmez** — her paket taşıdığı girdileri bağımsız uygular, düşürme kararı zaman aşımıdır.
- Bir `playerId` ~1.5 sn snapshot'larda görünmezse avatarı kaldırılır (sunucunun 15 sn'lik
  offline eşiğini beklemez).

### 3.6 Vuruş hattı

```
Weapon.Fire() / balta savurma / ok isabeti / bomba patlaması
   │
   ├─ shot_fired  → sunucu DOĞRULAMAZ, sadece relay eder; istemcide `RemoteShotFx`
   │                tüketir (uzak namlu alevi + konumsal atış sesi, weaponId profiliyle)
   └─ hit_report  → sunucu 5 tutarlılık kontrolü yapar:
        faz Live? · atıcı canlı? · hedef canlı? (çift ölüm olmasın) ·
        hedef başkası + takımlar farklı? (dost ateşi YOK) · damage sonlu ve pozitif mi?
                            ↓ geçerse
        hp -= damage → health_update (herkese) → hp ≤ 0 ise kill_event + IGameMode.OnKill + respawn
```

**Hasarı istemci hesaplar, sunucu aynen uygular.** Sunucuda silah tablosu, `weaponId` beyaz listesi
ve atış hızı denetimi **yoktur** — ürün gözetimli özel alanda (işletme, turnuva) çalıştığı için
hile koruması bilinçli olarak eklenmez; v1'deki denetimler meşru saçma/patlama/yaylım vuruşlarını
düşürdüğü için kaldırıldı. Yukarıdaki beş kontrol hile denetimi değil **durum tutarlılığı**dır.

Pratik sonucu: **yeni bir hasar kaynağı eklemek sıfır sunucu işidir.** Bomba = etkilenen her hedef
için bir `hit_report` (mesafeye göre düşen hasarı istemci hesaplar); yay çekiş gücü, düşme/tuzak
hasarı da aynı şekilde `damage` alanına yazılır. **Kafa vuruşu çarpanı bugün uygulanır:** `Weapon`,
isabet `RemoteHitBox.IsHead` ise hasarı `WeaponDefinition.headshotMultiplier` (vars. 4×) ile çarpıp
öyle bildirir. `weaponId` yalnız kill feed etiketidir, doğrulanmaz.

### 3.7 Free-roam canlanma (respawn) — ürünün en özel kuralı

Fiziksel oyuncu **ışınlanamaz**. Bu yüzden respawn bir **konum değil, DURUM değişimidir**:

```
ölüm → respawn{spawnSlot, delaySeconds:5} → ölüm ekranı ("tabanına dön"), ateş yok, avatar yarı saydam
     → süre dolar VE oyuncu kendi BaseZone'una FİZİKEN girer → revive_request (~1 sn'de bir tekrar)
     → sunucu doğrular → health_update{hp:100} → canlı
     → talep 20 sn (REVIVE_GRACE) gelmezse sunucu ZORLA canlandırır (maç kilitlenmesin)
```

⚠️ **Kod kuralı:** hiçbir bileşen rig'i/kamerayı taşımaz. `SpawnPoint` yalnız "hangi tabana dön"
göstergesidir; slot çözümü istemcide sahnedeki marker'lardan yapılır, sunucu sahne geometrisini
bilmez (yalnız `maps.json`'dan `spawnSlotsPerTeam` okuyup slotu geçerli aralığa sarar).

### 3.8 Maç faz makinesi (sunucuda)

```
Lobby ──start_match──► Loading ──herkes set_ready | 20 sn timeout──► Countdown(5 sn) ──► Live
  ▲                                                                                        │
  └──────── return_to_lobby ◄──── End (10 sn) ◄──── süre bitti | skor limiti ◄──────────────┘
```

`start_match` doğrulaması (sırayla): mod kayıtlı mı → sahne adı boş değil → sahne `maps.json`'da
var ve modu destekliyor mu (**tablo boşsa bu adım atlanır**) → en az 1 çevrimiçi oyuncu →
sahne TÜM oyuncuların `hello.scenes` listesinde. Geçerse takımlar dengelenir ve herkese **kişisel**
`load_match` (kendi `yourTeam` + `spawnSlot`'u) gider.

---

## 4. Bileşen sözlüğü — kim ne yapıyor

### İstemci: `VortexArena.Net` (ağ, oyun bilmez)

| Sınıf | Görevi |
|---|---|
| `ArenaClient` | Kalıcı tekil; WS bağlantısı (arka plan Task + `ConcurrentQueue` → ana thread köprüsü), hello/welcome, status kalp atışı, otomatik reconnect. **Tüm mesaj gönderimi buradan.** Teşhis için `ConnectAttempts` (son başarılı bağlantıdan beri kaçıncı deneme; bağlanınca 0) + `LastError` (son bağlanma hatası) — `ConnectionOverlay` bunları gösterir. `Disconnect()` otomatik yeniden denemeyi **durdurur** (dönüş yalnız açık `Connect` ile) |
| `ServerDiscovery` | Beacon dinleme (Android'de MulticastLock), elle girilen adresin `PlayerPrefs`'e yazılması, `arena.json` fallback |
| `UdpStateChannel` | UDP kaydı (`0x00`), 20 Hz poz gönderimi, snapshot alımı |
| `RemotePlayerRegistry` | Snapshot → oyuncu başına halka tampon → `GetInterpolatedPose`, `IsAlive`, `OnRemoteJoined/Left` |
| `NetEvents` | **Statik olay merkezi** — sunucu mesajları buradan ana thread'de yayınlanır. `InjectLoadMatch` yalnız editörde derlenir (`#if UNITY_EDITOR`): dev penceresinin sentetik `load_match`'i için test kancası — **protokol mesajı değildir** |
| `IPoseSource` | 20 Hz döngüye arena-uzayı pozu sağlayan arayüz |
| `NetIdentity` / `NetSpawnCatalog` | Sahne objesi kimliği (`sceneId`) ve id→prefab kataloğu — **dinamik obje senkronu altyapısı** (v1'de oyuncu senkronu playerId ile gider) |

### İstemci: `VortexArena.App` (akış ve köprüler)

| Sınıf | Görevi |
|---|---|
| `AppBoot` | Rol çözümü: Android → player; masaüstü → `--role` > `VORTEX_ROLE` > admin. **Sahne her rolde `Lobby`'dir** (Faz 6: admin'in ayrı kabuğu yok). **Adres çözümü:** `--server-ip` / `--server-port`'u **her rolde** okuyup `AppSession`'a yazar (player'da keşif zincirinin en üstü; admin'de tek kaynak — yoksa uyarı loglar). `AppSession.RoleResolved` doluysa hiçbir şey yazmaz → editörde `DevSession` kazanır. **Inspector'da rol/IP override alanı YOKTUR** (kaldırıldı: sahneyi kirletiyordu) |
| `SceneRouter` | `load_match` / `return_to_lobby` / geç katılım → sahne yükleme. **Rolden bağımsız** (Faz 6: admin de oyuncuların sahnesine gider); rol yalnız TEK yerde ayrışır — `set_ready` sadece player'dan gider (admin "hazır" görünmemeli) |
| `LobbyController` | VR lobi: roster, ready/takım + otomatik bağlanma; **gizli IP paneli** (varsayılan kapalı, sağ kumandada `OVRInput.Button.One`×2 ile açılır — beacon'ı kesen ağlar için kurtarma yolu). Admin de bu sahneden bağlanır (`Connect(..., AppSession.Role)`); world-space paneli admin'de `AdminSpectator` gizler |
| `UiKit` | **Prosedürel arayüz kiti** (statik): palet, yuvarlatılmış/halka sprite önbellekleri, öge fabrikaları (`Panel`/`Text`/`Button`/`Bar`/`WorldCanvas`), yerleşim yardımcıları (`Block`/`Corner`/`Stretch`) ve **EventSystem garantisi**. `ConnectionOverlay` + admin HUD tek görsel dili buradan alır. ⚠️ Layout Group KULLANILMAZ (sabit anchor = öngörülebilir yerleşim) |
| `ConnectionOverlay` | **Bağlantı hata ekranı** — kalıcı tekil, `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` ile kendini önyükler, tüm UI prosedürel (prefab/Resources/sahne bağı YOK → yeni arena eklerken unutulacak adım yok). ~3 sn **grace** (anlık kopmada yanıp sönmesin; açılışı da maç ortasındaki kopmayı da kapsar). İki durum: adres biliniyor → "SUNUCUYA BAĞLANILAMIYOR" + adres + `N sn · M. deneme` + son hata; adres yok → "SUNUCU BULUNAMADI". Rol'e göre ipucu (player: A×2 / admin: launcher). Masaüstü: screen-space + scrim + **"Yeniden Bağlan"** (adres yoksa devre dışı; `Disconnect()` otomatik denemeyi durdurduğu için tek kurtarma yolu). VR: world-space kart + `HudFollow`, scrim YOK. ⚠️ `ArenaBoundary.IsOutOfBounds` iken **tamamen gizlenir** — alan-dışı uyarısı her zaman baskın |
| `DevSession` | **Yalnız editör** (dosyanın tamamı `#if UNITY_EDITOR`): dev penceresinin `EditorPrefs` seçimini Play'e uygular. (a) `BeforeSceneLoad` → rol + adres `AppSession`'a, `RoleResolved = true`; (b) `AfterSceneLoad` → "Açık sahneden" kipinde ve aktif sahne bir ARENA sahnesiyse, bir kare sonra **sunucuya bağlanır** ve (player rolünde) **sentetik `load_match`** yayınlar (`NetEvents.InjectLoadMatch`) → takım/slot/mod gerçek kod yolundan geçer. **Bağlanmayı neden o üstleniyor:** `Connect` normalde kabuk controller'larından gelir, arena sahnelerinde onlar YOKTUR — bağlanmazsa can/skor/faz gelmez ve `CanFire` hiç açılmaz. Sunucuda maç koşuyorsa `welcome.match` geç-katılım senkronu devreye girip **gerçek takım ataması sentetiği ezer**. Pencerede "Dev enjeksiyonu" kapatılırsa üretim yolu birebir koşar |
| `AppSession` | Oturum: rol + sunucu adresi (`ServerIp`/`ServerPort`/`HasServerEndpoint`) — `AppBoot` yazar, controller'lar okur |
| `PlayerPoseTracker` | BB rig anchor'larını bulur, kalibrasyonu bekler, **dünya→arena** çevirip `IPoseSource` olarak kaydolur |
| `RemotePlayerSpawner` | Katılan/ayrılan uzak oyuncular için `RemoteAvatar` yaratır/yok eder |
| `TacticalView` | Üstten 2B nokta haritası (snapshot'lardan çizilir). Faz 6'dan beri admin HUD'ının **sağ alt mini haritası**; `Initialize(RectTransform)` ile prosedürel kurulur, kuş bakışı kipinde gizlenir |
| `ModeHudSpawner` | Aktif modun HUD prefabını katalogdan örnekler — **App, mod assembly'lerini referanslamaz** (prefab yalnız `GameObject` olarak taşınır) |
| `IdentifyOverlay` | Admin `identify` yollayınca o başlıkta büyük kimlik overlay'i |

### İstemci: `VortexArena.App.Admin` (admin gözlemci — masaüstü)

Rol `admin` değilse **hiçbiri çalışmaz** (`AdminSpectator` kendini yok eder); Quest build'inde ölü koddur.

> **Çoklu admin desteklenir ve sınırsızdır** (aynı PC'de birden çok pencere dahil — admin `deviceId`'si
> oturumluktur, §5.2 protokol). Hepsi **eş yetkilidir**; ayrım şudur: **operasyonel durum ortaktır**
> (mod/harita seçimi sunucuda yaşar, `set_selection`/`admin_state` ile senkronlanır → `AdminSelection`),
> **görünüm tercihleri yereldir** (kamera kipi, seçili oyuncu, halkalar, saydamlıklar → `AdminSession`,
> `PlayerPrefs`). Her admin eylemi diğerlerinin HUD'ında "kim ne yaptı" satırı olarak belirir.

| Sınıf | Görevi |
|---|---|
| `AdminSpectator` | Gözlemcinin kökü: kendini önyükler (`AfterSceneLoad` + `DontDestroyOnLoad`), rol çözülünce etkinleşir, kamerayı/HUD'ı/işaretçileri yaratır ve **her `sceneLoaded`'da sahneyi devralır**: BB Camera Rig kökünü kapatır (üç kamerası da `MainCamera` etiketli → `Camera.main` belirsiz kalırdı), `ArenaCalibrator` + `BaseZone`'ları kapatır, **`ArenaBoundary`'yi KAPATMADAN** `SetSpectatorMode(true)` ile susturur, world-space canvas'ları gizler, EventSystem'i devralır. Kısayollar: `1/2/3` kip · `Tab` sonraki oyuncu · `F` POV · `P`/`I` panel · `Esc` kapat |
| `AdminSpectatorCamera` | Üç kip: **POV** (seçili oyuncunun baş pozu; poz yoksa son konumda kalır) · **Serbest** (WASD + Q/E + **sağ tuş basılı** fare bakışı, Shift ×3, tekerlek hız; imleç KİLİTLENMEZ → HUD tıklanabilir kalır) · **Kuş bakışı** (ortografik, arena yaw'ına hizalı; kadraj `ArenaBoundary` → `MapDefinition.Size` → 10×10 varsayılanı, tekerlek zoom). Kip değişiminde `AdminSpectator.RefreshRoof()` çağrılır → sahnede `ArenaRoof` varsa çatı kuş bakışında kalkar |
| `AdminPlayerMarkers` | Oyuncu başına **zeminde halka + altında ad etiketi** (kuş bakışı isteği). Halka baş pozunun x/z'sinden arena zeminine indirilir; etiket kameraya döner ve kameranın yukarı vektörünün tersine kaydırılarak her kipte "dairenin altında" okunur. `RemoteAvatar`'a dokunmaz |
| `AdminHud` | **Kalıcı** ekran-uzayı HUD'ı (`sortingOrder = 4000`; hata ekranı 5000'de üstte kalır): üst orta takım skorları + **ortada istatistik chip'i** (faz/süre de gösterir), sol üst tercihler, sağ üst mod·harita + bağlantı/poz yaşı + **çoklu admin satırı** (kaç admin bağlı · son admin eylemi; tek admin varken boş kalır), yanlarda takım kolonları (**FFA'da tek kolon** — karar veriden gelir), alt orta kamera şeridi + seçili oyuncu, alt sağ ölüm akışı + mini harita |
| `AdminPlayerRow` | Oyuncu satırı: takım şeridi, ad + `#id`, HP barı, `K/D · batarya · durum`, eylemler POV/TAKIM/KİMLİK/**AT (iki adımlı onay)**. Satıra tıklamak seçer (MonoBehaviour değil, havuzlanan görünüm nesnesi) |
| `AdminPreferencesPanel` | Eski dashboard'un işi. **MAÇ bölümü ORTAK** (başlıkta yazar): mod/harita seçicileri yerel alana değil `set_selection` ile sunucudaki ortak seçime yazar → tüm adminlerde aynı anda değişir; tıklamada yerel imleç de iyimser ilerletilir, sunucudan gelen değer son sözü söyler. **Harita değişince (faz Lobby ise) o arena YEREL olarak hemen açılır** — önizleme; sunucuya maç komutu gitmez, oyuncular etkilenmez (`SceneRouter.LoadPreview`). Bu bileşen panel **kapalıyken de etkin** olduğu için başka bir operatörün harita değişikliği panel açılmadan da önizlemeye yansır. **GÖRÜNÜM bölümü YEREL** (halkalar, ad etiketleri, kamera hızı, duvar saydamlığı, **çatı**, mini harita) + bağlantı (yeniden bağlan/kes, bağlı admin sayısı). Yarı saydam, **scrim YOK**. Dropdown/slider yerine `[<] değer [>]` döngüleyici |
| `AdminStatsPanel` | Takım toplamları + oyuncu tablosu (ad/takım/K/D/K-D/HP/batarya/durum/sahne) + maç bilgisi. Tablo **kolon kolon** çizilir (TMP fontu eşit genişlikli değil, boşlukla hizalama kayar). Protokolde olmayan metrik (hasar/isabet/ping) **gösterilmez** |
| `AdminRoster` | Admin arayüzünün veri katmanı: `lobby_state` (otoriter tam görüntü + `kills/deaths/hp/alive`) + `health_update`/`kill_event` (anlık) + `match_state`/`countdown`/`match_end` birleşimi; takım listeleri, FFA kararı, ölüm akışı, snapshot yaşı. ⚠️ `respawn` admin'e GELMEZ (yalnız ölen oyuncuya gider) → geri sayım `kill_event` + `RESPAWN_DELAY` ile yerel hesaplanır |
| `AdminSession` | **YEREL** seçimler (kamera kipi, seçili oyuncu, açık panel) + görünüm tercihleri (`PlayerPrefs`'te kalıcı, admin PC'sine özel — halkalar, ad etiketleri, kamera hızı, duvar saydamlığı, **çatı kipi**, mini harita). Tek doğruluk noktası; `Changed` ile HUD/kamera/işaretçiler senkron kalır. `RoofAlphaNow()` tercih + kamera kipinden çatı alfasını türetir |
| `AdminSelection` | **ORTAK** durumun aynası (`admin_state`, §5.3): mod/harita seçimi, çevrimiçi admin sayısı, son admin eyleminin duyurusu. Statik durum + statik `Changed` (bileşen kurulum sırası dinleyiciyi ilgilendirmesin); bileşenin kendisi yalnız ağ olayı pompasıdır. Otorite sunucudadır — buraya yerelden yazılmaz |
| `AdminCommands` | Admin komutlarının tek çıkış kapısı (§5.2) + son işlemin durum metni. "Gönderildi" der, "oldu" demez — kabul/ret sunucuda. `SetSelection` ortak seçimi değiştirir (maçı başlatmaz) |
| `AdminContent` | `Resources.Load<GameCatalog>("GameCatalog")` (asset: `_Shared/Data/Resources/`) → mod/harita listeleri. Prosedürel arayüzün `[SerializeField]`'i olamaz, tek meşru yol bu |

### Editör: `VortexArena.App.Editor` (dev araç seti — yalnız Editor)

| Sınıf | Görevi |
|---|---|
| `DevWindow` | `Tools > VortexArena > Dev` penceresi: rol · hedef · Play başlangıcı · sentetik maç parametreleri (mod/takım/slot/raund sn/skor limiti) + ortam düğmeleri + canlı durum satırı. Mod listesi `GameCatalog`'dan okunur. **Modal dialog kullanmaz** (Unity CLI doğrulamasını kilitliyor); geri bildirim konsol + `HelpBox` |
| `DevTargets` | Repo kökündeki `dev-targets.json` okuyucusu (`defaultTarget`/`defaultRole` + adlandırılmış hedefler). Dosya yok/bozuksa bellekte `Local` + `Kesif (beacon)` varsayılanına düşer ve **dosyayı OLUŞTURMAZ** (commit kirletmemek için). Bir hedefin `ip`'si boşsa adres yazılmaz → keşif zinciri devralır |
| `DevProcesses` | **Yalnız PoseBot** süreçlerini başlatır/durdurur, `dotnet build -c Release` tetikler. **Sunucuya dokunmaz** — sunucu elle yönetilir (§6.1), editör onu ne başlatır ne öldürür. PID'ler `SessionState`'te (domain reload'ı aşar) **ad doğrulamasıyla** tutulur; "Sahipsiz botları temizle" ad bazlı süpürme yapar (yalnız `VortexArena.PoseBot`). Exe arama sırası: **PoseBot yalnız `bin\{Release,Debug}\net10.0\`** (dev/test aracı, `deploy/`'a publish EDİLMEZ) |
| `DevBootstrap` | Editör kancaları: "Boot'tan" kipinde `EditorSceneManager.playModeStartScene`'i Boot sahnesine ayarlar (sahne **Build Settings'ten** bulunur, sabit yol gömülmez); Play çıkışında **yalnız botları** öldürür (**sunucu kasıtlı olarak yaşar** — üretimde de ayrı makinede sürekli açık); editör kapanışında hepsini öldürür; `Ctrl+Alt+R` kısayolunu kurar (rol player↔admin) |

### İstemci: `VortexArena.Core` (oyun kodu)

`ArenaBoundary` (arena origin + sınır), `ArenaCalibrator` (2 nokta + OVRSpatialAnchor kalıcılığı),
`ArenaSpace` (dünya↔arena dönüşümü), `BaseZone` (taban bölgesi — canlanma kapısı), `SpawnPoint`
(takım + slot marker'ı), `MapDefinition` / `ModeDefinition` / `GameCatalog` (içerik SO'ları),
`Weapon` (ISDK ile tutulan hitscan tüfek; tetik **silahı tutan elin** kumandasından okunur — çift
silahta tetikler bağımsız; şarjör+yedek şarjör durumu taşır, boş şarjörde **otomatik reload YOK**
(kuru tetik sesi), reload **bel-altı jestiyle** başlar; `reserveMode=DiscardMagazine`'de erken
reload'da şarjörde kalan mermi **yanar** (ürün kuralı; `PoolRounds` = CS2 havuzu SO'dan seçilebilir);
spread atış sürdükçe açılır (bloom) ve boşta toparlar; yerel canlanmada tutulan silah tam dolar) +
`WeaponDefinition` (SO — hasar/HS çarpanı/RPM/şarjör/reload/spread/recoil/ses profili; **tek denge
kaynağı**, sunucuya export edilmez) + `WeaponAudio` (Meta XR spatializer'lı namlu AudioSource:
ateş/şarjör çıkar-tak/kuru tetik/alma) + `WeaponAnimator` (Animator'sız kod-güdümlü parça
animasyonu: atışta bolt tepmesi, reload'da `*_Mag` child'ı çıkar-takılır; şarjör seslerini de bu
zaman çizgisi çalar — görüntü/ses tek kaynaktan) + `WeaponReloadGesture` (silah bel hizasının
altına inince `TryStartReload`; kavradıktan sonra bir kez bel üstüne çıkmadan devreye girmez —
yerden alırken yanlış tetiklemeyi önler) + `WeaponAmmoDisplay` (silah üstü TMP cephane etiketi,
olay-güdümlü) + `WeaponCatalog` (SO, `_Shared/Data/Resources/` — `weaponId`→tanım araması;
`Resources.Load` ile okunduğu için klasöründen çıkarılmaz) + `RemoteShotFx` (kendini önyükler,
sahne kurulumu istemez; `shot_fired`'ı tüketip uzak oyuncunun namlu alevi + konumsal atış sesini
havuzlu çalar), `Health` (sunucudan set edilir), `PlayerCombatState`
(yerel oyuncunun takım/can/ateş yetkisi/canlanma akışı), `RemoteAvatar` + `RemoteHitBox`
(uzak oyuncu gövdesi ve isabet kutusu),
`ProximityWarning` (`Core/Player` — free-roam çarpışma önleme: `RemotePlayerRegistry` pozlarını
yerel HMD ile karşılaştırır; 1.2 m'de uzak oyuncunun konumunda **duvar arkasından da görünen**
halka (`VortexArena/ProximityHalo`, ZTest Always), 0.8 m'de tehlikenin geldiği **taraftaki**
kumandada haptik. Ölü oyuncular ELENMEZ — respawn durum değişimi olduğu için ölünün bedeni sahada
durmaya devam eder, çarpışma riski aynıdır. **Henüz hiçbir sahnede bağlı değil**: bileşen elle
eklenir, `head` ve `haloMaterial` (`_Shared/FX/M_ProximityHalo`) alanları Inspector'dan verilir),
`LocalAvatarHeadHider` (`Core/Player` — birinci şahıs gövde avatarında (Movement SDK retarget
karakteri) kafa kemiğini her kare sıfıra yakın ölçekleyip gizler: kamera kafanın tam içinde
durduğu için mesh'in içi görünmesin; yüksek execution order ile retargeter'dan SONRA yazar.
Şu an yalnız IceWorld'deki deneysel `StylizedCharacter`'a bağlı — gövde takibi ürünleşirse
rig kalıbına taşınacak),
`WeatherVolumeFollow` (`Core/FX` — ambiyans parçacık hacmini yerel kameranın üstünde tutar; bağlı
sistemler **World** simülasyon uzayında olmalı, `Start` sapmayı uyarır. Yalnız kendi transform'unu
taşır, rig'e dokunmaz), `WeatherWindDriver` (`Core/FX` — kök objeye takılır, altındaki tüm
sistemlerin `Velocity over Lifetime` XZ'sini ve Noise şiddetini tek Perlin kanalından salındırır:
rüzgar şiddeti + yönü + türbülans birlikte nefes alır. Temel değerler `Awake`'te alınır,
katmanların göreli hız farkı korunur).

**`ArenaRoof`** (çatılı arenalar için, **isteğe bağlı**): çatı hiyerarşisinin köküne konur
(`GameObject > VortexArena > Arena Roof`), altındaki **tüm** Renderer'lar çatı sayılır ve
`ArenaRoof` katmanı (user layer 8) damgalanır — hangi geometrinin gizleneceği sahne görünümündeki
Layers süzgecinden görülsün. Katman yalnız ayıklama içindir; davranış Renderer listesinden gelir,
damga unutulsa da çalışır. Gizleme `_BaseColor` alfasıyla (duvar saydamlığıyla aynı desen);
tam gizlemede Renderer **kapatılmaz**, `ShadowsOnly`'ye alınır → çatı çizilmez ama gölgesini atar
(kapatılsaydı iç mekân aydınlanıp kuş bakışı okunmaz olurdu). Son uygulanan alfa statik tutulur,
yeni sahnedeki çatı `OnEnable`'da devralır → kuş bakışındayken arena değiştirilince çatı bir kare
bile görünmez. Oyuncu tarafında etkisi YOKTUR — yalnız `AdminSpectator.RefreshRoof()` tetikler.
**Yapımcıya verilecek tek parça teknik not: [`Cati-Gizleme.md`](Cati-Gizleme.md).**

### Sunucu: `Server/VortexArena.Server.Core`

| Sınıf | Görevi |
|---|---|
| `ControlHost` | Kestrel WebSocket host (`/ws`), bağlantı başına `ClientConnection` |
| `BeaconService` | 2 sn'de bir broadcast |
| `StateHost` | UDP kaydı, poz alımı, 20 Hz snapshot yayını (16 girdiden fazlası MTU'ya sığan parçalara bölünür) |
| `PlayerRegistry` | Oyuncu listesi, `playerId` tahsisi (1..255), `devices.json` ile kalıcı adlandırma, çevrimiçi/çevrimdışı. **Rol başına kalıcılık farkı:** oyuncu kaydı kopunca Offline işaretlenir ama DURUR (deviceId kalıcı); **admin kaydı tümüyle SİLİNİR** (deviceId oturumluk — yoksa her açıp kapatma roster'da hayalet satır ve tükenen playerId bırakırdı) ve admin adı diske yazılmaz. Aynı PC'de iki admin varsa ad " (2)" ile ayrıştırılır |
| `LobbyService` | Roster yayını (`lobby_state`), ready/takım/kick + **adminler arası ortak durumun sahibi**: mod/harita seçimi burada yaşar, `set_selection` ile değişir, `admin_state` ile yalnız adminlere yayılır. Her admin komutu "kim ne yaptı" duyurusu üretir |
| `MatchDirector` | **Faz makinesi (10 Hz tick), vuruş hattı, can/skor, canlanma, zorla canlandırma** |
| `MapTable` | `maps.json` (Unity export'undan) — sunucunun okuduğu tek içerik tablosu |
| `Modes/IGameMode` + `TdmMode` | Mod kuralları: skor, kazanma koşulu, tur süresi |

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
`OnMatchEnd`, `OnReturnToLobby`, `OnShotFired`, `OnIdentify`, `OnKicked`.

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

Sunucu **her zaman elle** başlatılır — admin uygulamasının launcher ekranı sunucuyu başlatmaz,
yalnız çalışan bir sunucunun IP:port'una bağlanır.

Bu **geliştirirken de geçerlidir**: dev penceresinde (`Tools > VortexArena > Dev`) sunucu başlat/
durdur düğmesi **yoktur** — editör sunucuyu ne başlatır ne öldürür (elle başlatılmış bir sunucunun
Play çıkışında ya da editör kapanışında ölme riski kalmasın diye). Penceredeki
"Derle (dotnet build)" yalnız çözümü derler; çalıştırmak yine elle:
`deploy\server\VortexArena.Server.App.exe` (ya da yukarıdaki `dotnet run`).

### 6.2 Quest olmadan test (loopback) — `Tools > VortexArena > Dev`

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
| **Başlangıç: Açık sahneden** | Arena sahnesine doğrudan Play. Bir kare sonra `DevSession` (a) **seçili hedefe bağlanır** — arena sahnesinde `LobbyController` olmadığı için bunu başka kimse yapmaz; bağlanmazsa can/skor/faz gelmez ve `CanFire` hiç açılmaz — ve (b) player rolünde **sentetik `load_match`** yayınlar → **takım / spawn slot / mod** gerçek kod yolundan (`PlayerCombatState`, `ModeHudSpawner`, `SceneRouter`) uygulanır. Aşağıdaki mod/takım/slot/raund sn/skor limiti alanları bu mesajı doldurur. Sunucuda maç koşuyorsa `welcome.match` geç-katılım senkronu **gerçek takım atamasıyla sentetiği ezer**. Hedef "keşif" kipindeyse (ip boş) bağlanılmaz ve sebebi loglanır — arena sahnesinde adres girecek arayüz yok |
| **Ortam düğmeleri** (yalnız test botları) | N Bot · N Bot + Admin · Botları Durdur · Sahipsiz botları temizle · Derle (dotnet build) + canlı durum satırı (bot süreç sayısı). **Sunucu düğmesi yok** — sunucu elle başlatılır/durdurulur (§6.1) |
| **"Dev enjeksiyonu" onayı** | Kapatılırsa üretim yolu **birebir** koşar (rol `AppBoot`'tan, adres keşif zincirinden, sentetik mesaj yok) — beacon keşfini editörde denemenin yolu |

Botları elle çalıştırmak hâlâ geçerlidir (pencere de aynı PoseBot'u başlatır, yalnız `dotnet run`
yerine derlenmiş exe ile — §7 tuzak 13):

```powershell
# 2 bot, yalnız poz senkronu
dotnet run --project Server/VortexArena.PoseBot -- 127.0.0.1 2
# 4 bot savaşarak + maçı başlatan admin + belirli harita/mod
dotnet run --project Server/VortexArena.PoseBot -- 127.0.0.1 4 --fight --admin --map Arena12x12 --mode tdm
```

Editor **player** rolündeyken ortamda admin kalmadığı için maçı başlatacak bir admin gerekir:
PoseBot'a `--admin` ver (pencerede **"N Bot + Admin"** düğmesi; açık sahne bir arena sahnesiyse
`--map <sahne>`, seçili moddan `--mode <modId>` de eklenir).
Play'den çıkışta **yalnız botlar** ölür; **sunucuya hiç dokunulmaz** (üretimde de ayrı makinede
sürekli açıktır). Editör kapanışında da yalnız botlar toplanır — sunucuyu sen başlattın, sen
kapatırsın.

> Botların `devices.json`'a yazdığı test girdilerini **commit'leme**.

### 6.3 Build ve dağıtım

Üç bileşenin her biri kendi script'iyle `deploy/` altına üretilir:

| Komut | Ne yapar | Çıktı |
|---|---|---|
| `scripts\deploy-admin-game.bat` | Unity batch-mode Windows build (`PlayerBuildTool.BuildWindowsAdmin`) | `deploy\admin\VortexArena.exe` |
| `scripts\deploy-server.bat` | `dotnet publish -r win-x64 --self-contained` + `config/` kopyası | `deploy\server\VortexArena.Server.App.exe` |
| `scripts\deploy-launcher.bat` | `flutter build windows --release` | `deploy\launcher\vortex_launcher.exe` |

- **Admin build'i canlı ilerleme basar.** `deploy-admin-game.bat` Unity'yi doğrudan değil
  `scripts\lib\watch-unity-build.ps1` üzerinden çalıştırır: izleyici `deploy\admin-build.log`'u
  akarken okur ve tek satırlık durum gösterir (aşama · Bee yüzdesi · o an çalışan araç · log
  boyutu · CPU). Batch-mode Unity konsola hiçbir şey yazmadığı için "takıldı mı ilerliyor mu"
  başka türlü görünmüyordu. Log ~3 dk büyümez ve CPU da harcanmazsa uyarı basılır; hata satırları
  (proje kilidi, `error CS…`) anında ekrana düşer. Post-mortem: aynı betik `-ReplayLog <log>` ile
  bitmiş bir log'un aşama haritasını çıkarır. Süre `deploy\admin-build.last`'a yazılır ve sonraki
  koşuda "~mm:ss" referansı olarak gösterilir. Ayrıntı: `scripts/README.md`.
- **Admin build'i editör AÇIKKEN alınamaz** — batch-mode Unity proje kilidine takılır. Script
  bunu **kontrol etmez** (bilinçli: editör kapatıldıktan sonra bile AI motoru gibi alt süreçlerin
  `Unity.exe`'si arka planda yaşıyor, `tasklist` kontrolü yanlış alarm veriyordu). Build
  ilerlemiyorsa Ctrl+C ile iptal edip süreçleri kapat (izleyici Unity'yi de kapatır). Önceki
  `deploy\admin-build.log` silinemezse script uyarır — o dosyayı hâlâ bir Unity süreci tutuyor
  demektir.
- **Launcher build'i Windows Developer Mode ister** (Flutter plugin symlink'leri):
  `start ms-settings:developers`; script build'e girmeden kayıt defterinden kontrol eder.
- **VR (player):** Android build → `Builds/*.apk` → `install_game.bat` (adb) ile başlıklara kurulur
  (bu akış değişmedi; deploy script'i yok).
- Boot sahnesi build listesinde **index 0** olmalı; tüm arena sahneleri listede olmalı
  (sihirbaz bunu otomatik yapar).

**Operatör akışı (işletmede):** sunucuyu elle başlat → launcher'ı aç → Ayarlar'dan admin exe'yi
bir kez seç → Sunucu IP'sini yaz → **Yönetimi Başlat**. Oyun `--server-ip` ile açılır, IP sormaz,
doğrudan dashboard'a düşer. Ayrıntı: `deploy/README.md`.

### 6.4 İçerik eklemek (özet — tam reçeteler `CLAUDE.md`'de)

| İstek | Yol |
|---|---|
| **Yeni arena** | `Tools > VortexArena > Create Arena From Template` → arenaId, sahne adı, boyut, slot, hedef (Standard/Venue). Sihirbaz klasörleri + sahne kopyasını üretir, duvar/zemin/taban/spawn'ları ölçekler, `MapDefinition` yazar, `GameCatalog` + uyumlu `ModeDefinition` + Build Settings'e ekler. Sanat rötuşu elde. **Sonra `Export Server Config`.** |
| **Yeni silah** | `WeaponKitBuilder` tablosuna satır ekle (istatistik + ses profili + pack prefabı) → `Tools > VortexArena > Build Weapon Prefabs` → `WD_*.asset` + `WPN_*.prefab` üretir, `WeaponCatalog`'u tazeler → gerekiyorsa `ModeDefinition.loadout` + sahneye yerleştir. **Export GEREKMEZ** (sunucuda silah tablosu yok). Şablon (eski AK47_Red) silindi: sıfırdan farklı gövde için mevcut bir `WPN_*` prefabını kopyalayıp `Model` altındaki pack prefabını ve `definition`'ı değiştir, sonra *…(Yalnız Kataloğu Tazele)* çalıştır |
| **Yeni mod** | Unity: `Assets/Modes/<Ad>/Scripts/VortexArena.Modes.<Ad>.asmdef` (refs: Core, Net, Protocol) + Sunucu: `Modes/<Ad>Mode.cs : IGameMode` → `MatchDirector` ctor'unda `Register(new <Ad>Mode())` + protokol dokümanına `modId` |
| **Elle modellenmiş sahneyi arenaya çevirmek** | Aşağıdaki 6 adım (IceWorld böyle bağlandı) |

**Elle modellenmiş bir sahneyi ağa bağlama** (sihirbaz kullanılmadıysa — ör. `IceWorld`):

1. Sahneyi arena kutusuna taşı: `Assets/Arenas/Standard/<Ad>/Scenes/<Ad>.unity` (+ `Data`, `Prefabs`,
   arenaya özel sanat için `Art/`). **Sahne adı = katalog anahtarı** — sonradan değiştirme.
2. Arena çerçevesini kur: arena merkezinde, duvarlara hizalı bir objeye **`ArenaBoundary`**
   (halfExtentX/Z = iç ölçünün yarısı, `wallRenderers` = duvarlar, `head` = `CenterEyeAnchor`,
   `fadeRenderer`/`warningText` = rig altındaki `OutOfBoundsFade`/`BoundaryWarningText`).
   Bu transform arena origin'idir: **tüm ağ pozları buna göre çevrilir.**
3. Taban ve spawn'lar: iki `BaseZone` (Red/Blue, karşı kenarlarda) + takım başına `SpawnPoint`
   (`slot` 0..n-1). Canlanma bu marker'lardan çözülür — rig ASLA taşınmaz.
4. Ağ objeleri: `CalibrationManager` (`ArenaCalibrator` + iki anchor marker), `PoseSync`
   (`PlayerPoseTracker` + `RemotePlayerSpawner`), `[ModeHud]` (`ModeHudSpawner`), BB Camera Rig.
   En kolayı mevcut bir arenadan kopyalamak; **kopyaladıktan sonra sahneler-arası referansları
   (ör. `BaseZone.head`) yeni sahnenin `CenterEyeAnchor`'ına yeniden bağla** — Unity kopuk
   sahneler-arası referansı sessizce null yapar.
5. `MapDefinition` asset'i (`Data/`): sceneName + boyut + `spawnSlotsPerTeam` + desteklenen modlar →
   `GameCatalog.maps` + ilgili `ModeDefinition.maps` + **Build Settings**.
6. **`Tools > VortexArena > Export Server Config`** (maps.json) ve `Server/VortexArena.PoseBot`
   içindeki `BuildScenes` listesine sahne adını ekle.
7. **Arenanın çatısı/tavanı varsa** (isteğe bağlı, açık tavanlı arenalarda atlanır): çatı
   hiyerarşisinin kökünü seç → `GameObject > VortexArena > Arena Roof`. Bileşen eklenir ve
   altındaki tüm Renderer'lara `ArenaRoof` katmanı damgalanır → admin kuş bakışına geçince çatı
   kalkar, gölgesi kalır. Sonradan mesh eklersen bileşene sağ tık → *Çatı katmanını uygula*.
   **Tam not (davranış, tuzaklar, test, sorun giderme): [`Cati-Gizleme.md`](Cati-Gizleme.md).**

### 6.5 `Tools > VortexArena > Export Server Config` — ne zaman?

**Harita (`MapDefinition`) SO'su ekledin/değiştirdin mi → çalıştır.** Menü, `MapDefinition`
SO'larından `Server/config/maps.json` üretir; çıktı deterministiktir (alfabetik, LF, UTF-8
BOM'suz) → git diff temiz kalır.

**Silah için GEREKMEZ:** sunucu silah tablosu tutmaz (§3.6), hasarı istemci bildirir. Yeni silah
eklerken export çalıştırmaya gerek yoktur.

Unutursan ne olur: bilinmeyen `sceneName` → **`start_match` reddedilir** (maç başlamaz), sunucu
konsoluna tek satır sebep yazar.

---

## 7. Tuzaklar (pahalıya öğrenilmiş kurallar)

1. **`maps.json` ELLE DÜZENLENMEZ** — bir sonraki export ezer. Tek doğruluk kaynağı Unity
   SO'larıdır. (`server.json` elle, `devices.json` sunucu üretir; `weapons.json` artık yok.)
2. **Meta umbrella paketi (`com.meta.xr.sdk.all`) ASLA eklenmez** — Meta Project Setup Tool önerse
   bile. Çektiği `voice` paketi Android namespace çakışmasıyla build'i kırıyor. Bireysel paketler:
   core + interaction + interaction.ovr @203.0.0, audio @85.0.0 (spatializer, pinli).
3. **Rig'i/kamerayı asla taşıma** — free-roam'da oyuncu fiziksel; canlanma durum değişimidir.
4. **Sahne adı = katalog anahtarı.** `load_match` string gönderir; Build Settings'teki adla
   boşluk/typo dahil birebir eşleşmeli.
5. **Yeni arena eklerken `Server/VortexArena.PoseBot`'taki `BuildScenes` listesini güncelle** —
   sunucu sahneyi TÜM oyuncuların `hello.scenes` listesinde arar, eksik kalan bot maçı bloklar.
6. **`_Shared` köküne asmdef'siz gevşek script koyma** (Assembly-CSharp'a düşer, kimse göremez).
7. **Serialize edilen ikincil tipler kendi dosyasında** (`Team.cs` gibi) — gömülü tip
   "referenced script missing" üretebiliyor.
8. **Protokol dosyalarına `UnityEngine` sokma** — sunucu derlemesi kırılır (bilinçli bekçi).
9. **Doğrulamayı batch'le:** derleme/build/play testini işin sonunda tek geçişte yap.
10. **Kök `.gitignore`'a sabitlenmemiş Unity deseni ekleme.** Repo üç proje tipi barındırıyor
    (Unity + `Server/` .NET + `launcher/` Flutter) ve her birinin kendi `.gitignore`'u var.
    Windows'ta `core.ignorecase=true` olduğu için `*.app` deseni `Server/VortexArena.Server.App/`
    klasörünü, `*.sln`/`*.csproj` de sunucunun gerçek proje dosyalarını sessizce yutar — bunlar
    `/*.app`, `/*.sln`, `/*.csproj` diye köke sabitlenmiştir. Yeni desen eklendikten sonra
    `git ls-files -c -i --exclude-standard` (izlenen ama artık ignore'lu dosyalar) **boş dönmeli**.
11. **Dağıtım betiklerinde `call flutter …` KULLANMA** — `flutter.bat`'ın sonundaki
    `& exit_with_errorlevel.bat` zinciri çağıran batch bağlamını da sonlandırıyor: betik hiçbir
    şey yazmadan ölür, çift tıklanmışsa pencere anında kapanır. Doğrusu ayrı çocuk süreç:
    `cmd /c call "<tam yol>\flutter.bat" …`. Ayrıca `flutter.bat` PATH'ten tırnaklı çağrılırsa
    `FLUTTER_ROOT`'u yanlış çözer → önce `where` ile tam yola çöz.
12. **Batch değişkenlerine kısa genel ad verme (`RC`, `CC`, `SRC` …)** — çocuk süreçlere miras
    kalıyor: `set "RC=0"` CMake'in resource compiler değişkeniyle çakışıp Flutter build'ini
    kırdı; MSBuild de ortam değişkenlerini global property olarak okur (Unity → IL2CPP → MSVC
    dahil). `scripts/*.bat` içinde tüm betik-içi değişkenler `VA_` öneklidir.
13. **Öldüreceğin bir süreci `dotnet run` ile başlatma** — `dotnet run` asıl exe'yi ÇOCUK süreç
    olarak doğurur; parent öldürülünce `VortexArena.Server.App.exe` **yetim** kalır, 47821'i
    tutmaya devam eder ve PID takibinde olmadığı için öldürülemez → sonraki sunucu porta bind
    olamaz (yaşandı). Programatik başlatmada **her zaman doğrudan exe** (`DevProcesses`), PID
    kaydı **ad doğrulamalı** (PID'ler geri dönüşür) ve son çare **ad bazlı süpürme**.
14. **Okunmayan boru süreci kilitler** — `RedirectStandardOutput/Error = true` yapıp boruyu
    okumazsan çocuk süreç, tampon dolduğunda yazma çağrısında donar (süreç canlı görünür ama
    çalışmaz; aynı hata Flutter launcher'da yaşandı). Dev süreçleri bu yüzden
    `UseShellExecute = true` ile **kendi konsol penceresinde** koşar — boru yok, log canlı okunur.
15. **`ArenaBoundary`'yi DEVRE DIŞI BIRAKMA** — `OnDisable` → `ArenaSpace.ClearOrigin` arena uzayı
    origin'ini siler ve ağdan gelen TÜM uzak avatarlar dünya origin'ine yığılır (halkalar/ad
    etiketleri dahil). Admin gözlemci masaüstünde muhafazayı susturmak için bileşeni kapatmaz,
    `SetSpectatorMode(true)` kullanır. Aynı gerekçe her "sınırı geçici kapat" isteğinde geçerlidir.
16. **Arena sahnelerinde EventSystem YOK** (yalnız Lobby'de bir tane var) — masaüstü admin oraya
    girdiğinde HUD düğmeleri sessizce ölürdü. `UiKit.EnsureEventSystem()` kalıcı bir tane kurar,
    `TakeOverEventSystem()` sahnedekini kapatır: **iki etkin EventSystem** Unity uyarısı basar ve
    girdiyi ikisi arasında böler. Ayrıca proje Input System-only → modül
    `InputSystemUIInputModule` olmalı (`StandaloneInputModule` runtime'da patlar).
17. **BB Camera Rig'in ÜÇ kamerası da `MainCamera` etiketli** (Left/Right/CenterEye) → `Camera.main`
    hangisini döndüreceği garanti değil ve `RemoteAvatar` ad etiketleri yanlış kameraya döner.
    Sahnede kendi kamerasını kuran her şey (admin gözlemci) rig kökünü kapatmalı ve
    **kendi `AudioListener`'ını** eklemelidir (rig kapanınca sahnede dinleyici kalmaz).
    Masaüstü XR ayarı: Standalone `Initialize XR on Startup` **AÇIK** (27 Tem 2026'da geri açıldı).
    Sebep: **editör Play modu Android sekmesini değil PC/Standalone ayarını okur** (editörün kendisi
    bir PC uygulamasıdır) → kapalıyken Quest Link ile Play'e basmak gözlüğe hiçbir şey göndermez,
    `XRSettings.enabled` false kalır. Kapatılırsa VR'ı denemenin tek yolu her seferinde APK almaktır.
    Bedeli: Windows admin build'i de açılışta XR başlatmaya çalışır (başlıksız PC'de başlatma
    sessizce düşer); admin gözlemcinin rig kökünü kapatıp kendi `AudioListener`'ını kurması bu
    yüzden şarttır. Admin dağıtımında sorun çıkarsa çözüm ayarı topluca kapatmak değil, XR'ı
    role göre kod tarafında başlatmaktır (player rolünde `InitializeLoader`).
18. **`Shader.Find` build'de null dönebilir** — hiçbir materyalin referanslamadığı shader
    (`Universal Render Pipeline/Unlit` gibi) strip edilir. Runtime'da üretilen görseller bu yüzden
    UI/TMP shader'ları üzerinden çizilir (`UiKit.RingSprite` + world-space canvas), mesh + Unlit
    materyal ile değil.
19. **Quest'te "Soft Particles" YOK** — `Mobile_RPAsset.supportsCameraDepthTexture = false`
    (PC asset'te açık, bu yüzden editörde çalışıp cihazda çalışmaz). Parçacığın geometriyi
    kesmesini yumuşatmak için derinlik dokusu gerekmeyen iki araç kullanılır: materyalde
    **Camera Fading** (`_FADING_ON`, ekran uzayı w'sinden hesaplar) ve Collision modülünde
    **tek düzlem + `lifetimeLoss = 1`** (zemine değince öl). Ayrıca `renderScale = 1.6` +
    `MSAA 4` yüzünden darboğaz parçacık SAYISI değil **saydam overdraw**'dur: büyük yakın
    quad'ları `ParticleSystemRenderer.maxParticleSize` ile kırp.
20. **Ambiyans parçacığını arenadan çok geniş hacme yayma.** IceWorld'ün ilk kar sistemi
    12×12 m arenanın üstünde **50×50 m** kutuya 1500 parçacık saçıyordu → görünür alana
    ~%6'sı düşüyor, kalan bütçe boşa gidiyordu. Emisyon kutusu arena boyutu + ~3 m pay
    olmalı; derinlik hissi kutuyu büyütmekle değil, farklı boyut/hız/yoğunlukta
    **katmanlarla** kurulur.
21. **Beyaz parçacık beyaz sahnede görünmez.** Yumuşak gradyan sprite'lar 6 px'te arka plana
    karışır, büyütülünce renkli bulanıklığa döner. Çözüm: **opak çekirdek + ince smoothstep
    kenar** dokusu ve parçacığı arka plandan PARLAK tutmak (mermer duvar ~0.85 → parçacık
    1.0). Beyaz zeminde okunan tek şey **additive** katmandır (alpha katman kaybolur).
22. **Kar/pus katmanı kalibrasyonu iki farklı arka plana göre yapılır.** Gökyüzü (koyu) ve
    mermer duvar/zemin (parlak) zıt yönde çalışır: gökyüzünde iyi görünen alpha değeri
    duvarda kaybolur, duvarda iyi görünen değer gökyüzünde bulanık perde olur. Her ayardan
    sonra **hem yukarı hem duvara** bakan iki kare al. `Snow_G_Haze` yalnız gökyüzüne karşı
    okunur — bu bilinçli.

---

## 8. Durum ve sıradaki işler

**Tamamlanan:** Faz 0–6. Loopback E2E'ler geçti: lobi, poz senkronu, TDM maçı (faz makinesi +
vuruş doğrulama + free-roam canlanma), sihirbazla üretilen iki arenada maç.
APK: `Builds/vortexarena-faz4.apk` (~104 MB).

**Faz 5 (geliştirici araç seti + bağlantı hata ekranı):** `Tools > VortexArena > Dev` penceresi
(rol · hedef · Play başlangıcı · sentetik maç + tek tıkla sunucu/bot süreçleri, `Ctrl+Alt+R`),
commit'li hedef kataloğu `dev-targets.json` + kişisel seçim `EditorPrefs`'te → `AppBoot`'taki
`[SerializeField]` rol/IP override alanları kaldırıldı (Boot.unity artık kirlenmiyor); adres
zinciri rolden bağımsız hâle geldi (komut satırı > PlayerPrefs > beacon > `arena.json`);
`ConnectionOverlay` her sahnede kendini önyükleyen bağlantı hata ekranı (VR world-space + masaüstü
screen-space, ~3 sn grace, deneme sayacı + son hata, alan-dışıyken tamamen gizlenir).
**Protokol yüzeyi değişmedi** — sentetik `load_match` mevcut mesajı kullanır
(`NetEvents.InjectLoadMatch`, yalnız editör).

**Faz 6 (admin sahne-içi gözlemci):** admin artık ayrı bir dashboard sahnesinde değil,
**sunucudaki aktif sahnede** (Lobby fazında Lobby, maçta arena) — `AdminConsole.unity` +
`AdminConsoleController` tasfiye edildi, `SceneRouter` rolden bağımsız hâle geldi ve sunucu
`load_match`'i adminlere de yolluyor. Gözlemci (`VortexArena.App.Admin`) kendini önyükler,
sahneyi devralır (rig/kalibratör/BaseZone kapanır, `ArenaBoundary` **susturulur** ama kapanmaz) ve
üç kamera kipi sunar: POV · serbest (WASD/QE + sağ tuş bakış) · kuş bakışı (halka + ad etiketi).
Sahne üstü HUD: skor bandı + ortada istatistik chip'i, takım kolonları (FFA'da tek kolon), kamera
şeridi, ölüm akışı, mini harita; tercihler ve istatistikler **yarı saydam** panellerde (arkadaki
sahne izlenmeye devam eder). Görsel dil `UiKit`'e çıkarıldı (`ConnectionOverlay` de onu kullanıyor).
**Protokol:** `lobby_state.players` içine `kills/deaths/hp/alive` eklendi (§5.3) ve `load_match`
adminlere de gidiyor — yeni mesaj tipi/port/sabit YOK.

**Kullanıcı tarafında bekleyen saha testleri:**
- Faz 1: Quest'te lobi E2E.
- Faz 2: iki Quest ile fiziksel örtüşme testi (avatarlar gerçek konumda mı).
- Faz 3: iki Quest ile gerçek arenada TDM raundu — özellikle "tabanına dön ve canlan" akışının
  anlaşılır olup olmadığı.
- Faz 4: `Docs/Isletme-Kurulum.md` listesinin bir kez baştan sona yürütülmesi.
- Faz 6: admin gözlemcinin editörde/masaüstü build'inde doğrulanması (3 kip, halkalar, paneller,
  maç kontrolü) — derleme geçti, oynanış doğrulaması `plan/faz6-admin-gozlemci.md` §Doğrulama.

**"Oyuncunun gözünden izleme" — video akışıyla DEĞİL, oyun datasıyla.** MJPEG/video akışı
(cosmos `CameraStreamer` portu) **iptal edildi**: admin zaten sahneyi kendi makinesinde render
ediyor ve poz/can/skor/olay verisi ağdan geliyor. İstenen görüntü bu mevcut datadan üretilecek
(admin kamerasını hedef oyuncunun poz'una kilitlemek); protokole yeni binary kare tipi, sunucuya
kare relay'i ve Quest'te encode maliyeti **girmeyecek**. Ayrıntılı tasarım sonraki bir fazda
planlanacak.

**Planlanmamış ufuk:** quaternion sıkıştırma + delta snapshot (kalabalık maçta bant genişliği
gerektirirse), yeni modlar (FFA — admin HUD yerleşimi hazır, bölge kontrolü), dinamik obje
senkronu (`NetIdentity` + `NetSpawnCatalog` üzerinden), Meta colocation/paylaşımlı anchor
araştırması (offline çalışma şartıyla), launcher ekranından APK dağıtımı, eşzamanlı oyuncu
kotası (lisanslama katmanı geldiğinde).
