# Faz 6 — Admin sahne-içi gözlemci (In-Scene Spectator)

> **Durum:** ✅ uygulandı (2026-07-27) — batch-mode derleme **0 hata / 0 uyarı**, sunucu
> `dotnet build` **0 hata / 0 uyarı**. Editör/cihaz içi oynanış doğrulaması kullanıcıda
> (aşağıdaki tabloda ⏳ olanlar).
> Sapma olursa **"Uygulama kararları"** bölümü geçerlidir (plan metni değil).
> Önkoşul: Faz 5 ✅. Bu faz **admin (Windows) istemcisinin tamamını** yeniden kurar; VR oyuncu
> tarafında davranış değişikliği **yoktur** (tek istisna: `PlayerInfo`'ya eklenen dört alan,
> ki oyuncu istemcisi onları yok sayar).

## Bağlam — neden

Bugün admin `AdminConsole` sahnesinde bir **masa başı panelinde** oturuyor: metin roster'ı,
mod/harita dropdown'ları, kill-feed ve `TacticalView` (UGUI 2B nokta haritası). Arena sahnesini
hiç görmüyor — `SceneRouter` admin rolünde `load_match`'i bilerek yok sayıyor
(`SceneRouter.cs:96-101`), sunucu da `load_match`'i yalnız oyunculara gönderiyor
(`MatchDirector.cs:398`).

İşletmedeki gerçek ihtiyaç bu değil: operatör **oyuncuların içinde olduğu sahneyi** görmek,
kimin ne yaptığını izlemek ve maçı oradan yönetmek istiyor. Dolayısıyla:

- Admin **her zaman sunucudaki aktif sahnede** olur (Lobby fazında Lobby, maçta arena sahnesi).
- Ayrı bir dashboard ekranı **kalmaz**; tüm yönetim, canlı sahnenin üstündeki yarı saydam
  panellerden yapılır.
- Admin'in **üç kamera kipi** olur: oyuncu POV'u · serbest uçuş · kuş bakışı.

Veri akışı zaten hazır — bu fazın ucuz olmasının sebebi: sunucu snapshot'ları **UDP kayıtlı
herkese, admin dahil** yolluyor (`StateHost.cs:146-171`), `health_update` / `kill_event` /
`respawn` / `match_state` / `countdown` **herkese** yayınlanıyor (`MatchDirector.QueueBroadcastLocked`),
ve `RemotePlayerSpawner` + `RemoteAvatar` admin'de olduğu gibi çalışır. Eksik olan tek şey
**admin'i sahneye sokmak** ve **sunum katmanı**.

## Kullanıcı kararları (kesinleşmiş)

