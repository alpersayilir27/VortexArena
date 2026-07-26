# Faz 5 — Geliştirici araç seti + bağlantı hata ekranı

> **Durum:** planlandı, uygulanmadı. Uygulayıcı: sırayla adımları uygula, sondaki
> **Doğrulama** bölümünü geçmeden fazı bitmiş sayma.

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

## Adım 3 — Dev penceresi + kısayol

**Yeni:** `Assets/_Shared/Core/Editor/DevWindow.cs` — `Tools > VortexArena > Dev`

```
Rol        ( ) Player      (•) Admin
Hedef      [ Local (127.0.0.1:47821) ▾ ]   [ özel: ________ : ____ ]
Başlangıç  (•) Boot'tan     ( ) Açık sahneden
Takım      (•) Kırmızı  ( ) Mavi        Spawn slot [ 0 ]

[ Sunucuyu Başlat ]  [ 2 Bot ]  [ 2 Bot + Admin ]  [ Hepsini Durdur ]
Durum: sunucu ● çalışıyor (PID 1234) · 2 bot ● çalışıyor
```

- **Kısayol:** `[Shortcut]` ile `Ctrl+Alt+R` → rolü Player/Admin arasında çevirir (pencere açmaya
  gerek kalmadan, Play'den hemen önce).
- **Takım/slot** yalnız "açık sahneden" modunda etkin; Boot'tan başlarken takımı sunucu dağıtır.

### Süreç yönetimi — kanıtlanmış tuzak

Süreçler **`dotnet run` ile başlatılMAZ.** Bu oturumda yaşandı: `dotnet run` bir çocuk süreç
doğuruyor; parent öldürülünce `VortexArena.Server.App.exe` **yetim kalıp 47821'i tutmaya devam
etti** ve öldürülemedi → sonraki sunucu porta bind olamaz.

Bu yüzden:
- Sunucu **doğrudan exe** olarak başlatılır: önce `deploy\server\VortexArena.Server.App.exe`,
  yoksa `Server\VortexArena.Server.App\bin\Release\net10.0\...exe`. İkisi de yoksa
  "önce `scripts\deploy-server.bat` çalıştır" diye anlamlı hata.
- PoseBot için de aynı kural geçerli → PoseBot'un da publish edilmiş exe'si gerekir. Bunun için
  `scripts/deploy-server.bat`'e **PoseBot'u da publish eden** bir adım eklenir
  (`deploy\posebot\`), ya da dev penceresi `dotnet run` yerine `taskkill /T /PID` ile ağaç
  öldürür. **Tercih: PoseBot'u da publish etmek** — tek süreç, temiz öldürme.
- Pencere PID'leri tutar; **Play modundan çıkışta ve editör kapanışta otomatik öldürür**
  (`EditorApplication.quitting` + `playModeStateChanged`), ayrıca "Hepsini Durdur" düğmesi.

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

| Dosya | Değişiklik |
|---|---|
| `dev-targets.json` | **yeni** — hedef kataloğu (commit'li) |
| `Assets/_Shared/Core/Editor/DevTargets.cs` | **yeni** — katalog okuyucu |
| `Assets/_Shared/Core/Editor/DevPrefs.cs` | **yeni** — EditorPrefs sarmalayıcı |
| `Assets/_Shared/Core/Editor/DevBootstrap.cs` | **yeni** — play mode kancası + enjeksiyon |
| `Assets/_Shared/Core/Editor/DevWindow.cs` | **yeni** — pencere + kısayol + süreç yönetimi |
| `Assets/_Shared/Core/UI/ConnectionOverlay.cs` + prefab | **yeni** — hata ekranı |
| `Assets/_Shared/App/Scripts/AppBoot.cs` | `editorRoleOverride`/`editorServerIp` **kaldırılır** (artık EditorPrefs); Boot.unity sadeleşir |
| `Assets/_Shared/App/Scripts/SceneRouter.cs` | `load_match` idempotanlık koruması (aktif sahneyse yeniden yükleme yok) |
| `Assets/_Shared/App/Scripts/AdminConsoleController.cs` | `ConnectingPanel` → overlay ile birleşme |
| `Assets/_Shared/Scenes/Boot.unity` | kaldırılan alanların temizliği |
| `Assets/_Shared/Scenes/AdminConsole.unity` | overlay entegrasyonu |
| `scripts/deploy-server.bat` | PoseBot'u da publish et (`deploy\posebot\`) |

## Doküman güncellemeleri (aynı commit — `docs-sync` kuralı)

- `CLAUDE.md` — dev iş akışı + yeni editor araçları + `dev-targets.json`
- `Docs/Sistem-Ozeti.md` — §4 bileşen sözlüğü (`ConnectionOverlay`), §6.2 geliştirme akışı
  (`editorRoleOverride` yerine dev penceresi)
- `Docs/Isletme-Kurulum.md` — sorun giderme tablosuna "bağlantı hata ekranı ne diyor" satırı
- `plan/README.md` — Faz 5 satırı **+ kararı #2'nin düzeltilmesi**: "Launcher = admin
  uygulamasının giriş ekranı, sunucuyu `Process.Start` ile başlatır" artık **yanlış** (ayrı
  Flutter launcher var, sunucu hiçbir yerden otomatik başlatılmaz)
- Protokol dokümanı: **değişmiyor** — yeni mesaj/alan/sabit yok

## Doğrulama

1. **Katalog:** `dev-targets.json` silinmişken editör açılır, hata vermez, `Local` görünür.
2. **Seçim kişisel:** rol/hedef değiştirilir → `git status` **temiz kalır** (hiçbir sahne veya
   asset kirlenmez).
3. **Boot'tan mod:** bir arena sahnesi açıkken Play → Boot'tan koşar, rol doğru sahneye gider,
   Play bitince açık sahneye dönülür.
4. **Açık sahneden mod:** arena sahnesi açıkken Play → sahne yeniden **yüklenmez**, seçili
   takım/slot `PlayerCombatState`'e düşer, HUD gelir, `CanFire` sunucu `Live` deyince açılır.
5. **Tek tıkla ortam:** "Sunucuyu Başlat" + "2 Bot + Admin" → roster'da 2 bot + admin görünür,
   TDM raundu koşar.
6. **Süreç sızıntısı yok:** Play'den çıkılır ve editör kapatılır → `netstat -ano | findstr 4782`
   **boş** (yetim süreç yok). Bu maddenin kanıtı zorunlu, tuzak yaşanmış.
7. **Hata ekranı (admin):** sunucu kapalıyken admin açılır → ~3 sn sonra tasarımlı ekran,
   adres + deneme sayacı doğru; sunucu açılınca ekran kendiliğinden kaybolur.
8. **Hata ekranı (VR):** Quest'te sunucu kapalıyken lobi → aynı ekran okunur biçimde, lazy-follow
   rahat; A×2 ile IP paneli hâlâ açılabiliyor.
9. **Maç ortası kopma:** maç `Live` iken sunucu kapatılır → overlay gelir; geri açılınca kaybolur.
10. **Alan-dışı önceliği:** overlay açıkken arena sınırının dışına çıkılır → `ArenaBoundary`
    karartması/uyarısı **görünür kalır** (güvenlik maddesi).
11. Toplu geçiş: `unity cmd recompile` + `get_console_logs` temiz; Windows admin build'i alınır ve
    launcher'dan başlatılır (dev kodu build'e sızmadı — `#if UNITY_EDITOR` doğrulaması).

## Açık kalan / uygulama sırasında karar verilecek

- **Ana toolbar'a yerleştirme:** Play düğmesinin yanına rol/hedef açılır listesi koymak en az
  yorucu olurdu; Unity 6000.3'te bunun **resmi API'si var mı doğrulanmalı**. Yoksa garanti yol:
  dockable pencere + `[Shortcut]` (+ isteğe bağlı Scene View overlay — o API resmi).
- **PoseBot publish mi, process-tree kill mi:** plan publish'i tercih ediyor; `deploy-server.bat`
  şişerse ayrı `deploy-posebot.bat` ayrılır.
- **Overlay prefab'ı `Resources`'ta mı:** Resources kullanımı Unity'de genel olarak önerilmez
  (build'e her zaman girer); alternatif Addressables ya da katalog SO'su. Tek küçük prefab için
  Resources kabul edilebilir görülüyor, uygulamada tekrar bakılacak.
