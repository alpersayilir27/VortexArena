# VortexArena — Proje Talimatları (CLAUDE.md)

Free-roam VR PvP arena ürünü (işletmelere kurulum / LBE; Meta Quest 3 & 3S, Unity 6000.3.20f1, URP).
Oyuncular fiziksel alanda 1:1 yürür; farklı boyutlarda arenalar (12x12, işletmeye özel),
farklı oyun modları/haritalar/silahlar. VR build = player, Windows build = admin (yönetim + izleme).
Online haberleşme: kendi .NET sunucumuz (`Server/`, standalone exe, offline LAN) — Mirror/NGO YOK.

> **Dokümanı okumanın yolu: repo kökünde `docs-serve.bat` → http://localhost:1111** (Quartz;
> içerik doğrudan `Docs/`, kaydedince tarayıcı yenilenir. Yeni PC'de bir kez `scripts/docs-setup.bat`;
> motor repo DIŞINDA `../vortexarena-docs-site`, git'e girmez).
> **Oyun tarafını yazan geliştirici için giriş kapısı: `Docs/Gelistirici/`** (İlk Adımlar ·
> **Yemek Kitabı** = reçeteler · API Referansı · Sahne Kurulumu · **Arayüz Tasarımı** = 2D/UI
> nerede, hangisi prefab · Yapma Listesi).
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

