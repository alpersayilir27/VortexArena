# VortexArena — Proje Talimatları (CLAUDE.md)

Free-roam VR PvP arena ürünü (işletmelere kurulum / LBE; Meta Quest 3 & 3S, Unity 6000.3.20f1, URP).
Oyuncular fiziksel alanda 1:1 yürür; farklı boyutlarda arenalar (10x10, 12x12, işletmeye özel),
farklı oyun modları/haritalar/silahlar. VR build = player, Windows build = admin (yönetim + izleme).
Online haberleşme: kendi .NET sunucumuz (`Server/`, standalone exe, offline LAN) — Mirror/NGO YOK.

> Kurallar `.claude/rules/` altındadır. Sıradaki planlanmış işler: `plan/` (biten iş dokümanı silinir). Protokol: `Docs/ArenaNet-Protokol.md` (TEK doğruluk kaynağı).
> Sistemin tek sayfalık haritası (ne var, ağ nasıl çalışır, nasıl kullanılır): `Docs/Sistem-Ozeti.md`.
> Sahadaki operatörün günlük kullanım kılavuzu (teknik olmayan dille): `Docs/Kullanim-Kilavuzu.md`.
> Çatılı arena yapan geliştiriciye tek parça teknik not (bileşen + katman + editör aracı + tuzaklar):
> `Docs/Cati-Gizleme.md`.

## Çalışma tarzı (detay `.claude/rules/`)

- **Arama = önce auggie.** `mcp__auggie__codebase-retrieval` birincil bağlam aracıdır: "X nerede /
  bu nasıl çalışıyor / neyi etkiler" sorularında ilk durak; dönen sonuç Read/Grep ile teyit edilir
  (indeks bayat olabilir). Tam simge/string biliniyorsa doğrudan Grep. → `auggie-first-search.md`
- **Kod değişti = doküman değişti.** Temel kodda (protokol, ağ akışı, maç kuralı, mimari, editor
  tool'u, sunucu config'i) değişiklik AYNI commit'te dokümana yazılır; ağ davranışında sıra
  **önce `Docs/ArenaNet-Protokol.md`, sonra kod**. Hangi değişiklik hangi dokümana gider tablosu →
  `docs-sync.md`
- **Editörde rol/adres dev penceresinden seçilir.** `Tools > VortexArena > Dev` (rolü çevirmek için
  `Ctrl+Alt+R`): hedef listesi `dev-targets.json`'dan gelir (commit'li), seçimin kendisi
  `EditorPrefs`'te kişisel kalır → rol/IP değiştirmek hiçbir sahne/asset kirletmez.
  ⚠️ Boot.unity'ye (ya da başka bir sahneye) rol/IP için **[SerializeField] override KOYULMAZ** —
  `AppBoot.editorRoleOverride`/`editorServerIp` tam bu yüzden kaldırıldı.
- Doğrulama batch'lenir (`batch-build-verification.md`), editör doğrulaması Unity CLI ile yapılır
  (`unity-cli.md`), ağır uygulama işi alt-ajanlara devredilir (`delegate-to-subagents.md`),
  hafıza yalnız proje scope'unda tutulur (`ai-memory-scope.md`).

## Repo üst düzey yerleşim

`Assets/` (Unity) · `Server/` (.NET 10 sunucu kaynağı) · **`launcher/`** (Flutter Windows
launcher — operatör buradan admin oyununu başlatır) · **`scripts/`** (`deploy-admin-game.bat`,
`deploy-server.bat`, `deploy-launcher.bat`) · **`deploy/`** (üretilen çalıştırılabilirler:
`admin/`, `server/`, `launcher/` — **git'e girmez**) · **`dev-targets.json`** (repo kökü,
**commit'li**: dev penceresinin adlandırılmış sunucu hedefi kataloğu — `Local`, `Kesif (beacon)`,
`Ornek-PC` + `defaultTarget`/`defaultRole`; bir hedefin `ip`'si **boşsa** adres yazılmaz,
keşif zinciri devralır) · `Docs/` · `plan/` · `.claude/rules/`.

