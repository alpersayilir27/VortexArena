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
- **Editörde rol/adres dev penceresinden seçilir.** `Tools > VortexArena > Development > Dev` (rolü çevirmek için
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
- **Projeyi ajan DERLEMEZ.** Derleme/build/test/Play kullanıcıya aittir — ajan işi bitirir, neyin
  doğrulanması gerektiğini yazar ve durur; kullanıcı açıkça istemedikçe `recompile`/`build`/
  `run_tests`/`dotnet build` çağrılmaz. → `derleme-kullaniciya-aittir.md`
- Doğrulama batch'lenir (`batch-build-verification.md`), istisna geldiğinde editör işi Unity CLI
  ile yapılır (`unity-cli.md`).

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
  Kod-dışı: `Arsenal/` (silah prefab+SO, `VA_WeaponFrame`),
  `FX/`, `Shaders/` + `Materials/` (paylaşılan shader/materyal — ör. `DissolveEffect`;
  **`Materials/Resources/M_BaseZoneXRay.mat`** koddan `Resources.Load` ile alınır ve hiçbir
  sahneden referansı yoktur → `Resources/` altından ÇIKARILMAZ, yoksa shader build'den strip
  edilir ve taban şeridi Quest'te pembe çizilir),
  `Environments/`, `Avatars/` (gövde avatarı modeli ve yerel gövde
  prefabı. Yerel gövde (`Avatars/Resources/LocalBodyAvatar.prefab`) ile uzak avatar
  (`_Shared/App/Prefabs/RemoteAvatar.prefab`) **iki AYRI prefabtır**; ikisinin de AĞ GÖVDESİ aynı
  FBX'tir (`ThirdPartyPackages/MixamoCharacters/Ch15_nonPBR.fbx`), **aynı retarget config'ini ve
  aynı kod yolunu** paylaşırlar — tek davranış farkı
  `ArenaNetCharacterBehaviour.HasInputAuthority`'dir (yerelde gövde body tracking'den çözülür,
  uzakta ağdan gelen iskelet uygulanır).
  ⚠️ **Uzak avatar ayrıca KIRMIZI takımın gövdesini taşır** (`RemoteAvatar.redBodyRoot` →
  `T-Avatars/Ch18_nonPBR.fbx`): iki gövdeden aynı anda yalnız biri çizilir, seçim takıma göredir
  ve **yalnız istemci görselleştirmesidir** — takım zaten `lobby_state` ile geliyor, protokolde ve
  sunucuda karşılığı YOKTUR ve eklenmez. Kırmızı gövde ağdan DEĞİL, karakterin canlı iskeletinden
  `SkeletonPoseMirror` (kemik aynası: ada göre eşleşen kemiklerin `localRotation`'ı) ile sürülür;
  ⚠️ **ikinci modelin mesh'i karakterin iskeletine BAĞLANMAZ** (Mixamo modellerinin kemik adları
  aynı, oranları farklı → deforme gövde) ve ⚠️ **kırmızı gövde karakterin ALTINA asılmaz, KARDEŞİ
  olur** (aşağıdaki retarget kuralının aynısı).
  ⚠️ **Poz aktarımında `HumanPoseHandler` KULLANILMAZ** — `GetHumanPose` dünya uzayında verip
  `SetHumanPose` köke göreli uyguladığı için gövde metrelerce kayar
  (`Docs/Sistem-Ozeti.md` §7 "Tuzaklar", `HumanPoseHandler` maddesi).
  Yeni takım modeli = `TeamBodyBuilder`'daki yol sabitini değiştirip aracı tekrar çalıştırmak;
  tek koşul **kemik adlarının karakterinkiyle eşleşmesidir** (aynı Mixamo rig'i).
  **`LocalBodyAvatar.prefab`** kendini önyükleyen tekil tarafından
  `Resources.Load` ile yüklendiği için `Resources/` altından ÇIKARILMAZ ve ADI DEĞİŞMEZ —
  taşınırsa oyuncu ağa gövde göndermez, yani onu kimse göremez.
  ⚠️ **Yerel gövde HİÇ ÇİZİLMEZ ve ona görsel iş yaptırılmaz** — oyuncunun gözlükte gördüğü eller
  rig'in sentetik elleridir (`VA_CameraRig`). Prefab yalnız ağ kaynağıdır; "görünmüyorsa
  gereksizdir" refleksi tam da bu yüzden tehlikelidir, sileni etkilemez ama başkaları onu göremez.
  ⚠️ **Gövde oranı KALİBRE EDİLMEZ** (`CharacterRetargeter.Calibrate()` çağrılmaz ve o yol geri
  gelmez): gönderenin oranını değiştirmek blob'un eklem uzunluğu sıkıştırmasıyla uyuşmaz ve uzak
  avatarı bozuk duruşlara sokar. Boy farkı tek bir üniform çarpanla taşınır (`bodyScale`,
  `Docs/ArenaNet-Protokol.md` §10.8): ölçümü **operatör** başlatır, `BodyScaleState` ölçer ve
  ölçek YALNIZ uzak avatara uygulanır — yerel karakter ölçek-1 kalır, ölçümün referansı odur.
  ⚠️ Prefabtaki **`EyeAnchor`** (kafa kemiğinin altında, iki gözün arasında) ölçümün referansıdır:
  taşınırsa boy sessizce yanlış ölçülür → `Docs/Sistem-Ozeti.md` §7),
  `Data/` (**`Data/Resources/GameCatalog.asset`** —
  admin arayüzü `Resources.Load` ile okuduğu için klasörden ÇIKARILMAZ),
  `Scenes/` (Boot, Lobby),
  **`App/Resources/UI/`** (⚠️ **arayüzün TAMAMI burada, prefab olarak** — admin HUD'ı + tercihler
  ve istatistik panelleri, oyuncu satırı, oyuncu halkası, bağlantı ekranının iki varyantı,
  yükleme ekranının iki varyantı, cephane göstergesi, kimlik kartı. Kodda görsel kurulum YOKTUR ve yazılmaz: sınıflar yalnız veri
  yazar. `Resources/` altından ÇIKARILMAZ — sahneye konmuyorlar, `Resources.Load` ile
  yükleniyorlar; taşınırsa ilgili arayüz sessizce hiç çizilmez) ve **`App/UI/Sprites/`**
  (yuvarlak köşe + halka görselleri, 9-slice). → `Docs/Gelistirici/Arayuz-Tasarimi.md`
  ⚠️ Ayrı bir admin dashboard sahnesi YOKTUR ve açılmaz — admin
  oyuncularla aynı sahnede duran bir gözlemcidir.
  ⚠️ `_Shared` köküne asmdef'siz gevşek script koyMA (Assembly-CSharp'a düşer, kimse göremez).
- `Assets/Arenas/` altında **yalnız iki kök vardır**: `Venues/` (oynanan içerik) ve `Template/`
  (referans arena). Üçüncü bir kök açma — mekansız arena diye bir şey yoktur.
  - `Assets/Arenas/Venues/<İşletme>/Scenes/<SahneAdı>/` — arena kutusu: `<SahneAdı>.unity` +
    `Data/<SahneAdı>.asset` (MapDefinition) (+ yalnız o sahneye ait sanat/prefab varsa `Art/`,
    `Prefabs/`; ör. `Outdoor12x12/Scenes/IceWorld/`).
    ⚠️ **Klasör adı = sahne dosyası adı = MapDefinition asset adı** — üçünü de aynı yaz. Sahne adı
    zaten katalog anahtarıdır (`load_match` string'i), böylece klasöre bakan anahtarı görür ve isim
    sapması imkansızlaşır. Mekanın **tüm** sahnelerinin paylaştığı sanat/prefab/veri ise mekan
    kökündeki `Art/` · `Prefabs/` · `Data/` klasörlerine girer (ör.
    `VortexAntep/Data/VortexAntep_dimensions.json` = mekanın fiziksel ölçüsü, hem arena hem lobi
    kullanır). ⚠️ Mekan kökünde `Art`, `Data`, `Prefabs`, `Scenes` dışında klasör AÇMA.
  - ⚠️ **Boş klasör açma** (ne sihirbaz ne elle): git klasör tutmaz, dosya tutar → klonda kaybolur,
    geriye yetim `.meta` kalır ve Unity klasörü hayalet olarak geri üretir. Klasör, içine ilk dosya
    girdiğinde açılır.
  - ⚠️ **İşletme klasörü kutu DEĞİL, kutuların kabıdır** — bir işletmede birden çok arena oynatılır;
    hepsi `Venues/<İşletme>/Scenes/` altında yan yana durur (arenalar ve lobi aynı seviyede).
  - Her mekanın **kendi lobi kutusu** olur (`<İşletme>/Scenes/<LobiSahnesi>/`) ve o kutudaki `MapDefinition`'ın
    `supportedModeIds`'i `["lobby"]`'dir — sunucu açık sahneyi bununla bulur (§10.7).
  ⚠️ **Klasör = MEKAN.** Export haritanın mekanını yoldan türetir (`Venues/<İşletme>/…` → o işletme)
  ve sunucu açılışta hangi mekanı oynatacağını sorar; o oturumda yalnız o mekanın haritaları
  başlatılabilir ve adminlere yalnız onlar görünür. Yani **bir arenayı yanlış klasöre koymak onu
  yanlış işletmeye yazar** — `MapDefinition`'da mekan alanı YOKTUR ve eklenmez (ikinci,
  unutulabilir bir doğruluk kaynağı olurdu). Mekan klasörü dışındaki haritalar export'a HİÇ
  girmez (uyarı basılır) → `Docs/ArenaNet-Protokol.md` §11.1
  `Template/Scenes/Default12x12` yalnız **referans** olarak durur; yeni arena boş sahneden başlar ve
  `Template Temellerini Yükle` ile donatılır (sahne kopyalayan sihirbaz YOKTUR).
  ⚠️ `Template/` altındaki haritalar **oynanmaz**: export edilmez, Build Settings'e ve
  `GameCatalog`'a girmez (yoksa sunucu açılışında sahte bir mekan olarak listelenirlerdi).
  Arena = sahne + MapDefinition; arena-özel kod YAZILMAZ (marker bileşenleri Core'dan gelir).
  Bir arenanın ağa bağlanması için sahnede şunlar olmalı:
  `BaseZone`×2 (**taban bölgesi** = kırmızı/mavi şerit; ölen oyuncu buraya girince canlanır,
  `Team.Neutral` = herkese açık; şerit oyuncunun **kendi** takımına duvar arkasından da görünür —
  `BaseZoneVisibility` çalışma anında ekler, sahnede/prefabda kurulum adımı YOKTUR ve eklenmez),
  mekanın **ölçü maketi** (`<Mekan>_DimensionMesh` — kalibrasyon işaretçileri onun altındadır,
  aşağıya bak) ve **altyapı prefabları** (`_Shared/App/Prefabs/`):
  **`VA_ArenaBoundary`** (`ArenaBoundary` = muhafaza; ölçüsü **zorunlu** olarak bağlı boyut
  dosyasından gelir — `dimensionsJson` boşsa muhafaza hata basıp kendini kapatır),
  **`VA_CameraRig`** (kamera rig'i + `OVRComprehensiveInteractionRig` + `ControllerModelHider`,
  tracking origin `Stage`). ⚠️ **Oyuncu kendi gövdesinden HİÇBİR ŞEY görmez; gördüğü eller
  RİG'İN SENTETİK ELLERİDİR** (`OVRHandVisualLeft/Right` → ISDK `SyntheticHand`).
  `LocalBodyAvatar` (kendini önyükleyen tekil, sahneye ve rig'e KONMAZ) yalnız **ağ kaynağıdır**,
  tüm renderer'ları kapalıdır; obje YIKILMAZ, yoksa diğer oyuncular onu göremez.
  ⚠️ **Sentetik ellerin görünmesi `OVRManager.controllerDrivenHandPosesType`'a bağlıdır**
  (prefabda `Natural`) — `None` yapılırsa kumanda tutulurken el verisi hiç üretilmez, `HandVisual`
  mesh'i kendi kapatır ve oyuncu HİÇBİR el görmez. Kumanda modelleri ve mesafeli kavramanın
  hayalet elleri `ControllerModelHider` ile gizlenir; ⚠️ **oyuncunun kendi el görsellerine
  DOKUNULMAZ** (`drivenHandVisuals` listesi tam ad eşleştirir — liste saparsa gerçek eller de
  hayalet sayılıp kapanır ve oyuncu ellerini kaybeder) → `Docs/Sistem-Ozeti.md` §4.
  ⚠️ **Karakter rig'in (ya da başka bir şeyin) ALTINA asılmaz** — retarget çıktısı dünya
  uzayındadır, dolu bir ebeveyn dönüşümü ikinci kez uygulanır. Uzak tarafta kökü
  `ArenaNetCharacterBehaviour` açıkça yazar → `Docs/Sistem-Ozeti.md` §7, "retarget avatarı hareket
  eden kökün altına konmaz" maddesi.
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
  örnek üstünde doldurulur (prefab asset'inde boş durur — normaldir); `anchorA`/`anchorB` boş
  kalırsa kalibratör işaretçileri önce **`DimensionAnchor` bileşeninden** (maketin küpleri),
  o da yoksa **adlarından** (`anchor_a`/`anchor_b`) çözer.
  **Admin gözlemci için ek adım YOKTUR** — `AdminSpectator`
  kendini önyükler ve sahneyi devralır (rig'i kapatır, `ArenaBoundary`'yi susturur).
  ⚠️ **Arena geometrisi DÜNYA ORİJİNİNE göre kurulur** (arena uzayı = dünya uzayı): zemin dünya
  y=0'da, arena merkezi dünya (0,0,0) civarında; `VA_CameraRig`'in kökü de Y=0'da. Sahneyi topluca
  kaydırmak/döndürmek tüm oyuncuların ağ koordinatını kaydırır, zemini yükseltmek herkesi havada
  gösterir. ⚠️ **Harita değişimi ne
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
⚠️ Core'a URP (`Unity.RenderPipelines.Universal.Runtime`) referansı **geri eklenmez**: tek
tüketicisi olan overlay kamera kurulumu silindi (gerekçe `Docs/Sistem-Ozeti.md` §7, "URP overlay
kamerasının near-clip'i XR'da sessizce yok sayılır" maddesi).

**İsimlendirme:** asmdef = `VortexArena.<Katman>`; namespace = asmdef adıyla birebir
(rootNamespace dolu); global namespace'te tip YOK; serialize edilen ikincil tipler kendi
dosyasında (`Team.cs` gibi). Sahne adı = katalog anahtarı (`load_match` string'i) → birebir eşleşme.
⚠️ **Serialize edilen enum'a yeni değer SONA eklenir** — Unity sayısal indeks saklar, başa/ortaya
ekleme sahnelerdeki değerleri kaydırır. `Team = { Red, Blue, Neutral }`: `Neutral` bu yüzden sonda
(`BaseZone` bu enum'u serialize ediyor; orada `Neutral` "herkese açık" demektir).
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

**Yeni arena — altı adım** (tek düğmeli sihirbaz YOKTUR, kaldırıldı):
`File > New Scene` → arena kutusuna kaydet
(`Venues/<İşletme>/Scenes/<SahneAdı>/<SahneAdı>.unity` — klasör adı sahne adıyla AYNI) →
`Tools > VortexArena > Arena > Template Temellerini Yükle` (altyapı prefab ÖRNEKLERİ) →
`… > Arena > JSON'dan DimensionMesh Üret` (mekanın ölçü maketi; ⚠️ **atlanamaz** — kalibrasyon
işaretçileri bu adımda gelir, maketsiz sahne hizalanamaz) → ölçü yanlışsa köşeleri ProBuilder ile
düzelt + `… > Arena > DimensionMesh'i JSON'a Çevir` → environment/asset yerleşimi (**dünya orijinine**,
zemin y=0), bake → `… > Build > Configure All Build Elements`.
⚠️ **Son adım atlanırsa** harita ne katalogda ne `maps.json`'da olur; `start_match` sessizce
reddedilir. ⚠️ **Son adımı sahne AÇIKKEN çalıştır** — MapDefinition kendiliğinden üretilmez, tarama
onu yalnız eksik diye bildirir; modları (`supportedModeIds`) araç penceresinden sen seçersin (boş
bırakmak "kısıtsız" demektir, sahne o hâlde her modda oynanır).
⚠️ **Arena silinince ya da taşınınca aracı tekrar çalıştır** (*Yalnız Senkronize Et* yeter): kayıt
listeleri klasör taramasından eşitlenir, elle temizlenmez. ⚠️ **Ölçekleme YOKTUR ve eklenmez:** her işletmenin alanı farklı ölçüde ve çoğu
kare/dikdörtgen bile değil — orantılı ölçekleme işe yarar bir taslak değil, elle düzeltilecek bir
yalancı-doğru üretir.
**Arena ölçüsü:** tek doğruluk kaynağı **boyut dosyasıdır** (`ArenaDimensions` — elle yazılabilir
JSON) ve dosya **MEKAN başınadır**: `Venues/<İşletme>/Data/<İşletme>_dimensions.json`. Bir
işletmede hep aynı fiziksel alan oynatıldığı için o mekanın **tüm** sahneleri (arenalar + lobi)
`ArenaBoundary.dimensionsJson` alanında aynı dosyayı gösterir — sahne başına kopya kaçınılmaz
olarak sapar. İçerik: `plane` = tabanın sıralı köşeleri (metre, `ArenaBoundary` transformunun
yerel XZ'si, **kapalı** — ilk noktayı sona tekrarlama), `columns` = her biri kendi sıralı köşe
halkası olan kolonlar (`{name, height, points}`), `calibration` = zemin bandının iki noktası
(`{a, b}`).
⚠️ **Taban da kolon da TEK halkadır; parçalardan birleştirme (union) YOKTUR ve eklenmez.**
İçbükeylik için ek bir şey gerekmez — L şekli, yamuk, girintili duvar tek halkayla ifade edilir.
Birleşim `ArenaBoundary` yüzünden çalışma anında da koşmak zorunda kalırdı ve karşılığını mekan
başına yalnız bir kez verirdi.
⚠️ **Kolonun `{"points": […]}` sarmalayıcısı zorunlu** (`JsonUtility` iç içe dizi serialize
etmiyor); `plane` düz `Vector2[]`'dir. ⚠️ **`wallHeight` alanı YOKTUR** — duvar üretimi de
muhafazanın duvar göstergesi de kaldırıldı, okuyanı olmayan ölçü bayatlar.
⚠️ **Boyut dosyası ZORUNLUDUR** — bağlı değilse ya da okunamıyorsa `ArenaBoundary` bir kez hata
basıp muhafazayı tümden kapatır (açık başarısızlık; gerekçe `Docs/Sistem-Ozeti.md` §7).
⚠️ **Bağlanmayan JSON build'e GİRMEZ** (çalışma anında okunur, `TextAsset` referansı yoksa Unity
onu paketlemez). ⚠️ **Ölçü üç yeri birden besler** (muhafaza mesafesi · admin kuş bakışı kadrajı ·
kalibrasyon işaretçilerinin yeri) — ikinci bir yere yazma.
**Kalibrasyon noktaları (`calibration: {a, b}`)** de bu dosyadadır ve **mekan başınadır**: zemine
yapıştırılan A/B bantlarının yeri bir ölçüdür, aynı odadaki tüm arenalar ve lobi aynı iki fiziksel
işareti kullanır. Maketin `anchor_a`/`anchor_b` küpleri **elle taşınmaz** — `ArenaCalibrator`
her `Start`'ta onları buradan konumlandırır. Sahnede taşımanın kalıcı etkisi yoktur, ölçü
**dosyaya** yazılır (düzeltmeyi `DimensionMesh'i JSON'a Çevir` ile geri yaz).
⚠️ **İşaretçinin transform konumu ZEMİN NOKTASIDIR** — küp o noktada merkezlenir, yarısı zeminin
altında kalır. Görselin tabanını zemine hizalayan bir telafi YOKTUR ve eklenmez: tek bir Y
sözleşmesi olmazsa dosyadaki ölçü ile sahnedeki konum sessizce sapar.
⚠️ **Sıra A → B'dir ve geometrik olarak DOĞRULANAMAZ** (iki nokta hangisinin önce alındığını
söylemez, mesafe kontrolü simetriktir): garanti prosedüreldir — ilk yakalama A sayılır, o anda
A işaretçisi yanar. Karıştırılırsa arena 180° ters döner. ⚠️ İki nokta arasında en az
`ArenaDimensions.MinCalibrationSpan` metre olmalı; altındaki çift **yok sayılır** (yaw hatası
mesafeyle ters orantılı büyür).
**Ölçü maketi (`<Mekan>_DimensionMesh`)** oynanan geometri DEĞİLDİR: taban + kolonlar +
`anchor_a`/`anchor_b` işaretçilerinden ibarettir, duvar üretmez. ⚠️ **Maketin kökü ve kalibrasyon
işaretçileri build'e GİRER** (kalibrasyon onlara bağlı) — bu yüzden `EditorOnly` etiketlenmez;
**görsel dal (`Plane` + `Columns`) build'e HİÇ girmez**: `DimensionMeshBuildStripper`
(`IProcessSceneWithReport`) onu build'e giden geçici sahne kopyasından siler, sahne dosyası
değişmez. Gerekçe boyut değil **bağımlılıktır**: çokgenler `ProBuilderMesh` taşır ve o bileşen
`Unity.ProBuilder`'ı runtime'a sokardı. Editör Play kipinde her şey sahnededir, orada görseli
`ArenaDimensionMesh.Awake` yalnız `Renderer.enabled = false` ile gizler (obje kapatılmaz,
işaretçilerin Renderer'larına dokunulmaz — onları kalibrasyon sırasında `ArenaCalibrator` yakar).
Arena sanatı hazır environment'ların içine kurulur ve maket yalnız o sanatın oturacağı fiziksel
alanı gösterir.
⚠️ **Maket SAHNEDEN BAĞIMSIZ üretilir**: sahne köküne, dünya orijininde, **dönüşsüz** ve 1
ölçekte kurulur — hiçbir şeyin altına parent'lanmaz, böylece dosyadaki ölçü sahnede birebir okunur
(döndürülmüş bir kökün altında 12×12 kare, dünya eksenli kutuda `12×(cos θ + sin θ)` görünür ve
araç bozuk sanılır). Arenanın üstüne oturtmak isteyen **elle taşır/döndürür**; geri okuma maketin
KENDİ kökünü referans aldığı için bundan etkilenmez. ⚠️ Maketin **ölçeği değiştirilmez**.
⚠️ **Kalibrasyon işaretçisi TEKTİR ve maketin altındadır** — ikinci bir işaretçi ailesi açma
(sahneye elle `anchor_a` koymak dahil): hangisinin geçerli olduğu belirsizleşir. Adı her yerde
`anchor_a`/`anchor_b`'dir (tek sabit: `ArenaCalibrator.AnchorAName`/`AnchorBName`; C# alanı
`anchorA` aynı adın camelCase yazımıdır ve serialize anahtarı olduğu için değişmez), kimliğini
taşıyan bileşen `DimensionAnchor`'dır (`AnchorKind`) ve kalibratör önce onu arar; ad araması
yalnız maketi olmayan eski sahneler için vardır.
Elle konan engeller için `ArenaObstacle` (`Core/Arena/`): muhafaza onu engel sayar —
⚠️ **collider değildir, fizik yapmaz** (free-roam'da çarpışma yoktur).
⚠️ **Arenanın duvarları environment sanatına aittir** ve fiziksel sınırla **çakışmalıdır**:
muhafazanın yarı saydam duvar göstergesi kaldırıldı, yaklaşma uyarısı artık HMD'ye bağlı karartma
quad'ından geliyor (`warnFadeAlpha`). Sanat duvarı alandan içeride/dışarıda durursa oyuncu yanlış
yere göre kalibre olur.
**Yeni lobi:** lobi de bir arena kutusudur (`Venues/<İşletme>/Scenes/<LobiSahnesi>/`), farkı üç şeydir —
`MapDefinition.supportedModeIds` **yalnız `["lobby"]`** (boş bırakılırsa "kısıtsız" sayılır!),
sahnede `BaseZone` ve `VA_ModeHud` YOK, silah kaynağı `random` (sahneden silah alınmaz, grip'e
basınca elde belirir). **Her mekanın kendi lobisi olur** ve mekanın boyut dosyasını arenayla
**paylaşır** — fiziksel oda aynı, ikinci bir ölçü dosyası açılmaz. Kurulumu arenayla aynı altı
adımdır; `Template Temellerini Yükle` penceresinde taban bölgeleri ve `VA_ModeHud` kutuları
KAPATILIR. `Configure All Build Elements` yeter: sunucu **seçilen mekanın** lobi haritasını kendi
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
1. **`IGameMode.Rules`** — modun şekli (`ModeRules`: takım kipi, skor kanalı, canlanma şartı, silah
   kaynağı, canlanma gecikmesi). Bugünkü TDM davranışı için `ModeRules.TeamDefault` tek satırdır;
   yalnız FARKLI olan alanı yaz. Bu kural `load_match.rules` ile istemciye gider ve `ModeRuntime`
   üzerinden okunur → **istemcide `if (modeId == …)` zinciri YAZILMAZ**
   (Docs/ArenaNet-Protokol.md §10.5).
   ⚠️ **`FriendlyFire` bu listede YOKTUR ve moda YAZILMAZ:** dost ateşi bir mod kuralı değil,
   operatörün canlı anahtarıdır (`set_friendly_fire`; sunucu açılışında kapalı, koşan maçta da
   değişir). Değeri her kural şekline `MatchDirector.ApplyRulesLocked` damgalar — mod kendi
   değerini yazarsa anahtar sessizce ezilir. Aynı sebeple **takımdaş öldürmede `OnKill` hiç
   çağrılmaz** (skor yazılmaz; `kills`/`deaths` işler) — modun bunun için yazacağı bir şey yok.
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
**Tur tabanlı mod** (ör. `tournament`): `Phase` enum'unu BÜYÜTME — turlar modun iç durumudur.
Çekirdeğin üç komutu yeter: `TryPauseForMode(modeState)` · `SetModeState` · `TryStartRound()`
(→ `Docs/Sistem-Ozeti.md` §3.8.2). ⚠️ Tur başında oyuncuyu **`RevivePlayerLocked` ile** canlandır
(`ResetMatchStateLocked` istemciye haber vermez → ölüm ekranında donar) ve "canlanma yok" kuralını
**iki yolda birden** kapat (`revive_request` + `REVIVE_GRACE`).
**Silahı mod dağıtan mod** (`weaponSource:"random"`, ör. FFA): sahnede ya da arenada **hiçbir iş
yoktur**. `WeaponGranter` (`_Shared/Core/Combat/`) kendini önyükleyen kalıcı tekildir — kural
`RandomGrant` **ve ortada kurulmuş bir maç varken** sahnedeki silahları gizler (⚠️ kapı yalnız
"kaynak random mı" DEĞİLDİR: sahnelenen arena lobi profiliyle koşar ve orada tezgâhlar açık kalır —
`random` + `fireWhilePaused` bileşimi "maç yok" demektir; taban şeritleri ONA AİT DEĞİL — onların
kapısı takım kipidir, `BaseZoneVisibility`), grip'e basılı tutulan
her elde `ModeDefinition.loadout`'tan rastgele bir silah tutturur (bırakınca yok olur, tekrar
basınca yenisi gelir; şarjör değiştirme kapalıdır). Silahın eldeki duruşu
`ItemDefinition.primaryGripPosition/Euler`'dan gelir — VR'da ince ayar buradan yapılır ve
**tek yerden**: aynı iki alan hem yerel duruşu, hem uzak çizimi, hem kavrama soketinin yerini
besler (verilen silahta soket çizilmez — silah zaten elde).
⚠️ Sahneye bileşen KOYMA: tekil olmasının sebebi her yeni arenaya elle bir kurulum adımı
eklememektir.
**Yeni silah / hasar kaynağı** (mermi, balta, ok, bomba, tuzak): tüfeklerin kiti
`Tools > VortexArena > Weapons > Build Weapon Prefabs` ile üretilir — `WeaponKitBuilder` tablosuna satır
ekle (CS2 istatistikleri + ses profili + "Low Poly AR Weapon Pack 1" modeli — model üretimde
OKUNMAZ, köken kaydıdır). ⚠️ **Bir satırın `PackPrefab`'ı ve `NetItemId`'si o satırdan
AYRILMAZ** — pack modelleri jenerik adlı (`AR_B`…), hangisinin hangi gerçek silah olduğu gözle
eşlendi; kimliği taşımak istiyorsan satırın geri kalanını taşı. ⚠️ **Ses klipleri yalnız alan
BOŞSA yazılır** (elle sürüklenen klip korunsun diye): mevcut bir silahın sesini değiştiriyorsan
önce `WD_*.asset`'teki klip alanlarını boşalt, yoksa değişiklik sessizce inmez. Araç
`_Shared/Arsenal/Data/WD_*.asset`'i üretir, **mevcut**
`_Shared/Arsenal/Prefabs/WPN_*.prefab`'ı yerinde günceller ve
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
`Docs/Sistem-Ozeti.md` §4). ⚠️ **Kovanın çıkış noktası (`Eject`) elle ayarlanır ve araç onu
TAŞIMAZ** — `Muzzle` ile aynı kural; yalnız hiç yoksa kaba bir başlangıçla üretilir (gerekçe
`Docs/Sistem-Ozeti.md` §7). Gerekiyorsa
`ModeDefinition.loadout`'a eklenir. Kavrama **soketi** kurulum istemez: araç prefaba
`ItemGripSockets`'ı koyar ve **İKİ** yakın-kavrama bileşeninin birden filtre listesine bağlar —
`GrabInteractable` (kumanda hattı) ve `HandGrabInteractable` (el hattı; ikincisini araç üretir).
⚠️ **İkisi birden tutulur ve biri silinmez:** hangisinin koşacağını ISDK rig'i "el izleniyor mu"
sorusuna göre seçiyor (`OVRManager.controllerDrivenHandPosesType`) — tek hat bırakmak o anahtarın
her değişiminde silahı sessizce kavranamaz yapar → `Docs/Sistem-Ozeti.md` §7.
⚠️ **`WPN_*` KÖKÜNE mesafeden kavrama GERİ EKLENMEZ** (araç `DistanceGrabInteractable` ve
`DistanceHandGrabInteractable`'ı bilerek siler: kökte mesafeden kavrama soket
tasarımının zıddıdır ve soket kapısı el çözülemediğinde fail-open olduğu için silah bazı
oturumlarda odanın öbür ucundan kavranabilir kalırdı →
`Docs/Sistem-Ozeti.md` §7). Yasak yalnız kök içindir: **çerçeve prefabında (`VA_WeaponFrame`)
mesafeden kavrama ZORUNLUDUR ve orada da İKİ hat birden durur** — silah oradan alınır.
⚠️ **Çerçevenin el hattında `Hand Alignment` = `None`'dır ve öyle kalır** (varsayılan
`AlignOnGrab`): çerçeve bir kavrama hedefi değil bir SEÇİM tetikleyicisidir, `AlignOnGrab`
sentetik elin bileğini sahnedeki silaha kilitler ve oyuncu elini yerdeki silahta görür
→ `Docs/Sistem-Ozeti.md` §7.
⚠️ **Kavrama pozu düğümlerini araç açar, İÇİNİ insan doldurur:** düğümler ISDK'nın poz listesine
(`_handGrabPoses`) **girmez** — girseydi kavrama skoru poz tabanlı olur ve silah alma hissi
değişirdi; parmak pozu `Kavrama Pozu Stüdyosu`'nda yazılır, yazılmamış poz (düğüm hâlâ silahın
orijininde) sessizce yok sayılır → `Docs/Sistem-Ozeti.md` §7. Çerçeve adımı elle iş istemez, araç her
`WPN_*` köküne bir `VA_WeaponFrame` örneği koyar (idempotent). **Çözülme efekti de kurulum
istemez:** araç aynı köke `SimpleWeaponDissolve` koyup `_Shared/Materials/DissolveEffect.mat`'i
bağlar (yalnız alan BOŞSA — silaha özel materyal bağlanmışsa ezilmez, ikinci seçenek
`VoronoiDissolveEffect`), silah ele çözülerek gelir; **bırakışta efekt YOKTUR**. ⚠️ Efektin
**görünüm ayarları (kenar, desen) bileşende değil MATERYALDE** durur ve araç onlara dokunmaz;
**süresi** ise araçtaki sabitten gelir ve her koşuda prefaba geri yazılır — kalıcı ayar prefabda
değil `WeaponKitBuilder`'da değiştirilir. Aynı sebeple ⚠️ **`Grabbable._throwWhenUnselected` GERİ AÇILMAZ**
(araç kapatır): silahın pozunu ISDK değil `ApplyCanonicalGrip` sürdüğü için bırakış hızı uydurmadır
ve silah elden fırlar. **Silahı sahneye ELLE koyarsın** (`weaponSource:"weaponcanvas"` — TDM ·
turnuva): yerleşim **arena kararıdır**, harita tasarlanırken yapılır ve bunu yapan bir bileşen
YOKTUR (silahı üreten raf sistemi kaldırıldı, yerine ikinci bir üretici gelmez — sahnedeki örnek
ile onu üreten liste iki ayrı doğruluk kaynağı olurdu). ⚠️ Silah sahneye **`WPN_*` prefab ÖRNEĞİ**
olarak konur (kopyalanmaz, unpack edilmez) — kopya sahneye donar ve silah kitinde yapılan tek bir
düzeltme arena sayısı kadar elle iş doğurur. Örnekleri bir `WeaponCanvas` prefabında toplayıp onu
her sahneye `BaseZone` gibi bir örnek olarak koymak yerleşimi tek yerden düzeltilebilir kılar.
⚠️ **Kural `VA_WeaponCanvas`'ın İÇİ için de geçerlidir** (kartların altındaki silahlar da prefab
ÖRNEĞİDİR): orada unpack edilmiş bir kopya, kitin sonradan eklediği bileşenleri hiç almaz ve o
canvas'ı kullanan sahnelerde silah bir gün sessizce alınamaz olur → `Docs/Sistem-Ozeti.md` §7.
Tek satırlık denetim: `Weapon` bileşenini **doğrudan** serialize eden dosyalar yalnız
`_Shared/Arsenal/Prefabs/WPN_*.prefab` olmalıdır.
⚠️ **Bu kaynakta sahnede hangi silahın duracağını `ModeDefinition.loadout` DEĞİL arena belirler** —
moda silah eklemek arenaları değiştirmez, yeni silah her arenaya tek tek konur; `loadout` yalnız
`random` modlarında (FFA, lobi) okunur.
Sahnedeki silah **çerçeve kaynağıdır**: alınmaz ve TÜKENMEZ, `WeaponFrame.maxGrabDistance`
kadarından seçilince ele klonlanır (menzilin tavanı ISDK'nın 5 m'lik mesafe-kavrama konisidir);
çerçeve görselini `WeaponFrame.isFrameVisible` ile **örnek başına** (sahneden sahneye) aç/kapat →
`Docs/Gelistirici/Yemek-Kitabi.md`.
**Sunucu tarafında iş YOKTUR** ve export
gerekmez — sunucuda silah tablosu yok, hasarı (**bölge çarpanı** dahil) istemci hesaplayıp
`hit_report.damage` ile bildirir, sunucu aynen uygular (§10.3); `weaponId` yalnız kill feed
etiketi, doğrulanmaz. Bölge çarpanları `WeaponDefinition`'da (`GetZoneMultiplier`) ve CS2
modelindedir: kafa 4× · karın+leğen 1.25× · bacak 0.75× · gövde ve **kollar** 1×.
⚠️ **`HitZone`'a yeni değer SONA eklenir** (serialize ediliyor) ve `Body` sıfırda kalır —
atanmamış kutu 1× çarpana düşer. Alan etkisi için etkilenen her hedefe bir `hit_report` yollanır. Denge
sayıları istemcide (WeaponDefinition SO) yaşadığı için değişiklik APK build'i ister.
Şarjör kuralı: boş şarjörde otomatik reload YOK; reload silahı **bel altına indirme jestiyle**
başlar; `reserveMode=DiscardMagazine` (varsayılan) erken reload'da şarjörde kalan mermiyi YAKAR
(`PoolRounds` = CS2 havuz alternatifi SO'dan seçilir). Verilen silahta (`random`) reload kapalıdır.
⚠️ **Ağa bildirim TEK kapıdan yapılır: `ArenaCombat`** (`_Shared/Core/Combat/`, statik) —
`ReportShot` · `ReportHit` · `ReportRaycastHit` · `ReportAreaHit` (alan etkisi = hedef başına bir
`hit_report`) + `TryGetTargetPlayerId` · `GetHitZone` · `IsHeadshot` · `CanFire`. Protokol DTO'su kurma, arena
uzayı dönüşümünü elle yazma, `ArenaClient.Send`'i doğrudan çağırma: bir vuruşu doğru bildirmek
dört ayrı şeyi bilmeyi gerektiriyor (arena uzayı, **yön bir nokta değildir**, `RemoteHitBox` ile
hedef çözme, hasarı istemcinin belirlemesi) ve `Weapon` da bu kapıyı kullanıyor. Reçeteler:
`Docs/Gelistirici/Yemek-Kitabi.md`.
İçerik kataloğu: **`_Shared/Data/Resources/GameCatalog.asset`**
(ModeDefinition + MapDefinition listesi) — admin tercihler panelinin mod/harita seçicisi bunu
`Resources.Load<GameCatalog>("GameCatalog")` ile okur, bu yüzden `Resources/` altında kalmalı.
**Kar/hava efekti (başka arenaya):** `Arenas/Venues/Outdoor12x12/Scenes/IceWorld/Prefabs/FX_SnowStorm.prefab`'ı
sahneye arena origin'ine (0,0,0) bırak; kendine yeter (`Snow_C_NearField` üstündeki
`WeatherVolumeFollow` hedefi boşsa `Camera.main`'i bulur). Arena 12×12 değilse `Snow_A/B/E`
shape scale'lerini arena boyutu + ~3 m payla ölçekle — geniş kutu bütçeyi görünmeyen alana harcar.

**Editor araçları** (`VortexArena.Core.Editor`, `VortexArena.Net.Editor`, `VortexArena.App.Editor`
— yalnız Editor). Ne yaptıklarının ayrıntısı `Docs/Sistem-Ozeti.md` §4'te; burada **hangi işi
hangi araç yapar** ve bağlayıcı yasaklar:

| Araç | Ne zaman |
|---|---|
| `Tools > VortexArena > Build > Configure All Build Elements` | Sahne hazır → **Hepsini Yapılandır**: aktif sahnenin `MapDefinition`'ını yazar, sonra `Venues/*/Scenes/*/` taramasıyla `GameCatalog` + dolu `ModeDefinition.maps` + Build Settings + `maps.json`'ı EŞİTLER (eksik = uyarı, fazla/ölü kayıt = silinir; `Boot.unity` index 0'da, mekan-dışı sahneler dokunulmadan kalır). Arena silindi/taşındı → **Yalnız Senkronize Et** (sahne açık olmasa da koşar). ⚠️ Ayrı "Arena Id" alanı YOKTUR: MapDefinition'ın adı sahne adıdır |
| `… > Arena > Template Temellerini Yükle` | Yeni/boş sahneye altyapı prefab ÖRNEKLERİ (`VA_ArenaBoundary`, `VA_CameraRig`, `VA_PoseSync`, `VA_CalibrationManager`, seçime bağlı `VA_ModeHud`/taban bölgeleri) + kalibratör/muhafaza alanlarının rig'e bağlanması + boyut dosyası bağlama. İdempotent. ⚠️ Kalibrasyon işaretçisi KOYMAZ — onlar maketle gelir |
| `… > Arena > JSON'dan DimensionMesh Üret` | Mekanın boyut JSON'undan ölçü maketi (taban + kolonlar + kalibrasyon işaretçileri `anchor_a`/`anchor_b`). **Sahne köküne, dönüşsüz** kurar. İdempotent. ⚠️ Her arenada ZORUNLU adım: sahnenin kalibrasyon işaretçileri burada üretilir |
| `… > Arena > DimensionMesh'i JSON'a Çevir` | Maketin köşeleri/kalibrasyon işaretçileri sahnede düzeltildi → aynı boyut dosyasının ÜSTÜNE yazar (hedefi maketin kendisi söyler). İşaretçi yoksa dosyadaki `calibration` KORUNUR |
| `Tools > VortexArena > Server > Export Server Config` | Yalnız `maps.json` tazelenecekse (`Configure All Build Elements` bunu zaten çağırıyor) |
| `… > Weapons > Build Weapon Prefabs` | `WeaponKitBuilder` tablosuna silah eklendi / ses-VFX-kovan kiti tazelenecek (idempotent; *Yalnız Kataloğu Tazele* varyantı da var). ⚠️ WPN prefabı ÜRETMEZ, **mevcudu** yerinde günceller — gövde/`Muzzle`/**`Eject`** yerleşimi elle ayarlanır ve araç onlara DOKUNMAZ (`Eject` yalnız hiç yoksa üretilir) |
| `… > Weapons > Rebuild Net Item Catalog` | Yeni eşya (silah/bomba) eklendi ya da `netItemId` değişti → kimlikleri doğrular (atanmış + tekil) ve `Resources/NetItemCatalog.asset`'i projedeki TÜM `ItemDefinition`'lardan yeniden yazar. ⚠️ Doğrulama düşerse katalog yazılmaz |
| `… > Weapons > Kavrama Pozu Stüdyosu` | Silahın kavrama pozu yazılacak / elin silahı nasıl sardığı **gözlüksüz** denetlenecek: soketlere hayalet el oturur, işaretçi sürüklenirken takip eder, baş parmak–soket ve avuç–kabza mesafesini cm olarak çizer. Poz düğümü üretir ve karşı ele aynalar. ⚠️ Hiçbir şey YAZMAZ — kalıcı duruş yine `Write Grip Sockets To Definition` ile yazılır |
| `… > Weapons > Write Grip Sockets To Definition` | Sahnedeki kavrama işaretçileri sürüklenip ayarlandı → `WD_*.asset`'e yazar (ters/düz bileşimi araç yapar). Yalnız BULUNAN işaretçinin alanlarına dokunur |
| `… > Avatars > Hayalet Gövdesini Kur` | `RemoteAvatar.prefab`'a ölü/kalibresizde çizilen ayrı gövdeyi kurar: model ÖRNEĞİ + hayalet materyali + `GhostPoseDriver` bağları + `ghostRoot`. İdempotent. ⚠️ Hayalet modelini değiştirmek = araçtaki yol sabitini değiştirip tekrar çalıştırmak, **prefabı elle düzenlemek DEĞİL** (model içi fileID'ler import öncesi bilinemez, elle yazılan bağ sessizce boşa düşer). Modelin tek koşulu **Rig = Humanoid**; iskelet adlarının karakterinkiyle eşleşmesi gerekmez |
| `… > Avatars > Takım Gövdesini Kur` | `RemoteAvatar.prefab`'a KIRMIZI takımın gövdesini kurar: model ÖRNEĞİ (karakterin KARDEŞİ) + `SkeletonPoseMirror` bağları + `redBodyRoot`. İki FBX'in **bind** pozundan kalça referanslarını ve `heightCalibration`'ı (iskelet kolonu oranı) hesaplayıp yazar — bu yüzden ölçü sabit olarak koda YAZILMAZ. İdempotent. Model değiştirmek = araçtaki yol sabitini değiştirip tekrar çalıştırmak (aynı fileID gerekçesi). ⚠️ Çalıştırılmadıkça davranış eskisi gibi: herkes tek gövdeyle çizilir |
| `… > Development > Dev` (`Ctrl+Alt+R`) | Rol/hedef seçimi, Play başlangıcı, **sunucusuz sandbox** (sunucu/admin/kalibrasyon olmadan silah denemek) |
| `GameObject > VortexArena > Network Parent` · `Arena Roof` | Sahneye ilgili bileşeni + kurulumunu ekler |
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
⚠️ **Hedef platform betikte SABİTTİR, aktif platformdan türetilmez** — her betik Unity'yi kendi
hedefiyle başlatır (`-buildTarget Win64` / `Android`) ve platformu build sonunda geri almaz.
Aktif platform hedefe eşit değilse o koşu tam reimport demektir (20-40 dk). İki build birbirinin
cache'ini ısıtmaz (shader/asset/script cache'i platform başınadır) → `Docs/Sistem-Ozeti.md` §7.
⚠️ **İki Unity build'i AYNI sahne listesini kullanır** (Build Settings); platforma göre ayrı liste
tutma — bir arenayı admin bilip oyuncu bilmezse `start_match` sessizce reddedilir.
Betik yazım tuzakları ve aşama izleyici (`watch-unity-build.ps1`): `scripts/README.md`.
Çıktı yerleşimi: `deploy/README.md`.