`Assets/` (Unity) · `Server/` (.NET 10 sunucu kaynağı) · **`launcher/`** (.NET 10 WPF Windows
launcher — operatör buradan sunucuyu **mekan seçerek** (`--venue`) ve admin oyununu başlatır;
mekansız sunucu başlatmaz) · **`scripts/`** (`deploy-admin-game.bat`,
`deploy-player-apk.bat`, `deploy-server.bat`, `deploy-launcher.bat`, `docs-setup.bat`) ·
**`docs-serve.bat`** (repo kökü:
doküman sitesini localhost:1111'de sunar; motor repo DIŞINDA `../vortexarena-docs-site`) ·
**`deploy/`** (üretilen çalıştırılabilirler:
`admin/`, `server/`, `launcher/` — **git'e girmez**) · **`dev-targets.json`** (repo kökü,
**commit'li**: dev penceresinin adlandırılmış sunucu hedefi kataloğu + `defaultTarget`/`defaultRole`;
bir hedefin `ip`'si **boşsa** adres yazılmaz, keşif zinciri devralır) ·
`Docs/` · `plan/` · `.claude/rules/`.

**`.gitignore` proje tipi başına ayrıdır** — her biri kendi klasörünü yönetir:
kök = Unity (+ repo geneli OS/IDE) · `Server/` = .NET 10 · `launcher/` = .NET 10 WPF
(Windows-only) · `deploy/` = beyaz liste (`*` + yalnız
README). ⚠️ Köke Unity deseni eklerken **`/` ile sabitle**: `*.sln`/`*.csproj` sabitlenmezse
Server'ın gerçek kaynaklarını, `*.app` ise Windows'ta (`core.ignorecase=true`)
`Server/VortexArena.Server.App/` klasörünü yutar. Alt proje çıktısı (bin/obj)
kökte DEĞİL, ilgili klasörün kendi dosyasında ignore edilir.

## Asset mimarisi (feature-first + asmdef)

- `Assets/_Shared/` — ortak. Ortak KOD yalnız bir asmdef altında: `Core/` (VortexArena.Core),
  `Net/Protocol` (VortexArena.Protocol — saf C#, server aynı dosyaları derler), `Net/Scripts`
  (VortexArena.Net), `App/Scripts` (VortexArena.App — `Admin/` alt klasörü aynı asmdef'te:
  admin gözlemci; `UiKit.cs` arayüz paleti + EventSystem garantisi — görünüm prefablarda).
  Kod-dışı: `Arsenal/` (silah prefab+SO),
  `FX/`, `Environments/`, `Avatars/` (**`PlayerBodyAvatar.prefab`** — Mixamo Ch15 + Movement SDK
  CharacterRetargeter'lı yerel gövde avatarı; retarget config JSON'u FBX'in yanında,
  `ThirdPartyPackages/MixamoCharacters/`), `Data/` (**`Data/Resources/GameCatalog.asset`** —
  admin arayüzü `Resources.Load` ile okuduğu için klasörden ÇIKARILMAZ),
  `Scenes/` (Boot, Lobby),
  **`App/Resources/UI/`** (⚠️ **arayüzün TAMAMI burada, prefab olarak** — admin HUD'ı + tercihler
  ve istatistik panelleri, oyuncu satırı, oyuncu halkası, bağlantı ekranının iki varyantı,
  cephane göstergesi, kimlik kartı. Kodda görsel kurulum YOKTUR ve yazılmaz: sınıflar yalnız veri
  yazar. `Resources/` altından ÇIKARILMAZ — sahneye konmuyorlar, `Resources.Load` ile
  yükleniyorlar; taşınırsa ilgili arayüz sessizce hiç çizilmez) ve **`App/UI/Sprites/`**
  (yuvarlak köşe + halka görselleri, 9-slice). → `Docs/Gelistirici/Arayuz-Tasarimi.md`
  ⚠️ Ayrı bir admin dashboard sahnesi YOKTUR ve açılmaz — admin
  oyuncularla aynı sahnede duran bir gözlemcidir.
  ⚠️ `_Shared` köküne asmdef'siz gevşek script koyMA (Assembly-CSharp'a düşer, kimse göremez).
- `Assets/Arenas/` altında **yalnız iki kök vardır**: `Venues/` (oynanan içerik) ve `Template/`
  (sihirbaz kaynağı). Üçüncü bir kök açma — mekansız arena diye bir şey yoktur.
  - `Assets/Arenas/Venues/<İşletme>/<Arena>/` — arena kutusu: `{Scenes, Data}` (+ yalnız o arenaya
    ait sanat/prefab varsa `Art/`, `Prefabs/`; ör. `Outdoor12x12/IceWorld/`). Mekanın **tüm**
    sahnelerinin paylaştığı sanat/prefab/veri ise bir seviye yukarıda, mekan kökündeki
    `Art/` · `Prefabs/` · `Data/` klasörlerine girer (ör. `VortexAntep/Data/vortexantep_dimensions.json`
    = mekanın fiziksel ölçüsü, hem arena hem lobi kullanır).
  - ⚠️ **Boş klasör açma** (ne sihirbaz ne elle): git klasör tutmaz, dosya tutar → klonda kaybolur,
    geriye yetim `.meta` kalır ve Unity klasörü hayalet olarak geri üretir. Klasör, içine ilk dosya
    girdiğinde açılır.
  - ⚠️ **İşletme klasörü kutu DEĞİL, kutuların kabıdır** — bir işletmede birden çok arena oynatılır
    (`Venues/Outdoor12x12/IceWorld/`, `.../A12x12/`, `.../Lobby/`).
  - Her mekanın **kendi lobi kutusu** olur (`<İşletme>/Lobby/`) ve o kutudaki `MapDefinition`'ın
    `supportedModeIds`'i `["lobby"]`'dir — sunucu açık sahneyi bununla bulur (§10.7).
  ⚠️ **Klasör = MEKAN.** Export haritanın mekanını yoldan türetir (`Venues/<İşletme>/…` → o işletme)
  ve sunucu açılışta hangi mekanı oynatacağını sorar; o oturumda yalnız o mekanın haritaları
  başlatılabilir ve adminlere yalnız onlar görünür. Yani **bir arenayı yanlış klasöre koymak onu
  yanlış işletmeye yazar** — `MapDefinition`'da mekan alanı YOKTUR ve eklenmez (ikinci,
  unutulabilir bir doğruluk kaynağı olurdu). Mekan klasörü dışındaki haritalar export'a HİÇ
  girmez (uyarı basılır) → `Docs/ArenaNet-Protokol.md` §11.1
  **Her yeni arenanın kaynağı `Template/Default12x12`'dir** — harita dizaynı taşımayan, yalnız ağa
  bağlanmak için gerekenleri içeren TEK KAYNAK arena; sihirbazın varsayılan kaynağı da odur.
  ⚠️ `Template/` altındaki haritalar **oynanmaz**: export edilmez, Build Settings'e ve
  `GameCatalog`'a girmez (yoksa sunucu açılışında sahte bir mekan olarak listelenirlerdi).
  Arena = sahne + MapDefinition; arena-özel kod YAZILMAZ (marker bileşenleri Core'dan gelir).
  Bir arenanın ağa bağlanması için sahnede şunlar olmalı: `SpawnPoint` (**arena uzayının sıfırı**),
  `ArenaBoundary` (muhafaza; ölçüsü **zorunlu** olarak bağlı boyut dosyasından gelir —
  `dimensionsJson` boşsa muhafaza hata basıp kendini kapatır),
  `BaseZone`×2 (**taban bölgesi** = kırmızı/mavi şerit; ölen oyuncu buraya girince canlanır,
  `Team.Neutral` = herkese açık) ve **altyapı prefabları** (`_Shared/App/Prefabs/`):
  **`VA_CameraRig`** (kamera rig'i + `OVRComprehensiveInteractionRig` + `ControllerModelHider`,
  tracking origin `Stage`). ⚠️ **Yerel gövde avatarı YOKTUR ve rig'e geri eklenmez** — oyuncu
  kendinden yalnız BB rig'inin ellerini görür (`OVRHandVisualLeft/Right`; kumanda modelleri ve
  sentetik el kopyaları `ControllerModelHider` ile gizli). Movement SDK'nın retarget çıktısı
  dünya uzayında olduğu için kalibre edilen rig'le çakışıyordu →
  `Docs/Sistem-Ozeti.md` §7, "retarget avatarı hareket eden kökün altına konmaz" maddesi;
  yerel kol görselinin doğru yolu `plan/yerel-kol-gorseli.md`.
  **`VA_PoseSync`** (`PlayerPoseTracker` + `RemotePlayerSpawner`),
  **`VA_CalibrationManager`** (`ArenaCalibrator`), **`VA_ModeHud`** (`ModeHudSpawner`).
  ⚠️ **Altyapı sahneye PREFAB ÖRNEĞİ olarak konur — kopyalanmaz, unpack edilmez:** kopya konursa
  rig/kalibrasyon kurulumundaki tek bir düzeltme arena sayısı kadar elle iş doğurur. Aynı sebeple
  sahneye **Building Blocks rig'i ya da ayrı bir `OVRComprehensiveInteractionRig` EKLENMEZ**: BB
  kurulumu prefabı otomatik unpack eder (`CameraRigBBBlockData`) ve ikisi de zaten `VA_CameraRig`
  içindedir. ⚠️ **`VA_CameraRig`'de yapay hareket KAPALIDIR ve açılmaz** (kumandayla yürüme,
  eksende dönme, adımlama, ışınlanma): free-roam'da hareket yalnız fizikseldir. Rig'e locomotion
  geri gelirse sebebi neredeyse her zaman sahneye elle eklenmiş bir BB rig'idir →
  `Docs/Sistem-Ozeti.md` §7, "rig'i/kamerayı asla taşıma" maddesi.
  `VA_CalibrationManager`'ın `anchorA`/`anchorB`/`rigRoot` alanları sahneye baktığı için
  örnek üstünde doldurulur (prefab asset'inde boş durur — normaldir).
  **Admin gözlemci için ek adım YOKTUR** — `AdminSpectator`
  kendini önyükler ve sahneyi devralır (rig'i kapatır, `ArenaBoundary`'yi susturur).
  Arena başına **tek** `SpawnPoint` (`GameObject > VortexArena > Spawn Point`): hem "maçtan önce
  şurada toplanın" göstergesi hem **arena uzayının sıfırıdır** — ağa giden/gelen tüm pozlar ona göre
  çevrilir. Takımı/slotu yoktur ve protokolde karşılığı yoktur (kimse oraya ışınlanmaz).
  ⚠️ **Bir kez yerleştirilir, sonra TAŞINMAZ:** taşımak tüm oyuncuların arenadaki koordinatını
  kaydırır. ⚠️ **Zemin seviyesinde durmalıdır** — `ThreePointBodyIK` zemin Y'sini
  `ArenaSpace.ArenaToWorld(Vector3.zero).y`'den türetir, marker havadaysa avatarların ayağı havada
  kalır. Yoksa origin hiç kaydolmaz: dünya=arena kabul edilir (uzak oyuncular dünya orijininde
  toplanır) ve konsola uyarı düşer — lobide bu normaldir. ⚠️ **Harita değişimi ne
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
`includePlatforms:["Editor"]` + kendi runtime'ını referanslar; `Core.Editor` ayrıca
`Unity.ProBuilder` referanslar (plandan arena geometrisi üretimi — yalnız editörde,
runtime'a ProBuilder BULAŞMAZ).
App ayrıca `Unity.InputSystem` referanslar (gözlemci kamerası klavye/fare + `InputSystemUIInputModule`);
proje **Input System-only** — `StandaloneInputModule` runtime'da patlar, kullanılmaz.
Core ayrıca `Unity.RenderPipelines.Universal.Runtime` referanslar (`LocalBodyOverlayCamera` —
yerel oyuncunun gövdesini ayrı bir kamerayla, farklı near-clip ile çizen URP camera stack kurulumu).

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
  (XRI kurulu, yedek). Hedef YALNIZ Quest 3/3S. Sahnelerde `VA_CameraRig` prefabı kullanılır
  (BB rig'i sahneye eklenmez — gerekçe "Asset mimarisi" altında).
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
- ⚠️ **İzleme/ağdan gelen rotasyon humanoid kemiğe DOĞRUDAN yazılmaz** — `HandGripConvention`
  köprüsünden geçer (`Docs/Sistem-Ozeti.md` §7, "izleme/ağ uzayından gelen rotasyon" maddesi).
- ⚠️ **DTO'lar `_Shared/Net/Protocol/` altında saf C# kalır** — server csproj aynı dosyaları
  derler, `UnityEngine` girerse server derlemesi kırılır (bilinçli bekçi).
- ⚠️ **Ağa vuruş bildirimi tek kapıdan: `ArenaCombat`** (`_Shared/Core/Combat/`). Protokol DTO'su
  kurma, arena uzayı dönüşümünü elle yazma, `ArenaClient.Send`'i doğrudan çağırma.
- ⚠️ **Eşzamanlı oyuncu/admin KOTASI YOKTUR** (lisanslama geldiğinde eklenecek). `MAX_PLAYERS`
  gibi bir protokol sabiti **YOKTUR ve eklenmez**; tek tavan `PLAYER_ID_MAX = 255` ve o bir ürün
  kararı değil `playerId`'nin `u8` olmasıdır. Dev aracı emniyeti gerekiyorsa **yerel** bir sabit
  kullan, protokole sabit ekleme.
- ⚠️ **Bir oyuncu durumuna savaş kapısı eklerken o durumu değiştiren TÜM yolları ara** — talep
  tabanlı olan (`revive_request`) ile zamanlayıcı tabanlı olan (`REVIVE_GRACE`) ayrı kod
  yollarıdır; birini kapatmak kuralı işlevsiz bırakır.
- ⚠️ **Yeni admin ayarı eklerken önce sor: operatörler arasında ORTAK mı, ekrana mı ait?** —
  ortaksa `AdminSelection` + protokol (`admin_state`), ekrana aitse `AdminSession` (`PlayerPrefs`).
  Çoklu admin sınırsızdır ve hepsi eş yetkilidir.
- **Protokol değişikliği bir MALİYET KALEMİ DEĞİLDİR.** Tel formatı gerektiğinde değişir:
  `PROTOCOL_VERSION` artar, tüm başlıklara yeni APK kurulur — bu normal iş akışıdır, plana ayrı bir
  yük olarak yazılmaz ve **tasarım ondan kaçınmak için eğilmez** (karışık sürüm çalışsın diye ek kod
  yolu, ya da "tek APK turu olsun" diye kapsam kesme/faz birleştirme yok). Karışık sürümün *bozuk
  çizim* ürettiği durumlar yine yazılır — bilgi olarak, kısıt olarak değil.
- Portlar: UDP beacon 47820 · WS kontrol 47821 `/ws` · UDP state 47822 (cosmos 47800/1 ile
  çakışmaz).

## Yeni içerik ekleme reçeteleri

**Yeni arena:** `Tools > VortexArena > Create Arena From Template` → arenaId + sahne adı +
**mekan** (kutu her zaman `Venues/<İşletme>/<arenaId>/` altına açılır; mekan ZORUNLUDUR).
**Kaynak varsayılanı `Template/Default12x12`'dir** — dizaynlı bir arenadan türetmek o arenanın
geometrisini de kopyalar ve elle temizlemek gerekirdi. Sihirbazda bir **geometri kaynağı**
(`ArenaGeometrySource`) seçilir ve **kaynak ZORUNLUDUR** — "geometriye dokunmadan kopyala"
seçeneği YOKTUR: boyut dosyası (JSON) ya da TestMesh kökü. İkisi de aynı kapıdan geçer; TestMesh
seçilirse araç önce ondan bir boyut JSON'u üretir (arena kutusunun `Data/` klasörüne). Her iki
yolda da `ArenaBoundary.dimensionsJson` + `wallRenderers` otomatik bağlanır.
⚠️ **Hiçbir kaynakta ölçekleme yapılmaz.**
Yaptığı iş: klasörleri (`{Scenes,Data}`) + kaynak
sahnenin bire bir kopyasını üretir, MapDefinition asset'ini yazar, GameCatalog + uyumlu
ModeDefinition'lara ekler, Build Settings'e koyar (sahne adı = katalog anahtarı). Değeri
**bileşen bütünlüğü**: kopyalanan sahne ağa bağlanmak için gereken her şeyi hazır taşır
(`ArenaBoundary`, kalibrasyon işaretçileri, `BaseZone`'lar + altyapı prefablarının örnekleri —
`VA_CameraRig`, `VA_PoseSync`, `VA_CalibrationManager`, `VA_ModeHud`). ⚠️ **Farklı ÖLÇÜDEKİ arena Default12x12'den
türetilmez** (10×10 bir arena 12×12 duvar/zeminle gelir) — o ölçü için kendi `Default`'unu kur.
**Ölçekleme bilinçli olarak yoktur ve eklenmez:**
her işletmenin alanı farklı ölçüde ve çoğu kare/dikdörtgen bile değil, plan zaten baştan
çiziliyor — orantılı ölçekleme işe yarar bir taslak değil, elle düzeltilecek bir yalancı-doğru
üretir. ELDE: alanın ölçüsünü boyut dosyasına yazmak (ya da TestMesh'i modellemek) ·
kalibrasyon işaretçilerinin
yerleşimi (yerleri zemin bandından gelir) · tek `SpawnPoint` · **raf kipinde oynanacaksa silah rafı**
(raf kökünde `WeaponRackSpawner`, altında konum tutan `RackSlot` gözleri — şablon dizayn taşımadığı
için raf içermez) · NavMesh/ışık bake. Sonrasında
`Tools > VortexArena > Export Server Config` çalıştır — **yeni `sceneName` `maps.json`'a girsin
diye** (ölçü için değil, oraya arena boyutu yazılmaz).
**Arena ölçüsü:** tek doğruluk kaynağı **boyut dosyasıdır** (`ArenaDimensions` — elle yazılabilir
JSON, `Venues/<İşletme>/…/Data/<ad>_dimensions.json`): `outline` = sıralı köşeler (metre,
`ArenaBoundary` transformunun yerel XZ'si, **kapalı** — ilk noktayı sona tekrarlama), `columns` =
kolonlar. Sahnede `ArenaBoundary.dimensionsJson` alanına bağlanır ve çalışma anında okunur.
⚠️ **Alan tam kare/dikdörtgen bile olsa dört köşeli bir `outline` olarak yazılır** — "dikdörtgense
şu hızlı yol" ayrımı ve ona ait alanlar (`halfExtentX/Z`, `rectCenter`) YOKTUR ve geri eklenmez:
aynı ölçünün iki ayrı ifadesi kaçınılmaz olarak birbirinden sapıyordu.
⚠️ **Boyut dosyası ZORUNLUDUR** — bağlı değilse ya da okunamıyorsa `ArenaBoundary` bir kez hata
basıp muhafazayı tümden kapatır (açık başarısızlık; gerekçe `Docs/Sistem-Ozeti.md` §7).
⚠️ **Bağlanmayan JSON build'e GİRMEZ** (çalışma anında okunur, `TextAsset` referansı yoksa Unity
onu paketlemez). ⚠️ **Ölçü üç yeri birden besler** (geometri · muhafaza mesafesi · admin kuş bakışı
kadrajı) — ikinci bir yere yazma.
Dosya değişince `Tools > VortexArena > Build Arena From Dimensions` (seçimde boyut JSON'u)
geometriyi yeniden üretir (idempotent).
**Elde modellenmiş kaba bir alan varsa** (mekanın fiziksel alanını temsil eden basit
quad/blok yığını = TestMesh) `Tools > VortexArena > Build Arena From TestMesh` ondan bir boyut
JSON'u ÜRETİR ve geometriyi o dosyadan çizer — TestMesh ikinci bir üretim yolu değil, boyut
dosyasının otomatik yazılma biçimidir; ölçü sonradan dosyada elle düzeltilebilir.
Elle konan engeller için `ArenaObstacle` (`Core/Arena/`): muhafaza onu engel sayar —
⚠️ **collider değildir, fizik yapmaz** (free-roam'da çarpışma yoktur).
**Yeni lobi:** lobi de bir arena kutusudur (`Venues/<İşletme>/Lobby/`), farkı üç şeydir —
`MapDefinition.supportedModeIds` **yalnız `["lobby"]`** (boş bırakılırsa "kısıtsız" sayılır!),
sahnede `BaseZone` ve `VA_ModeHud` YOK, silah rafı VAR. **Her mekanın kendi lobisi olur** ve
kaynağı **o mekanın kendi arenasıdır** — fiziksel oda aynı olduğu için geometri birebir tutar
(ölçekleme yoktur). `Export Server Config` yeter: sunucu **seçilen mekanın** lobi haritasını kendi
bulur (`server.json → lobbyScene` yalnız mekanda birden çok lobi varsa doldurulur).
⚠️ `lobby` **kayıtlı bir mod DEĞİLDİR** — sunucuya `IGameMode` olarak eklenmez (`start_match` onu
reddeder, "lobi türünde maç başlamaz" kuralı buradan gelir), `ModeDefinition.lobbyProfile`
işaretlidir ve admin mod seçicisinde görünmez. **Hasarı kapatan şey fazdır** (`hit_report` yalnız
`playing`) — o kapıya dokunma; **ateşe izin veren şey moddur** (`rules.fireWhilePaused`, lobide
`lobbyProfile`'dan türetilir). İkisi ayrı olduğu için lobi "hasarsız atış alanı"dır.
→ `Docs/ArenaNet-Protokol.md` §10.7

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
3. **Kural önizlemesi** `ModeDefinition` SO'suna girilir (kurallar telde gelmediğinde —
   `rules == null` — devreye giren fallback) — **otorite sunucudadır, sapmada sunucu kazanır.**
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
`ItemDefinition.primaryGripPosition/Euler`'dan gelir — VR'da ince ayar buradan yapılır ve
**tek yerden**: aynı iki alan hem yerel duruşu, hem uzak çizimi, hem kavrama soketinin yerini
besler (verilen silahta soket çizilmez — silah zaten elde).
⚠️ Sahneye bileşen KOYMA: tekil olmasının sebebi her yeni arenaya elle bir kurulum adımı
eklememektir.
**Yeni silah / hasar kaynağı** (mermi, balta, ok, bomba, tuzak): tüfeklerin kiti
`Tools > VortexArena > Build Weapon Prefabs` ile üretilir — `WeaponKitBuilder` tablosuna satır
ekle (CS2 istatistikleri + ses profili), araç `_Shared/Arsenal/Data/WD_*.asset`'i üretir,
**mevcut** `_Shared/Arsenal/Prefabs/WPN_*.prefab`'ı yerinde günceller ve
**`_Shared/Data/Resources/WeaponCatalog.asset`**'i tazeler (RemoteShotFx `weaponId`→profil
aramasını `Resources.Load` ile yapar — GameCatalog gibi klasöründen ÇIKARILMAZ).
⚠️ **Araç WPN prefabını YOKTAN ÜRETMEZ** (şablondan kurma yolu silindi: `Muzzle`'ı `Model`'in
altından köke alıyordu, oysa geri tepmenin nişanı da kaldırması `Muzzle`'ın `Model` ALTINDA
kalmasına bağlı). Yeni gövde mevcut bir `WPN_*` kopyalanarak kurulur: `Model` altındaki pack
modelini ve `definition`'ı değiştir, sonra aracı çalıştır. Ses/VFX/kovan
kiti de aynı tablodan (`WeaponSpec`) gelir: silaha özgü ateş/reload/dry-fire klipleri
(`Assets/Audio/Weapons/SFX_<Ad>_*.wav`), namlu alevi (renk/boyut/ömür/koni açısı) + `MuzzleFlash`
altında sub-emitter'lı namlu dumanı (`Smoke`), ve kalibreye göre (762x39/556x45) paylaşılan
`Casing_*.prefab`'a bağlı `ShellEjector` (ateşte kovan fırlatan, `Weapon.Fired`'a abone bileşen —
`Docs/Sistem-Ozeti.md` §4). Gerekiyorsa
`ModeDefinition.loadout`'a eklenir. Kavrama **soketi** kurulum istemez: araç prefaba
`ItemGripSockets`'ı koyar ve `GrabInteractable`'ın filtre listesine bağlar — ⚠️ **prefaba
`DistanceGrabInteractable` GERİ EKLENMEZ** (araç onu bilerek siler: mesafeden kavrama soket
tasarımının zıddıdır ve filtre hover'ı kesmediği için yalan söyleyen bir vurgu bırakırdı →
`Docs/Sistem-Ozeti.md` §7). Aynı sebeple ⚠️ **`Grabbable._throwWhenUnselected` GERİ AÇILMAZ**
(araç kapatır): silahın pozunu ISDK değil `ApplyCanonicalGrip` sürdüğü için bırakış hızı uydurmadır
ve silah elden fırlar. ⚠️ **Sahneye elle `WPN_*` örneği KOYULMAZ:** raf silahlarını
`WeaponRackSpawner` (`_Shared/Core/Combat/`, raf kökünde) kural `Rack` iken loadout'tan üretir —
göz (`RackSlot`) yalnız KONUMU tutar, hangi silahın duracağını mod belirler. Elle konan örnek
sahneye donar ve moda silah eklendiğinde her arenayı tek tek açmak gerekirdi.
**Sunucu tarafında iş YOKTUR** ve export
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
**Kar/hava efekti (başka arenaya):** `Arenas/Venues/Outdoor12x12/IceWorld/Prefabs/FX_SnowStorm.prefab`'ı
sahneye arena origin'ine (0,0,0) bırak; kendine yeter (`Snow_C_NearField` üstündeki
`WeatherVolumeFollow` hedefi boşsa `Camera.main`'i bulur). Arena 12×12 değilse `Snow_A/B/E`
shape scale'lerini arena boyutu + ~3 m payla ölçekle — geniş kutu bütçeyi görünmeyen alana harcar.

**Editor araçları** (`VortexArena.Core.Editor`, `VortexArena.Net.Editor`, `VortexArena.App.Editor`
— yalnız Editor). Ne yaptıklarının ayrıntısı `Docs/Sistem-Ozeti.md` §4'te; burada **hangi işi
hangi araç yapar** ve bağlayıcı yasaklar:

| Araç | Ne zaman |
|---|---|
| `Tools > VortexArena > Export Server Config` | `MapDefinition` değişti / yeni arena eklendi → `Server/config/maps.json` |
| `… > Build Weapon Prefabs` | `WeaponKitBuilder` tablosuna silah eklendi / ses-VFX-kovan kiti tazelenecek (idempotent; *Yalnız Kataloğu Tazele* varyantı da var). ⚠️ WPN prefabı ÜRETMEZ, **mevcudu** yerinde günceller — gövde/`Muzzle` yerleşimi elle ayarlanır |
| `… > Rebuild Net Item Catalog` | Yeni eşya (silah/bomba) eklendi ya da `netItemId` değişti → kimlikleri doğrular (atanmış + tekil) ve `Resources/NetItemCatalog.asset`'i projedeki TÜM `ItemDefinition`'lardan yeniden yazar. ⚠️ Doğrulama düşerse katalog yazılmaz |
| `… > Create Arena From Template` | Yeni arena kutusu — geometri kaynağı ZORUNLU: boyut JSON'u ya da TestMesh kökü (kaynak `ArenaBoundary.dimensionsJson` + `wallRenderers`'ı da bağlar) |
| `… > Build Arena From Dimensions` | Boyut dosyası değişti (seçimde JSON) → zemin/duvar/kolon geometrisini yeniden üretir (idempotent) |
| `… > Build Arena From TestMesh` | Kaba alan bloklarından boyut JSON'u üretilecek → dosyayı yazar, sonra geometriyi ondan çizer (idempotent) |
| `… > Write Grip Sockets To Definition` | Sahnedeki kavrama işaretçileri sürüklenip ayarlandı → `WD_*.asset`'e yazar (ters/düz bileşimi araç yapar). Yalnız BULUNAN işaretçinin alanlarına dokunur |
| `… > Dev` (`Ctrl+Alt+R`) | Rol/hedef seçimi, Play başlangıcı, **sunucusuz sandbox** (sunucu/admin/kalibrasyon olmadan silah denemek) |
| `GameObject > VortexArena > Network Parent` · `Arena Roof` · `Spawn Point` | Sahneye ilgili bileşeni + kurulumunu ekler |
| `GameObject > VortexArena > Grip Socket (Primary/Secondary)` | Seçili silahın altına kavrama işaretçisi üretir (mevcut değerlerden başlatır; aynı türden ikincisini üretmez) |
| `PlayerBuildTool.BuildWindowsAdmin` · `…BuildQuestPlayer` | Menü değil — batch-mode `-executeMethod` girişleri (`deploy-admin-game.bat` / `deploy-player-apk.bat` çağırır) |

- ⚠️ **`maps.json` elle düzenlenmez** — export ezer. Tek doğruluk kaynağı Unity SO'larıdır.
- ⚠️ **Sunucu editörden YÖNETİLMEZ** — dev penceresinin sunucuyla hiç işi yoktur (başlatmaz,
  durdurmaz, derlemez); sunucu her zaman elle çalıştırılıp elle kapatılır, derleme `dotnet build`
  ya da `scripts\deploy-server.bat` ile yapılır.
- ⚠️ Süreç başlatırken **asla `dotnet run`** (yetim süreç portu tutar) ve **çıktıyı borulama**
  (okunmayan boru süreci kilitler) — gerekçeler `Docs/Sistem-Ozeti.md` §7 tuzaklar listesinde.

**Dağıtım:** `scripts\deploy-admin-game.bat` (Windows admin) · `deploy-player-apk.bat`
(Quest oyuncu APK'sı) · `deploy-server.bat` · `deploy-launcher.bat`
(dördü de çift tıklanabilir; otomasyonda `--no-pause` / `VORTEX_NO_PAUSE=1`).
⚠️ **Her iki Unity build'i için editör kapalı olmalı** (batch-mode proje kilidine takılır; betik
bunu zorlamaz, takılırsa elle iptal et). Sunucu ve launcher `dotnet publish` ile self-contained
üretilir — tek ön koşul .NET 10 SDK'dır.
⚠️ **APK build'i aktif platformu Android'e çevirir ve geri almaz** (geri almak ikinci bir
tam reimport demek olurdu) — Windows'tan ilk geçiş 20-40 dk sürer.
⚠️ **İki Unity build'i AYNI sahne listesini kullanır** (Build Settings); platforma göre ayrı liste
tutma — bir arenayı admin bilip oyuncu bilmezse `start_match` sessizce reddedilir.
Betik yazım tuzakları ve aşama izleyici (`watch-unity-build.ps1`): `scripts/README.md`.
Çıktı yerleşimi: `deploy/README.md`.
