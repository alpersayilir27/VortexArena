# Faz 5 — Geliştirici araç seti + bağlantı hata ekranı

> **Durum:** ✅ uygulandı (2026-07-27). Batch-mode derleme **0 hata**. Editör/VR içi doğrulama
> maddeleri (aşağıdaki listede ⏳ ile işaretli) kullanıcıda.
>
> Planın yazıldığı hâli tarihsel kayıt olarak bırakıldı; **uygulamada verilen kararlar ve plandan
> sapmalar** en sonda "Uygulama kararları" bölümündedir. Sapma varsa **o bölüm doğrudur.**

## Bağlam — neden

Faz 4 sonrası masaüstü zinciri şöyle oturdu: **Flutter launcher → admin exe'yi `--server-ip` ile
başlatır**, VR tarafı beacon ile kendiliğinden bağlanır (kurtarma yolu: sağ kumandada A×2 →
gizli IP paneli). Bu üretim akışı için doğru, ama **geliştirme** akışını zorlaştırdı:

1. **Rol ve adres sahne dosyasında yaşıyor.** `AppBoot.editorRoleOverride` / `editorServerIp`
   birer `[SerializeField]` — her değiştirişte `Boot.unity` kirleniyor, commit'e sızma riski var
   ve ekipte birbirinin ayarını ezme sorunu doğuyor.
2. **Tek sahneyi denemek pratik değil.** Bir arena sahnesine doğrudan Play'e basınca `AppBoot`
   koşmadığı için rol çözülmemiş, bağlantı yok ve **takım/spawn slot yok** → `PlayerCombatState`
   boş, `CanFire` kapalı, HUD gelmiyor, canlanma akışı denenemiyor.
3. **Her test için elle 2–3 terminal.** Sunucu + bot'lar ayrı ayrı başlatılıyor; editör *player*
   rolündeyken ortamda admin kalmadığı için PoseBot'a `--admin` vermeyi unutmak klasik zaman kaybı.
4. **Sunucu kapalıyken hiçbir şey söylenmiyor.** İstemci sessizce backoff'ta bekliyor; hem
   geliştirmede hem sahada "neden bağlanmadı?" dakikaları buradan gidiyor.

**Hedef:** geliştiricinin rol/hedef/takım seçimini tek pencereden yapması, herhangi bir sahneden
Play'e basabilmesi, tam ortamı tek tıkla ayağa kaldırması; ve bağlanamama durumunun **oyun
ekranında** (VR dahil) açıkça görünmesi.

## Kullanıcı kararları (kesinleşmiş)

| Karar | Sonuç |
|---|---|
| Offline sandbox **YOK** | Her test gerçek sunucu kurallarıyla koşar; bunun yerine sunucu+bot'ları tek tıkla başlatmak hızlandırılır |
| Takım/slot **dev penceresinden seçilebilir** | Kırmızı/mavi + slot seçimi → iki farklı tabanı ve canlanma akışını deneyebilmek için |
| Sunucu yoksa **başlatılmaz, hata gösterilir** | Otomatik sunucu başlatma yok. Hata **oyun içi ekranda**, VR'da da aynı ekran, tasarımlı |
| Ayarlar **ekiple paylaşılır** | İki katman: hedef **kataloğu** commit'li, **aktif seçim** kişisel (aşağıda gerekçe) |

### Neden iki katman (bu fazın en kritik kararı)

"Ayarlar paylaşılsın" isteği doğrudan uygulanırsa **anlık IP seçimi** de commit'lenir: biri
`192.168.1.50` yazıp push eder, diğeri kendi makinesini yazar → sürekli birbirini ezen commit'ler
ve hep kirli `git status`. Klasik "checked-in user settings" tuzağı.

| Katman | İçerik | Yer | Git |
|---|---|---|---|
| **Katalog** | Adlandırılmış hedefler (`Local`, `Ofis-PC`, `Arena-PC`) + varsayılan hedef/rol | `dev-targets.json` (repo kökü) | **commit'li** |
| **Seçim** | Şu an hangi hedef/rol/takım/slot/başlangıç modu | `EditorPrefs` | **commit'siz** |