**`.gitignore` proje tipi başına ayrıdır** — her biri kendi klasörünü yönetir:
kök = Unity (+ repo geneli OS/IDE) · `Server/` = .NET 10 · `launcher/` = Flutter (Windows-only;
`launcher/windows/.gitignore` Flutter'ın kendi dosyası) · `deploy/` = beyaz liste (`*` + yalnız
README). ⚠️ Köke Unity deseni eklerken **`/` ile sabitle**: `*.sln`/`*.csproj` sabitlenmezse
Server'ın gerçek kaynaklarını, `*.app` ise Windows'ta (`core.ignorecase=true`)
`Server/VortexArena.Server.App/` klasörünü yutar. Alt proje çıktısı (bin/obj, build/, .dart_tool)
kökte DEĞİL, ilgili klasörün kendi dosyasında ignore edilir.

## Asset mimarisi (feature-first + asmdef)

- `Assets/_Shared/` — ortak. Ortak KOD yalnız bir asmdef altında: `Core/` (VortexArena.Core),
  `Net/Protocol` (VortexArena.Protocol — saf C#, server aynı dosyaları derler), `Net/Scripts`
  (VortexArena.Net), `App/Scripts` (VortexArena.App — `Admin/` alt klasörü aynı asmdef'te:
  admin gözlemci; `UiKit.cs` prosedürel arayüz kiti). Kod-dışı: `Arsenal/` (silah prefab+SO),
  `FX/`, `Environments/`, `Data/` (**`Data/Resources/GameCatalog.asset`** — prosedürel admin
  arayüzü `Resources.Load` ile okuduğu için klasörden ÇIKARILMAZ), `Scenes/` (Boot, Lobby).
  ⚠️ Ayrı bir admin dashboard sahnesi YOKTUR (`AdminConsole.unity` kaldırıldı) — admin
  oyuncularla aynı sahnede duran bir gözlemcidir.
  ⚠️ `_Shared` köküne asmdef'siz gevşek script koyMA (Assembly-CSharp'a düşer, kimse göremez).
