# VortexArena — Proje Talimatları (CLAUDE.md)

## ⚠️ Bu dosyanın kuralı — TALİMAT dosyasıdır, anlatım değil

Bu dosya HER oturumda bağlama yüklenir: her satırı kalıcı bir maliyettir ve `Docs/` ile
çakışan her anlatım ikinci bir doğruluk kaynağı üretir.

**Test:** yazacağın cümle *"şunu şöyle yap/yapma"* mı, *"sistem şöyle çalışıyor"* mu?
İkincisinin yeri `Docs/`'tur; buraya en fazla tek satırlık işaret girer.
**Kalıp:** yasağın tek satırı burada kalır, gerekçesinin paragrafı `Docs/Sistem-Ozeti.md`'ye gider.
**Tavan:** bu dosya 400 satırı aşmaz. Aşıyorsa eklenecek şey değil, çıkarılacak şey vardır.

> # ⛔ HER ŞEYDEN ÖNCE: UnityMCP kapısı
> İlk iş **`UnityMCP` ayakta mı** kontrolüdür (`mcp__UnityMCP__manage_editor` →
> `telemetry_status`). Düşerse önce sebebi ayır (`Unity.exe` varsa köprü arızasıdır — sessizce
> esnetme, kullanıcıya söyle). Editör kapalıysa kararı ajan verir: iş Unity verisine
> (prefab/sahne/asset/bileşen/konsol) dokunuyorsa tek çıktı **"MCP'yi çalıştır."**tır ve tahmin
> yürütülmez; dokunmuyorsa (git · `Docs/` · `Server/` · `launcher/` · `scripts/` · saf soru)
> sorulmadan devam edilir, cevabın başına tek satır not düşülür. Kullanıcı *"zorla devam et"*
> derse kural düşer: varsayımlar açıkça yazılır, iş "Unity açılınca doğrulanacaklar" listesiyle
> biter. → `.claude/rules/unity-erisim.md`

Free-roam VR PvP arena ürünü (işletmelere kurulum / LBE; Meta Quest 3 & 3S, Unity 6000.3.20f1, URP).
Oyuncular fiziksel alanda 1:1 yürür; farklı boyutlarda arenalar (12x12, işletmeye özel),
farklı oyun modları/haritalar/silahlar. VR build = player, Windows build = admin (yönetim + izleme).
Online haberleşme: kendi .NET sunucumuz (`Server/`, standalone exe, offline LAN) — Mirror/NGO YOK.

## Doküman giriş kapıları

| Soru | Yer |
|---|---|
| Dokümanı okumak | repo kökünde `docs-serve.bat` → http://localhost:1111 (yeni PC'de bir kez `scripts/docs-setup.bat`) |
| Sistem nasıl çalışıyor, hangi bileşen ne yapar, tuzaklar | `Docs/Sistem-Ozeti.md` (§2 repo · §3 ağ · §4 bileşen · §5-6 kullanım · §7 Tuzaklar) |
| Ağ mesajı/sabit/port/doğrulama | `Docs/ArenaNet-Protokol.md` — **TEK doğruluk kaynağı** |
| Oyun tarafını yazan geliştirici | `Docs/Gelistirici/` (İlk Adımlar · **Yemek Kitabı** = reçeteler · API Referansı · Sahne Kurulumu · Arayüz Tasarımı · Yapma Listesi) |
| Sahadaki operatör (teknik olmayan dil) | `Docs/Kullanim-Kilavuzu.md` |
| Sıradaki planlanmış işler | `plan/` (biten iş dokümanı **silinir**) |
| Çalışma kuralları | `.claude/rules/` |

## Çalışma tarzı (detay `.claude/rules/`)

- **Arama = önce auggie.** `mcp__auggie__codebase-retrieval` birincil bağlam aracıdır; dönen sonuç
  Read/Grep ile teyit edilir (indeks bayat olabilir). Tam simge biliniyorsa doğrudan Grep.
  → `is-akisi.md`
- **Kod değişti = doküman değişti** (AYNI commit). Ağ davranışında sıra **önce
  `Docs/ArenaNet-Protokol.md`, sonra kod**. Hangi değişiklik hangi dokümana gider → `docs-sync.md`
- **AI notu kullanıcının makinesine YAZILMAZ** (harness bir hafıza dizini verse bile): git'e
  girmeyen not takımda yoktur. Hatırlanacak her şey repoda. → `docs-sync.md`
- **Ağır uygulama işi alt-ajana devredilir** — kullanıcının istemesi beklenmez
  (`subagent_type: "uygulayici"`). Kararı verilmemiş iş devredilmez. → `is-akisi.md`
- **Projeyi ajan DERLEMEZ.** Derleme/build/test/Play kullanıcıya aittir; kullanıcı açıkça
  istemedikçe `recompile`/`build`/`run_tests`/`dotnet build` çağrılmaz. Doğrulama batch'lenir.
  → `is-akisi.md`
