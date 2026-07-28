# VortexArena — Proje Talimatları (CLAUDE.md)

Free-roam VR PvP arena ürünü (işletmelere kurulum / LBE; Meta Quest 3 & 3S, Unity 6000.3.20f1, URP).
Oyuncular fiziksel alanda 1:1 yürür; farklı boyutlarda arenalar (10x10, 12x12, işletmeye özel),
farklı oyun modları/haritalar/silahlar. VR build = player, Windows build = admin (yönetim + izleme).
Online haberleşme: kendi .NET sunucumuz (`Server/`, standalone exe, offline LAN) — Mirror/NGO YOK.

> **Dokümanı okumanın yolu: repo kökünde `docs-serve.bat` → http://localhost:1111** (Quartz;
> içerik doğrudan `Docs/`, kaydedince tarayıcı yenilenir. Yeni PC'de bir kez `scripts/docs-setup.bat`;
> motor repo DIŞINDA `../vortexarena-docs-site`, git'e girmez).
> **Oyun tarafını yazan geliştirici için giriş kapısı: `Docs/Gelistirici/`** (İlk Adımlar ·
> **Yemek Kitabı** = reçeteler · API Referansı · Sahne Kurulumu · Yapma Listesi).
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
  `AppBoot`'ta böyle bir alan yoktur ve eklenmez: her rol değişimi sahneyi kirletir.
- **Ağır uygulama işi alt-ajana devredilir** — kullanıcının istemesi beklenmez. Ajanlar
  Opus 5 + medium effort ile koşar (`subagent_type: "uygulayici"`, tanımı
  `.claude/agents/uygulayici.md`). → `delegate-to-subagents.md`
- **AI notu kullanıcının makinesine YAZILMAZ.** Harness bir hafıza dizini
  (`~/.claude/.../memory/`) verse bile kullanılmaz: git'e girmediği için takım göremez.
  Hatırlanacak her şey repoda. → `ai-memory-scope.md`
- Doğrulama batch'lenir (`batch-build-verification.md`), editör doğrulaması Unity CLI ile yapılır
  (`unity-cli.md`).

## Repo üst düzey yerleşim