- `Assets/Arenas/Standard/<AXxX veya TemaAdı>/` ve `Assets/Arenas/Venues/<İşletme>/` — arena kutuları:
  `{Scenes, Data, Prefabs}` (+ arenaya özel sanat varsa `Art/{Materials,Textures}`; ör. `Standard/IceWorld`).
  Arena = sahne + MapDefinition; arena-özel kod YAZILMAZ (marker bileşenleri Core'dan gelir).
  Bir arenanın ağa bağlanması için sahnede şunlar olmalı: `ArenaBoundary` (arena origin + halfExtent),
  `BaseZone`×2, `SpawnPoint`×(2×slot), `ArenaCalibrator`, `PlayerPoseTracker`, `RemotePlayerSpawner`,
  `ModeHudSpawner`, BB Camera Rig. **Admin gözlemci için ek adım YOKTUR** — `AdminSpectator`
  kendini önyükler ve sahneyi devralır (rig'i kapatır, `ArenaBoundary`'yi susturur).
  **Çatılı arenada tek isteğe bağlı adım:** çatı kökünde `ArenaRoof`
  (`GameObject > VortexArena > Arena Roof`) — altındaki tüm Renderer'lar çatı sayılır, `ArenaRoof`
  katmanı (user layer 8) damgalanır ve admin kuş bakışına geçince çatı çizilmez (gölgesi kalır).
  Katman yalnız "hangi geometri gizlenecek" sorusunu sahnede görünür kılar; davranış Renderer
  listesinden gelir. Açık tavanlı arenalarda bu adım hiç yapılmaz.
  → tam reçete, editör aracının davranışı, tuzaklar ve sorun giderme: **`Docs/Cati-Gizleme.md`**
- `Assets/Modes/<Mod>/` — mod kutuları: `{Scripts (VortexArena.Modes.<Ad>.asmdef), Data, UI}`.
  Modlar birbirini REFERANSLAMAZ.
- Üçüncü parti: `Assets/ThirdPartyPackages/`. ⚠️ İstisna: `Assets/Low Poly AR Weapon Pack 1/`
  kökte duruyor (editör açıkken taşıma OS dosya kilidine takılıyor — dört denemede de
  "Moving file failed"). Editör KAPALIYKEN `git mv` ile taşınabilir; sonrasında
  `WeaponKitBuilder.PackRoot` sabitini güncelle (tek satır).

**Assembly grafiği** (bağımlılık hep aşağı):
Protocol (saf C#, noEngineReferences) ← Net ← Core ← App, Modes.<X>
Net oyun/sahne bilgisi içermez; olay yayınlar, App dinler. Editor asmdef'leri
`includePlatforms:["Editor"]` + kendi runtime'ını referanslar.
App ayrıca `Unity.InputSystem` referanslar (gözlemci kamerası klavye/fare + `InputSystemUIInputModule`);
proje **Input System-only** — `StandaloneInputModule` runtime'da patlar, kullanılmaz.

**İsimlendirme:** asmdef = `VortexArena.<Katman>`; namespace = asmdef adıyla birebir
(rootNamespace dolu); global namespace'te tip YOK; serialize edilen ikincil tipler kendi
dosyasında (`Team.cs` gibi). Sahne adı = katalog anahtarı (`load_match` string'i) → birebir eşleşme.
⚠️ **Serialize edilen enum'a yeni değer SONA eklenir** — Unity sayısal indeks saklar, başa/ortaya
ekleme sahnelerdeki değerleri kaydırır. `Team = { Red, Blue, Neutral }`: `Neutral` bu yüzden sonda
(`BaseZone`/`SpawnPoint`/`Weapon` bu enum'u serialize ediyor). Aynısı `ModeTeamMode`/`ModeScoreKind`/
`ModeReviveAnchor`/`ModeWeaponSource` için de geçerli.

**Paylaşımlı-mı-modül-mü:** "İkinci bir mod/arena bunu aynen kullanır mı?" → evet=_Shared, hayır=kutu.

## XR / Meta politikası

- **Meta-first:** önce Meta Building Blocks + Meta XR SDK; yetmezse Unity XR Interaction Toolkit
  (XRI kurulu, yedek). Hedef YALNIZ Quest 3/3S. Sahnelerde BB Camera Rig kullanılır.
- **Umbrella paket YASAK** (`com.meta.xr.sdk.all` — Meta Project Setup Tool önerse bile ekleme):
  kullanılmayan voice@85, SDKTelemetry.aar ↔ OVRPlugin.aar Android namespace çakışmasıyla
  build kırar (vortexcosmos'ta yaşandı). Bireysel paketler: core + interaction + interaction.ovr
  @203.0.0, audio @85.0.0 (spatializer=Meta XR Audio olduğu için gerekli, pinli).
- Haptik: `OVRInput.SetControllerVibration` (core) — ayrı haptics paketi ekleme.
- XR loader: OpenXR (mevcut, çalışıyor) — değiştirme.

## Network (özet — detay Docs/ArenaNet-Protokol.md)

Portlar: UDP beacon 47820 · WS kontrol 47821 `/ws` · UDP state 47822 (cosmos 47800/1 ile çakışmaz).
Pozlar istemci-otoriter (kalibrasyon sonrası ARENA UZAYINDA, 20 Hz UDP); can/skor/kurallar/maç
fazları SUNUCU-otoriter (.NET `Server/`, mod kuralları `IGameMode`). Vuruş: atıcı raycast →
hit_report → server doğrular → health_update. **Free-roam respawn = konum değil DURUM değişimi**
(fiziksel oyuncu ışınlanamaz): ölüm → `RESPAWN_DELAY` → oyuncu kendi `BaseZone`'una fiziken girince
`revive_request` → sunucu canlandırır (istemci takılırsa `REVIVE_GRACE` ile zorla). Rig'i ASLA taşıma. Keşif zinciri **rolden bağımsız**: komut satırı
`--server-ip` > PlayerPrefs (elle girilmiş) > beacon > StreamingAssets/arena.json — **VR'a adres
verilmediği için pratikte beacon ile otomatik** (bulamazsa sağ kumandada **A×2** ile gizli IP
paneli) · **admin'e adres launcher'ın geçtiği `--server-ip`'ten gelir** (oyun içinde IP sorulmaz).
Adres hiç yoksa/erişilemezse ~3 sn sonra `ConnectionOverlay` hata ekranı (VR + masaüstü, her
sahnede kendini önyükler). DTO'lar `_Shared/Net/Protocol/` — saf C#, server csproj
aynı dosyaları derler; Unity API'si girerse server derlemesi kırılır (bilinçli bekçi).
⚠️ **Eşzamanlı oyuncu/admin KOTASI YOKTUR** (lisanslama geldiğinde eklenecek). Tek tavan
`PLAYER_ID_MAX = 255` ve o ürün kararı değil, `playerId`'nin UDP'de `u8` olması; 16'dan fazla
pozlu oyuncuda snapshot MTU'ya sığan parçalara bölünür (istemcide birleştirme gerekmez).
Yeni bir sayısal sınır eklemek istersen bunu protokol sabiti gibi yazma — `MAX_PLAYERS` tam bu
yüzden kaldırıldı; dev aracı emniyeti gerekiyorsa `DevProcesses.MaxDevBots` gibi yerel bir sabit kullan.

## Akış

Boot(index 0) → **her rolde Lobby** (rol editörde dev penceresinden gelir).
Lobby (VR): roster, ready/takım + **gizli** IP paneli (varsayılan KAPALI; beacon bulamazsa sağ
kumandada **A×2** ile açılır).
**Admin = sahne-içi gözlemci:** IP SORMAZ (adres `--server-ip`'ten gelir), Lobby'den bağlanır ve
**her zaman sunucudaki aktif sahnededir** — `load_match`/`return_to_lobby`/geç katılım admin'de de
sahne yükler (`SceneRouter` rolden bağımsız; yalnız `set_ready` player'a özel). Sahneyi
`AdminSpectator` devralır: BB rig kapanır, `ArenaCalibrator`/`BaseZone` kapanır, `ArenaBoundary`
**kapatılmaz** `SetSpectatorMode(true)` ile susturulur (kapatmak arena origin'ini siler!), kendi
kamerası + `AudioListener`'ı gelir. Üç kamera kipi: POV · serbest (WASD/QE + sağ tuş bakış) · kuş
bakışı (halka + ad etiketi; sahnede `ArenaRoof` varsa çatı bu kipte kalkar). Yönetim sahne üstü
HUD'dan: skorlar + ortada istatistik chip'i, takım kolonları (FFA'da tek kolon), tercihler paneli
(mod/harita + başlat/iptal/lobiye dön + görünüm + bağlantı) — paneller **yarı saydam**, arkadaki
sahne izlenmeye devam eder.
Harita seçimi değişince, maç başlamamışsa (faz Lobby) o arena admin'de **yerel olarak hemen
açılır** (önizleme; sunucuya maç komutu gitmez). Sunucu **oyuncusuz da maç başlatır** — boş arenayı
gezmek için (konsolda uyarı; §10.1).
**Çoklu admin sınırsızdır** (aynı PC'de birden çok pencere dahil — admin `deviceId`'si oturumluk
GUID taşır, aksi hâlde ikisi birbirini sonsuz kick ederdi) ve hepsi eş yetkilidir. Ayrım net:
**operasyonel durum ORTAK** (mod/harita seçimi sunucuda yaşar → `set_selection` gider, `admin_state`
herkese döner; biri haritayı değiştirince diğerinin paneli ve yerel önizlemesi de değişir),
**görünüm tercihleri YEREL** (kamera kipi, seçili oyuncu, halkalar, duvar/çatı saydamlığı —
`PlayerPrefs`). Her admin eylemi diğerlerinin HUD'ında "kim ne yaptı" satırı olur. ⚠️ Yeni bir
admin ayarı eklerken önce şunu sor: **operatörler arasında ortak mı, ekrana mı ait?** — ortaksa
`AdminSelection`/protokol, ekrana aitse `AdminSession`.
Masaüstü zinciri: **`launcher/` (Flutter) → admin exe'yi `--server-ip` ile başlatır**. Sunucu
hiçbir yerden otomatik başlatılmaz, her zaman elle çalıştırılır.
Arena sahneleri kendine yeten (kendi BB rig'i taşır).

## Yeni içerik ekleme reçeteleri

**Yeni arena:** `Tools > VortexArena > Create Arena From Template` → arenaId + sahne adı + boyut +
takım başına spawn + hedef (Standard / Venue). Sihirbaz: klasörleri (`{Scenes,Data,Prefabs}`) ve
sahne kopyasını üretir, duvar/zemin/taban/spawn'ları yeni boyuta göre ölçekler, MapDefinition
asset'ini yazar, GameCatalog + uyumlu ModeDefinition'lara ekler, Build Settings'e koyar
(sahne adı = katalog anahtarı). Duvar/cover sanat rötuşu ELDE; sonrasında
`Tools > VortexArena > Export Server Config` çalıştır (sunucu `maps.json` tazelensin).
**Yeni mod:** `Assets/Modes/<Ad>/Scripts/VortexArena.Modes.<Ad>.asmdef` (refs: Core, Net, Protocol;
mevcut moddan JSON kopyala, name değiştir, .meta KOPYALAMA) + server tarafında `Modes/<Ad>Mode.cs`
(IGameMode) + `MatchDirector.RegisterModes()`'a bir satır + `Docs/ArenaNet-Protokol.md`'ye modId ekle.
Üç ek adım:
1. **`IGameMode.Rules`** — modun şekli (`ModeRules`: takım kipi, skor kanalı, dost ateşi, canlanma
   şartı, silah kaynağı, canlanma gecikmesi). Bugünkü TDM davranışı için `ModeRules.TeamDefault`
   tek satırdır; yalnız FARKLI olan alanı yaz. Bu kural `load_match.rules` ile istemciye gider ve
   `ModeRuntime` üzerinden okunur → **istemcide `if (modeId == …)` zinciri YAZILMAZ**
   (Docs/ArenaNet-Protokol.md §10.5).
2. **HUD = `ModeHudBase` alt sınıfı** (`_Shared/Core/UI/`). Faz/süre, geri sayım, can, ölüm ekranı,
   kill-feed, kendi sayaçların tabandan gelir; alt sınıf yalnız `ScoreLine`/`WinnerLine` (+ istersen
   `EndScoreLine`/`OnLobbyStateApplied`) yazar. Takıma ait hiçbir şey tabana koyulmaz.
3. **Kural önizlemesi** `ModeDefinition` SO'suna girilir (dev penceresi + sunucusuz editör oturumu
   için) — **otorite sunucudadır, sapmada sunucu kazanır.**
`IGameMode`'a yeni kanca eklerken **varsayılan gövde** kullan (default interface method) ve
**tüketicisi olmayan kancayı hiç ekleme**; skor yalnız `MatchDirector` skor defterinden yazılır
(`AddScore` takım / `AddPlayerScore` bireysel).
**Yeni silah / hasar kaynağı** (mermi, balta, ok, bomba, tuzak): tüfekler
`Tools > VortexArena > Build Weapon Prefabs` ile üretilir — `WeaponKitBuilder` tablosuna satır
ekle (CS2 istatistikleri + ses profili + "Low Poly AR Weapon Pack 1" prefabı), araç
`_Shared/Arsenal/Data/WD_*.asset` + `_Shared/Arsenal/Prefabs/WPN_*.prefab` üretip
**`_Shared/Data/Resources/WeaponCatalog.asset`**'i tazeler (RemoteShotFx `weaponId`→profil
aramasını `Resources.Load` ile yapar — GameCatalog gibi klasöründen ÇIKARILMAZ). Gerekiyorsa
`ModeDefinition.loadout` + sahneye yerleştirme elle. **Sunucu tarafında iş YOKTUR** ve export
gerekmez — sunucuda silah tablosu yok, hasarı (headshot çarpanı dahil) istemci hesaplayıp
`hit_report.damage` ile bildirir, sunucu aynen uygular (§10.3); `weaponId` yalnız kill feed
etiketi, doğrulanmaz. Alan etkisi için etkilenen her hedefe bir `hit_report` yollanır. Denge
sayıları istemcide (WeaponDefinition SO) yaşadığı için değişiklik APK build'i ister.
Şarjör kuralı: boş şarjörde otomatik reload YOK; reload silahı **bel altına indirme jestiyle**
başlar; `reserveMode=DiscardMagazine` (varsayılan) erken reload'da şarjörde kalan mermiyi YAKAR
(`PoolRounds` = CS2 havuz alternatifi SO'dan seçilir).
İçerik kataloğu: **`_Shared/Data/Resources/GameCatalog.asset`**
(ModeDefinition + MapDefinition listesi) — admin tercihler panelinin mod/harita seçicisi bunu
`Resources.Load<GameCatalog>("GameCatalog")` ile okur, bu yüzden `Resources/` altında kalmalı.
**Kar/hava efekti (başka arenaya):** `Arenas/Standard/IceWorld/Prefabs/FX_SnowStorm.prefab`'ı
sahneye arena origin'ine (0,0,0) bırak; kendine yeter (`Snow_C_NearField` üstündeki
`WeatherVolumeFollow` hedefi boşsa `Camera.main`'i bulur). Arena 12×12 değilse `Snow_A/B/E`
shape scale'lerini arena boyutu + ~3 m payla ölçekle — geniş kutu bütçeyi görünmeyen alana harcar.

**Editor araçları** (`VortexArena.Core.Editor`, `VortexArena.Net.Editor`, `VortexArena.App.Editor`
— yalnız Editor):
`Tools > VortexArena > Export Server Config` (MapDefinition SO'larından `Server/config/maps.json`;
deterministik, LF, BOM'suz — **JSON'u elle düzenleme, export ezer**. Silah için gerekmez —
sunucuda silah tablosu yok), **`… > Build Weapon Prefabs`** (`WeaponKitBuilder`: tablodaki 6 silah
için WD_*.asset + WPN_*.prefab + FX_RemoteShot + WeaponCatalog üretir/günceller; idempotent,
dialog açmaz; *…(Yalnız Kataloğu Tazele)* varyantı yalnız katalog+prefab bağlarını yeniler),
`… > Create Arena From
Template`, **`… > Dev`** (`_Shared/App/Scripts/Editor/`: rol · hedef · Play başlangıcı (Boot'tan /
açık sahneden) · sentetik maç parametreleri (mod, takım, spawn slot, raund sn, skor limiti) + test
botu düğmeleri: N Bot · N Bot + Admin · Botları Durdur · Sahipsiz botları temizle ·
Derle (dotnet build) + canlı durum satırı. **Kısayol `Ctrl+Alt+R`**
rolü player↔admin çevirir, pencere açık olmasa da. Seçim `EditorPrefs`'te, hedefler
`dev-targets.json`'da → sahne/asset kirlenmez), `GameObject > VortexArena > Network Parent` (sahne
objesine `NetIdentity` + benzersiz
`sceneId`; sahne kaydında SceneIdGuard 0/çakışan id'leri onarır — dinamik obje senkronu altyapısı),
`GameObject > VortexArena > Arena Roof` (seçime `ArenaRoof` ekler + altındaki Renderer'lara
`ArenaRoof` katmanını damgalar — admin kuş bakışında gizlenecek çatı geometrisi;
çoklu seçim + tek adımda Undo + prefab asset'leri atlar, ayrıntı `Docs/Cati-Gizleme.md` §4),
`PlayerBuildTool.BuildWindowsAdmin` (menü değil — batch-mode `-executeMethod` girişi; sahne listesi
Build Settings'ten gelir, çıktı `-buildOutput` ile verilir; `scripts/deploy-admin-game.bat` çağırır).
⚠️ **Sunucu editörden YÖNETİLMEZ** — dev penceresinde başlat/durdur düğmesi yoktur, sunucu her
zaman elle çalıştırılıp elle kapatılır (editör onu ne başlatır ne öldürür; ad bazlı süpürme de
yalnız PoseBot'a bakar). Pencere yalnız test botlarını **doğrudan exe** ile başlatır (asla
`dotnet run` — yetim süreç 47821'i tutar) ve çıktıyı **borulamaz** (okunmayan boru süreci
kilitler); Play çıkışında botlar ölür.

**Dağıtım:** `scripts\deploy-admin-game.bat` (Unity → `deploy\admin\`; **editör kapalı olmalı** —
batch-mode proje kilidine takılır, ama betik bunu zorlamaz: arka planda kalan `Unity.exe`'ler
yanlış alarm veriyordu, takılırsa elle iptal et) · `scripts\deploy-server.bat` (`dotnet publish` self-contained →
`deploy\server\` + `config/`) · `scripts\deploy-launcher.bat` (Flutter → `deploy\launcher\`;
Windows **Developer Mode** açık olmalı — plugin symlink'leri, betik build öncesi kontrol eder).
Üçü de çift tıklanabilir (sonda `pause`); otomasyonda `--no-pause` / `VORTEX_NO_PAUSE=1`.
Admin build'i Unity'yi **`scripts\lib\watch-unity-build.ps1`** üzerinden çalıştırır: log'u canlı
okuyup aşama/yüzde/hareketsizlik uyarısı basar (batch-mode Unity konsola hiçbir şey yazmaz);
`-ReplayLog <log>` ile bitmiş bir build'in aşama haritası çıkarılır. Çıktısı **ASCII** olmalı.
Betik yazım tuzakları (`call flutter` çağıranı öldürür, değişkenler `VA_` önekli):
`scripts/README.md`. Detay: `deploy/README.md`.