- **Shell SON basamaktır.** Aynı işi bir MCP tool'u ya da yerleşik araç (Read/Write/Edit/Grep/Glob)
  yapabiliyorsa `Bash`/`PowerShell` çalıştırılmaz; Unity'nin kendi verisi `manage_*` ile okunur —
  **YAML grep'lenmez**, geçici python betiği yazılmaz. Geliştirme makinesi HER ZAMAN Windows.
  → `unity-erisim.md`
- **Editörde rol/adres dev penceresinden seçilir** (`Tools > VortexArena > Development > Dev`,
  `Ctrl+Alt+R`): hedef kataloğu `dev-targets.json` (commit'li), seçim `EditorPrefs`'te kişisel.
  ⚠️ Boot.unity'ye (ya da başka bir sahneye) rol/IP için **[SerializeField] override KOYULMAZ** —
  `AppBoot`'ta böyle bir alan yoktur ve eklenmez. Aynı PC'de player + admin = Multiplayer Play
  Mode, sanal oyuncuya `admin`/`player` tag'i → `Docs/Gelistirici/Ilk-Adimlar.md`

## Repo üst düzey yerleşim

`Assets/` (Unity) · `Server/` (.NET 10 sunucu) · `launcher/` (.NET 10 WPF; operatör sunucuyu
**mekan seçerek** (`--venue`) ve admin oyununu buradan başlatır) · `updater/` + `updater_uploader/`
(Quest OTA — `updater/README.md`) · `scripts/` (`deploy-*.bat`, `docs-setup.bat`,
`defender-exclusions.cmd` — ayrıntı `scripts/README.md`) · `docs-serve.bat` ·
`deploy/` (üretilen çalıştırılabilirler, **git'e girmez** — `deploy/README.md`) ·
`dev-targets.json` (kökte, commit'li) · `Docs/` · `plan/` · `.claude/rules/`.

**`.gitignore` proje tipi başına ayrıdır** (kök = Unity · `Server/` · `launcher/` · `updater/` ·
`deploy/` = beyaz liste). ⚠️ Köke Unity deseni eklerken **`/` ile sabitle** (`*.sln`/`*.csproj`/
`*.app` sabitlenmezse Server'ın kaynaklarını/klasörünü yutar). Alt proje çıktısı (bin/obj) kökte
DEĞİL, ilgili klasörün kendi dosyasında ignore edilir.

## Asset mimarisi (feature-first + asmdef)

**`Assets/_Shared/`** — ortak. Ortak KOD yalnız bir asmdef altında: `Core/` (VortexArena.Core) ·
`Net/Protocol` (VortexArena.Protocol — saf C#, server aynı dosyaları derler) · `Net/Scripts`
(VortexArena.Net) · `App/Scripts` (VortexArena.App; `Admin/` alt klasörü aynı asmdef'te).
Kod-dışı: `Arsenal/` (silah prefab+SO, `VA_WeaponFrame`) · `FX/` · `Shaders/` + `Materials/` ·
`Environments/` · `Avatars/` · `Data/` · `Scenes/` (Boot, Lobby) · `App/Resources/UI/` ·
`App/UI/Sprites/`.

- ⚠️ `_Shared` köküne asmdef'siz gevşek script koyMA (Assembly-CSharp'a düşer, kimse göremez).
- ⚠️ **`Resources/` altındaki şu asset'ler oradan ÇIKARILMAZ ve ADI DEĞİŞMEZ** (hepsi koddan
  `Resources.Load` ile alınır, hiçbirinin sahneden referansı yoktur; taşınırsa ilgili şey sessizce
  hiç çalışmaz/çizilmez): `Materials/Resources/M_BaseZoneXRay.mat` ·
  `Avatars/Resources/LocalBodyAvatar.prefab` · `Data/Resources/GameCatalog.asset` ·
  `Data/Resources/GameSoundBank.asset` · `Data/Resources/ModeAudioRegistry.asset` ·
  `Data/Resources/WeaponCatalog.asset` · `App/Resources/UI/*`.
- ⚠️ **Arayüzün TAMAMI `App/Resources/UI/` altında, prefab olarak.** Kodda görsel kurulum YOKTUR
  ve yazılmaz: sınıflar yalnız veri yazar. → `Docs/Gelistirici/Arayuz-Tasarimi.md`
- ⚠️ Ayrı bir admin dashboard sahnesi YOKTUR ve açılmaz — admin oyuncularla aynı sahnede duran bir
  gözlemcidir (`AdminSpectator` kendini önyükler, ek kurulum adımı yoktur).

**Gövde avatarı** (yerel `Avatars/Resources/LocalBodyAvatar.prefab` + uzak
`App/Prefabs/RemoteAvatar.prefab` — iki AYRI prefab, aynı FBX/retarget/kod yolu):
- ⚠️ **Yerel gövde HİÇ ÇİZİLMEZ ve ona görsel iş yaptırılmaz** — yalnız ağ kaynağıdır; obje
  YIKILMAZ, silinirse oyuncuyu kimse göremez. Oyuncunun gördüğü eller rig'in sentetik elleridir.
- ⚠️ **Poz aktarımında `HumanPoseHandler` KULLANILMAZ** (gövde metrelerce kayar).
- ⚠️ **Gövde oranı KALİBRE EDİLMEZ** (`CharacterRetargeter.Calibrate()` çağrılmaz ve geri gelmez);
  boy farkının tek taşıyıcısı `bodyScale`'dir ve YALNIZ uzak avatara uygulanır.
- ⚠️ Prefabtaki **`EyeAnchor`** boy ölçümünün referansıdır — taşınırsa boy sessizce yanlış ölçülür.
- ⚠️ **Kırmızı takım gövdesi yalnız istemci görselleştirmesidir** — protokolde ve sunucuda karşılığı
  YOKTUR ve eklenmez. ⚠️ İkinci modelin mesh'i karakterin iskeletine BAĞLANMAZ ve kırmızı gövde
  karakterin ALTINA asılmaz, **KARDEŞİ** olur (`SkeletonPoseMirror` sürer).
- ⚠️ **Karakter rig'in (ya da başka bir şeyin) ALTINA asılmaz** — retarget çıktısı dünya uzayındadır.
- Gerekçelerin tamamı: `Docs/Sistem-Ozeti.md` §4 ve §7.

**`Assets/Arenas/`** — altında **yalnız iki kök vardır**: `Venues/` (oynanan içerik) ve `Template/`
(referans; oynanmaz, export/Build Settings/`GameCatalog` dışıdır). ⚠️ Üçüncü kök açma — mekansız
arena diye bir şey yoktur.
- Arena kutusu: `Venues/<İşletme>/Scenes/<SahneAdı>/<SahneAdı>.unity` +
  `Data/<SahneAdı>.asset` (+ yalnız o sahneye ait `Art/`, `Prefabs/`).
  ⚠️ **Klasör adı = sahne dosyası adı = MapDefinition asset adı** (sahne adı katalog anahtarıdır).
- Mekanın **tüm** sahnelerinin paylaştığı sanat/prefab/veri mekan kökündeki `Art/` · `Prefabs/` ·
  `Data/` altına girer. ⚠️ Mekan kökünde bu dördü (+`Scenes/`) dışında klasör AÇMA.
- ⚠️ **Klasör = MEKAN.** Export mekanı yoldan türetir; yanlış klasör arenayı yanlış işletmeye
  yazar. `MapDefinition`'da mekan alanı YOKTUR ve eklenmez. → `Docs/ArenaNet-Protokol.md` §11.1
- ⚠️ **Boş klasör açma** (ne araçla ne elle): git klasör tutmaz, klonda kaybolur ve yetim `.meta`
  kalır. Klasör, içine ilk dosya girdiğinde açılır.
- ⚠️ İşletme klasörü kutu DEĞİL, kutuların kabıdır (arenalar ve lobi aynı seviyede yan yana).
- Arena = sahne + MapDefinition; **arena-özel kod YAZILMAZ** (marker bileşenleri Core'dan gelir).

**Sahnede olması gerekenler** (altyapı `_Shared/App/Prefabs/`): `VA_ArenaBoundary` ·
`VA_CameraRig` · `VA_PoseSync` · `VA_CalibrationManager` · `VA_ModeHud` · `BaseZone`×2 ·
mekanın ölçü maketi (`<Mekan>_DimensionMesh`). Kurulum reçetesi:
`Docs/Gelistirici/Yemek-Kitabi.md`, bileşen davranışları `Docs/Sistem-Ozeti.md` §4.
- ⚠️ **Altyapı sahneye PREFAB ÖRNEĞİ olarak konur — kopyalanmaz, unpack edilmez.** Aynı sebeple
  sahneye **Building Blocks rig'i ya da ikinci bir `OVRComprehensiveInteractionRig` EKLENMEZ**
  (BB kurulumu prefabı otomatik unpack eder).
- ⚠️ **`VA_CameraRig`'de yapay hareket KAPALIDIR ve açılmaz** (yürüme/dönme/adımlama/ışınlanma):
  free-roam'da hareket yalnız fizikseldir.
- ⚠️ **Oyuncunun kendi el görsellerine DOKUNULMAZ** (`ControllerModelHider.drivenHandVisuals`
  listesi saparsa gerçek eller de hayalet sayılıp kapanır) ve ⚠️
  **`OVRManager.controllerDrivenHandPosesType` `None` YAPILMAZ** (oyuncu hiç el görmez).
- ⚠️ **Arena geometrisi DÜNYA ORİJİNİNE göre kurulur** (zemin y=0, arena merkezi ~(0,0,0)):
  sahneyi topluca kaydırmak/döndürmek herkesin ağ koordinatını kaydırır. Hazır bir environment'ın
  içinde oynanacaksa **environment değil `VA_ArenaBoundary` örneği** taşınır.
- ⚠️ `ArenaBoundary.dimensionsJson` **zorunludur** — boşsa/okunamıyorsa muhafaza hata basıp kendini
  kapatır. ⚠️ Bağlanmayan JSON build'e GİRMEZ.
- ⚠️ `BaseZone`'un sınırı **çizilen şeridin kendisidir** (ayrı ölçü alanı yoktur, şeritsiz bölge
  kendini kapatır); `Team.Neutral` = herkese açık. Duvar arkası görünürlük (`BaseZoneVisibility`)
  çalışma anında eklenir — sahnede/prefabda kurulum adımı YOKTUR ve eklenmez.
- Çatılı arenada tek isteğe bağlı adım: çatı kökünde `ArenaRoof`
  (`GameObject > VortexArena > Arena Roof`). Açık tavanlı arenada hiç yapılmaz.

**`Assets/Modes/<Mod>/`** — mod kutuları: `{Scripts (VortexArena.Modes.<Ad>.asmdef), Data, UI}`.
⚠️ Modlar birbirini REFERANSLAMAZ; ortak HUD/silah kodu mod kutusunda DEĞİL Core'da durur
(`ModeHudBase`, `WeaponGranter`). Kayıtlı modlar: `Docs/ArenaNet-Protokol.md` §10.5.

**`Assets/ThirdPartyPackages/`** — ⚠️ buradaki klasörler editör AÇIKKEN taşınmaz (OS dosya
kilidi); taşıma editör kapalıyken `git mv` + `WeaponKitBuilder.PackRoot` sabiti (tek satır).
⚠️ **`.unitypackage` arşivi `Assets/` altına KOPYALANMAZ** — paket Unity'de içe aktarılır; bu
yayıncının pack'leri aynı GUID'leri paylaştığı için ikinci pack ilk pack'in klasörüne yazar
(`Docs/Sistem-Ozeti.md` §7). Yeni pack aramadan önce mevcut pack klasörüne bak.

**Assembly grafiği** (bağımlılık hep aşağı):
`Protocol` (saf C#, noEngineReferences) ← `Net` ← `Core` ← `App`, `Modes.<X>`.
Net oyun/sahne bilgisi içermez; olay yayınlar, App dinler. Editor asmdef'leri
`includePlatforms:["Editor"]` + kendi runtime'ını referanslar; `Core.Editor` ayrıca
`Unity.ProBuilder` (runtime'a ProBuilder BULAŞMAZ), `App` ayrıca `Unity.InputSystem`.
Proje **Input System-only** — `StandaloneInputModule` kullanılmaz.
⚠️ Core'a URP (`Unity.RenderPipelines.Universal.Runtime`) referansı **geri eklenmez**.

**İsimlendirme:** asmdef = `VortexArena.<Katman>`; namespace = asmdef adıyla birebir
(rootNamespace dolu); global namespace'te tip YOK; serialize edilen ikincil tipler kendi dosyasında
(`Team.cs` gibi). Sahne adı = katalog anahtarı (`load_match` string'i) → birebir eşleşme.
⚠️ **Serialize edilen enum'a yeni değer SONA eklenir** (Unity sayısal indeks saklar): `Team`,
`HitZone` (`Body` sıfırda kalır), `ModeTeamMode`, `ModeScoreKind`, `ModeReviveAnchor`,
`ModeWeaponSource`, `ModeAudioEvent`, `HandGripPreset`.

**Paylaşımlı-mı-modül-mü:** "İkinci bir mod/arena bunu aynen kullanır mı?" → evet=_Shared, hayır=kutu.

## XR / Meta politikası

- **Meta-first:** önce Meta Building Blocks + Meta XR SDK; yetmezse XRI (kurulu, yedek).
  Hedef YALNIZ Quest 3/3S. Sahnelerde `VA_CameraRig` prefabı kullanılır.
- ⚠️ **Umbrella paket YASAK** (`com.meta.xr.sdk.all` — Project Setup Tool önerse bile): Android
  namespace çakışmasıyla build kırar. Bireysel paketler: core + interaction + interaction.ovr
  @203.0.0, audio @85.0.0 (pinli, spatializer = Meta XR Audio).
- Haptik: `OVRInput.SetControllerVibration` (core) — ayrı haptics paketi ekleme.
- XR loader: OpenXR — değiştirme.
- ⚠️ **Movement SDK retargeter'ında `ApplyRootScale` KAPALI kalır** (`LocalBodyAvatar` +
  `RemoteAvatar`): açıkken ağa giden gövde kökü dünya orijininden uzaklıkla orantılı kayar.
- ⚠️ **`VA_CameraRig`'in near-clip'i `0.05`'tir ve BÜYÜTÜLMEZ** (üç göz kamerasında da aynı).
- ⚠️ **Tracking origin = `Stage` (2), tüm sahnelerde; `AllowRecenter = 0`.** `FloorLevel`
  OpenXR'da recentering'i zorla açar ve kalibrasyonu bayatlatır; `AllowRecenter` alanı tek başına
  yetmez.
- ⚠️ **İşletme başlıklarında guardian/alan kurulumu YAPILMAZ** (zemini kalibrasyon ölçer) ve
  **eğim telafisi yoktur, eklenmez**.
- Gerekçelerin tamamı: `Docs/Sistem-Ozeti.md` §7.

## Network — kod yazarken uyulacak kurallar

> Ağ **anlatımı** burada DEĞİL: mesaj/sabit/port/doğrulama `Docs/ArenaNet-Protokol.md` (TEK
> doğruluk kaynağı), akış ve bileşen sorumluluğu `Docs/Sistem-Ozeti.md` §3.

- **Otorite bölünmesi kodun nereye yazılacağını belirler.** Pozlar istemci-otoriter (arena uzayında,
  20 Hz UDP); can/skor/kural/maç fazı **ve kalibrasyon durumu** sunucu-otoriter. Bir kuralı
  istemcide "de" uygulamak = ikinci doğruluk kaynağı; istemci sunucuyu bekler.
- ⚠️ **Rig'i/kamerayı ASLA taşıma** — free-roam'da oyuncu fiziksel; canlanma ve harita değişimi
  konum değil **durum** değişimidir.
- ⚠️ **İzleme/ağdan gelen rotasyon humanoid kemiğe DOĞRUDAN yazılmaz** — `HandGripConvention`
  köprüsünden geçer.
- ⚠️ **DTO'lar `_Shared/Net/Protocol/` altında saf C# kalır** — `UnityEngine` girerse server
  derlemesi kırılır (bilinçli bekçi).
- ⚠️ **Ağa vuruş/atış bildirimi TEK kapıdan: `ArenaCombat`** (`_Shared/Core/Combat/`, statik).
  Protokol DTO'su kurma, arena uzayı dönüşümünü elle yazma, `ArenaClient.Send`'i doğrudan çağırma.
  Reçeteler: `Docs/Gelistirici/Yemek-Kitabi.md`.
- ⚠️ **Hitscan ışını `ArenaCombat.TraceShot` ile atılır, elle `Physics.Raycast` ile DEĞİL** (engel
  kuralı ve trigger elemesi orada). ⚠️ Tetiği olan silah ayrıca `IsWeaponBlocked` ile kapatılır;
  ⚠️ oyuncunun kendisi engeldeyken kapı `ArenaCombat.CanFire`'dadır, silahta değil.
- ⚠️ **Eşzamanlı oyuncu/admin KOTASI YOKTUR.** `MAX_PLAYERS` gibi bir protokol sabiti YOKTUR ve
  eklenmez; tek tavan `PLAYER_ID_MAX = 255` (`playerId` `u8` olduğu için). Dev aracı emniyeti
  gerekiyorsa **yerel** sabit kullan.
- ⚠️ **Bir oyuncu durumuna savaş kapısı eklerken o durumu değiştiren TÜM yolları ara** — birini
  atlamak kuralı sessizce işlevsiz bırakır.
- ⚠️ **Yeni admin ayarında önce sor: operatörler arasında ORTAK mı, ekrana mı ait?** — ortaksa
  `AdminSelection` + protokol (`admin_state`), ekrana aitse `AdminSession` (`PlayerPrefs`).
  Çoklu admin sınırsızdır ve hepsi eş yetkilidir.
- **Protokol değişikliği bir MALİYET KALEMİ DEĞİLDİR.** Gerektiğinde `PROTOCOL_VERSION` artar ve
  tüm başlıklara yeni APK kurulur; **tasarım ondan kaçınmak için eğilmez** (karışık sürüm için ek
  kod yolu ya da kapsam kesme yok). Karışık sürümün *bozuk çizim* ürettiği durumlar bilgi olarak
  yazılır, kısıt olarak değil.
- Portlar: UDP beacon 47820 · WS kontrol 47821 `/ws` · UDP state 47822.

## Yeni içerik ekleme — reçete nerede

| İstek | Reçete | Atlanırsa |
|---|---|---|
| **Yeni arena** (altı adım; tek düğmeli sihirbaz YOK) | `Docs/Gelistirici/Yemek-Kitabi.md` §14 | Son adım (`Configure All Build Elements`) atlanırsa harita ne katalogda ne `maps.json`'da olur → `start_match` sessizce reddedilir |
| **Arena ölçüsü / boyut dosyası** | Yemek-Kitabı §17 | Dosya bağlı değilse muhafaza kendini kapatır |
| **Hazır environment içinde arena bölgesi** | Yemek-Kitabı §14.1 | — |
| **Yeni lobi** (arena kutusudur; `supportedModeIds` = **yalnız** `["lobby"]`, `BaseZone`/`VA_ModeHud` YOK, mekanın boyut dosyasını arenayla PAYLAŞIR) | `Docs/Sistem-Ozeti.md` §3.8.1 + §6.4 | `supportedModeIds` boş bırakılırsa "kısıtsız" sayılır ve lobi her modda oynanır |
| **Yeni mod** | Yemek-Kitabı §13 + `Docs/Sistem-Ozeti.md` §3.9 | `Export Server Config` atlanırsa `start_match` "harita bu modu desteklemiyor" der |
| **Yeni silah / hasar kaynağı** | Yemek-Kitabı §11 | Kavrama yazılmazsa el idle'da kalır; ateş sesi atanmazsa silah sessiz doğar (ikisi de koşu sonunda listelenir) |
| **Silahın kavraması** (gözlüksüz, stüdyoda) | Yemek-Kitabı §11.0 | Ön kabza kaydı yoksa soket çizilmez ve ikinci el bağlanmaz |
| **Ortam sesi / duyuru sesi** | `Docs/Sistem-Ozeti.md` §4 (`SceneAmbience`, `GameAudio`+`GameSoundBank`, `ModeAudioRegistry`) | — |
| **Kar/hava efekti** | `Docs/Sistem-Ozeti.md` §6.4 | — |

**Bağlayıcı yasaklar** (gerekçeler `Docs/Sistem-Ozeti.md` §7):

- ⚠️ **Ölçekleme YOKTUR ve eklenmez** — her işletmenin alanı farklı ölçüde; orantılı ölçekleme
  yalancı-doğru üretir. ⚠️ Boyut dosyasında **taban da kolon da TEK halkadır**, birleştirme (union)
  yoktur; `wallHeight` alanı YOKTUR. ⚠️ Ölçü **ikinci bir yere yazılmaz**: dosya MEKAN başınadır,
  mekanın tüm sahneleri (arenalar + lobi) aynı dosyayı gösterir.
- ⚠️ **Kalibrasyon işaretçisi TEKTİR ve maketin altındadır** (`anchor_a`/`anchor_b`); sahneye elle
  ikinci bir işaretçi konmaz, küpler elle taşınmaz (ölçü **dosyaya** yazılır). ⚠️ İşaretçinin
  transform konumu **zemin noktasıdır**, telafi eklenmez. ⚠️ Sıra **A → B**'dir ve geometrik olarak
  doğrulanamaz (karıştırılırsa arena 180° döner); iki nokta arası en az
  `ArenaDimensions.MinCalibrationSpan` metre olmalıdır.
- ⚠️ **Maketin ölçeği değiştirilmez** ve ölçü dünya eksenli seçim kutusundan okunmaz — okunacağı
  yer boyut dosyasıdır. Maket `ArenaBoundary`'nin ALTINA üretilir.
- **İç engelin collider'ı `Obstacle` layer'ına (10) konur.** Sözleşme: *kafa* içeri girerse ihlaldir
  (karartma → 3 sn sonra can erimesi → 8. sn ölüm), *kafa/el/silah* değiyorsa tetik ölür.
  ⚠️ Ceza yalnız kafayı, ateş kapısı kafa+el+silahı yargılar; oran (gövde kütlesi) kuralı YOKTUR
  (`Docs/ArenaNet-Protokol.md` §10.9). ⚠️ Bu layer'daki collider **KONVEKS olmalı**. ⚠️ Dış duvar,
  zemin ve tavan bu layer'a KONMAZ. ⚠️ İkinci bir "mantık hacmi" collider'ı EKLENMEZ.
  ⚠️ Layer 11 (`Breakable`) ve 12 (`PlayerHitbox`) sonraki işlere **rezervedir**.
  `ArenaObstacle` (`Core/Arena/`) ⚠️ collider değildir, fizik yapmaz.
- ⚠️ **Arenanın duvarları environment sanatına aittir** ve fiziksel sınırla **çakışmalıdır**.
- ⚠️ **`lobby` kayıtlı bir mod DEĞİLDİR** — sunucuya `IGameMode` olarak eklenmez, admin mod
  seçicisinde görünmez. **Hasarı kapatan şey fazdır** (`hit_report` yalnız `playing`) — o kapıya
  dokunma; **ateşe izin veren şey moddur** (`rules.fireWhilePaused`).
- ⚠️ **`FriendlyFire` `ModeRules`'a YAZILMAZ** — operatörün canlı anahtarıdır
  (`set_friendly_fire`), değerini `MatchDirector.ApplyRulesLocked` damgalar.
- ⚠️ **İstemcide `if (modeId == …)` zinciri YAZILMAZ** — kural `load_match.rules` ile gelir ve
  `ModeRuntime`'dan okunur. ⚠️ `IGameMode`'a kanca eklerken **varsayılan gövde** kullan ve
  **tüketicisi olmayan kancayı hiç ekleme**; skor yalnız `MatchDirector` defterinden yazılır.
- ⚠️ **Tur tabanlı modda `Phase` enum'unu BÜYÜTME** (turlar modun iç durumudur); tur başında oyuncu
  **`RevivePlayerLocked` ile** canlandırılır (`ResetMatchStateLocked` istemciye haber vermez).
- ⚠️ **`WeaponGranter` sahneye KOYULMAZ** (kendini önyükleyen tekil).
- ⚠️ **Silah sahneye `WPN_*` prefab ÖRNEĞİ olarak konur** — kopyalanmaz, unpack edilmez; kural
  `VA_WeaponCanvas`'ın İÇİ için de geçerlidir. Tek satırlık denetim: `Weapon` bileşenini doğrudan
  serialize eden dosyalar yalnız `_Shared/Arsenal/Prefabs/WPN_*.prefab` olmalıdır.
  ⚠️ Sahnede hangi silahın duracağını `ModeDefinition.loadout` DEĞİL **arena** belirler
  (`loadout` yalnız `random` modlarında okunur).
- ⚠️ **`WPN_*` KÖKÜNE mesafeden kavrama GERİ EKLENMEZ** (`DistanceGrabInteractable` /
  `DistanceHandGrabInteractable` — araç bilerek siler). Yasak yalnız kök içindir: **çerçeve
  prefabında (`VA_WeaponFrame`) mesafeden kavrama ZORUNLUDUR.** ⚠️ Kökteki İKİ yakın-kavrama hattı
  (`GrabInteractable` + `HandGrabInteractable`) **birlikte tutulur, biri silinmez** ve filtresiz
  bırakılır. ⚠️ Çerçevenin el hattında `Hand Alignment` = `None`'dır ve öyle kalır.
  ⚠️ **`Grabbable._throwWhenUnselected` GERİ AÇILMAZ.**
- ⚠️ **Kavrama için ikinci bir işaretçi/alan AÇILMAZ** (prefaba grip düğümü, `Weapon`'a ikinci
  `Transform`, Scene View gizmo'su): kayıt yalnız `WD_*`'a yazılır, prefaba HİÇBİR ŞEY yazılmaz.
  ⚠️ Kayıt **DÖNÜŞ TAŞIMAZ** ve el başınadır; eller prefaba GİRMEZ.
  ⚠️ **Parmak duruşu slider'la yazılmaz, preset'ten seçilir** ve **hiçbir zaman donanımdan
  sürülmez**; eşya başına serbest parmak verisi YOKTUR (yeni preset = enum'a SONA değer +
  `HandGripPresets`'e bir satır).
- ⚠️ **Silah ses klipleri `WeaponKitBuilder` tablosundan GELMEZ** — tek doğruluk kaynağı
  `WD_*.asset`'in Inspector'ıdır (klip elle sürüklenir). ⚠️ Silaha kendi reload sesi bağlanınca
  tablodaki `Reload` o klibin uzunluğuna çekilir. ⚠️ Tek tek fişek dolduran silahta
  `perShellReloadAudio` işaretlenir ve `magOutClip` TEK fişeğin sesi olur (+ tabloya
  `ReserveMode = "PoolRounds"`). ⚠️ `PitchBase`'in 1.00'dan sapması yalnız ödünç klibi maskeler.
- ⚠️ **`Muzzle` ve `Eject` elle ayarlanır, araç onları TAŞIMAZ**; araç `WPN_*` prefabını yoktan
  ÜRETMEZ (yeni gövde mevcut bir `WPN_*` kopyalanarak kurulur).
- ⚠️ **Saçmalı silahta menzil kimliğini `Range` DEĞİL `BaseSpread` taşır** (+ düşük bir `Headshot`);
  CS'in saçmalı sayıları arenaya birebir kopyalanmaz.
- ⚠️ **Silah için sunucu tarafında iş YOKTUR ve export gerekmez** — hasarı (bölge çarpanı dahil)
  istemci hesaplar, `weaponId` yalnız kill feed etiketidir. Denge sayıları istemcide yaşadığı için
  değişiklik APK build'i ister.
- ⚠️ **Sahneye ses objesi KOYMA** ve klibi ikinci bir yere yazma; ortam klipleri
  `Assets/Audio/Ambience/` altında ve **`Streaming`** import'lu olur.

## Editor araçları

`VortexArena.Core.Editor` · `VortexArena.Net.Editor` · `VortexArena.App.Editor` (yalnız Editor).
Ne yaptıklarının ayrıntısı `Docs/Sistem-Ozeti.md` §4'te.

| Araç | Ne zaman |
|---|---|
| `Tools > VortexArena > Build > Configure All Build Elements` | Sahne hazır → **Hepsini Yapılandır** (aktif sahnenin `MapDefinition`'ı + `GameCatalog` + `ModeDefinition.maps` + Build Settings + `maps.json` + **silah kiti** + **net eşya kataloğu**, tek geçişte, idempotent). Arena silindi/taşındı → **Yalnız Senkronize Et**. **Hazırlık** bölümü build öncesi denetimleri gösterir (yalnız okur). ⚠️ Ayrı "Arena Id" alanı YOKTUR. ⚠️ Hazırlık'ta kalan ✗ **insan adımıdır** |
| `… > Arena > Template Temellerini Yükle` | Yeni/boş sahneye altyapı prefab ÖRNEKLERİ + rig bağları + boyut dosyası bağlama. İdempotent. ⚠️ Kalibrasyon işaretçisi KOYMAZ — onlar maketle gelir |
| `… > Arena > JSON'dan DimensionMesh Üret` | Boyut JSON'undan ölçü maketi + kalibrasyon işaretçileri. ⚠️ Her arenada ZORUNLU adım |
| `… > Arena > DimensionMesh'i JSON'a Çevir` | Maket sahnede düzeltildi → aynı boyut dosyasının ÜSTÜNE yazar. İşaretçi yoksa `calibration` KORUNUR |
| `… > Arena > Engel Hacimlerini Denetle` | Sahneye iç engel eklendi/layer'ı değişti → konveks olmayan/trigger/collider'sız ve **görünen yüzeyden şişkin** collider'ları raporlar. ⚠️ Hiçbir şeyi düzeltmez; sahne kaydedilmeden koştur |
| `… > Arena > HMD Katmanlarını Kur` | Rig prefabına ekran katmanları (iki uyarı yazısı + hasar vinyeti). **Tek seferlik**, tüm arenalara birden gider. ⚠️ Yazının yeri/boyu/fontu `HmdOverlayBuilder` sabitlerinden gelir, prefabta elle kaydırılan örnek geri yazılır. ⚠️ Çalıştırılmadıkça yazı/vinyet hiç çizilmez |
| `… > Server > Export Server Config` | Yalnız `maps.json` tazelenecekse (`Configure All Build Elements` bunu zaten çağırır) |
| `… > Weapons > Kavrama Pozu Stüdyosu` | Silahın kavraması yazılacak / gözlüksüz denetlenecek. Akış **prefab kipinde**. ⚠️ Kök YALNIZ TAŞINIR, çocukları taşınmaz; kayıt `WD_*`'a gider, prefaba hiçbir şey yazılmaz. ⚠️ Eller prefaba GİRMEZ. ⚠️ Kaydet silah kitini kendisi eşitler — ayrıca *Yalnız Senkronize Et*'e basma; elin Inspector'ına kaydet düğmesi geri eklenmez |
| `… > Avatars > Takım Gövdesini Kur` | `RemoteAvatar.prefab`'a KIRMIZI takım gövdesi (model örneği + `SkeletonPoseMirror` + `redBodyRoot`; ölçüler bind pozundan hesaplanır, koda yazılmaz). İdempotent; model değiştirmek = araçtaki yol sabitini değiştirip tekrar çalıştırmak |
| `… > Audio > Mod Sesleri` | Moda/haritaya göre değişen duyuru sesi → `ModeAudioRegistry.asset`'i seçer. ⚠️ Düzenleme yeri **Inspector**'dır; mod ve harita orada **katalogdan seçilir** |
| `… > Development > Dev` (`Ctrl+Alt+R`) | Rol/hedef seçimi, Play başlangıcı, **sunucusuz sandbox**. ⚠️ Silah kavraması bu pencerenin işi DEĞİLDİR |
| `GameObject > VortexArena > Network Parent` · `Arena Roof` | Sahneye ilgili bileşeni + kurulumunu ekler |
| `PlayerBuildTool.BuildWindowsAdmin` · `…BuildQuestPlayer` | Menü değil — batch-mode `-executeMethod` girişleri |

- ⚠️ **`maps.json` elle düzenlenmez** — export ezer. Tek doğruluk kaynağı Unity SO'larıdır.
- ⚠️ **Sunucu editörden YÖNETİLMEZ** — dev penceresi sunucuyu başlatmaz/durdurmaz/derlemez.
- ⚠️ Süreç başlatırken **asla `dotnet run`** ve **çıktıyı borulama**.

## Dağıtım

`scripts\deploy-admin-game.bat` (Windows admin) · `deploy-player-apk.bat` (Quest oyuncu APK'sı) ·
`deploy-server.bat` · `deploy-launcher.bat` · `deploy_android_updater.bat` (hepsi çift
tıklanabilir; otomasyonda `--no-pause` / `VORTEX_NO_PAUSE=1`). Ayrıntı: `scripts/README.md` ·
`deploy/README.md` · OTA akışı `updater/README.md`.

- ⚠️ **Her iki Unity build'i için editör kapalı olmalı** (batch-mode proje kilidine takılır).
- ⚠️ **Hedef platform betikte SABİTTİR**, aktif platformdan türetilmez ve build sonunda geri
  alınmaz; aktif platform hedefe eşit değilse o koşu tam reimport demektir (20-40 dk).
- ⚠️ **İki Unity build'i AYNI sahne listesini kullanır** — platforma göre ayrı liste tutma.
- ⚠️ **Yeni geliştirici makinesinde bir kez `scripts\defender-exclusions.cmd`** (yönetici, projeyi
  ilk açmadan önce). Dışlama listesi betikten TÜRETİLİR — elle yol ekleme (`-ExtraPath` geç).
  ⚠️ Dışlanan klasöre indirme yapılmaz.
