# VortexArena — Sistem Özeti (ne yapıldı, nasıl çalışıyor, nasıl kullanılır)

> **Bu doküman ne?** Faz 0–4'te kurulan sistemin tek sayfalık haritası: mimari, ağ mantığı, hangi
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
| **Windows** | `admin` | Launcher (sunucunun IP'sine bağlan) → dashboard: roster, mod+harita seçimi, start/abort, canlı taktik üstten görünüm |

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

---

## 2. Repo haritası ve assembly grafiği

```
D:\Games\vortexarena\
  Assets\
    _Shared\                 ← "ikinci bir mod/arena bunu aynen kullanır mı?" → EVET olan her şey
      Core\                  VortexArena.Core        (arena, savaş, oyuncu, UI, katalog SO'ları)
        Editor\              VortexArena.Core.Editor (Export Server Config, Arena şablon sihirbazı)
      Net\Protocol\          VortexArena.Protocol    (SAF C# — sunucu aynı dosyaları derler)
      Net\Scripts\           VortexArena.Net         (bağlantı/keşif/senkron; oyun bilgisi YOK)
        Editor\              VortexArena.Net.Editor  (Network Parent, sceneId bekçisi)
      App\Scripts\           VortexArena.App         (Boot yönlendirme, Lobi, AdminConsole, köprüler)
      Arsenal\ FX\ Environments\ Data\ Scenes\       (kod-dışı ortak içerik + Boot/Lobby/AdminConsole)
    Arenas\Standard\A10x10\  arena kutusu: {Scenes, Data, Prefabs} — arena-özel KOD yazılmaz
    Arenas\Standard\A12x12\  (sihirbazla üretildi)
    Arenas\Standard\IceWorld\ (elle modellenmiş 12×12 tematik arena + Art/{Materials,Textures})
    Arenas\Venues\DemoVenue\ (sihirbazla üretildi — 11×8 asimetrik)
    Modes\TeamDeathmatch\    mod kutusu: {Scripts → VortexArena.Modes.Tdm, Data, UI}
  Server\                    .NET 10 çözümü (Core kütüphanesi + App konsolu + PoseBot test istemcisi)
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
  └─ keşif:  elle girilen IP (PlayerPrefs)  >  beacon (5 sn dinle)  >  StreamingAssets/arena.json
  └─ ws://ip:47821/ws  →  hello{role, deviceId, scenes}  →  welcome{playerId, udpToken, match}
  └─ UDP kaydı: 0x00 UdpHello (ack gelene dek 1 sn'de bir tekrar)
  └─ status kalp atışı 5 sn  +  (player ise) poz döngüsü 20 Hz
  └─ welcome.match.phase ≠ Lobby ise → GEÇ KATILIM: maç sahnesine yetiş

Kopma → 1 → 2 → 5 sn backoff ile keşiften itibaren baştan (sonsuz, otomatik)
Sunucu → 15 sn status gelmezse çevrimdışı işaretle + bağlantıyı kapat + lobby_state yayınla
```

Elle girilen IP her zaman beacon'ı ezer ve `PlayerPrefs`'e kalıcı yazılır — işletmede beacon'ı
kesen/izole eden AP'lerde kurtarıcı budur.

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
- `MAX_PLAYERS = 16` keyfi değil: 16 oyuncu tek UDP paketine (1382 B < MTU) sığsın diye.
- Bir `playerId` ~1.5 sn snapshot'larda görünmezse avatarı kaldırılır (sunucunun 15 sn'lik
  offline eşiğini beklemez).

### 3.6 Vuruş hattı

```
Weapon.Fire()  ─raycast→ RemoteHitBox
   │
   ├─ shot_fired  → sunucu DOĞRULAMAZ, sadece relay eder (uzak namlu alevi/sesi)
   └─ hit_report  → sunucu 7 adımda doğrular:
        faz Live? · atıcı canlı? · hedef canlı? · takımlar farklı? (dost ateşi YOK) ·
        weaponId tabloda? · atış hızı makul? (60/rpm × 0.8) · damage tabloyla uyuşuyor?
                            ↓ geçerse
        hp -= damage → health_update (herkese) → hp ≤ 0 ise kill_event + IGameMode.OnKill + respawn
```