Kazanç: yeni gelen klonlayıp listeden seçer, hiçbir şey kurmadan çalışır; kimse kimsenin seçimini
ezmez. Katalog **JSON** (ScriptableObject değil): `.asset` YAML'ı merge'de kâbus, JSON text olarak
birleşir ve `Server/config/*.json` ailesiyle tutarlı.

---

## Adım 1 — Hedef kataloğu + seçim katmanı

**Yeni:** `dev-targets.json` (repo kökü, commit'li)

```json
{
  "defaultTarget": "Local",
  "defaultRole": "admin",
  "targets": [
    { "name": "Local",    "ip": "127.0.0.1",    "port": 47821 },
    { "name": "Ofis-PC",  "ip": "192.168.1.50", "port": 47821 }
  ]
}
```

**Yeni:** `Assets/_Shared/Core/Editor/DevTargets.cs` — JSON'u okur (`JsonUtility`), hedef listesi
+ varsayılanları verir. Dosya yoksa `Local` tek hedefiyle bellekte varsayılan üretir (repo'ya
yazmaz) — böylece dosya silinse de editör çalışır.

**Yeni:** `Assets/_Shared/Core/Editor/DevPrefs.cs` — `EditorPrefs` sarmalayıcı. Anahtarlar
`VortexArena.Dev.` önekli: `Role`, `TargetName`, `CustomIp`, `CustomPort`, `StartMode`, `Team`,
`SpawnSlot`. Tek yerden okunur/yazılır ki anahtar adları dağılmasın.

> ⚠️ `dev-targets.json` **oyuna girmez** — `Assets/` dışında durur, yalnız editor assembly'si
> okur. Runtime statik fallback'i `Assets/StreamingAssets/arena.json`'dur, o ayrı bir şeydir.

## Adım 2 — `DevBootstrap`: her sahneden Play

**Yeni:** `Assets/_Shared/Core/Editor/DevBootstrap.cs` (editor-only)

İki başlangıç modu, dev penceresinden seçilir:

| Mod | Mekanizma | Ne zaman |
|---|---|---|
| **Boot'tan başlat** | `EditorSceneManager.playModeStartScene = Boot.unity` — hangi sahne açık olursa olsun akış Boot'tan koşar, Play bitince açık sahneye dönülür | Rol yönlendirme, `load_match`, faz makinesi gibi akışın tamamı |
| **Açık sahneden** | `playModeStartScene = null`; aşağıdaki enjeksiyon devreye girer | Tek sahne üzerinde çalışırken — **en sık kullanılan** |

"Açık sahneden" modunda Play'e basıldığında:

1. `AppSession.Role` / `ServerIp` / `ServerPort` seçili değerlerden yazılır, `RoleResolved = true`
   (böylece controller'ların `Awake`'teki "kendi varsayılanını yaz" yolu devreye girmez).
2. Sahne Boot/Lobby/AdminConsole **değilse** (yani arena sahnesiyse) sentetik bir `load_match`
   yayınlanır: `sceneName` = açık sahne, `yourTeam` = seçili takım, `spawnSlot` = seçili slot.

**Neden sentetik `load_match`?** `PlayerCombatState` takımı/slotu `NetEvents.OnLoadMatch`'ten
alıyor (`PlayerCombatState.cs:358` — `msg.yourTeam`, `msg.spawnSlot`), `ModeHudSpawner` de aynı
olaya bağlı. Dev'e özel ikinci bir API açmak yerine **gerçek kod yolunu** kullanırız; böylece dev
ortamı ile sahada koşan yol sapmaz.

> ⚠️ **Bunun için `SceneRouter`'a idempotanlık koruması gerekiyor:** `load_match` geldiğinde
> istenen sahne **hâlihazırda aktifse yeniden yükleme yapılmamalı**. Aksi halde enjeksiyon
> sahneyi yeniden yükler ve döngüye girer. Bu koruma dev-only bir yama değil, **üretimde de
> doğru davranış**: sunucudan mükerrer `load_match` gelirse sahne boşuna yeniden yüklenmemeli.
> Uygulama sırasında `SceneRouter.cs` okunup guard eklenecek.
>
> ✅ **Sonuç: guard HÂLİHAZIRDA VARDI** — `SceneRouter.LoadChecked` içinde
> `if (SceneManager.GetActiveScene().name == sceneName)` kontrolü mevcut (aktif sahneyse yeniden
> yüklemez, yalnız hazır bildirimini elden verir). `SceneRouter.cs` **değiştirilmedi.**

## Adım 3 — Dev penceresi + kısayol

**Yeni:** `Assets/_Shared/Core/Editor/DevWindow.cs` — `Tools > VortexArena > Dev`

```
Rol        ( ) Player      (•) Admin
Hedef      [ Local (127.0.0.1:47821) ▾ ]   [ özel: ________ : ____ ]
Başlangıç  (•) Boot'tan     ( ) Açık sahneden
Takım      (•) Kırmızı  ( ) Mavi        Spawn slot [ 0 ]

[ 2 Bot ]  [ 2 Bot + Admin ]  [ Botları Durdur ]
Durum: 2 bot süreci ● çalışıyor
```

> ⚠️ **Sonradan değişti (27 Tem 2026):** "Sunucuyu Başlat / Durdur / Hepsini Durdur" düğmeleri
> **kaldırıldı** — sunucu artık tamamen elle yönetilir. Gerekçe aşağıda ("Uygulama kararları").

- **Kısayol:** `[Shortcut]` ile `Ctrl+Alt+R` → rolü Player/Admin arasında çevirir (pencere açmaya
  gerek kalmadan, Play'den hemen önce).
- **Takım/slot** yalnız "açık sahneden" modunda etkin; Boot'tan başlarken takımı sunucu dağıtır.

### Süreç yönetimi — kanıtlanmış tuzak

Süreçler **`dotnet run` ile başlatılMAZ.** Bu oturumda yaşandı: `dotnet run` bir çocuk süreç
doğuruyor; parent öldürülünce `VortexArena.Server.App.exe` **yetim kalıp 47821'i tutmaya devam
etti** ve öldürülemedi → sonraki sunucu porta bind olamaz.

Bu yüzden:
- ~~Sunucu **doğrudan exe** olarak başlatılır…~~ **geçersiz (27 Tem 2026):** sunucu editörden hiç
  başlatılmıyor, elle çalıştırılıyor. Tuzağın kendisi hâlâ geçerli — programatik başlatılan her
  süreç (PoseBot) doğrudan exe ile başlar.
- PoseBot için de aynı kural geçerli → PoseBot'un da publish edilmiş exe'si gerekir. Bunun için
  `scripts/deploy-server.bat`'e **PoseBot'u da publish eden** bir adım eklenir
  (`deploy\posebot\`), ya da dev penceresi `dotnet run` yerine `taskkill /T /PID` ile ağaç
  öldürür. **Tercih: PoseBot'u da publish etmek** — tek süreç, temiz öldürme.
- Pencere **bot** PID'lerini tutar; **Play modundan çıkışta ve editör kapanışta otomatik öldürür**
  (`EditorApplication.quitting` + `playModeStateChanged`), ayrıca "Botları Durdur" /
  "Sahipsiz botları temizle" düğmeleri. Sunucu bu otomatizmin dışındadır.

## Adım 4 — Bağlantı hata ekranı (`ConnectionOverlay`)

Kullanıcı kararı: sunucu yoksa **otomatik başlatma yok**, hata **oyun ekranında**, VR'da da aynı
ekran, tasarımlı.

**Yeni:** `Assets/_Shared/Core/UI/ConnectionOverlay.cs` + prefab.

**Ne zaman görünür:** `NetEvents.OnConnectionStateChanged` dinlenir. Bağlı değilken **grace
süresi** (~3 sn) beklenir — anlık kopmalarda ekran yanıp sönmesin. Bağlantı kurulunca kaybolur.
Yalnız açılışı değil **maç ortasındaki kopmayı** da kapsar.

**İçerik (her iki platformda aynı bilgi hiyerarşisi):**

```
        ⚠  SUNUCUYA BAĞLANILAMIYOR
        192.168.1.50:47821
        12 sn · 4. deneme

        VR    : Sunucunun açık olduğundan emin olun.
                Adresi değiştirmek için sağ kumandada A×2.
        Admin : Sunucu uygulamasını başlatın, sonra "Yeniden Bağlan".
```

**Nasıl her sahnede var olur:** `ArenaClient` deseni tekrarlanır — `RuntimeInitializeOnLoadMethod`
ile prefab'ı kendisi örnekler ve `DontDestroyOnLoad` yapar. Böylece sahne başına elle bağlama
gerekmez (yeni arena eklerken unutulacak bir adım olmaz). Prefab'a erişim: `Resources.Load`
(en basit ve sahne-bağımsız) — alternatif bir katalog SO'su ama o da her sahnede referans ister.
Karar: **Resources**, gerekçesi yorumda yazılır.

**VR yerleşimi — konfor ve güvenlik:**
- World-space panel, kafanın ~2 m önünde; **lazy-follow** (ölü bölge + yumuşatma). Katı biçimde
  kafaya kilitlemek free-roam'da rahatsızlık verir.
- Kamerayı her sahnede yeniden bulmak gerekir (overlay sahnelerden önce doğuyor) →
  `PlayerPoseTracker`'ın BB rig anchor'larını bulma deseni tekrar kullanılır (`CenterEyeAnchor`).
- Depth-test kapalı çizim: arena geometrisi paneli yutmasın.
- ⚠️ **Güvenlik:** oyuncu fiziksel alanda yürüyor. Panel yarı saydam olmalı ve
  **`ArenaBoundary`'nin alan-dışı karartması her zaman öncelikli kalmalı** — bağlantı hatası
  ekranı oyuncunun duvara yürümesine sebep olmamalı. Bu, iki overlay'in z-sırası ve alfa
  değerlerinin bilinçli seçilmesi gereken tek yer.

**Admin yerleşimi:** mevcut `ConnectingPanel` bu overlay ile birleşir — bugün orada düz metin var
(`ConnectingStatusText`), yerine aynı tasarımlı panel gelir; "Yeniden Bağlan" düğmesi korunur.

---

## Dokunulacak dosyalar

*(Aşağıdaki tablo **gerçekleşen hâle güncellendi**; planlanandan farklı satırlar işaretli.)*

| Dosya | Değişiklik |
|---|---|
| `dev-targets.json` | **yeni** — hedef kataloğu (commit'li) |
| `Assets/_Shared/App/Scripts/Editor/VortexArena.App.Editor.asmdef` | **yeni** — dev araçları assembly'si (⚠ planda `Core.Editor` deniyordu; `AppSession`/`DevSession` gerektiği için App altında) |
| `Assets/_Shared/App/Scripts/Editor/DevTargets.cs` | **yeni** — katalog okuyucu |
| `Assets/_Shared/App/Scripts/Editor/DevProcesses.cs` | **yeni** ⚠ planda yoktu — bot süreç kaydı (`SessionState` + ad doğrulaması + yetim süpürme). *27 Tem 2026: sunucu başlat/durdur kaldırıldı* |
| `Assets/_Shared/App/Scripts/Editor/DevBootstrap.cs` | **yeni** — `playModeStartScene`, Play çıkışında bot temizliği, `Ctrl+Alt+R` |
| `Assets/_Shared/App/Scripts/Editor/DevWindow.cs` | **yeni** — pencere |
| `Assets/_Shared/App/Scripts/DevSession.cs` | **yeni** ⚠ planda `DevPrefs.cs` (editör) idi — runtime + `#if UNITY_EDITOR`: anahtarlar, rol/adres uygulama, sentetik `load_match` |
| `Assets/_Shared/App/Scripts/ConnectionOverlay.cs` | **yeni** ⚠ planda `Core/UI` + prefab idi — App altında, prosedürel, prefab/Resources yok |
| `Assets/_Shared/App/Scripts/AppBoot.cs` | `editorRoleOverride`/`editorServerIp` **kaldırıldı**; adres çözümü **rolden bağımsız** hâle geldi; rol zaten çözülmüşse ezmiyor |
| `Assets/_Shared/App/Scripts/LobbyController.cs` | ⚠ planda yoktu — keşif zincirinin başına `AppSession.HasServerEndpoint` |
| `Assets/_Shared/Net/Scripts/NetEvents.cs` | ⚠ planda yoktu — `InjectLoadMatch` (`#if UNITY_EDITOR`) |
| `Assets/_Shared/Net/Scripts/ArenaClient.cs` | ⚠ planda yoktu — `ConnectAttempts`, `LastError` |
| `Assets/_Shared/App/Scripts/AdminConsoleController.cs` | yalnız sınıf dokümanı (overlay ile sıralı iş bölümü) — panel alanları **kaldırılmadı** |
| `Assets/_Shared/Scenes/Boot.unity` | kaldırılan iki alanın temizliği ✅ |
| ~~`Assets/_Shared/App/Scripts/SceneRouter.cs`~~ | **değişmedi** — guard zaten vardı |
| ~~`Assets/_Shared/Scenes/AdminConsole.unity`~~ | **değişmedi** — gerekçe "Uygulama kararları"nda |
| ~~`scripts/deploy-server.bat`~~ | **değişmedi** — PoseBot publish edilmiyor |

## Doküman güncellemeleri (aynı commit — `docs-sync` kuralı)

- `CLAUDE.md` — dev iş akışı + yeni editor araçları + `dev-targets.json`
- `Docs/Sistem-Ozeti.md` — §4 bileşen sözlüğü (`ConnectionOverlay`), §6.2 geliştirme akışı
  (`editorRoleOverride` yerine dev penceresi)
- `Docs/Isletme-Kurulum.md` — sorun giderme tablosuna "bağlantı hata ekranı ne diyor" satırı
- `plan/README.md` — Faz 5 satırı ✅ **+ karar #2 DÜZELTİLMEDİ, SİLİNDİ** (kullanıcı kararı):
  "Launcher = admin uygulamasının giriş ekranı, sunucuyu `Process.Start` ile başlatır" tümden
  geçersiz; yerine neden geçersiz olduğunu söyleyen bir not bırakıldı ve karar #3 → #2 oldu.
  Ayrıca "Akış" satırındaki "ayar panelinden IP girilir" ifadesi beacon keşfine göre düzeltildi.
- Protokol dokümanı: **mesaj yüzeyi değişmedi** (yeni mesaj/alan/sabit/port yok) — ama §4 keşif
  tablosu zincir rolden bağımsız hâle geldiği için güncellendi ✅

## Doğrulama

| # | Madde | Durum |
|---|---|---|
| 1 | **Katalog:** `dev-targets.json` silinmişken editör açılır, hata vermez, `Local` görünür | ⏳ editörde |
| 2 | **Seçim kişisel:** rol/hedef değiştirilir → `git status` **temiz kalır** | ✅ tasarımca (`EditorPrefs`; `Boot.unity`'deki override alanları silindi, `git status` doğrulandı) |
| 3 | **Boot'tan kip:** arena sahnesi açıkken Play → Boot'tan koşar, rol doğru sahneye gider, Play bitince açık sahneye dönülür | ⏳ editörde |
| 4 | **Açık sahneden kip:** arena sahnesi açıkken Play → sahne yeniden **yüklenmez**, seçili takım/slot `PlayerCombatState`'e düşer, HUD gelir, `CanFire` sunucu `Live` deyince açılır | ⏳ editörde |
| 5 | **Tek tıkla ortam:** sunucu **elle** başlatılır + "2 Bot + Admin" → roster'da 2 bot + admin, TDM raundu koşar | ⏳ editörde |
| 6 | **Bot sızıntısı yok:** Play'den çıkılır + editör kapatılır → geride `VortexArena.PoseBot` süreci kalmaz (`tasklist \| findstr PoseBot` boş). Sunucu **kasten yaşar** — onu elle kapatırsın | ⏳ editörde — **kanıtı zorunlu, tuzak yaşanmış** |
| 7 | **Hata ekranı (admin):** sunucu kapalıyken admin → ~3 sn sonra tasarımlı ekran, adres + deneme sayacı doğru; sunucu açılınca kaybolur | ⏳ editörde |
| 8 | **Hata ekranı (VR):** Quest'te sunucu kapalıyken lobi → aynı ekran okunur, lazy-follow rahat; A×2 ile IP paneli hâlâ açılıyor | ⏳ cihazda |
| 9 | **Maç ortası kopma:** maç `Live` iken sunucu kapatılır → overlay gelir; geri açılınca kaybolur | ⏳ editörde |
| 10 | **Alan-dışı önceliği:** overlay açıkken arena sınırı dışına çıkılır → `ArenaBoundary` karartması/uyarısı **görünür kalır** | ⏳ cihazda (güvenlik maddesi) |
| 11 | **Derleme:** tüm assembly'ler hatasız | ✅ batch-mode Unity 6000.3.20f1 → **0 `error CS`**; `VortexArena.App.dll` + yeni `VortexArena.App.Editor.dll` üretildi |
| 12 | **Dev kodu build'e sızmıyor:** `#if UNITY_EDITOR` doğrulaması | ✅ tasarımca (`DevSession.cs` tamamı `#if UNITY_EDITOR`; dev araçları `includePlatforms:["Editor"]` asmdef'te; `NetEvents.InjectLoadMatch` de guard'lı) — Windows admin build'i ile son teyit ⏳ |

## Uygulama kararları (plandan sapmalar — çelişki hâlinde BU bölüm doğrudur)

**Plandaki üç açık soru kapandı:**

| Açık soru | Karar | Gerekçe |
|---|---|---|
| Ana toolbar'a rol/hedef seçici | **Kullanılmadı.** Dockable pencere + `Ctrl+Alt+R` + kısayolda `SceneView.ShowNotification` + Play'de konsola konfig satırı | Unity 6000.3'te ana toolbar API'si hâlâ internal; dev aracını resmi olmayan API'ye bağlamak her editör yükseltmesinde kırılır |
| PoseBot publish mi | **Publish YOK.** `Server\VortexArena.PoseBot\bin\{Release,Debug}\net10.0\` doğrudan başlatılır; `scripts\deploy-server.bat` **değiştirilmedi** | `deploy/` işletmeye giden klasör; PoseBot dev/test aracı — sahaya sentetik oyuncu üreten exe gitmemeli. Exe zaten orada, ek adım gereksiz. Yoksa pencerede "Derle (dotnet build)" düğmesi var |
| Overlay prefab'ı `Resources`'ta mı | **Prefab da Resources da YOK** — UI tamamen koddan kurulur (yuvarlak köşe sprite'ı dahil runtime'da üretilir) | `IdentifyOverlay` bu deseni zaten kuruyor; sahne bağı olmadığı için yeni arena eklerken unutulacak adım kalmıyor ve Resources build şişmesi olmuyor |

**Dosya/yapı sapmaları:**

- **`DevPrefs.cs` yazılmadı.** `EditorPrefs` anahtarları + accessor'lar `DevSession.cs` içinde
  (runtime, tamamı `#if UNITY_EDITOR`). Sebep: enjeksiyonu **runtime** yapmak zorunlu
  (`RuntimeInitializeOnLoadMethod`), anahtarlar iki dosyaya bölünürse editör ile runtime arasında
  **anahtar adı sapması** riski doğar. Tek dosya = tek sözleşme.
- **`ConnectionOverlay` `Core/UI` değil `App/Scripts` altında.** Rol'e göre ipucu metni için
  `AppSession.Role` gerekiyor; `Core` → `App` referansı assembly grafiğini ters çevirirdi
  (`App → Core`). Overlay bir uygulama-kabuğu sorumluluğu, `AppBoot`/`SceneRouter` ile aynı yerde.
- **`DevProcesses.cs` eklendi** (planda yoktu): süreç kaydı pencereden ayrıldı, çünkü
  `DevBootstrap` de (Play çıkışı / editör kapanışı) aynı kaydı kullanıyor.
- **`SceneRouter.cs` değiştirilmedi** — idempotanlık guard'ı zaten vardı (yukarıda).
- **`AdminConsole.unity` değiştirilmedi**, `ConnectingPanel` sahnede kaldı. İş bölümü **sıralı,
  mükerrer değil**: ilk ~3 sn hafif "Bağlanılıyor: …" satırı, sonra overlay (`sortingOrder = 5000`)
  üstünü kaplar. İkisi aynı anda görünmez, ikisi de `ArenaClient.Connect`'i çağırır.
  *(Editör kapalıydı; sahne YAML'ını elle kesmek `m_Children` listesi ve bileşen blokları yüzünden
  gereksiz risk. Panel gerçekten istenmiyorsa editör açıkken 2 dakikalık bir temizlik.)*
- **Overlay kendi `EventSystem`'ini kurmuyor.** Buton yalnız masaüstünde var, masaüstü admin akışı
  Boot → AdminConsole'dan çıkmıyor ve o sahnede (Lobby'de de) `InputSystemUIInputModule`'lü
  EventSystem zaten duruyor. Proje **Input System-only** (`activeInputHandler: 1`) olduğu için eski
  `StandaloneInputModule`'ü örneklemek çalışmazdı. Yoksa bir kez uyarı loglanır.

**Plan dışı ama gereken üretim değişikliği (bilinçli):**

- **Adres zinciri rolden bağımsız hâle geldi.** `AppBoot` artık `--server-ip`/`--server-port`'u
  **her rolde** okuyor; `LobbyController.Start()` zincirin başına `AppSession.HasServerEndpoint`
  kontrolünü aldı (`_manualEntry` ile işaretli → beacon ezmiyor). Yeni sıra:
  **komut satırı > PlayerPrefs > beacon > `arena.json`**.
  Sebep: dev'de player rolüne hedef seçtirmek için `PlayerPrefs`'e yazan bir dev-hack'i koymak
  yerine zinciri düzeltmek doğru olan — açıkça verilen adres her rolde kazanmalı. VR build'ine
  argüman geçilmediği için **Quest davranışı pratikte değişmedi**.
- **`NetEvents.InjectLoadMatch`** (`#if UNITY_EDITOR`) — `RaiseLoadMatch` internal olduğu için
  enjeksiyonun tek temiz yolu. Protokol mesajı DEĞİL, test kancası.
- **`ArenaClient.ConnectAttempts` + `LastError`** — overlay'in "N sn · M. deneme" ve "Son hata"
  satırları için; başka tüketicisi yok.

**Faz sonrası değişiklik — 27 Tem 2026: sunucu süreç yönetimi editörden çıkarıldı**

Kullanıcı kararı: **sunucu işleri tamamen elle.** Dev penceresindeki "Sunucuyu Başlat" / "Durdur" /
"Hepsini Durdur" düğmeleri ve `DevProcesses`'teki sunucu tarafı (`StartServer`, `StopServer`,
`ServerPid`, `IsServerRunning`, `StopAll`, sunucu exe arama listesi, `ServerProcessName`)
**silindi**; ad bazlı süpürme yalnız `VortexArena.PoseBot`'a bakıyor, editör kapanışı da sunucuya
dokunmuyor. Gerekçe: sunucu üretimde ayrı makinede uzun ömürlü — editörün onu başlatması/öldürmesi
o topolojiden uzaklaştırıyor ve elle başlatılmış bir sunucuyu beklenmedik anda öldürme riski
taşıyordu. Kalanlar: bot düğmeleri + "Derle (dotnet build)" (yalnız derler, çalıştırmaz).