| Karar | Sonuç |
|---|---|
| Admin **her zaman** sunucuda açık olan sahnede olur | `AdminConsole` dashboard'u tasfiye edilir; sahne akışı rolden bağımsız hâle gelir |
| Taktiksel dashboard görünümü **kullanılmayacak** | `AdminConsole.unity` + `AdminConsoleController.cs` **silinir** |
| Oyuncuların verisi admin'e akar, admin herkesin ne yaptığını görür | Zaten akıyor; sunum eklenir (avatar + halka + ad + HP/K/D) |
| **3 kamera kipi:** POV · serbest (WASD+QE+fare) · kuş bakışı | `AdminSpectatorCamera` |
| Kuş bakışında oyuncuların **etrafında halka, altında ad** | `AdminPlayerMarkers` (prosedürel halka sprite'ı + world-space TMP etiket — bkz. sapma S4) |
| Dashboard'daki tüm ayarlar **aktif sahnedeki canvasta** yapılır | `AdminHud` + `AdminPreferencesPanel` |
| **En tepe orta:** takım skorları · skorların ortasında **istatistikler** butonu | `AdminHud` skor bandı |
| **Yan paneller:** takım oyuncuları (FFA'da tek taraf, takımlıda sol kırmızı / sağ mavi) | `AdminHud` + `AdminPlayerRow` |
| **Sol üst:** tercihler butonu | `AdminHud` |
| Tercihler/istatistikler canvası **yarı saydam**, arkada sahne izlenmeye devam eder | Panel arkasına scrim KOYULMAZ; kart alfası ~0.88 |
| Görselliğe dikkat | Aşağıda "Görsel dil" bölümü — palet/ölçü/hareket tanımlı |

## Uygulama kararları (bana bırakılanlar — gerekçeli)

| # | Karar | Gerekçe |
|---|---|---|
| K1 | Admin'in "maç yokken" kabuğu **`Lobby` sahnesidir** (yeni bir "AdminIdle" sahnesi yapılmaz) | Kullanıcının kuralı "her zaman sunucuda açık olan sahne". Lobby fazında oyuncular Lobby sahnesindedir → admin de orada. Tek kural, iki sahne tipi için aynı kod yolu. |
| K2 | Gözlemci **prosedürel ve kendini önyükleyen** tekildir (prefab/sahne bağı yok) | Aksi hâlde her yeni arenaya elle bileşen eklemek gerekir — `ConnectArena`/`ConnectionOverlay`'de bilinçle kaçınılan tuzak. Yeni arena eklerken **hiçbir ek adım** doğmaz. |
| K3 | `ArenaBoundary` **devre dışı bırakılmaz**, `SetSpectatorMode(true)` ile susturulur | `OnDisable` → `ArenaSpace.ClearOrigin` → arena uzayı origin'i kaybolur → **tüm uzak avatarlar yanlış yere düşer**. Bileşen açık kalmalı, yalnız görsel muhafaza kapanmalı. |
| K4 | Admin HUD `sortingOrder = 4000`; `ConnectionOverlay` 5000'de kalır | Bağlantı koparsa hata ekranı HUD'ın **üstünde** olmalı; ikisi mükerrer değil, sıralı. |
| K5 | `GameCatalog` `Assets/_Shared/Data/Resources/`'a **taşınır** (git mv, GUID korunur) | Prosedürel HUD'ın `[SerializeField]`'i olamaz; mod/harita seçicisinin kataloğa runtime'da erişmesi gerekir. Tek meşru yol `Resources.Load`. Mevcut serialize edilmiş referanslar GUID sayesinde kopmaz. |
| K6 | K/D için protokole **`kills`/`deaths`/`hp`/`alive`** eklenir (`PlayerInfo`) | Sunucu bu sayaçları zaten tutuyor (`PlayerState.Kills/Deaths`) ama **hiçbir mesajda taşımıyor**. Yalnız `kill_event`'ten sayarsak admin yeniden bağlandığında istatistik sıfırlanır → otorite sunucuda kalsın. |
| K7 | Serbest kipte fareyle bakış **sağ tuş basılı tutularak** yapılır | İmleci kilitlemek HUD'ı kullanılamaz hâle getirir; operatörün tek ekranı var. |
| K8 | `TacticalView` **silinmez**, opsiyonel mini haritaya dönüşür (son adım) | Çalışan kod; kuş bakışı kamerası onun yerini alsa da POV/serbest kipte konum farkındalığı için değerli. Mini harita yapılmazsa dosya silinir — ölü kod bırakılmaz. |
| K9 | Bu fazda **MJPEG/video akışı yok** | Faz 4'te ertelendi; admin artık sahneyi doğrudan render ediyor, ihtiyaç zayıfladı. |

## Görsel dil

`ConnectionOverlay`'in paleti **tek kaynağa** (`UiKit`) taşınır ve HUD onu kullanır — iki ekran
aynı üründen görünsün.

| Rol | Değer | Kullanım |
|---|---|---|
| Kart | `#1B2029` @ 0.88 | Panel/kart zeminleri (arkadaki sahne görünür kalır) |
| Kenar | `#2E3542` | 1 px kart kenarı, ayırıcılar |
| Başlık metni | `#F5F7FA` | Skor, oyuncu adı, panel başlığı |
| Sönük metin | `#9AA4B2` | Etiketler, ipuçları |
| Çok sönük | `#6E7A8A` | Devre dışı durumlar, çevrimdışı satır |
| Vurgu | `#F2A33C` | Seçili oyuncu, aktif kamera kipi, uyarı |
| Kırmızı takım | `#D93333` | `RemoteAvatar.TeamRedColor` ile **aynı** olmalı |
| Mavi takım | `#3366E6` | `RemoteAvatar.TeamBlueColor` ile **aynı** olmalı |
| Nötr/FFA | `#999999` | Takımsız oyuncu |

- **Ölçü:** Canvas `ScaleWithScreenSize`, referans 1920×1080, match 0.5. Kenar boşluğu 24 px,
  kart iç boşluğu 20 px, **satır yüksekliği 116 px** (ad + HP barı + istatistik satırı + 4 eylem
  düğmesi sığsın), kolon genişliği 380 px, köşe yarıçapı 12 px (9-slice, `UiKit` önbelleği).
- **Tipografi:** TMP varsayılan font. Skor 64 pt, panel başlığı 28 pt, oyuncu adı 24 pt,
  ikincil satır 18 pt, ipucu 16 pt. Türkçe karakterler için font atlası zaten dolu
  (⚠ TMP'de olmayan sembol kullanılmaz — eksik glif □ çizilir, `TdmClientController` dersi).
- **Hareket:** panel açılış/kapanış 120 ms alfa+ölçek (0.98→1.0); HP barı 150 ms lerp;
  ölüm/canlanma renk geçişi 200 ms. Kamera kipi geçişinde **anlık** kesme (operatör gecikme istemez).
- **Durumlar:** canlı (tam renk) · ölü (renk ×0.5 + "ÖLÜ 3sn" geri sayımı, sonra "TABANDA
  BEKLENİYOR") · çevrimdışı (ad %45 alfa + "çevrimdışı") · seçili (**kart kenarı vurgu rengine
  döner** — takım şeridini ezmemek için; halka ×1.18 büyür ve kalınlaşır).

### Yerleşim (1920×1080)

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ [⚙ TERCİHLER]        KIRMIZI 12   ┌ 03:41 · LIVE ┐   9 MAVİ        tdm · Arena10x10  │
│                                   └  İSTATİSTİK  ┘                 ● 127.0.0.1:47821 │
│ ┌ KIRMIZI (3) ─────────┐                                    ┌ MAVİ (3) ───────────┐ │
│ │▌Gözlük 03        #3  │                                    │▌Gözlük 07       #7  │ │
│ │ ███████░░░  72 HP    │                                    │ █████████░  88 HP   │ │
│ │ 4/2 · %87 · HAZIR    │                                    │ 1/5 · %64 · ÖLÜ 3sn │ │
│ │ [POV] [TAKIM] [KİMLİK] [AT]                               │ [POV] [TAKIM] ...   │ │
│ └──────────────────────┘        (canlı sahne görüntüsü)     └─────────────────────┘ │
│                                                                                      │
│                                                              ┌ ÖLÜM AKIŞI ─────────┐ │
│                                                              │ Gözlük 03 -> 07 [ak]│ │
│ ┌ KAMERA ────────────────────────────────────────┐           └─────────────────────┘ │
│ │ [1 POV] [2 SERBEST] [3 KUŞ BAKIŞI] · Gözlük 03 │  WASD/QE gez · sağ tuşu tut bak  │
│ └────────────────────────────────────────────────┘                                   │
└──────────────────────────────────────────────────────────────────────────────────────┘
```

- **FFA yerleşimi:** çevrimiçi oyuncuların **hepsinin** `team`'i boşsa tek kolon (sol) kullanılır,
  skor bandı `KIRMIZI/MAVİ` yerine mod adı + skor limiti gösterir. Bugün FFA modu YOK
  (TDM her zaman takım atıyor) — yerleşim veriden türetildiği için mod eklenince kendiliğinden çalışır.
- **Kısayollar:** `1/2/3` kamera kipi · `Tab` sonraki oyuncu · `F` seçiliye POV ·
  `P` tercihler · `I` istatistikler · `Esc` açık paneli kapat · tekerlek: serbest kipte hız,
  kuş bakışında zoom.

---

## Adım 0 — Protokol + sunucu (ÖNCE doküman, sonra kod)

`docs-sync` kuralı: ağ davranışı değişiyor → **`Docs/ArenaNet-Protokol.md` ilk**, sonra iki taraf.

| Değişiklik | Yer |
|---|---|
| `load_match` **admin'e de** gönderilir (`yourTeam=""`, `spawnSlot=-1`) | §5.3 `load_match` satırı + §10.1 "`load_match` kişiselleştirilir" maddesi |
| `PlayerInfo`'ya `kills`, `deaths`, `hp`, `alive` eklenir | §5.3 `lobby_state` örneği |
| `lobby_state` artık **kill sonrası da** yayınlanır | §5.3 `lobby_state` başlığı ("roster her değiştiğinde" → "+ skor/K-D değiştiğinde") |
| Admin'in sahne takibi rollere göre değil, herkese aynı | §2 Roller (admin de `load_match`/`return_to_lobby` uygular; **`set_ready` göndermez**) |

Kod:

1. `Assets/_Shared/Net/Protocol/ControlMessages.cs` → `PlayerInfo`'ya dört alan
   (`int kills; int deaths; float hp; bool alive;`). Sunucu **aynı dosyayı** derliyor → tek yazım.
2. `Server/.../PlayerState.cs` → `ToPlayerInfo()` yeni alanları doldurur.
3. `Server/.../MatchDirector.cs`:
   - `StartMatchAsync` içindeki oyuncu döngüsünden sonra, **admin bağlantılarına** da `load_match`
     kuyruklanır (`yourTeam = ""`, `spawnSlot = -1`). `_registry.Snapshot()` üzerinden
     `Role == "admin" && Online && Connection != null`.
   - Ölüm işlendiği yerde (`kill_event` kuyruklandıktan sonra) roster yayını tetiklenir → K/D
     tablosu canlı kalır. Yayın **kilit dışında** yapılır (`FlushReadyClear` deseni).
4. `Server/VortexArena.PoseBot/Program.cs` → sahte admin'in `Scene`/`currentScene` değeri
   `"AdminConsole"` → `"Lobby"`, `scenes` listesinden `"AdminConsole"` çıkar (sahne tasfiye ediliyor).

⚠ **Ready kapısı:** sunucu Loading fazında yalnız `Role == "player"` olanları sayıyor
(`MatchDirector.cs:730` → `OnlinePlayersLocked`), admin sahneyi yüklese bile geri sayımı
etkilemez. **Sunucuda bu konuda değişiklik gerekmez** — istemcide `set_ready`'nin player-only
kalması yeterli.

## Adım 1 — Sahne akışı: admin sunucuyu takip eder

1. `AppSession.cs` → `SceneAdminConsole` sabiti **kaldırılır** (tek kabuk `SceneLobby`).
2. `AppBoot.cs` → rol ne olursa olsun `Lobby` yüklenir; sınıf dokümanı ve log satırı güncellenir.
3. `SceneRouter.cs` → rol kapıları kaldırılır:
   - `HandleLoadMatch`, `HandleConnected` (geç katılım), `HandleReturnToLobby`: **rolden bağımsız**
     sahne yükler.
   - `ReportSceneLoaded`: `set_ready` **yalnız player** (mevcut kapı kalır — admin "hazır"
     görünmemeli, operatörü yanıltır).
4. `EditorBuildSettings.asset` → `AdminConsole.unity` satırı çıkar. Tüm sahne yüklemeleri
   **isimle** yapıldığı için (`AppBoot.cs:48`, `SceneRouter.cs:145`) indeks kayması zararsız;
   `Boot` index 0 kalır.
5. **Silinir:** `Assets/_Shared/Scenes/AdminConsole.unity(.meta)`,
   `Assets/_Shared/App/Scripts/AdminConsoleController.cs(.meta)`.
6. `DevSession.cs` / `DevProcesses.cs` → `IsShellScene` listesinden `AdminConsole` çıkar;
   `DevSession` dokümanındaki "AdminConsole kendi bağlanmasını yapar" ifadesi düzeltilir
   (artık admin de `LobbyController` üzerinden bağlanıyor).
7. `ModeHudSpawner.cs` / `GameCatalog.cs` / `ConnectionOverlay.cs` içindeki "admin AdminConsole
   kabuğunda" yorumları güncellenir (davranış aynı: mod HUD'ı hâlâ player-only).
8. `git mv Assets/_Shared/Data/GameCatalog.asset{,.meta} Assets/_Shared/Data/Resources/`
   (K5). Yeni klasör + `.meta`. `AssetDatabase` yolu değişir, **GUID değişmez**.

**Bağlanma:** admin artık `LobbyController` üzerinden bağlanıyor — o `ArenaClient.Connect(ip, port,
AppSession.Role)` çağırıyor ve adres zincirinin başında `AppSession.HasServerEndpoint` var
(launcher'ın `--server-ip`'i) → **admin akışı bozulmadan** korunur. Lobby'nin gizli IP paneli
(A×2) masaüstünde erişilemez, sorun değil: adres launcher'dan gelir, hata durumunda
`ConnectionOverlay` devrede.

## Adım 2 — `UiKit`: paylaşılan prosedürel UI katmanı

`Assets/_Shared/App/Scripts/UiKit.cs` (**yeni**) — `ConnectionOverlay`'de kanıtlanmış yardımcıların
tek yere çıkarılması:

- Palet sabitleri (yukarıdaki tablo) + `Hex(rgb, alpha)`.
- `RoundedSprite(radius)` — önbellekli SDF yuvarlatılmış kare, `Image.Type.Sliced` için
  `Vector4` border; `HideFlags.DontSave`.
- Fabrikalar: `Panel`, `Label`, `Button`, `Bar` (HP/ilerleme), `Row`, `Divider`, `Chip`,
  `Segmented` (kamera kipi seçici), `Dropdown` (TMP), `Slider`, `Toggle`.
- `EnsureEventSystem()` — **admin-only**: kalıcı `EventSystem` + `InputSystemUIInputModule`
  kurar, sahnedeki başka `EventSystem`'i kapatır (⚠ Lobby'de bir tane VAR, arena sahnelerinde
  HİÇ yok → iki EventSystem = "double event system" uyarısı + tıklamalar ölür).

`ConnectionOverlay.cs` bu yardımcılara geçirilir (davranış aynı, ~150 satır tekrar silinir).
`VortexArena.App.asmdef`'e **`Unity.InputSystem`** referansı eklenir (proje Input System-only;
`StandaloneInputModule` runtime'da patlar).

## Adım 3 — Veri katmanı: `AdminSession` + `AdminRoster`

UI'dan **önce** veri. İki dosya, `Assets/_Shared/App/Scripts/Admin/`:

**`AdminSession.cs`** — statik oturum durumu + olaylar:
- `CameraMode { Pov, Free, TopDown }`, `SelectedPlayerId`, `OpenPanel { None, Preferences, Stats }`.
- Tercihler (`PlayerPrefs`'te kalıcı, admin PC'sine özel → repo kirlenmez): kamera hızı,
  halkalar (`Kapalı | Kuş bakışı | Her zaman`), ad etiketleri, duvar saydamlığı, HUD ölçeği,
  mini harita.
- `OnChanged` olayı → HUD + kamera + işaretçiler tek kaynaktan beslenir.

**`AdminRoster.cs`** — sunucudan gelen her şeyin birleşik canlı modeli:
- Kaynaklar: `lobby_state` (otoriter tam görüntü: ad/rol/takım/hazır/çevrimiçi/batarya/sahne
  **+ yeni** K/D/hp/alive), `health_update` (canlı hp), `kill_event` (K/D + akış satırı),
  `respawn` (geri sayım), `match_state`/`countdown`/`match_end` (faz, süre, skor),
  `RemotePlayerRegistry` (poz/alive bayrağı, snapshot yaşı).
- Türetilenler: takım listeleri (sıralı), `IsFfa` (tüm çevrimiçi oyuncuların takımı boş mu),
  ölüme kalan süre, ağ sağlığı (`LastSnapshotMs` yaşı).
- **Yalnız veri** — hiçbir `UnityEngine.UI` tipine dokunmaz; HUD ve paneller bunu okur.

## Adım 4 — `AdminSpectator` + `AdminSpectatorCamera`

**`AdminSpectator.cs`** — kendini önyükleyen kalıcı tekil (K2):

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
// rol admin değilse HİÇBİR ŞEY yapmaz (VR build'de ölü kod gibi durur)
```

`sceneLoaded` başına sahneyi **devralır** (idempotent):

| İş | Neden |
|---|---|
| BB Camera Rig kökünü kapat | 3 kamerası da `MainCamera` etiketli → `Camera.main` belirsiz; `RemoteAvatar` etiketlerini yanlış kameraya döndürür. Masaüstünde XR **başlatılmıyor** (`XRGeneralSettingsPerBuildTarget`: Standalone `m_InitManagerOnStart: 0`) → rig zaten işlevsiz. |
| `ArenaCalibrator`'ı kapat | `OVRSpatialAnchor` çağrıları masaüstünde anlamsız (ve gürültülü) |
| `ArenaBoundary.SetSpectatorMode(true)` | K3 — origin korunur, karartma/uyarı susar, duvarlar sabit alfaya çekilir |
| World-space canvas'ları kapat | Lobby'nin VR paneli admin ekranında havada durmasın (aynı bilgi HUD'da) |
| Kendi kamerasını + `AudioListener`'ını yerleştir | Rig kapandığı için sahnede kamera/dinleyici kalmaz ("no audio listener" uyarısı) |

**`ArenaBoundary.cs` (Core) değişikliği** — tek yeni public API:

```csharp
/// <summary>Gözlemci kipi: ArenaSpace origin'i KORUNUR, görsel muhafaza susar.</summary>
public void SetSpectatorMode(bool on, float wallAlpha = 0.25f)
```
`Update` `on` iken erken döner, `IsOutOfBounds` false'a sabitlenir, fade quad + uyarı kapatılır,
duvarlar `wallAlpha`'ya çekilir. Core → App bağımlılığı doğmaz (App çağırır).

**`AdminSpectatorCamera.cs`** — üç kip:

| Kip | Davranış |
|---|---|
| **POV** (`1`) | Seçili oyuncunun **baş** pozu: `RemotePlayerRegistry.GetInterpolatedPose` → `ArenaSpace.ArenaToWorld` → `LateUpdate`'te kameraya uygula. FOV 90. Poz yoksa son pozda kalır, HUD "poz yok" yazar. Seçili oyuncu düşerse otomatik olarak sıradakine geçer. |
| **Serbest** (`2`) | WASD düzlemde, `Q/E` alçal/yüksel, **sağ tuş basılı** fareyle bakış (K7), `Shift` ×3 hız, tekerlek hız kademesi. `y >= 0.2` tabanı. Girdi `Keyboard.current`/`Mouse.current` (aksiyon asset'i yok — kullanıcı `RuntimeActionBindings.json`'u silmiş, ona bağlanılmaz). |
| **Kuş bakışı** (`3`) | Ortografik, arena merkezinin üstünde, pitch 90°, yaw = arena origin yaw'ı. `orthographicSize` = arena yarı-ölçüsü + %8 pay; en-boy oranına göre X/Z'den büyüğü seçilir. Sınır kaynağı sırayla: sahnedeki `ArenaBoundary` → aktif haritanın `MapDefinition.Size` (katalog) → 10×10 varsayılan (Lobby). Tekerlek zoom. |

## Adım 5 — `AdminPlayerMarkers` (halka + ad etiketi)

`Assets/_Shared/App/Scripts/Admin/AdminPlayerMarkers.cs` (**yeni**) — oyuncu başına dünya-uzayı
işaretçisi. `RemotePlayerRegistry.OnRemoteJoined/OnRemoteLeft` ile yaşar; `RemoteAvatar`'a
**dokunmaz** (oyuncu tarafı kodu değişmesin).

- **Halka:** runtime üretilen `Mesh` (48 segment şerit halka), URP `Universal Render Pipeline/Unlit`
  materyali, renk `MaterialPropertyBlock` ile. Zeminde, ayak hizasının 2 cm üstünde; yarıçap 0.45 m.
  Seçili oyuncuda kalınlık ×1.6 + vurgu rengi; ölüde renk ×0.35.
- **Ad etiketi:** TMP (3B) halkanın **altında**; kuş bakışında metin yukarıdan okunacak şekilde
  yaw'ı kameraya hizalanır, diğer kiplerde kameraya billboard olur. İçerik: `ad · HP` (+ ölüyse
  `(ölü)`).
- **Görünürlük:** `AdminSession` tercihine göre (varsayılan: kuş bakışı + serbest kipte açık,
  POV'da kapalı — kendi kafasının içinde halka görmek anlamsız).

## Adım 6 — `AdminHud` (kalıcı canvas)

`AdminHud.cs` + `AdminPlayerRow.cs`. `AdminSpectator` ile birlikte doğar; `DontDestroyOnLoad`
olduğu için Lobby ↔ arena geçişlerinde **kesintisiz** kalır (kullanıcının "sürekli arkaplanda
sahne açıkken bu şekilde görülecek" isteği birebir bu).

- `Canvas`: `ScreenSpaceOverlay`, `sortingOrder = 4000` (K4), `CanvasScaler` 1920×1080.
- **Skor bandı (üst orta):** `KIRMIZI n` — orta chip (`süre · faz`, tıklanınca **istatistikler**) —
  `n MAVİ`. `match_state`/`countdown`/`match_end` ile beslenir.
- **Tercihler düğmesi (sol üst)** ve **maç kimliği (sağ üst):** `mod · harita` + bağlantı noktası
  (yeşil/kırmızı, snapshot yaşı > 1 sn ise sarı).
- **Yan kolonlar:** `AdminPlayerRow` — takım şeridi, ad, `#id`, HP barı + değer, `K/D`, batarya,
  durum (hazır/yükleniyor/ölü+geri sayım/çevrimdışı). Satır tıklaması = seçim; küçük düğmeler:
  `POV` · `TAKIM` (red↔blue, `set_team`) · `KİMLİK` (`identify`) · `AT` (`kick`, **iki adımlı
  onay** — tek tıkla oyuncu atılmaz).
- **Kamera şeridi (alt orta):** segmented `POV | SERBEST | KUŞ BAKIŞI` + seçili oyuncu adı +
  kısayol ipucu.
- **Ölüm akışı (alt sağ):** 8 satır, mevcut `AdminConsoleController.HandleKillEvent` biçimi korunur
  (`ad -> ad [silah]`, TMP'de olmayan sembol yok).
- Boş durumlar: bağlı ama oyuncu yoksa "Oyuncu bekleniyor"; bağlantı yoksa HUD sönükleşir ve
  üstünü `ConnectionOverlay` alır.

## Adım 7 — `AdminPreferencesPanel` + `AdminStatsPanel`

İkisi de ortalanmış kart, **arkasında scrim yok**, kart alfası 0.88 → sahne izlenmeye devam eder
(kullanıcının açık isteği). Aynı anda yalnız biri açık; `Esc` kapatır. Panel açıkken oyun/maç
**durmaz** (otorite sunucuda, durdurulacak bir şey yok).

**Tercihler (sol üst düğme / `P`):**
- *Maç:* mod dropdown + harita dropdown (katalogdan, `MapsForMode` ile filtreli) +
  `BAŞLAT` / `İPTAL` / `LOBİYE DÖN`. Eski dashboard'un işi buraya taşınır; `start_match`
  doğrulaması sunucuda kalır, ret sebebi sunucu konsolunda.
- *Görünüm:* kamera hızı, halkalar (kapalı/kuş bakışı/her zaman), ad etiketleri, duvar
  saydamlığı, HUD ölçeği, mini harita.
- *Bağlantı:* adres + durum, `YENİDEN BAĞLAN`, `BAĞLANTIYI KES`.

**İstatistikler (skor bandı ortası / `I`):**
- Takım toplamları: skor, toplam K/D, canlı sayısı.
- Oyuncu tablosu: ad · takım · K · D · K/D · HP · hazır · batarya · sahne · çevrimiçi
  (kaynak: `AdminRoster`; K/D **sunucudan**, K6).
- Maç bilgisi: faz, kalan süre, mod, harita, skor limiti, sunucu adresi, snapshot yaşı.
- ⚠ Uydurma metrik gösterilmez: hasar/isabet oranı/ping protokolde **yok** — eklenirse ayrı iş.

## Adım 8 — Doküman + toplu doğrulama

`batch-build-verification`: tüm adımlar bitince tek geçiş.

| Doküman | Güncelleme |
|---|---|
| `Docs/ArenaNet-Protokol.md` | Adım 0'da **önce** yazılır (§2, §5.3, §10.1) |
| `Docs/Sistem-Ozeti.md` | §2 repo haritası (Admin/ klasörü, silinen sahne), §3.4 bağlantı yaşam döngüsü (admin de sahne yükler), §4 bileşen sözlüğü (7 yeni bileşen, `AdminConsoleController` **silinir**), §6.2 dev akışı (admin artık Lobby'den başlar), §7 tuzaklar (**3 yeni:** `ArenaBoundary` kapatmak origin'i siler · iki EventSystem · üç `MainCamera` etiketli rig kamerası), §8 durum |
| `CLAUDE.md` | Akış bölümü (Boot → her rol Lobby; AdminConsole yok), arena sahnesi gereksinimleri (gözlemci **ek adım gerektirmez**), katalog yolu `Data/Resources/`, `App` asmdef'ine `Unity.InputSystem` |
| `Docs/Kullanim-Kilavuzu.md` | Operatör bölümü: 3 kamera kipi, kısayollar, tercihler/istatistikler, oyuncu satırındaki eylemler |
| `Docs/Isletme-Kurulum.md` | Smoke test adımına "admin arena sahnesini görüyor + POV çalışıyor" |
| `Server/README.md` | `load_match` admin'e de gidiyor notu |
| `plan/README.md` | Faz 6 satırı + durum |

## Dokunulacak dosyalar (gerçekleşen)

⚠ = plandan sapma (gerekçesi "Uygulama kararları"nda).

| Dosya | Değişiklik |
|---|---|
| `Docs/ArenaNet-Protokol.md` | **ilk yazıldı** — §2 (admin sahne takibi), §5.3 (`lobby_state` +4 alan, `load_match` adminlere), §10.1 (ready kapısı), §10.2 (sayaçların yayını) |
| `Assets/_Shared/Net/Protocol/ControlMessages.cs` | `PlayerInfo` + `kills`/`deaths`/`hp`/`alive` |
| `Server/VortexArena.Server.Core/PlayerState.cs` | `ToPlayerInfo()` yeni alanlar |
| `Server/VortexArena.Server.Core/MatchDirector.cs` | `load_match` adminlere; ⚠ `_rosterRefreshFor` + `FlushRosterRefresh()` (S7); `ResetMatchStateLocked` artık static değil |
| `Server/VortexArena.PoseBot/Program.cs` | ⚠ sahte admin sahnesi `AdminConsole` → `Lobby`, `BuildScenes` temizliği (S9) |
| `Assets/_Shared/App/Scripts/AppSession.cs` | `SceneAdminConsole` **kaldırıldı** |
| `Assets/_Shared/App/Scripts/AppBoot.cs` | her rol → `Lobby` |
| `Assets/_Shared/App/Scripts/SceneRouter.cs` | sahne yükleme rolden bağımsız; ⚠ `set_ready` kapısı bilinçle KALDI (S1) |
| `Assets/_Shared/Core/Arena/ArenaBoundary.cs` | `SetSpectatorMode(bool, float)` + `HalfExtents` |
| `Assets/_Shared/App/Scripts/VortexArena.App.asmdef` | `Unity.InputSystem` referansı |
| `Assets/_Shared/App/Scripts/UiKit.cs` | **yeni** — palet + fabrikalar + yerleşim + EventSystem; ⚠ `RingSprite` + `WorldCanvas` + `TakeOverEventSystem` (S5) |
| `Assets/_Shared/App/Scripts/ConnectionOverlay.cs` | `UiKit`'e geçiş (~150 satır tekrar silindi); EventSystem garantisi (arena sahnelerinde yok) |
| `Assets/_Shared/App/Scripts/Admin/AdminSession.cs` | **yeni** — seçim + tercihler (`PlayerPrefs`) |
| `Assets/_Shared/App/Scripts/Admin/AdminRoster.cs` | **yeni** — birleşik canlı model; ⚠ canlanma geri sayımı yerel (S6) |
| `Assets/_Shared/App/Scripts/Admin/AdminSpectator.cs` | **yeni** — önyükleme + sahne devralma |
| `Assets/_Shared/App/Scripts/Admin/AdminSpectatorCamera.cs` | **yeni** — 3 kip + girdi |
| `Assets/_Shared/App/Scripts/Admin/AdminPlayerMarkers.cs` | **yeni** — halka + ad etiketi |
| `Assets/_Shared/App/Scripts/Admin/AdminHud.cs` | **yeni** — kalıcı canvas + mini harita |
| `Assets/_Shared/App/Scripts/Admin/AdminPlayerRow.cs` | **yeni** — oyuncu satırı (iki adımlı kick) |
| `Assets/_Shared/App/Scripts/Admin/AdminPreferencesPanel.cs` | **yeni** — maç + görünüm + bağlantı; ⚠ döngüleyici (S3) |
| `Assets/_Shared/App/Scripts/Admin/AdminStatsPanel.cs` | **yeni** — kolon kolon istatistik tablosu |
| `Assets/_Shared/App/Scripts/Admin/AdminCommands.cs` | **yeni** ⚠ planda yoktu (S2) — komutların tek çıkış kapısı + durum metni |
| `Assets/_Shared/App/Scripts/Admin/AdminContent.cs` | **yeni** ⚠ planda yoktu (S2) — `Resources.Load` katalog erişimi |
| `Assets/_Shared/App/Scripts/TacticalView.cs` | ⚠ silinmedi — `Initialize(RectTransform)` ile mini harita oldu (S8) |
| `Assets/_Shared/Data/GameCatalog.asset(.meta)` | `Data/Resources/`'a **git mv** (GUID korundu) |
| `ProjectSettings/EditorBuildSettings.asset` | `AdminConsole.unity` satırı çıktı (tüm yüklemeler isimle → indeks kayması zararsız) |
| `Assets/_Shared/Scenes/AdminConsole.unity(.meta)` | **silindi** |
| `Assets/_Shared/App/Scripts/AdminConsoleController.cs(.meta)` | **silindi** |
| `Assets/_Shared/App/Scripts/DevSession.cs` · `Editor/DevProcesses.cs` | `IsShellScene` + doküman |
| `Assets/_Shared/App/Scripts/ModeHudSpawner.cs` · `Core/GameCatalog.cs` | yorum/doküman |
| `Docs/Sistem-Ozeti.md` · `CLAUDE.md` · `Docs/Kullanim-Kilavuzu.md` · `Docs/Isletme-Kurulum.md` · `Server/README.md` · `launcher/{README.md,lib/launcher_page.dart}` | doküman senkronu (aynı commit) |

## Doğrulama

| # | Madde | Durum |
|---|---|---|
| 1 | **Sahne takibi:** admin Lobby'de başlar; `start_match` sonrası arena sahnesini yükler, `return_to_lobby` ile döner | ⏳ editörde |
| 2 | **Geç katılım:** maç koşarken admin yeniden başlatılır → doğrudan arena sahnesine düşer (`welcome.match`) | ✅ tasarımca (`SceneRouter.HandleConnected` rol kapısı kaldırıldı) · ⏳ elle |
| 3 | **Ready kirlenmesi yok:** admin sahneyi yükleyince "hazır" görünmez, geri sayım oyuncuları bekler | ✅ kod (`ReportSceneLoaded` player-only + sunucu `OnlinePlayersLocked` yalnız player sayıyor) |
| 4 | **Avatar konumu doğru** (K3 testi: `ArenaBoundary` susturuldu ama kapatılmadı → origin yaşıyor) | ⏳ editörde |
| 5 | **POV:** seçili oyuncunun kafasına oturur, poz yoksa "poz yok" yazar, oyuncu düşerse sıradakine geçer | ⏳ editörde |
| 6 | **Serbest:** WASD/QE + sağ tuş bakış; imleç kilitlenmez, HUD tıklanabilir kalır | ⏳ editörde |
| 7 | **Kuş bakışı:** tüm arena kadrajda, halkalar + adlar okunur, ölü sönük | ⏳ editörde |
| 8 | **HUD verisi:** skor/faz/süre, HP barı, K/D (admin yeniden bağlanınca sıfırlanmıyor — K6) | ⏳ editörde |
| 9 | **Paneller yarı saydam,** arkadaki sahne izlenebiliyor, `Esc` kapatıyor | ✅ tasarımca (scrim yok, kart alfası 0.88) · ⏳ göz kontrolü |
| 10 | **Maç kontrolü:** mod/harita + BAŞLAT/İPTAL/LOBİYE DÖN, satırdan takım/kimlik/at (iki adımlı) | ⏳ editörde |
| 11 | **EventSystem:** arenada düğmeler çalışıyor, Lobby'de çift EventSystem uyarısı yok | ⏳ editörde |
| 12 | **Bağlantı hatası:** ~3 sn sonra `ConnectionOverlay` HUD'ın üstünde (5000 > 4000) | ✅ kod · ⏳ elle |
| 13 | **VR regresyonu:** oyuncu tarafı etkilenmedi | ✅ kod (gözlemci Android'de hiç önyüklenmez, mod HUD'ı player-only, protokol alanları toplanır) · ⏳ cihazda |
| 14 | **Build:** admin exe + APK | ⏳ kullanıcıda |
| 15 | **Derleme:** Unity batch-mode `error CS` = 0, uyarı 0; `dotnet build` 0/0 | ✅ (2026-07-27) |

## Uygulama kararları (plandan sapmalar)

| # | Sapma | Gerekçe |
|---|---|---|
| S1 | **`SceneRouter` guard'ı** planda "rol kapıları kaldırılır" idi; `ReportSceneLoaded` içindeki player kapısı **bilinçle KALDI** | Admin `set_ready` göndermemeli (operatör "hazır" görünen bir admin görürse yanılır). Sunucu tarafında da gerek yoktu: ready kapısı zaten yalnız `role=player` sayıyor (doğrulandı) |
| S2 | **`AdminContent.cs` + `AdminCommands.cs`** planda yoktu (11 dosya → 13) | Katalog erişimi (Resources) ve komut gönderimi hem HUD hem paneller tarafından kullanılıyor; satır içi tekrar yerine iki küçük odaklı dosya |
| S3 | **Dropdown/slider yerine `[<] değer [>]` döngüleyici ve `[-] değer [+]` adımlayıcı** | `TMP_Dropdown`/`Slider` serialize edilmiş şablon hiyerarşisi ister (viewport, item template, handle); prosedürel kurulumda uzun ve kırılgan. Operatör için de daha az hatalı |
| S4 | Halkalar **mesh + URP Unlit** değil, **world-space canvas + prosedürel halka sprite'ı** | `Shader.Find("Universal Render Pipeline/Unlit")` build'de null dönebilir (hiçbir materyal referanslamıyorsa shader strip edilir). UI/TMP shader'ları her zaman build'de → garantili yol. Tuzak §7'ye eklendi |
| S5 | `UiKit`'e **`RingSprite` + `WorldCanvas` + `TakeOverEventSystem`** eklendi (planda yoktu) | S4 ve EventSystem devralma ihtiyacı ortaya çıktı |
| S6 | **`respawn` mesajı admin'e gelmiyor** — geri sayım `kill_event` + `RESPAWN_DELAY` ile YEREL hesaplanıyor | Sunucu `respawn`'ı yalnız ölen oyuncunun bağlantısına yolluyor (§10.4). Protokolü genişletmek yerine türetildi; sayaç bitince "TABANDA BEKLENİYOR"a döner (yanlış "canlandı" demez) |
| S7 | Sunucuda **`_rosterRefreshFor` + `FlushRosterRefresh()`** deseni | `registry.Announce` olay tetikliyor → `_gate` kilidi altında çağrılamaz. Mevcut `FlushReadyClear` deseni birebir tekrarlandı |
| S8 | `TacticalView` **silinmedi**, mini haritaya dönüştü (opsiyonel adım uygulandı) | Çalışan kod + POV/serbest kipte konum farkındalığı; `Initialize(RectTransform)` ile prosedürel kuruluyor, kuş bakışında gizleniyor |
| S10 | **Sunucu oyuncusuz maç başlatıyor** (kullanıcı isteği, plan sonrası): `start_match`'in "en az 1 çevrimiçi oyuncu" koşulu kaldırıldı; Loading'de beklenecek `set_ready` olmadığı için faz doğrudan Countdown'a geçer | Operatör haritayı boş arenada görmek istiyor. Güvenlik ayrımı korundu: **oyuncularla başlamış** maçta son oyuncu düşerse hâlâ lobiye dönülür (`_startedWithPlayers`), oyuncusuz başlatılmışta dönülmez |
| S11 | **Harita seçimi değişince anında yerel önizleme** (kullanıcı isteği, plan sonrası): faz Lobby ise seçilen arena admin'de hemen yüklenir (`SceneRouter.LoadPreview`) | Sunucuya komut gitmez, oyuncular etkilenmez, `LastMatchScene`/`LastModeId` (sunucu gerçeği) DEĞİŞMEZ. Maç sürerken devre dışı — o an sahne otoritesi sunucudadır. HUD ve mini harita "yüklü sahne"yi baz alır, sunucunun bildirdiğini değil |
| S9 | `PoseBot`'un sahte admin'i artık `Lobby` sahnesini bildiriyor, `BuildScenes`'ten `AdminConsole` çıkarıldı | Sahne tasfiye edildi; bot gerçek istemciyi taklit etmeye devam etmeli |

## Kapsam dışı (bilinçli)

- **MJPEG/video akışı** (K9) — admin sahneyi kendi render ediyor.
- **Kayıt/replay, ısı haritası, hasar istatistiği** — protokolde veri yok, ayrı iş.
- **Admin'in oyuncu HUD'ını görmesi** (POV'da mod HUD'ı) — mod HUD'ı player-only kalıyor;
  istenirse `ModeHudSpawner`'a "gözlemci kipi" eklenir.
- **FFA modu** — yerleşim hazır, mod yok.
- **Admin'in oyuncuya ses/mesaj göndermesi** — `identify` dışında kanal yok.

## Opsiyonel — `TacticalView` mini harita (K8)

Kuş bakışı kamerası ana çözüm; ama POV/serbest kipte konum farkındalığı için sağ altta küçük
harita değerli. `TacticalView` bugün çalışıyor, tek eksiği `mapArea`'nın serialize edilmesi:
`Initialize(RectTransform area, float width, float length)` eklenip HUD'dan prosedürel bir
`RectTransform` verilir, `AdminSession.MiniMap` tercihiyle açılıp kapanır. **Yapılmazsa dosya
silinir** — kullanılmayan bileşen bırakılmaz.