**Hasar her zaman sunucunun `weapons.json` tablosundan uygulanır.** İstemci farklı bildirirse
konsola uyumsuzluk yazılır ve tablo kazanır — bu satır "export'u çalıştırmayı unuttum"u yakalar.

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
| `ArenaClient` | Kalıcı tekil; WS bağlantısı (arka plan Task + `ConcurrentQueue` → ana thread köprüsü), hello/welcome, status kalp atışı, otomatik reconnect. **Tüm mesaj gönderimi buradan.** |
| `ServerDiscovery` | Beacon dinleme (Android'de MulticastLock), elle girilen adresin `PlayerPrefs`'e yazılması, `arena.json` fallback |
| `UdpStateChannel` | UDP kaydı (`0x00`), 20 Hz poz gönderimi, snapshot alımı |
| `RemotePlayerRegistry` | Snapshot → oyuncu başına halka tampon → `GetInterpolatedPose`, `IsAlive`, `OnRemoteJoined/Left` |
| `NetEvents` | **Statik olay merkezi** — sunucu mesajları buradan ana thread'de yayınlanır |
| `IPoseSource` | 20 Hz döngüye arena-uzayı pozu sağlayan arayüz |
| `NetIdentity` / `NetSpawnCatalog` | Sahne objesi kimliği (`sceneId`) ve id→prefab kataloğu — **dinamik obje senkronu altyapısı** (v1'de oyuncu senkronu playerId ile gider) |

### İstemci: `VortexArena.App` (akış ve köprüler)

| Sınıf | Görevi |
|---|---|
| `AppBoot` | Rol çözümü: Android → player/Lobby; masaüstü → `--role` > `VORTEX_ROLE` > admin/AdminConsole (Editor'de `editorRoleOverride` alanı) |
| `SceneRouter` | `load_match` / `return_to_lobby` / geç katılım → sahne yükleme; sahne yüklenince `set_ready` |
| `LobbyController` | VR lobi: IP:port paneli, roster, ready/takım |
| `AdminConsoleController` | Launcher (IP:port'a bağlan — sunucuyu başlatmaz) + dashboard: roster, mod/harita seçimi, start/abort/kick/identify |
| `PlayerPoseTracker` | BB rig anchor'larını bulur, kalibrasyonu bekler, **dünya→arena** çevirip `IPoseSource` olarak kaydolur |
| `RemotePlayerSpawner` | Katılan/ayrılan uzak oyuncular için `RemoteAvatar` yaratır/yok eder |
| `TacticalView` | Admin'in üstten taktik görünümü (snapshot'lardan çizilir) |
| `ModeHudSpawner` | Aktif modun HUD prefabını katalogdan örnekler — **App, mod assembly'lerini referanslamaz** (prefab yalnız `GameObject` olarak taşınır) |
| `IdentifyOverlay` | Admin `identify` yollayınca o başlıkta büyük kimlik overlay'i |

### İstemci: `VortexArena.Core` (oyun kodu)

`ArenaBoundary` (arena origin + sınır), `ArenaCalibrator` (2 nokta + OVRSpatialAnchor kalıcılığı),
`ArenaSpace` (dünya↔arena dönüşümü), `BaseZone` (taban bölgesi — canlanma kapısı), `SpawnPoint`
(takım + slot marker'ı), `MapDefinition` / `ModeDefinition` / `GameCatalog` (içerik SO'ları),
`Weapon` + `WeaponDefinition` + `WeaponAudio`, `Health` (sunucudan set edilir), `PlayerCombatState`
(yerel oyuncunun takım/can/ateş yetkisi/canlanma akışı), `RemoteAvatar` + `RemoteHitBox`
(uzak oyuncu gövdesi ve isabet kutusu).

### Sunucu: `Server/VortexArena.Server.Core`

| Sınıf | Görevi |
|---|---|
| `ControlHost` | Kestrel WebSocket host (`/ws`), bağlantı başına `ClientConnection` |
| `BeaconService` | 2 sn'de bir broadcast |
| `StateHost` | UDP kaydı, poz alımı, 20 Hz snapshot yayını |
| `PlayerRegistry` | Oyuncu listesi, `devices.json` ile kalıcı adlandırma, çevrimiçi/çevrimdışı |
| `LobbyService` | Roster yayını (`lobby_state`), ready/takım/kick |
| `MatchDirector` | **Faz makinesi (10 Hz tick), vuruş doğrulama, can/skor, canlanma, zorla canlandırma** |
| `WeaponTable` / `MapTable` | `weapons.json` / `maps.json` (Unity export'undan) |
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

### 6.2 Quest olmadan test (loopback)

```powershell
# 2 bot, yalnız poz senkronu
dotnet run --project Server/VortexArena.PoseBot -- 127.0.0.1 2
# 4 bot savaşarak + maçı başlatan admin + belirli harita/mod
dotnet run --project Server/VortexArena.PoseBot -- 127.0.0.1 4 --fight --admin --map Arena12x12 --mode tdm
```

Unity Editor'de rolü seçmek için Boot sahnesindeki `AppBoot.editorRoleOverride` alanına
`player` veya `admin` yaz (boş = normal çözüm). Editor **player** rolündeyken ortamda admin
kalmadığı için PoseBot'a `--admin` vermek şarttır.

> Botların `devices.json`'a yazdığı test girdilerini **commit'leme**.

### 6.3 Build

- **VR (player):** Android build → `Builds/*.apk` → `install_game.bat` (adb) ile başlıklara kurulur.
- **Admin:** Windows build; açılışta launcher ekranı gelir.
- Boot sahnesi build listesinde **index 0** olmalı; tüm arena sahneleri listede olmalı
  (sihirbaz bunu otomatik yapar).

### 6.4 İçerik eklemek (özet — tam reçeteler `CLAUDE.md`'de)

| İstek | Yol |
|---|---|
| **Yeni arena** | `Tools > VortexArena > Create Arena From Template` → arenaId, sahne adı, boyut, slot, hedef (Standard/Venue). Sihirbaz klasörleri + sahne kopyasını üretir, duvar/zemin/taban/spawn'ları ölçekler, `MapDefinition` yazar, `GameCatalog` + uyumlu `ModeDefinition` + Build Settings'e ekler. Sanat rötuşu elde. **Sonra `Export Server Config`.** |
| **Yeni silah** | Prefab `_Shared/Arsenal/Prefabs/` + `WeaponDefinition` SO `_Shared/Arsenal/Data/` (weaponId iki tarafta aynı string) → gerekiyorsa `ModeDefinition.loadout` → **`Export Server Config`** |
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

### 6.5 `Tools > VortexArena > Export Server Config` — ne zaman?

**Silah veya harita SO'su ekledin/değiştirdin mi → çalıştır.** Menü, `WeaponDefinition` ve
`MapDefinition` SO'larından `Server/config/weapons.json` + `maps.json` üretir; çıktı deterministiktir
(alfabetik, LF, UTF-8 BOM'suz) → git diff temiz kalır.

Unutursan ne olur: bilinmeyen `weaponId` → **`hit_report` reddedilir** (mermi işe yaramaz),
bilinmeyen `sceneName` → **`start_match` reddedilir** (maç başlamaz). İkisi de sunucu konsoluna
tek satır sebep yazar.

---

## 7. Tuzaklar (pahalıya öğrenilmiş kurallar)

1. **`weapons.json` / `maps.json` ELLE DÜZENLENMEZ** — bir sonraki export ezer. Tek doğruluk
   kaynağı Unity SO'larıdır. (`server.json` elle, `devices.json` sunucu üretir.)
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

---

## 8. Durum ve sıradaki işler

**Tamamlanan:** Faz 0–4 (dört planlı faz da bitti). Loopback E2E'ler geçti: lobi, poz senkronu,
TDM maçı (faz makinesi + vuruş doğrulama + free-roam canlanma), sihirbazla üretilen iki arenada
maç. APK: `Builds/vortexarena-faz4.apk` (~104 MB).

**Kullanıcı tarafında bekleyen saha testleri:**
- Faz 1: Quest'te lobi E2E.
- Faz 2: iki Quest ile fiziksel örtüşme testi (avatarlar gerçek konumda mı).
- Faz 3: iki Quest ile gerçek arenada TDM raundu — özellikle "tabanına dön ve canlan" akışının
  anlaşılır olup olmadığı.
- Faz 4: `Docs/Isletme-Kurulum.md` listesinin bir kez baştan sona yürütülmesi.

**Planlanmamış ufuk:** MJPEG canlı izleme (ertelendi — Quest'te fps etkisi ölçülmeli), quaternion
sıkıştırma + delta snapshot (>16 oyuncu gerekirse), yeni modlar (FFA, bölge kontrolü), dinamik obje
senkronu (`NetIdentity` + `NetSpawnCatalog` üzerinden), Meta colocation/paylaşımlı anchor
araştırması (offline çalışma şartıyla), launcher ekranından APK dağıtımı.