`Assets/` (Unity) · `Server/` (.NET 10 sunucu kaynağı) · **`launcher/`** (Flutter Windows
launcher — operatör buradan admin oyununu başlatır) · **`scripts/`** (`deploy-admin-game.bat`,
`deploy-server.bat`, `deploy-launcher.bat`, `docs-setup.bat`) · **`docs-serve.bat`** (repo kökü:
doküman sitesini localhost:1111'de sunar; motor repo DIŞINDA `../vortexarena-docs-site`) ·
**`deploy/`** (üretilen çalıştırılabilirler:
`admin/`, `server/`, `launcher/` — **git'e girmez**) · **`dev-targets.json`** (repo kökü,
**commit'li**: dev penceresinin adlandırılmış sunucu hedefi kataloğu + `defaultTarget`/`defaultRole`;
bir hedefin `ip`'si **boşsa** adres yazılmaz, keşif zinciri devralır) ·
`Docs/` · `plan/` · `.claude/rules/`.

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
  `FX/`, `Environments/`, `Avatars/` (**`PlayerBodyAvatar.prefab`** — Mixamo Ch15 + Movement SDK
  CharacterRetargeter'lı yerel gövde avatarı; retarget config JSON'u FBX'in yanında,
  `ThirdPartyPackages/MixamoCharacters/`), `Data/` (**`Data/Resources/GameCatalog.asset`** —
  prosedürel admin arayüzü `Resources.Load` ile okuduğu için klasörden ÇIKARILMAZ),
  `Scenes/` (Boot, Lobby).
  ⚠️ Ayrı bir admin dashboard sahnesi YOKTUR ve açılmaz — admin
  oyuncularla aynı sahnede duran bir gözlemcidir.
  ⚠️ `_Shared` köküne asmdef'siz gevşek script koyMA (Assembly-CSharp'a düşer, kimse göremez).
- `Assets/Arenas/Standard/<AXxX veya TemaAdı>/` ve `Assets/Arenas/Venues/<İşletme>/` — arena kutuları:
  `{Scenes, Data, Prefabs}` (+ arenaya özel sanat varsa `Art/{Materials,Textures}`; ör. `Standard/IceWorld`).
  Arena = sahne + MapDefinition; arena-özel kod YAZILMAZ (marker bileşenleri Core'dan gelir).
  Bir arenanın ağa bağlanması için sahnede şunlar olmalı: `ArenaBoundary` (arena origin + halfExtent),
  `BaseZone`×2 (**taban bölgesi** = kırmızı/mavi şerit; ölen oyuncu buraya girince canlanır,
  `Team.Neutral` = herkese açık), `ArenaCalibrator`, `PlayerPoseTracker`, `RemotePlayerSpawner`,
  `ModeHudSpawner`, BB Camera Rig. **Admin gözlemci için ek adım YOKTUR** — `AdminSpectator`
  kendini önyükler ve sahneyi devralır (rig'i kapatır, `ArenaBoundary`'yi susturur).
  Ayrıca arena başına **tek** `SpawnPoint`: `GameObject > VortexArena > Spawn Point` ile eklenir ve
  ELLE yerleştirilir (sihirbaz üretmez). Yalnız "maçtan önce şurada toplanın" göstergesidir —
  takımı/slotu yoktur, hiçbir kod okumaz, protokolde karşılığı yoktur. ⚠️ **Harita değişimi ne
  oyuncuyu yeniden doğurur ne kalibrasyonu sıfırlar**: `ArenaCalibrator` yeni sahnede kayıtlı
  `OVRSpatialAnchor`'dan hizalamayı geri yükler — ön koşulu bir işletmede hep aynı ölçüde arena
  oynatmaktır (zemin işaretleri sabit kalsın).
  **Çatılı arenada tek isteğe bağlı adım:** çatı kökünde `ArenaRoof`
  (`GameObject > VortexArena > Arena Roof`) — altındaki tüm Renderer'lar çatı sayılır, `ArenaRoof`
  katmanı (user layer 8) damgalanır ve admin kuş bakışına geçince çatı çizilmez (gölgesi kalır).
  Katman yalnız "hangi geometri gizlenecek" sorusunu sahnede görünür kılar; davranış Renderer
  listesinden gelir. Açık tavanlı arenalarda bu adım hiç yapılmaz.
  → tam reçete, editör aracının davranışı, tuzaklar ve sorun giderme: **`Docs/Cati-Gizleme.md`**
- `Assets/Modes/<Mod>/` — mod kutuları: `{Scripts (VortexArena.Modes.<Ad>.asmdef), Data, UI}`.
  Modlar birbirini REFERANSLAMAZ. Ortak HUD/silah kodu mod kutusunda DEĞİL Core'da durur
  (`ModeHudBase`, `WeaponGranter`) — modlar birbirini göremediği için ikinci mod aksi hâlde aynı
  kodu baştan yazardı. (Kayıtlı modlar `Docs/ArenaNet-Protokol.md` §10.5 tablosunda.)
- Üçüncü parti: `Assets/ThirdPartyPackages/`. ⚠️ İstisna: `Assets/Low Poly AR Weapon Pack 1/`
  kökte duruyor; editör AÇIKKEN taşınamaz (OS dosya kilidi). Editör kapalıyken `git mv` ile
  taşınabilir — sonrasında `WeaponKitBuilder.PackRoot` sabitini güncelle (tek satır).

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
(`BaseZone`/`Weapon` bu enum'u serialize ediyor; `BaseZone`'da `Neutral` "herkese açık" demektir).
Aynısı `ModeTeamMode`/`ModeScoreKind`/
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
- **Tracking origin = `Stage` (2), tüm sahnelerde; `AllowRecenter = 0`.** `FloorLevel` ile aynı
  zemin seviyesini verir ama OpenXR'da **recentering'i zorla açar** (`OVRManager`:
  `SetAllowRecentering(true)`), `Stage` kapatır — recenter free-roam'da kalibrasyonu bayatlatıp
  arenayı kaydırır. `AllowRecenter` alanı tek başına yetmez (yalnız OVR'ın kendi çağrısını keser).
- **İşletme başlıklarında guardian/alan kurulumu YAPILMAZ** (serbest dolaşım) → sistemin zemin
  seviyesi tahmindir, bu yüzden zemini kalibrasyon ölçer. **Eğim telafisi yoktur ve eklenmez**
  (iki nokta düzlem tanımlamaz + eğik dünya VR'da mide bulandırır).
  Gerekçelerin tamamı `Docs/Sistem-Ozeti.md` §7, "tracking origin" ve "guardian" maddeleri.

## Network — kod yazarken uyulacak kurallar

> Sistemin ağ **anlatımı** burada DEĞİL: mesajlar/sabitler/portlar/doğrulama
> `Docs/ArenaNet-Protokol.md` (TEK doğruluk kaynağı), akış ve bileşen sorumluluğu
> `Docs/Sistem-Ozeti.md` §3. Burada yalnız **kod yazarken bağlayıcı olan** maddeler durur.

- **Otorite bölünmesi kodun nereye yazılacağını belirler.** Pozlar istemci-otoriter (arena
  uzayında, 20 Hz UDP); can/skor/kural/maç fazı **ve kalibrasyon durumu** sunucu-otoriter. Bir
  kuralı istemcide "de" uygulamak = ikinci doğruluk kaynağı; istemci sunucuyu bekler.
- ⚠️ **Rig'i/kamerayı ASLA taşıma** — free-roam'da oyuncu fiziksel. Canlanma ve harita değişimi
  konum değil **durum** değişimidir.
- ⚠️ **DTO'lar `_Shared/Net/Protocol/` altında saf C# kalır** — server csproj aynı dosyaları
  derler, `UnityEngine` girerse server derlemesi kırılır (bilinçli bekçi).
- ⚠️ **Ağa vuruş bildirimi tek kapıdan: `ArenaCombat`** (`_Shared/Core/Combat/`). Protokol DTO'su
  kurma, arena uzayı dönüşümünü elle yazma, `ArenaClient.Send`'i doğrudan çağırma.
- ⚠️ **Eşzamanlı oyuncu/admin KOTASI YOKTUR** (lisanslama geldiğinde eklenecek). `MAX_PLAYERS`
  gibi bir protokol sabiti **YOKTUR ve eklenmez**; tek tavan `PLAYER_ID_MAX = 255` ve o bir ürün
  kararı değil `playerId`'nin `u8` olmasıdır. Dev aracı emniyeti gerekiyorsa
  `DevProcesses.MaxDevBots` gibi **yerel** bir sabit kullan.
- ⚠️ **Bir oyuncu durumuna savaş kapısı eklerken o durumu değiştiren TÜM yolları ara** — talep
  tabanlı olan (`revive_request`) ile zamanlayıcı tabanlı olan (`REVIVE_GRACE`) ayrı kod
  yollarıdır; birini kapatmak kuralı işlevsiz bırakır. Sunucuya oyuncu için yeni bir ön koşul
  eklediğinde `Server/VortexArena.PoseBot`'un da onu sağladığından emin ol, yoksa test botları
  sessizce işlevsizleşir.
- ⚠️ **Yeni admin ayarı eklerken önce sor: operatörler arasında ORTAK mı, ekrana mı ait?** —
  ortaksa `AdminSelection` + protokol (`admin_state`), ekrana aitse `AdminSession` (`PlayerPrefs`).
  Çoklu admin sınırsızdır ve hepsi eş yetkilidir.
- Portlar: UDP beacon 47820 · WS kontrol 47821 `/ws` · UDP state 47822 (cosmos 47800/1 ile
  çakışmaz).

## Yeni içerik ekleme reçeteleri

**Yeni arena:** `Tools > VortexArena > Create Arena From Template` → arenaId + sahne adı +
hedef (Standard / Venue). ⚠️ **Sihirbaz arena GEOMETRİSİNE DOKUNMAZ — boyut sormaz, ölçekleme
yapmaz.** Yaptığı iş: klasörleri (`{Scenes,Data,Prefabs}`) + kaynak sahnenin bire bir kopyasını
üretir, MapDefinition asset'ini yazar (boyut kaynaktan kopyalanır), GameCatalog + uyumlu
ModeDefinition'lara ekler, Build Settings'e koyar (sahne adı = katalog anahtarı). Değeri
**bileşen bütünlüğü**: kopyalanan sahne ağa bağlanmak için gereken her şeyi hazır taşır
(`ArenaBoundary`, `ArenaCalibrator` + işaretçiler, `PlayerPoseTracker`, `RemotePlayerSpawner`,
`ModeHudSpawner`, `BaseZone`'lar, BB rig). **Ölçekleme bilinçli olarak yoktur ve eklenmez:**
her işletmenin alanı farklı ölçüde ve çoğu kare/dikdörtgen bile değil, plan zaten baştan
çiziliyor — orantılı ölçekleme işe yarar bir taslak değil, elle düzeltilecek bir yalancı-doğru
üretir. ELDE: geometri çizimi · `ArenaBoundary.halfExtentX/Z` + `MapDefinition.size` (**yalnız
istemci** — admin mini haritası + gözlemci kamerası kadraj yedeği) · kalibrasyon işaretçilerinin
yerleşimi (yerleri zemin bandından gelir) · tek `SpawnPoint` · NavMesh/ışık bake. Sonrasında
`Tools > VortexArena > Export Server Config` çalıştır — **yeni `sceneName` `maps.json`'a girsin
diye** (ölçü için değil, oraya arena boyutu yazılmaz).
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
Sonra `FFA.asset` gibi bir `ModeDefinition` yaz (modId, süre/limit, kural alanları, `maps`,
`loadout`, `hudPrefab`), `GameCatalog.asset`'e ekle, oynanacak `MapDefinition`'ların
`supportedModeIds`'ine yeni modId'yi koy ve **`Export Server Config`'i çalıştır** — atlanırsa
`start_match` "harita bu modu desteklemiyor" diye sessizce reddedilir.
`IGameMode`'a yeni kanca eklerken **varsayılan gövde** kullan (default interface method) ve
**tüketicisi olmayan kancayı hiç ekleme**; skor yalnız `MatchDirector` skor defterinden yazılır
(`AddScore` takım / `AddPlayerScore` bireysel).
**Silah rafsız mod** (`weaponSource:"random"`, ör. FFA): sahnede ya da arenada **hiçbir iş yoktur**.
`WeaponGranter` (`_Shared/Core/Combat/`) kendini önyükleyen kalıcı tekildir — kural
`RandomGrant` olunca sahnedeki raf silahlarını ve taban bölgelerini gizler, grip'e basılı tutulan
her elde `ModeDefinition.loadout`'tan rastgele bir silah tutturur (bırakınca yok olur, tekrar
basınca yenisi gelir; şarjör değiştirme kapalıdır). Silahın eldeki duruşu
`WeaponDefinition.grantedHoldPosition/Euler`'dan gelir — VR'da ince ayar buradan yapılır.
⚠️ Sahneye bileşen KOYMA: tekil olmasının sebebi her yeni arenaya elle bir kurulum adımı
eklememektir.
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
(`PoolRounds` = CS2 havuz alternatifi SO'dan seçilir). Verilen silahta (`random`) reload kapalıdır.
⚠️ **Ağa bildirim TEK kapıdan yapılır: `ArenaCombat`** (`_Shared/Core/Combat/`, statik) —
`ReportShot` · `ReportHit` · `ReportRaycastHit` · `ReportAreaHit` (alan etkisi = hedef başına bir
`hit_report`) + `TryGetTargetPlayerId` · `IsHeadshot` · `CanFire`. Protokol DTO'su kurma, arena
uzayı dönüşümünü elle yazma, `ArenaClient.Send`'i doğrudan çağırma: bir vuruşu doğru bildirmek
dört ayrı şeyi bilmeyi gerektiriyor (arena uzayı, **yön bir nokta değildir**, `RemoteHitBox` ile
hedef çözme, hasarı istemcinin belirlemesi) ve `Weapon` da bu kapıyı kullanıyor. Reçeteler:
`Docs/Gelistirici/Yemek-Kitabi.md`.
İçerik kataloğu: **`_Shared/Data/Resources/GameCatalog.asset`**
(ModeDefinition + MapDefinition listesi) — admin tercihler panelinin mod/harita seçicisi bunu
`Resources.Load<GameCatalog>("GameCatalog")` ile okur, bu yüzden `Resources/` altında kalmalı.
**Kar/hava efekti (başka arenaya):** `Arenas/Standard/IceWorld/Prefabs/FX_SnowStorm.prefab`'ı
sahneye arena origin'ine (0,0,0) bırak; kendine yeter (`Snow_C_NearField` üstündeki
`WeatherVolumeFollow` hedefi boşsa `Camera.main`'i bulur). Arena 12×12 değilse `Snow_A/B/E`
shape scale'lerini arena boyutu + ~3 m payla ölçekle — geniş kutu bütçeyi görünmeyen alana harcar.

**Editor araçları** (`VortexArena.Core.Editor`, `VortexArena.Net.Editor`, `VortexArena.App.Editor`
— yalnız Editor). Ne yaptıklarının ayrıntısı `Docs/Sistem-Ozeti.md` §4'te; burada **hangi işi
hangi araç yapar** ve bağlayıcı yasaklar:

| Araç | Ne zaman |
|---|---|
| `Tools > VortexArena > Export Server Config` | `MapDefinition` değişti / yeni arena eklendi → `Server/config/maps.json` |
| `… > Build Weapon Prefabs` | `WeaponKitBuilder` tablosuna silah eklendi (idempotent; *Yalnız Kataloğu Tazele* varyantı da var) |
| `… > Create Arena From Template` | Yeni arena kutusu |
| `… > Dev` (`Ctrl+Alt+R`) | Rol/hedef seçimi, Play başlangıcı, sentetik maç, test botları |
| `GameObject > VortexArena > Network Parent` · `Arena Roof` · `Spawn Point` | Sahneye ilgili bileşeni + kurulumunu ekler |
| `PlayerBuildTool.BuildWindowsAdmin` | Menü değil — batch-mode `-executeMethod` girişi (`deploy-admin-game.bat` çağırır) |

- ⚠️ **`maps.json` elle düzenlenmez** — export ezer. Tek doğruluk kaynağı Unity SO'larıdır.
- ⚠️ **Sunucu editörden YÖNETİLMEZ** — dev penceresinde başlat/durdur düğmesi yoktur; sunucu her
  zaman elle çalıştırılıp elle kapatılır. Pencere yalnız test botlarını yönetir.
- ⚠️ Süreç başlatırken **asla `dotnet run`** (yetim süreç portu tutar) ve **çıktıyı borulama**
  (okunmayan boru süreci kilitler) — gerekçeler `Docs/Sistem-Ozeti.md` §7/13-14.

**Dağıtım:** `scripts\deploy-admin-game.bat` · `deploy-server.bat` · `deploy-launcher.bat`
(üçü de çift tıklanabilir; otomasyonda `--no-pause` / `VORTEX_NO_PAUSE=1`).
⚠️ Admin build'i için **editör kapalı olmalı** (batch-mode proje kilidine takılır; betik bunu
zorlamaz, takılırsa elle iptal et); launcher build'i için Windows **Developer Mode** açık olmalı.
Betik yazım tuzakları ve aşama izleyici (`watch-unity-build.ps1`): `scripts/README.md`.
Çıktı yerleşimi: `deploy/README.md`.
