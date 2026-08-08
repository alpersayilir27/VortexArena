# VortexArena — Sistem Özeti (ne yapıldı, nasıl çalışıyor, nasıl kullanılır)

> **Bu doküman ne?** Bugünkü sistemin tek sayfalık haritası: mimari, ağ mantığı, hangi
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
> | Sıradaki planlanmış işler (mod altyapısı, FFA) | `plan/README.md` |

---

## 1. Ürün ve roller

**VortexArena** = işletmelere (LBE) kurulan **free-roam VR PvP arena**. Oyuncular fiziksel alanda
**1:1 yürür** (lokomosyon yok), hedef cihaz **yalnız Quest 3 / 3S**. Aynı Unity projesinden iki
build çıkar:

| Build | Rol | Ne yapar |
|---|---|---|
| **Android (Quest)** | `player` | Lobi → maç; oynar, poz gönderir, ateş eder |
| **Windows** | `admin` | `--server-ip` ile açılır → **oyuncularla aynı sahneye** girer (gözlemci): 3 kamera kipi + sahne üstü yönetim HUD'ı (roster, mod+harita, start/abort, istatistik) |
| **Windows** | launcher | `launcher/` — .NET 10 WPF operatör uygulaması: sunucu/admin exe yollarını, sunucu IP'sini ve **mekanı** tutar; sunucuyu `--venue`, oyunu `--server-ip` ile başlatır |

Üçüncü bileşen: **`Server/` — kendi .NET 10 konsol sunucumuz** (standalone exe, tamamen offline
LAN). Mirror/NGO gibi hazır netcode **kullanılmıyor**; hem oyun kurallarının sunucuda koşması hem
de işletmede internetsiz çalışma şartı bunu gerektiriyor.

---

## 2. Repo haritası ve assembly grafiği

```
D:\Games\vortexarena\
  Assets\
    _Shared\                 ← "ikinci bir mod/arena bunu aynen kullanır mı?" → EVET olan her şey
      Core\                  VortexArena.Core        (arena, savaş, oyuncu, UI, katalog SO'ları, FX)
        Editor\              VortexArena.Core.Editor (Configure All Build Elements, Template
                             Temellerini Yükle, ölçü maketi üretimi/geri okuması)
      Net\Protocol\          VortexArena.Protocol    (SAF C# — sunucu aynı dosyaları derler)
      Net\Scripts\           VortexArena.Net         (bağlantı/keşif/senkron; oyun bilgisi YOK)
        Editor\              VortexArena.Net.Editor  (Network Parent, sceneId bekçisi)
      App\Scripts\           VortexArena.App         (Boot yönlendirme, Lobi, UiKit, köprüler)
        Admin\               (aynı asmdef) admin gözlemci: kamera kipleri, HUD, paneller
        Editor\              VortexArena.App.Editor  (Dev penceresi: rol/hedef + sunucu derleme)
        Resources\UI\        ARAYÜZÜN TAMAMI prefab olarak (admin HUD+paneller, oyuncu satırı,
                             oyuncu halkası, bağlantı ekranı ×2, cephane, kimlik kartı).
                             Sahneye KONMAZ, Resources.Load ile yüklenir — elle düzenlenir
        UI\Sprites\          o prefabların 9-slice köşe/halka görselleri
      Arsenal\ FX\ Environments\ Scenes\             (kod-dışı ortak içerik + Boot/Lobby)
      Data\Resources\        GameCatalog.asset — admin arayüzü Resources.Load ile okur
    Arenas\Template\         referans arena — OYNANMAZ (export/Build Settings/katalog dışı)
      Scenes\Default12x12\   dizayn taşımayan referans: "ağa bağlı sahne neye benzer"
    Arenas\Venues\<İşletme>\ MEKAN = kutuların kabı; kökünde YALNIZ Art\ Prefabs\ Data\ Scenes\
                             durur (ilk üçü o mekanın TÜM sahnelerinin paylaştığı içerik)
      Scenes\<SahneAdı>\     arena kutusu: <SahneAdı>.unity + Data\<SahneAdı>.asset (MapDefinition)
                             + istenirse Art\ Prefabs\ — klasör/sahne/asset adı ÜÇÜ DE aynı;
                             arena-özel KOD yazılmaz. Lobi de bir kutudur, farkı
                             MapDefinition.supportedModeIds = ["lobby"]
    Arenas\Venues\Outdoor12x12\  Scenes\IceWorld (elle modellenmiş tematik arena +
                             Art\{Materials,Textures} + Prefabs\FX_SnowStorm) · Scenes\Arena12x12
                             · Scenes\Lobby12x12
                             + Data\Outdoor12x12_dimensions.json — mekanın boyut dosyası
    Arenas\Venues\VortexAntep\   Scenes\VortexAntep (asimetrik hangar arenası) ·
                             Scenes\LobbyVortexAntep (aynı fiziksel oda)
                             + Data\VortexAntep_dimensions.json — mekanın boyut dosyası (ölçünün
                             TEK kaynağı), ikisi de kullanır
    Modes\TeamDeathmatch\    mod kutusu: {Scripts → VortexArena.Modes.Tdm, Data, UI}
    Modes\FreeForAll\        mod kutusu: {Scripts → VortexArena.Modes.Ffa, Data, UI} — takımsız
                             "Herkes Tek"; arena-özel hiçbir iş gerektirmez
  Server\                    .NET 10 çözümü (Core kütüphanesi + App konsolu)
  launcher\                  .NET 10 WPF Windows launcher — operatör giriş noktası
    VortexArena.Launcher\    App/MainWindow (tek ekran: Sunucu / Bağlantı / Yönetim oyunu),
                             LauncherConfig (ayarlar + argümanlar), VenueCatalog (maps.json →
                             mekan listesi), Theme\Dark.xaml
    VortexArena.Launcher.Tests\  argüman sözleşmesi + maps.json ayrıştırma testleri
  docs-serve.bat             dokuman sitesini localhost:1111'de sunar (Quartz; icerik = Docs\,
                             motor repo DISINDA ..\vortexarena-docs-site — git'e girmez)
  scripts\                   deploy-admin-game.bat · deploy-player-apk.bat · deploy-server.bat
                             deploy-launcher.bat
                             docs-setup.bat (doküman sitesini yeni PC'de bir kez kurar)
                             defender-exclusions.cmd + .ps1 (Defender dışlamaları; yeni PC'de
                             bir kez, yönetici — build/import süresi için)
  deploy\                    ÜRETİLEN çalıştırılabilirler: admin\ player\ server\ launcher\
                             (git'e girmez)
  dev-targets.json           dev penceresinin adlandırılmış sunucu hedefleri (COMMIT'Lİ;
                             seçim EditorPrefs'te kişisel kalır — bkz. §6.2)
  Docs\                      dokumantasyon (docs-serve.bat bunu sunar)
    Gelistirici\             OYUN GELISTIRICISI icin giris kapisi: Ilk-Adimlar · Yemek-Kitabi
                             (receteler) · API-Referansi · Sahne-Kurulumu · Yapma-Listesi
  plan\  .claude\rules\  CLAUDE.md
```

**Bağımlılıklar hep aşağı akar; modlar birbirini referanslamaz:**

```
        VortexArena.Protocol      saf C#, noEngineReferences  ─┐
                 ▲                                            │ aynı .cs dosyaları
        VortexArena.Net           taşıma/keşif/senkron         │ Server csproj'da
                 ▲                                            │ <Compile Include>
        VortexArena.Core          oyun kodu (arena, savaş)     │ ile derlenir
           ▲          ▲                                       │
  VortexArena.App   VortexArena.Modes.Tdm / .Ffa           ──┘
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
MODUN ŞEKLİ         → SUNUCU OTORİTER    (ModeRules → load_match.rules → ModeRuntime; §3.9)
```

Neden böyle: oyuncu gerçekten fiziksel alanda yürüyor, pozu için tek doğru kaynak başlığın kendisi.
Ama can/skor iki istemcide sapmamalı → onlar sunucuda. İstemcide **can tutan hiçbir bileşen
yoktur**: can yalnız `health_update` ile gelir ve `PlayerCombatState` üzerinden okunur. Aynı kural
kırılabilir/yıkılabilir sahne objeleri için de geçerli olacak — hasar alabilen her şey ağsaldır
(`NetIdentity`), yerel bir can havuzu açılmaz.

### 3.3 Arena uzayı (koordinat çerçevesi)

Ağa giden **her poz arena uzayındadır** ve **arena uzayı = sahnenin dünya uzayıdır**: origin dünya
(0,0,0), rotasyon kimlik, eksenler duvarlara hizalı.

```
Quest'in kendi dünya uzayı ──(ArenaCalibrator: 2 nokta + OVRSpatialAnchor)──► arena uzayı
                                     │
                       ArenaSpace.WorldToArena/ArenaToWorld  (kimlik)
```

- `ArenaSpace`'in poz/pozisyon/rotasyon dönüşümleri **kimliktir**; çağrı yerleri yine ondan geçer,
  böylece koordinat çerçevesi tek bir yerde tanımlı kalır. `WorldToArenaDirection` kimlik değildir:
  yönü normalize eder (protokol her olayda bir **birim** yön taşır) ve sıfır/NaN girdide
  `Vector3.forward` döner.
- Bunun bağlayıcı sonucu: **arena geometrisi dünya orijinine göre kurulur** — arenanın zemini dünya
  y=0'da, merkezi dünya (0,0,0) civarında olur. Sahneyi topluca kaydırmak ya da döndürmek tüm
  oyuncuların ağ koordinatını kaydırır. Muhafaza (`ArenaBoundary`) bundan **bağımsızdır**: duvarı
  büyütmek/kaydırmak ağ koordinatlarının sıfırını oynatmaz.
- Dönüşüm **istemcide** yapılır (`PlayerPoseTracker`); sunucu ve admin ham arena koordinatı görür.
- Bütün başlıklar aynı fiziksel alana kalibre olduğu için, arena uzayı **tüm cihazlarda aynı fiziksel
  noktayı** gösterir — çakışan avatar / yanlış yerde görünen rakip sorununun çözümü budur.
- Hizalama **6DOF**'tur: yaw + yatay konum A→B çiftinden, **zemin yüksekliği B noktasında yakalanan
  kumanda ucundan**. Zemin tracking origin'den alınmaz çünkü başlıklar **guardian/alan kurulumu
  olmadan** çalışır (§7.29). Yakalanan nokta kumandanın pivotu değil ucudur
  (`ArenaCalibrator.floorProbeDropMeters`, **dünya -Y ekseninde**); iki noktanın Y farkı **eğim
  telafisi için kullanılmaz**, ölçüm sağlığı olarak denetlenir (>10 cm → yakalama reddedilir).
- ⚠️ **Bu ölçüme hiçbir rotasyon girmez:** ofset kumandanın YEREL ekseninde uygulanmaz, ne kumandanın
  tutuş açısı ne gözlüğün bakışı/yüksekliği sonucu değiştirir. Yaw yalnız iki yakalanan noktanın
  yatay farkından gelir.

### 3.4 Bağlantı yaşam döngüsü

```
İstemci açılır
  └─ adres zinciri (ROLDEN BAĞIMSIZ, AppBoot komut satırını her rolde okur):
       komut satırı --server-ip [--server-port] > elle girilen IP (PlayerPrefs)
       > beacon (5 sn dinle) > StreamingAssets/arena.json
  └─ VR (player): pratikte beacon — VR build'ine argüman geçilmez
                 → bulunan adrese OTOMATİK bağlanır; oyuncuya sorulmaz
                 → hiç bulunamazsa 8 sn sonra "joystick'e 1 sn basılı tut" ipucu (gizli IP paneli)
  └─ admin: adres launcher'ın geçtiği `--server-ip`'ten gelir; argüman yoksa bağlanmaz,
            sebebini ekranda yazar (editörde adres/rol dev penceresinden gelir → §6.2)
            → admin de Lobby sahnesinden bağlanır (ayrı dashboard sahnesi YOK)
  └─ ws://ip:47821/ws  →  hello{role, deviceId, scenes}  →  welcome{playerId, udpToken, match}
  └─ UDP kaydı: 0x00 UdpHello (ack gelene dek 1 sn'de bir tekrar)
  └─ status kalp atışı 5 sn  +  (player ise) poz döngüsü 20 Hz
  └─ welcome.match.phase ≠ Lobby ise → GEÇ KATILIM: maç sahnesine yetiş (admin dahil)
  └─ maç başlarken load_match → oyuncular + ADMİNLER aynı sahneyi yükler
       (admin'de yourTeam=""; admin set_ready GÖNDERMEZ)

Kopma → 1 → 2 → 5 sn backoff ile keşiften itibaren baştan (sonsuz, otomatik)
      → bağlantısızlık ~3 sn sürerse ConnectionOverlay hata ekranı (§4)
Sunucu → 15 sn status gelmezse çevrimdışı işaretle + bağlantıyı kapat + lobby_state yayınla
```

Elle girilen IP her zaman beacon'ı ezer ve `PlayerPrefs`'e kalıcı yazılır — işletmede beacon'ı
kesen/izole eden AP'lerde kurtarıcı budur. Açıkça verilen komut satırı adresi ise zincirin en
üstündedir: `LobbyController` onu `_manualEntry` sayar, böylece gelen bir beacon adresi EZMEZ.

### 3.5 Poz akışı (20 Hz)

```
PlayerPoseTracker (kafa + 2 el, dünya→arena) + HeldItems (elde ne var)
        │ IPoseSource
        ▼
UdpStateChannel ──0x01 PoseUpdate (95 B)──► StateHost ──0x02 Snapshot (≤1414 B)──► TÜM kayıtlı endpoint'ler
                                                       (olay varsa ve sığıyorsa 0x05 birleşik, §6.8)
                                                                                          │ (admin dahil)
                                                                     RemotePlayerRegistry ─┘
                                                          │ poz: 100 ms tamponla interpolasyon
                                                          │ eşya: interpole EDİLMEZ, son değer geçerli
                                                                    RemoteAvatar
```

**Kumanda düşünce poz akışı KESİLMEZ.** `0x01` sabit uzunluktadır ve telde "eli olmayan oyuncu"
diye bir durum yoktur — akışı kesmek ya da eli boş göndermek seçenek değil, alıcı tarafta gövdesi
çöken bir oyuncudur. `PlayerPoseTracker` bu yüzden geçersiz elin **son geçerli pozunu kafaya
göreli** saklar ve o kareki kafayla yeniden kurar; snapshot'ta el `FLAG_HAND_L_STALE` /
`FLAG_HAND_R_STALE` ile bayat işaretlenir (§6.3). ⚠️ Tutma **kafaya görelidir, arena uzayına
değil**: arena uzayında dondurulan bir el oyuncu yürüdükçe gövdenin arkasında kalır ve kol
uzayarak sahnede bir yere çakılır. ⚠️ Bayat el bir **tahmindir, ölçüm değil** — alıcı ona nişan
yönü, temas/isabet teşhisi ya da kavrama kararı dayandırmaz; bayrak tam da bunu ayırt edebilmek
için gider.

Eşya baytları (`itemL`/`itemR` + kavrama bitleri) pozla **aynı pakette** gider: ikisi de
istemci-otoriter sunum bilgisidir. Duruş telde GİTMEZ — eşyanın ele göre pozu her istemcinin
APK'sındaki `ItemDefinition`'dan gelir (ön koşul: kanonik kavrama). → `Docs/ArenaNet-Protokol.md` §6.6

### 3.5b Atış/atma olay akışı (olay başına yukarı, 20 Hz batch aşağı)

```
Weapon.Fire() ──► ArenaCombat.ReportShot (dünya→arena YÖN çevirimi)
        │
        ▼
UdpStateChannel ──0x03 FireEvent (12 B, HEMEN)──► StateHost (kapı: faz + canlı + kalibre;
        │                                          kopya bastırma: seq)
        │                                                   │ ConcurrentQueue
        │                                                   ▼
        │                          0x04 EventBatch (6+9n B, snapshot ile aynı tik) ──► TÜM endpoint'ler
        ▼                                                                                     │
  yerel FX + tracer: Weapon.Fire (anında — sunucu atana        RemoteShotFx ────────────────────┘
  kendi olayını GERİ YOLLAMAZ, ShotTracer.Shared havuzu)
                                                             │ kendi playerId'sini süzer
                                                             │ tik halkasıyla kopya ayıklar
                                                             │ serverTick'in oynatma anına kadar BEKLETİR
                                                       namlu alevi + ses + ShotTracer
```

⚠️ Atış **UDP**'de (kaybı kozmetik), `hit_report` **WS**'te (otoriter hasar). Kanal ayrımı
bilinçli — gerekçe §7'de ve `Docs/ArenaNet-Protokol.md` §10.3'te.

**Olay neden bekletiliyor:** uzak avatarın pozu bilerek `INTERP_DELAY_MS` (100 ms) geriden
çizilir, ama sunucu snapshot'ı ile olay batch'ini AYNI tik'te yayınlar. Olay geldiği anda
oynatılsa alev/ses/tracer elin **100 ms öncesindeki** yerinden çıkardı (kol 2 m/s ise ~20 cm
kayma). Bu yüzden `RemotePlayerRegistry` snapshot'ların `serverTick → alım zamanı` eşlemesini
tutar (global bir halka — tik başına bir snapshot var, eşleme oyuncu başına değil) ve
`TryGetPlaybackTimeMs` her olayın oynatılacağı yerel anı verir: `alım + INTERP_DELAY_MS`.
Sonuç olarak 20 Hz batch'leme **algılanan gecikmeye eklenmez** — ≤50 ms'lik batch beklemesi
100 ms'lik tamponun içinde erir. Eşleme yoksa (henüz snapshot gelmemiş / tik halkadan düşmüş)
olay **hemen** oynatılır: geciken tracer kabul edilebilir, kaybolan tracer edilemez.
⚠️ **Geçmiş pozu örnekleyen bir kapı YOK ve gerekmiyor** — tracer'ın orijini telden gelen bir
konum değil, o karede ÇİZİLMİŞ silahın namlusu (§6.4 "tutarlılık > sadakat"); olay doğru anda
oynayınca çizili namlu zaten o tik'in namlusudur.

- Kendi pozunu snapshot'tan **çizmezsin** (yerelden çizersin) — gecikme sıfır kalır.
- Uzak oyuncular `INTERP_DELAY_MS = 100` tamponuyla yumuşatılır; paket kaybı tolere edilir
  (son gelen kazanır, eski `seq` atılır).
- **Eşzamanlı oyuncu/admin sınırı YOKTUR** (kota ileride lisanslamayla gelecek). Tek tavan
  `PLAYER_ID_MAX = 255` ve o bir ürün kotası değil, `playerId`'nin UDP'de `u8` olmasıdır.
  16'dan fazla pozlu oyuncu olduğunda snapshot MTU'ya sığan parçalara bölünür
  (`SNAPSHOT_MAX_ENTRIES_PER_PACKET = 16` → 1414 B); **istemcide birleştirme yoktur ve
  gerekmez** — her paket taşıdığı girdileri bağımsız uygular, düşürme kararı zaman aşımıdır.
- Bir `playerId` ~1.5 sn snapshot'larda görünmezse avatarı kaldırılır (sunucunun 15 sn'lik
  offline eşiğini beklemez).

### 3.6 Vuruş hattı

```
Weapon.Fire() / balta savurma / ok isabeti / bomba patlaması
   │
   ├─ 0x03 atış olayı (UDP) → sunucu DOĞRULAMAZ, sadece relay eder (0x04 batch); istemcide
   │                `RemoteShotFx` tüketir (uzak namlu alevi + konumsal atış sesi + tracer,
   │                itemId profiliyle). Kaybı KOZMETİKTİR — güvenilirlik aranmaz (§6.4)
   └─ hit_report (WS) → sunucu 5 tutarlılık kontrolü yapar:
        faz Live? · atıcı canlı? · hedef canlı? (çift ölüm olmasın) ·
        hedef başkası + takım arkadaşı DEĞİL mi? · damage sonlu ve pozitif mi?
                            ↓ geçerse
        hp -= damage → health_update (herkese) → hp ≤ 0 ise kill_event + IGameMode.OnKill + respawn
```

**Hasarı istemci hesaplar, sunucu aynen uygular.** Sunucuda silah tablosu, `weaponId` beyaz listesi
ve atış hızı denetimi **yoktur** — ürün gözetimli özel alanda (işletme, turnuva) çalıştığı için
hile koruması bilinçli olarak eklenmez; böyle bir denetim meşru saçma/patlama/yaylım vuruşlarını
sessizce düşürür. Yukarıdaki beş kontrol hile denetimi değil **durum tutarlılığı**dır.

Pratik sonucu: **yeni bir hasar kaynağı eklemek sıfır sunucu işidir.** Bomba = etkilenen her hedef
için bir `hit_report` (mesafeye göre düşen hasarı istemci hesaplar); yay çekiş gücü, düşme/tuzak
hasarı da aynı şekilde `damage` alanına yazılır. **Bölge çarpanı bugün uygulanır:** her isabet
kutusu bir `HitZone` taşır (`RemoteHitBox.Zone`) ve `Weapon`, hasarı
`WeaponDefinition.GetZoneMultiplier(...)` ile çarpıp öyle bildirir — CS2 modeli: kafa 4×, karın
(karın + leğen) 1.25×, bacak 0.75×, gövde ve **kollar** 1×. `weaponId` yalnız kill feed
etiketidir, doğrulanmaz.
⚠️ **`HitZone` serialize ediliyor: yeni değer SONA eklenir** (Unity sayısal indeks saklıyor,
başa/ortaya ekleme prefabdaki kutuların bölgesini kaydırır) ve **`Body` sıfırdadır** — atanmamış
bir kutu en zararsız değere, 1× çarpana düşer.

⚠️ **Dost ateşi kapısı tek bir yardımcıdan geçer** (`MatchDirector.AreTeammates`) ve **boş takım
asla takım arkadaşı sayılmaz**: takımsız modda herkesin takımı `""` olduğu için düz
`a.Team == b.Team` karşılaştırması `"" == ""` ile TÜM vuruşları reddederdi. Kapının açık/kapalı
olmasını ise **operatör** belirler (`set_friendly_fire`, §3.9) — `ModeRules.FriendlyFire` yürürlükteki
anahtarı taşır, açıkken kapı hiç uygulanmaz.

⚠️ **Dost ateşiyle gelen takımdaş öldürme skor YAZMAZ:** öldürücü vuruş takım arkadaşınaysa
`IGameMode.OnKill` hiç çağrılmaz, yani ne takım skoru ne bireysel skor işler. `kills`/`deaths` ve
kill feed satırı normal işler — olay gerçekleşti, yalnız ödülü yok; ceza (−1) bilinçli olarak yok.
Kapı modun içine değil **çağrı yerine** konur: skorun tek yazarı `OnKill` olduğu için kural tek
noktada durur ve her yeni mod ona kendiliğinden uyar.

### 3.7 Free-roam canlanma (respawn) — ürünün en özel kuralı

Fiziksel oyuncu **ışınlanamaz**. Bu yüzden respawn bir **konum değil, DURUM değişimidir**:

```
ölüm → respawn{delaySeconds} → ölüm ekranı, ateş yok, avatar hayalet (yarı saydam)
     → süre dolar VE MODUN CANLANMA ŞARTI sağlanır → revive_request (~1 sn'de bir tekrar)
     → sunucu doğrular → health_update{hp:100} → canlı

operatör yolu:
ölü satır → admin CAN düğmesi → revive_player{playerId} (playerId:0 = tüm ölüler)
     → MatchDirector kapıları uygular → health_update{hp:100} → canlı
```

Şart `ModeRules.Revive`'dan gelir (§3.9) ve istemcide `ModeRuntime.Revive` olarak okunur —
`PlayerCombatState` içinde mod adına bakan hiçbir dal yoktur:

| `reviveAnchor` | Şart | Ölüm ekranı |
|---|---|---|
| `base` (varsayılan, TDM) | Oyuncu bir **taban bölgesine** (`BaseZone` — arenadaki kırmızı/mavi şerit) fiziken girer | "Tabanına dön ve canlan" |
| `standstill` | Ölüm anındaki HMD çapasından `REVIVE_HOLD_RADIUS` (1 m) içinde `REVIVE_HOLD_SECONDS` (5 sn) kesintisiz durur. ⚠️ İç engelin içinde sayaç **ilerlemez**, süre engelden çıkınca sıfırdan başlar | "Canlanmak için sabit dur — N sn" · engeldeyken "Engelden çık ve canlan" |
| `none` (turnuva) | **Canlanma yok.** İstemci `revive_request` hiç göndermez, sunucu gelirse reddeder. Ölü oyuncuyu modun başlattığı yeni tur canlandırır — ve yeni tur **herkes tabanına dönene kadar** açılmaz, yani ölü kalma süresinin üst sınırı yoktur (§3.8.2). Bu şartı yalnız operatörün elle canlandırması geçer (aşağıda) | "Elendin — takımın turu bitirene kadar bekle" |

⚠️ **Canlandırmanın İKİ yolu vardır, üçüncüsü yoktur:** oyuncunun `revive_request`'i
(`MatchDirector.HandleReviveRequestAsync`) ve operatörün `revive_player` komutu
(`MatchDirector.HandleAdminReviveAsync`, admin satırındaki **CAN** düğmesi). Sunucunun zamanlayıcı
tabanlı bir canlandırması, yani kendiliğinden işleyen bir emniyet ağı yoktur: şartı sağlamayan
oyuncu kendiliğinden geri gelmez — bu bilinçli bir üründür, canlanmak oyuncunun kendi işidir;
takılan oyuncuyu kurtarmak da operatörün işidir.

| Yasak | `revive_request` | Operatörün `revive_player`'ı |
|---|---|---|
| **Kalibrasyon** (§3.11) | Uygulanır | **Uygulanır** — kalibresiz oyuncu ateş edemez ve vurulamaz; canlandırmak onu savaşa döndürmez, yalnız tabloda "canlı" gösterir |
| **Engelin içinde olmak** (engel ihlali, `Docs/ArenaNet-Protokol.md`) | Uygulanır (tavanlı: `OBSTACLE_REVIVE_BLOCK_SECONDS`) | **Uygulanır** — engelin içinde canlanan kör kalır ve tolerans dolar dolmaz yeniden ölür, düğme bir ölüm döngüsü üretirdi |
| **`reviveAnchor:"none"`** | Uygulanır | **GEÇİLİR** — düğmenin varlık sebebi budur; ⚠️ turnuvada tur sonucunu değiştirir (tur, bir takımın tamamı ölünce biter) ve bu operatörün bilinçli kararıdır |
| **Canlanma gecikmesi** (`respawnDelay`) | Uygulanır | **GEÇİLİR** — operatör beklemez |

⚠️ **Bir yasak, o durumu değiştiren tüm yolları kapatmadıkça yoktur.** Kalibrasyon ve engel
yasakları bu yüzden iki yolda birden tekrarlanır; yalnız birine konsalardı tek bir operatör tuşu
ikisini de delerdi. Üçüncü bir canlandırma yolu eklenirse yasaklar orada da uygulanır.
Operatör komutu ayrıca faz `playing` ister (başka fazda ölü oyuncu kavramı yoktur), canlandırmayı
`RevivePlayerLocked` ile yapar ve **skor defterine/`deaths` sayacına dokunmaz**. Reddedilen komut
istemciye ret göndermez, sunucu konsoluna gerekçesiyle tek satır yazar.

**Taban bölgesi eşleşmesi:** bölge oyuncuya açıktır eğer takımı aynıysa, bölge `Neutral` ise
(herkese açık joker) ya da oyuncunun takımı boşsa (takımsız mod). Aynı takımdan birden çok bölge
varsa **herhangi birine** girmek yeter. Kapalı bileşen açık sayılmaz — `BaseZone.Update` koşmadığı
için `IsPlayerInside` donar, açık sayılsaydı oyuncu bölgeye girse de hiç canlanamazdı.

⚠️ **Şartı SUNUCU doğrulamaz** (§10.3 felsefesi: hakemlik değil defter tutar) — karar istemcinindir,
sunucu faz + ölü + gecikme kontrolüyle yetinir. Şart ölçülemiyorsa (sahnede açık taban bölgesi yok,
kamera yok) istemci onu sağlanmış sayar: bu sınıf hiçbir koşulda oyuncuyu kalıcı ölü bırakmaz.

⚠️ **Kod kuralı:** hiçbir bileşen rig'i/kamerayı taşımaz — ne canlanmada, ne harita değişiminde.
Protokolde konum/slot taşıyan bir alan **yoktur**; sunucu sahne geometrisini bilmez. Oyuncu
canlandığı yerde durur, ölüm ekranı kapanır.

⚠️ **Harita değişimi kalibrasyonu sıfırlamaz.** `load_match` oyuncu için yalnız bir sahne
değişimidir: kimse "yeniden doğmaz". Yeni sahnenin `ArenaCalibrator`'ı `Start`'ta kayıtlı
`OVRSpatialAnchor`'dan hizalamayı geri yükler (yükleme geçici düşerse 3 kez denenir). Ön koşul:
**aynı işletmede oynanan arenaların zemin işaretleri aynı yerde olmalı** — anchor fiziksel dünyada
sabittir, sanal işaretler sahneden gelir.

⚠️ **Poz gönderimi kalibrasyonu BEKLEMEZ.** `PlayerPoseTracker` rig anchor'larını bulur bulmaz
`IPoseSource` olarak kaydolur; hizalama gelmeden gönderilen pozlar arena ile örtüşmez (rig henüz
hizalanmadığı için ofsetlidir) ama gönderilir — oyuncunun bağlı ve hareket hâlinde olduğu ağdan
görülebilsin diye. Hizalama oturunca aynı kaynak kendiliğinden doğru uzayda poz verir; yeniden
kaydolma yoktur. Sonucu: uzak avatarlarda kalibrasyon öncesi konum
**kaymış görünür**, bu bir hata değildir.

### 3.8 Maçın durumu (sunucuda) — dört alan, dört sahip

Durum tek bir enum değildir. Dört alan taşınır ve her birinin **tek** sahibi vardır:

| Alan | Sahibi | Değerler | Anlamı |
|---|---|---|---|
| `modeId` | operatör seçimi | `lobby` · `tdm` · `ffa` · `tournament` | Ne oynanıyor. **Lobi de bir türdür** (§3.8.1) |
| `phase` | çekirdek | `paused` · `playing` · `finished` | Maçın genel durumu |
| `phaseReason` | çekirdek | `lobby` · `loading` · `countdown` · `operator` · `mode` | Neden duraklı |
| `modeState` | mod (`IGameMode`) | serbest string | Modun kendi ara durumu; çekirdek yorumlamaz |

```
              start_match                 herkes set_ready | 20 sn
paused ─────────────────────► paused ──────────────────────────────► paused
(lobby)                       (loading)                              (countdown 5 sn)
   ▲                                                                    │
   │ return_to_lobby                                                    ▼
   └──────────── finished ◄──── süre bitti | skor limiti ◄───────────  playing
        (operatör seçer; ~16,5 dk emniyet)                            ▲    │
                                                                      └────┘ duraklat / devam
                                                                  paused(operator|mode)
```

⚠️ **Kazanan ekranı kendiliğinden kapanmaz — operatörü bekler.** `finished` fazından çıkaran şey
fazı değiştiren her komuttur: harita seçmek (ya da harita listesindeki lobi satırı), `start_match`,
`abort_match`/`return_to_lobby`. `MATCH_END_SECONDS` (999 sn) bunların hiçbiri gelmezse devreye
giren **emniyettir, akış değil**: turnuvada tur/maç aralarını sahada hakem yönetir ve 10 sn'lik
otomatik dönüş kazanan ekranını hakemin elinden alıyordu.

⚠️ **`phase`'in tek yetkisi hasar kapısıdır:** `hit_report` yalnız `playing`'de işlenir. Başka
hiçbir kural doğrudan faza bakmaz — "ateş edebilir miyim", "silahım nereden gelir", "hangi HUD"
sorularının cevabı **moddan** gelir (§3.9). Sebebi ileriye dönük: turnuva gibi kendi ara durumu
olan bir mod, çekirdek enum'unu büyütmek yerine `phaseReason:"mode"` + `modeState` kullanır.

⚠️ **`modeState` asla kural/hasar kapısı olamaz.** Çekirdek onu okumaz, yalnız HUD okur. Mod
duraklatmak isterse çekirdekten `paused` + `phaseReason:"mode"` ister, gerekçesini `modeState`'e
yazar; operatör duraklatması (`operator`) ile karışmaz, o yüzden mod kaldığı yerden sürebilir.

**Operatör duraklatması** `pause_match` / `resume_match` ile gelir (§5.2). Duraklatmada süre
kendiliğinden durur (sayaç yalnız `playing` tikinde azalıyor), hasar kapanır, skor/can/`modeState`
ellenmez. ⚠️ **`resume_match` yalnız `phaseReason == "operator"` iken kabul edilir** — her
duraklamayı kendi sahibi kaldırır: modun istediği duraklamayı operatörün kaldırması modun ara
durumunu bozar, geri sayımı elle bitirmek yükleme kapısını atlar. Duraklatmak maçtan çıkmak
değildir; çıkış hâlâ `abort_match`/`return_to_lobby`'dir ve ikisi duraklı maçta da çalışır.

`start_match` doğrulaması (sırayla): mod kayıtlı mı → sahne adı boş değil → sahne `maps.json`'da
var ve modu destekliyor mu (**tablo boşsa bu adım atlanır**) → sahne TÜM oyuncuların
`hello.scenes` listesinde. Geçerse takımlar **modun şekline göre** kurulur (takımlıda dengelenir,
takımsızda temizlenir) ve herkese **kişisel** `load_match` (kendi `yourTeam` + maçın `rules`'ü) gider.

**Süre, skor limiti ve geri sayım o maça özel olabilir:**
`start_match.roundSeconds`/`scoreLimit`/`countdownSeconds` doluysa onlar koşar, boş/`0` ise modun
(geri sayımda protokolün) varsayılanı. Yani `ModeDefinition`/`IGameMode` sayıları **kilit değil
varsayılandır** — operatör raundu kısaltıp uzatabilir. Seçim mod/harita ile aynı ortak kanaldan
gider (`set_selection` → `admin_state`), çünkü parametreler yerel kalsaydı bir operatörün 5 dk
sandığı maç diğerinin seçtiği 30 dk ile başlardı. `countdownSeconds` sunucuda 5–30 sn aralığına
kırpılır ve maçın **her** geri sayımında kullanılır — tur tabanlı modda turlar arasındaki bekleme
de odur (§3.8.2).

### 3.8.2 Tur tabanlı modlar — çekirdek TUR diye bir şey bilmez

`tournament` bir maçı **turlara** böler ama `Phase` enum'u bunun için büyümedi. Turun tamamı
`TournamentMode` içinde yaşar; çekirdek yalnız dört yetenek sunar ve hiçbirini yorumlamaz:

| Çekirdek API | İş |
|---|---|
| `TryPauseForMode(modeState)` | `playing` → `paused`/`mode`; süre 0'lanır, `ready` bayrakları temizlenir |
| `SetModeState(modeState)` | Ara durumu yazar, **yalnız değiştiyse** `match_state` yayınlar |
| `TryStartRound()` | `paused`/`mode` → geri sayım → `playing` (çekirdeğin normal yolu) |
| `TryCancelCountdownForMode(modeState)` | Geri sayımı geri alır: `paused`/`countdown` → `paused`/`mode` |

Akış: tur `playing`'de koşar → mod turu bitirir (eleme ya da süre) → maç bitmediyse
`paused`/`mode` + `modeState:"regroup:2/6"` → herkes kendi tabanına yürüyüp `set_ready{true}`
yollar → geri sayım → yeni tur. **Geri sayım her koşulda geri alınabilir:** biri tabanından çıkıp
bayrağını düşürürse mod geri sayımı iptal eder ve toplanmaya döner; istisnası yoktur. Kural
"tabanda **bekle**"dir, "tabana uğra" değil — şart girişte bir kez değil, tur açılana kadar
**sürekli** ölçülür.

**Toplanma zaman aşımı YOKTUR: tur, herkes kendi tabanına girmeden başlamaz.** Eksik oyuncuyla
zorla başlatılan bir tur turnuvada hakemin istemediği bir turdur; beklemenin çıkışı da zaten
operatördedir — takılan oyuncuyu **atar** (`kick`) ya da `abort_match` yapar. Atılan/kopan oyuncu
toplamdan düştüğü için kalanlar hazırsa tur o an başlar: sayım her tikte çevrimiçi oyunculardan
yeniden yapılır, "kaç kişiydik" diye bir kayıt tutulmaz. Bekleme uzarsa sunucu konsoluna 30 sn'de
bir *"toplanma bekleniyor (4/6) — tabanına dönmeyenler: …"* satırı düşer; bu bir **teşhis**
satırıdır, tur başlatmaz.

Dört şey bunu mümkün kılıyor ve ilk üçü **zaten vardı**:

1. **`phaseReason:"mode"`** — duraklamanın sahibi mod. `resume_match` onu kaldıramaz (§3.8), yani
   operatör modun ara durumunu kazara bozamaz.
2. **`set_ready` bayrağı** — yükleme kapısında zaten "hazırım" demek. Toplanma kapısı onu yeniden
   kullanır; **yeni protokol mesajı yok**. "Tabanda mıyım" kararı istemcinindir (§3.7 felsefesi).
3. **Mod tik'i duraklamada da koşar** — `paused`/`mode` **ve** `paused`/`countdown`'da. Operatör
   duraklatmasında (`operator`) mod tik ALMAZ: donmuş maç donmuş kalır.
4. **Mod kendi aşamasını hatırlar** (`RoundStage`). `phaseReason` yetmiyor: maçın ilk geri sayımı
   da `paused`/`countdown`'dur ama toplanmadan gelmez, orada iptal edilecek bir şey yoktur.
   Aynı ayrımı istemci de kendi tarafında yapar (raporlayıcı yalnız toplanmadan gelen geri sayımda
   çalışır). Çekirdeğin `PauseReason`'ını dışarı açmak ikinci bir okuma yolu olurdu.

⚠️ **`TryStartRound` `ready` bayraklarını TEMİZLEMEZ.** Toplanmada o bayrak "şu anda tabanımdayım"
demektir ve geri sayım boyunca canlı kalması gerekir — iptal kararının tek dayanağı odur.
Temizleyen tek yer toplanmanın **başıdır** (`TryPauseForMode`).

⚠️ **Tur başında sunucu herkese `health_update` yollar** (`RevivePlayerLocked`). Sunucu içi
alanları sessizce sıfırlamak yetmez: maç içi tur geçişinde `load_match` yoktur, yani istemcinin
kendini sıfırlayacağı ikinci bir yol da yoktur — mesaj gitmezse tur içinde ölmüş oyuncu ölüm
ekranında donar.

⚠️ **Mod kancaları `await` edemez** (`OnTick` `void`). Bu yüzden yukarıdaki üç API mesajı doğrudan
göndermez: kilit altında bir bekleyen kutuya yazar, tik döngüsü kanca dönüşünde yollar. Tek
gönderici → sıra korunur, kilit altında gönderim olmaz.

### 3.8.1 Lobi — bir FAZ değil, bir TÜR

Lobi "hiçbir şey olmuyor" durumu değil, **işletmenin kendi odası**dır. Maç koşmadığı sürece
oyuncular ve admin orada durur: birbirlerini görürler, **kalibrasyonlarını orada yaparlar**
(harita değişimi kalibrasyonu sıfırlamadığı için maça hazır girilir), silah çerçevesinden silah
seçip hedeflere ateş edebilirler.

- **Lobi bir türdür** (`modeId:"lobby"`), faz `paused` + `phaseReason:"lobby"`dir. Tür yalnız lobi
  haritasında olur ve o türdeyken **maç başlatılamaz** — ikisi de `maps.json`'daki `modes`
  alanından gelir (`supportedModeIds == ["lobby"]`), ayrıca bir kural yazılmaz.
- **Sunucunun her zaman bir açık sahnesi vardır** ve istemcinin tek yönlendirme kaynağı odur:
  `welcome.match.sceneName` / `return_to_lobby.sceneName`. Açılışta bu, mekanın lobi haritasıdır
  (`server.json → lobbyScene`, boşsa otomatik bulunur). ⚠️ **Çözülemezse sunucu açılmaz** — sessizce
  boş sahneyle açılmak hatayı sahaya taşırdı.
  **Adminlerin ortak seçimi de (`admin_state`) aynı değerle tohumlanır**, yani sunucu ayaktayken
  "harita seçilmemiş" diye bir durum yoktur. Lobi mod/harita seçicilerinde bulunmadığı için panel
  imleci yerinde kalır ve o an açık olanı MAÇ bölümünün başlığı yazar — imleç "bir sonraki maçın
  adayı", açık sahne "şu an yüklü olan"dır; ikisi maç sonrası ayrışır (herkes lobiye döner,
  imleç son arenada kalır).
- **Operatör arena "sahneler":** admin panelinden harita seçmek `set_selection` gönderir, sunucu da
  o arenayı `return_to_lobby` ile **tüm istemcilere** yükletir (`MatchDirector.StageSceneAsync`).
  Oyuncular maç başlamadan arenaya girip kalibrasyonunu yapar ve yerini alır. Faz `paused` kalır,
  tür `lobby` kalır; doğrulama `start_match` ile aynıdır (sahne tabloda + her oyuncunun build
  listesinde). ⚠️ **Koşan maçta olmaz** — sahne komutu herkese gittiği için maçın ortasında harita
  değiştirmek maçı bozardı; `finished` iken ise serbesttir (operatör bir sonrakini seçebilsin).
- **Oyuncuya hasar imkânsızdır:** `hit_report` yalnız `playing` fazında işlenir. Hasarı kapatan şey
  bir kural bayrağı değil, **fazın kendisidir**.
- **Atış görünür:** `shot_fired` relay'inin kapısı `playing` **veya** `rules.fireWhilePaused`'dur.
  İki kapı bilerek ayrı: atış bir sunum olayı, vuruş bir durum değişimidir. Yani **hasarı faz,
  ateşi mod** kapatır — lobiyi "hasarsız atış alanı" yapan tam olarak bu ayrım.
- **Taban şeritleri seçili moda göre görünür/gizlenir** (`selection_state.teamMode` → `ModeSelection`
  → `BaseZoneVisibility`): admin TDM/turnuva seçtiyse şeritler durur, FFA seçtiyse maç başlamadan
  kaybolur. Aktif kural bu sırada hâlâ lobi profilidir — değişen yalnız sunumdur. Kapının silah
  kaynağı OLMAMASININ sebebi de bu: lobide silah her hâlükârda rastgeledir.
- **Silah gelir** çünkü `modeId` boş değil `"lobby"`dir — istemci loadout'u
  `GameCatalog.FindMode(ModeRuntime.ModeId)` ile çözüyor. Lobinin kaynağı
  `weaponSource:"random"`: grip'e basılı tutulan elde rastgele bir silah durur, bırakınca yok olur.
  `ModeRules.LobbyProfile` bilinçli olarak `RandomGrant` taşır — `WeaponCanvas` seçilseydi her
  lobi sahnesine elle silah yerleştirmek gerekirdi. ⚠️ Aynı profil **sahnelenen arenada da**
  geçerlidir ve orada sahnede silah VARDIR: iki yol birden açıktır (tezgâhtan seçilen silah, ya da
  hiç seçilmediyse loadout'tan rastgele biri) ve tezgâhlar **gizlenmez** — gizleme yalnız kurulmuş
  bir maçta koşar (§7, "sahnelenen arena lobi profiliyle koşar").
- **Takımı yalnız admin atar** (`set_team`), üstelik **her fazda** — koşan maçın ortasında da.
  Oyuncunun kendi takımını seçmesi için protokol mesajı yoktur.

> ⚠️ **Lobi bir maç DEĞİLDİR ve yapılmayacaktır.** `playing`'e taşınsaydı hasar kapısı açılır,
> ayrıca yükleme/geri sayım/tur sayacı/`finished` yaşam döngüsü ve `return_to_lobby`'nin kendini
> çağırması gibi lobide karşılığı olmayan bir makine devralınırdı. "Maç koşuyor mu?" sorusunun tek
> cevabı `phase == playing`'dir. Ayrıntı: `Docs/ArenaNet-Protokol.md` §10.7.

### 3.9 Mod kuralları (`ModeRules`) — modun şekli

Bir modun "ne tür bir oyun olduğu" **sunucu-otoriter** bir şekil tanımıyla anlatılır ve telden
gelir. Amaç tek: **istemci modun ne olduğunu TAHMİN ETMESİN.**

```
IGameMode.Rules ─┐
                 ├─► MatchDirector ─►  load_match.rules / welcome.match.rules
dost ateşi ──────┘   (ApplyRulesLocked) rules_update  (maç ORTASINDA değişirse)
anahtarı (operatör)                          │
                                             ▼
                                          ModeRuntime (istemcide TEK okuma noktası)
                                          ├─ PlayerCombatState  (canlanma şartı, gecikme)
                                          ├─ ModeHudBase        (skor satırı alt sınıfta)
                                          └─ AdminRoster        (tek kolon mu, çift kolon mu)
```

| Kural | Değerler | Varsayılan | Ne değişir |
|---|---|---|---|
| `Teams` | `TwoTeams` / `None` | `TwoTeams` | Sunucu: takımları dengele mi temizle mi. İstemci: avatar rengi, admin kolonu, taban bölgesi eşleşmesi |
| `Scoring` | `Team` / `Player` | `Team` | Skor `match_state.scoreRed/Blue`'ya mı `PlayerInfo.score`'a mı yazılır |
| `FriendlyFire` | bool | `false` | `hit_report` dost ateşi kapısı. ⚠️ **Modun değil operatörün alanı** — aşağı bak |
| `Revive` | `OwnBase` / `StandStill` / `None` | `OwnBase` | Canlanma şartı (§3.7). `None` = tur içinde canlanma yok |
| `Weapons` | `WeaponCanvas` / `RandomGrant` | `WeaponCanvas` | Silah sahnedeki **çerçevelerden** mi seçilir (ele klonlanır, tükenmez) yoksa mod mu dağıtır. **Yalnız istemci sunumu** — sunucuda karşılığı yok. ⚠️ `WeaponCanvas`'ta silahı sahneye koyan bileşen YOKTUR: yerleşim arena kararıdır, harita tasarlanırken elle konur. FFA ve lobi `RandomGrant` kullanır |
| `RespawnDelay` | saniye | `RESPAWN_DELAY` (5) | `respawn.delaySeconds` + sunucudaki gecikme eşiği |
| `FireWhilePaused` | bool | `false` | Faz `playing` değilken ateş edilebilir mi (§3.8.1). Lobi türünde `true`; **hasar yine yok** — onu faz kapatır. Bu alan sayesinde istemcide `if (modeId == "lobby")` zinciri doğmaz |

**Kayıtlı modlar** (somut örnek — TDM tüm varsayılanları alır, FFA beş alanı, turnuva yalnız
**iki** alanı farklı yazar):

| | `tdm` — Takım Ölüm Maçı | `ffa` — Herkes Tek | `tournament` — Turnuva |
|---|---|---|---|
| `Teams` | `TwoTeams` | **`None`** | `TwoTeams` |
| `Scoring` | `Team` | **`Player`** | `Team` (skor = **kazanılan tur**) |
| `Revive` | `OwnBase` | **`StandStill`** (5 sn / 1 m) | **`None`** (tur içinde canlanma yok) |
| `Weapons` | `WeaponCanvas` (çerçeveden seçilir, kalıcı) | **`RandomGrant`** (grip'e basınca elde rastgele silah) | `WeaponCanvas` (şarjör/yedek şarjör işlesin diye) |
| `RespawnDelay` | `5` | **`0`** (bekleme yerine sabit durma şartı) | **`0`** (canlanma yok, sayaç göstermek yalan olurdu) |
| Süre / limit | 300 sn / 30 | 300 sn / 20 | 120 sn (**turun** süresi) / 4 tur |

⚠️ **Turnuvada `roundSeconds` TURUN süresidir, maçın değil** ve `scoreLimit` maçı kazanmak için
gereken tur sayısıdır (tavan `2 × limit − 1` tur). Tur kavramı `ModeRules`'a **girmez** — turlar
modun iç durumudur (§3.8.2).

- **Varsayılan = bugünkü TDM.** Yeni mod yalnız FARKLI olduğu alanı yazar (`TdmMode.Rules =>
  ModeRules.TeamDefault`).
- **Dost ateşi takımsız modu KİLİTLEMEZ:** boş takım asla takım arkadaşı sayılmaz, FFA'da
  herkesin takımı `""` olduğu için kapı hiç kapanmaz (§3.6).

- **Bilinmeyen/boş değer varsayılana düşer** (değerler bilerek string) → yeni bir kural değeri
  eklemek `PROTOCOL_VERSION`'ı artırmaz.
- **Kurallar telde gelmediğinde** (`rules == null` — kuralları taşımayan bir sunucu) `ModeDefinition`'ın
  önizleme alanları fallback olarak devreye girer; **sapmada sunucu kazanır** — kural taşıyan bir
  `load_match` bunları ezer.
- Tam semantik: `Docs/ArenaNet-Protokol.md` §10.5.

**⚠️ `FriendlyFire` bu tablonun tek istisnasıdır: mod kuralı değil OPERATÖR ANAHTARIDIR.** Değeri
sunucuda yaşar (`MatchDirector`, açılışta kapalı), yalnız admin komutu `set_friendly_fire`
değiştirir ve etkisi anlıktır — koşan maçta da geçerlidir, çünkü sahadaki durumu (takımlar karıştı,
antrenman yapılıyor) operatörün maçı iptal etmeden düzeltebilmesi gerekir. Maç başlangıcı, harita
sahneleme ve lobiye dönüş anahtarı **sıfırlamaz**; sıfırlayan tek şey sunucunun yeniden
başlatılmasıdır (süre/limit seçimiyle aynı sözleşme).

- **Modlar bu alanı bildirmez.** Bir modun kendi kuralında ona değer yazması operatörün anahtarını
  sessizce ezerdi. `ModeRules.FriendlyFire` telde "o an geçerli değer"i taşır, "bu modun tercihi"ni
  değil.
- **Yürürlükteki şekli yazan tek kapı `MatchDirector.ApplyRulesLocked`'tır:** modun (ya da lobinin)
  kural şeklini alır, üstüne anahtarı damgalar.
- **Maç ortasında değişince `rules_update` herkese yayınlanır** ve istemcide `ModeRuntimePump` onu
  `ModeRuntime`'a uygular. `selection_state`'in aksine bu **gerçek bir kural mesajıdır**: o yalnız
  sunuma (taban şeritleri) dokunur, bu aktif kuralı değiştirir. Geç bağlanan doğru değeri yine
  `welcome.match.rules`'tan alır — mesajın kaybı kalıcı sapma üretmez.
- Adminler arası senkron `admin_state.friendlyFire` ile sağlanır: seçim değil **yürürlükteki
  durum** olduğu için diğer alanların "0/boş = değişmedi" sözleşmesine girmez ve panelde maç
  kuruluyken de basılabilir (mod/harita gibi kilitlenmez).

### 3.11 Kalibrasyon durumu — operatörün kaldıracı

Bir başlığın hizalı olup olmadığı **sunucuda** tutulur (`lobby_state.calibrated`) ve sahadaki en
sık sorunu çözer: bir oyuncunun kalibrasyonu kayar, avatarı fiziksel konumundan sapar.

**Akış:** operatör admin ekranında o satırın **KAL** düğmesiyle kalibrasyonu sıfırlar → oyuncu
**ateş edemez, hasar yemez, canlanamaz**, diğer herkesin ekranında **avatarı parlar** ve vuruş
kutuları kapanır → oyuncu elle (ya da kayıtlı anchor'dan) yeniden kalibre olur → gözlük
`set_calibration` yollar → tik geri yanar ve oyuncu **kaldığı yerden devam eder** (can/K-D/skor
korunur, bu bir ceza değil geçici dondurmadır).

**Asimetri kasıtlıdır:** admin yalnız SIFIRLAYABİLİR, "kalibre oldu" diye işaretleyemez —
hizalamanın gerçekten oturduğunu yalnız başlık bilir. Admin elle işaretleyebilseydi, sunucunun
hizalı sandığı ama fiilen kaymış bir oyuncuya ateş ve hasar açılırdı.

⚠️ **Sıfırlama bir DURUM değil bir KOMUTtur ve koşulsuzdur.** Sunucu yalnız roster'daki bayrağı
düşürmez, hedef başlığa `clear_calibration` **iletir** (`identify` deseni) ve başlık hizalamayı,
kayıtlı anchor'ı ve **yarım kalmış elle kalibrasyon sekansını** birlikte siler. Roster tek başına
yetmemesinin sebebi şudur: A'sını almış ama B'sini almamış bir oyuncuda `calibrated` **zaten**
`false`'tur — sıfırlamanın orada görünür bir deltası yoktur. Bu yüzden zincirin üç halkası da
(admin arayüzü · sunucu · istemci) oyuncunun o anki durumuna bakmaz; birini duruma bağlamak,
komutu var olma sebebi olan durumda işlevsiz bırakır (§7).

**Kalibreliyken elle kalibrasyon kilitlidir:** oyuncu kendi hizalamasını kazara bozamaz, kapıyı yalnız operatör
açar. Hiç bağlanılmamışsa (sunucusuz editör testi) kapı açıktır ve silah çalışır.

⚠️ **Poz gönderimi buna bağlı DEĞİLDİR** — kalibresiz oyuncu poz göndermeye devam eder (§3.5).
Bilinçlidir: operatörün "avatar kaymış" teşhisini koyabilmesi ve parlayan avatarın hareket ettiğini
görebilmesi için pozun akıyor olması gerekir.

⚠️ **Parlamayı çizen liste `RemoteAvatar.bodyRenderers`'tır** — takım rengini taşıyan
`teamRenderers`'tan AYRI tutulur, çünkü takım rengi karakter mesh'ine bilerek uygulanmaz (düşmanı
işaretlemek duvar arkasından okunabilen bir avantaj olurdu). İkisi tek listeye bağlanamaz; liste
boş bırakılırsa **uyarı sessizce hiç çizilmez** ve konumu yalan söyleyen avatar normal görünür —
sahada tam olarak bu yaşandı (§7).

Tam semantik: `Docs/ArenaNet-Protokol.md` §10.6.

### 3.12 Bant, paket ve airtime bütçesi

**Sonuç önce: bant genişliği bu üründe hiçbir zaman darboğaz değil, paket sayısı olabilir.**
Kablosuzda maliyet bayt başına değil **çerçeve başına** ödenir; ürünün trafiği ise "az bayt, çok
çerçeve" desenindedir (20 Hz × hedef sayısı kadar minik datagram).

**Ağ tick'i 20 Hz'dir** (`SNAPSHOT_RATE_HZ` / `POSE_RATE_HZ`). `MatchDirector`'ın 10 Hz maç tick'i
(`TickIntervalMs`) ağa hiçbir şey yazmaz — faz/süre/zorla canlanma çözünürlüğüdür.

**Hesap formülleri** (`N` = pozlu oyuncu, `A` = admin, hedef sayısı `N+A`; snapshot her hedefe
**ayrı unicast** gider, multicast yoktur):

```
snapshot   (sunucu TX) = SNAPSHOT_RATE_HZ × (6 + 88×N) × (N+A)     ← N² büyür
                         olay varken tek pakette birleşir (0x05, §6.8): (7 + 88×N + 9×E/20) × (N+A)
pose       (sunucu RX) = POSE_RATE_HZ × 95 × N
skeleton   (sunucu RX) = SKELETON_RATE_HZ × (34 + B) × N            ← B = blob boyu
           (sunucu TX) = SKELETON_RATE_HZ × ⌈(31+B)×N / 1200⌉ datagram × (N+A)   ← N² büyür
health_update (sunucu TX, TCP) = isabet/sn × (1 + admin sayısı) × ~140 B   ← N ile büyümez
rttProbe   (her iki yön) = 1 Hz × (N+A) × 2 datagram
```

⚠️ **İskelet kanalı bu bütçenin en duyarlı kalemidir** ve tasarımı ona göredir: oyuncu başına ayrı
datagram yollansaydı `SKELETON_RATE_HZ × N × (N+A)` paket ederdi, batch'leme (§6.10) onu
`⌈(31+B)×N / 1200⌉` ile böler. `B` bilinmeden hesap yapılmaz — **ölçülür** (sunucu konsolundaki
`[state]` satırında `iskelet … p/s`). Kanal ayrıca poz kanalından **daha düşük hızda** akar; oradaki
sayı bir akıcılık ayarı değildir, alıcıda SDK'nın kendi interpolasyonu koşuyor.

Snapshot'ın `N²` olmasının sebebi bilinçli: her oyuncu diğerlerinin pozunu **ve kendi pozunu**
alır (kendi pozunu yok sayar — §3.5). Kazancı hedef başına serileştirme yapmamaktır.
`health_update`'in `N` ile büyümemesi ise sonradan kazanıldı (aşağıya bak).

**Örnek: 10 oyuncu + 1 admin, aktif çatışma** (10 oyuncu da ~600 RPM = 100 atış/sn toplam,
~%25 isabet). IP+UDP/TCP başlıkları dahil, 802.11 çerçevelemesi hariç. ⚠️ **Hesaplanmış
değerlerdir** — sunucu konsolundaki `[state]` satırı bunları artık ölçüyor; sapma olursa doğru olan
ölçümdür:

| Kanal | Yön | Bant | paket/sn | (önce) |
|---|---|---|---|---|
| `0x05` snapshot+olay birleşik (932 B/tik/hedef) | ↓ | 1,69 Mbps | 220 | 440 |
| WS `health_update` + `kill_event` | ↓ | 0,06 Mbps | 50 | 275 |
| WS `match_state` (1 Hz) | ↓ | 0,02 Mbps | 11 | 11 |
| `0x06` RTT echo | ↓ | ~0 | 11 | — |
| WS `net_stats` (1 Hz, admin) | ↓ | ~0 | 1 | — |
| `0x01` poz | ↑ | 0,20 Mbps | 200 | 200 |
| `0x03` atış | ↑ | 0,03 Mbps | 100 | 100 |
| WS `hit_report` | ↑ | 0,03 Mbps | 25 | 25 |
| `0x06` RTT yoklaması | ↑ | ~0 | 11 | — |
| **Toplam** | | **~2,0 Mbps** | **~630** | **~1.050** |

Sakin durumda (lobi, atış yok) `0x02` + poz + yoklama kalır: **~1,8 Mbps, ~450 paket/sn**.

**1 Gbps'lik bir AP'ye göre bant %0,2'dir.** Buna karşılık ~730 çerçeve/sn (TCP ACK'leri dahil),
802.11ax'te küçük unicast çerçeve başına ~120–200 µs havayı tuttuğu için **tek radyoda ~%10–12
airtime** demektir (fan-out kesilmeden önce ~%15–20 idi). Yani darboğaz bant değil airtime;
konforlu üst sınır kabaca **2.500–3.500 çerçeve/sn**, bugünün ~4–5 katı. (AP'nin DL/UL OFDMA'sı
çalışıyorsa bu tavan belirgin yükselir — bu iş yükü tam OFDMA'nın tasarım hedefi; ama consumer
AP'lerde garanti sayılmaz.)

**Paket sayısı nasıl düştü** (~1.050 → ~630, %40):

1. **`health_update` broadcast olmaktan çıktı** → kurban + adminler (§10.3). İki tüketicisi de dardı:
   istemci kendisine ait olmayanı **zaten düşürüyordu**, admin ise tabloyu çiziyor. 10 oyunculu
   maçta her isabette 11 mesaj gidip 9'u çöpe atılıyordu. **−225 paket/sn.**
2. **`0x02` + `0x04` tek datagramda birleşti** (`0x05`, §6.8) — sığdığı sürece. **−220 paket/sn.**
3. **RTT yoklaması eklendi** (§6.7): **+22 paket/sn** — bu planın paket EKLEYEN tek parçası, ve
   bilinçli olarak 1 Hz'de tutuluyor.

⚠️ **Sunucu PC kabloyla bağlanmalıdır.** Wi-Fi'daysa her downstream paket havayı iki kez geçer
(sunucu→AP, AP→istemci) → airtime ikiye katlanır. Sahadaki AP/ağ kontrol listesi:
`Docs/Isletme-Kurulum.md` "Ağ" bölümü.

**Ne ölçülüyor** (§6.7): uplink jitter/kayıp ve sunucu tik kayması **sunucuda** (konsol `[state]`
satırı), downlink jitter/kayıp ve RTT **istemcide** (snapshot varışlarından — RTT dışında ek paket
yok) → `status` → `net_stats` → admin panelinde **PING** kolonu. Yön asimetriktir: 802.11'de yukarı
ve aşağı simetrik bozulmaz, bu yüzden iki taraf ayrı ölçülür. Hacim sayıları bilinçli olarak yalnız
konsoldadır, panelde gösterilmez.

⚠️ **Sunucu PC kabloyla bağlanmalıdır.** Wi-Fi'daysa her downstream paket havayı iki kez geçer
(sunucu→AP, AP→istemci) → airtime ikiye katlanır. Sahadaki AP/ağ kontrol listesi:
`Docs/Isletme-Kurulum.md` "Ağ" bölümü.

**Ölçeklendiğinde ilk çarpılacak tavanlar, sırayla:**

1. **Airtime / paket-sn** — yukarıdaki tavan. Bağlayıcı kısıt budur.
2. **MTU 1500** → `SNAPSHOT_MAX_ENTRIES_PER_PACKET = 16` ve `COMBINED_MAX_BYTES = 1200`. Girdi
   büyürse (ör. tam gövde izleme) ikisi de düşer; ayrıca 16 girdiyi aşan tik **birleştirilemez**
   (§6.8) ve o tik'te paket sayısı bugünkü iki katına döner.
3. **`u8 playerCount`** ve `PLAYER_ID_MAX = 255`.
4. **Quest 3 CPU'su** — 20 Hz'de 10 uzak avatarı interpole etmek bedava; 60 Hz tam gövde değil.

Karşılaştırma için: 60 Hz snapshot 10 oyuncuda 4,7 Mbps / 660 paket-sn; 20 oyuncu bugünkü formatta
5,9 Mbps / 420 paket-sn; 32 oyuncu 14,9 Mbps / 1.320 paket-sn (16 girdi sınırı aşıldığı için tik
başına iki datagram **ve birleştirme devre dışı**). **Hiçbiri bant sorunu değil, hepsi airtime
sorunu.**

---

## 4. Bileşen sözlüğü — kim ne yapıyor

### İstemci: `VortexArena.Net` (ağ, oyun bilmez)

| Sınıf | Görevi |
|---|---|
| `ArenaClient` | Kalıcı tekil; WS bağlantısı (arka plan Task + `ConcurrentQueue` → ana thread köprüsü), hello/welcome, status kalp atışı, otomatik reconnect. **Tüm mesaj gönderimi buradan.** Teşhis için `ConnectAttempts` (son başarılı bağlantıdan beri kaçıncı deneme; bağlanınca 0) + `LastError` (son bağlanma hatası) — `ConnectionOverlay` bunları gösterir. `Disconnect()` otomatik yeniden denemeyi **durdurur** (dönüş yalnız açık `Connect` ile) |
| `ServerDiscovery` | Beacon dinleme (Android'de MulticastLock), elle girilen adresin `PlayerPrefs`'e yazılması, `arena.json` fallback |
| `UdpStateChannel` | UDP kaydı (`0x00`), 20 Hz poz + eşya gönderimi (`0x01`), snapshot alımı (`0x02` ve birleşik `0x05`), atış/atma olayı gönderimi (`SendFireEvent` → `0x03`, olay başına, hemen) ve olay bloğu alımı (`0x04`/`0x05`, **tek** `serverTick` halkasıyla kopya ayıklama). Ayrıca **ağ telemetrisini ölçer** (§6.7): 1 Hz RTT yoklaması (`0x06`) + snapshot varışlarından downlink jitter/kayıp; `SampleTelemetry` ile `ArenaClient`'a verir, o da `status`'a yazar. ⚠️ **Datagram işleme kendi `try/catch`'i içindedir** (§7): bozuk tek bir paket alım döngüsünü öldürürse istemci sessizce donar — durum kanalında doğrusu paketi düşürüp devam etmektir, eksiği sonraki tik kapatır |
| `RemotePlayerRegistry` | Snapshot → oyuncu başına halka tampon → `GetInterpolatedPose`, `IsAlive`, `OnRemoteJoined/Left`; ayrıca **eşya durumu** `TryGetHeldItems` (interpole edilmez — ayrık veri, son gelen geçerli) ve **`serverTick` → yerel oynatma zamanı** eşlemesi `TryGetPlaybackTimeMs` (§3.5b). ⚠️ Tik eşlemesi **global** bir halkada durur, oyuncu başına halkada değil: tik başına bir snapshot var ve hiç pozu olmayan bir oyuncunun olayı da zamanlanabilmeli. Damga `playerCount = 0` snapshot'ında da yazılır (o da meşru bir yayın) ve parçalanmış snapshot'ta yalnız İLK parçadan alınır |
| `NetEvents` | **Statik olay merkezi** — sunucu mesajları buradan ana thread'de yayınlanır (`OnRemoteFireEvent` dahil) |
| `RemoteFireEvent` | Uzak atış/atma olayının istemci-içi taşıyıcısı: `kind`, `rightHand`, `itemId`, arena-uzayı yönü, `magnitude` (atışta mesafe m / atmada hız m·sn⁻¹), `serverTick` |
| `IPoseSource` | 20 Hz döngüye arena-uzayı pozu **ve elde tutulan eşya baytlarını** sağlayan arayüz (`GetHeldItems`) — Net katmanı Core'u göremediği için eşya bilgisi buradan sızar |
| `NetIdentity` / `NetSpawnCatalog` | Sahne objesi kimliği (`sceneId`) ve id→prefab kataloğu — **dinamik obje senkronu altyapısı** (v1'de oyuncu senkronu playerId ile gider) |

### İstemci: `VortexArena.App` (akış ve köprüler)

| Sınıf | Görevi |
|---|---|
| `AppBoot` | Rol çözümü: Android → player; masaüstü → `--role` > `VORTEX_ROLE` > admin. **Sahne her rolde `Lobby`'dir** (admin'in ayrı kabuğu yok). **Adres çözümü:** `--server-ip` / `--server-port`'u **her rolde** okuyup `AppSession`'a yazar (player'da keşif zincirinin en üstü; admin'de tek kaynak — yoksa uyarı loglar). `AppSession.RoleResolved` doluysa hiçbir şey yazmaz → editörde `DevSession` kazanır. **Inspector'da rol/IP override alanı YOKTUR** (kaldırıldı: sahneyi kirletiyordu) |
| `SceneRouter` | `load_match` / `return_to_lobby` / geç katılım → sahne yükleme. **Rolden bağımsız** (admin de oyuncuların sahnesine gider); rol yalnız TEK yerde ayrışır — `set_ready` sadece player'dan gider (admin "hazır" görünmemeli). **Lobi sahnesi de sunucudan gelir** (§3.8.1): `LobbyScene` alanı `return_to_lobby`/`welcome`'dan beslenir, sahne bu build'in listesinde yoksa kabuk `Lobby`'ye düşer ve sebebini loglar. Lobi bir maç sahnesi olmadığı için `LastMatchScene` boş kalır → `set_ready` gönderilmez. **Yükleme asenkrondur** (`LoadSceneAsync`): geçiş boyunca oyun döngüsü aktığı için `LoadingOverlay` çizilebilir ve ilerleme gösterilebilir; `set_ready` kapısı DEĞİŞMEZ — `sceneLoaded` aktivasyon sırasında tetiklenir ve bildirim yine oradan gider (tek kapı). ⚠️ Asenkron yükleme **iptal edilemez**: yükleme sürerken gelen yeni hedef (ör. maç ortasında `load_match`) **sıraya alınır** ve mevcut yükleme biter bitmez yüklenir — hedef sessizce düşürülmez. ⚠️ Geçiş boyunca `Application.backgroundLoadingPriority` **`High`'a çekilir ve sonra eski değerine geri konur**: aksi hâlde asenkron yükleme senkrondan gözle görülür biçimde yavaştır (Tuzaklar: "`LoadSceneAsync` varsayılan ayarla yavaştır") |
| `LobbyController` | VR lobi: roster, ready/takım + otomatik bağlanma; **gizli IP paneli** (varsayılan kapalı, sağ kumandada `OVRInput.Button.PrimaryThumbstick` **1 sn basılı tutularak** açılır/kapanır — beacon'ı kesen ağlar için kurtarma yolu; jest tetiklendiğinde kumanda titrer). Admin de bu sahneden bağlanır (`Connect(..., AppSession.Role)`); world-space paneli admin'de `AdminSpectator` gizler. Panel ilk açıldığında **canvas düzleminden sapmadığı** doğrulanır ve sapmışsa hata basılır — sapma paneli çizilir ama tıklanamaz hâle getirir (Tuzaklar: "world-space canvas'ta düzlemden sapmış bir çocuk"). ⚠️ Paneldeki **"Bağlan" düğmesi bağlantı durumuna göre kilitlenmez**, yalnız yazılan adres ayrıştırılabiliyorsa etkindir: panel tam da istemci yanlış adrese deneyip dururken açılır ve `ArenaClient` o sırada saniyelerce `Connecting`de kalır — duruma bağlansaydı düğme tam gerektiği anda gri olurdu. Deneme ortasında basmak güvenlidir (`Connect` koşan döngüyü iptal edip yenisini kurar) |
| `InputModuleAutoSwitch` | *(`Lobby` sahnesindeki `EventSystem`)* Aynı objede duran iki girdi modülünden hangisinin konuşacağını seçer: XR aygıtı etkinken ISDK `PointableCanvasModule` (kumanda ışını + parmakla dokunma), aygıt yokken `InputSystemUIInputModule` (fare). **Her an yalnız biri etkindir** — `EventSystem` listedeki İLK uygun modülü seçtiği için ikisi birden açıkken kazananı kayıt sırası belirlerdi. Ölçüt platform DEĞİL `XRSettings.isDeviceActive`'dir: `RuntimePlatform.Android` editörde her zaman yanlıştır ve Quest Link ile test ederken arayüzü ölü bırakırdı (Android ayrıca sorulur — cihazda XR başlatılamasa da fare yoktur). Karar her karede yoklanır, **yalnız değişince** yazılır (Link oturum ortasında bağlanır/kopar). ⚠️ Yazma sırası sabittir — **önce kaybeden kapatılır, sonra kazanan açılır**: ISDK modülü "exclusive mode" ile gelir ve etkin olduğu sürece aynı objedeki diğer modülleri kapatır |
| `UiKit` | **Arayüz paleti + çalışma zamanı yardımcıları** (statik). Arayüz prefaba taşındıktan sonra geriye kalan iş: renk paleti (durum renkleri — HP eşikleri, seçim vurgusu, kalibresiz kenarlık, bağlantı noktası), `TeamColor`/`Dim`/`WithAlpha`, dinamik yerleşim (`Block` — havuzlanan satırların konumu), `SetBarFill` ve **EventSystem garantisi** (`EnsureEventSystem`/`TakeOverEventSystem`: arena sahnelerinde EventSystem YOK, edilmezse HUD düğmeleri sessizce ölür). ⚠️ Öge fabrikaları (`Panel`/`Button`/`Text`…) hâlâ durur ama **yeni arayüz onlarla kurulmaz** — görünüm prefabta yaşar |
| `ConnectionOverlay` | **Bağlantı hata ekranı** — kalıcı tekil, `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` ile kendini önyükler ve **görünümü prefabtan alır**: `Resources/UI/ConnectionOverlayScreen` (masaüstü) ya da `…World` (VR), seçimi XR aygıtı/platform belirler. Prefab sahneye KONMAZ (yeni arena eklerken unutulacak adım doğmasın). ~3 sn **grace** (anlık kopmada yanıp sönmesin; açılışı da maç ortasındaki kopmayı da kapsar). Durumlar: adres yok → "SUNUCU BULUNAMADI" (bu bir bağlantı değil **yapılandırma** sorunudur, ayrı dal); adres biliniyor + **oyuncu** → `RECONNECT_GRACE` dolana dek "BAĞLANTI KOPTU · çıkarılmana N sn / maç istatistiklerin korunuyor", sonra "OYUNDAN ÇIKARILDINIZ — yeniden bağlanılıyor"; adres biliniyor + **admin** → bugünkü "SUNUCUYA BAĞLANILAMIYOR". ⚠️ **Admin'de geri sayım YOKTUR** çünkü admin kaydı kopar kopmaz silinir (§2) — ona süre göstermek yalan olurdu. ⚠️ **Değişen yalnız SUNUM:** `ArenaClient` her iki hâlde de sonsuz backoff'la denemeyi sürdürür; süre sunucunun kaydı ne zaman düşüreceğini söyler, başlığın ne zaman pes edeceğini değil. Geri sayım istemcinin **kendi** kopuş anından sayılır (bağlantı yokken sunucudan gelemez) ve Wi-Fi sessizce öldüğünde sunucununkinden `HEARTBEAT_TIMEOUT` kadar erken biter — sapma bilerek bu yönde: ekran "çıkarıldın" derken kayıt hâlâ durabilir, tersi olamaz. Altta `N sn · M. deneme` + son hata. Rol'e göre ipucu (player: joystick 1 sn / admin: launcher). Masaüstü varyantı: screen-space + scrim + **"Yeniden Bağlan"** (adres yoksa devre dışı; `Disconnect()` otomatik denemeyi durdurduğu için tek kurtarma yolu). VR varyantı: world-space kart + `HudFollow`, scrim YOK, **düğme YOK** (o yüzden `_reconnectButton` alanı orada boştur — normaldir). ⚠️ `ArenaBoundary.IsOutOfBounds` iken **tamamen gizlenir** — alan-dışı uyarısı her zaman baskın |
| `LoadingOverlay` | **Sahne geçişi yükleme ekranı** — `ConnectionOverlay` ile birebir aynı desen: kalıcı tekil, `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` ile kendini önyükler, görünümü prefabtan alır (`Resources/UI/LoadingOverlayScreen` masaüstü / `…World` VR, seçimi XR aygıtı/platform belirler), prefab sahneye KONMAZ. **Açılışta önyüklenir, ilk gösterimde değil:** prefabı tam geçiş anında `Resources.Load` etmek yüklemenin en kötü karesinde takılma üretirdi. Yalnız `SceneRouter` sürer: `Show(sceneName)` / `SetProgress(0..1)` / `Hide()`. Bar dolumu `UiKit.SetBarFill` desenidir (`anchorMax.x`) ve hedefe **yumuşayarak** gider — yükleme kareleri düzensizdir, ham `progress` sıçrardı. Başlık ve ipucu metinleri prefabta SABİTTİR; kod yalnız sahne adını, yüzdeyi ve barı yazar. ⚠️ VR varyantında **scrim YOKTUR** (`ConnectionOverlay` ile aynı güvenlik gerekçesi: free-roam'da görüşü karartmak tehlikeli). ⚠️ **World-space kart kamera bulunana kadar ÇİZİLMEZ** ve kamera değişince `HudFollow` yeniden başlatılır (Tuzaklar: "dünya-uzayı overlay'i kamera yokken çizme") — sahne geçişi tam da kameranın öldüğü/doğduğu andır, bu kapı olmadan ekran masaüstünde görünüp VR'da hiç çizilmez. Prefab bulunamazsa hata loglanır ve ekran hiç çizilmez — **sahne geçişi yine de tamamlanır** |
| `DevSession` | **Yalnız editör** (dosyanın tamamı `#if UNITY_EDITOR`): dev penceresinin `EditorPrefs` seçimini Play'e uygular. (a) `BeforeSceneLoad` → rol + adres `AppSession`'a, `RoleResolved = true`; (b) `AfterSceneLoad` → "Açık sahneden" kipinde ve aktif sahne bir ARENA sahnesiyse, bir kare sonra **sunucuya bağlanır**. **Bağlanmayı neden o üstleniyor:** `Connect` normalde kabuk controller'larından gelir, arena sahnelerinde onlar YOKTUR — bağlanmazsa can/skor/faz gelmez ve `CanFire` hiç açılmaz. Takım/mod/süre/limit/faz **yalnız sunucudan** gelir: `welcome.match` geç-katılım senkronu ya da gerçek `load_match`; sunucuda maç koşmuyorsa istemci maç verisi almaz ve bir **admin** maçı başlatmalıdır. **Sandbox kipinde bağlanmaz:** adresi siler (tek başarılı bağlantı `_hasEverConnected`'i kalıcı açar ve kalibrasyon kapısını kapatırdı) ve bağlanmak yerine `ModeRuntime.Apply` ile seçilen `modeId` + `fireWhilePaused = true` yazar, ayrıca `WeaponGranter.SequentialGrant`'i açar — sunucudan gelmiş gibi mesaj **üretmez** (§6.2). Pencerede "Dev enjeksiyonu" kapatılırsa üretim yolu birebir koşar |
| `AppSession` | Oturum: rol + sunucu adresi (`ServerIp`/`ServerPort`/`HasServerEndpoint`) — `AppBoot` yazar, controller'lar okur |
| `PlayerPoseTracker` | *(`VA_PoseSync` prefabı)* Rig anchor'larını bulur, **dünya→arena** çevirip `IPoseSource` olarak kaydolur (kalibrasyon BEKLENMEZ; hizalanana dek pozlar ofsetli gider). Eller `ResolveHandWorld` ile önce **dünya uzayında** kurulur, arenaya çevirim en sonda yapılır — tutulan pozun saklandığı çerçeve ile üretildiği çerçeve aynı olsun. Kapı `ControllerTracking.IsValid`'dir: geçerli elde canlı anchor okunur ve poz **kafaya göreli** saklanır, geçersizde saklanan kafa-göreli poz o karenin kafasıyla yeniden kurulur (el gövdeyle taşınır, arena uzayında donmaz) ve `GetHeldItems` o el için `SnapshotEntry.FLAG_HAND_*_STALE` yazar (§3.5). Oturumda hiç geçerli örnek alınmamışsa dinlenme ofseti kullanılır (sağ el `(0.20, −0.45, 0.25)`, solda X ters) — açılışta bir kere bile okunmamış bir el aksi hâlde oyuncunun ayağının dibinde çizilirdi. Kumanda durumunu `ArenaClient.ReportControllerState` ile Net'e iter; ⚠️ **ölçüm Core'da, bildirim App'te** çünkü Net Oculus'u referanslamaz |
| `RemotePlayerSpawner` | *(aynı `VA_PoseSync` prefabı)* Katılan/ayrılan uzak oyuncular için `RemoteAvatar` yaratır/yok eder; her `lobby_state`'te ad/takım/**kalibrasyon** bilgisini besler. Ayrıca roster'da KENDİ id'sini bulup yerel takımı çözer ve her avatara **dost mu** olduğunu bildirir (`SetFriendly`) — takımsız modda (FFA) ve admin gözlemcide yerel takım boştur, kimse dost işaretlenmez |
| `ModeHudSpawner` | *(`VA_ModeHud` prefabı)* Aktif modun HUD prefabını katalogdan örnekler — **App, mod assembly'lerini referanslamaz** (prefab yalnız `GameObject` olarak taşınır) |
| `IdentifyOverlay` | Admin `identify` yollayınca o başlıkta büyük kimlik overlay'i. Kalıcı dinleyicidir; kartın kendisi **geçicidir** ve `IdentifyDisplay` prefabından örneklenir (`Resources/UI/IdentifyDisplay`), birkaç saniye sonra yok edilir |
| `IdentifyDisplay` | O kartın **görünümü** (prefab): world-space canvas + `CanvasGroup` (sönme) + yazı. İçeriği `IdentifyOverlay` yazar, biçimi prefabta |
| `KickedShutdown` | `kicked` gelince **oyuncu uygulamasını kapatır** (kendini önyükleyen kalıcı tekil; sahnede işi yoktur). Atmanın karşılığı "lobiye dön" değil "oturumdan çık"tır — başlık açık kalırsa operatör panelde düşmüş ama sahada hâlâ oynayan bir oyuncu görür. ~1,5 sn pay (soketin kapanması + log), editörde Play'i durdurur. ⚠️ **Admin kapanmaz** — operatör penceresi sahadaki tek yönetim aracıdır, yalnız bağlantısız duruma düşer. Kapanış dizisi: `Docs/ArenaNet-Protokol.md` §5.4 |

### İstemci: `VortexArena.App.Admin` (admin gözlemci — masaüstü)

Rol `admin` değilse **hiçbiri çalışmaz** (`AdminSpectator` kendini yok eder); Quest build'inde ölü koddur.

> ⚠️ **Arayüzün görünümü bu sınıflarda DEĞİL, prefablarda yaşar** (`_Shared/App/Resources/UI/`).
> Bu sınıflar yalnız veri yazar (metin, renk, görünürlük, dinamik konum); yerleşim/punto/sprite
> elle düzenlenir. Prefablar **sahneye konmaz**, `Resources.Load` ile yüklenir — yeni arena
> eklerken kurulum adımı doğmasın diye. Alanlar `[SerializeField]`; prefabta bağlanmayan alan
> **hata vermeden sessizce çizilmez**, bu yüzden öge silinmez (gizlenecekse devre dışı bırakılır).
> Düğmelerin `onClick`'i prefabta **boştur** ve doldurulmamalıdır: geri çağrıların çoğu koşulludur
> (iki adımlı onay, kilitli satır, faza göre değişen komut) ve kalıcı bir inspector kaydı o
> koşulları atlar. → `Docs/Gelistirici/Arayuz-Tasarimi.md`

> **Çoklu admin desteklenir ve sınırsızdır** (aynı PC'de birden çok pencere dahil — admin `deviceId`'si
> oturumluktur, §5.2 protokol). Hepsi **eş yetkilidir**; ayrım şudur: **operasyonel durum ortaktır**
> (mod/harita seçimi sunucuda yaşar, `set_selection`/`admin_state` ile senkronlanır → `AdminSelection`),
> **görünüm tercihleri yereldir** (kamera kipi, seçili oyuncu, halkalar, saydamlıklar → `AdminSession`,
> `PlayerPrefs`). Her admin eylemi diğerlerinin HUD'ında "kim ne yaptı" satırı olarak belirir.

| Sınıf | Görevi |
|---|---|
| `AdminSpectator` | Gözlemcinin kökü: kendini önyükler (`AfterSceneLoad` + `DontDestroyOnLoad`), rol çözülünce etkinleşir, kamerayı/HUD'ı/işaretçileri yaratır ve **her `sceneLoaded`'da sahneyi devralır**: `VA_CameraRig` kökünü kapatır (üç kamerası da `MainCamera` etiketli → `Camera.main` belirsiz kalırdı), `ArenaCalibrator` + `BaseZone`'ları kapatır, **`ArenaBoundary`'yi KAPATMADAN** `SetSpectatorMode(true)` ile susturur, world-space canvas'ları gizler, EventSystem'i devralır. Kısayollar: `1/2/3` kip · `Tab` sonraki oyuncu · `F` POV · `P`/`I` panel · `Esc` kapat |
| `AdminSpectatorCamera` | Üç kip: **POV** (seçili oyuncunun baş pozu; poz yoksa son konumda kalır) · **Serbest** (WASD + Q/E + **sağ tuş basılı** fare bakışı, Shift ×3, tekerlek hız; imleç KİLİTLENMEZ → HUD tıklanabilir kalır) · **Kuş bakışı** (ortografik, arena yaw'ına hizalı; kadrajın **tek kaynağı** sahnedeki `ArenaBoundary` — ölçü `HalfExtents`, merkez `LocalCenter`'dan gelir (yamuk arenada kutunun ortası transformun üstüne düşmez), varsayılan ölçü YOKTUR — sınır bulunamazsa kamera dünya origin'inin üstünde kalır, ölçü değişmez ve konsola sahne başına bir uyarı düşer (lobide susar); tekerlek zoom. Kameranın **yüksekliği** de sınırdan gelir (`ArenaBoundary.TopDownHeight` → boyut dosyasının `topViewHeight`'ı), yazılmamışsa 20 m: ortografik kamerada bu sayı kadrajı değil yalnız çatının/yüksek objelerin üstünde kalmayı belirler). Kip değişiminde `AdminSpectator.RefreshRoof()` çağrılır → sahnede `ArenaRoof` varsa çatı kuş bakışında kalkar |
| `AdminPlayerMarker` | Tek işaretçinin **görünümü** (prefab: `Resources/UI/AdminPlayerMarker`) — halka canvas'ı, halka görseli, ad etiketi. Seçim görseli iki ayrı sprite alanından gelir (`ringNormal`/`ringSelected`): ⚠️ halka sprite'ı çalışırken **ÜRETİLMEZ** — üretilseydi sanatçının prefabta seçtiği görsel her seçim değişiminde ezilirdi. ⚠️ **Bugün ikisi de aynı görseldir** (`Ring_16`), yani seçim yalnız **boyut** artışıyla anlatılıyor (`SelectedScale`); prefab öncesi kod ek olarak halkayı kalınlaştırıyordu. Kalınlık ipucu isteniyorsa `ringSelected`'a daha kalın bir halka sprite'ı konur — kod değişikliği gerekmez |
| `AdminPlayerMarkers` | Oyuncu başına **zeminde halka + altında ad etiketi** (kuş bakışı isteği). İşaretçiyi `AdminPlayerMarker` prefabından örnekler; konum/renk/seçim bu sınıfta kalır. Halka baş pozunun x/z'sinden arena zeminine indirilir; etiket kameraya döner ve kameranın yukarı vektörünün tersine kaydırılarak her kipte "dairenin altında" okunur. `RemoteAvatar`'a dokunmaz |
| `AdminHud` | **Kalıcı** ekran-uzayı HUD'ı (`sortingOrder = 4000`; hata ekranı 5000'de üstte kalır): üst orta takım skorları + **ortada istatistik chip'i** (faz/süre de gösterir), sol üst tercihler, sağ üst mod·harita + bağlantı/poz yaşı + **çoklu admin satırı** (kaç admin bağlı · son admin eylemi; tek admin varken boş kalır), yanlarda takım kolonları (**FFA'da tek kolon** — karar veriden gelir), alt orta kamera şeridi + seçili oyuncu, alt sağ ölüm akışı. **Görünüm prefabtan gelir** (`_Shared/App/Resources/UI/AdminHud.prefab`) — sınıfın kendisi yalnız veri bağlama/tazelemedir, yerleşim ve renk elle düzenlenir. Prefab **sahneye konmaz**, `AdminSpectator` onu `Resources.Load` ile yükleyip kendi altına örnekler (gözlemci kalıcı → arayüz de kalıcı, lobi ↔ arena geçişinde yeniden kurulmaz). ⚠️ Prefabtaki ögeler silinirse alan boşalır ve **hata vermeden sessizce çizilmez** |
| `AdminPlayerRow` | Oyuncu satırı: takım şeridi, ad + `#id`, HP barı, `K/D · batarya · durum`, eylemler POV/**KAL**/ÖLÇ/**CAN**/TAKIM/KİMLİK/**AT**. `KAL` ve `AT` **iki adımlı onay** ister (oyuncuyu savaş dışı bırakan/atan eylem tek tıkla olmamalı); ⚠️ **`CAN` tek adımlıdır ve onay istemez** — iki adımlı kilit oyuncuyu savaş dışı bırakan komutlar içindir, canlandırma tersidir: oyuncuyu savaşa geri sokar ve yanlışlıkla basılması geri alınabilir bir durumdur. `CAN` yalnız **rolü `player` olan ve ölü** satırda etkindir; `revive_player` gönderir (§3.7) — kalibresiz ya da engelin içindeki oyuncuda sunucu komutu reddeder, yani düğme basılır ama satır canlanmaz (gerekçe sunucu konsolundadır). `KAL` hem gösterge hem düğmedir (`KAL` yeşil / `KAL !` kırmızı — sembol değil renk+ünlem, çünkü TMP varsayılan fontunda ✓/✗ garantisi yok) ve **yalnız sıfırlar** — geri açmayı gözlük yapar (§3.11); kalibresiz satırın kenarlığı kırmızıya döner. ⚠️ **Kalibresiz satırda da basılır:** kırmızı satır iki durumu birden gösterir (hiç kalibre olmamış / elle kalibrasyonun ortasında kalmış) ve düğme ikisini de aynı yere götürür — "zaten kalibresiz" diye elemek operatörü tam da sıfırlaması gereken oyuncudan koparır (§3.11). Satıra tıklamak seçer. **Ad TAKIM RENGİNDE yazılır** (ölüde karartılmış; takımsız/FFA oyuncuda başlık rengi kalır) — aynı renk sahnedeki ad etiketinde ve kuş bakışı işaretçisinde de kullanılır, operatör "bu hangi takım" sorusunu okumadan cevaplasın. `K/D · batarya · kumanda · durum` satırı **zengin metindir** (token başına renk); bayrağı prefab değil kod açar, gerekçesi `EnableStatsRichText`'te. **Görünüm prefabtan gelir** (`_Shared/App/Resources/UI/AdminPlayerRow.prefab`); `AdminHud` onu kolona örnekler ve havuzlar. Satır **yüksekliği prefabtan okunur** → sanatçı satırı büyütünce kolon yerleşimi kendiliğinden uyar. ⚠️ Düğmelerin `onClick`'i prefabta BOŞTUR ve doldurulmamalıdır: hedef oyuncu her `Bind` ile değişiyor, kalıcı bir inspector kaydı yanlış oyuncuya komut gönderir (ve iki adımlı onayı atlar) |
| `AdminPreferencesPanel` | Eski dashboard'un işi. **MAÇ bölümü ORTAK** (başlıkta yazar): mod/harita seçicileri yerel alana değil `set_selection` ile sunucudaki ortak seçime yazar → tüm adminlerde aynı anda değişir; tıklamada yerel imleç de iyimser ilerletilir, sunucudan gelen değer son sözü söyler. **Harita değişince o arenayı HERKES yükler** (§10.7 sahneleme — sunucu `return_to_lobby` yayar, faz `Lobby` kalır, maç başlamaz); panel ayrıca sahneyi yerel olarak da açar (`SceneRouter.LoadPreview`) ama bu yalnız gecikmeyi gizler. ⚠️ **Mod/harita satırları maç KURULMUŞKEN PASİFTİR** (`AdminRoster.CanChangeSelection`): izin yalnız lobide (maç kurulmamış) ve maç bittiğinde (`finished`, operatör bir sonrakini seçiyor) vardır — koşan maç, yükleme, geri sayım ve **duraklatma** kapalıdır (donmuş maç da kurulmuş maçtır; çıkış yolu `abort_match`). Bölüm başlığı sebebini yazar, tıklanırsa durum satırına uyarı düşer; süre/limit her fazda açıktır. Aynı kural sunucuda da uygulanır ve otorite orasıdır (`MatchDirector.CanChangeSelection`) — buradaki kopya yalnız operatörü boşuna tıklatmamak içindir. Bu bileşen panel **kapalıyken de etkin** olduğu için başka bir operatörün harita değişikliği panel açılmadan da yansır. ⚠️ **Harita listesi mekan süzgeci her değiştiğinde yeniden kurulur** (`AdminSelection.VenueVersion`) — panel bağlantıdan ÖNCE kurulduğu için ilk liste kaçınılmaz olarak süzgeçsizdir ve orada bırakılırsa operatör başka işletmelerin arenalarını görür. Yeniden kurulumda **seçili harita hayatta kalıyorsa imleç onda bırakılır**. Bölüm başlığı ayrıca sunucunun **açık sahnesini** yazar (`SceneRouter.OpenScene`): harita satırı bir sonraki maçın adayı, açık sahne şu an yüklü olandır. **GÖRÜNÜM bölümü YEREL** (halkalar, ad etiketleri, kamera hızı, **çatı**) + bağlantı (yeniden bağlan/kes, bağlı admin sayısı). MAÇ bölümünde ayrıca **Süre** (`ROUND_SECONDS_OPTIONS`: 2.5/5/10/15/20/30 dk · 1 saat) ve **Skor limiti** (eşiğin altında ±1, üstünde ±5) seçicileri vardır — ikisi de ORTAK; **mod değişince o modun `ModeDefinition` varsayılanına dönerler**. Bir de **Dost ateşi** satırı: tek değer + iki düğme, ikisi de aç/kapa yapar (satır deseni diğerleriyle aynı kalsın diye) ve değer AÇIK iken kırmızı vurguyla gösterilir. ⚠️ **Bu satır seçim kilidine takılmaz** — mod/harita gibi maç kuruluyken pasifleşmez, çünkü işin özü tam olarak koşan maçta basılabilmesidir (takım arkadaşlarını vuran oyuncu için operatör maçı iptal etmek zorunda kalmasın). Yerel bir alan tutulmaz: gönderilen istek sunucudakinin tersidir, panel `admin_state.friendlyFire`'ı gösterir — iki operatör sapmasın. Yarı saydam, **scrim YOK**. **Liste tabanlı seçim açılır listedir** (mod/harita → `TMP_Dropdown`): seçenekleri kod doldurur (katalogdan; boş katalogda tek satır "katalog yok"/"harita yok" yazar ve seçici pasifleşir), imleç `SetValueWithoutNotify` ile eşitlenir — `value` ataması `onValueChanged`'i tetikleyeceği için sunucudan gelen her tazeleme yeni bir `set_selection` doğururdu. Şablon hiyerarşisi (viewport/item/scrollbar) prefabtadır ve **kapalı durur**. ⚠️ **Harita listesinin ilk satırı "Lobi"dir:** ayrı bir "LOBİYE DÖN" düğmesi yoktur, satır seçilince `set_selection` değil `return_to_lobby` gider. Lobi haritası katalogdan `AdminContent.ResolveLobbyMap` ile çözülür (`supportedModeIds == ["lobby"]` + mekan süzgeci; sunucudaki `MapTable.ResolveLobbyScene` ile aynı ölçüt, birden çok adayda alfabetik ilki → iki taraf aynı sahneyi seçer) ve **arena listesine karışmaz**. ⚠️ **Harita seçicisinin imleci ortak seçimi değil AÇIK SAHNEYİ izler** (`ApplyOpenScene`, kaynak: `welcome` + `return_to_lobby` + `load_match`): ikisi ayrışır — maç bitip lobiye dönüldüğünde ortak seçim hâlâ son arenayı gösterir, açık sahne lobidir. İmleci ortak seçime bağlamak operatörü **o arenaya geri dönemez** hâle getiriyordu: `TMP_Dropdown` seçili satıra tıklamayı olay saymaz (`value == m_Value` iken `onValueChanged` ateşlenmez), yani sahneleme komutu hiç gönderilemiyordu. Aynı sebeple seçicide **"zaten seçili" erken çıkışı yoktur** (sunucu aynı sahneyi tekrar sahnelemeyi zaten idempotent karşılar). Lobi açıkken **BAŞLAT reddeder** ve sebebini durum satırına yazar — sahnelenmiş arena yoktur, sunucu lobi türünde maç başlatmaz. Sayısal değerler (süre, skor limiti, numara) `[<] değer [>]` adımlayıcı kalır: gezilecek bir listeleri yok, asıl jest komşu değere gitmektir. Maç düğmelerinin altında **tek** DURAKLAT/DEVAM ET düğmesi: hangi komutu göndereceğine yerel bir bayrakla değil sunucudan gelen faza bakarak karar verir (`playing` → `pause_match`, `paused`/`operator` → `resume_match`), diğer her durumda pasiftir — çoklu admin'de duraklatmayı başkası da yapmış olabilir, yerel bayrak iki paneli birbirine ters düşürürdü |
| `AdminStatsPanel` | Takım toplamları + oyuncu tablosu (ad/takım/**SKOR**/K/D/K-D/HP/batarya/durum/sahne/**PING**) + maç bilgisi. **FFA'da tablo skora göre azalan sıralanır**, başlık lideri yazar. Tablo **kolon kolon** çizilir (TMP fontu eşit genişlikli değil, boşlukla hizalama kayar). ⚠️ Kolon eklemek koda yetmez, prefabta (`AdminHud.prefab`) bir TMP objesi açıp `_columns` dizisine bağlamak gerekir — bağlanmazsa kolon sessizce hiç çizilmez. Protokolde olmayan metrik (hasar/isabet oranı) **gösterilmez**; jitter/kayıp protokolde VAR ama panelde bilinçli olarak yok (operatörün eyleme çevirebileceği sayı ping'dir) |
| `AdminRoster` | Admin arayüzünün veri katmanı: `lobby_state` (otoriter tam görüntü + `kills/deaths/hp/alive/score`) + `health_update`/`kill_event` (anlık) + `match_state`/`countdown`/`match_end` birleşimi; takım listeleri, takım kipi kararı, ölüm akışı, snapshot yaşı. **`IsFfa` OTORİTER:** maç yüklüyse `ModeRuntime.Teams`, lobide ortak seçimin katalogdaki modu, ikisi de yoksa eski sezgisel yedek ("kimsenin takımı yok"). ⚠️ `respawn` admin'e GELMEZ (yalnız ölen oyuncuya gider) → geri sayım `kill_event` + `RESPAWN_DELAY` ile yerel hesaplanır. **Bağlantı durumu üç değerlidir** (§5.3 `connection`) ve string karşılaştırmasının TEK yeri buradaki `IsConnected`/`IsReconnecting`/`HasLeft` kısayollarıdır — bilinmeyen/boş değer **bağlı** sayılır, yoksa sürüm karışımında roster tümden sönerdi. POV döngüsü, canlı sayacı ve takım kipi sezgisi yalnız `IsConnected` satırları sayar; `left` satırlar tabloda **durur** (maç istatistiği) ama eylem düğmeleri kapalıdır. ⚠️ `reconnectSeconds` **yerelde ilerletilir** (`lobby_state` damgası + `Time.unscaledTime`): roster yayını olay tabanlı olduğu için sunucudan saniyede bir güncelleme gelmez, damgasız okuma sayacı yalnız başka bir roster değişikliğinde ilerletirdi |
| `AdminSession` | **YEREL** seçimler (kamera kipi, seçili oyuncu, açık panel) + görünüm tercihleri (`PlayerPrefs`'te kalıcı, admin PC'sine özel — halkalar, ad etiketleri, kamera hızı, **çatı kipi**). Tek doğruluk noktası; `Changed` ile HUD/kamera/işaretçiler senkron kalır. `RoofAlphaNow()` tercih + kamera kipinden çatı alfasını türetir |
| `AdminSelection` | **ORTAK** durumun aynası (`admin_state`, §5.3): mod/harita seçimi, **maç süresi + skor limiti**, çevrimiçi admin sayısı, son admin eyleminin duyurusu, **mekan süzgeci** (`venueId`/`venueScenes` + her değişiminde artan `VenueVersion`), **dost ateşi anahtarının yürürlükteki değeri** (`FriendlyFire` — seçim değil durum, §3.9). Statik durum + statik `Changed` (bileşen kurulum sırası dinleyiciyi ilgilendirmesin); bileşenin kendisi yalnız ağ olayı pompasıdır. Otorite sunucudadır — buraya yerelden yazılmaz |
| `AdminCommands` | Admin komutlarının tek çıkış kapısı (§5.2) + son işlemin durum metni. "Gönderildi" der, "oldu" demez — kabul/ret sunucuda. `SetSelection` ortak seçimi (mod/harita/süre/limit) değiştirir, maçı başlatmaz; `StartMatch` süre/limit taşır (`0` = mod varsayılanı); `PauseMatch`/`ResumeMatch` koşan maçı dondurur/sürdürür; `SetFriendlyFire` dost ateşi anahtarını çevirir (**faz kapısı yok** — koşan maçta da geçerli, §3.9); `RevivePlayer(playerId)` ölü oyuncuyu elle canlandırır (`playerId:0` = o an ölü olan herkes, §3.7) ve kapıları sunucu uygular — bu komut da "gönderildi" der, "canlandı" demez |
| `AdminContent` | `Resources.Load<GameCatalog>("GameCatalog")` (asset: `_Shared/Data/Resources/`) → mod/harita listeleri. **Statik** yardımcıdır (`[SerializeField]` taşıyamaz), katalogu bu yüzden `Resources`'tan okur |

### Editör: `VortexArena.App.Editor` (dev araç seti — yalnız Editor)

| Sınıf | Görevi |
|---|---|
| `DevWindow` | `Tools > VortexArena > Development > Dev` penceresi: "Dev enjeksiyonu açık" onayı · **Rol** (Player/Admin) · **Sunucusuz sandbox** (+ mod seçicisi; açıkken Hedef bloğu devre dışı) · **Hedef** (`dev-targets.json` + "Özel…" IP/Port + Tazele) · **Başlangıç** (Boot'tan / Açık sahneden) · alttaki "Seçim: …" özeti. **Sunucuya hiç dokunmaz** — ne başlatır, ne durdurur, ne derler (§6.1). Maç parametresi taşımaz — mod/takım/süre/limit sunucudan gelir. **Modal dialog kullanmaz** (Unity CLI doğrulamasını kilitliyor); geri bildirim konsol + `HelpBox` |
| `DevTargets` | Repo kökündeki `dev-targets.json` okuyucusu (`defaultTarget`/`defaultRole` + adlandırılmış hedefler). Dosya yok/bozuksa bellekte `Local` + `Kesif (beacon)` varsayılanına düşer ve **dosyayı OLUŞTURMAZ** (commit kirletmemek için). Bir hedefin `ip`'si boşsa adres yazılmaz → keşif zinciri devralır |
| `DevBootstrap` | Editör kancaları: "Boot'tan" kipinde `EditorSceneManager.playModeStartScene`'i Boot sahnesine ayarlar (sahne **Build Settings'ten** bulunur, sabit yol gömülmez); `Ctrl+Alt+R` kısayolunu kurar (rol player↔admin). **Hiçbir süreç öldürmez** — sunucu kasıtlı olarak yaşar (üretimde de ayrı makinede sürekli açık) |

### İstemci: `VortexArena.Core` (oyun kodu)

`ArenaBoundary` (`VA_ArenaBoundary` prefabıyla gelir; muhafaza: kenara/kolona olan mesafeden
karartma + uyarı — kenara `warnDistance`
kala hafif bir rampa başlar (`warnFadeAlpha`), sınır aşılınca tam karartmaya gider; iki dal sınırda
aynı değeri verdiği için geçiş süreklidir. ⚠️ **Yarı saydam duvar göstergesi KALDIRILDI**
(`wallRenderers`/`minWallAlpha`/`maxWallAlpha` yok): arenanın duvarları environment sanatına ait ve
mekanizma oraya taşınamıyordu — alfa yazımı yalnız Transparent malzemede iş görür, üstelik alfa
düşünce Renderer'ı kapatıyordu. Uyarı bu yüzden HMD'ye bağlı karartma quad'ına taşındı, arena
geometrisinden tümden bağımsız;
`HalfExtents`/`LocalCenter`'ı admin kuş bakışı kadrajı okur — ikisi de plandaki sınır çokgeninin
sınırlayıcı kutusundan türer, ölçü bileşende TUTULMAZ. Aynı kadraj kameranın yüksekliğini de
buradan alır (`TopDownHeight` → plandaki `topViewHeight`; 0 = kamera kendi varsayılanını kullanır):
boyut dosyasını çözen tek yer bu bileşendir, kamera JSON'u kendisi AÇMAZ.
**Ölçü maketi bu transformun altında durur** (yerel sıfırda), yani muhafazayı taşımak/döndürmek
arenanın ölçü kutusunu ve kalibrasyon işaretçilerini birlikte taşır. Planın **tek kaynağı** `dimensionsJson`
alanına bağlanan boyut dosyasıdır; ikinci bir kip yoktur. Plan çözülünce mesafe **çokgene işaretli
mesafe ⊓ kolonlar ⊓ sahnedeki `ArenaObstacle`'lar** olur — en yakın tehlike kazanır, kolonun içi
alan-dışı sayılır. JSON kare başına ayrıştırılmaz (referans değişmedikçe önbelleklenir).
⚠️ Dosya bağlı değilse ya da okunamıyorsa **açık başarısızlık**: bir kez `Debug.LogError` basılır ve
muhafaza tümden susar (karartma ve alan-dışı uyarısı çalışmaz). Gerekçe: ölçüsü
bilinmeyen bir arenada doğru bir muhafaza zaten üretilemez, kapalı başarısızlık (ör. her karede
ekranı karartmak) işletmede oyunu tümden oynanamaz kılardı — bu bir KURULUM hatasıdır, editörde/
QA'da yakalanmalıdır. Arena origin'i bu bileşende DEĞİLDİR, devre dışı bırakılabilir),
`ArenaDimensions` (`Core/Arena` — **arena ölçüsünün TEK doğruluk kaynağı**: elle yazılabilir bir
JSON dosyası (`TextAsset`), çalışma anında okunur ve **MEKAN başınadır** — bir işletmenin bütün
sahneleri (arenalar + lobi) aynı dosyayı gösterir, sahne başına kopya kaçınılmaz olarak sapardı.
Alanlar `plane`/`columns`/`calibration`/`defaultColumnHeight`/`topViewHeight` (sonuncusu admin kuş
bakışı kamerasının zeminden yüksekliği — ortografik kamerada kadrajı değil yalnız çatının/yüksek
objelerin üstünde kalmasını etkiler; 0 = kameranın varsayılanı); noktalar `ArenaBoundary`
transformunun yerel XZ'sinde ve JSON'daki `y` dünya Z'sidir. Halkalar **kapalı**dır (ilk nokta sona
tekrarlanmaz), köşe yönü önemsizdir. ⚠️ **Taban da kolon da TEK sıralı köşe halkasıdır**: parçalardan
birleştirme (union) yoktur — içbükeylik için gerekmez ve `ArenaBoundary` yüzünden çalışma anında da
koşmak zorunda kalırdı. Aynı sebeple "dikdörtgense şu hızlı yol" ayrımı ve ona ait bileşen alanları
da yoktur. ⚠️ **`wallHeight` alanı YOKTUR**: duvar üretimi de duvar göstergesi de kalktığı için
okuyanı olmayan bir ölçü olurdu. ⚠️ Kolondaki `{"points": […]}` sarmalayıcısı zorunlu —
`JsonUtility` iç içe dizi serialize etmiyor. `Parse`/`FromTextAsset` **exception fırlatmaz** — bozuk
dosyada `null` + hata metni döner, çünkü çağıran yer sahne yükleme yolu; `FromJsonOverwrite`
kullanıldığı için **yazılmayan alan varsayılanında kalır** (aksi hâlde eksik bir
`defaultColumnHeight` hiç çizilmeyen kolonlar demekti). ⚠️ **Kolonun muhafazaya girip girmeyeceğini
seçen bir anahtar YOKTUR ve eklenmez** — kolon binanın taşıyıcısıdır, oyuncu ona her hâlükârda
çarpar. `calibration: {a, b}` = zemine yapıştırılan A/B bantlarının yeri; **mekan başınadır**
(aynı odadaki tüm sahneler aynı iki fiziksel işareti kullanır) ve `HasCalibration` iki nokta
arasında en az `MinCalibrationSpan` (0,5 m) ister — daha yakın bir çift yön tanımlamaz, yaw
hatası mesafeyle ters orantılı büyür. ⚠️ Kalibrasyon noktaları `IsValid`'in parçası **değildir**:
noktasız bir dosya muhafazayı çalıştırmaya yeter, yalnız işaretçiler kendiliğinden yerleşmez.
Dosya olmasının kazancı: ölçüyü sahadan alan kişi Unity açmadan güncelleyebilir),
`Polygon2D` (`Core/Arena` — halkalara sorulan her geometrik sorunun tek yeri: `Contains`,
`DistanceToRing`, `SignedDistance` (alan: içeride +) / `ObstacleDistance` (engel: dışarıda +),
`Bounds`, `Centroid`, `IsSelfIntersecting`. İki mesafe sözleşmesi muhafazanın ikisini tek
`Mathf.Min` ile birleştirebilmesi içindir — her ikisinde de "artı = güvenli pay". Metotlar tahsis
yapmaz: muhafaza her karede çağırıyor),
`ArenaDimensionMesh` + `DimensionPolygon` + `DimensionAnchor` (ölçü maketinin işaretçileri; kökte
mekan adı + kaynak `TextAsset` + geri yazma taşıyıcıları, çokgenlerde yalnız
`Kind { Plane, Column }`, kalibrasyon küplerinde yalnız `AnchorKind { A, B }`. ⚠️ Nokta/ad/
yükseklik işaretçide TUTULMAZ — kaynakları sırasıyla mesh (kalibrasyon noktasında transform),
`GameObject` adı ve mesh'in Y aralığı;
kopyalamak sahnede düzenlenen değerden sapan ikinci bir kaynak üretirdi.
⚠️ **Maketin kökü ve kalibrasyon küpleri build'e GİRER** (`EditorOnly` etiketlenmez): sahnenin
`anchor_a`/`anchor_b` işaretçileri bunlardır ve çalışma anında gerekir. **Görsel dal
(`Plane` + `Columns`) gerçek build'e hiç girmez** — `DimensionMeshBuildStripper` onu build'e giden
geçici sahne kopyasından siler (gerekçesi boyut değil bağımlılık: `ProBuilderMesh` runtime'a
`Unity.ProBuilder`'ı sokardı; sahne dosyası değişmez). **Editör Play kipinde** ise her şey
sahnededir: orada görseli `ArenaDimensionMesh.Awake` `Plane`/`Columns` altındaki
`Renderer.enabled`'ı false yaparak gizler — obje kapatılmaz (kapalı bir kökün altındaki
işaretçiler bulunamazdı) ve işaretçilerin `Renderer`'larına dokunulmaz (onları kalibrasyon
sırasında `ArenaCalibrator` yakıp sonra gizler). ⚠️ Sahnede ikinci bir işaretçi
ailesi yoktur: `anchor_a`/`anchor_b` yalnız bu küplerdir),
`ArenaObstacle` (sahneye ELLE konan engelin muhafaza dikdörtgeni; konum/dönüş transformdan,
ölçü `size`'dan gelir — ⚠️ **collider eklemez, fizik yapmaz**: free-roam'da oyuncuyu durduran şey
gerçek nesnedir, bileşenin tek işi uyarıyı erken tetiklemektir),
`ArenaCalibrator` (`VA_CalibrationManager` prefabıyla gelir; 2 nokta → 6DOF hizalama +
OVRSpatialAnchor kalıcılığı + recenter onarımı; nokta alma jesti **sağ kumandada A basılıyken
B'ye çift basış** (`doubleTapSeconds` penceresi; basılı tutma süresi yoktur) ve **yalnız sunucu
"kalibresiz" derken açılır**,
§3.11. `anchor_a`/`anchor_b` işaretçileri **kurulum aracıdır, dekor değil**: yalnız elle
kalibrasyon sürerken görünürler ve hizalamadan `markerVisibleSeconds` sonra gizlenirler — kayıtlı
anchor'dan geri yükleme yolunda hiç gösterilmezler, yoksa harita değişiminde maçın ortasında ekrana
obje düşerdi. **Yerleri sahneden değil boyut dosyasından gelir**: `Start` işaretçileri
`ArenaBoundary.TryGetCalibrationMarks` üzerinden `calibration.a`/`.b` noktalarına oturtur
(`PlaceMarkerAtFloor`), dosyada nokta yoksa dokunmaz ve uyarır.
İşaretçi çözümü üç basamaklıdır: (1) Inspector'da elle bağlanmış `anchorA`/`anchorB`, (2) ölçü
maketinin `DimensionAnchor` küpleri (`AnchorKind`), (3) ada göre arama (`AnchorAName`/`AnchorBName`)
— sonuncusu yalnız maketi olmayan eski sahneler için vardır.
**`Invalidate()` oyuncuyu hiçbir ara aşamada bırakmaz** (§3.11): tamamlanmış hizalamanın yanında
yakalanmış A noktasını + bekleyen çift basışı, o an basılı tutulan jestin kuyruğunu (A bırakılana
dek yeni yakalama alınmaz) ve **uçuşta olan kayıtlı-anchor geri yüklemesini** de siler — sonuncusu
birkaç saniyelik bir yeniden deneme penceresidir ve iptal edilmezse sıfırlamadan sonra başarıya
ulaşıp arenayı yeniden hizalardı.
**Tamamlanma tek kapıdan bildirilir** ve iki tamamlanma yolu da (elle yakalama + kayıtlı anchor'dan
geri yükleme) oradan geçer: statik `Calibrated` olayı (dinleyicisi `CalibrationState`).
⚠️ **Gövde ölçüsü buna BAĞLI DEĞİLDİR:** ölçümü operatör başlatır (`measure_body_scale`, §10.8),
hizalama değil — hizalamadan otomatik tetiklenen bir ölçüm, oyuncu kumandayı zemine değdirmek için
**eğilmişken** ölçmek olurdu. `CharacterRetargeter.Calibrate()` bu projede hiç çağrılmaz.
⚠️ **İki kalibrasyon ayrı şeydir:** bu sınıf rig'i fiziksel arenaya hizalar (sunucu-otoriter durum,
§10.6), gövde ölçüsü ise ayrı bir eksendir (§10.8) — ölçünün ön koşulu hizalamanın geçerli
olmasıdır (zemin oradan gelir) ama tetikleyicisi operatördür.
**Kalibresiz ön-hizalama:** kayıtlı hizalaması olmayan bir başlıkta (PlayerPrefs'te anchor UUID'si
yok, ya da geri yükleme tüm denemelerde düştü) rig, kafası arenanın A-B ortasında ve A→B'ye bakar
olacak biçimde **tahminen** yerleştirilir; yükseklik `uncalibratedHeadHeight` (varsayılan 1.8 m,
zeminden). Sebebi görünürlüktür: hizalanmamış rig oyuncuyu arenanın dışında, yani `ArenaBoundary`
karartmasının içinde bırakabiliyor ve elle kalibre etmesi gereken oyuncu tam o anda hiçbir şey
göremiyor — `PlayerPoseTracker`'ın "kalibrasyondan önce de poz gönder" tercihiyle aynı çizgi.
⚠️ **Kalibrasyon SAYILMAZ**: yakalama sayacı artmaz, `Calibrated` yayınlanmaz, anchor kaydedilmez,
elle kalibrasyon kapısı açık kalır; gerçek hizalama geldiğinde rig mutlak olarak yeniden konumlanır
(tahmin birikmez). `CalibrationGeneration` yine artar — taşınma meşrudur, sıçrama bastırıcıları
bunu arıza sanmamalıdır. **Koşmadığı durumlar:** kayıtlı anchor geri yüklenebildiyse, oyuncu jeste
başladıysa, operatör sıfırladıysa, rig kökü kapalıysa (admin gözlemci rig'i kapatır) ve işaretçiler
yok/aynıysa. İzleme en fazla ~2 sn beklenir; HMD'siz sandbox'ta süre dolar ve tahmin yine uygulanır.
⚠️ **Taşınan kafadır, rig kökü değil** — kök zemin referansıdır.
⚠️ **Sıra A → B'dir ve geometrik olarak doğrulanamaz**: iki nokta hangisinin önce alındığını
söylemez, mesafe kontrolü de simetriktir. Garanti prosedüreldir — ilk yakalama A sayılır, o anda
A işaretçisi yanar ve log `1/2 — A yakalandı` yazar. Karıştırılırsa arena 180° ters döner.
⚠️ **Tek Y sözleşmesi: işaretçinin transform konumu zemin noktasıdır** — küp o noktada
merkezlenir, yarısı zeminin altında kalır. Görsel bir telafi (mesh tabanını zemine kaldırma)
YOKTUR: aynı transform hem dosyaya yazılan hem dosyadan okunan ölçü olduğu için ikinci bir
sözleşme yazma/okuma arasında sessiz sapma üretirdi),
`CalibrationState` (kalıcı tekil — kalibrasyon durumunun sunucu ile iki yönlü köprüsü: hizalanınca
`set_calibration` yollar, operatör sıfırlayınca `ArenaCalibrator.Invalidate()` çağırır.
⚠️ Sıfırlamayı **`clear_calibration` komutundan** duyar ve **koşulsuz** uygular — yerelde hizalama
olup olmadığına bakmaz, çünkü silinecek şeyin bir kısmı hizalama değildir (yarım kalmış sekans).
⚠️ **Roster'daki `calibrated:false` bir sıfırlama sinyali DEĞİLDİR ve hizalamayı sildirmez:** sunucu
her `hello`'da o alanı sıfırladığı için (§10.6) değer her yeniden bağlanışta bir kez yayınlanır ve
başlığın kendi yeniden bildirimiyle yarışır. Roster bu sınıfta yalnız **ayna**dır — bayrağı yazar,
anchor'a dokunmaz),
**`BodyScaleState`** (kalıcı tekil — gövde ölçüsünü **operatör düğmesiyle** alır ve `set_body_scale`
ile bildirir, §10.8. Ölçüm: oyuncunun gözü (`centerEyeAnchor`) ile karakterin göz işaretçisi
(`LocalBodyAvatar.EyeAnchor`) **aynı karede**, arena uzayında okunup oranlanır; ~0,5 sn örneklenip
medyanı alınır, yayılım %5'i aşarsa ölçüm reddedilir (oyuncu hareketli/eğilmiş). Sonuç cihazda
saklanır ve yeniden bağlanınca yeniden bildirilir; sunucu ölçeği sıfırlarsa yerel kayıt da silinir —
yoksa operatörün sıfırlaması bir sonraki bağlanışta sessizce geri alınırdı. ⚠️ Sabit bir "model göz
yüksekliği" sayısı YOKTUR: karakter zaten oyuncuyla aynı pozdadır, oran duruş farkını götürür ve
model değişince bayatlayacak bir sayı kalmaz),
`ArenaSpace` (dünya↔arena dönüşümünün **tek adresi**: arena uzayı dünya uzayıyla çakışık olduğu
için poz/pozisyon/rotasyon dönüşümleri kimliktir. Çağrı yerleri yine ondan geçer — çerçeve tek
yerde tanımlı kalsın. `WorldToArenaDirection` istisnadır: yönü **normalize eder** ve sıfır/NaN
girdide `Vector3.forward` döner, çünkü protokol her olayda bir birim yön taşır),
`BaseZone` (**taban bölgesi** — kırmızı/mavi şerit, canlanma
kapısı; `Neutral` = herkese açık),
`MapDefinition` / `ModeDefinition` / `GameCatalog` (içerik SO'ları),
`Weapon` (ISDK ile tutulan hitscan tüfek; tetik **silahı tutan elin** kumandasından okunur — çift
silahta tetikler bağımsız; şarjör+yedek şarjör durumu taşır, boş şarjörde **otomatik reload YOK**
(kuru tetik sesi), reload **bel-altı jestiyle** başlar; `reserveMode=DiscardMagazine`'de erken
reload'da şarjörde kalan mermi **yanar** (ürün kuralı; `PoolRounds` = CS2 havuzu SO'dan seçilebilir);
spread atış sürdükçe açılır (bloom) ve boşta toparlar; yerel canlanmada tutulan silah tam dolar;
vuruş/atış bildirimi `ArenaCombat` üzerinden gider — protokol DTO'su bu sınıfta YOK. **İkinci tutuş
yolu:** `WeaponGranter` silahı doğrudan ele verir (`GrantTo(hand, kind)` — §3.9); verilen silah
tanım gereği tutuluyordur (ISDK kavraması işletilmez), gerisini `WeaponGrantKind` ayırır —
`Disposable` (FFA'nın rastgele silahı) tek elli/rezervsiz ve reload'u KAPALIDIR, `Persistent`
(çerçeveden seçilen silah) tam rezervle gelir, reload'u AÇIKTIR ve ön kabzası tutulabilir) +
`WeaponDefinition` (SO — hasar/HS çarpanı/RPM/şarjör/reload/spread/recoil/**haptik (atış başına
titreşim şiddeti + süresi; biri 0 = o silahta haptik yok)**/ses profili + verilen
**tek denge kaynağı**, sunucuya export edilmez; el duruşu prefabtaki `GripPoses/Pose_*` düğümlerinde
— tabandaki `ItemDefinition.primaryGrip*` onun fallback'i, denge alanlarının yanında DEĞİL) + `WeaponAudio` (Meta XR spatializer'lı namlu AudioSource:
ateş/şarjör çıkar-tak/kuru tetik/alma) + `WeaponAnimator` (Animator'sız kod-güdümlü parça
animasyonu: atışta bolt tepmesi, reload'da `*_Mag` child'ı çıkar-takılır; şarjör seslerini de bu
zaman çizgisi çalar — görüntü/ses tek kaynaktan) + `WeaponReloadGesture` (silah bel hizasının
altına inince `TryStartReload`; bel çizgisi = kafa − `waistDropMeters` (0.62 m vars.) — ORAN DEĞİL:
fark matematiği zemin/kalibrasyon ofsetlerinden etkilenmez; kavradıktan sonra bir kez bel üstüne
çıkmadan devreye girmez — alçakta duran bir silahı seçer seçmez yanlış tetiklemeyi önler) + `WeaponCatalog` (SO, `_Shared/Data/Resources/` — `weaponId`→tanım araması;
`Resources.Load` ile okunduğu için klasöründen çıkarılmaz) + `RemoteShotFx` (kendini önyükler,
sahne kurulumu istemez; UDP atış olayını (§6.4/6.5) tüketip uzak oyuncunun namlu alevi + konumsal
atış sesini havuzlu çalar, tracer'ı çizer ve silahın **geri tepmesini** tetikler —
`RemoteAvatar.ApplyShotRecoil`, olayın kendi tik'inin oynatma anında) + `ShellEjector` (`Weapon.Fired` olayına abone; ateşte namlunun yanındaki `Eject`
noktasından kalibreye göre (`Casing_762x39`/`Casing_556x45`) bir kovan fırlatır — 10'luk round-robin
havuz, süre kontrolü coroutine değil `Update`'te `Time.time` ile; havuz+`MuzzleFlash` altındaki
"Smoke" sub-emitter'ı da dahil tüm bu kit `WeaponKitBuilder` tarafından üretilir/güncellenir) +
`AmmoHud` (`Core/UI` — kendini önyükler ve görünümünü **`Resources/UI/AmmoHud` prefabından**
alır; tutulan silah(lar)ın adı/mermisi/yedek şarjörleri görüş alanının sağ altına düşen
`HudFollow`'lu tembel-takip panelinde; silah tutulmuyorken gizli, yalnız
`Weapon.Active`/`ActiveChanged` + silah olaylarıyla yenilenir — mermi göstergesi silahın
ÜSTÜNE koyulmaz; punto/konum prefabta düzenlenir) + `ArenaCombat` / `WeaponGranter` (aşağıdaki
tabloda), `PlayerCombatState`
(yerel oyuncunun takım/can/ateş yetkisi/canlanma akışı), `RemoteAvatar` + `RemoteHitBox`
(uzak oyuncu gövdesi ve isabet kutusu; `RemoteAvatar` ayrıca çizdiği silahın **geri tepmesini**
yerelin eğrisiyle üretir — kavrama örneğin KÖKÜNÜ, geri tepme `Model` ÇOCUĞUNU yazar, bu yüzden
yarışmazlar; telde geri tepme diye bir alan yoktur ve eklenmeyecek, §6.4.
**İsabet kutuları gövde başına 16 parçadır** (kafa · göğüs · karın · leğen · üst kol ×2 · ön kol ×2 ·
el ×2 · uyluk ×2 · baldır ×2 · ayak ×2), kemiklere asılıdır ve her biri bir `HitZone` taşır
(bölge = hasar çarpanı, yukarıya bak).
⚠️ **Her gövdenin KENDİ seti vardır** (varsayılan karakter + takım gövdesi ayrı ayrı) ve set,
çizilen gövdeyle birlikte değişir; çizilmeyen gövdenin kutuları koşulsuz kapalıdır — açık
kalsalardı görünmeyen bir hacim mermi yutardı. Sebep ölçülebilir: kemik oranları modelden modele
değişiyor (bugünkü iki modelde bacaklarda ~5 cm, kalça genişliği farkından), tek set paylaşılsaydı
çizilen gövdeyle vurulan hacim birbirinden kayardı.
⚠️ **Kutular ELLE bakılır — üreten bir araç YOKTUR ve eklenmez.** Her karakterin eti farklı; tek
bir tabloya bağlı üretim ilk birkaç modelden sonra kaçınılmaz olarak yalancı-doğru üretir ve
düzeltmeleri her koşuda ezerdi. Yeni bir karakter geldiğinde kutular onun gövdesine bakılarak
kurulur.
⚠️ Aynı sebeple `RemoteAvatar`'da **serialize edilen bir kutu listesi YOKTUR**: kutular her
gövdenin altından `RemoteHitBox` işaretine bakılarak toplanır. Elle bakılan bir yapının yanında
elle güncellenen bir dizi tutmak ikinci bir doğruluk kaynağı olurdu (kemiğe asılan yeni kutu
listeye girmezse ölü oyuncuda kapanmaz). İşaret filtresi de bilinçli: işaretsiz bir collider
sessizce vurulabilir hale gelmez.
**Kutular Scene view'de her zaman çizilir** (`RemoteHitBox.OnDrawGizmos`, bölgeye göre renkli:
kafa kırmızı · karın turuncu · bacak sarı · gövde yeşil) — seçili olmaları gerekmez: kutu
ayarlanırken seçili olan çoğu zaman başka bir şeydir (kemik, karakter kökü) ve kutunun nerede
olduğu o anda da görünmeli. Elle ayar bu gizmo'ya bakılarak yapılır.
⚠️ Prefabın kökünde **kinematik `Rigidbody`** durur ve kaldırılmaz — Rigidbody'siz bir collider
PhysX için "static"tir ve her kare kıpırdatmak broadphase'i yeniden kurdurur; 12 kutu × oyuncu
sayısı bunu Quest'te ölçülebilir yapar. Fizik davranışı için değil BÜTÇE için oradadır
(yerçekimi kapalı, hiçbir şey avatarı itmez).
**Kafanın üstündeki ad etiketi TAKIM RENGİNDEDİR** (ölüde karartılır; takımsız/FFA oyuncuda beyaz
kalır — orada rengin anlatacağı bir şey yok). Aynı renk admin kartında ve kuş bakışı
işaretçisinde de kullanılır: bir oyuncunun adı nerede yazarsa yazsın aynı renkte okunur.
Bu, "takım rengi karakter mesh'ine yazılmaz" kuralının istisnası değildir — etiket zaten oyuncunun
kimliğini yazıyor, kırmızı takım kendi gövdesiyle çiziliyor ve etiket normal `ZTest` ile
çiziliyor (duvar arkasından okunmaz); renk telde olmayan yeni bir bilgi açmaz, olanı okunur yapar.
**Ölü ya da kalibresiz uzak oyuncu HAYALETE döner:** yarı saydam, iki yüzü de çizilen
`VortexArena/AvatarGhost` (`_Shared/Materials/M_AvatarGhost.mat`) — `Cull Off` + `ZWrite Off`
olduğu için gövdenin içi görünür, `ZTest` normaldir yani duvar arkasından GÖRÜNMEZ. Renk
oyuncunun **kendi takımıdır** (kırmızı takım kırmızı, mavi takım mavi) — bakana göre değişen
dost/düşman bilgisi DEĞİL: aynı ölü oyuncu her başlıkta ve admin ekranında aynı renkte okunur.
Takımsız modda (FFA) takım diye bir şey olmadığı için hayalet **nötr** (kirli beyaz) çizilir;
oradaki bir kırmızı/mavi var olmayan bir takımı işaret ederdi. Renkler takım renklerinin
saydamda okunan tonlarıdır (opak gövde için seçilen ton `GhostBaseAlpha` altında griye kaçıyor),
ikinci bir palet değil. Kalibresizken turuncuya nabız atar ve **kalibresizlik ölümü ezer**.
Canlı + kalibreli oyuncunun gövdesine HİÇ dokunulmaz. Hayalet **ayrı bir model DEĞİLDİR**: o an
çizilmekte olan gövdenin (varsayılan mavi karakter ya da kırmızı takım gövdesi) KENDİ mesh'inin
materyal takasıdır — özgün materyal dizisi renderer başına saklanır, aynı UZUNLUKTA bir hayalet
dizisiyle değiştirilir, hayaletten çıkışta özgün dizi geri konur ve property block sökülür
(block'lu renderer SRP Batcher dışında kalır). ⚠️ Uzunluk birebir korunmalı: alt mesh sayısından
fazla materyal SON alt mesh'i bir kez daha çizer, eksik olan hiç çizilmez. Poz aktarımı YOKTUR ve
eklenmez — mesh zaten karakterin kendisi olduğu için hayaletin kayması yapısal olarak imkansızdır.
İki gövdenin de kendi özgün/hayalet dizisi vardır (takım değişiminden sonra takas yanlış
renderer'a yazılmasın). `ghostMaterial` boşsa hayalet **hiç uygulanmaz** ve bileşen örnek başına
bir kez HATA basar: ölü oyuncu canlıdan ayırt edilemez.
**Kırmızı takım ayrı bir MODELLE çizilir** (`RemoteAvatar.redBodyRoot` → `RedTeamBody` kabı,
altında `_Shared/Avatars/T-Avatars/Ch18_nonPBR.fbx` örneği; kurulumu
`Tools > VortexArena > Avatars > Takım Gövdesini Kur`): iki gövdeden aynı anda yalnız biri
çizilir, seçimi `SetInfo`'ya gelen takım dizesi yapar ve takım değişimi avatarı yeniden
doğurmadan anında uygulanır. **Ağda hiçbir karşılığı yoktur** — takım zaten `lobby_state` ile
geliyor, model seçimi tamamen istemci görselleştirmesidir. Gövde karakterin
KARDEŞİDİR ve pozu `SkeletonPoseMirror` ile karakterin canlı iskeletinden sürülür — **kemik
aynası**: iki ağaçta ADI eşleşen kemiklerin yalnız `localRotation`'ı kopyalanır, kalça ise kendi
BIND pozuna göre aktarılır. ⚠️ İki Mixamo modelinin kemik ADLARI aynı olsa da ORANLARI farklıdır
(kalça genişliği, kol rest duruşu), yani aynı iskelete ikinci bir mesh bağlamak deforme bir gövde
üretirdi; yalnız dönüşler kopyalandığı için hedef KENDİ oranlarında çizilir. Bu bileşenin tek ön
koşulu **kemik adlarının eşleşmesidir** (aynı Mixamo rig'i) — humanoid Avatar gerekmez.
İki modelin iskelet kolonu arasındaki sabit fark (~%1.3) `heightCalibration` ile kapatılır; o
çarpanı editör aracı iki FBX'in bind pozundan hesaplar. ⚠️ **Oyuncunun gerçek boyu AYRI bir
şeydir** (kaynağın `localScale`'i, SDK yazıyor) ve ayrıca çarpılır — ikisi karıştırılmaz. Ağ yolu, MSDK retarget config'i ve
vuruş kutuları bu yüzden HİÇ değişmez; kutular karakterin (varsayılan modelin) iskeletinde kalır.
⚠️ Bu, "takım kimliği karakter mesh'ine yazılmaz" kuralının bilinçli **istisnasıdır**: renk
kuralı yerinde duruyor (renk bakana göre değişen dost/düşman bilgisidir ve property block ile
duvar arkasından okunabilecek bir işaret üretirdi), model ise oyuncunun kendi özelliğidir,
herkeste aynı görünür ve normal `ZTest` ile çizilir,
`ProximityWarning` (`Core/Player` — free-roam çarpışma önleme: `RemotePlayerRegistry` pozlarını
yerel HMD ile karşılaştırır; 1.2 m'de uzak oyuncunun konumunda **duvar arkasından da görünen**
halka (`VortexArena/ProximityHalo`, ZTest Always), 0.8 m'de tehlikenin geldiği **taraftaki**
kumandada haptik. Ölü oyuncular ELENMEZ — respawn durum değişimi olduğu için ölünün bedeni sahada
durmaya devam eder, çarpışma riski aynıdır. **Henüz hiçbir sahnede bağlı değil**: bileşen elle
eklenir, `head` ve `haloMaterial` (`_Shared/FX/M_ProximityHalo`) alanları Inspector'dan verilir),
`ControllerModelHider` (`Core/Player` — **`VA_CameraRig` kökünde**; Meta Building Blocks kamera
rigine BİRDEN FAZLA yerde (`Controller Tracking Left/Right` VE ayrıca
`OVRComprehensiveInteractionRig` altında) fiziksel Touch controller modeli + el görseli koyuyor.
Rig kökünden TÜM alt ağacı **bileşen tipiyle** tarar: her `OVRControllerHelper` ile tip adı
`HandVisual` olan her `MonoBehaviour` adaydır. ⚠️ **Oyuncunun gördüğü el rig'in kendi sentetik
elidir** (`OVRHandVisualLeft/Right` → `SyntheticHandData`); bu bileşenin işi onun ÜSTÜNE binen
İKİNCİ görselleri susturmaktır, yoksa oyuncu iç içe geçmiş eller ve elinde duran bir kumanda
modeli görürdü. Kumanda modelleri ile mesafeli kavramanın hayalet el reticle'ları **objesiyle
kapatılır** (`SetActive(false)`); oyuncunun kendi el görsellerine (`drivenHandVisuals`, **tam ad**
eşleşmesi) **hiç dokunulmaz** — ne objesine ne Renderer'ına. `LateUpdate`'te her kare yeniden
çalışır — kontrolcü bırakılıp-tutulduğunda Meta gizlenenleri yeniden aktifleştiriyor.
⚠️ İsim deseniyle çalışan bir gizleme hedefi ıskalar (§7, "rig görselleri isimle değil bileşen
tipiyle gizlenir"); ad yalnız "hangi el OYUNCUNUN" sorusunda kullanılır ve o adlar birbirine tuzak
kadar yakındır (§7, "`OVRHandVisualLeft` ile `OVRLeftHandVisual` aynı şey değildir"). Listeyle
hiçbir el eşleşmezse oturum başına bir kez **hata** basılır: o durumda gerçek eller de hayalet
sayılıp kapatılmıştır, yani **oyuncu ellerini tümden kaybeder**.
⚠️ **`SyntheticHand`, `OVRHand`, interactor'lar, retiküller ve
`HandSphereMap`'e DOKUNULMAZ** — kavrama onlara bağlıdır; kapatılırsa silah hiç tutulamaz. Sonuç:
oyuncu gözlükte **yalnız rig'in sentetik ellerini** görür; kumanda modeli **hiçbir yerde**
(lobi ve kalibrasyon dahil) çizilmez, gövde de çizilmez (`LocalBodyAvatar`). Kozmetiktir,
`OVRInput` girdisini etkilemez.
⚠️ **Ellerin görünmesi bu bileşene DEĞİL, `OVRManager.controllerDrivenHandPosesType`'a bağlıdır**
(`VA_CameraRig` prefabında `Natural`): `None` iken kumanda tutulurken el verisi hiç üretilmez,
`HandVisual` `!IsTrackedDataValid` görüp mesh'i kendi kapatır ve ekranda hiç el olmaz),
`WeatherVolumeFollow` (`Core/FX` — ambiyans parçacık hacmini yerel kameranın üstünde tutar; bağlı
sistemler **World** simülasyon uzayında olmalı, `Start` sapmayı uyarır. Yalnız kendi transform'unu
taşır, rig'e dokunmaz), `WeatherWindDriver` (`Core/FX` — kök objeye takılır, altındaki tüm
sistemlerin `Velocity over Lifetime` XZ'sini ve Noise şiddetini tek Perlin kanalından salındırır:
rüzgar şiddeti + yönü + türbülans birlikte nefes alır. Temel değerler `Awake`'te alınır,
katmanların göreli hız farkı korunur).

> **Gövde Meta Movement SDK ile çözülür ve yerel/uzak AYNI yoldan geçer.** ⚠️ Oyuncu kendi
> gövdesinden **HİÇBİR ŞEY görmez** — ne gövde, ne kol, ne el. Gözlükte gördüğü eller **rig'in
> sentetik elleridir** (`VA_CameraRig` → `OVRHandVisualLeft/Right`, ISDK `SyntheticHand`) ve
> gövdeyle hiçbir bağı yoktur: oyuncunun gördüğü el ile başkalarının gördüğü el **ayrı
> modellerdir**. Yerel gövde (`LocalBodyAvatar`) yalnız **ağ kaynağıdır**, tüm renderer'ları
> kapalıdır; başkaları o gövdeyi uzak avatarda görür — iki taraf **aynı FBX, aynı retarget
> config, aynı kod** (prefablar ayrıdır: `Avatars/Resources/LocalBodyAvatar.prefab` ve
> `App/Prefabs/RemoteAvatar.prefab`). Tek fark
> `ArenaNetCharacterBehaviour.HasInputAuthority`'dir: yerelde `true` (gövde body tracking'den çözülür
> ve ağa akar), uzakta `false` (gelen iskelet uygulanır, sensör hiç koşmaz).
>
> ⚠️ **Üç noktadan gövde TÜRETMEK bırakıldı.** Sebep yapısaldır: Meta'nın body tracking'i bir cihaz
> servisidir ve dışarıdan poz kabul etmez, yani uzak avatara "aynı body tracking'i takmak" her
> avatarın YEREL gövdeyi oynatması demektir. Doğru çözüm gövdeyi sahibinin cihazında çözüp sonucu
> akıtmaktır (`0x07`/`0x08`, §6.9/6.10) — SDK'nın kendi çözümü de budur.
>
> ⚠️ **Bacak izleme YOKTUR ve gelmeyecek:** Quest 3'te bacakta sensör yok. `BodyJointSet.FullBody`
> seçilse bile alt gövde ÜRETİLİR (generative legs), yani "full body" adı izlemeyi değil eklem
> sayısını anlatır.

| Sınıf | Görevi |
|---|---|
| `Arena/BaseZoneVisibility` | Taban şeritlerinin (`BaseZone`) görünür/etkin olup olmadığına karar veren **tek yer**. **Kendini önyükleyen kalıcı tekil** — sahneye konmaz. Kapı **takım kipidir**: takımlı modda (TDM/turnuva) şeritler durur — biri canlanma kapısı, diğeri tur arası toplanma kapısıdır; takımsızda (FFA) gizlenir, çünkü orada canlanma şartı sabit durmaktır ve renkli şerit olmayan bir kuralı anlatırdı. Hangi mod? **Öncelik `ModeSelection`** (seçili mod): admin lobide bir arena sahnelediğinde herkes o arenaya geçer ama aktif kural hâlâ lobi profilidir, yani koşan kurala bakan bir kapı "hangi maç kurulacak" sorusunu göremezdi. Seçim bilinmiyorsa `ModeRuntime`'ın takım kipine düşer. **Bileşen kapatılır** + Renderer'lı doğrudan çocuklar gizlenir. ⚠️ **Yalnız kendi kapattığını geri açar** — aynı bileşenleri `AdminSpectator` de kapatıyor. Eskiden bu iş `WeaponGranter`'ın süpürmesindeydi ve kapısı `weaponSource`'tu; FFA'da ikisi birlikte değiştiği için doğru görünüyordu, lobinin silahı rastgeleye alınınca lobideki tabanlar da kayboldu. **İkinci işi duvar-arkası görünürlüktür (x-ray):** şeritler görünürken oyuncunun **kendi** takımının şerit renderer'ına ikinci bir materyal slotu eklenir (`M_BaseZoneXRay` → `VortexArena/BaseZoneXRay`, `ZTest Greater`) — aynı mesh bir kez daha çizilir ve yalnız **önünde başka geometri olan** piksellerde görünür, yani arena dekorla dolsa da ölen oyuncu canlanma noktasını görür. Yeni GameObject / URP renderer feature / katman gerekmez, arena başına kurulum adımı doğmaz. ⚠️ **Rakip taban hiçbir koşulda çizilmez** (slot eklenmez) ve takım `Neutral` ise (takım atanmadı, admin gözlemci) hiç eklenmez; takım rengi **şeridin kendi materyalinden** okunur, ikinci bir renk tanımı yoktur. Slotu **çalışma anında** ekler çünkü `TemplateBasicsLoader` her renderer'a tek `sharedMaterial` yazıyor — asset'e konan ikinci slot o araç her çalıştığında silinirdi; ayrıca çalışma anı yolu mevcut tüm arenaları sahne düzenlemesi olmadan kapsar. Takım değişimini `PlayerCombatState.LocalTeamChanged` ile dinler |
| `ModeSelection` | **Henüz başlamamış** maçın seçili modu (`selection_state`, §5.3) — yalnız sunum. `ModeRuntime` ile karıştırılmaz: orası **koşan** maçın kuralıdır. Statik `HasValue`/`ModeId`/`IsTeamless` + `Changed`; besleyicisi `ModeRuntimePump`. ⚠️ Hiçbir kuralı/HUD'ı/loadout'u değiştirmez (maç türü `start_match`'i bekler) ve **tüketicisi olmayan alan eklenmez** — bugün tek tüketicisi `BaseZoneVisibility` |
| `ModeRuntime` (+ `ModeRuntimePump`) | Aktif maçın kurallarının **tek okuma noktası** (§3.9). `load_match.rules` / `welcome.match.rules` / `return_to_lobby.rules` besler, **`rules_update` maç ortasında tazeler** (bugün tek tetikleyicisi dost ateşi anahtarı — §3.9); kurallar telde yoksa (`rules == null`) `ModeDefinition` önizlemesi fallback olarak devralır. Lobiye dönüşte SIFIRLANMAZ, **lobi profili uygulanır** (`modeId:"lobby"`, §3.8.1) — lobideki silah seçimi loadout'unu bu anahtarla buluyor. Statik durum + statik `Changed`; pompa kendini önyükler (`BeforeSceneLoad` + `DontDestroyOnLoad`). Tüketiciler: `PlayerCombatState`, `ModeHudBase`, `AdminRoster` |
| `UI/ModeHudBase` | Mod HUD'larının **takım-agnostik** tabanı: faz/süre, geri sayım, can barı, ölüm ekranı + durum metni, kill-feed (ad çözümü `lobby_state`'ten), kendi öldürme/ölüm sayacın, maç sonu satırı. **Takıma ait hiçbir şey burada değil** — skor satırı (`ScoreLine`) ve kazanan metni (`WinnerLine`) alt sınıfın işi. Core'da durur çünkü modlar birbirini referanslamaz. `PhaseLabel`/`ModeStateLabel` `virtual`'dır: tur tabanlı mod "MAÇ" yerine "TUR 3", mod duraklamasında da "TOPLANMA 2/6" yazabilsin diye — taban `modeState`'i **yorumlamaz** |
| `Combat/ItemDefinition` | Elde tutulabilen her şeyin (silah, ileride bomba) **dar** tabanı: `netItemId` (telde giden kimlik, §6.6), prefab, `holdMode`, kanonik kavrama pozları, tracer görünümü. Davranış alanı (hasar/şarjör/fitil) **girmez** — `RemoteAvatar` eşyayı ne YAPTIĞINI bilmeden çizer; Net katmanının "oyun bilgisi içermez" ilkesinin sunumdaki karşılığı. ⚠️ `primaryGrip` = eşyanın **ele göre** pozu, `secondaryGrip` = ön kabzanın **eşyaya göre** pozu — uzayları terstir (§6.6). Soket çizimi ana noktanın **eşyaya göre** yerini ister ve o `PrimaryGripPointOnItem` olarak **türetilir** (`Inverse(R)·(−P)`), ayrı bir alan olarak tutulmaz — aynı nokta iki yerde yaşasa biri güncellenip diğeri unutulurdu. ⚠️ **`primaryGripEuler` eşyanın eldeki ROTASYONUNUN etkin ve elle ayarlanan TEK kaynağıdır** (kimlik = kumandayla birebir; stüdyonun Kaydet'i ona DOKUNMAZ); `primaryGripPosition` ise yalnız fallback'tir — etkin KONUM el başına poz düğümünden çözülür (`ItemGripAuthority`). Alanlar ayrıca kavrama soketinin ve ikincil eksenin kaynağıdır |
| `Combat/ItemGripSolver` | Kanonik kavramanın **matematiği** — saf statik, sahne bağımlılığı yok. Girdi: eşya tanımı + ana **avuç** pozu (`HandGripPivot`) + varsa ikinci elin avuç konumu + `aimBlend`; çıktı eşyanın dünya pozu. Ana kavrama ofsetini **parametreden de** alır: çağıran onu önce poz düğümünden çözer (`ItemGripAuthority`), çözemezse tanım alanlarını verir — ⚠️ o hâlde **ikincil eksen de aynı ofsetten** türer (`GripPointOnItem`), yoksa iki elli nişan silahı yamuk çevirirdi. **Tek el:** eşya ana avucun kavrama ofsetinden sürülür. **İki el:** tek elli çözümden başlanır ve eşyanın *ana kavrama → ön kabza* ekseni ikinci elin avucuna çevrilir (`FromToRotation`) — **ulaşılabilirlik bandı** ile ağırlıklandırılmış: `120°`'ye kadar birebir takip, `120°→160°` arası `SmoothStep` ile 1→0, `160°` sonrası ikinci el yok sayılır ve silah ana elin duruşunda kalır. ⚠️ Bant **kavramayı kesmez** (ön kabza bağı yalnız grip tuşuna bakar), yalnız silahın ne kadar DÖNECEĞİNİ söyler. ⚠️ Sert bir tavan (clamp) buraya geri konmaz: tavan savrulmanın yalnız büyüklüğünü kırpar, oysa sorun `FromToRotation`'ın ters-paralelde tanımsızlaşmasıdır — ağırlık o bölgede zaten `0` olduğu için savrulmanın kendisi görünmez olur ve geçiş sürekli kalır (bandın iki yakasında zıplama yok) — yani ikinci el eşyayı **taşımaz, nişanlar**: ana kavrama noktası her karede ana avucun tam üstünde kalır (konum dönüşten SONRA ve ters yönde kurulur; `delta = identity` iken sonuç tek elli formülle **özdeştir**). ⚠️ **Yerel ve uzak uç AYNI fonksiyonu koşar** — duruş telde gitmediği için (§6.6) ikinci bir uygulama aynı silahı iki ekranda iki ayrı duruşta çizerdi. Emniyetler: ön kabza ekseni 1 cm'den kısaysa (ikincil soket hiç yazılmamış) ya da iki avuç arası 5 cm'den yakınsa iki el çözümü **hiç koşmaz**. ⚠️ **Yumuşatma çözücüde DEĞİL** (`StepAimBlend` ayrı, 0.08 sn): saf kalması iki uçta koşabilmesinin ön koşulu; uzakta yumuşatma zaten telin interpolasyonundan gelir ve orada `aimBlend` sabit `1`'dir |
| `Combat/ItemGripSockets` | Kavrama noktalarını **soket** yapar: el yaklaşınca (`0.30 m`) gösterge belirir, soketin üstünde (`0.12 m`) grip'e basılınca kavrama doğar. Mesafe **anchor'dan değil AVUÇtan** ölçülür (`HandGripPivot`) — oyuncunun gördüğü şey kumanda değil sentetik eldir. Renk durumu okutur: hover **mavi**, kabul mesafesinde **yeşil** + %35 büyük (yalnız parlaklık farkı VR'da yeterince okunmuyordu). ⚠️ **Verilen silahta ANA soket kendiliğinden gizlidir** (silah tutuluyor → ana soket kapalı) **ama İKİNCİL soket ÇİZİLİR**: yoksa FFA'da eline tüfek verilen oyuncu ön kabzayı nereden tutacağını hiç göremezdi. Kapı ISDK'nın **kendi** uzatma noktasıdır — bileşen bir `IGameObjectFilter`'dır ve **iki** yakın-kavrama bileşeninin birden `_interactorFilters`'ına yazılır — `GrabInteractable` (kumanda hattı) ve `HandGrabInteractable` (el hattı); ikisini de `WeaponKitBuilder` bağlar. Yani kavramanın ALGISI ISDK'da kalır ve tel yolu (`Grabbable` → `Weapon` → `HeldItems`) hiç değişmez. Çizim ile kapı **aynı** açıklık kuralını kullanır (`IsSocketOpen`): ana soket eşya tutulmuyorsa açık, ön kabza yalnız tutuluyor + çift elli + soran el ana el değilken açık — tek elli eşyada ön kabza hiç açılmaz, bu ikinci elin aynı tabancayı kavramasını da engeller. Gösterge `WeaponCatalog.GripSocketPrefab`, yoksa prosedürel halka. Kavrama yarıçapı **silah başınadır** (`ItemDefinition.primary/secondaryGripRadius`) — tabanca kabzası ile tüfek ön kabzası aynı büyüklükte değil; hover (0.30 m) global sabit ama etkin değeri `Max(hover, yarıçap)`, yoksa yarıçap hover'ı geçtiğinde oyuncu soketi hiç görmeden kavrardı. ⚠️ El çözülemezse **fail-open** (editörde kavrama mümkün kalsın) ve bu tek seferlik loglanır — sessiz bırakılsa özellik "çalışıyor gibi görünüp" hiçbir şey yapmazdı |
| `Combat/ItemHandRig` | Prefabta kalmış **eski el rig'inin** (`<silah kökü>/Hands/Hand_<Primary\|Secondary>`) yerinin ve adının tek tanımı (statik). ⚠️ **Bu düğümlere artık hiçbir şey YAZMAZ ve okumaz:** kavrama Kavrama Pozu Stüdyosu'nda yazılıyor, çıktısı `GripPoses/Pose_*` düğümleri + `ItemDefinition` kavrama alanları; el modeli prefabın içinde durmaz. Sınıfın iki işi kaldı ve ikisi de **temizlik/emniyettir**: adı, düğümü silen editör yollarına (`GripPoseStudio` kaydı, `WeaponKitBuilder`) tek yerden verir; `HideAll` (`Weapon.Awake` + `RemoteAvatar.SterilizeVisual`) henüz temizlenmemiş bir prefabda düğümü kapatır, yoksa arenada havada bir el görünürdü |
| `Combat/ItemGripPoses` | Kavrama pozunun prefabdaki **yerinin ve adının tek tanımı** (statik): `<silah kökü>/GripPoses/Pose_<Primary\|Secondary>_<R\|L>`. ⚠️ Bu düğümler **el modelinin duruşunun ve silahın tutulma NOKTASININ etkin kaynağıdır** (silahın ROTASYONUNUN değil — o `primaryGripEuler` sabitidir) — hem eli süren taraf (`HandGripPoser`) hem silahın konumunu süren taraf (`ItemGripAuthority`) onları okur. Üretici (`GripPoseStudio` Kaydet'i — **tek üretici**) ile tüketiciler kendi string'ini taşısaydı bir harflik sapma hata değil **sessiz bulunamama** üretirdi. `Find` üç durumda `null` döner: düğüm yok · düğüm poz verisi taşımıyor (`UsesHandPose` false → yalnız konum işaretçisi) · düğüm hâlâ **silahın orijininde** duruyor (`IsUnplaced`, §7 "yerleştirilmemiş kavrama pozu düğümü"). ⚠️ Arama **ağacı taramaz**, tam iki seviye `Transform.Find` ile iner: silahın altına ileride konacak başka bir `HandGrabPose` (aksesuar, nişangâh, çerçeve) ana kavrama pozu sanılıp eli silahın ortasına sarardı. `IsUnplaced` ölçüsünü tüketicinin gerçekten kullandığı büyüklükten alır (`RelativePose`, düğümün ham `localPosition`'ından değil — araya `GripPoses` düğümü giriyor) ve eşikleri **dar** tutar (1 mm / 0.5°): amaç "yerleştirilmemiş"i ayırmak, "orijine yakın"ı elemek değil |
| `Combat/ItemGripAuthority` | "Bu eşya bu elde nasıl durur" sorusunun **etkin** cevabı (saf statik) — iki kaynağın bilinçli bileşimi, çıktısı `ItemDefinition.primaryGrip*` ile **aynı anlamda** (anchor uzayı): **ROTASYON `ItemDefinition.primaryGripEuler` sabitinden gelir** (elle ayarlanan tek düğme; kimlik = eşya eksenleri kumanda anchor'ıyla birebir — düğümdeki elin açısı eşyayı DÖNDÜRMEZ, o açı el MODELİNİ sürer: `HandGripPoser` bileği düğüme kilitler), **KONUM poz düğümünden** (`GripPoses/Pose_<Kind>_<L\|R>`): düğümün bilek NOKTASI canlı bileğin noktasına oturtulur (`gripPosition = delta.pos − Q·bilekEşyada`; anchor→bilek deltası sabit değil **canlı ölçümdür**, `HandGripPoser.TryGetAnchorToWrist`). Eşyayı düğümdeki elden döndürmek "kumandayı uzatınca namlu başka yöne bakıyor" demekti — kumanda nereye, namlu oraya. ⚠️ **Sol ve sağ AYRI çözülür** — poz düğümleri el başınadır, kabza simetrik olmadığı için iki elin bilek noktası eşyanın farklı yerlerine düşer. ⚠️ **Çözülemediğinde `false` döner ve çağıran tanım alanlarına düşer**: poz yazılmamış silah, rig'i olmayan uç (admin gözlemci, editör) ve delta ölçülmeden önceki ilk kare o yoldan geçer — fallback bir hata değil normal bir yoldur. ⚠️ **Sıra her çağıranda AYNI olmak zorunda** (`Weapon.ApplyCanonicalGrip`, `WeaponGranter.SolveInitialGrip`, `RemoteAvatar.ApplyGrip`/`TryResolveGripPalm`): biri düğüme, öteki tanıma bakarsa silah verildiği karede bir duruşta belirip sonraki karede ötekine zıplar — ya da el silahın yanında yüzer. Ters yön (`GripPointOnItem`) de buradan alınır, `ItemDefinition`'dan değil. ⚠️ **Bileğin eşyaya göre pozu ISDK'nın ÖLÇEKLİ bileşimiyle okunur** (`PoseUtils.GlobalPoseScaled`): `WPN_*` kökleri 0.8 ölçekli, ölçeksiz geri bileşim bileği eşyadan `1/0.8` kadar uzağa koyardı; ölçekli dünya pozu alındıktan SONRAKİ fark elle alınır (`InverseTransformPoint` değil), çünkü orası metredir. ⚠️ **Protokolde karşılığı YOKTUR, tel formatı değişmez** (§6.6): duruş yine telde gitmiyor, iki uç da aynı prefabtan aynı düğümü okuyup aynı deltayı ölçüyor (delta kumanda sürümlü el pozlarından türer, yani her başlıkta aynıdır) |
| `Combat/HandGripPoser` | Elde tutulan silahın kavrama pozunu ISDK'nın **sentetik eline** uygular — oyuncunun gözlükte gördüğü el budur: bilek `SyntheticHand.LockWristPose` ile kavrama noktasına kilitlenir, parmaklar `OverrideAllJoints` + `SetFingerFreedom` ile pozun şekline girer. **Kendini önyükleyen kalıcı tekil** (`WeaponGranter` kalıbı) — sahneye konmaz, yeni arenaya kurulum adımı doğurmaz; rig yoksa (admin gözlemci, sahne henüz yüklenmedi) sessizce hiçbir şey yapmaz. ⚠️ **Silahın pozuna DOKUNMAZ**: silahın dünya pozunun tek yazarı `Weapon.ApplyCanonicalGrip` + `ItemGripSolver`'dır; iki yazar aynı silahı kendi ekranında başka, karşı ekranda başka gösterirdi. ⚠️ **Yalnız YEREL oyuncuda koşar** — uzak avatarın eli ağdan gelen iskeletten çizilir, bu sınıfın ağ tarafında hiçbir işi yoktur ve **protokolde karşılığı yoktur**. ⚠️ **Bilek kilidi KOŞULSUZDUR**, mesafe/açı kapısı yoktur ve eklenmez: bedeli fiziksel kumanda kavrama noktasından uzaklaşınca elin kolla arasının görsel olarak gerilmesidir, kazancı oyuncu grip tuşunu bırakmadıkça elin silahtan kopmamasıdır — mesafeye bakan bir kapı özellikle ön kabzada eli oyuncu hiçbir şey yapmadan bırakır ve "iki elle tuttum ama ikinci el havada" hissi üretir. Bu bir üründür, eksik değil. ⚠️ **Parmak serbestliği pozun kendi `FingersFreedom` dizisinden okunur**, beş parmağa koşulsuz `Locked` yazılmaz: kilitli bir işaret parmağı ateş ederken kıpırdamaz, yani oyuncu tetiği çektiğini elinde göremez — hangi parmağın serbest kalacağı silah başına bir TASARIM kararıdır ve pozla birlikte yazılır. `[DefaultExecutionOrder(100)]`: `Weapon.LateUpdate` silahın pozunu yazıyor, bilek ona kilitleniyor — daha erken koşan bir kilit bir kare gerideki silaha sarılır ve hızlı harekette titrer. Sentetik el **adla** süzülür (`SyntheticHandData`): rig'in altında mesafeli kavramanın hayalet elleri de aynı tipte, filtresiz arama oyuncunun elini bırakıp odanın öbür ucundaki hayaleti sarardı. Eli serbest bırakmak yalnız **kilitli → serbest geçişinde** yapılır (her karede koşulsuz `FreeWrist` ISDK'nın kendi kilitlerini de iptal ederdi). Pozu olmayan silahta davranış değişmez (el kumanda duruşunda kalır) + oturum başına tek uyarı. ⚠️ **İkinci işi ölçüdür: anchor → izlenen bilek deltasını her karede, el başına ölçer** (`TryGetAnchorToWrist`) — silahın duruşunu çözen taraf (`ItemGripAuthority`) elin ANCHOR pozunu bilir, poz düğümü ise bileği tarif eder; ikisi arasındaki köprü bu deltadır ve onsuz kavrama tahmin sabitlerine düşer. Ölçü sentetik elin **KAYNAĞINDAN** alınır (`ModifyDataFromSource`, izleme uzayı → dünya çevirisi `ITrackingToWorldTransformer` ile), sentetik elin kendisinden DEĞİL: bileği zaten bu sınıf kilitliyor, kilitli eli okumak ölçüyü bir kare içinde kendi çıktısına kilitlerdi. Değer execution order yüzünden **bir kare bayattır** ve bu bilinçlidir — delta fiziksel olarak sabit bir ofsettir, bir karelik gecikmesi görünmez; ölçümü öne almak elin bir kare gerideki silaha sarılması pahasına olurdu. ⚠️ **Boştaki el de bu sınıfın kilidindedir:** silah yokken (ya da silahın kavrama pozu yazılmamışsa) el izlemeye BIRAKILMAZ, `ApplyIdle` gevşek duruşu yazar — bilek serbest, parmaklar kilitli. Gerekçe §6.9 ile aynı: parmakların duruşu oyuncunun ekranında da uzak ekranlarda da tek bir tanımdan gelmeli, yoksa oyuncu kendi elini tetiğe basınca kıvrılırken görür, başkaları sabit görür. ⚠️ Idle duruşu bir **asset değil ÖRNEKtir**: kumanda kipinde hiçbir tuşa basılmıyorken üretilen poz zaten "yumruk değil, hafif açık" eldir ve bir kez örneklenip sonsuza dek uygulanır — tetik/grip basılıyken örnek alınmaz (yarı yumruk bir idle kalıcı olurdu). Elle yazılmış bir idle donanımın ürettiğinden kaçınılmaz olarak saparadı ve fark tam da oyuncunun kendi eline baktığı yerde görünürdü |
| `Combat/NetItemCatalog` | `netItemId` → `ItemDefinition` eşlemesi (`Resources`, ilk sorguda sözlük kurar). `Tools > VortexArena > Weapons > Rebuild Net Item Catalog` projedeki TÜM `ItemDefinition`'lardan yazar — silah tablosundan değil, ki yeni bir eşya TÜRÜ (bomba) eklenince sessizce eksik kalmasın. `Resources/` altından çıkarılmaz |
| `Combat/HeldItems` | Yerel oyuncunun "hangi elde hangi eşya" durumunun tek buluşma noktası (statik). **Yazan** `Weapon`/`WeaponGranter` (`Weapon.ActiveChanged` üzerinden toplanır — çift tabanca mümkün olduğu için bildirim per-instance olamaz), **okuyan** `PlayerPoseTracker`. Hiçbir şey göndermez |
| `Combat/ShotTracer` | Havuzlu `LineRenderer` mermi izi — ömrü boyunca **sönerek** kaybolur (alfa düşer + çizgi incelir; eskiden ömrün sonunda `enabled=false` ile bir anda kesiliyordu, göz bunu sönme değil "pat" olarak okuyordu). Ayrı bir *sönme süresi* alanı YOK: sönme `tracerLifetime`'ın kendisine yayılır, yoksa iki sayıdan hangisinin diğerini kestiği sessiz bir tuzak olurdu. Üstüne **yol boyunca duman izi**. **İKİ çağıranı vardır ve olmak zorundadır:** atanın kendi izini `Weapon.Fire` çizer (sunucu atış olayını atana geri yollamaz, istemci de kendi `playerId`'sini süzer — §6.5), uzaktakileri `RemoteShotFx`. Havuz ikisi arasında **paylaşılır** (`ShotTracer.Shared`, kendini önyükleyen DDOL tekil): silah başına havuz açmak, silahların sürekli üretilip yok edildiği modlarda materyali + `Update`'i silah sayısınca çoğaltırdı. Görünüm `ItemDefinition`'dan, sıklık `tracerEveryNthRound`'dan — iki yol da aynı alanları okur (ayrı okusalar aynı silah kendi ekranında başka, karşı ekranda başka görünürdü). Sayaç yerelde silah başına, uzakta oyuncu başına (paylaşılan sayaç izleri rastgele namlulara dağıtırdı). Her mermide çizmek lazer ışını gibi durur + konumu fazla ifşa eder; asıl maliyet bayt değil GC/draw call. **Duman `Play`'in İÇİNDEDİR**, ayrı bir giriş noktası değil — ikinci bir `PlaySmoke` kapısı olsa iki çağırandan biri onu unutabilir, yani aynı silah kendi ekranında dumanlı, karşı ekranda dumansız görünürdü. Puf'lar TEK paylaşılan `ParticleSystem`'e manuel `Emit` edilir (sistemin kendi parçacık dizisi zaten havuz; atış başına `TrailRenderer` objesi üretmek Quest'te hem GC hem draw call olurdu) ve namludan isabete doğru **sönümlenir**: alfa düşer, boy büyür, ömür kısalır. Ömür `tracerLifetime`'dan TÜRETİLİR ama birebir değil — 0.06 sn'lik duman tek karelik gri lekedir, o yüzden ×katsayı + kullanılabilir banda kırpma. Eşyaya ayrı duman alanı **eklenmedi**: duman tracer'a biniyor, `tracerEveryNthRound` tracer'ı kapattığında duman da kapanır. Materyal/doku (yumuşak radyal puf) çalışma anında üretilir — hazır duman materyali `Resources/` altında değil ve serialize alan açmak bu tekili sahneye konması gereken bir bileşene çevirirdi |
| `Combat/HitMarker` | **İsabet göstergesi:** vuruşun değdiği dünya noktasında ~0.3 sn beliren, açılıp sönerek kaybolan bir X. Oyuncunun tek "vurdum" geri bildirimi — can sunucu-otoriter olduğu için (§10.3) ekranda başka hiçbir şey değişmez. **Yalnız vuran görür ve bu bir süzme DEĞİL, yapısaldır:** tek çağıranı `ArenaCombat.ReportHit`'tir ve o metot yalnız hasarı VEREN istemcide koşar; protokolde karşılığı yoktur ve eklenmez (telde bir gösterge mesajı olsaydı vurulan da kendi gövdesinde X görürdü). ⚠️ Gösterge *bildirimin yapıldığını* söyler, hasarın uygulandığını değil — sunucu vuruşu reddedebilir (dost ateşi kapalı, faz `playing` değil). Otoriter sonucu beklemek göstergeyi gidiş-dönüş kadar geciktirirdi ve `health_update` vuruşun NEREYE değdiğini taşımıyor. **Kendini önyükleyen DDOL tekil** (`ShotTracer` kalıbı, `HitMarker.Shared`): sahneye konmaz, yeni arenaya kurulum adımı doğurmaz. X **iki `LineRenderer`** ile çizilir (doku+quad değil): her mesafede keskin, doku üretmez ve `ShotTracer`'ın kanıtlanmış yolunu kullanır — paylaşılan materyal + vertex rengi, yani materyal örneği açılmaz. İki çizgi ayrı olmak zorunda; bir X tek polyline ile birleştirici kenar çizmeden ifade edilemez. Boy **açısaldır** (mesafenin katı, metre bandına kırpılı): sabit metre yakın hedefte ekranı kaplar, uzakta hiç okunmazdı. Kollar kameranın sağ/yukarı eksenlerinden kurulur (ekrana paralel düzlem, stereo tutarlı), kaldırma ise göze doğrudur — yüzeyden kaçmanın doğru yönü bakış ekseni değil o noktadan göze giden vektördür. Renk **takım renklerinden uzak** tutulur (gövdenin üstünde kırmızı/mavi X takım okumasını bozar). ⚠️ İşaret dünyada **sabit** durur, hedefe yapışmaz: vuruşun nerede olduğunu gösterir, hedefin çocuğu olsaydı ölüp yok olan avatarla kaçardı. **Görünümün tamamı `HitMarkerStyle`'dan okunur** — kodda ayar sabiti yoktur, kalan sabitler (havuz boyu, shader zinciri) yapısaldır |
| `Combat/HitMarkerStyle` | *(`_Shared/Data/Resources/HitMarkerStyle.asset`)* İsabet göstergesinin görünüm ayarı: boy (**açısal** — 1 m'deki kenar uzunluğu + metre bandı), renk/**saydamlık**, kalınlık, ömür, saydamlık ve boy **eğrileri**, kontur (X'in dışındaki ikinci kalın X — açık zeminde okunurluk), çizgi materyali (glow için additive bağla) ve **`markerPrefab`**. `WeaponCatalog` ile aynı gerekçeyle `Resources`'ta: `HitMarker` kendini önyükleyen bir tekildir, bağlanacak referans alanı yoktur. ⚠️ **Asset zorunlu DEĞİL** — yoksa alan başlangıç değerleri kullanılır (`CreateInstance`), yani varsayılanların tek doğruluk kaynağı C# dosyasıdır ve asset onun kopyasıdır. Taşınırsa/adı değişirse gösterge çalışmaya devam eder ama **ayarlar sessizce yok sayılır** (bu yüzden açık bir hata değil, dokümante edilmiş bir kural). `markerPrefab` bağlanırsa çizgi X hiç çizilmez, örnek havuzlanır ve görünümün tamamı prefabın olur — `HitMarker` yalnız yeri, boyu, dönüşü, ömrü yönetir (renk/kontur alanları o yolda okunmaz). ⚠️ Eğri **boş bırakılırsa** koddaki formül yedeği devreye girer: boş bir `AnimationCurve` her yerde 0 döner, yani gösterge tümden görünmez olurdu. Sayılar/renkler/eğriler her karede okunur → Play kipinde canlı ayarlanır; materyal ve prefab havuz düğümü kurulurken bağlanır (ayar değişince düğüm yeniden kurulur) |
| `Combat/ArenaCombat` | **Oyun kodunun ağa açılan tek kapısı** (statik). `ReportShot` / `ReportThrow` / `ReportHit` / `ReportRaycastHit` / `ReportAreaHit` + `TryGetTargetPlayerId` / `IsHeadshot` / `CanFire` / `LocalPlayerId`. ⚠️ `ReportShot`/`ReportThrow` **UDP olay kanalına** (`0x03`) gider, `ReportHit` **WS**'te kalır — kaybı kozmetik olan ile otoriter olanın kanalı ayrıdır (§10.3). Bir vuruşu doğru bildirmek dört şeyi bilmeyi gerektiriyor (arena uzayı, yön≠nokta, `RemoteHitBox` ile hedef çözme, hasarı istemcinin belirlemesi) — bunlar `Weapon` içinde gömülü kalsaydı ikinci bir hasar kaynağı yazan herkes aynı dördünü yeniden keşfederdi. `Weapon` de bu kapıyı kullanır (tek doğruluk kaynağı). Bağlantı yokken sessizce no-op. **Tek sunum işi burada durur:** `ReportHit` gönderimden sonra `HitMarker`'ı tetikler — kapının kendisiyle aynı gerekçe, yeni bir hasar kaynağı isabet göstergesini bedavaya alsın. Reçeteler: `Gelistirici/Yemek-Kitabi.md` |
| `Combat/WeaponFrame` | Sahnedeki silahın **çerçevesi** — `VA_WeaponFrame` prefabı olarak her `WPN_*` kökünün çocuğudur (`WeaponKitBuilder` bağlar). Kaynak silahı **dondurur** (Rigidbody kinematik + yerçekimsiz, `Grabbable`/`GrabInteractable`/`HandGrabInteractable`/`DistanceHandGrabInteractable`/`ItemGripSockets` kapatılır) → yakın kavrama tümden kapalıdır, silah **yalnız uzaktan** alınır ve çerçeveden hiç ayrılmaz. ⚠️ **İki mesafe-kavrama bileşeni birden taşır ve ikisini de dinler** (`DistanceGrabInteractable` = kumanda hattı, `DistanceHandGrabInteractable` = el hattı): hangisinin koşacağını ISDK rig'i "el izleniyor mu" sorusuna göre seçiyor (§7). ⚠️ El hattının **`Hand Alignment`'ı `None`'dır**: `AlignOnGrab` ISDK'ya kavrama boyunca sentetik elin bileğini kavranan nesneye kilitletir ve çerçeve yerinde dondurulduğu için oyuncunun eli sahnedeki silaha yapışır (§7). Pointer olaylarında `Select` gelince `WeaponGranter.SelectWeapon(...)` çağrılır — yani oyuncunun eline giden şey kaynağın bir **KLONU**dur. Nişan geri bildirimi **ISDK'nın kendi mesafe-kavrama göstergesinden** gelir (tüp + reticle); çerçevenin kendi `LineRenderer` ışını `isRayVisible` ile **kapalıdır** — ikisi birden açıkken elde iki ışın görünür ve kapatmak menzil bilgisini kaybettirmez, çünkü ISDK'nın aday listesi her adayı `CanBeSelectedBy` ile (yani `WeaponFrame.Filter` ile) süzer: menzil dışındaki çerçeve aday bile olmaz. Aynı zamanda bir `IGameObjectFilter`'dır (mesafe kapısı `_interactorFilters`'a bağlanır). Çerçeve görselinin görünürlüğü `isFrameVisible` ile **örnek başına** (sahneden sahneye) ayarlanır — reçete `Gelistirici/Yemek-Kitabi.md`. ⚠️ **Çerçeve yalnız silah SABİT dururken vardır:** `Weapon.HeldChanged` dinlenir ve silah hangi yoldan tutulursa tutulsun (ele verildi ya da ISDK ile kavrandı) çerçevenin GameObject'i kapanır, bırakılınca geri gelir. Kural olayda durur, çağrı noktalarında değil — "silahı ele alan" birden çok yol var ve her birine ayrı ayrı "çerçeveyi de kapat" eklemek yeni bir yol açıldığında unutulacak bir adım olurdu. Abonelik bu yüzden `Awake`/`OnDestroy`'dadır: `OnDisable`'da olsaydı çerçeve kendini kapattığı anda "bırakıldı" sinyalini duyamaz ve bir daha geri gelmezdi |
| `Combat/WeaponGranter` | Silahın ele geldiği **tek nokta** (§3.9). **Kendini önyükleyen kalıcı tekil** — sahneye konmaz, bu yüzden yeni arenaya ek kurulum adımı doğurmaz. İki kaynağı vardır: (a) **`RandomGrant`** kuralında grip basılıyken o elde rastgele bir silah durur, bırakılınca **yok olur**, tekrar basınca **yenisi** gelir (`Disposable`); (b) **`WeaponCanvas`** kuralında `WeaponFrame`'in çağırdığı `SelectWeapon` ile seçilen silahın **kalıcı klonu** tutulur — grip bırakılınca yalnız gizlenir, tekrar basınca arenanın neresinde olursa olsun aynı silah aynı mermiyle geri gelir (`Persistent`). Sahnedeki silah **tükenmez**, sınırsız kez seçilir. Seçim ancak başka bir çerçeveden alınarak değişir ve **harita başına** sıfırlanır. **Kaç klon:** seçilen tanım `TwoHand` ise oyuncu başına **tek** (ikinci el ön kabzaya adaydır), `OneHand` ise **el başına bir tane** (çift tabanca; her klon kendi şarjörünü taşır). ⚠️ **"Aynı anda ikinci silah tutulamaz" kuralının yeri `RandomGrant` yoludur** (`TickHand`): bir eldeki silah çift kavramalıysa öteki ele ikinci bir silah verilmez, o el ön kabzaya adaydır — yoksa oyuncu FFA'da iki tüfek tutar ve tüfeği iki elle sabitlemenin hiçbir yolu kalmazdı. ⚠️ **Çerçeve yolunda böyle bir kapı YOKTUR ve eklenmez:** orada kural yapısaldır (çift elli seçimde oyuncu başına zaten TEK klon var), seçim değiştirmek ikinci silah üretmez — yalnız rafta silah değiştirir. Seçime kapı koymak tam da bunu kırar ve belirtisi teşhisi zordur (§7). ⚠️ **Verilen silahta kavrama algısının TEK kaynağı bu sınıftır:** `Disposable` de `Persistent` klon da el anchor'ının değil bu tekilin (DDOL kökü) altında durur ve pozunu `Weapon.ApplyCanonicalGrip` her karede sürer — anchor'ın çocuğu olsaydı iki elli çözümün yazdığı dünya pozu parent dönüşümüyle çakışırdı. Klonun ISDK kavrama bileşenleri bu yüzden **açılmaz** (`PrepareSummonedClone` yalnız `ItemGripSockets`'i açık bırakır: soket görseli çizilsin); ikinci el grip'e basıp avucunu ikincil soketin yarıçapına getirince `Weapon.SetSecondaryHand` ile yazılır. İki algı yolu birden açık olsaydı aynı el iki ayrı kaynaktan "tutuyor" görünürdü. ⚠️ **Ön kabza bağının KURULMASI ile SÜRDÜRÜLMESİ ayrı kurallardır** (`ResolveSecondaryHand`, silah örneğine bağlı bir kilit): kurulma mesafeye bakar (yeşile dönen soket neyi vaat ediyorsa o), **sürdürme yalnız grip tuşuna** — ne mesafe ne açı yoklanır, oyuncu tuşu bırakana kadar bağ durur. Sürdürmeye mesafe kapısı **geri konmaz**: iki elli çözüm silahı ikinci ele doğru taşımaz, yalnız nişanlar, yani soketin avuçtan uzaklığı her zaman *|ellerin arası − silahın kavrama arası|* kadardır ve oyuncu kolunu uzatıp topladıkça bu fark kabul yarıçapını (0.12 m) kendiliğinden aşar — mesafeye bakan bir sürdürme kuralı, nişan alma/eğilme/dönme gibi normal hareketlerde bağı oyuncu tuşu bırakmadan koparır. Kilit silah **örneğine** bağlı olduğu için grip bırakılıp yeni silah gelince kendiliğinden düşer, yani bir silahtan ötekine sızmaz. **Sahne süpürmesi** (arenadaki tezgâhların gizlenmesi — ⚠️ **taban bölgeleri BURAYA AİT DEĞİL**, onlar `BaseZoneVisibility`'dedir) tek bir kapıdan geçer: `ModeDistributesWeapons` = kaynak `random` **ve** ortada **kurulmuş bir maç var**. ⚠️ "Kaynak random mı" sorusu tek başına bu sorunun cevabı DEĞİLDİR (§7): sahnelenen arena lobi profiliyle koşar ve orada kaynak da `random`'dır — kapı yalnız kaynağa baksaydı maç kurulmadan tezgâhlar gizlenir, oyuncu bekleme boyunca ne silah alabilir ne serbest atış yapabilirdi. Ayrım `modeId`'den DEĞİL kuraldan okunur (§10.5): serbest alanı benzersiz kılan bileşim `random` + `fireWhilePaused`'dur, koşan FFA de `random`'dır ama serbest atışı yoktur. **Serbest alanda iki yol birden açıktır:** oyuncu tezgâhtan seçerse o silah gelir (seçildiği anda rastgele verilenler geri alınır), seçmezse loadout'tan rastgele biri. ⚠️ Sahnelemeden FFA'ya geçişte kaynak `random` kaldığı için "kaynak değişti mi" yetmez, "mod dağıtıyor mu" da izlenir — izlenmeseydi sahnelemede seçilen silah maça taşınır ve modun rastgele dağıtımı sessizce delinirdi. ⚠️ **(b) yolunda silahı sahneye koyan bileşen YOKTUR ve yazılmaz** — yerleşim arena kararıdır, harita tasarlanırken elle konur (`BaseZone` gibi prefab örneği olarak); silah konmamış bir arenada bu yol sessizce boş döner. Canlanmada seçim korunup silah `RefillFull` ile tam şarjör + rezervle döner — dolum yeri burasıdır. **İkinci dolum kapısı `countdown` mesajıdır:** her geri sayımın başında eldeki silah dolar. Tek başına canlanma yetmiyor — tur tabanlı modda turu **sağ bitiren** oyuncu canlanmaz ve yarım şarjörle yeni tura girerdi (§3.8.2). Kapı geri sayım olduğu için mod-agnostiktir. Admin'de rig kapalı olduğu için silah verme yolu kendiliğinden kapalı, süpürme ise çalışır. Dağıtım normalde rastgeledir; **yalnız editörde** `SequentialGrant` bayrağı (dev sandbox yazar, `#if UNITY_EDITOR`) onu loadout sırasına çevirir — bütün silahları tek tek gözden geçirmek için. Üretim davranışı değişmez |
| `Combat/WeaponGrantKind` | `None` / `Disposable` / `Persistent` — silahın **nasıl** verildiği (`Weapon.GrantTo`'nun ikinci argümanı). `Disposable` = FFA'nın rastgele silahı: rezerv yok, reload kapalı. `Persistent` = çerçeveden seçilen silah: tam rezerv, reload AÇIK. ⚠️ **"Tek el/çift el" bu ayrımın parçası DEĞİLDİR** ve buraya geri eklenmez: ikinci eli `WeaponGranter` her iki türde de aynı kuralla çözer (kurulma: grip basılı + avuç ikincil soketin yarıçapında; sürdürme: yalnız grip basılı), yoksa aynı tüfek çerçeveden alındığında iki elle, FFA'da verildiğinde tek elle tutulur — aynı silah iki farklı his üretirdi. **Neden tek bayrak değil:** `IsGranted` üç ayrı kuralı birbirine kilitliyordu ("elde sabit" + "reload kapalı" + "tek el/rezervsiz"); çerçeve silahı yalnız ilkini ister. ⚠️ Serialize EDİLMEZ (çalışma anı durumu), o yüzden "yeni değer sona" kuralı burada bağlayıcı değildir |
| `Combat/SimpleWeaponDissolve` | *(her `WPN_*` kökünde; `WeaponKitBuilder` takar ve `DissolveEffect.mat`'i bağlar)* Silah ele geldiğinde **çözülerek belirir**: model geçici olarak çözülme materyaline çevrilir, `_Dissolve` 1→0 sürülür (SmoothStep, `appearSeconds`), sonra özgün materyaller geri konur. **Yalnız beliriş vardır** — bırakışta efekt yoktur, silah anında gider ve yerinde kalan bir kopya bırakmaz. Kapı **`Weapon.HeldChanged`**'dir, çağrı noktaları değil — üç tutma yolu da (rastgele verilen silah, çerçeve klonu, ISDK kavraması) tek yerden karşılansın, yeni bir yol açıldığında sessizce unutulmasın (`WeaponFrame` aynı olayı aynı sebeple dinliyor). Silahın **kendi albedosu** (`_BaseMap`/`_MainTex` + `_BaseColor`/`_Color`) özgün materyalden okunup `MaterialPropertyBlock` ile taşınır: çözülme materyali TEK bir asset ve hangi silaha takıldığını bilmiyor, taşınmasaydı silah düz renkli bir siluet olarak çözülürdü. Materyal `.sharedMaterials` ile takılır (`.materials` her renderer için toplanmayan bir kopya üretirdi). Hedefler `Awake`'te bir kez toplanır: yalnız `MeshRenderer`/`SkinnedMeshRenderer` (namlu alevi/duman ve nişan ışını kendi materyalleriyle çizilir), `WeaponFrame`'in alt ağacı atlanır. ⚠️ `OnDisable` materyalleri geri koyar: obje kapanınca coroutine ölüyor — geri konmasaydı silah bir dahaki çağrılışında yarı çözülmüş belirir, üstelik property block'lu renderer SRP Batcher dışında kalmaya devam ederdi. ⚠️ **Kenar rengi/kalınlığı, desen sıklığı gibi görünüm ayarları bileşende YOKTUR ve eklenmez** — onların tek doğruluk kaynağı **materyaldir** (`_Edge_Color`, `_Edge_Width`, `_NoiseScale`, `_DissolveAxis`, `_DirectionStrength` orada ayarlanır); bileşen yalnız `_Dissolve`'u ve albedoyu yazar, materyalin geri kalanına dokunmaz. Serialize edilen alan bu yüzden yalnız iki tane: `dissolveMaterial` ve `appearSeconds` (süreyi `WeaponKitBuilder` her koşuda prefaba geri yazar). **İki materyal seçeneği var:** `DissolveEffect` (Simple Noise — yumuşak lekeler) ve `VoronoiDissolveEffect` (Voronoi — hücresel, "parçalara ayrılıyor"); ikisi de aynı property setini konuşur, yani bileşende yalnız materyal alanı değişir |
| `Combat/FrozenGrabTransformer` | Hiçbir şey yapmayan ISDK `ITransformer`'ı: kavranan nesneyi **yerinde dondurur**. Çerçevedeki kaynak silahın `Grabbable._oneGrabTransformer`/`_twoGrabTransformer` alanlarına bağlanır. ⚠️ **Alanları boş bırakmak hareketsizlik değil, SERBEST hareket demektir** — `Grabbable.Start` ikisi de boşsa kendisi bir `GrabFreeTransformer` üretir |
| `Player/ArenaNetCharacterBehaviour` | Movement SDK'nın ağ katmanı ile ArenaNet arasındaki **tek köprü** (§6.9/6.10). SDK'nın `INetworkCharacterBehaviour`'ını uygular: ürettiği blob'u `0x07` olarak yollar, gelen blob'u `NetworkCharacterHandler.ReceiveData`'ya verir, karakterin kökünü `LateUpdate`'te arena uzayına oturtur. **Rol ayrımının uygulandığı TEK yer**: `HasInputAuthority` yerelde `true` (sensör kaynağı `MetaSourceDataProvider` açık, gövde body tracking'den çözülür ve akar), uzakta `false` (kaynak KAPATILIR — açık bırakılsaydı her uzak avatar aynı yerel sensörü okurdu). ⚠️ Kaynak bileşen prefabdan **silinmez, yalnız kapatılır**: `CharacterRetargeter.Awake` onu kendi GameObject'inden `GetComponent` ile arıyor ve yoksa assert atıyor — tek prefabın hem yerel hem uzak çalışabilmesi bileşenin orada durmasına bağlı. ⚠️ **Kökü SDK değil bu sınıf yazar**: blob'un 0. eklemi gönderenin dünya uzayındadır ve blob opak olduğu için içeriden çevrilemez, o yüzden kök arena uzayında ayrıca taşınır (§6.9). ⚠️ `NetworkTime`/`RenderTime` **sunucunun tik saatinden** gelir (`RemotePlayerRegistry.TryGetServerTimeSeconds`), `Time.unscaledTime`'dan DEĞİL: SDK'nın interpolasyonu gönderenin damgasıyla alıcının render zamanını karşılaştırıyor, iki uç aynı epoch'ta olmazsa gövde 12 Hz basamaklarla oynar. ⚠️ `ReceiveStreamAck` **bilerek boştur** — ack yalnız delta sıkıştırma içindir ve delta kapalıdır (§6.9). ⚠️ **Kök emniyeti (`GuardRootJump`)**: iskelet kökü bir gönderimde `RootJumpLimitMeters` (1,5 m) üstünde sıçrarsa son kök gönderilmeye devam eder ve yeni kök ancak `RootHoldMaxSends` (24) gönderim ısrar edince kabul edilir — emniyet **yalnız bir el tutuluyorken silahlıdır** (kumanda kaybı gövde çözümünü çökertip kökü fırlatıyor, §7 "`OVRCameraRig` el anchor'larını KOŞULSUZ yazar"), `ArenaCalibrator.CalibrationGeneration` değişince saklanan kök düşer: rig'in gerçekten taşındığı an meşru bir sıçramadır |
| `Combat/HandPoseProfile` | Bir elin parmak duruşu: **beş sayı**, parmak başına kapanma oranı (`0` açık, `1` kapalı) + `Idle`/`DefaultGrip` hazır değerleri. ⚠️ Quaternion DEĞİL oran tutulur, çünkü duruş iki ayrı iskelete uygulanabilmeli ve iki iskeletin kemik eksenleri aynı değil — ham rotasyon taşımak §7'deki "izleme uzayından gelen rotasyon humanoid kemiğe doğrudan yazılmaz" tuzağının parmak ölçeğinde tekrarı olurdu. ⚠️ `IsEmpty` (tümü sıfır) **yazılmamış** sayılır: alanı hiç görmemiş eski bir `WD_*.asset` elin tahta gibi düz kalmasına değil makul bir kavramaya düşsün |
| `Player/HandFingerRig` | Bir elin parmak zincirlerini (`…Thumb1-4` … `…Pinky1-4`) **bind pozunda bir kez** çözer ve her eklemin bükülme eksenini **ölçer**; sonra `HandPoseProfile`'ı o iskeletin kendi eksenlerinde uygular. ⚠️ Eksen sabit yazılmaz (`HandGripConvention` ile aynı gerekçe: model değişince tek satır bile değişmesin) ve sol/sağ çapraz çarpım sırası **kopyalanmaz** — avuç normali `HandGripConvention.TryMeasureBoneBasis`'ten okunur, o kuralın tek uygulaması orasıdır. ⚠️ Eksen `Cross(avuç normali, kemik yönü)`'dür; tersi yazılırsa parmaklar el sırtına kırılır. Bilek adları (`mixamorig:LeftHand`/`RightHand`) burada durur — tam eşleşmeyle aranır, çünkü parmak kemikleri o adın üstüne ek alır. Bileği ve `HandGripConvention.Correction`'ı (izleme/kavrama pozu → bu iskeletin bileği) **saklar**: ikisi de bind pozunda ölçülmek zorunda ve ikinci bir yerde tekrar ölçmek o garantiyi de tekrar vermeyi gerektirirdi |
| `Player/RemoteHandPoser` | Uzak avatarın elini **elindeki eşyaya oturtur**: parmakları eşyanın duruşundan sürer (§6.9 — parmaklar telde gitmez) ve kolu, bileği eşyanın kavrama noktasına götürecek biçimde çözer (`TwoBoneIk`). Duruşun kaynağı el değil **eşya** olduğu için aynı silah her ekranda aynı tutulur ve sol/sağ farkı oluşmaz. ⚠️ **Kolun sürülmesi kozmetik değil:** eşya ana elin **kumanda anchor'ı** pozundan çiziliyor, çizilen el ise retarget edilmiş **anatomik bilek** — iki nokta aynı yer değil (fark `HandGripPivot`'un henüz ölçülmemiş avuç ofseti + retarget hatası) ve arada onları birleştiren bir şey yoktu; belirtisi "herkesin silahı elinin biraz ilerisinde duruyor" oluyordu. Yerelde aynı boşluk görünmez çünkü `HandGripPoser` sentetik bileği kavrama pozuna **sert kilitler**; bu bileşen onun uzak aynasıdır. ⚠️ Bileğin KONUMU yazılmaz, **kol döndürülür** (gerekçe `TwoBoneIk`). ⚠️ Elde eşya yoksa kola HİÇ dokunulmaz. ⚠️ Ölçek (§10.8) için ek terim YOK: kol, ölçeklenmiş iskeletin kendi kemikleriyle çözüldüğü için el boy çarpanından bağımsız olarak eşyanın üstüne gelir. ⚠️ **Execution order 100 ile 30100 ARASINDA olmak zorunda** (bugün 30050): altında SDK iskeleti yazıp parmakları ezer, üstünde `SkeletonPoseMirror` çoktan kopyalamış olur — aradaki pencerede yazınca kırmızı takım gövdesi ve hayalet parmakları **bedavaya** alır, ikinci bir kurulum gerekmez. ⚠️ Prefaba KONMAZ, `RemoteAvatar.Awake` ekler: eksenler bind pozunda ölçülmek zorunda ve prefabdaki bir bileşenin `Awake`'inin iskeletten önce koştuğu garanti değil |
| `Player/TwoBoneIk` | İki kemikli (üst kol → ön kol → el) analitik IK, kosinüs teoremi. **Yalnız ROTASYON yazar** ve bu bir kısıt değil var olma sebebi: bileği doğrudan taşımak kemik uzunluğunu değiştirir ve `SkeletonPoseMirror` kırmızı gövdeye yalnız `localRotation` kopyaladığı için ikinci gövde ilk gövdeyi takip edemezdi (aynı oyuncu iki takımda iki ayrı duruşta çizilirdi). ⚠️ **Pole target YOK:** dirseğin bükülme düzlemi kolun **o karedeki** duruşundan okunur — sabit bir pole, retargeting'in bulduğu doğal dirsek yönünü ezerdi ve düzeltme birkaç santimlik olduğu için kazancı da olmazdı. Ulaşılamayan hedefte uzaklık kola **kırpılır** (kol düz uzanır); kemik uzunlukları kurulumda bir kez ölçülür (canlı ölçüm bir kareki hatayı sürekli sürüklerdi). Uç kemiğin kendi rotasyonuna dokunmaz — onu çağıran yazar |
| `Player/ControllerTracking` | "Bu elin anchor pozuna güvenilir mi" sorusunun **tek cevabı** (statik; `Tick()` kare başına bir kez, idempotent). Ölçüt rig'in kendi aynasıdır: `OVRInput.GetActiveControllerForHand` `None` ise `CONTROLLER_LOST`, aksi hâlde aktif kumandanın `GetControllerPositionValid`'i false ise `CONTROLLER_UNTRACKED`, true ise `CONTROLLER_OK`. **İki ayrı çıkış vardır ve karıştırılmaz:** `IsValid(right)` o karenin **ham** geçerliliğidir (debounce YOK) ve poz yazan kod yolunun kapısıdır — filtrelenmiş bir kapı bir sıfır poza saniyeler boyunca "geçerli" derdi; `GetState(right)` ise 1 sn kararlılık filtresinden geçmiş **göstergedir** (`ArenaProtocol.CONTROLLER_*`, admin satırı) — filtresiz bir gösterge her tracking kırpışmasında yanıp sönerdi. `OVRManager` yokken (admin, gözlüksüz editör) durum `UNKNOWN` ve `IsValid = true`: orada kumanda diye bir şey yok, kapı kapanırsa hiçbir poz akmaz |
| `Player/HandGripPivot` | "Kumanda anchor'ı verildiğinde oyuncunun **avucu** nerede duruyor" sorusunun tek cevabı: anchor → avuç ofseti (el başına bir `Vector3`, metre) + `Resolve` yardımcısı. **Neden var:** kavramanın referansı anchor idi, oysa oyuncunun gördüğü şey kumanda değil **sentetik eldir** — aradaki birkaç santim, silahı elin içinden geçmiş ya da havada duruyor gösteriyordu. Anchor'a doğrudan bakan her tüketici (`Weapon`, `WeaponGranter`, `ItemGripSockets`, `WeaponFrame`, `RemoteAvatar`) buradan geçer. ⚠️ **Uzak taraf da geçmek ZORUNDA**: telde giden el pozu anchor pozudur (§6.6), ofset iki uçta aynı yerden uygulanmazsa aynı silah iki ekranda iki ayrı duruşta çizilir. ⚠️ **Rotasyon bilerek anchor'ın kendisidir** ve `HandGripConvention.AnchorBasis` buraya karıştırılmaz: ikisi tek sabite bağlanırsa uzak gövdenin bileğini düzeltmek silahın duruşunu bozar (ve tersi). ⚠️ **Etkin kavrama zincirinde bu sınıf yalnız "anchor'a göre avuç nerede" terimini taşır:** silahın rotasyonu `primaryGripEuler` sabiti (anchor ekseninde), konumu poz düğümü + canlı ölçülen anchor→bilek deltasıdır (`ItemGripAuthority`). ⚠️ **Ofset bugün SIFIRDIR** (avuç = anchor) ve buraya tahmin edilmiş bir sayı yazılmaz: ölçülmemiş bir ergonomi sabiti hem silahın yerini kumandanın gerçek pozundan koparıyor hem de silah başına yazılan kavrama verisiyle **aynı şeyi iki düğmeden** ayarlıyordu — hangisinin bozuk olduğu ayırt edilemezdi. Silahın elde nerede durduğu tek yerden ayarlanır: Kavrama Pozu Stüdyosu (poz düğümleri; fallback alanları da aynı kayıttan yazılır). Gerçekten ölçülmüş bir bilek ofseti gerekirse yeri yine burasıdır (`HandGripCalibrationProbe` ölçer); kimlik dönüşümün arkasında kapı bırakmak `ArenaSpace` deseniyle aynıdır |
| `Player/HandGripConvention` | Anchor (kumanda) uzayındaki el pozunu karakterin el kemiğinin bind eksenine çeviren **statik köprü**. Kemik anatomisi (parmak yönü = hand→MiddleProximal, avuç normali = parmak×başparmak) **modelden çalışma anında ölçülür**, sabit derece yazılmaz: karakter değişince burada tek satır değişmez. Sabit olan tek şey anchor tarafındaki el anatomisidir ve bugünkü değeri ergonomik bir TAHMİNDİR; kesin değeri `HandGripCalibrationProbe` ölçer. Sol ve sağ ayrı hesaplanır; ortak bir ofset iki eli birden düzeltemez (§7). ⚠️ **Kapsamı iki kez daraldı ve dar kalır.** Gövde buradan geçmez (kol/bilek zinciri Movement SDK retargeting'inden geliyor, SDK kendi eşlemesini kendi yapıyor); **authoring döngüsü de geçmez** — silahın eldeki duruşu poz düğümünden, ölçülmüş deltayla çözülüyor. Bugünkü iki tüketicisi: `WD_*` fallback kavrama alanlarının **yazımı** (`GripPoseStudio` → `AnchorProxy`, tek yönde) ve uzak gövdenin el kemiği köprüsü. ⚠️ **Buraya yeni tüketici eklenmesi bir uyarıdır:** tahmini sabitlerden türeyen bir dönüş etkin kavrama yoluna girdiği anda, gözle doğru yerleştirilen el tahminin hatası kadar yanlış yere yazılır (belirtisi: silah oyunda dönük) |
| `Player/HandGripCalibrationProbe` | Yukarıdaki tahmini sabitin **kesin** değerini cihazda ölçen geliştirici aracı (`VA_CameraRig`'de durur): bir kez log basıp kendini kapatır, çıkan satırlar `HandGripConvention`'a (el YÖNÜ) ve `HandGripPivot`'a (avuç KONUMU) yapıştırılır — aynı örneklemeden iki ayrı sabit çıkar. ⚠️ **Etkisi FALLBACK kalitesiyle sınırlıdır:** o iki sabit silahın eldeki etkin duruşunu artık belirlemiyor (onu poz düğümü + canlı ölçülen delta belirliyor), yalnız `WD_*` kavrama alanlarının yazımını ve uzak gövde köprüsünü etkiler — yani prob koşulmasa da silah elde doğru durur. Ölçüm kaynağı **rig'in kumandadan sürdüğü sentetik el iskeletidir** (`OVRHandVisualLeft/Right → OculusHand_* → b_*_wrist`) — oyuncunun gözlükte gördüğü elin ta kendisi, yani "anchor'a göre el nerede" sorusunun cevabı odur. ⚠️ **Ön koşul `OVRManager.controllerDrivenHandPosesType ≠ None`** (`VA_CameraRig`'de `Natural`): kapalıyken kumanda tutulurken el verisi hiç üretilmez ve prob **hatasız ama yanlış** bir sabit basar (bind pozu). ⚠️ Denenip elenen iki kaynak: `OVRInput.Controller.LHand/RHand` **multimodal** ister (projede kapalı), mesafeli kavrama önizlemesindeki kopyalar (`OVRLeftHandVisual`/`OVRRightHandVisual`) ise `ControllerModelHider` tarafından kapatılır (kapalı kemik sürülmez, bind pozu ölçülürdü). Oyun kodu onu OKUMAZ. ⚠️ Ölçüm kaynağı ile o kopyalar **yalnız kelime sırasıyla** ayrılır (§7, "`OVRHandVisualLeft` ile `OVRLeftHandVisual` aynı şey değildir"); probe'un beslendiği ele `ControllerModelHider` `drivenHandVisuals` listesi sayesinde hiç dokunmaz, listeden çıkarılırsa obje tümden kapanır — o zaman yalnız ölçüm değil **oyuncunun elleri** de gider |
| `Player/LocalBodyAvatar` | Oyuncunun gövdesi — **yalnız ağ kaynağı**, hiç çizilmez. ⚠️ Oyuncu kendi gövdesinden HİÇBİR ŞEY görmez (gövde de kol da el de); gözlükte gördüğü eller **rig'in sentetik elleridir** ve bu sınıfın onlarla ilgisi yoktur. **Görünmezlik yalnız çizimdedir; telde tam gövde gider.** Uzak avatarla **aynı FBX ve aynı retarget config'ini** `Owner = Host` olarak kurar (`ArenaNetCharacterBehaviour.Initialize(playerId, hasInputAuthority: true)`), yani "başkalarının gördüğü gövde" tek doğruluk kaynağıdır. **Kendini önyükleyen kalıcı tekil** (`WeaponGranter` kalıbı): prefabı `Resources.Load("LocalBodyAvatar")` ile yükleyip sahne köküne kurar, sahneye elle KONMAZ → yeni arena bir kurulum adımı doğurmaz. Gövde ancak **iki koşul** birden sağlanınca kurulur: etkin bir `OVRCameraRig` (yani rol gerçekten oyuncu) ve sunucudan alınmış bir `playerId` (blob onunla etiketleniyor, §6.9). Kurulumda `HideAllRenderers()` alt ağaçtaki **tüm** Renderer'ları istisnasız kapatır ve bir daha çalışmaz — gövdeye sonradan renderer eklenmiyor; prefabda da hepsi kapalı gelir, çalışma anındaki geçiş yalnız garantidir. ⚠️ **Obje YAŞAMAYA devam eder ve yıkılmaz** — görünmez olması "gereksiz" demek değildir: bu obje giderse oyuncu ağa hiç iskelet göndermez ve **diğer oyuncular onu göremez**. Artık yerelde tek bir pikseli bile çizilmediği için bu refleks daha da tehlikelidir: sileninin ekranında hiçbir şey değişmez. ⚠️ Görünmezlik **renderer düzeyinde** yapılır, obje kapatılarak DEĞİL (§7, `OVRBody` kendini kalıcı kapatabilir) ve **kemik ölçeğiyle de DEĞİL** (§7, ağa giden iskelet `localScale`'i de okur). Retargeter hiç poz uygulamazsa (`RetargeterValid` kalıcı `false`) tek atışlık bir `LogError` basılır ve ⚠️ **bu satır TEK sinyaldir**: arıza yerelde hiçbir iz bırakmaz (eller rig'den geliyor), oyuncu başkalarının ekranından silinmiş olduğunu kendi başına anlayamaz (§7). **Gövde kalibrasyonunun tetikleyicisi de buradadır** ve ⚠️ **varsayılan KAPALIDIR** (`calibrateBodyProportions = false`): kapalıyken `CharacterRetargeter.Calibrate()` hiç çağrılmaz ve herkes prefabın oranlarını kullanır — açılırsa **uzak gövde bozulur**, çünkü blob `SerializationCompressionType.High` ile eklem uzunlukları üzerinden sıkıştırılıyor ve gönderenin oranı değişince alıcının hedef iskeleti uyuşmaz (§7). ⚠️ Alanın **yerel karşılığı YOKTUR**: gövde çizilmiyor, eller rig'den geliyor — yani açmanın oyuncunun kendi ekranında hiçbir kazancı yok, geriye yalnız bedeli kalıyor. Alan açıldığında yol şudur: `ArenaCalibrator.CalibrationGeneration` değişince 3 sn sonra `Calibrate()` çağrılır — gecikme zorunludur, oyuncu arena kalibrasyonunu zemine EĞİLEREK yapıyor ve o andaki poza sabitlenen gövde oranı maçın kalanı boyunca yanlış boy demektir. ⚠️ Sürenin dolması tek başına yetmez: `Calibrate()` geçerli poz yokken **sessizce hiçbir şey yapmaz** (§7), bu yüzden çağrı koşul sağlanana dek her karede yeniden denenir. Kalibrasyondan sonra uygulanan gövde ölçeği bir kez raporlanır; `ScaleRange` sınırına dayanmışsa uyarıya döner — sınıra dayanmış ölçekte **diğer oyuncular** bu oyuncuyu yanlış boyda görür; sonucu yalnız onlar görür, oyuncunun kendi ekranında iz bırakmaz. ⚠️ Sınıf `[DefaultExecutionOrder(30000)]`'dir: `Calibrate()` o karenin **uygulanmış** pozunu ölçmeli, yani SDK'nın retarget döngüsünden ve iskeleti serileştiren `NetworkCharacterHandler`'dan (order 100) sonra koşmalıdır. ⚠️ Avatar **sahne kökünde** durur, rig'in altına konmaz (§7, "retarget avatarı hareket eden kökün altına konmaz"). ⚠️ Gövdede **collider yoktur** — `Weapon`'ın atış raycast'i maskesiz, kendi gövden kendi atışını yerdi. Admin'de kurulmaz ve bu rol kontrolüyle DEĞİL, etkin rig yoksa hiç çalışmayarak sağlanır (`AppSession` App asmdef'indedir, Core onu göremez) |
| `Team` | `Red` / `Blue` / **`Neutral`**. `BaseZone`'da `Neutral` = herkese açık joker. ⚠️ Yeni değer SONA eklenir: `BaseZone`/`Weapon` bu enum'u serialize ediyor, başa ekleme her arenanın taban takımlarını kaydırır |

**`ArenaRoof`** (çatılı arenalar için, **isteğe bağlı**): çatı hiyerarşisinin köküne konur
(`GameObject > VortexArena > Arena Roof`), altındaki **tüm** Renderer'lar çatı sayılır ve
`ArenaRoof` katmanı (user layer 8) damgalanır — hangi geometrinin gizleneceği sahne görünümündeki
Layers süzgecinden görülsün. Katman yalnız ayıklama içindir; davranış Renderer listesinden gelir,
damga unutulsa da çalışır. Gizleme `MaterialPropertyBlock` üstünden `_BaseColor` alfasıyla;
tam gizlemede Renderer **kapatılmaz**, `ShadowsOnly`'ye alınır → çatı çizilmez ama gölgesini atar
(kapatılsaydı iç mekân aydınlanıp kuş bakışı okunmaz olurdu). Son uygulanan alfa statik tutulur,
yeni sahnedeki çatı `OnEnable`'da devralır → kuş bakışındayken arena değiştirilince çatı bir kare
bile görünmez. Oyuncu tarafında etkisi YOKTUR — yalnız `AdminSpectator.RefreshRoof()` tetikler.
**Yapımcıya verilecek tek parça teknik not: [`Cati-Gizleme.md`](Cati-Gizleme.md).**

**`ObstacleViolationProbe`** (kendini önyükleyen kalıcı tekil; sahneye ve prefaba KONMAZ): yerel
oyuncunun **kafası** bir iç engelin içinde mi diye 20 Hz ölçer ve sonucu `IsViolating`'ten
yayınlar — cezayı sunucu uygular (`ArenaNet-Protokol.md` §10.9). **Tek kural budur.**

⚠️ **İki ayrı çıktı üretir:** `IsViolating` (yalnız KAFA → tel bayrağı + ceza + karartma) ve
`IsBodyBlocked` (kafa **ya da izlenen bir el** → yalnız **ateş kapısı**, tele gitmez, bekleme süresi
yok). Ceza *"görüşüm geometrinin içinde mi"*, ateş kapısı *"gövdemi göstermeden mi ateş ediyorum"*
sorusudur — bloğun içinde durup silahı dışarı uzatan oyuncu ikincisini ihlal ediyor ama silahı
tertemiz boşlukta. ⚠️ **İzlenmeyen el hiç sorulmaz** (rig onun anchor'ını rig orijinine yazıyor,
`ControllerTracking`): kumandası kapanan oyuncu sebepsizce ateş edemez hâle gelirdi.
⚠️ **Ölçülen kütlenin oranını yargılayan bir kural YOKTUR ve geri eklenmez:** Quest'te alt gövde sensörle ölçülmez, üst gövdeden üretilir (`OVRBody` FullBody =
generative legs) ve üretilmiş bir uzuv, oyuncu siperin arkasında dururken siperin *içinde*
çözülebilir — oran kuralı kaçınılmaz olarak dokunulmamış bir cisimden ceza üretir. Kafa merkezi
kemikten değil **HMD'den** gelir, yani kural body tracking hiç çalışmasa da işler.

Ölçüm yedi noktalı bir küre kabuğudur (merkez + ±x/±y/±z, yarıçap 11 cm). ⚠️ **EŞİK ile RAMPA
ayrı şeylerdir** ve bu ayrım kuralın çekirdeğidir: giriş = merkez içeride **ya da** yedi noktanın
≥3'ü içeride (+0.15 sn minimum süre), çıkış = **hiçbir** nokta içeride değil — histerezis ikinci
bir eşikten değil buradan gelir. Hedef geometri `Obstacle` layer'ından; geniş faz tek bir
`OverlapSphereNonAlloc`'tur ve **yakında engel yokken orada durur**. ⚠️ **Kafaya collider konarak
yapılamazdı:** `Weapon`'ın atış ışını maskesiz — oyuncu kendi atışını kendi gövdesine yerdi;
üstelik trigger "değdi mi" der, "ne kadarı içeride" demez. Nokta-içeride testinin kendisi burada
değil `ObstacleVolumes`'dedir.

**Karartmayı da bu bileşen sürer ve iki durum vardır, gerekçeleri farklıdır:** kafa içeride →
**koşulsuz tam siyah** (0.2 sn'de; orada görülecek meşru bir şey yok ve duvar arkasını görmek tam
olarak istismarın kendisi — kuralın anti-hile ayağı budur, ceza değil), kabuk değiyor ama kafa
içeride değil → **hafif kararma** (tavan 0.35, nokta sayısıyla rampalı; gerekçesi kalibrasyon
sapmasıdır, birkaç santimlik hizalama hatası oyuncuyu aniden kör etmesin). ⚠️ **Karartma derinliğe
BAĞLANMAZ:** yedi noktanın kaçının içeride olduğunu alfaya çevirmek "içerideyim ama görüyorum"
demektir — kafası yarı gömülü oyuncu duvarın öbür yüzünü okumaya devam eder. Ayrıca nabız
titreşimi. Teşhis için `LastTrigger`, `HeadInsideLevel`, `FadeAlpha` ve son cevap veren engelin adı
okunabilir — Dev penceresi Play kipinde bunları çizer.

**`ObstacleWarningOverlay`** (`VA_CameraRig` → `CenterEyeAnchor` altında, `HMD Katmanlarını Kur`
aracının kurduğu): ihlal boyunca karartmanın üstünde nabız atan uyarı yazısı. Ölçüm yapmaz, probe'un
durumunu okur; alfası `FadeAlpha`'ya bağlıdır (yazı ekran kararırken gelir, ondan önce belirmez).
Karartma quad'ından **daha yakın** durduğu için saydam sıralamasında onun üstüne çizilir.

**`DamageVignette`** (aynı yerde): can kaybının kırmızısı. Can düştükçe nabız (tek pakette 25 HP =
tam nabız, saniyede 2 birim sönüm), can 40'ın altındayken sabit çerçeve; ölüyken çizilmez.
⚠️ **`ScreenFade`'e kaynak olarak EKLENEMEZ ve eklenmemelidir:** hakem "en yüksek alfa kazanır"
diyor, yani engel karartması 1.0'dayken hiçbir kırmızı görünmez ve oyuncu kapkaranlık bir ekranda
canının gittiğini fark etmez. Bu yüzden kendi renderer'ı ve kendi shader'ı vardır
(`VortexArena/ScreenVignette` — `Overlay` kuyruğu + `ZTest Always`), yani karartmanın **üstüne**
çizilir. Engele özel değildir: mermiyle gelen hasarda da çalışır. Değeri **abonelikle değil kare
başına farkla** okur — can yalnız ağ mesajıyla değişiyor, kalıcı tekilin doğuş sırasına bağlı bir
abonelik ömrü yönetmeye gerek yok.

**`ObstacleVolumes`** (statik): *"bu şey bir iç engelin içinde mi"* sorusunun **tek** cevabı, üç
biçimde: `Sample` + `Contains` (bir sorgu, çok nokta — kafa ve el ölçümü), `ContainsPoint` (tek
atışlık, kendi tamponuyla, ölçüm turunu bozmadan — namlu) ve **`OverlapsBox`** (yönlendirilmiş kutu
— silahın gövdesi; "namlu içeride mi" tek noktadır, "silahın herhangi bir parçası değiyor mu" ise
bir HACİM sorusudur ve nokta örneklemesiyle cevaplanamaz). ⚠️ `OverlapsBox` konvekslik süzgeci
uygulamaz ve buna gerek de yoktur: kutu-mesh kesişimi `ClosestPoint`'e dayanmadığı için o API'nin
"her nokta içeride" yalanı oraya bulaşmaz. ⚠️ **Konvekslik şartının
gerekçesi burada durur:** `Collider.ClosestPoint` non-convex bir `MeshCollider`'da girdi noktasını
aynen döndürür → her nokta "içeride" okunur → sahnedeki herkes anında ölmeye başlar; böyle bir
collider kalıcı olarak elenir ve **bir kez** hata basar. Aynı sebeple içeriden yüzey mesafesi
**ölçülemez** — derinlik ancak çok nokta örnekleyerek yaklaşıklanır.

**`ScreenFade`** (statik hakem): HMD karartma quad'ını isteyen **iki** sistem var (muhafaza ·
engel ihlali) ama quad tektir. Kaynaklar alfalarını her karede bildirir, **en yüksek** olan çizilir;
bildirmeyi bırakan kaynak 0.25 sn sonra kendiliğinden düşer (kalp atışı sözleşmesi — "kapat" demeyi
unutmak mümkün değil). ⚠️ Karışım (alfa toplama) bilinçli olarak yok: iki yarı saydam katman üst
üste binince sonucu hiçbir kaynak istememiş olurdu. Renderer'ın **sahibi** yine `ArenaBoundary`'dir
(quad onun serialize alanı); hakem yalnız hangi değerin çizileceğini söyler.
⚠️ **Buraya üçüncü bir GÖRSEL katman eklenmez** (hasar kırmızısı, ölüm efekti, mod göstergesi):
"en yüksek alfa kazanır" kuralı, tam karartma varken kendisinden düşük olan her katmanı **görünmez
kılar**. Bu hakem yalnız *"ekran ne kadar kararsın"* sorusunu cevaplar; üstte görünmesi gereken her
şey kendi renderer'ıyla ve daha yakında/`Overlay` kuyruğunda çizilir (`DamageVignette` ·
`ObstacleWarningOverlay`).

**`ArenaLayers`** (statik): `Obstacle` layer adının tek yazıldığı yer, maskeyi bir kez çözer ve
layer tanımsızsa **bir kez hata basar**. Gerekçe: `LayerMask.NameToLayer` tanımsız adda `-1` döner,
maske `0` olur ve sorgu sessizce hiçbir şey bulmaz — yanlış yazımın belirtisi "sistem hiç
çalışmıyor" olurdu.

### Editör: `VortexArena.Core.Editor` (içerik araçları — yalnız Editor)

Menü öğelerinin tam listesi ve "ne zaman çalıştırılır" tablosu `CLAUDE.md`'de; burada arena
geometrisini üreten araçlar + kavrama ayarı:

| Sınıf | Görevi |
|---|---|
| `ItemHandGripBake` | **Avuç** düğümü ↔ `WD_*` kavrama alanları dönüşümünün **tek** uygulaması: `FromWrist` kayıt yönü (avucun dünya pozu → tanım alanları), `ToWristLocal` ters yön (elin başlangıç yerleşimi). İkisinin birbirinin tersi olması "elleri oluştur → dokunma → Kaydet değeri değiştirmez" kimliğinin dayanağıdır. ⚠️ **Referans AVUÇTUR (kumanda anchor'ı), el modelinin bileği DEĞİL** — stüdyodaki elin kökü bilek çerçevesindedir ve anchor ondan `HandGripConvention.Correction` ile **türetilir** (`AnchorProxy`), bu yüzden `Pose` alan bir aşırı yükü vardır: yalnız o imza için sahneye geçici bir GameObject açmak ölçünün referansını görünmez bir yan etkiye bağlardı. ⚠️ **Tükettiği alanlar FALLBACK'tir:** yerel oyuncunun silahı poz düğümünden sürülüyor, buradaki sayılar kavrama soketini ve rig'siz uçları besliyor — "silah elde ters duruyor" belirtisinde ilk bakılacak yer poz düğümüdür. ⚠️ **Var olma sebebi ASİMETRİ:** `primaryGrip` "el → eşya", `secondaryGrip` "eşya → el" yönündedir — ana elde ters bileşim gerekir, ikincilde gerekmez. İki ayrı yerde elle yazılsaydı biri er geç ters yazılır ve belirtisi "silah elde ters duruyor" olurdu. ⚠️ Ölçek dönüşümü kullanılmaz (elle bileşim): kavrama ofsetleri METRE cinsindendir |
| `GripPoseStudio` (+ `GripHandAuthoring`, `GripHandAuthoringEditor`) | `Kavrama Pozu Stüdyosu`: silahın elde nasıl duracağını **gözlük takmadan** ayarlama aracı ve kavramanın **elle yazıldığı tek yer**. Akış **prefab kipindedir**: `WPN_*` prefab kipinde açılır → *Ana Kabza Ellerini Oluştur* / *Ön Kabza Ellerini Oluştur* (sağ+sol hayalet el) → eller kabzaya oturtulur → parmaklar `GripHandAuthoring` Inspector'ından (kıvrım/açıklık/eklem ince ayarı/parmak serbestliği) ya da Hierarchy'den bükülür → istenirse *Karşı Ele Aynala* → *Kaydet*. ⚠️ **El HAM sürüklenir ve çeviriden geçmez:** elin kökü doğrudan ISDK **bilek** çerçevesidir. ⚠️ **Tezgâhtaki el SİLAHI DÖNDÜRMEZ:** elin yeri oyunda (1) el MODELİNİN silah üstündeki duruşunu (`HandGripPoser` bileği düğüme kilitler) ve (2) silahın avuca oturacağı NOKTAYI belirler; silahın kumandaya göre rotasyonu `WD_*`'daki elle ayarlanan `primaryGripEuler`'dır (kimlik = kumandayla birebir, `ItemGripAuthority`). `HandGripConvention.Correction` yalnız İKİNCİL `WD_*` alanları yazılırken, tek yönde uygulanır (`AnchorProxy`) — ölçülmemiş tahmin sabitlerinden türeyen o çeviri authoring döngüsüne girdiği anda kullanıcının doğru gördüğü eli tahminin hatası kadar yanlış yere yazardı. ⚠️ **Eller prefabın İÇİNE girmez:** her el, prefab stage sahnesinin ayrı bir köküdür (`[VA El_<Kind>_<R\|L>]`, `HideFlags.DontSave`) — prefab kipi yalnız `prefabContentsRoot` altındaki ağacı diske yazar; elin prefaba sürüklenmesi kazası hem Kaydet'te hem `WeaponKitBuilder`'da temizlenir. Ölçek 1'e sabitlenir (silah 0.8; el küçülseydi "avuç kabzayı sarıyor mu" sorusu %25 yanlış orandan cevaplanırdı) ve iskelet/eksen ölçümü el **poza sokulmadan ÖNCE** yapılır (bükülmüş elden ölçülen baz o duruşu tanıma yazardı). Elin başlangıcı sırayla: yerleştirilmiş poz düğümü → `WD_*` alanları (`ItemHandGripBake.ToWristLocal`) → kabza parçasının kabaca ortası; ilk iki yol kaydın **birebir tersi** olduğu için "oluştur, dokunma, Kaydet" değeri değiştirmez. **Kaydet:** tezgâhtaki her elin duruşu + parmakları kendi `GripPoses/Pose_<Kind>_<L\|R>` düğümüne; `WD_*.asset`'e **sağ** elden yalnız `primaryGripPosition` (bilek noktasından, `ItemHandGripBake.PrimaryPositionFromWrist` — **`primaryGripEuler`'a DOKUNULMAZ**, o elle ayar) ve ikincil alanlar (yalnız anatomik baz ölçülebildiyse — ölçülemezse alanlara DOKUNULMAZ, yanlış yazmaktansa boş bırakmak), tek taraflı kavramada eksik el ISDK aynasıyla (`HandGrabUtils.MirrorHandGrabPose`; ⚠️ karşı el tezgâhtaysa ya da karşı düğüm zaten yerleştirilmişse ayna KOŞMAZ, yoksa elle ayarlanmış bir kavramayı ezerdi), ayrıca eski `Hands/*` ve prefaba sızmış `[VA El_*]` temizliği. ⚠️ **İki el de ayrı yazılır** — poz düğümleri el başınadır, ayna bir kolaylıktır. ⚠️ **Parmaklar `HandPuppet.CopyCachedJoints` ile okunur, ham `localRotation` ile DEĞİL** (§7, `RotationOffset` tuzağı). ⚠️ Yazım **açık prefab kipinin kendi içeriğine** yapılır ve stage kirli işaretlenir (`LoadPrefabContents` yolu kullanılmaz: aynı prefab kipte açıkken ikinci bir kopyaya yazmak kullanıcının ekranındaki hâli sessizce ezerdi) — `WD_*.asset` anında yazılır, düğümler prefab kaydedilince iner. Poz düğümünün yerel konumu `InverseTransformPoint` ile, bilerek **ölçekli** (ISDK'nın `HandGrabPose.RelativePose`'u da öyle). ⚠️ **Scene view'a hiçbir şey çizilmez** (yardımcı çerçeve/ölçü katmanı ve `SceneView.duringSceneGui` aboneliği yoktur); kavramanın doğruluğu gözle değerlendirilir, pencere yalnız kaydedilecek sayıları canlı ve **salt okunur** gösterir. **Var olma sebebi geri besleme süresidir:** kavrama tek doğru değeri olan bir sayı değil, "avuç kabzaya değiyor mu / işaret parmağı tetiğe ulaşıyor mu" sorusunun cevabıdır ve o soruyu APK build'i + gözlük turuyla sormak her denemeyi dakikalara çıkarır. ⚠️ Eller Play'e girerken, stage kapanırken ve sahne değişince silinir; temizlik kancaları **pencereden bağımsız** kurulur (kullanıcı pencereyi kapatıp prefab kipinde çalışmaya devam edebilir). ⚠️ Sahne açılışındaki temizlik **yalnız açılan sahneye** uygulanır ve **önizleme sahnelerine (`.prefab` yollu — önizleme sahnesinin yolu prefabın asset yoludur) hiç dokunmaz**: prefab kipi Auto Save ile kaydedip içeriğini yeniden yüklerken bu olay tetikleniyor, açık stage'i de süpüren bir temizlik kullanıcının o an sürüklediği eli silerdi (§7). Prefab kaydının fazladan kökleri gizlemesine karşı bekçi `EditorApplication.update`'tedir (`RestoreHandFlags`, §7). ⚠️ `GripHandAuthoring` bilerek **runtime asmdef'indedir** (`VortexArena.Core`) ve dosyanın tamamı `#if UNITY_EDITOR` sarmalındadır: Unity, editör asmdef'inde derlenen bir `MonoBehaviour`'ı `AddComponent` ile kabul etmez (sessizce `null` döner), build güvenliği ise sarmaldan + ellerin `DontSave` olmasından gelir. Slider'ları **kolaylıktır, doğruluk kaynağı değildir**: Kaydet her zaman kemiklerin o andaki gerçek duruşunu okur. ⚠️ Eklem eşlemesi **ada göre değil KİMLİĞE göre** yapılır (`HandJointMap.id`): ISDK'nın OpenXR ve OVR dallarında kemik adları farklı, kimlikler aynıdır. Dal tahmin edilmez **ölçülür** (`#if ISDK_OPENXR_HAND` bizim derlememizde her zaman false olurdu). Modal dialog yok, sonuç `Debug.Log` |
| `DimensionMeshBuilder` | `JSON'dan DimensionMesh Üret`: boyut dosyasından **ölçü maketi** üretir — tek `Plane` (ProBuilder çokgeni, extrude 0) + kolon başına bir prizma (pivotu ayak izinin ağırlık merkezinde, sürüklemek doğal olsun diye) + iki kalibrasyon küpü (`anchor_a` kırmızı / `anchor_b` mavi; **küpün merkezi noktanın kendisidir**, yarısı zeminin altında kalır — Inspector'daki konum dosyadaki nokta ile birebir aynı okunsun diye; dosyada nokta yoksa üretilmez ve uyarılır). ⚠️ **Sahnenin kalibrasyon işaretçilerini üreten tek yer burasıdır**, yani her arenada çalıştırılması zorunludur; kök + küpler build'e girsin diye maket `EditorOnly` etiketlenmez (görsel dalı build'den `DimensionMeshBuildStripper` ayıklar). ⚠️ **Duvar ÜRETMEZ ve maket oynanan geometri değildir**: arena sanatı maketin üstüne kurulur. ⚠️ **Kök SAHNEDEN BAĞIMSIZ kurulur**: sahne kökünde, dünya orijininde, dönüşsüz ve 1 ölçekte — hiçbir şeyin altına parent'lanmaz, böylece dosyadaki ölçü sahnede birebir okunur. Arenanın üstüne oturtmak isteyen elle taşır/döndürür; geri okuma maketin kendi kökünü referans aldığı için etkilenmez. **İdempotent**: aynı mekanın maketi varsa silinip yeniden kurulur. Üretimden önce halkayı `Polygon2D.IsSelfIntersecting` ile denetler |
| `DimensionMeshReader` | `DimensionMesh'i JSON'a Çevir`: maketi okuyup **kaynak dosyanın üstüne** yazar (hedef sorulmaz, maketin işaretçisinden gelir). Ayak izi çıkarımı: yatay yüzler (`\|normal.y\| > 0.9`) Y seviyesine göre gruplanır, **en alt** grup alınır (prizmada alt yüz kazanır), kenarlar XZ'ye izdüşürülüp kaynaştırılır, **yalnız bir kez geçen** kenar sınır sayılır, halka yürünür ve doğrusal ara köşeler ayıklanır. Noktalar dünya üstünden kök uzayına çevrilir — kolonu sürüklemek/döndürmek doğru yazılır. ⚠️ Kenarlar köşe **indeksiyle değil konumla** anahtarlanır: ProBuilder sert normaller için köşeleri yüz başına ayırıyor, indeksle bakan tespit tüm mesh'i sınır sanar. Kalibrasyon noktaları `DimensionAnchor` küplerinin transformundan okunur; ⚠️ küp yoksa dosyadaki `calibration` **KORUNUR** (sıfırlanmaz — eski bir maketi çevirmek mekanın zemin bandı ölçüsünü silerdi). Yazmadan önce çıktı geri ayrıştırılır; doğrulanamazsa dosyaya **dokunulmaz** |
| `DimensionMeshBuildStripper` | Menü değil, **build kancası** (`IProcessSceneWithReport`): ölçü maketinin görsel dalını (`Plane` + `Columns`) build'e giden **geçici sahne kopyasından** siler — sahne dosyasına dokunmaz, kök ve `DimensionAnchor` küpleri kalır (kalibrasyon onlara bağlı). ⚠️ Gerekçe boyut değil **bağımlılık**: çokgenler `ProBuilderMesh` taşır ve o bileşen `Unity.ProBuilder`'ı runtime derlemesine sokardı; bu projede ProBuilder yalnız editör tarafıdır. Editör Play kipinde kanca koşmaz, orada görseli `ArenaDimensionMesh.Awake` `Renderer.enabled` ile gizler |
| `TemplateBasicsLoader` | `Template Temellerini Yükle`: aktif sahneye altyapıyı **prefab örneği** olarak koyar (`VA_ArenaBoundary`, `VA_CameraRig`, `VA_PoseSync`, `VA_CalibrationManager`; seçime bağlı `VA_ModeHud` · taban bölgeleri), `ArenaCalibrator`'ın sahneye bakan alanlarını bağlar, `ArenaBoundary`'nin rig'e bakan alanlarını (`head`/`fadeRenderer`/`warningText`) `VA_CameraRig` içinden bağlar ve mekanın boyut dosyasını `ArenaBoundary.dimensionsJson`'a takar. ⚠️ **Kalibrasyon işaretçisi koymaz ve yerleştirmez** — onların tek kaynağı ölçü maketidir (`DimensionMeshBuilder`); ikinci bir üretici hangisinin geçerli olduğunu belirsizleştirirdi. Taban bölgelerini takım malzemesiyle boyar (tek `VA_BaseZone` prefabı iki takıma da hizmet ediyor; şerit rengini çalışma anında kimse yazmıyor). **İdempotent** — var olan örneği asset yoluyla tanır ve atlar; dolu bir alanın üstüne YAZMAZ |
| `ObstacleLayerAuditor` | `Engel Hacimlerini Denetle`: açık sahnelerde `Obstacle` layer'ını tarar ve **konveks olmayan** collider'ları, trigger'ları, collider'sız damgalı objeleri ve **görünen yüzeyden şişkin** collider'ları raporlar (dialog + tıklanabilir konsol satırları). Şişkinlik testi çalışma anındaki testin **aynısıdır** (`ClosestPoint` ile nokta-içeride; ikinci bir matematik ikinci bir doğruluk kaynağı olurdu) ve kalan geometrik tuzağı yakalar: konveks işaretlenmiş **içbükey** bir mesh'te hull çukuru doldurur, collider görünenden büyür ve oyuncu **boşlukta** ceza alır — çizilen mesh doğru olduğu için gözle görülmez. Üçgen ağırlık merkezleri yüzeyin **iki yanına** 2 cm taşınır ve ölçüt "ikisi de içeride"dir: tek yana bakmak üçgen sarım yönüne güvenmek olurdu ve yön ters okunsaydı araç HER objeyi şişkin raporlardı. ⚠️ Yalnız `MeshCollider` denetlenir (kaynak: collider'ın kendi mesh'i) — Box/Capsule zaten bilinçli bir kabalaştırmadır ve aracın **kendi önerdiği** çözümdür; onu hata saymak olurdu. Mesh okunamıyorsa (Read/Write kapalı) ya da collider/obje kapalıysa test yapılmaz, obje sorunlu diye raporlanmaz. **Var olma sebebi tek bir kuraldır ve gözle denetlenemez:** non-convex bir `MeshCollider` bu layer'a girerse `Collider.ClosestPoint` girdi noktasını aynen döndürür → nokta-içeride testi her zaman "içeride" der → **o sahnedeki herkes anında ölmeye başlar**. Çalışma anında `ObstacleVolumes` böyle bir collider'ı eliyor ama o satır ancak Play'e girilince görülür; bu araç aynı soruyu sahne kaydedilmeden önce sorar. ⚠️ Hiçbir şeyi DÜZELTMEZ — otomatik convex işaretlemek sanatçının bilerek yaptığı seçimi sessizce ezerdi |
| `HmdOverlayBuilder` | `HMD Katmanlarını Kur`: `VA_CameraRig.prefab` içindeki `CenterEyeAnchor`'a engel uyarı yazısını (`ObstacleWarningOverlay`) ve hasar vinyetini (`DamageVignette` + `M_DamageVignette`) kurar. **İdempotent.** ⚠️ **Vinyetin materyalini araç ÜRETİR** — shader import edilmeden GUID'i bilinemediği için elle yazılmış bir `.mat` sessizce boş shader referansıyla açılırdı; materyal bir asset olduğu ve prefabtan referanslandığı için de build'den strip edilmez (`Shader.Find` ile çalışma anında üretilseydi Quest'te pembe çizilirdi). ⚠️ **Çizim sırası prefabdaki MESAFEDEN gelir** (yazı 0.42 · vinyet 0.44 · karartma quad'ı 0.5) — sayılar değişirse sıra korunmalı. Araç çalıştırılmadıkça davranış eskisi gibi: karartma çalışır, yazı ve vinyet hiç çizilmez |
| `BuildElementsConfigurator` | `Configure All Build Elements`: kayıt listelerini **klasör ağacından eşitler** — `Venues/*/Scenes/*/` taranır ve klasör tek doğruluk kaynağı sayılır. **Hepsini Yapılandır** önce aktif sahnenin `MapDefinition`'ını yazar/günceller, sonra eşitler; **Yalnız Senkronize Et** sahne açık olmadan da eşitler (silinmiş bir arenanın kalıntısını temizlemenin yolu budur). Eşitleme: eksik olan **uyarı** üretir (kutuda sahne yok / birden çok sahne var / sahne adı klasör adıyla uyuşmuyor / `Data/<Sahne>.asset` MapDefinition yok ya da yanlış yerde / mekan kökünde `Art,Data,Prefabs,Scenes` dışında klasör), fazla olan **silinir** (Build Settings'te mekan ağacında olmayan ya da diskte bulunmayan satırlar; `GameCatalog.maps` ve `ModeDefinition.maps` içindeki ölü ve artık taranmayan referanslar). `Boot.unity` index 0'da kalır, mekan-dışı sahneler (`_Shared/Scenes/*`) korunur, `Template/` sahneleri listeye hiç girmez. `ModeDefinition.maps` **boşsa** dokunulmaz (boş = kısıtsız); doluysa o modu destekleyen haritalarla birebir eşitlenir — hedef küme boş çıkarsa liste boşaltılmaz (boş liste "kısıtsız" demek olurdu), yalnız uyarı basılır. Sonda `ServerConfigExporter.Export(false)` + **sağlık raporu**: `ArenaBoundary` var mı · `dimensionsJson` dolu mu · muhafaza dünya orijinine yakın mı (arena uzayı = dünya uzayı) · ölçü maketi `EditorOnly` etiketli mi (etiketliyse build'e girmez ve kalibrasyon işaretçileri onunla birlikte silinir). Hiçbiri işi durdurmaz, hepsi rapora satır düşer. Kontroller **aktif sahneye** bakar, yani sahne bir kutuda değilse hiç koşmaz. ⚠️ **MapDefinition kendiliğinden ÜRETİLMEZ:** `supportedModeIds` boş bırakmak "kısıtsız" demek olduğu için üretilen boş bir tanım lobiyi sessizce her modda oynanır kılardı — sahne açılıp modlar araçtan seçilir. Ayrı bir "Arena Id" alanı yoktur: MapDefinition'ın adı sahne adıdır |

### Sunucu: `Server/VortexArena.Server.Core`

| Sınıf | Görevi |
|---|---|
| `ControlHost` | Kestrel WebSocket host (`/ws`), bağlantı başına `ClientConnection` |
| `BeaconService` | 2 sn'de bir broadcast |
| `StateHost` | UDP kaydı, poz alımı, 20 Hz snapshot yayını (16 girdiden fazlası MTU'ya sığan parçalara bölünür; olay varsa ve sığıyorsa `0x05` ile tek datagramda birleşir), `0x06` RTT echo'su. **Telemetriyi burada üretir:** saniyelik `[state]` satırı — gerçek bayt-sn/paket-sn, tik kayması, uplink jitter + poz/olay kaybı; eşiği aşan oyuncu için ek `[net]` satırı |
| `PlayerRegistry` | Oyuncu listesi, `playerId` tahsisi (1..255), `devices.json` ile kalıcı **kimlik** (ad + forma numarası), bağlantı durumu ve **maç katılımcısı defteri**. ⚠️ **"Çevrimdışı" diye bir durum YOKTUR** (§2): `Connected` → soket düştü ya da `HEARTBEAT_TIMEOUT` doldu → `Reconnecting` (kayıt durur, maç kapılarına girmez) → `RECONNECT_GRACE` da dolarsa oyuncu **çıkarılır**: koşan maçın katılımcısıysa `Left` olarak maç sonuna kadar durur, değilse kayıt silinir ve playerId havuza döner. **Maç defteri** (`MatchParticipant` → `inMatch`) istatistik satırını bağlantıdan bağımsız kılar: `left` bir kayıt yalnız bu bayrak yüzünden yaşar. ⚠️ Defter **lobiye dönerken** kapanır, `match_end`'de DEĞİL — erken temizlik maç sonu tablosunu tam da okunduğu anda boşaltırdı. ⚠️ `Left` kayıtlar `_players`'ta durduğu için playerId'leri `NextFreePlayerIdLocked` tarafından zaten atlanır; ayrı bir rezervasyon defteri gerekmez. ⚠️ Toplu bayrak yazımı (maç kurulumu/kapanışı) **tek bir** `Updated` yayınlar — kayıt başına yayın N tam roster JSON'u demek olurdu. ⚠️ Soket alanının adı `Socket`'tir; `Connection` **durumu** taşır (aynı isimde iki kavram olamazdı). **Kimlik:** ilk bağlantıda ad 20'lik havuzdan rastgele (kullanılmayanlar arasından), numara 1'den itibaren ilk boş (1..99); `set_identity` ikisini de değiştirir. Adlar tekrar edebilir, **numara tüm KAYITLI cihazlar arasında benzersizdir** — sahiplik sorgusu `_players`'a değil `_devices`'a bakar (hiç bağlanmamış cihaz da numara tutar). Çevrimiçi sahipten numara istenirse reddedilir; çevrimdışı sahip **aynı anda** yeniden numaralanır. **Rol başına kalıcılık farkı:** oyuncu kaydı yukarıdaki iki aşamadan geçer (deviceId kalıcı, geri dönen aynı satıra oturur — ad, numara, takım, `kills/deaths/score` korunur); **admin kaydı ilk adımda tümüyle SİLİNİR** ve `Reconnecting` durumuna hiç girmez (deviceId oturumluk — geri gelen admin yeni bir kimlikle gelir, o satır asla eşleşemezdi; ayrıca her açıp kapatma roster'da hayalet satır ve tükenen playerId bırakırdı) ve admin adı diske yazılmaz. Aynı PC'de iki admin varsa ad " (2)" ile ayrıştırılır. **Atma bunun istisnasıdır** (`RemoveByPlayerId`): kayıt anında silinir ve katılımcılıktan da düşer — kopma satırı bırakır (maç sonu tablosunda görünür), atma bırakmaz; `devices.json`'a dokunulmaz, yani atılan cihaz geri bağlanırsa adını/numarasını korur (§5.4) |
| `LobbyService` | Roster yayını (`lobby_state`) — **tek yayıncı döngüden**, kirli bayrakla birleştirilerek, her yayında `version` artarak (Tuzaklar: "ateşle-unut yayın sıra garantisi vermez"); `status.rosterVersion` geride kalan istemciye yalnız ona tam snapshot yollatır. Ayrıca ready/takım/kick/`set_identity` + **adminler arası ortak durumun sahibi**: mod/harita seçimi burada yaşar, `set_selection` ile değişir, `admin_state` ile yalnız adminlere yayılır. Her admin komutu "kim ne yaptı" duyurusu üretir |
| `MatchDirector` | **Faz makinesi (10 Hz tick), vuruş hattı, can/skor, canlanma.** Mod kaydı tek yerde (`RegisterModes()` — yeni mod buraya bir satır). **Skor defteri:** `AddScore(team,…)` (takım) + `AddPlayerScore/ScoreOf/TryGetLeader` (bireysel); modlar skoru YALNIZ buradan yazar. **Mod komutları** (§3.8.2): `TryPauseForMode` / `SetModeState` / `TryStartRound` / `TryCancelCountdownForMode` — modun fazı doğrudan yazmasını (ikinci otorite) ve kendi mesajını yollamasını (ikinci gönderici) gereksiz kılar. **Dost ateşi anahtarının da sahibi burasıdır** (§3.9): açılışta kapalı, yalnız `set_friendly_fire` çevirir, yürürlükteki kural şekline `ApplyRulesLocked` damgalar. ⚠️ **Takımdaş öldürmede `OnKill` çağrılmaz** — skor yazılmaz, `kills`/`deaths` ve kill feed işler. **Canlandırmanın iki yolunun da sahibi burasıdır** (§3.7): `HandleReviveRequestAsync` (oyuncunun talebi) ve `HandleAdminReviveAsync` (operatörün `revive_player` komutu; `playerId:0` = o an ölü olan herkes) — ikisi de canlandırmayı `RevivePlayerLocked` ile yapar. ⚠️ Canlandırma `ResetMatchStateLocked` ile YAPILMAZ: o sunucudaki alanları yazar ama istemciye hiçbir şey göndermez, oyuncu ölüm ekranında donar |
| `MapTable` | `maps.json` (Unity export'undan) — sunucunun okuduğu tek içerik tablosu. Girdi başına yalnız `sceneName` + `modes`; **arena ÖLÇÜSÜ yoktur** (sunucu metre kullanmaz, §7.30) |
| `Modes/IGameMode` + `TdmMode` + `FfaMode` | Mod kuralları: skor, kazanma koşulu, tur süresi. Yeni kancalar **varsayılan gövdeyle** eklenir (default interface method) → mevcut modların hiçbiri değişmez; **tüketicisi olmayan kanca EKLENMEZ**. `FfaMode` yüzeyin ilk tüketicisidir: takımsız + bireysel skor + sabit durma canlanması, `MatchDirector`'a tek satır kayıt dışında hiçbir dokunuş yok. `OnRoundStart` ikinci örnektir: Live'a HER girişte çağrılır, tur kavramı olmayan modlar hiç yazmaz |
| `Modes/TournamentMode` | **Tur tabanlı takım elemesi** (§3.8.2). Kural olarak TDM'den tek farkı `Revive = None`'dır; turun tamamı bu sınıfın iç durumudur (`_round`, `_roundLive`, `_matchOver`). Eleme `OnKill`'de değil **`OnTick` taramasında** ölçülür — takım bağlantı kopmasıyla da boşalır ve o yolda `OnKill` çağrılmaz; tek tarama tek doğruluk kaynağıdır. Süre dolunca **savaşabilir** (canlı **ve** kalibreli) sayısı fazla olan tur alır, eşitse kimseye puan yok. Toplanma kapısı `set_ready` bayrağını yeniden kullanır ve **zaman aşımı yoktur**: tur herkes tabanına girmeden başlamaz, geri sayım her koşulda iptal edilebilir, çıkış operatörün `kick`/`abort_match` komutudur. Bekleme uzarsa 30 sn'de bir konsola teşhis satırı basar (tur başlatmaz). ⚠️ `IsMatchOver`'da `TimeRemaining <= 0` dalı YOKTUR: bu modda o sayaç **turun**dur |
| `Modes/ModeRules` | Modun ŞEKLİ (§3.9): takım kipi, skor kanalı, dost ateşi, canlanma, silah kaynağı, canlanma gecikmesi. `ToInfo()` ile tele çıkar. Varsayılanı (`TeamDefault`) bugünkü TDM'dir. ⚠️ `FriendlyFire` alanını **modlar yazmaz** — onu `MatchDirector` operatörün anahtarından damgalar |
| `Modes/MatchOutcome` | Maç sonucunun tek tipi: kazanan takım **veya** kazanan oyuncu (`match_end`'in iki kanalı) |

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
`OnMatchEnd`, `OnReturnToLobby`, `OnRemoteFireEvent`, `OnIdentify`, `OnKicked`,
`OnRulesUpdate` (koşan maçın kural şekli değişti — `ModeRuntimePump` dinler),
`OnSelectionState`, `OnAdminState`, `OnNetStats` (son ikisi yalnız admin bağlantısında).

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

Açılışta **hangi mekanın oynatılacağı sorulur** (§11.1 — `maps.json`'daki `venue` alanı, o da
Unity'deki klasör yerleşiminden gelir). Seçim oturum boyunca sabittir: yalnız o mekanın haritaları
`start_match` ile başlatılabilir ve admin harita seçicisinde yalnız onlar görünür
(`admin_state.venueScenes`). Soruyu atlamak için `server.json → venue` doldurulur ya da
`--venue <ad>` verilir; konsol etkileşimli değilse sunucu bloklanmaz, ilk mekanla açılır.
Mekanı değiştirmek = sunucuyu yeniden başlatmak.

Sunucuyu **operatör launcher'ı da başlatabilir** ve başlatırsa mekanı her seferinde `--venue` ile
geçer — sessiz "ilk mekan" yoluna hiç girilmez (launcher mekan seçilmeden başlatmaz). Launcher
sunucuyu **kapatmaz**: ömrü operatör uygulamasına bağlı değildir, kapatma sunucunun kendi
penceresinde Ctrl+C'dir. Sunucu istenirse eskisi gibi elle de çalıştırılır.

Bu **geliştirirken de geçerlidir**: dev penceresinin (`Tools > VortexArena > Development > Dev`) sunucuyla hiç
işi yoktur — ne başlatır, ne öldürür, ne derler (elle başlatılmış bir sunucunun Play çıkışında ya
da editör kapanışında ölme riski kalmasın diye). Derleme `dotnet build Server/VortexArena.Server.sln
-c Release` (ya da `scripts\deploy-server.bat`), çalıştırmak yine elle:
`deploy\server\VortexArena.Server.App.exe`.

### 6.2 Quest olmadan test (loopback) — `Tools > VortexArena > Development > Dev`

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
| **Başlangıç: Açık sahneden** | Arena sahnesine doğrudan Play. Bir kare sonra `DevSession` **seçili hedefe bağlanır** — arena sahnesinde `LobbyController` olmadığı için bunu başka kimse yapmaz; bağlanmazsa can/skor/faz gelmez ve `CanFire` hiç açılmaz. Maç verisi (takım / mod / süre / limit / faz) **yalnız sunucudan** gelir: `welcome.match` geç-katılım senkronu (`SceneRouter`) ya da gerçek `load_match`. Sunucuda koşan bir maç yoksa istemci maç verisi almaz — bu beklenen davranıştır, bir **admin** maçı başlatmalıdır. Hedef "keşif" kipindeyse (ip boş) bağlanılmaz ve sebebi loglanır — arena sahnesinde adres girecek arayüz yok |
| **Sunucusuz sandbox** (+ mod) | Sunucuya **hiç bağlanılmaz**: sunucu açma, admin'den harita seçme ve elle kalibrasyon üçü de atlanır. `DevSession` bağlanmak yerine `ModeRuntime.Apply` ile iki alan yazar — seçilen `modeId` (**silah loadout'u buradan okunur**; onsuz silah gelmez) ve `fireWhilePaused = true` (faz sunucusuz `paused` kaldığı için tetiği açan tek kapı). **Tek seçim moddur;** silah kaynağı seçilmez: sandbox her zaman grant yolunu kullanır (`weaponSource:"random"`) ve `WeaponGranter.SequentialGrant` ile loadout **SIRAYLA** dağıtılır — grip'e her basışta bir sonraki silah. Çerçeve yolu sandbox'ta kullanılmaz (amaç silahı hemen ele almak). Yalnız **"Açık sahneden"** başlangıcıyla ve **kabuk dışı** bir sahnede (arena ya da mekan lobisi) çalışır; ikisi de sağlanmazsa konsola uyarı düşer. ⚠️ Hasar/skor/faz **YOKTUR** — üçünün de otoritesi sunucudadır; bu kip yerel şeyler (silah duruşu, namlu, ses, kavrama) içindir |
| **"Dev enjeksiyonu" onayı** | Kapatılırsa üretim yolu **birebir** koşar (rol `AppBoot`'tan, adres keşif zincirinden) — beacon keşfini editörde denemenin yolu |

Editör **player** rolündeyken ortamda maçı başlatacak bir admin kalmaz: ikinci bir istemciyi
**admin** rolünde bağla (ikinci bir editör/Windows admin build'i) ya da rolü `Ctrl+Alt+R` ile
çevirip maçı oradan başlat. ⚠️ Bu adım **gerçek maç akışı için** zorunludur — editörün maç verisi
üreten bir kısa yolu yoktur, maçı her zaman bir admin başlatır.

**Maç akışı gerekmiyorsa sandbox kipi bu üçlüyü tümden atlar.** Silah duruşu/namlu/ses/kavrama
gibi tümüyle YEREL şeyleri denemek için sunucu, admin ve kalibrasyon gerekmez: sandbox açıkken
sunucuya hiç bağlanılmadığı için kalibrasyon kapısı da kendiliğinden açık kalır
(`CalibrationState.IsCalibrated` = `!_hasEverConnected`) ve `ArenaCombat` UDP kanalı yokken
sessiz no-op olduğu için silahlar olduğu gibi çalışır. ⚠️ Sandbox **sunucudan gelmiş gibi mesaj
üretmez** ve bir maç simülasyonu değildir: hasar, skor, faz ve canlanma yoktur; takım/skor/canlanma
kuralları `ModeRulesInfo` varsayılanında (TDM) kalır. Gerçek kural davranışı test edilecekse
sunucu + admin yolu kullanılır — sapmada sunucu kazanır (§10.5).

### 6.3 Build ve dağıtım

Dört bileşenin her biri kendi script'iyle `deploy/` altına üretilir:

| Komut | Ne yapar | Çıktı |
|---|---|---|
| `scripts\deploy-admin-game.bat` | Unity batch-mode Windows build (`PlayerBuildTool.BuildWindowsAdmin`) | `deploy\admin\VortexArena.exe` |
| `scripts\deploy-player-apk.bat` | Unity batch-mode Android build (`PlayerBuildTool.BuildQuestPlayer`) | `deploy\player\game.apk` + `install_game.bat` |
| `scripts\deploy-server.bat` | `dotnet publish -r win-x64 --self-contained` + `config/` kopyası | `deploy\server\VortexArena.Server.App.exe` |
| `scripts\deploy-launcher.bat` | `dotnet publish -r win-x64 --self-contained` | `deploy\launcher\VortexArena.Launcher.exe` |

**İki Unity build'i tek sahne listesini paylaşır.** Windows build'i admin, Android build'i Quest
oyuncusudur; ikisi de Build Settings'teki etkin sahneleri aynen kullanır. Liste platforma göre
ayrıştırılmaz — ayrışsaydı bir arenayı admin bilir oyuncu bilmez olurdu ve `start_match` sessizce
reddedilirdi (sahne TÜM oyuncuların `hello.scenes` listesinde aranır). `PlayerBuildTool` build'e
girmeden önce **diskte olmayan sahne satırlarını** yakalayıp adlarıyla iptal eder: silinmiş bir
arenanın satırı Build Settings'te kalabiliyor ve o hâlde `BuildPipeline` sebebi görünmeyen bir
yığın iziyle düşerdi.

- **İki Unity build'i de canlı ilerleme basar.** İkisi de Unity'yi doğrudan değil
  `scripts\lib\watch-unity-build.ps1` üzerinden çalıştırır: izleyici kendi log'unu
  (`deploy\admin-build.log` / `deploy\player-build.log`) akarken okur ve tek satırlık durum
  gösterir (aşama · Bee yüzdesi · o an çalışan araç · log
  boyutu · CPU). Batch-mode Unity konsola hiçbir şey yazmadığı için "takıldı mı ilerliyor mu"
  başka türlü görünmüyordu. Log ~3 dk büyümez ve CPU da harcanmazsa uyarı basılır; hata satırları
  (proje kilidi, `error CS…`) anında ekrana düşer. Post-mortem: aynı betik `-ReplayLog <log>` ile
  bitmiş bir log'un aşama haritasını çıkarır. Süre `<log>.last`'a yazılır ve sonraki
  koşuda "~mm:ss" referansı olarak gösterilir. Ayrıntı: `scripts/README.md`.
- **Unity build'leri editör AÇIKKEN alınamaz** — batch-mode Unity proje kilidine takılır. Script
  bunu **kontrol etmez** (bilinçli: editör kapatıldıktan sonra bile AI motoru gibi alt süreçlerin
  `Unity.exe`'si arka planda yaşıyor, `tasklist` kontrolü yanlış alarm veriyordu). Build
  ilerlemiyorsa Ctrl+C ile iptal edip süreçleri kapat (izleyici Unity'yi de kapatır). Önceki
  `deploy\admin-build.log` silinemezse script uyarır — o dosyayı hâlâ bir Unity süreci tutuyor
  demektir.
- **Sunucu ve launcher build'i yalnız .NET 10 SDK ister.** İkisi de self-contained tek klasör
  üretir; işletme PC'sine .NET kurmak gerekmez. Launcher açıkken `deploy\launcher\` kilitli olur —
  script bunu kontrol edip uyarır.
- **Hedef platform betikte sabittir, aktif platformdan türetilmez.** İki Unity betiği de Unity'yi
  kendi hedefiyle **başlatır** (`deploy-admin-game.bat` → `-buildTarget Win64`,
  `deploy-player-apk.bat` → `-buildTarget Android`); bayrak açılışta verilir çünkü platformu
  `-executeMethod`'un içinden çevirmek `SwitchActiveBuildTarget` ile domain reload tetikleyip
  çalışan metodu yarıda bırakıyor. Projede hangi platform açık kalmış olursa olsun her betik kendi
  çıktısını üretir. Aktif platform hedefe eşit değilse geçiş açılışta olur ve **o koşu tam
  reimport** demektir (texture'lar yeniden sıkıştırılır: Quest'te ASTC, Windows'ta DXT — 20-40 dk);
  sonrakiler hızlıdır. Platform build sonunda **geri alınmaz**, geri almak ikinci bir tam reimport
  daha olurdu. `deploy-player-apk.bat` ayrıca Android Build Support modülünü build'e girmeden önce
  doğrular (`Data\PlaybackEngines\AndroidPlayer`): modül yoksa Unity platformu çeviremez, sessizce
  Windows'ta devam edip `.exe` üretirdi.
- **İki Unity build'i birbirinin cache'ini ısıtmaz.** `Library/` ortaktır ama içindeki şeritler
  platform başınadır: shader varyantları grafik API'sine göre anahtarlanır (admin d3d11, oyuncu
  vulkan), asset artifact'ları sıkıştırma biçimine göre, script derleme çıktıları hedefe göre ayrı
  klasörlerde. Her platform kendi cache'ini bir kez ısıtır; **soğuk cache'te build süresinin büyük
  kısmı shader varyantı derlemektir.** Pratik sonuç: `Library/`'yi silme, ve aynı gün ikisi de
  gerekiyorsa önce admin sonra APK al.
- **Gerçek zamanlı antivirüs build süresinin görünmeyen kalemidir.** IL2CPP on binlerce
  `.cpp`/`.obj` üretir, `Library/` sürekli yazılıp okunur; Defender her dosya açılışında araya
  girip çok çekirdekli derlemenin önünde kuyruk oluşturur (%20-40 bandında fark). Yeni
  bilgisayarda bir kez `scripts\defender-exclusions.cmd` (yönetici) çalıştırılır: repo kökü,
  Unity kurulumu, Unity/Hub + paket cache'leri ve build zincirinin exe'leri dışlanır. ⚠️ Dışlanan
  klasörler taranmaz — oraya indirme yapılmaz. Ayrıntı ve Dev Drive alternatifi:
  `scripts/README.md`.
- **APK kurulumu:** `install_game.bat` → `adb install -r -g`. Betik APK'yı **sırayla kendi yanında,
  `deploy\player\` ve `Builds\player\` altında** arar, ilk bulduğunu kurar — bu yüzden repo
  kökündeki kopya da `deploy-player-apk.bat`'in APK yanına bıraktığı kopya da çalışır ve dosya
  taşımak gerekmez. **Aynı APK her gözlüğe kurulur** — rol ve sunucu adresi gömülü değildir,
  oyuncu build'i sunucuyu UDP beacon ile kendi bulur.
  Cihaz `unauthorized` görünüyorsa betik kuruluma **hiç girmez**: sebebi yazar (gözlük bu PC'nin
  RSA anahtarını kabul etmemiş — geliştirici modu açık olsa bile bu ayrı bir onaydır, kablo/sürücü
  sorunu değildir), izin isteyip `adb kill-server` + `adb start-server` ile onay penceresini
  yeniden tetikler, gözlükte izni verdin mi diye sorar ve cevabı `adb devices`'a tekrar sorarak
  **teyit eder** — yalnız teyit geçerse kurar. Hâlâ yetkisizse yetkilendirmeleri iptal etme /
  `adbkey` sıfırlama yolunu yazıp tekrar dener.
  ⚠️ Onay penceresinde **"bu bilgisayardan her zaman izin ver" işaretlenmezse** yetki yalnız o
  bağlantı için geçerlidir: adb server'ı her yeniden başladığında cihaz `unauthorized`'a döner ve
  onay yeniden istenir.
- Boot sahnesi build listesinde **index 0** olmalı; tüm arena sahneleri listede olmalı
  (`Configure All Build Elements` sahneyi listeye kendisi ekler).

**Operatör akışı (işletmede):** launcher'ı aç → (bir kez) sunucu exe'si + **mekan**, sunucu IP'si,
admin exe'si → **Sunucuyu Başlat** → **Yönetimi Başlat**. Sunucu `--venue <mekan>` ile açılır; oyun
`--server-ip` ile açılır, IP sormaz, doğrudan dashboard'a düşer. Ayrıntı: `deploy/README.md`,
`launcher/README.md`.

### 6.4 İçerik eklemek (özet — tam reçeteler `CLAUDE.md`'de)

| İstek | Yol |
|---|---|
| **Yeni arena** | Altı adım, tek düğmeli sihirbaz YOK: boş sahne → arena kutusuna kaydet (`Venues/<İşletme>/Scenes/<SahneAdı>/<SahneAdı>.unity` — klasör adı = sahne adı) → `Template Temellerini Yükle` (altyapı prefab örnekleri + boyut dosyası bağlama) → `JSON'dan DimensionMesh Üret` (mekanın ölçü maketi + kalibrasyon işaretçileri — `ArenaBoundary`'nin altına, yerel sıfırda kurulur; sırası serbest ama **atlanamaz**) → ölçü tutmuyorsa köşeleri ProBuilder ile düzeltip `DimensionMesh'i JSON'a Çevir` → environment sanatı (zemini **dünya y=0**'a, arenayı dünya orijinine kur) + bake → **`Configure All Build Elements`** (MapDefinition + katalog + mod listeleri + Build Settings + `maps.json`, tek geçişte). ⚠️ **Ölçekleme yoktur**; maket duvar üretmez — arenanın duvarları environment sanatına aittir ve fiziksel sınırla çakışmalıdır |
| **Yeni silah** | `WeaponKitBuilder` tablosuna satır ekle (istatistik + ses profili + pack modeli = köken kaydı) → `Tools > VortexArena > Weapons > Build Weapon Prefabs` → `WD_*.asset` üretir, **mevcut** `WPN_*.prefab`'ı yerinde günceller (ses + namlu alevi/dumanı + kovan kiti dahil), `WeaponCatalog`'u tazeler → gerekiyorsa `ModeDefinition.loadout` + sahneye yerleştir. **Export GEREKMEZ** (sunucuda silah tablosu yok). ⚠️ Araç **mevcut prefabların `Muzzle`/`Model` yerleşimine DOKUNMAZ**, yalnız definition bağlarını + ses/VFX/kovan kitini tazeler — VR'da elle ayarlanmış tutuş/namlu konumu tekrar çalıştırmakla bozulmaz. Paylaşılan şablon yoktur: sıfırdan farklı gövde için mevcut bir `WPN_*` prefabını kopyalayıp `Model` altındaki pack prefabını ve `definition`'ı değiştir, sonra *…(Yalnız Kataloğu Tazele)* çalıştır. ⚠️ **Ses klipleri yalnız alan BOŞSA yazılır** (elle sürüklenen klip korunsun diye): mevcut bir silahın sesini tablodan değiştiriyorsan önce `WD_*.asset`'teki klip alanlarını boşalt, yoksa değişiklik sessizce hiç inmez (Tuzaklar: "elle atanmışsa ezme"). Diğer alanlar (hasar/rpm/menzil/saçılım/kimlik) her koşuda ezilir |
| **Yeni mod** | Unity: `Assets/Modes/<Ad>/Scripts/VortexArena.Modes.<Ad>.asmdef` (refs: Core, Net, Protocol) + Sunucu: `Modes/<Ad>Mode.cs : IGameMode` → `MatchDirector` ctor'unda `Register(new <Ad>Mode())` + protokol dokümanına `modId` |
| **Hazır bir sahneyi arenaya çevirmek** | Aşağıdaki adımlar — araçları kullanmadan, elle |

**Hazır/dışarıdan gelmiş bir sahneyi ağa bağlama** (araçların ne yaptığını elle yapmak; normal yol
yukarıdaki altı adımdır):

1. Sahneyi arena kutusuna taşı: `Assets/Arenas/Venues/<İşletme>/Scenes/<Ad>/<Ad>.unity`
   (+ `Data/<Ad>.asset`; sahneye özel sanat/prefab varsa `Art/`, `Prefabs/` — mekanın tümüne aitse
   mekan kökündeki ortak klasörlere). **Klasör adı, sahne adı ve MapDefinition asset adı ÜÇÜ DE
   aynıdır**; sahne adı katalog anahtarıdır — sonradan değiştirme.
   ⚠️ Mekan klasörü dışına konan arena export'a girmez, yani sunucuda hiç görünmez.
2. Arena çerçevesini kur: sahne köküne **`VA_ArenaBoundary`** örneği (`ArenaBoundary`)
   (`head` = `CenterEyeAnchor`, `fadeRenderer`/`warningText` = rig altındaki
   `OutOfBoundsFade`/`BoundaryWarningText`; duvar Renderer'ı diye bir alan YOKTUR).
   **Ölçüyü mekanın boyut dosyasına yaz** (`Venues/<İşletme>/Data/<İşletme>_dimensions.json`) ve
   `dimensionsJson` alanına bağla — alan tam kare olsa bile dört köşeli bir `plane` halkası olarak
   girilir, ölçü için bileşende ayrı bir alan YOKTUR. Dosya bağlanmazsa muhafaza hata basıp kendini
   kapatır. Plan koordinatları bu transformun yerel XZ'sindedir; ölçüyü bir köşeden alıyorsan o köşe
   `(0,0)` olur (plan sıfırının arena origin'i olması gerekmez). Zemin bandının A/B noktalarını da
   aynı dosyanın `calibration` alanına yaz — işaretçilerin sahnedeki yeri oradan gelir. Elle konmuş
   kolon/kasa varsa üstlerine `ArenaObstacle` ekle.
3. Taban bölgeleri: iki `BaseZone` (Red/Blue, karşı kenarlarda; `Neutral` = herkese açık).
   Ölen oyuncu bunlardan birine fiziken girince canlanır — rig ASLA taşınmaz.
   ⚠️ Geometriyi **dünya orijinine** oturt: zemin dünya y=0'da, arena merkezi dünya (0,0,0)
   civarında. Arena uzayı dünya uzayıdır, yani sahneyi topluca kaydırmak/döndürmek arenadaki tüm
   oyuncuların ağ koordinatını kaydırır.
4. Ağ objeleri: `_Shared/App/Prefabs/` altındaki prefabları sahne köküne **örnek olarak** sürükle —
   `VA_CameraRig` (kamera/kumanda + etkileşim rig'i; ⚠️ gövde avatarı burada DEĞİL, kendini
   önyükler), `VA_PoseSync`
   (`PlayerPoseTracker` + `RemotePlayerSpawner`), `VA_CalibrationManager` (`ArenaCalibrator`),
   `VA_ModeHud` (`ModeHudSpawner`). **Başka bir arenadan kopyalama** (kopya prefab bağını kaybeder,
   rig/kalibrasyon düzeltmeleri o sahneye ulaşmaz). Sonra sahneye bakan referansları elle bağla:
   `VA_CalibrationManager`'ın `rigRoot`'u ile `ArenaBoundary`/`BaseZone`'un `head` alanı → sahnenin
   `CenterEyeAnchor`'ı. Boş bırakılırsa sahne sessizce çalışmaz; Unity kopuk sahneler-arası
   referansı sessizce null yapar. (`anchorA`/`anchorB` istisnadır: boş bırakılabilir, kalibratör
   işaretçileri ölçü maketinin `DimensionAnchor` küplerinden çözer.)
   ⚠️ Ölçü maketi bu yolda da zorunludur: sahnenin `anchor_a`/`anchor_b` işaretçileri onunla gelir,
   maketsiz sahne kalibre edilemez.
5–6. **`Tools > VortexArena > Build > Configure All Build Elements` → Hepsini Yapılandır** —
   `MapDefinition` (sceneName + görünen ad + desteklenen modlar) yazılır, ardından `GameCatalog.maps`,
   dolu `ModeDefinition.maps`, **Build Settings** ve `maps.json` klasör ağacına göre eşitlenir;
   sonunda sağlık raporu basar. Arena silindiyse/taşındıysa **Yalnız Senkronize Et** yeter.
7. **Arenanın çatısı/tavanı varsa** (isteğe bağlı, açık tavanlı arenalarda atlanır): çatı
   hiyerarşisinin kökünü seç → `GameObject > VortexArena > Arena Roof`. Bileşen eklenir ve
   altındaki tüm Renderer'lara `ArenaRoof` katmanı damgalanır → admin kuş bakışına geçince çatı
   kalkar, gölgesi kalır. Sonradan mesh eklersen bileşene sağ tık → *Çatı katmanını uygula*.
   **Tam not (davranış, tuzaklar, test, sorun giderme): [`Cati-Gizleme.md`](Cati-Gizleme.md).**

### 6.5 `Tools > VortexArena > Server > Export Server Config` — ne zaman?

**Harita (`MapDefinition`) SO'su ekledin/değiştirdin mi → çalıştır.** Menü, `MapDefinition`
SO'larından `Server/config/maps.json` üretir; çıktı deterministiktir (alfabetik, LF, UTF-8
BOM'suz) → git diff temiz kalır.

Dışa aktarılan tek şey **`sceneName` + `supportedModeIds`** — yani sunucunun `start_match`'te
gerçekten sorduğu iki soru. **Arena ölçüsü gitmez** — ölçü yalnız arenanın **boyut dosyasında**
yaşar (Tuzaklar, "sunucu metre bilmez"). Pratik sonuç: bir
arenanın boyutunu değiştirdiysen ama mod listesine dokunmadıysan **export'a gerek yoktur**
(çıktı zaten aynı).

**Silah için GEREKMEZ:** sunucu silah tablosu tutmaz (§3.6), hasarı istemci bildirir. Yeni silah
eklerken export çalıştırmaya gerek yoktur.

Unutursan ne olur: bilinmeyen `sceneName` → **`start_match` reddedilir** (maç başlamaz), sunucu
konsoluna tek satır sebep yazar.

---

## 7. Tuzaklar (pahalıya öğrenilmiş kurallar)

1. **`maps.json` ELLE DÜZENLENMEZ** — bir sonraki export ezer. Tek doğruluk kaynağı Unity
   SO'larıdır. (`server.json` elle, `devices.json` sunucu üretir; `weapons.json` diye bir dosya yoktur.)
2. **Meta umbrella paketi (`com.meta.xr.sdk.all`) ASLA eklenmez** — Meta Project Setup Tool önerse
   bile. Çektiği `voice` paketi Android namespace çakışmasıyla build'i kırıyor. Bireysel paketler:
   core + interaction + interaction.ovr @203.0.0, audio @85.0.0 (spatializer, pinli).
3. **Rig'i/kamerayı asla taşıma** — free-roam'da oyuncu fiziksel; canlanma durum değişimidir.
   Ayrıca **sahneye Building Blocks rig'i eklenmez, `VA_CameraRig` prefabı kullanılır**: BB kurulum
   yordamı (`CameraRigBBBlockData`) prefabı örnekledikten sonra **otomatik unpack ediyor** — yani
   her arena rig'in birbirinden habersiz donmuş bir kopyasını taşırdı ve tek bir rig düzeltmesi
   (tracking origin, el görselleri, gizleyici listesi) arena sayısı kadar elle iş doğururdu. Rig'e bakan
   sahne referansları (`ArenaBoundary.head/fadeRenderer/warningText`, `BaseZone.head`,
   `WeaponReloadGesture.head`, `ArenaCalibrator.rigRoot`) rig değiştirilirken **yeniden bağlanmalı**;
   boş kalırlarsa sahne sessizce çalışmaz hâle gelir.
   **Yapay hareket bu yüzden `VA_CameraRig`'de kapatılmıştır ve açılmaz:** Meta'nın paket prefabı
   `OVRComprehensiveInteractionRig` tam bir locomotion yığınıyla gelir — thumbstick'i okuyan
   Slide/Step/Turn yayıncıları, turner interactor'ları, teleport ve bunları uygulayan
   `FirstPersonLocomotor` + `CharacterController`. Free-roam'da bunların hiçbiri istenmez: sanal
   öteleme/dönme kalibrasyonu bayatlatır (rig fiziksel zemine hizalanmıştır), arenayı fiziksel
   alandan kaydırır ve arena-uzayı poz akışını yalanlar — oyuncu duvarın içinde görünür.
   Prefab bir PAKET içinde olduğu için düzeltilemez; `VA_CameraRig` örneği üzerinde **yedi
   GameObject kapatılarak** susturulur (`Locomotor` + sol/sağ `LocomotionControllerInteractorGroup`,
   `MicroGesturesLocomotionHandInteractorGroup`, `LocomotionOutput`). Bu bir prefab override'ıdır,
   yani **tüm arenalara tek kaynaktan yansır** ve hiçbir sahne onu geri açmaz. Pratik sonuç:
   sahneye elle BB rig'i eklemek yalnız rig'i kopyalamaz, **yapay hareketi de geri getirir**.
4. **Sahne adı = katalog anahtarı.** `load_match` string gönderir; Build Settings'teki adla
   boşluk/typo dahil birebir eşleşmeli.
5. **`_Shared` köküne asmdef'siz gevşek script koyma** (Assembly-CSharp'a düşer, kimse göremez).
6. **Serialize edilen ikincil tipler kendi dosyasında** (`Team.cs` gibi) — gömülü tip
   "referenced script missing" üretebiliyor.
7. **Protokol dosyalarına `UnityEngine` sokma** — sunucu derlemesi kırılır (bilinçli bekçi).
8. **Doğrulamayı batch'le:** derleme/build/play testini işin sonunda tek geçişte yap.
9. **Kök `.gitignore`'a sabitlenmemiş Unity deseni ekleme.** Repo üç proje tipi barındırıyor
   (Unity + `Server/` .NET + `launcher/` .NET WPF) ve her birinin kendi `.gitignore`'u var.
   Windows'ta `core.ignorecase=true` olduğu için `*.app` deseni `Server/VortexArena.Server.App/`
   klasörünü, `*.sln`/`*.csproj` de sunucunun gerçek proje dosyalarını sessizce yutar — bunlar
   `/*.app`, `/*.sln`, `/*.csproj` diye köke sabitlenmiştir. Yeni desen eklendikten sonra
   `git ls-files -c -i --exclude-standard` (izlenen ama artık ignore'lu dosyalar) **boş dönmeli**.
10. **Batch değişkenlerine kısa genel ad verme (`RC`, `CC`, `SRC` …)** — çocuk süreçlere miras
    kalıyor: `set "RC=0"` CMake'in resource compiler değişkeniyle çakışıp bir masaüstü build'ini
    kırdı; MSBuild de ortam değişkenlerini global property olarak okur (Unity → IL2CPP → MSVC
    dahil). `scripts/*.bat` içinde tüm betik-içi değişkenler `VA_` öneklidir.
11. **Öldüreceğin bir süreci `dotnet run` ile başlatma** — `dotnet run` asıl exe'yi ÇOCUK süreç
    olarak doğurur; parent öldürülünce `VortexArena.Server.App.exe` **yetim** kalır, 47821'i
    tutmaya devam eder ve PID takibinde olmadığı için öldürülemez → sonraki sunucu porta bind
    olamaz (yaşandı). Programatik başlatmada **her zaman doğrudan exe** (launcher böyle yapar); bir
    süreci PID'inden geri bulup öldüreceksen kaydı **ad doğrulamalı** tut (PID'ler geri dönüşür).
12. **Okunmayan boru süreci kilitler** — `RedirectStandardOutput/Error = true` yapıp boruyu
    okumazsan çocuk süreç, tampon dolduğunda yazma çağrısında donar (süreç canlı görünür ama
    çalışmaz). Launcher'ın başlattığı sunucu bu yüzden çıktı yönlendirmeden, **kendi konsol
    penceresinde** koşar — boru yok, log canlı okunur.
13. **Muhafazayı susturmak bileşeni KAPATMAKLA yapılmaz — `SetSpectatorMode(true)` ile yapılır.**
    Kapatılan `ArenaBoundary` alan-dışı karartmasını son yazdığı değerde dondurur **ve planı
    çözmeyi bırakır**; susturma kipi ise yalnız uyarıyı keser, bileşeni ayakta tutar. Admin
    gözlemci bu yolu kullanır: başlığı olmadığı için muhafaza mesafesi onda anlamsız veri üretir,
    ama kuş bakışı kadrajı bileşenin `HalfExtents`/`LocalCenter` değerlerini okumaya devam eder.
    (Ağ koordinatlarının sıfırı bu bileşende değil dünya orijinindedir; muhafazayı kapatmak
    koordinatları bozmaz.)
14. **Arena sahnelerinde EventSystem YOK** (yalnız Lobby'de bir tane var) — masaüstü admin oraya
    girdiğinde HUD düğmeleri sessizce ölürdü. `UiKit.EnsureEventSystem()` kalıcı bir tane kurar,
    `TakeOverEventSystem()` sahnedekini kapatır: **iki etkin EventSystem** Unity uyarısı basar ve
    girdiyi ikisi arasında böler. Ayrıca proje Input System-only → modül
    `InputSystemUIInputModule` olmalı (`StandaloneInputModule` runtime'da patlar).
15. **`VA_CameraRig`'in ÜÇ kamerası da `MainCamera` etiketli** (Left/Right/CenterEye) → `Camera.main`
    hangisini döndüreceği garanti değil ve `RemoteAvatar` ad etiketleri yanlış kameraya döner.
    Sahnede kendi kamerasını kuran her şey (admin gözlemci) rig kökünü kapatmalı ve
    **kendi `AudioListener`'ını** eklemelidir (rig kapanınca sahnede dinleyici kalmaz).
    Masaüstü XR ayarı: Standalone `Initialize XR on Startup` **AÇIK kalmalı**.
    Sebep: **editör Play modu Android sekmesini değil PC/Standalone ayarını okur** (editörün kendisi
    bir PC uygulamasıdır) → kapalıyken Quest Link ile Play'e basmak gözlüğe hiçbir şey göndermez,
    `XRSettings.enabled` false kalır. Kapatılırsa VR'ı denemenin tek yolu her seferinde APK almaktır.
    Bedeli: Windows admin build'i de açılışta XR başlatmaya çalışır (başlıksız PC'de başlatma
    sessizce düşer); admin gözlemcinin rig kökünü kapatıp kendi `AudioListener`'ını kurması bu
    yüzden şarttır. Admin dağıtımında sorun çıkarsa çözüm ayarı topluca kapatmak değil, XR'ı
    role göre kod tarafında başlatmaktır (player rolünde `InitializeLoader`).
16. **`Shader.Find` build'de null dönebilir** — hiçbir materyalin referanslamadığı shader
    (`Universal Render Pipeline/Unlit` gibi) strip edilir. Runtime'da üretilen görseller bu yüzden
    UI/TMP shader'ları üzerinden çizilir (world-space canvas + sprite), mesh + Unlit materyal ile
    değil. Admin oyuncu halkası bu yüzden `AdminPlayerMarker` prefabında bir UI `Image`'dır;
    görseli artık çalışırken üretilmez, `UI/Sprites/Ring_16.png` asset'inden gelir.
17. **Serialize edilen enum'a yeni değer SONA eklenir.** Unity enum'ları sayısal indeksle saklar:
    `Team`'e başa/ortaya bir değer eklemek sahnelerdeki tüm `BaseZone`/`Weapon`
    takımlarını kaydırır (`Neutral` bu yüzden `= 2`). Aynısı `ModeTeamMode`/`ModeScoreKind`/
    `ModeReviveAnchor`/`ModeWeaponSource` için de geçerli — hepsi `ModeDefinition`'da serialize.
18. **Boş takım takım arkadaşı DEĞİLDİR.** Dost ateşi kontrolünü düz `a.Team == b.Team` yazma:
    takımsız modda herkesin takımı `""` olduğu için `"" == ""` **tüm vuruşları reddeder** ve kimse
    kimseyi vuramaz. Tek kapı `MatchDirector.AreTeammates` (§3.6).
19. **İstemcide `if (modeId == "...")` zinciri yazma.** Modun davranışı telden gelir
    (`ModeRules` → `ModeRuntime`, §3.9). Zincir yazılırsa her yeni mod istemci kodunu değiştirir
    ve dört ayrı yerde ayrı ayrı bayatlar — `ModeRuntime` tam bu yüzden **tek** okuma noktasıdır.
20. **Yeni `IGameMode` kancası varsayılan gövdeyle eklenir** (default interface method) ve
    **tüketicisi yoksa hiç eklenmez.** Ölü kanca, her modun boş uygulamak zorunda kaldığı bir
    vergidir; varsayılan gövde sayesinde sonradan eklemek ücretsizdir.
21. **Quest'te "Soft Particles" YOK** — `Mobile_RPAsset.supportsCameraDepthTexture = false`
    (PC asset'te açık, bu yüzden editörde çalışıp cihazda çalışmaz). Parçacığın geometriyi
    kesmesini yumuşatmak için derinlik dokusu gerekmeyen iki araç kullanılır: materyalde
    **Camera Fading** (`_FADING_ON`, ekran uzayı w'sinden hesaplar) ve Collision modülünde
    **tek düzlem + `lifetimeLoss = 1`** (zemine değince öl). Ayrıca `renderScale = 1.6` +
    `MSAA 4` yüzünden darboğaz parçacık SAYISI değil **saydam overdraw**'dur: büyük yakın
    quad'ları `ParticleSystemRenderer.maxParticleSize` ile kırp.
22. **Ambiyans parçacığını arenadan çok geniş hacme yayma.** IceWorld'ün ilk kar sistemi
    12×12 m arenanın üstünde **50×50 m** kutuya 1500 parçacık saçıyordu → görünür alana
    ~%6'sı düşüyor, kalan bütçe boşa gidiyordu. Emisyon kutusu arena boyutu + ~3 m pay
    olmalı; derinlik hissi kutuyu büyütmekle değil, farklı boyut/hız/yoğunlukta
    **katmanlarla** kurulur.
23. **Beyaz parçacık beyaz sahnede görünmez.** Yumuşak gradyan sprite'lar 6 px'te arka plana
    karışır, büyütülünce renkli bulanıklığa döner. Çözüm: **opak çekirdek + ince smoothstep
    kenar** dokusu ve parçacığı arka plandan PARLAK tutmak (mermer duvar ~0.85 → parçacık
    1.0). Beyaz zeminde okunan tek şey **additive** katmandır (alpha katman kaybolur).
24. **Kar/pus katmanı kalibrasyonu iki farklı arka plana göre yapılır.** Gökyüzü (koyu) ve
    mermer duvar/zemin (parlak) zıt yönde çalışır: gökyüzünde iyi görünen alpha değeri
    duvarda kaybolur, duvarda iyi görünen değer gökyüzünde bulanık perde olur. Her ayardan
    sonra **hem yukarı hem duvara** bakan iki kare al. `Snow_G_Haze` yalnız gökyüzüne karşı
    okunur — bu bilinçli.
25. **`BaseZone`'u gizlemek bileşeni kapatmakla bitmez.** Yalnız `zone.enabled = false` yarım
    çözümdür: görsel taban şeridi (Renderer'lı doğrudan çocuk) ekranda kalır. Doğrusu ikisi
    birlikte — bileşen kapatılır + Renderer'lı doğrudan çocuklar `SetActive(false)`
    (`BaseZoneVisibility`).
    İkinci yüzü: **kapalı bir `BaseZone` canlanma için AÇIK SAYILMAZ** — `Update` koşmadığı
    için `IsPlayerInside` donar (`PlayerCombatState.EvaluateZones`).
26. **Verilen silah kavrama sistemine SOKULMAZ ve tetiği ayrı okunur.** `Weapon.IsHeld` yalnız
    `grabbable.SelectingPointsCount`'a bakarsa el anchor'ının altına örneklenen silah **hiç ateş
    edemez** (`GrantedHand` bu yüzden var). İkinci tuzak tetikte: `Player/Attack` tek bir Button
    action'dır ve `<XRController>/{PrimaryAction}` ile İKİ kumandayı da toplar → iki elde iki
    silahla tek tetiğe basmak ikisini birden ateşlerdi. Verilen silah bu yüzden kendi elinin
    tetiğini `OVRInput` ile okur. Bu yol çerçeveden seçilen silah için de aynıdır — o da
    `GrantTo` ile ele verilir (`Persistent`), yalnız reload/rezerv/ön kabza kapıları açıktır.
27. **Free-roam'da tracking origin `Stage`'dir, `FloorLevel` değil.** İkisi de aynı zemin
    seviyesini verir (`TrackingOriginModeFlags.Floor`) ama OpenXR loader'da `FloorLevel`
    **recentering'i zorla açar** (`OVRManager.cs`: `SetAllowRecentering(true)`), `Stage` kapatır.
    OVRManager'daki `AllowRecenter` alanı bunu ezmez — o yalnız OVR'ın kendi manuel recenter
    çağrısını keser. Recenter olursa origin kayar, rig'in hizalama transform'u eski kalır ve
    **arena kayar**; operatöre "sebepsiz bozuldu" gibi görünür. İkinci savunma: `ArenaCalibrator`
    `RecenteredPose` + `TrackingAcquired` olaylarında kayıtlı anchor'dan yeniden hizalar.
28. **Guardian kurulu olmadığı için sistemin zemin seviyesi güvenilmez.** İşletme başlıklarında
    alan kurulumu bilinçli olarak yapılmaz (serbest dolaşım) → "floor level" ölçülmüş değil
    tahmindir: gözlük havadayken açılırsa yanlış başlar. Bu yüzden kalibrasyon zemini de ölçer
    (§3.3) ve yakalanan nokta kumandanın **ucudur**, pivotu değil — pivot gövdenin içindedir,
    fark doğrudan dikey hataya dönüşürdü. **Eğim telafisi yoktur ve eklenmemelidir:** iki nokta
    bir düzlem tanımlamaz (roll bilinemez) ve sanal dünyayı eğmek görsel "yukarı" ile
    yerçekimini ayrıştırıp VR'da mide bulantısı yapar. Ayrıca ölçülebilecek eğim (~5–10 mm),
    operatörün kumanda tutuş farkının (±10–20 mm) altında kalır.
29. **Sunucu metre bilmez — `maps.json`'a arena ölçüsü koyulmaz.** Sebep ölçünün kullanılmaması
    değil, **ölçülemez** olması: her işletmenin alanı farklı ve çoğu kare/dikdörtgen bile değil,
    yani tek bir ölçü çifti o arenayı tarif etmez — sunucuya gönderilen sayı, doğru sandığın
    yanlış bir sayı olurdu. Arenanın gerçek sınırı zaten arenanın **boyut dosyasındaki**
    çokgendir ve yalnız istemciyi ilgilendirir (alan-dışı uyarısı,
    gözlemci kamerasının kuş bakışı kadrajı). Genel kural: **sunucuya yalnız sunucunun
    karar vermek için okuduğu alan gider** — "ileride lazım olur" diye alan taşımak, iki uçta
    sessizce sapan ikinci bir doğruluk kaynağı üretir.
30. **Bir durumu değiştiren yol sayısı kadar kapı vardır.** "Kalibresiz oyuncu canlanamaz" gibi bir
    yasak, o durumu yazan yolların hepsine konmadıkça işlevsizdir. Canlandırmanın iki yolu var
    (`MatchDirector.HandleReviveRequestAsync` = oyuncunun talebi, `HandleAdminReviveAsync` =
    operatörün komutu) ve kalibrasyon ile engel yasakları **ikisinde de** tekrarlanır; yalnız birine
    konsa tek bir operatör tuşu yasağı sessizce delerdi ve hiçbir hata üretmezdi. Bir yasağın
    yalnız bir yolda geçilmesi ancak **açıkça verilmiş bir karar** olabilir (mod kuralı
    `reviveAnchor:"none"` operatör yolunda bilerek geçilir), varsayılan değil. Kural: yasak
    yazmadan önce o durumu (`Alive`/`Hp`) yazan **tüm** çağrıları ara — zamanlayıcı, mod kancası
    ya da admin komutu fark etmez.
31. **Arayüzde "kaç px sığar" hesabı elle YAPILMAZ.** Yerleşim Layout Group değil **sabit anchor**
    ile kurulu (öngörülebilir yerleşim) — bedeli: bir satıra düğme eklemek kalanların genişliğini
    sessizce daraltır. Oyuncu satırına `KAL` eklenince düğme başına 94 px'ten ~70 px'e düşüldü ve
    `KIRMIZIYA` etiketi `KIRMIZ…` diye kırpılır oldu. Çözüm iki katmanlı: (a) düğme etiketleri
    **aşağı yönlü autosize** yapar (tavan = istenen punto, taban %70) → sığmayan etiket
    kırpılmadan önce küçülür; (b) etiketler kısaltıldı (`MAVİ`/`KIRMIZI`).
    ⚠️ Arayüz prefaba taşındıktan sonra bu ayarlar **prefabtaki TMP bileşenlerinde** durur
    (Auto Size + Min/Max) — bir satıra öge eklerken hâlâ geçerlidir, ama artık koddan değil
    inspector'dan yönetilir. → `Docs/Gelistirici/Arayuz-Tasarimi.md`
    Aynı sebeple **panel yüksekliği de elle yığılan `y`'ye bağlıdır** —
    `AdminPreferencesPanel`'e bölüm eklerken `PanelHeight` de büyütülür (taşma hata vermez,
    alt kısmı ekran dışına atar).
32. **Arayüz metninde ✓ ✗ gibi sembol kullanma** (`UiKit` sınıf dokümanı zaten söylüyor): TMP
    varsayılan fontunda glif garantisi yok, eksik glif **□** çizilir — çalışmayan ama hata da
    vermeyen bir görsel. Kalibrasyon düğmesi bu yüzden `KAL` / `KAL !` + renk kullanıyor.
    Türkçe harfler ve `·` / `—` güvenlidir (mevcut kodda kullanılıyor).
33. **`MaterialPropertyBlock` shader keyword'ü AÇAMAZ.** `RemoteAvatar`'daki kalibresiz parlaması
    `_EmissionColor` ile yazılsaydı, paylaşılan materyalde `_EMISSION` önceden açık olmadığı için
    **sessizce hiçbir şey yapmazdı** — çalışmayan ama hata da vermeyen bir görsel. Bu yüzden
    parlama `_BaseColor` nabzıyla yapılıyor (mevcut takım rengi yolunun aynısı). Emission gerekirse
    materyalde keyword'ü açmak ayrı bir adımdır; ikinci bir materyal örneği yaratmak da Quest'te
    SRP batch'ini bozar.
34. **İstemcide can havuzu tutulmaz.** "Kırılabilir obje ağa girmiyor, canını yerelde
    tutalım" yolu iki istemcide sapma üretir (herkes kendi hasarını görür) ve ikinci bir doğruluk
    kaynağı doğurur. Bu yüzden yerel `Health` bileşeni tamamen kaldırıldı: `ReportRaycastHit`
    `false` dönünce hiçbir şey olmaz (dönüş değeri yalnız sunum kararıdır), ağa bağlı olmayan
    geometri hasar almayan **dekor**dur. Hasar alması gereken her şey ağsal olur
    (`NetIdentity` → `plan/agsal-kirilabilir-objeler.md`).

35. **Ateşle-unut yayın SIRA garantisi vermez — durum yayınları tek yayıncıdan gider.**
    `lobby_state` eskiden her registry değişiminde `_ = BroadcastLobbyStateAsync()` ile
    yayınlanıyordu. Arka arkaya iki değişiklik iki eşzamanlı task açıyor, her biri kendi
    `Snapshot()`'ını farklı anda alıp `ClientConnection`'ın gönderim semaforu için yarışıyordu.
    Semafor **çerçevelerin iç içe geçmemesini** garanti eder, **yeni olanın kazanmasını etmez** →
    eski roster sonra yazılabilir ve istemcide bir sonraki değişikliğe kadar bayat kalır.
    Belirtisi çok yanıltıcıdır: *"atılan oyuncu hâlâ listede online görünüyor"* — insanın aklına
    ilk olarak paket kaybı gelir, oysa kanal TCP'dir ve hiçbir şey kaybolmamıştır. Kural: durum
    yayınları **kirli bayrak + tek yayıncı döngü** ile gider (`LobbyService.MarkRosterDirty`) ve
    mesaj **monoton bir sürüm** taşır (`lobby_state.version`); istemci eski sürümü atar.
    Yan kazanç: birleştirme — 16 oyuncu aynı anda bağlanınca 16 değil 2 yayın olur.

36. **Kalp atışı mesajı koşulsuz yayın tetiklemez.** `status` her 5 sn'de bir gelir ve eskiden
    koşulsuz `Changed(Updated)` raise ediyordu → 18 istemcide **saniyede ~65 tam roster JSON'u**,
    hiçbir şey değişmese bile. Kural: yayın yalnız roster'da **görünen** bir alan gerçekten
    değiştiğinde tetiklenir (`Fps` PlayerInfo'da taşınmadığı için tetiklemez). Kaçırılmış bir
    yayın varsa onu `status.rosterVersion` uzlaştırması kapatır — periyodik körlemesine yayın
    değil, geride kalana hedefli tek mesaj.

37. **Arena uzayının sıfırı DÜNYA orijinidir — arena geometrisi ona göre kurulur.** Sahne
    düzenlemesi göze zararsız görünür, oysa ağ koordinatlarının sıfırı sahnenin dünya sıfırıdır:
    arenayı birkaç metre kaydırmak (ya da döndürmek) arenadaki **tüm** oyuncuların ağ koordinatını
    aynı miktarda kaydırır ve hata yalnız birden çok başlık aynı sahnede buluşunca görünür. İkinci
    yüzü dikeydir: uzak avatarların kökü arena koordinatına oturur → zemin dünya y=0'da değilse
    herkes aradaki fark kadar havada (ya da zeminin içinde) durur.

38. **Arena ölçüsünün TEK temsili boyut dosyasıdır — dikdörtgen alan için kısa yol açılmaz.**
    Alan tam kare bile olsa dört köşeli bir `plane` halkası olarak yazılır. Bileşende ölçü tutan alanlar
    (yarım genişlik/derinlik + merkez) bilinçli olarak kaldırıldı ve geri eklenmez: aynı ölçünün
    iki ayrı ifadesi kaçınılmaz olarak birbirinden sapar — biri düzeltilir, öteki eski değeriyle
    kalır ve hangisinin okunduğu koda gömülü bir öncelik sırasına bağlı olurdu. Kolayına kaçıp
    "burası zaten dikdörtgen" demek de aldatıcıdır: gerçek işletme alanlarının çoğu kare değil,
    duvarları eksenlere paralel değil. Sahneye elle konan kolon/kasa ayrıca `ArenaObstacle` ile
    işaretlenir; o bileşen fizik yapmaz, yalnız uyarıyı erken tetikler.

39. **Referanslanmayan boyut dosyası build'e GİRMEZ.** `ArenaDimensions` çalışma anında okunur;
    Unity bir `TextAsset`'i yalnız bir sahne/prefab onu referansladığı için paketler. Dosyayı
    `Assets/` altına yazıp `ArenaBoundary.dimensionsJson` alanına **bağlamamak** dosyayı build'in
    dışında bırakır. Kural: JSON'u yazdıktan sonra alana bağla; bağlamadıysan o plan yok sayılmalıdır.

40. **Boyut dosyası bağlanmazsa muhafaza tümden devre dışı kalır — sessizce değil, yüksek sesle.**
    `ArenaBoundary` dosyayı çözemezse bir kez `Debug.LogError` basar ve kapanır: yaklaşma rampası,
    alan-dışı karartması ve uyarı çalışmaz. Bu bilinçli bir seçimdir — ölçüsü bilinmeyen bir arenada
    doğru bir muhafaza zaten üretilemez, kapalı başarısızlık (ör. her karede ekranı karartmak)
    işletmede oyunu tümden oynanamaz kılardı. Yani bu bir KURULUM hatasıdır ve editörde/QA'da
    yakalanmalıdır: yeni bir arena sahnesini ilk açtığında konsolu oku, guardian kapalı olduğu için
    sahada başka fren yoktur.

41. **Meta'nın CCD çözücüsü kemik dizisini UÇ→KÖK bekler, toleransı da KARE alır.**
    `IKUtilities.SolveCCDIK` effector olarak dizinin **0. elemanını** kullanır ve zincirin kökünü
    son elemanın *ebeveyninden* bulur; kök→uç verilirse çözücü sessizce ters çalışır — eli hedefe
    götürmek yerine omzu/kalçayı hedefe sürükler (diz kalçanın üstüne çıkar, ayak havada kalır).
    İkinci yüzü: döngü koşulu `sqrMagnitude > tolerance`, yani parametre **metre değil metrekare**;
    `0.01` yazmak 1 cm değil **10 cm** slop demektir. Kural: zinciri `{ uç, orta, kök }` sırala,
    toleransı karesiyle geç.

42. **Kemiklerini kodla süren avatar her kare bind pozuna dönmelidir.** Sahnedeki karakterlerde
    AnimatorController yok (animasyon klibi kullanılmıyor), yani pozu sıfırlayan hiçbir şey yok:
    `rotation = delta * rotation` gibi **birikimli** bir yazım (omurga eğimi) her karede biraz daha
    büker, avatar dakikalar içinde katlanır. İkinci kural aynı aileden: el/kafa kemiğine **konum
    yazılmaz, yalnız rotasyon** — konum yazmak kemiği ebeveyninden koparır ve aradaki mesh gerilir;
    hedef erişilemez olduğunda (kumanda pozu bileğin değil avucun ötesindedir) bu her kare olur.
    Üçüncüsü: model **sabit boyda**, oyuncu değil — avatar oyuncunun kafa yüksekliğine
    ölçeklenmezse kollar ele yetişmez, ayaklar zemine değmez. Belirti üçünde de aynı görünür:
    "uzamış/kopmuş uzuvlar".

43. **Editör AÇIKKEN `ProjectSettings/*.asset` dosyasını elle düzenleme — Unity onu okumaz, üstüne
    yazar.** Editör bu dosyaları açılışta belleğe alır; diskteki değişikliği görmez ve bir sonraki
    kaydında kendi hâlini geri yazar. Sinsi tarafı sessiz olmasıdır: dosyada `ArenaRoof` katmanı
    **yazılı dururken** editörde (ve ondan üretilen build'de) o katman yoktur ve bağlı özellik
    (çatı gizleme) kurulumunu atlar. Katman/tag/kalite ayarı
    editörden ya da `SerializedObject` ile yapılır; git'e giden dosya o yolun çıktısıdır.

44. **Poz kanalının "eski `seq`'i at" kuralı OLAY kanalına kopyalanmaz.** İkisi aynı alan adını
    taşır, kuralları taban tabana zıttır: poz bir **durum**dur (son gelen kazanır, geç kalan eski poz
    oyuncuyu geri zıplatır → atılmalı), atış/atma bir **olgu**dur (sırası bozuk gelen atış gerçekten
    olmuş bir atıştır → atmak sessizce bir tracer ve bir ses siler). Olay kanalında `seq` yalnız
    **birebir kopyayı** bastırır (UDP paket çoğaltabilir → çift ses/çift tracer) ve kayıp ölçer.
    Aşağı yönde karşılığı `serverTick` halkasıdır, aynı kuralla: eski tik'li ama görülmemiş batch
    OYNATILIR. → `Docs/ArenaNet-Protokol.md` §6.4/§6.5

45. **Serialize edilen `int` alana `[Range(min,…)]` koymak varsayılan `0`'ı SESSİZCE min'e çeker.**
    Unity'nin Range drawer'ı `IntSlider` ile çizerken değeri clamp'ler **ve asset'i dirty yapar** —
    yani Inspector'da açılan her asset kendiliğinden değişir. `netItemId` (varsayılan `0` =
    "atanmamış", §6.6) için bu, Inspector'ı açılan altı silahın hepsinin `netItemId=1` olması, yani
    altı kimliğin birbiriyle çakışması demekti. Aralık denetimi koda (`HasNetItemId`) ve editör
    bekçisine (`Tools > VortexArena > Weapons > Rebuild Net Item Catalog`) ait; `[Range]` "0 = atanmamış"
    semantiği taşıyan alanlarda kullanılmaz.

46. **Alanı yeniden adlandırırken `[FormerlySerializedAs]` yoksa değer sessizce sıfırlanır.** Unity
    alanı **isimle** saklar: `grantedHoldPosition` → `primaryGripPosition` geçişi attribute olmadan
    yapılsaydı altı `WD_*.asset`'in kavrama pozu boşalırdı ve bunu ancak VR'da "silah elde ters
    duruyor" olarak görürdük. (Alanı türetilmiş sınıftan **tabana taşımak** isim aynı kaldığı sürece
    güvenlidir — kaybettiren şey taşıma değil **yeniden adlandırma**dır.)

47. **Uzak oyuncuda çizilen eşya bir GÖRSELDİR; prefabın bileşenleri sterilize edilmeli — ve
    `enabled=false` YETMEZ.** `Awake` devre dışı bırakılmış bileşende de koşar, `AudioSource`
    `playOnAwake` ile ses çalar. Sterilize edilmemiş bir uzak silah kendi sesini çalar, fizik yapar,
    hatta raycast atar; teşhisi en zor hata sınıfıdır çünkü **yalnız başkalarının ekranında** olur —
    atıcının kendi görüntüsü kusursuzdur. Çözüm: örneği **pasif** bir kökün altında `Instantiate`
    edip (hiç `Awake` koşmaz) sonra bileşenleri toptan kaldırmak. Tipe göre liste tutmak da tuzak:
    prefaba yeni bileşen eklenince liste güncellenmeyi unutur.

48. **Uzak bir olayı GELDİĞİ ANDA oynatmak, pozu bilerek geciktirmiş olmakla çelişir.** Uzak
    avatar `INTERP_DELAY_MS` geriden çizilir; sunucu snapshot'ı ile olay batch'ini aynı tik'te
    yayınladığı için "hemen oynat" demek efekti elin **100 ms öncesindeki** yerine koymak demektir
    (kol 2 m/s ise ~20 cm). Kaymanın sinsiliği şu: görüntü kendi içinde tutarlı görünür, çünkü
    tracer o karede çizilmiş namludan çıkar — yani yanlış olan efektin YERİ değil ZAMANI, ve gözle
    "biraz geriden çıkıyor" diye okunur, hata gibi değil. Kural: interpolasyon tamponu olan her
    sistemde olayın da **kendi tik'inde** oynaması gerekir; tamponun büyüklüğü kadar bekle.
    ⚠️ Bunun tersi de tuzak: bekletmek için "geçmiş pozu örnekleyen" bir API yazmak. Gerekmiyor —
    doğru anda oynayınca zaten çizili poz doğru pozdur (§3.5b).

49. **Bir kavrama yolunu kapatmak için FİLTREYE güvenme — bileşeni kaldır.** ISDK'nın
    `_interactorFilters` kapısı `CanBeSelectedBy`'ın içinde çalışır ve aday listesi de oradan
    geçer (`InteractableRegistry.List(interactor)` her adayı `CanBeSelectedBy` ile süzer), yani
    filtre hover'ı da keser — "filtre yalnız seçimi keser" diye bir kural YOKTUR. Buna rağmen kökte
    mesafeden kavrama bırakılmaz, çünkü kapı bir **emniyet** kapısı değildir: `ItemGripSockets.Filter`
    el çözülemediğinde FAIL-OPEN'dır (editör oturumu, `InteractorControllerDecorator`'ı eksik bir
    rig) ve o durumda silah odanın öbür ucundan kavranabilir hale gelir. Filtre "kapalı" demez,
    "çoğu zaman kapalı" der. Bu yüzden bileşen filtrelenmedi, **kaldırıldı** (`WeaponKitBuilder`) —
    ⚠️ ve kaldırma **iki hattı birden** kapsar (`DistanceGrabInteractable` +
    `DistanceHandGrabInteractable`, §7 "eli göster ayarı kavrama hattını da değiştirir"): tek hat
    silinseydi yasak yapılandırmanın yarısında delinirdi. `MoveTowardsTargetProvider` de prefabdan
    çıkar — kalan tüketicisi `HandGrabInteractable` onu alan boşken çalışma anında kendisi kuruyor,
    asset'te tutmak sonraki okuyucuya "burada elle ayarlanmış bir hareket var" diye yalan söyler.

50. **Kablosuzda maliyet BAYT değil ÇERÇEVE'dir — "bant genişliği bol" ölçeklenebilirlik demek
    değildir.** Bu ürün 1 Gbps'lik bir AP'nin binde ikisini kullanıyor ama tek radyonun
    airtime'ının ~%15–20'sini tutuyor (§3.12): trafiği "az bayt, çok çerçeve" desenindedir ve
    802.11'de her minik unicast çerçeve preamble + SIFS + ACK + backoff olarak sabit bir hava
    bedeli öder. Pratik sonuçları:
    - **Bant üzerinden yapılan kapasite planı yanlış sonuç verir.** "%0,2 kullanıyoruz, 50 kat
      büyüyebiliriz" cümlesi bant için doğru, airtime için yanlıştır (paket tarafında pay ~2,5 kat).
    - ⚠️ **Donanım datasheet'i bu profili hiç ölçmez.** "1000 Mbps / AX3000" rakamı **büyük TCP
      paketleriyle** üretilir; bu yükte tıkanan şey radyo değil AP'nin **küçük paket iletim hızı
      (pps) / CPU'su**dur ve o sayı yazılmaz. Yani AP seçimi bir hız kıyaslaması değil bir
      **yönetilebilirlik** kıyaslamasıdır (OFDMA DL+UL, sabit kanal, DTIM, elle WMM/QoS
      ayarlanabiliyor mu) → seçim ölçütleri `Docs/Isletme-Kurulum.md` "Ön koşullar / Donanım".
    - ⚠️ **Sıkıştırma bu darboğaza DOKUNMAZ.** Quaternion sıkıştırma / delta snapshot bandı ~2,5×
      düşürür, **paket sayısını hiç düşürmez** — yani bugün hiçbir şey çözmez. Paketi kesen tek
      şey **kanal birleştirmek**tir (durumu zaten giden bir pakete bindirmek), baytı kısmak değil.
    - **Fan-out'u N² olan her kanal önce paket olarak patlar.** `health_update` bant olarak
      0,3 Mbps'lik hiçbir şeydir ama çatışmada sistemin en çok paket üreten kanalıdır — üstelik
      TCP olduğu için retransmit/ACK ile katlanır. Yeni bir "olay başına herkese haber ver"
      kanalı eklerken sorulacak soru "kaç bayt" değil **"tik başına kaç datagram"**dır.

51. **Pozunu KENDİN sürdüğün bir gövdede fizik motorunun "hız" tahmini anlamsızdır.** ISDK'nın
    `Grabbable._throwWhenUnselected` bayrağı (varsayılan AÇIK) bırakış anında gövdeye elin izlenen
    hızını uygular — makul bir varsayıma dayanır: eşyayı tutuş boyunca ISDK taşımıştır. Silahta bu
    varsayım YANLIŞ: kökü `Weapon.ApplyCanonicalGrip` her kare kanonik kavramadan **ışınlıyor**
    (§6.6). Işınlanan gövdeden türetilen hız fiziksel bir büyüklük değil kare farkının artığıdır ve
    bırakınca silah elden fırlıyordu. `WeaponKitBuilder` bayrağı kapatıyor; `_kinematicWhileSelected`
    ise AÇIK bırakılıyor (tutuşta yerçekimi hız biriktirmesin) → "bırakınca yere düşer, fırlamaz".
    Genel kural: **bir üçüncü parti bileşenin varsayılanı, o bileşenin senaryosunda geçerli olan bir
    varsayıma dayanıyor olabilir — pozu/durumu devraldığın her yerde o varsayımı yeniden sına.**
    ⚠️ Fırlatma gereken eşya (bomba) bu kapıdan geçmez: atılışı telde bildirilen kendi balistiğidir
    (`ArenaCombat.ReportThrow`), ISDK'nın fizik impulsu değil.

52. **Bir gövdeyi üç noktadan TÜRETMEK bir çözücü sorunu değil, bir VERİ sorunudur.** Kafa + iki
    elden gövde uyduran her çözücü dirsek, omuz ve bacak yönünü tahmin eder; tahmin ne kadar
    incelirse incelsin gerçek olmaz ve ayar alanları (bilek roll'ü, kalça ofseti, adım eşiği) er geç
    "hangi sayı hangi bozukluğu düzeltiyor" sorusuna dönüşür. Meta'nın body tracking'i aynı üç
    noktadan çalışır ama arkasında öğrenilmiş bir model + IOBT kameraları vardır — yani fark
    algoritmada değil **girdide**dir ve o girdiye uygulama erişemez (servis dışarıdan poz kabul
    etmez). Genel kural: **tahmin üreten bir bileşeni iyileştirmeden önce, tahmini gereksiz kılan
    bir veri kaynağı olup olmadığına bak.** Burada cevabı vardı: gövdeyi sahibinin cihazında çöz,
    sonucu akıt (§6.9). ⚠️ Bunun bedeli tel formatıdır ve **bu bir kısıt değildir** — protokol
    gerektiğinde değişir.

53. **Kendini düzeltmeyen bir tahmin, tek bozuk kareyi KALICI hataya çevirir.** Sabitlenen (mandallı)
    her değer için kural: **tetikleyicisi ve geri dönüş yolu ADI KONMUŞ olmalı.** Bugünkü örneği
    gövde ölçeğidir (§10.8) — ölçüm o andaki poza sabitlenir ve bir daha kendiliğinden değişmez;
    tetikleyicisi operatörün `measure_body_scale` düğmesi, geri dönüş yolu da aynı düğmedir (ve
    kalibrasyonun düşmesi ölçeği sıfırlar). ⚠️ Tetikleyicinin **zaman değil komut** olması bu
    kuralın sonucudur: hizalamadan otomatik tetiklenen bir ölçüm, oyuncu kumandayı zemine
    değdirmek için EĞİLMİŞKEN ölçmek olurdu ve o oran maçın kalanı boyunca yanlış boy demektir.
    Adı konmamış bir mandal ile "yalnız büyüyen" bir değişken aynı kapıya çıkar: er geç bir gürültü
    örneğine kilitlenir ve geri dönmez.

54. **Sıfır quaternion telde meşru görünür, Unity'de geçersizdir.** Dört sıfır bayt geçerli bir
    poz gibi okunur; bir Transform'a yazılınca "Quaternion To Matrix conversion failed" basar ve o
    kemik o kareden sonra bozuk kalır. Poz zincirinde (`RemoteAvatar` → eşya/etiket sunumu) gelen
    rotasyon normalize edilemiyorsa kimliğe düşürülür, NaN/∞ taşıyan poz ise hiç uygulanmaz.
    ⚠️ Aynı denetim **iskelet kökü için de gereklidir** (`0x07`'nin `root` alanı): o da doğrudan bir
    Transform'a yazılıyor. ⚠️ Sunucu poz İÇERİĞİNİ doğrulamaz ve doğrulamayacak (istemci-otoriter,
    §3.2) — denetim çizen tarafın işidir.

55. **Rig kökü arena zemininde durmalıdır — tracking origin `Stage` onu FİZİKSEL ZEMİN sayar.**
    Rig kökü zeminden birkaç santim yukarıdaysa kalibrasyon yapılmadan oynayan her oyuncu o kadar
    havada durur ve uzak avatarı orantısız büyük görünür (yukarıdaki boy tahmini üzerinden: 35 cm
    fark ~1.32× dev bir avatar demektir). Zemin dünya y=0'da olduğu için kural tek satırdır:
    **`VA_CameraRig`'in kökü Y=0'da durur** — "uzayda duran oyuncu" görüntüsünün kaynağı hep bu
    dikey sapmadır.

56. **Movement SDK retarget avatarı hareket eden bir kökün altına KONMAZ — çıktı dünya
    uzayındadır.** Sezgi "avatar rig'in çocuğu olsun, rig'le gelsin" der; SDK'nın sözleşmesi
    bunun tam tersidir. `SkeletonUtilities.GetPosesFromTheTracker` verilen ofseti
    `OVRCameraRig.trackingSpace.localToWorldMatrix` ile çarpıp **her kemiğe** baskılar, ardından
    `ConvertWorldToLocalPoseJob` **kök eklemi** ebeveynine göre yerelleştirmeden bırakır ve
    `ApplyPoseJob` onu `SetLocalPositionAndRotation` ile yazar. Bu projede kök eklem avatarın
    KENDİ transformudur → avatar rig'in altındayken rig transformu **iki kez** uygulanıyordu.
    Belirtisi gecikmelidir ve bu yüzden pahalıdır: rig birimken (arena origin'inde, kalibrasyon
    alınmamışken) hiçbir şey görünmez; kalibrasyon rig'e bir dönüşüm yazar yazmaz avatar
    oyuncudan tam **bir kalibrasyon ofseti** kadar uzağa oturur — arena etrafında dönmüş, zemin
    düzeltmesi kadar yükselmiş, oyuncunun hareketlerini birebir yapan "ikinci bir gövde" gibi.
    ⚠️ Yerel gövde çizilmediği için belirti **yalnız başkalarının ekranında** görünür: hata onu
    yapan kişinin başlığında hiçbir iz bırakmaz.

    **Kural bugün de bağlayıcıdır ve tasarımın kendisi oldu:** karakter — yerel de uzak da — sahne
    kökünde durur, hiçbir şeyin altına parent'lanmaz. Uzak tarafta kökü `ArenaNetCharacterBehaviour`
    yazar (`ArenaSpace.ArenaToWorld`, `LateUpdate`), yerelde ise kök zaten izleme uzayından gelir,
    yani kalibrasyon rig'i kaydırınca gövde kendiliğinden onunla gelir. ⚠️ Uzak tarafta blob'un
    KENDİ kök eklemi **kullanılmaz** (gönderenin dünya uzayındadır ve blob opaktır) — kök arena
    uzayında ayrı bir alanda taşınır, §6.9. Bu iki maddeyi birleştiren tek cümle: **retarget çıktısı
    dünya uzayındadır; onu bir uzaydan diğerine taşımanın yolu parenting değil, kökü açıkça
    yazmaktır.**

57. **"Mesajı yolla, sonra soketi kopar" mesajı YOLLAMAMAKLA aynı şey olabilir.** Atma yolu
    `kicked` JSON'unu yollayıp hemen `Abort()` ediyordu; abortif kapanış (RST) istemcinin henüz
    okumadığı çerçeveyi tamponundan silebilir. `SendAsync`'in dönmesi *işletim sistemine teslim
    edildi* demektir, *karşı taraf okudu* demek değildir. Sonuç sinsidir: atılan başlık kopuşu
    sıradan bir kesinti sanıp yeniden bağlanma backoff'unu çalıştırır — operatör panelde "atıldı"
    yazısını görür ama oyuncu sahada oynamaya devam eder. Kural: **son mesajdan sonra kapanış el
    sıkışması** (`CloseOutputAsync`, cevapsız istemci için üst sınırlı) ve niyet **kapanış
    sebebine de** yazılır (§5.4), yani JSON kaybolsa bile karşı taraf ne olduğunu bilir. İkinci
    yüzü: aynı kapanış sebebi sıradan kopmalarda kullanılmamalıdır (bayat soket değişimi,
    `OFFLINE_TIMEOUT` temizliği) — yoksa istemci kendini atılmış sanıp uygulamayı kapatır.

58. **İzleme/ağ uzayından gelen bir rotasyon humanoid kemiğe DOĞRUDAN yazılmaz — iki iskeletin
    bind ekseni farklıdır.** Telde giden el rotasyonu izleme uzayındadır
    (`OVRCameraRig.left/rightHandAnchor`, yani kumandanın pozu); karakterin el kemiği ise modelin
    kendi bind eksenindedir. `hand.rotation = handPose.rotation` bu iki sözleşmeyi eşit sayar ve
    bilekler ters çizilir. Ölçüldü: Ch15'in el kemiğinde parmaklar kemik-yerel `+Y`, avuç normali
    `+Z`; Meta'nın el iskeletinde parmaklar `∓X`, avuç normali `±Y/±Z` — aradaki fark sol elde
    ~115°, sağ elde ~128°, yani **iki el aynı ofsetle bile düzelmez** (Mixamo'nun sol/sağ el
    kemikleri parmak ekseni etrafında 180° farkla duruyor; "bilek hep ters" belirtisi buradan
    gelir). Köprü `HandGripConvention`'dır: kemik anatomisi modelden çalışma anında ölçülür, anchor
    tarafındaki anatomi tek ayar noktası olarak sabittir, düzeltme
    `anchorRotation * anchorBasis * Inverse(boneBasis)`. ⚠️ **Kafanın doğru görünmesi bir doğrulama
    değil TESADÜFTÜR:** Ch15'te Hips/Neck/Head bind ekseni kimliktir (0° sapma), yani aynı hatalı
    kalıp orada belirti vermez — ayak kemikleri ~177° sapıyor ama onlara rotasyon yazılmıyor.
    **Karakter değişirse kafa da aynı sebeple kırılır.** Meta'nın kendi body tracking'inde sorun
    çıkmamasının sebebi de aynı köprünün orada VAR olmasıdır: `CharacterRetargeter` per-joint T-poz
    ofsetlerini retarget config JSON'undan (`ThirdPartyPackages/MixamoCharacters/Ch15_nonPBR.json`)
    okur; elle yazılan bir IK'nın karşılığını kendisi kurması gerekir. ⚠️ Düzeltme yalnız **çizim**
    tarafındadır, protokol değişmez: telde ham anchor pozu kalır (§6.2 — fiziksel gerçek odur ve
    `ItemDefinition` kavramaları da anchor'a göre tanımlıdır). Yan kanıt: uzak **silah** zaten doğru
    duruyordu, çünkü `RemoteAvatar.ApplyGrip` anchor pozunu eşyanın kavrama ofsetiyle bileştiriyor —
    o yolda ofset vardı, bilek yolunda yoktu.

59. **Uzak sunum davranışı eşya prefabına BİLEŞEN eklenerek yazılmaz — `SterilizeVisual` tüm
    MonoBehaviour'ları toptan söker** (bilinçli: unutulan tek bileşen = sahada kendi sesini çalan,
    hatta `hit_report` üreten bir uzak silah). Uzak silahın geri tepmesi bu yüzden `RemoteAvatar`'ın
    kendisinde yaşıyor. Genel kural: **çizilen kopya salt görseldir; onu SÜREN kod avatarın
    tarafında durur.** ⚠️ İkinci yarısı da aynı derste: "yerelde görünen ama uzakta görünmeyen"
    her animasyon önce *telde eksik bir alan* gibi görünür — oysa girdisi zaten geliyorsa
    (olay + `itemId`) doğru cevap **alıcıda türetmektir**, protokole alan eklemek değil (§6.4).
    Silahın geri tepmesi tam olarak buydu: yön (spray + kick dahil) ilk günden telde gidiyordu,
    eksik olan tek şey onu çizilen silaha bağlayan koddu.

60. **Silah çerçevesinin görseli prefabda PASİF durur; onu AÇAN tek yer `WeaponFrame.Awake`'tir.**
    `RemoteAvatar.SterilizeVisual` kopyadaki tüm MonoBehaviour'ları siler ama **GameObject'leri
    kapatmaz** — görsel aktif başlasaydı `WeaponFrame` bileşeni silinmiş olmasına rağmen çerçeve
    uzak oyuncuların elindeki silahta ve oyuncunun kendi eline gelen klonda çizilmeye devam ederdi
    (bir sahne kaynağının süsü, tutulan silahın üstünde). Kural: "yalnız çalışan bileşenle anlamı
    olan" bir görsel prefabda kapalı doğar, onu bileşenin kendisi açar — sterilizasyon bileşeni
    aldığında süs de kendiliğinden gitmiş olur.

61. **ISDK interactor filtresi SEÇİMİ keser, HOVER'ı kesmez — mesafe testi İKİ yerde yapılır.**
    Çerçevenin 2 m'lik kavrama menzili yalnız `IGameObjectFilter`'a yazılsaydı oyuncu 5 m'den de
    mavi ışını ve vurguyu görür, nişan alır, grip'e basar ve hiçbir şey olmazdı; bu yüzden mesafe
    testi hem filtrede hem **ışın çiziminde** koşar. Yukarıdaki "yalan söyleyen affordance"
    tuzağının aynısıdır — orada çözüm bileşeni kaldırmaktı çünkü `WPN_*` **kökünde** mesafeden
    kavrama soket tasarımının zıddıdır; o yasak hâlâ geçerli ama **yalnız kök için**: çerçeve ayrı
    bir objedir ve silah zaten oradan, uzaktan alınır.

62. **Transformer'sız bir `Grabbable` hareketsiz DEĞİLDİR — en serbest hâlidir.** `Grabbable.Start`
    tek ve çift el transformer alanlarının İKİSİ de boşsa kendisi bir `GrabFreeTransformer` üretip
    bağlar ("create missing defaults"), yani çerçevedeki silah seçilir seçilmez oyuncunun eline
    doğru kaymaya başlar. Çözüm no-op bir `ITransformer` (`FrozenGrabTransformer`) yazıp
    `_oneGrabTransformer`/`_twoGrabTransformer` alanlarına **açıkça** bağlamaktır. "Her karede pozu
    geri yaz" alternatifi ISDK'nın transformer'ıyla kare kare yarışırdı — kimin sonra yazdığı
    Unity'nin çağrı sırasına kalır ve silah titrer; kıpırdamamanın tek kesin yolu kıpırdatacak
    kodu devreden çıkarmaktır.

61. **"Elle atanmışsa ezme" koruması, tabloyu SESSİZCE hiç uygulanmamış bir niyet hâline
    getirebilir.** `WeaponKitBuilder` ses kliplerini yalnız alan boşsa yazıyor
    (`SetClipArrayIfEmpty` / `SetObjectRefIfEmpty`) — gerekçesi doğru: Inspector'dan sürüklenen
    klip bir sonraki koşuda silinmesin. Ama silahlar ilk üretildiğinde alanlar iki ESKİ ortak
    klip setiyle dolmuştu, dolayısıyla tabloya sonradan yazılan silaha özgü setler
    (`SFX_<Ad>_Shot_*`) **hiçbir koşuda inmedi**: dosyalar diskte duruyor, tablo onları
    gösteriyor, asset'ler başka bir şey çalıyordu. Araç uyarı basmaz çünkü "dolu alanı atlamak"
    onun için normal davranıştır. Ders: koşullu yazan bir alan, tabloyu tek doğruluk kaynağı
    saymanı engeller — o alanları değiştirdiğinde **önce asset'te boşalt**, sonra aracı koş; ve
    aracın çıktısına değil ASSET'in kendisine bak.

62. **`Rebuild Net Item Catalog` modal dialog açar — CLI/MCP'den çalıştırılınca komut timeout
    verir.** `NetItemIdGuard` sonda `EditorUtility.DisplayDialog` gösteriyor
    (`ServerConfigExporter` ile aynı tuzak). İş **yapılır ve loglanır**, sonra ana thread dialog
    kapanana kadar kilitlenir; ardından her MCP çağrısı (konsol okuma dahil) timeout verir.
    Komutu TEKRAR DENEME — yeni bir dialog kuyruğa girer. Editörde pencereyi kapat, sonra devam
    et. Kalıcı çözümü diğer araçlardaki gibi dialog'u kaldırmaktır (`Debug.Log` yeter).

63. **Bir asset'in GUID'i değişirse asset silinmemiş olsa bile HERKES için yok olur — ve o
    GUID'i onarmak EDİTÖR KAPALIYKEN yapılır.** `.meta`'daki `guid` asset'in kimliğidir; dosya
    yerinde dursa da GUID değişince ona bakan her referans (mod `loadout`'ları, `WeaponCatalog` /
    `NetItemCatalog`, prefabın `definition` alanı, sahnedeki örnekler) boşluğa bakar — Unity
    "Missing" gösterir, `git status` ise yalnız iki satırlık masum bir `.meta` değişikliği. Teşhis
    ucuz: eski ve yeni GUID'i repoda ara; **yeni GUID'e hiçbir dosya referans vermiyorsa** değişim
    kazadır, doğru onarım eski GUID'i geri yazmaktır (önce başka bir `.meta` onu sahiplenmiyor mu
    diye bak — sahipleniyorsa gerçek bir çakışma vardır). ⚠️ **Ama `.meta`'yı Unity açıkken
    düzenleme:** editör GUID değişimini canlı yakalayıp asset'i yeniden içe aktarıyor ve prefabı
    **boş bir GameObject olarak diske geri yazıyor** (385 KB → 914 byte). Onarım sırası: Unity'yi
    kapat → GUID'i yaz → editörü aç. Aynısı GUID'e dokunan her toplu düzenleme için geçerlidir.

64. **`LoadSceneAsync` varsayılan ayarla `LoadScene`'den YAVAŞTIR — sebebi asenkronluk değil,
    `Application.backgroundLoadingPriority`.** Unity kare hızını korumak için yükleme
    entegrasyonuna kare başına yalnızca küçük bir dilim ayırır (proje varsayılanı genelde
    `BelowNormal`); senkron yükleme ise her şeyi tek karede bitirir. Yani "yükleme ekranı koyduk,
    geçiş uzadı" tanısı yanlıştır: uzatan ekran değil, önceliktir. Geçiş boyunca `High`'a çekip
    **eski değerine** geri koy (sabit bir değere değil — ayarı başka bir yer değiştirmiş olabilir).
    ⚠️ Ödünç açıktır: `High` kare süresini uzatır, yani VR'da geçiş boyunca titreme artar. Kabul
    edilebilir olmasının sebebi tam da yükleme ekranıdır — kapatılan şey zaten o karelerdir.

65. **Dünya-uzayı bir overlay'i kamera YOKKEN çizme — orijinde asılı kalır ve kimse görmez.**
    `HudFollow` paneli `Camera.main`'in önüne koyar; kamera null iken panel dünya orijininde
    (arenanın ortasında, ayakların dibinde ya da geometrinin içinde) durur. Bu, sahne geçişinde
    bir istisna değil KURALDIR: eski sahnenin kamerası aktivasyonda ölür, yenisi bir sonraki
    karede gelir. Aynı sebeple kamera **değiştiğinde** `HudFollow` yeniden başlatılmalıdır
    (`enabled = false; enabled = true`) — yoksa panel eski kameranın konumundan yenisine doğru
    süzülür ve geçişin yarısı boyunca yanlış yerde durur. Belirti sinsidir: **masaüstü
    (screen-space) varyantı sorunsuz görünür, yalnız VR'da hiç çizilmez** — "sadece adminde
    gözüküyor" tarifi neredeyse her zaman budur.

66. **Rig görselleri İSİMLE değil BİLEŞEN TİPİYLE gizlenir — GameObject adı donanım varyantına göre
    değişir, tip değişmez.** BB rig'i kumandanın üç modelini birden taşır (`Touch`, `Touch Pro`,
    `Touch Plus`) ve `OVRControllerHelper` çalışma anında bağlı olanı açar. `ControllerModelHider`
    isim deseniyle çalışırken hedefi ıskalıyordu: aradığı `questController_animrig` deseni rig'de
    24 objeyle eşleşiyor ama **hiçbiri aktif değil** (Quest 1 / Rift S varyantı), Quest 3'te açılan
    varyant ise `MetaQuestTouchPlus_Left/Right → oculus_controller_l/r_MeshX` adıyla desene hiç
    uymuyor — yani kumanda modeli sahada çiziliyordu ve bu iki kez tekrarlandı. Doğru ölçüt tiptir:
    `OVRControllerHelper` ve tip adı `HandVisual` olan her `MonoBehaviour`. ⚠️ Aynı tarama
    `SyntheticHand`, `OVRHand`, interactor'lar, retiküller ve `HandSphereMap`'e DOKUNMAZ — kavrama
    onlara bağlıdır, kapatılırsa silah hiç tutulamaz. ⚠️ Gizleme tek seferlik değil `LateUpdate`
    başınadır: Meta bırak-tut'ta bu objeleri yeniden aktifleştirir. Genel ders: SDK'nın **adı** bir
    sözleşme değildir, bileşen tipi öyledir.
67. **Sunucudaki maç durumunu sessizce sıfırlamak, istemciyi ölüm ekranında DONDURUR.**
    `ResetMatchStateLocked` `Hp`/`Alive` alanlarını yazar ama telde hiçbir şey üretmez. Tek turlu
    modlarda zararsızdı: istemci zaten `load_match` geldiğinde kendini sıfırlıyor. Tur tabanlı modda
    (§3.8.2) turlar arası `load_match` **yoktur** → mesaj gitmeyince ölü oyuncu `playing` fazına ölü
    ekranıyla girer ve ateş edemez. Kural: **oyuncunun görebileceği bir durum değişimi telde de
    görünmelidir** — canlandırma `RevivePlayerLocked` (`health_update`) ile yapılır.
68. **Otoriter bir yasak yalnız istemci kapısında durmaz.** Canlanma yasaklarının üçü de
    (kalibrasyon, `reviveAnchor:"none"`, engelin içinde olmak) iki uçta birden uygulanır: istemci
    talebi hiç göndermez, sunucu gelirse **reddeder** (§3.7). İstemci tarafı sunumdur — ölüm ekranı
    doğru metni yazsın, ağ boşuna talep tekrarlamasın; kuralı tutan sunucudur. Yalnız istemcide
    durursa bayat ya da yanlış konuşan bir istemci yasağı deler, yalnız sunucuda durursa oyuncu
    hiçbir açıklama görmeden reddedilir.
69. **Aynı değeri tekrar tekrar yazan bir bayrak, yayın tetikliyorsa fan-out'a dönüşür.**
    `PlayerRegistry.SetReady` koşulsuz `Changed` yayınlasa her çağrı bir TAM `lobby_state`
    broadcast'i olurdu; toplanma kapısı bu bayrağı yeniden kullandığı için (§3.8.2) "tabandayım"
    bildirimleri roster'ı saniyede birkaç kez herkese yollardı. Kural iki taraflıdır: registry
    **değişmediyse yayınlamaz**, istemci de yalnız **kenarda** gönderir. Soru her zaman "kaç bayt"
    değil **"kaç datagram"**dır (§3.12).
70. **Mod kancaları `await` edemez — kancadan mesaj yollamak sıra bozar.** `IGameMode.OnTick`
    `void`'dir; mod bir mesaj göndermek isterse ya kilit altında gönderim yapar (kilit sözleşmesi
    ihlali) ya da ateşle-unut bir `Task` bırakır — ikincisinde iki gönderim yarışır ve WebSocket'e
    ters sırada düşebilir. Çözüm: kanca mesajı **kilit altında bir bekleyen kutuya** yazar, tik
    döngüsü kanca dönüşünde yollar. Tek gönderici kalır, sıra korunur.
71. **Elle yazılan bir asset referansı, Unity'nin ürettiği `.meta` GUID'iyle tutmuyorsa SESSİZCE
    null olur.** Bir `.asset`/`.prefab` metin olarak (editör dışından) üretildiğinde `.meta`'yı
    Unity yazar ve **kendi rastgele GUID'ini** verir; ona elle yazılmış `guid:` referansları
    (katalog satırı, `hudPrefab` alanı) o an kırılır. Kırık referans hata basmaz — alan yalnızca
    boş görünür. En pahalı biçimi **prefab referansı**dır: prefab bulunamayınca örneklenmez, onunla
    birlikte köküne asılı bileşenler de hiç doğmaz — yani bozulan tek bir alan değil, o bileşenin
    taşıdığı bütün davranıştır ve hiçbir yerde hata görünmez.
    **Kural:** asset'i metin olarak üretiyorsan referansı yazmadan ÖNCE hedefin `.meta`'sındaki
    gerçek GUID'i oku; işin sonunda üretilen dosyalardaki her `guid:`'i projedeki `.meta`'larla
    karşılaştır (çözülmeyen varsa iş bitmemiştir). Editörde sürükleyip bırakmak bu tuzağı hiç
    doğurmaz — elle GUID yazmak yalnız editör kapalıyken bir çaredir, varsayılan yol değildir.
72. **Bir şartı "girişte bir kez" ölçmek, şartı sürdürmez.** Girişte ölçülen bir kapı, oyuncunun
    koşulu bir saniye sağlayıp bırakmasına açıktır — turnuvada bu "tabana değip kaç, turu sahanın
    ortasında karşıla" demek olurdu. Kapıyı açan koşul kararın **yürürlüğe girmesine kadar**
    ölçülmeli ve bozulursa karar geri alınabilmeli (`TryCancelCountdownForMode`). Bunun kaçınılmaz
    ikinci yarısı: kapının dayandığı bayrak (`ready`) o pencere boyunca **temizlenmez**, yoksa geri
    alma kararının dayanağı kalmaz.
73. **Elle ayarlanan bir yerleşimi editör aracı HER KOŞUDA yeniden hesaplamaz.** `Muzzle`/
    `MuzzleFlash` için zaten geçerli olan kural kovan çıkışına (`Eject`) da uygulanır:
    `WeaponKitBuilder` onu **yalnız yoksa** üretir, varsa **taşımaz**. Sebebi iki katlı:
    (a) yeri gözle ayarlanan bir şeydir, (b) hesabın kendisi güvenilmez — silah ölçüsü alt
    ağaçtaki tüm Renderer'lardan çıkarılıyordu ve **kapalı bir `Renderer`'ın `bounds`'u
    BAYATTIR** (Unity güncellemez, en son çizildiği yerdeki **dünya** kutusunu döndürür), yani
    prefabdaki kapalı çerçeve `VA_WeaponFrame` metrelerce ötede bir kutu sızdırıp kovanı silahın
    2.5 m önünde doğuruyordu. Ölçüm artık kapalı Renderer'ı ve çerçeve alt ağacını atlıyor, ama
    asıl koruma **yeniden yazmamaktır**: hesap düzelse bile elle yapılan ince ayarı geri getirmez.
    Aynı desen ses kliplerinde de var (yalnız alan boşsa yazılır). Genel kural: **bir asset'e
    hesaplanmış sayı yazan araç, o alana insan dokunmuşsa yazmaz** — sapma gözle fark edilmeden
    commit'lenir.
74. **Bir alanın "iki yazarı" varsa, yazan her yol AYNI kapıdan geçmelidir.** Kural şekli
    (`_rules`) hem modun/lobinin tanımından hem operatörün dost ateşi anahtarından besleniyor;
    ikincisini unutan tek bir atama anahtarı sessizce düşürür — harita sahnelemek ya da lobiye
    dönmek dost ateşini kapatırdı ve operatör bunu ancak sahada, yanlış vuruşta fark ederdi.
    Bu yüzden `_rules`'ü yazan tek kapı `MatchDirector.ApplyRulesLocked`'tır: taban şekli argüman
    olarak gelir, anahtar orada damgalanır. ⚠️ İşlevsel yükü yalnız `start_match` taşır (koşan maçın
    kapısını o besler); diğer çağrı yerleri (lobi profili, sahneleme) `welcome.match.rules`'un
    **doğru görünmesi** içindir — bugün onu okuyan bir tüketici olmasa da, olmadığı gün fark
    edilmeyecek bir yalan bırakırdı.
75. **Bir görsel uyarı mekanizması, süreceği malzemeyi de kısıtlıyorsa "başka bir Renderer'a
    bağlarız" diye taşınamaz.** Muhafazanın yarı saydam duvarı `_BaseColor.a` yazıyor ve alfa
    düşünce Renderer'ı kapatıyordu — ikisi de yalnız o iş için üretilmiş bir geometride masumdur.
    Environment'ın gerçek duvarlarına bağlanınca birincisi **hiç iş görmez** (opak malzemede alfa
    yazımının görsel karşılığı yok), ikincisi **duvarı yok eder** (oyuncu uzaktayken kapanır).
    Doğru hamle mekanizmayı taşımak değil, uyarıyı malzemeden bağımsız bir kanala almaktı: HMD'ye
    bağlı karartma quad'ı. ⚠️ Bunun bedeli bir kurulum kuralıdır — **environment'ın duvarları
    fiziksel sınırla çakışmalıdır**, çünkü oyuncunun gördüğü sınır artık yalnız o duvardır.
76. **Bir editör aracının yanlış seçimle çalıştırılması, çıktısı DOSYAYSA sessizce kalıcı olur.**
    `Build Arena From TestMesh` "seçili kök"ten plan çıkarıyordu; bir silah prefabı seçiliyken
    çalıştırıldığında 20 cm'lik bir "arena" yazdı ve o dosya bir mekanın boyut dosyası olarak
    aylarca durdu (`"name": "WPN_M4A4"`, kolonu `AR_B_Mag`). Hiçbir yerde hata yok: dosya geçerli
    JSON, muhafaza onu okuyup çalışıyor — yalnız yanlış. Kural: **çıktısı üzerine yazan bir araç,
    girdisini tür olarak değil ANLAM olarak doğrulamalı** (bu yüzden yeni maket aracı hedef dosyayı
    sormaz — maketin kendi işaretçisinden okur, yanlış dosyaya yazma yolu hiç açılmaz).

77. **Bir OBJE ADI, o objeyi üreten aracın imzası değildir — ada bakarak toplu silme yapılmaz.**
    `Wall_N/S/E/W` hem kaldırılan muhafaza duvarlarının hem de bir arenanın gerçek environment
    duvarlarının adı (IceWorld'de ProBuilder ile modellenmiş, köşe parçalarıyla birlikte duran
    duvarlar tam olarak bu adı taşır). Ada bakan bir temizlik arenanın kendisini siler; ada bakan
    bir "kalıntı var" uyarısı ise her açılışta yanlış alarm verir ve sağlık raporunun tamamını
    okunmaz kılar. Ayırt eden şey **bileşen izidir** (üretilen duvar: `MeshFilter` + kutu mesh'i;
    sanat duvarı: `ProBuilderMesh`). Bu yüzden `Configure All Build Elements` ada bakan bir kontrol
    TAŞIMAZ.

78. **Döndürülmüş bir ebeveynin altındaki ölçü, seçim kutusundan OKUNMAZ.** Inspector, seçim kutusu
    ve ProBuilder ölçü göstergesi — hepsi **dünya eksenine hizalı** kutuyu gösterir. Ölçü maketi
    `ArenaBoundary`'nin altında durur ve muhafaza arenayı yerleştirmek için döndürülebilir: 48,72°
    dönmüş bir kökün altında dosyada 12×12 yazan kusursuz bir kare `12 × (cos θ + sin θ) ≈ 16,93`
    okunur ve araç ölçeği bozuyor sanılır. Ölçünün okunacağı tek yer boyut dosyasıdır; maketin
    kendi yerel uzayında ölçü birebirdir ve geri okuma da maketin KENDİ kökünü referans alır, yani
    dönüş sonucu hiçbir yönde bozmaz. Genel kural: çıktısı *ölçü* olan bir araç, o ölçünün
    okunacağı çerçeveyi de söylemelidir.

79. **Bir API başarısızlığını exception ile bildirmiyorsa dönüş değerini okumak ZORUNLUDUR.**
    ProBuilder'ın `CreateShapeFromPolygon`'u üçgenleme düştüğünde `ActionResult.Failure` döner ve
    geriye **boş bir mesh** bırakır — sahnede adı doğru, geometrisi olmayan bir obje. Sessizce
    devam eden kod bunu hata saymaz; eksiklik ancak "maketimde taban yok" diye çok sonra fark
    edilir. Düşen çokgen silinir, taban düşerse üretim tümden başarısız sayılır.

80. **Aynı rolü oynayan iki obje ailesi tutma; kimliği ADLA değil BİLEŞENLE taşı.** Kalibrasyon
    işaretçisi tektir ve ölçü maketinin altındadır: `DimensionAnchor` + `AnchorKind` onu ada
    bakmadan çözülebilir kılar, ad araması yalnız maketi olmayan eski sahneler için son
    basamaktadır. İki ayrı işaretçi ailesi (biri sahnede, biri makette) aynı adı taşıdığı sürece
    hangisine hizalanıldığı sahneye bakarak anlaşılamaz — üstelik `EditorOnly` etiketi build'den
    siler ama **editör Play kipinden silmez**, yani "build'e girmiyor" demek "aramada çıkmıyor"
    demek değildir. Genel kural: ada göre obje çözen kodun kapsamı "build'e giren" değil
    **"sahnede duran"** kümesidir; iki şeyi ayırmanın yolu adı çeşitlendirmek değil türü
    işaretlemek, daha iyisi ikinci şeyi hiç var etmemektir.

81. **İzlemeden gelen "kafa" GÖZÜN pozudur; humanoid kafa KEMİĞİ oraya oturtulmaz.**
    `centerEyeAnchor` (hem yerel rig'de hem telde) gözün yeridir, kafa kemiği ise Ch15'te gözün
    ~12 cm altında ve ~9 cm gerisindedir. İkisini eşit saymak bütün iskeleti bir kafa yarısı kadar
    yukarı + öne kaydırır: yaka kemiği gözün 18-20 cm altında olması gerekirken 6-7 cm altına
    çıkar, yani gövde başın içine gömülür. ⚠️ Belirti **yalnız uzak avatarda görünür** (yerel gövde
    çizilmiyor), yani hatayı yapan kişi kendi ekranında hiçbir şey fark etmez. İkinci yüzü
    ölçektir: oyuncunun göz yüksekliğini modelin **kafa kemiği** yüksekliğine bölmek avatarı
    sistematik olarak ~%8 büyütür (büyüyen gövde = yüze daha yakın göğüs, yani aynı sorunun
    beslemesi). Kural: **ölçülen büyüklüğün model tarafındaki karşılığı aynı nokta olmalı** —
    göz/göz. ⚠️ Bugün bu eşlemeyi Movement SDK'nın retarget config'i yapıyor ve ölçek tarafını
    `SkeletonRetargeter.ApplyHeadScale` (kafayı 0.95 ile daha az büyütür) taşıyor — yani madde bir
    uygulama talimatı değil **retarget config'i hazırlarken kontrol edilecek bir ölçüttür**: ikinci
    bir başlıktan uzak avatara bak, kafa gövdenin üstünde mi yoksa içinde mi.

82. **Bilek 30 cm'den bakıldığında `skinWeights` bir görsel ayar değil, DOĞRULUK ayarıdır.**
    `QualitySettings`'te Android varsayılanı **"Mobile" seviyesidir ve `skinWeights: 2`** (PC'de 4),
    yani vertex başına yalnız iki kemik. Bilek gibi çok kemikli bir bölge bununla lineer blend
    skinning altında "şeker ambalajı" gibi çöker — büküldükçe incelip kalınlaşır. Uzaktan bakan bunu
    görmez; oyuncunun KENDİ görüşünde belirgindir. Kural: oyuncunun 30 cm'den baktığı **her** skinned
    mesh'te `SkinnedMeshRenderer.quality` Bone4'e **sabitlenir** (Auto bırakılmaz). Bu kümenin
    üyeleri rig'in **sentetik el meshleridir** (`VA_CameraRig` → `OVRHandVisualLeft/Right` altındaki
    `l_/r_handMeshNode`); ayar Meta'nın paket prefabında değil `VA_CameraRig` üstünde bir prefab
    override'ı olarak durur. Yerel gövde hiç çizilmediği için kümeye girmez.
    ⚠️ Uzak avatarlarda Auto kalır ve bu bilinçlidir: eşzamanlı oyuncu kotası yoktur, N avatarın
    hepsini Bone4 yapmak bedava değildir ve mesafeden fark edilmez.

83. **URP overlay kamerasının near-clip'i XR'da SESSİZCE yok sayılır.** "Gövdeyi ayrı bir katmana
    alıp daha büyük near-clip'li bir overlay kamerayla çiz" kalıbı masaüstünde çalışır, Quest'te
    çalışmaz: `XRLayout.AddCamera` z-aralığını **yalnız base kameradan** alır
    (`SetDisplayZRange(camera.nearClipPlane, ...)`) ve URP her overlay kamerada
    `UpdateCameraStereoMatrices(overlayCamera, xrPass)` ile projeksiyonu XR pass'inkiyle **ezer**.
    Yani overlay kameraya yazılan `nearClipPlane` hiçbir işe yaramaz. Sinsi tarafı editörde
    (XR display yokken) doğru çalışmasıdır — bileşen "kapalı duruyor, gerekirse açılır" diye
    aylarca durabilir. Genel kural: **XR'da kamera projeksiyonu senin değil, display subsystem'in
    kararıdır**; near-clip'e dayanan her çözüm ana kameranın near-clip'i üzerinden kurulur.
    Bu projede böyle bir overlay kamerası YOKTUR ve geri eklenmez (yerel gövde zaten hiç
    çizilmiyor). ⚠️ `LocalBody` katmanı `TagManager`'da duruyor (artık
    kullanılmıyor): katman silmek ProjectSettings'e dokunmaktır ve o dosyanın kendi tuzağı var.

84. **Bir prefabın TEK sürücü bileşenini silmek, prefabı bozuk değil SESSİZ bırakır — mesh çizilir,
    hiçbir şey onu sürmez.** Bir görsel bileşen (`RemoteAvatar`, `LocalBodyAvatar`) alanlarının
    yalnız bir kısmını doldurup gerisini sürücüye bırakabilir: uzak avatarda `head`/`handL`/`handR`/
    `body` alanlarının **hepsi boş** olabilir, çünkü gövdeyi kemik kemik süren ayrı bir bileşen
    vardır. O bileşen silindiğinde geriye kalan "yedek yol" hiçbir şey yapmaz (her `Apply` çağrısı
    `null` hedefe düşer), avatar dünya orijininde T-pozunda donar ve sahada **"ağ çalışmıyor, admin
    oyuncuyu görmüyor"** diye okunur — teşhis protokole/sunucuya yönelir, oysa eksik olan tek bir
    prefab bağıdır. İki kural: (1) bir bileşeni silmeden önce onu **referanslayan prefabların
    alanlarına bak** — kod derleniyor olması prefabın çalıştığı anlamına gelmez, eksik script
    derlemeyi kırmaz; (2) sürücüsü olmayan görsel yolu **sessiz bırakma**, `LogError` bas ve kökü
    yine de doğru yere taşı — yanlış pozda ama doğru yerde duran avatar teşhis edilebilir, hiç
    görünmeyen avatar edilemez.

85. **Rol ayrımı `Initialize`'da yapılıyorsa, role bağlı bileşen prefabda AÇIK gelemez —
    `OnEnable` `Instantiate` anında koşar.** "Bileşeni prefabda bırak, kurulumda rolüne göre
    aç/kapat" kalıbı sezgisel ama bir kare geç kalır: `Instantiate` dönmeden `Awake`+`OnEnable`
    çalışır, `Initialize` ise ondan sonra çağrılır. Aradaki pencerede bileşen **bir kez tam
    yetkiyle** koşmuştur. `MetaSourceDataProvider` (`OVRBody`) örneğinde bu, her uzak avatar
    doğarken bir `StartBodyTracking` çağrısıdır: HMD'siz admin'de spawn başına bir hata satırı,
    Quest'te oyuncunun kendi izlemesini uzak bir avatarın yeniden başlatması. Doğrusu tersidir —
    **bileşen prefabda KAPALI gelir, sahibi olan taraf onu açar**; "kapalı doğup açılan" yolda
    yanlış çalışan bir pencere yoktur. Aynı kural `AudioSource.playOnAwake`, `ParticleSystem`,
    fizik ve abonelik kuran her bileşen için geçerlidir (uzak eşya örneklerinin PASİF kuluçka
    kökünde kurulmasının sebebi de budur).

86. **85'in AYNASI: pasif objede `Awake` HİÇ koşmaz — kurulum çağrısı objeyi açmadan yapılmaz.**
    "Kurulana kadar gizli tut" deseni (`visualRoot.SetActive(false)`) ile "kurulumu `Initialize`
    yapar" deseni yan yana geldiğinde sessizce çakışırlar: gizlenen kök kurulacak bileşenin
    KENDİ objesiyse, o objenin `Awake`'i hiç çalışmamıştır ve `Initialize` içindeki her referans
    null'dur. Belirtisi teşhisi saptırır — `NullReferenceException` kurulum satırını gösterir,
    asıl sebep ise iki satır yukarıdaki `SetActive` sırasıdır; üstelik SDK tarafında sonuç
    "sahiplik None, karakter T-pozunda" gibi tamamen alakasız görünen ikinci bir hata üretir.
    İki kural: (1) `SetActive(true)` **önce** çağrılır — `Awake`'leri kendi çağrısı içinde senkron
    koşturur, yani o satırdan sonra obje kurulmaya hazırdır; (2) dışarıdan çağrılan kurulum
    metotları referanslarını **`Awake`'ten ayrı, idempotent** bir çözücüden alır (`Awake` koşmamış
    olabilir) — böylece sıra hatası patlamaya değil, en fazla bir kez fazladan `GetComponent`'e
    mal olur.

87. **Görünürlüğü `SetActive` ile yönetmek, altındaki bileşenlerin `OnEnable`'ını TEKRAR
    koşturur — ve bazı bileşenler başarısızlıkta kendilerini KALICI kapatır.** "Gizle/göster"
    için objeyi kapatmak masumdur sanılır, oysa her gizle/göster çevrimi tam bir yaşam döngüsü
    turudur. `OVRBody` bunun en pahalı örneği: `OnDisable` açık son örnekte
    `StopBodyTracking` çağırıyor, `OnEnable` yeniden başlatmayı deniyor ve
    **başaramazsa `enabled = false` yapıp bir daha DENEMİYOR**. Yani rig'in bir an kaybolduğu
    her harita geçişi, oyuncunun gövdesini oturumun geri kalanı boyunca sessizce öldürebilecek
    bir kumardır — geriye konsolda tek bir `[OVRBody]` satırı kalır ve o satır "gövdem neden
    yok" sorusuna bağlanmaz. Kural: **kurulmuş bir alt ağacın görünürlüğü renderer düzeyinde
    yönetilir** (`Renderer.enabled`), `SetActive` yalnız kurulumdan ÖNCE meşrudur (orada henüz
    başlatılmış bir şey yoktur). Kendini kapatabilen bir bileşene bağımlıysan durumunu kurulumdan
    sonra **oku ve bildir**; sessiz kalırsa teşhis sensöre/SDK'ya gider, oysa sebep senin
    gizleme yöntemindir.
88. **Ters derinlik testi (`ZTest Greater`) oyuncunun KENDİ silahını, elini ve gövdesini de
    "engel" sayar.** Duvar arkasından görünen bir işaret (taban şeridinin x-ray çizimi) yalnız
    arena dekorunun arkasında değil, **eldeki silahın ve elin** arkasında
    da geçerlidir: oyuncu kendi tabanının içinde durup aşağı baktığında hayalet doğrudan silahının
    üstüne çizilir ve "silahım şeffaflaştı" gibi görünür. Çözüm efekti kapatmak değil **yakın
    mesafe sönümüdür** — `M_BaseZoneXRay`'in `_NearFadeStart`/`_NearFadeEnd` alanları hayaleti
    birkaç metrenin altında tamamen söndürür; zaten o mesafede gerçek şerit görünüyordur, yani
    sönüm hiçbir bilgi kaybettirmez. Aynı sorun ileride duvar arkasından çizilecek her işaret için
    (takım arkadaşı halkası, hedef işareti) tekrar eder.

89. **Bir `.meta` guid'i değişince o asset'e yapılan TÜM referanslar sessizce ölür — ne derleyici
    ne konsol bunu tam olarak söyler.** Asset silinip yeniden import edilirse (ya da `.meta`
    kaybolursa) yeni bir guid üretilir; eski guid'e bakan her yer artık `None` görür. Tehlikesi
    kapsamıdır: tek bir silah için bu aynı anda **sahnedeki prefab örneği**, **SO alanı**
    (`Weapon.definition`), **SO dizileri** (`ModeDefinition.loadout`) ve **kataloglar**
    (`NetItemCatalog`, `WeaponCatalog`) demektir — belirtiler de o kadar dağınık çıkar: silah
    sahnede yok, uzak oyuncunun elinde çizilmiyor, atış sesi/alevi gelmiyor, alınınca hasar
    vermiyor. Hepsi tek sebepten ama hiçbiri sebebi göstermez; Unity yalnız sahnedeki prefab
    örneği için "Missing Prefab" basar, dizideki boş slot için hiçbir şey demez.
    **Tespit:** tüm `*.unity`/`*.prefab`/`*.asset` dosyalarındaki `guid:` değerlerini toplayıp
    `AssetDatabase.GUIDToAssetPath` ile çözülüyor mu diye bak — çözülemeyen her guid ölü bir
    referanstır ve bu tarama saniyeler sürer. **Onarım:** ölü guid'in taşıdığı `fileID`'ler yeni
    asset'te de duruyorsa (asset aynı, yalnız kimliği değişmiş) guid'i metinsel değiştirmek birebir
    doğrudur; tutmuyorsa referanslar Unity API'siyle tek tek bağlanır — kopuk alanı boş alandan
    ayırmak için `objectReferenceValue == null && objectReferenceInstanceIDValue != 0` bak.
    ⚠️ Editörde **açık ve kirli** bir sahnenin dosyasına diskten dokunma: kaydedilmemiş iş ezilir.

90. **Movement SDK retargeter'ı, sensör geçerli veri üretene dek karaktere HİÇBİR ŞEY uygulamaz —
    "kurulmuş olmak" ile "çizilebilir olmak" aynı şey değildir.** `CharacterRetargeter.Update`
    kaynağın `IsPoseValid()`'i false iken erkenden döner; o pencerede karakter hâlâ prefabdan
    geldiği transformdadır, yani **dünya orijininde ve T-pozunda**. Çizilen bir avatarda bunun
    bedeli görünür (haritanın ortasında duran bir kopya); çizilmeyen yerel gövdede ise **hiçbir
    belirti yoktur** ve arıza yalnız başkalarının ekranında vardır. Ölçüt her iki durumda da
    `RetargeterValid`'dir (`_isValid && IsInitialized && AppliedPose`): "kurulmuş" değil, **o kare
    bir poz gerçekten uygulanmış** demektir. ⚠️ Tanı satırının ölçütü de bununla hizalanmalıdır —
    "sağlayıcı açık mı" diye bakan bir kontrol, açık kalıp geçerli veri üretmeyen bir sensörü hiç
    uyarı basmadan görünmez bir gövdeye çevirir.

91. **Gövde ORANINI değiştirmek ağ formatını bozar; BOYU taşımanın yolu ayrı bir alandır.**
    `CharacterRetargeter.Calibrate()` gönderenin gövde oranlarını o andaki poza sabitler — ama
    iskelet blob'u `SerializationCompressionType.High` ile **eklem uzunlukları** üzerinden
    sıkışıyor, yani alıcının hedef iskeleti artık gönderenin kodladığı uzunluklarla uyuşmaz ve uzak
    avatar rastgele **bozuk duruşlara** girer. Teşhisi zorlaştıran şey, arızanın yalnız KARŞI
    tarafta görünmesidir: ölçen oyuncunun ekranında hiçbir belirti yoktur (yerel gövde zaten
    çizilmiyor). Bu yüzden o çağrı bu projede **hiç kullanılmaz**; boy farkı tek bir üniform
    çarpanla (`bodyScale`, §10.8) taşınır ve **yalnız alıcı tarafta**, karakter kökünün ölçeğine
    uygulanır — blob'a hiç dokunulmaz. Genel kural: **bir "yerel görsel ayar", serileştirilen
    yapının kendisini değiştiriyorsa yerel değildir.**
    ⚠️ Aynı ölçeğin gönderen tarafta da uygulanması ayrı bir tuzaktır: ölçüm referansı karakterin
    kendi göz hizası olduğu için, ölçeklenmiş bir gövdeden alınan ikinci ölçüm çarpanı `1`'e
    yaklaştırır ve düğme ikinci basışta sessizce bozulur. Ölçüm referansı **her zaman ölçek-1
    hâlinde** kalmalıdır.

92. **Movement SDK'nın ağa gönderdiği iskelet, kemiklerin CANLI Unity transformlarından okunur —
    `localScale` dahil.** `NetworkCharacterHandler` serileştirmeyi `GetCurrentBodyPose` ile alıyor,
    o da `SkeletonJobs.GetPoseJob` üzerinden her kemiğin `localPosition`/`localRotation`'ının yanı
    sıra **`localScale`'ini de** okuyor. Sonuç: gövde üstünde yapılan her "yalnız görsel" kemik
    hilesi aslında **telde gider**. Somut hâli: bir uzvu gizlemek için kemiği sıfıra yakın
    ölçeklemek (mesh tek `SkinnedMeshRenderer` olduğunda akla gelen ilk yol) uzak tarafta bacakları
    kalçaya, kafayı göğse ÇÖKERTİR — belirti "oyuncular havada duruyor" olur, çünkü görünen gövde
    kalçada biter. ⚠️ Teşhisi zorlaştıran şey, hatanın **kendini gizleyen** tarafta hiç
    görünmemesidir: gizleyen oyuncu o uzvu zaten görmüyordur. **Kural: gizlemeyi kemik ölçeğiyle
    YAPMA** — görünürlük `Renderer.enabled` ile yönetilir, o transformlara hiç dokunmaz ve telde tam
    gövde gider. Kemik transformuna yazmak zorunda kalınırsa yazı gönderimden
    (`NetworkCharacterHandler`, `[DefaultExecutionOrder(100)]`, `LateUpdate`) ÖNCE geri alınmalı ve
    çizimden hemen önce yeniden basılmalıdır. **Ayrım:** SDK'nın her kare kendisinin yazdığı alanlar
    (kök konumu/rotasyonu, kemik konum/rotasyonu) geri alma İSTEMEZ, kendiliğinden temizlenir;
    yazmadığı alanlar (ölçek) ister. Bir alanı hangi grupta olduğunu bilmeden değiştirme.

93. **World-space canvas'ta düzlemden sapmış bir çocuk ÇİZİLİR ama tıklanamaz — ne ışın ne fare
    ulaşır.** Grafik raycast'i canvas düzleminde kurulan bir kameradan yapılır: ISDK
    `PointableCanvasModule` her karede kendi kamerasını **işaretçinin canvas düzlemine düşen
    izdüşümünün 1 m önüne** koyup `canvas.forward`'a baktırır, masaüstünde ise `GraphicRaycaster`
    canvas'ın `worldCamera`'sına (boşsa `Camera.main`'e) düşer. İki durumda da düzlemin dışında
    kalan öge o kameranın **arkasına** düşer ve `RectTransformUtility.RectangleContainsScreenPoint`
    false döner — buton tıklanmaz. Çizim etkilenmez (UI shader'ı `Cull Off`), yani belirti "panel
    açılıyor ama hiçbir tuşu basmıyor" olur ve konsolda tek satır yoktur.
    ⚠️ **Kolayca olur ve gözle görülmez:** VR canvas'ları küçük ölçekle durur (ör. 0.0012), bu
    yüzden sahne görünümünde 1 m'lik bir kaydırma yerel uzayda **üç haneli** bir Pos Z'dir; üstelik
    RectTransform'un x/y'si anchor'dan türetilirken **z serbesttir**, yani "stretch" kurulmuş bir
    panel bile düzlemden ayrılabilir. **Kural: canvas altındaki her RectTransform'un Pos Z'si
    0'dır** — derinlik anchor'la değil, kardeş sırasıyla ayarlanır. `LobbyController` bu sapmayı
    panel ilk açıldığında ölçüp hata basar; kod konumu **düzeltmez** (sahne ile kod ikinci bir
    doğruluk kaynağı olurdu).

94. **Bir canvas'ın ışınla tıklanması ile parmakla dokunulması AYRI kurulumdur; biri diğerini
    getirmez.** ISDK'da canvas'ı işaretçilere açan şey `PointableCanvas`'tır, ama olayı ona
    hangi interactable'ın taşıyacağı ayrı bir karardır: ışın için `RayInteractable` (+ `ISurface`,
    pratikte `ColliderSurface`), dokunma için `PokeInteractable` (+ `ISurfacePatch`, pratikte
    `ClippedPlaneSurface` = `PlaneSurface` + `BoundsClipper`). İkisi de aynı `PointableCanvas`'ı
    `_pointableElement` olarak gösterir. Yalnız `RayInteractable` varken parmakla dokunmak
    **hiçbir şey yapmaz ve hata da vermez** — rig'in poke interactor'ları sahnede vurulacak bir
    `PokeInteractable` bulamaz. ⚠️ `PlaneSurface._facing` **kullanıcının geldiği tarafı**
    göstermelidir (`Backward` = transformun -Z'si); ters tarafa bakan bir düzlemde poke hiç
    tetiklenmez. `BoundsClipper._size` canvas'ın **yerel** ölçüsüdür (piksel), dünya metresi değil.

95. **Kayıt listeleri elle değil klasör taramasından EŞİTLENİR — "ekleyen" bir araç silineni
    temizleyemez.** Bir arena silindiğinde ya da taşındığında geride üç ölü kayıt kalır: Build
    Settings satırı, `GameCatalog.maps` girdisi ve onu destekleyen `ModeDefinition.maps`
    referansı. Üçü de sessizdir — katalogdaki `Missing` referans Inspector'da boş bir satır, Build
    Settings'teki ölü yol ise build'i sebebi görünmeyen bir hatayla düşürür (sahne dosyası yok ama
    liste onu istiyor). Bu yüzden `Configure All Build Elements` tek doğruluk kaynağı olarak
    **`Venues/*/Scenes/*/` klasör ağacını** alır: ağaçta olmayan her kaydı siler, ağaçta olup
    kaydı eksik olanı uyarı olarak bildirir. Pratik sonucu şudur: **arena silmek/taşımak bir
    senkronizasyon adımı ister** (*Yalnız Senkronize Et*), kayıtları elle temizlemek değil.

96. **Retarget edilen bir transforma `+=` ile ofset yazmak, ancak SDK'nın o kare pozu GERÇEKTEN
    uyguladığı doğrulanırsa güvenlidir.** "SDK kökü her kare yeniden yazıyor, ofset birikmez"
    akıl yürütmesi doğru ama KOŞULLUdur: `CharacterRetargeter.LateUpdate` pozu yalnız
    `AppliedPose` iken uygular, aksi hâlde köke hiç dokunmadan döner. Sensör bir kare bile
    geçersiz veri ürettiğinde (el/gövde örtülmesi, sağlayıcının duraklaması ve özellikle **sahne
    yüklemesi**) ofset eskisinin üstüne eklenir; kare başına birikir, yani 72 Hz'de saniyede
    metrelerce. Yönü gövdenin yaw'ından geldiği için oyuncu döndükçe kayma yön değiştirir ve
    belirti "gövde sağa sola süzülüyor" olur — kafa izleme kusursuz çalışırken de görülür, o ayrı
    bir hattır. ⚠️ Gövde avatarı `DontDestroyOnLoad` olduğu için birikim **harita değişiminde
    sıfırlanmaz ve taşınır**: hata haritaya göre değişiyormuş gibi okunur, oysa değişken haritanın
    kendisi değil o oturumda o ana dek geçen geçersiz kare sayısıdır. Kural: böyle bir ofset,
    sınıfın geri kalanıyla **aynı kapıdan** geçer (`RetargeterValid`).

97. **Kavrama ofsetleri DÜNYA metresidir; işaretçinin YEREL konumu değildir — ölçekli bir kökte
    ikisi aynı sayı değildir.** `Weapon.ApplyCanonicalGrip`, `ItemGripSockets.PrimarySocketWorld`
    ve `ItemHandGripBake` üçü de bilerek `TransformPoint`/`InverseTransformPoint`
    KULLANMAZ (ofset metre cinsindendir, eşyanın görsel ölçeğiyle büyümemeli). `WPN_*` köklerinin
    ölçeği 1 DEĞİLDİR; dolayısıyla Inspector'da okunan bir `localPosition`'ı olduğu gibi
    `WD_*.asset`'e yazmak ofseti `1/ölçek` kadar şişirir ve kavrama noktası silahın üstünde görünür
    bir noktadan kayar. Doğru yol tek: değeri **araçla** yaz (`Kavrama Pozu Stüdyosu`: prefabı prefab
    kipinde aç → elleri oluştur → Kaydet — dünya farkını kendisi alır) ve gözle doğrula: **camgöbeği
    tel küre (SO'nun dediği) tezgâhtaki elin bileğiyle ÇAKIŞMALIDIR.** Çakışmıyorsa değer elle
    yazılmış ya da Kaydet'e basılmamış demektir.
    ⚠️ Tek istisna ISDK'nın **kendi** verisidir: `HandGrabPose.RelativePose` `PoseUtils.DeltaScaled`
    ile, yani eşyanın ölçeğine **bölünerek** üretiliyor — onu geri bileştiren her taraf
    (`HandGripPoser.Apply`, `ItemGripAuthority`, `GripPoseStudio`'nun poz yazımı ve elin ilk
    yerleşimi) `PoseUtils.GlobalPoseScaled` ile ölçekli bileşim kullanır. Kural bizim tanım
    alanlarımız içindir; ISDK'nın sözleşmesini ölçeksiz okumak eli eşyadan `1/ölçek` kadar uzağa
    yapıştırır. ⚠️ Ölçekli dünya pozu alındıktan **sonraki** fark yine elle alınır: oradan sonrası
    metredir ve eşyanın görsel ölçeğiyle bir daha büyümemelidir — iki adımın karıştırılması ölçeği
    ya iki kez uygular ya hiç.

98. **Aynı transform hem yazılıyor hem okunuyorsa görsel bir telafi eklenmez — ölçünün TEK Y
    sözleşmesi olur.** Kalibrasyon işaretçisinin konumu doğrudan zemin noktasıdır; küp o noktada
    merkezlenir ve yarısı zeminin altında kalır. "Mesh'in tabanı zemine otursun" diye yerleştirmeye
    yarım yükseklik eklemek kolaydır, ama aynı obje geri okumanın da kaynağıdır: yazma tarafı
    telafiyi uygularken okuma tarafı unutursa ölçü her tur biraz kayar, üstelik belirti sahnede
    değil **dosyada** görünür. Görsel bir hizalama gerçekten isteniyorsa ölçüyü taşıyan transformda
    değil, onun ALTINDAKİ görselde yapılır.

99. **`BinaryReader.ReadBytes(n)` akış erken bittiğinde İSTİSNA ATMAZ — daha kısa bir dizi
    döndürür.** Uzunluk önekli bir alanı okuyup dönen diziyi doğrulamadan kullanmak, kırpılmış bir
    datagramı "geçerli ama yarım" veri olarak sisteme sokar. İskelet blob'unda bunun bedeli tek
    pakete kalmaz: `SkeletonUpdate.Read` yolu **sunucunun uplink okuyucusudur** ve sunucu blob'u
    açmadan tüm istemcilere relay eder, yani yarım bir kare tek oyuncuyu değil arenadaki
    **herkesi** bozuk iskeletle çizer (belirti: uzak avatarın rastgele şekillere girmesi). Kural:
    okunan uzunluk istenenden azsa girdi **boş blob** sayılır (`blobLength = 0`) ve tüketici onu
    düşürür — gövdeyi bir kare kaybetmek bozuk çizmekten iyidir. ⚠️ Değişken uzunluklu girdilerden
    oluşan bir batch'te (`SkeletonBatch.Read`) kırpılmış girdiden **sonrası okunmaz**: sonraki
    girdinin nerede başladığı bilinemez, okumayı sürdürmek akış sonunda istisna atar ve ondan önce
    okunmuş **sağlam** girdileri de düşürürdü.

100. **Alım döngüsünü saran `try`, döngünün DIŞINDAysa tek bozuk datagram kanalı tümden
    öldürür.** `while (…) { Receive(); Handle(); }` gövdesinde `Handle`'ın attığı istisna dış
    `catch`'e çıkar, döngü biter ve görev sessizce sonlanır — istemci o andan sonra hiç
    snapshot/iskelet almaz, ne hata ekranı ne yeniden bağlanma vardır, yalnızca **donmuş bir
    dünya** görür. Durum kanalında doğru davranış **tek paketi düşürmektir**: sonraki tik eksiği
    zaten kapatır. Kural: her datagramın işlenmesi **kendi `try/catch`'i** içindedir, dış `try`
    yalnız soketin kendi hatası içindir; uyarı bir kez basılır (paket başına log, bozuk bir
    gönderende saniyede 20 satır demektir).

101. **Gövde oranı kalibrasyonu YERELDİR ama sonucu TELDEN gider — bu yüzden varsayılan
    KAPALIDIR.** İskelet blob'u `SerializationCompressionType.High` ile kodlanıyor ve o kip
    eklemleri *joint lengths* üzerinden sıkıştırıyor: gönderen `CharacterRetargeter.Calibrate()`
    ile kendi gövde oranlarını değiştirdiğinde alıcının hedef iskeleti o uzunluklarla uyuşmaz ve
    uzak avatar rastgele bozuk duruşlara girer. ⚠️ Anahtarın **yerel görünümle ilgisi yoktur**
    (gövde oyuncunun kendisine hiç çizilmiyor): açmanın tek etkisi **başkalarının gördüğü**
    gövdedir, yani arıza onu açan kişinin ekranında hiç görünmez. İkinci yüzü ölçünün oynaklığıdır:
    kalibrasyon arena hizalamasından birkaç saniye sonra **o anki poza** sabitlenir — oyuncu o
    sırada yürüyor ya da eğilmişse oran yanlış kilitlenir ve oturumun kalanı boyunca öyle kalır.
    Genel kural: **yerel sanılan bir SDK ayarını açmadan önce telde ne taşındığına bak** —
    sıkıştırma kipi bir ölçüyü tele bağlıyorsa o ayar artık yerel değildir.

102. **Kaynak dosyaya karışmış tek bir NUL baytı o dosyayı TÜM aramalardan gizler.** ripgrep (ve
    onu kullanan editör/ajan arama araçları) içinde `\0` gören dosyayı ikili sayıp atlar: dosya
    eşleşse bile sonuçlarda **hiç görünmez**, "eşleşme yok" cevabı alınır. Bir tipi silip
    referanslarını süpürürken bu sessiz bir yanlış negatiftir — derleyici hatayı yakalar ama
    ondan önceki her doğrulama "temiz" der. NUL genelde bir string literalinin içine düşer ve
    editörde boşluk gibi görünür, yani gözle de ayırt edilmez. Şüphe varsa arama ikili modda
    tekrarlanır (`grep -a`) ya da doğrudan taranır:
    `find Assets Server -name "*.cs" -print0 | xargs -0 grep -lP "\x00"`. Sentinel gerekiyorsa
    `null` kullanılır — `"\0"` hem bu tuzağı kurar hem `""`'den ayırt edilmesi gereken bir şey
    anlatmaz.

103. **Sahnelenen arena LOBİ PROFİLİYLE koşar: "silah kaynağı `random` mı" sorusu "mod silah
    dağıtıyor mu" sorusunun cevabı DEĞİLDİR.** Operatör lobideyken bir arena seçtiğinde sunucu o
    arenayı sahneler — herkes arenaya geçer ama maç kurulmamıştır (`start_match` gelmedi), tür
    `lobby` kalır ve kural şekli `ModeRules.LobbyProfile`'dır: `weaponSource:"random"` +
    `fireWhilePaused`. Yani oyuncu **silah tezgâhı olan bir arenada** ama lobi kuralıyla durur.
    Kapısını yalnız kaynağa bağlayan her davranış burada yanlış tarafa düşer; somut hâli sahnedeki
    silahların gizlenmesidir — maçı bekleyen oyuncu ne silah alabilir ne serbest atış yapabilir,
    oysa lobi profilinin bütün amacı odur. **Kural: "maç kuruldu mu" sorusunu kaynaktan değil
    bileşimden sor** — `random` + `fireWhilePaused` = serbest alan, yalnız `random` = mod dağıtıyor
    (FFA). ⚠️ İkinci yüzü GEÇİŞTEDİR: sahnelemeden FFA maçına geçerken kaynak `random` kalır, yani
    "kaynak değişti mi" diye bakan bir sıfırlama hiç tetiklenmez ve serbest alanda seçilmiş durum
    sessizce maça taşınır. Kaynağı değil **türetilmiş kararı** izle. Ayrım `modeId`'den okunmaz
    (§10.5: istemcide `if (modeId == "lobby")` zinciri yazılmaz).

104. **`OVRHandVisualLeft` ile `OVRLeftHandVisual` aynı şey değildir — rig'de yalnız kelime SIRASI
    farklı iki el ailesi var.**
    `OVRHandVisualLeft`/`OVRHandVisualRight` etkileşim rig'inin doğrudan çocuğudur ve **oyuncunun
    gözlükte gördüğü el** budur; `OVRLeftHandVisual`/`OVRRightHandVisual` ise
    `…/DistanceHandGrabInteractor/Visuals/…Reticle/…Synthetic/` altındaki **mesafeli kavrama
    hayaletidir** ve gizli kalmalıdır. İkisinin de bileşen tipi aynıdır (`HandVisual`), yani tip
    onları ayıramaz; ayıran tek şey addır ve adlar bu kadar yakınken **"içerir" eşleşmesi ikisini
    birden yakalar** — o zaman gerçek eller de hayalet sayılıp kapatılır ve **oyuncu ellerini
    tümden kaybeder**. Kural: bu ailelerde eşleşme **tam ad** iledir; gerçek ele hiç dokunulmaz.

105. **Yerelde çizilmeyen bir şeyin arızası yerelde HİÇ görünmez — tek sinyal log satırıdır.**
    Yerel gövde (`LocalBodyAvatar`) yalnız ağ kaynağıdır ve tek bir pikseli bile çizilmez; oyuncunun
    gördüğü eller rig'den geliyor. Sonuç: body tracking hiç başlamasa, retargeter hiç poz
    uygulamasa ya da obje silinse bile **oyuncunun kendi ekranında her şey normal görünür** —
    bozulan tek şey BAŞKALARININ ekranıdır ve onu oyuncu kendi başına fark edemez. Kural: bu
    yoldaki her arıza `LogError`'a bağlanır ve hata metni "seni değil başkalarını etkiliyor"u
    açıkça söyler; ayrıca görünmez diye "gereksiz" sanılıp silinmemesi için obje ve prefab
    ⚠️ işaretiyle korunur.

106. **Rig'in sentetik elleri kumanda tutulurken varsayılan olarak ÇİZİLMEZ — görünürlüğü tek bir
    `OVRManager` alanı belirler.** `HandVisual` her karede `Hand.IsTrackedDataValid`'e bakar ve
    geçersizse `SkinnedMeshRenderer.enabled = false` yapar. El verisinin kaynağı `OVRHand`'dir ve
    kumanda tutulurken veri üretmesi `OVRManager.controllerDrivenHandPosesType`'a bağlıdır; alan
    `None` iken (SDK varsayılanı) el hiç çizilmez. Tuzak, arızanın yanlış yeri işaret etmesidir:
    hiyerarşide el görselleri **açık ve doğru kurulmuş** görünür, gizleyici bileşen kaldırılmış
    olsa bile ekranda el yoktur — arayan kişi kendi kodunda hata arar. Kural: değer
    `VA_CameraRig` prefabında `Natural`'dır (`ConformingToController` = elin kumanda şekline
    sarılması; `Natural` = parmakların tetik/grip girdisinden sürülmesi) ve ön koşulu
    `OVRProjectConfig.handTrackingSupport = ControllersAndHands`'tir.

107. **"Eli göster" ayarı aynı anda KAVRAMA HATTINI da değiştirir — ISDK interactor grubunu
    el izlemeye göre seçiyor.** Rig'de üç grup var ve her biri bir `ActiveStateTracker` ile açılıp
    kapanıyor: `Controller and No Hand` = `AND(Controller, NOT(Hand))` → içinde `GrabInteractor` +
    `DistanceGrabInteractor`; `Controller and Hand` = `AND(Controller, Hand)` → içinde
    `HandGrabInteractor` + `DistanceHandGrabInteractor`. `controllerDrivenHandPosesType` açılınca
    `Hand` aktifleşir, birinci grup **tümden kapanır** ve yalnız `GrabInteractable` taşıyan bir
    eşya bir daha hiç kavranamaz. Tuzak şudur: belirti **kavrama kodunda** görünür ("silahı elime
    alamıyorum") ama sebep tamamen ilgisiz bir **görsel** ayardadır; kavrama kodunda tek satır
    değişmemiştir. Kural: kavranabilir her eşya **iki hattı birden** taşır — yakın kavramada
    `GrabInteractable` + `HandGrabInteractable`, mesafeden kavramada `DistanceGrabInteractable` +
    `DistanceHandGrabInteractable`; ikisi de aynı `Grabbable`'ı besler, böylece olay yolu tektir ve
    grubu kimin seçtiği oyun koduna hiç sızmaz. Aynı sebeple bir eşyayı "kavranamaz" yapan her
    süpürme (`WeaponFrame.FreezeSource`, `WeaponGranter.DetachFromPhysicsAndGrab` /
    `PrepareSummonedClone`) dört tipi de saymak zorundadır: biri unutulursa yasak yarım kalır.

108. **El hattıyla kavranan bir nesne YERİNDE dondurulmuşsa, kilitlenen nesne değil ELDİR.**
    ISDK'nın el kavramasında `HandGrabInteractable`/`DistanceHandGrabInteractable` üzerindeki
    `Hand Alignment` varsayılan olarak `AlignOnGrab`'dır ve kavrama sürerken `HandGrabStateVisual`
    sentetik elin bileğini `SyntheticHand.LockWristPose` ile nesnenin kavrama pozuna kilitler.
    Normalde bu doğrudur (el nesneye sarılır), ama nesne `FrozenGrabTransformer` ile yerinde
    duruyorsa el nesneye gider: oyuncu, elini metrelerce ötedeki sahne nesnesinde görür. Belirti
    "kavrama noktası yanlış yerde" diye okunur, oysa kavrama noktası doğru — kilitlenen taraf
    yanlıştır. Kumanda hattında (`GrabInteractable`) karşılığı yoktur, çünkü orada kilitlenecek bir
    sentetik el yoktur; yani aynı prefab kumandayla doğru, elle bozuk görünür. Kural: **bir
    interactable yalnız SEÇİM tetikleyicisiyse `Hand Alignment = None` olur** (`VA_WeaponFrame`
    böyledir). ⚠️ `WPN_*` kökündeki `Hand Alignment` ise **hiçbir şey sürmez ve ona bakarak teşhis
    yapılmaz**: ISDK bileği ancak interactable'ın poz listesi doluyken kilitler, o liste ise
    bilerek boş tutuluyor (gerekçe "yerleştirilmemiş kavrama pozu düğümü" maddesinde). Elde tutulan
    silahta bileği ve parmakları süren taraf `HandGripPoser`'dır.

109. **Bir `.meta` guid'i değiştiğinde git bunu GÖRMEYEBİLİR — dosya boyu değişmez, bu yüzden
    `git status` temiz der.** Guid sabit uzunlukta bir onaltılık dizidir: satırın değişmesi dosya
    boyunu bir bayt bile oynatmaz. Git, çalışma kopyasındaki bir dosyayı yalnız `stat` bilgisi
    (boy + mtime) indekstekinden saparsa okur; boy aynı kalmış ve mtime da korunmuşsa içeriği hiç
    karşılaştırmaz ve dosyayı **temiz** ilan eder. Sonuç, "guid değişti" tuzağının
    (`.meta` guid'i değişince tüm referanslar sessizce ölür maddesi) en sinsi biçimidir: sapma
    commit'lenemez (git değişiklik görmez), `pull` üzerine yazmaz (zaten güncel sanır) ve
    `git checkout` no-op olur — yani **arıza her senkrondan sonra geri gelir** ve yalnız o makinede
    görünür. Takım arkadaşının klonunda sahne düzgün açılır, sende dört "Missing Prefab" vardır.
    ⚠️ `git update-index --really-refresh` bunu YAKALAMAZ: o yalnız `assume-unchanged` bayrağını
    yok sayar, kararı yine `stat`'a dayandırır.
    **Tespit — tek güvenilir yol içeriği hash'lemektir**, `status`/`diff` değil:
    `git ls-files -s` ile indeksteki blob hash'lerini al, aynı yolları
    `git hash-object --stdin-paths` ile hash'le ve eşleşmeyenleri listele; `git status`'ün
    bildirmediği her fark sessiz sapmadır. **Onarım:** dosyayı diskten sil, `git checkout HEAD --`
    ile geri al (ya da içeriği doğrudan yaz) — stat sapacağı için git bir daha körleşmez.
    **Korunma:** `.meta` guid'i ELLE düzenlenmez. Bir referans kopuksa düzeltilecek olan guid'i
    taşıyan `.meta` değil, **referansın kendisidir**; guid'i "tutsun diye" geri yazmak arızayı
    taşımaktan başka bir şey yapmaz ve karşı tarafta aynı anda ikinci bir kopuk referans üretir.

110. **Unpack edilmiş bir kopya, kit değişikliklerinin ULAŞMADIĞI ikinci bir kaynaktır; arızası
    aylar sonra ve tamamen ilgisiz bir yerde çıkar.** `WPN_*` / `VA_WeaponFrame` prefab örneği
    yerine kopya konursa (tipik yeri bir `WeaponCanvas`'ın içidir) `Build Weapon Prefabs`'ın
    sonradan eklediği hiçbir bileşen o kopyaya inmez. Kopya konduğu gün doğru çalıştığı için ortada
    hata yoktur; kırılma **kite yeni bir bileşen eklendiğinde** olur ve belirtisi "şu üç sahnede
    silah alınamıyor" diye okunur — yani teşhis, değişikliğin yapıldığı yerden çok uzakta aranır.
    Somut biçimi: el hattı (`HandGrabInteractable` + `DistanceHandGrabInteractable`) kite
    eklendiğinde kopyalar yalnız kumanda hattıyla kalır ve `controllerDrivenHandPosesType` açıkken
    o silahlar hiç kavranamaz (§7 "eli göster ayarı kavrama hattını da değiştirir"). Kural: sahnede
    ya da prefabta silah **her zaman `WPN_*` prefab ÖRNEĞİDİR**; `VA_WeaponCanvas`'ın içi de dahil.
    Denetimi tek grep'tir: `Weapon` bileşenini **doğrudan** serialize eden dosya kümesi yalnız
    `_Shared/Arsenal/Prefabs/WPN_*.prefab` olmalı — listeye başka bir `.unity`/`.prefab` giriyorsa
    orada unpack edilmiş bir kopya vardır.

111. **Kumanda anchor'ı oyuncunun ELİ DEĞİLDİR — kavramayı ona göre hizalamak matematiksel olarak
    doğru, gözle yanlış bir sonuç verir.** `OVRCameraRig.leftHandAnchor`/`rightHandAnchor`
    kumandanın pozudur; oyuncunun gözlükte gördüğü şey ise ISDK'nın sentetik elidir ve arada
    birkaç santimlik sabit bir fark vardır. Kanonik kavrama anchor'a hizalarken kavrama noktası
    her karede anchor'ın **tam üstünde** duruyordu (bu kısım kusursuz çalışıyor), ama sahada belirti
    "silah elin içinden geçiyor / avuçtan kopuk duruyor" oluyordu — yani hatanın kaynağı formül değil
    **referans noktasıydı**, bu yüzden kavrama pozunu tekrar tekrar elle ayarlamak da düzeltmiyordu.
    Kural: anchor'a doğrudan bakan hiçbir kavrama tüketicisi yazılmaz, hepsi `HandGripPivot`'tan
    geçer — ve **uzak uç dahil**: telde giden el pozu anchor pozudur (§6.6), ofset bir uçta uygulanıp
    ötekinde unutulursa aynı silah iki ekranda iki ayrı duruşta çizilir ve sapma yalnız karşı
    taraftan görülür.

112. **İki elli tutuşta silahı ikinci ele doğru KAYDIRMAK, ana elin silahı bıraktığı izlenimi
    verir.** Ön kabzayı tutan el eşyanın **yönelimini** çeker, konumunu değil: ana kavrama noktası
    her karede ana avucun tam üstünde kalmalıdır (`ItemGripSolver` konumu dönüşten SONRA ve ters
    yönde kurar). İki elin ortasına yerleştirmek ya da konumu da harmanlamak gerçekçi görünmez —
    gerçek silahta da ağırlığı tetik eli taşır. Aynı sebeple ikinci elin **dönüşü** hiç okunmaz:
    roll ana elden gelir, yoksa iki bilek birbirine karşı burulduğunda silah kendi ekseninde
    savrulur.

113. **Bir kaydı silmek roster yayını TETİKLER — "temizliği maç sonunda yap" refleksi, tam da
    korumak istediğin tabloyu boşaltır.** Ayrılmış oyuncuların satırı `left` olarak yaşıyor ve maç
    sonu tablosunun kapsamı bu. `match_end` anında temizlemek doğal görünür ("maç bitti, defteri
    kapat") ama `Changed(..., Removed)` hemen yeni bir `lobby_state` yayınlar ve operatör kazanan
    ekranını satırlar kaybolmuş hâlde okur. Defter bu yüzden **`finished` fazının tamamı boyunca
    durur** ve ancak lobiye dönerken kapanır. Genel kural: **görünür bir listeden kayıt silen her
    temizlik, o listeyi son kez gönderen mesajdan SONRA koşar** — ve kilit altında değil, çünkü
    silme olay tetikliyor.

114. **Bir kapının koşulunu, o kapıyı tetikleyen eylemin KENDİSİ üretiyorsa kapı hiç açılmaz.**
    Çerçeveden silah seçmek grip'e basmayı gerektiriyor; grip'e basmak ise `WeaponGranter`'a
    hâlihazırdaki silahı ele çağırtıyor. "Elde çift elli silah varken başkası seçilemesin" kuralı
    `WeaponFrame.Filter`/`ApplySelection`'a konduğunda, seçim komutunun **ön koşulu** ile **yasağı**
    aynı karede doğuyordu: oyuncu ilk aldığı silaha ömür boyu kilitleniyordu. Belirti özellikle
    yanıltıcıdır — nişan alırken el boş olduğu için **ışın çıkar**, yani kural "çalışıyor" görünür;
    yalnız basış işlemez. Kural: bir kapı yazmadan önce "bu kapının kapanmasına sebep olan durumu,
    kapıyı açmak isteyen eylem kendisi mi yaratıyor?" diye sor. Bu vakada doğru cevap kapıyı
    taşımak değil **hiç yazmamaktı**: çift elli seçimde oyuncu başına tek klon tutulduğu için
    "ikinci silah" zaten yapısal olarak imkânsız; kuralın gerçek yeri iki elin bağımsız silah
    aldığı `RandomGrant` yoludur.

115. **Bir kapının yeri, kapının koşulunu KİMİN ürettiğine bağlıdır — "Filter'a kapı konmaz" diye
    bir kural YOKTUR.** Bir önceki madde `WeaponFrame.Filter`'daki bir kapının oyuncuyu ilk silaha
    kilitlediğini anlatıyor; oradaki kusur kapının YERİ değil, koşulunun kapıyı açmak isteyen
    eylemin kendisi tarafından üretilmesiydi. Koşul **dışarıdan** geliyorsa (kalibrasyon durumu:
    yazarı sunucu, tetikleyicisi operatör) aday listesi kapının doğru yeridir — çerçeve adaylıktan
    düşer, ışın hiç çizilmez ve oyuncu "nişan aldım, bastım, olmadı" yaşamaz. Ayrım sorusu tek
    cümledir: **bu kapının kapalı olmasına sebep olan durumu, kapıyı açmak isteyen eylem kendisi mi
    yaratıyor?** Hayırsa kapı adaylıkta, evetse kapı hiç yazılmamalıdır.

116. **Tüketilen bir ağ kaynağı iki kez okunamaz — ikinci bir görsel temsil, ikinci bir ağ
    tüketicisiyle sürülmez.** Aynı gövdeyi ikinci bir modelle çizmek gerektiğinde ilk refleks
    ikinci bir retargeter kurmaktır; oysa iskelet blob'u `RemoteSkeletonRegistry.TryTakeBlob` ile
    **yuvadan alınır** (aynı kareyi iki kez oynatmamak için) ve iki tüketici aynı kare için
    yarışır — biri o kareyi hiç görmez, gövde ikiye ayrılır. Üstüne her model kendi retarget
    config'ini gerektirir. Doğru hamle **zaten uygulanmış olan sonucu okumaktır**: ikinci model
    karakterin CANLI iskeletinden sürülür (`SkeletonPoseMirror` — takım gövdesi), ağ yoluna hiç
    dokunulmaz. ⚠️ Bunun için kas uzayına girilmez (gerekçe aşağıdaki `HumanPoseHandler`
    maddesinde); sürücü SDK'nın yazımından SONRA koşmalıdır (yüksek execution order), erken koşan
    bir sürücü bir kare bayat gövde çizer. Genel kural: bir kaynağın "okundu mu tüketildi mi" olduğunu
    ikinci tüketici yazmadan ÖNCE sor.

117. **`Quaternion.ToAngleAxis` 180°'yi AŞAN bir açı ve işareti dönmüş bir eksen döndürebilir —
    açı ölçmek için kullanılmaz.** Quaternion çift örtülüdür (`q` ile `−q` aynı dönüştür), yani
    `w` işaret değiştirdiği anda `ToAngleAxis` aynı dönüşü `θ` yerine `360−θ` ve `−eksen` olarak
    bildirir. `Mathf.Min(açı, tavan)` gibi bir kırpma o ters ekseni aynen kullandığı için sonuç,
    olması gerekenin **tam tersi yönde tavan kadar** bir dönüştür — silah bir anda savrulur.
    İki vektör arası açı **`Vector3.Angle` ile** ölçülür: her zaman `[0,180]`'dedir ve bu
    belirsizliği hiç taşımaz.
    ⚠️ **İkiz tuzak `FromToRotation`'ın kendisindedir:** dönüş ekseni `from × to`'dur ve iki
    vektör ters-paralele yaklaşırken o çarpım sıfıra gider — eksenin YÖNÜ en küçük gürültüde
    işaret değiştirir, sonuç tanımsızlaşır. Eksenin **uzunluğuna** bakan bir emniyet (`sqrMagnitude
    < ε`) bunu yakalamaz: 170°'de çarpımın boyu hâlâ `0.17`'dir, kararsız olan boyu değil yönüdür.
    ⚠️ **Doğru çözüm bir TAVAN değil bir AĞIRLIKTIR.** Tavan yalnız savrulmanın büyüklüğünü kırpar,
    savrulmanın kendisini bırakır. Bunun yerine tekillik bölgesine **ağırlığı sıfırlanmış** girilir
    (`ItemGripSolver.ReachWeight`: `120°`'ye kadar 1, `160°`'de 0, arada `SmoothStep`) — orada eksen
    gürültü olsa da uygulanan dönüşün ağırlığı `0` olduğu için hiçbir görsel etkisi kalmaz ve geçiş
    **sürekli**dir, yani bandın iki yakasında zıplama olmaz. Genel kural: bir çözümün dejenere
    bölgesi kırpılarak değil, **o bölgeye varmadan söndürülerek** güvenli hale getirilir.
118. **`HumanPoseHandler.GetHumanPose` pozu DÜNYA uzayında verir, `SetHumanPose` ise KÖKE GÖRELİ
    uygular — iki modeli "köklerini üst üste oturtarak" eşlemek ötelemeyi İKİ KEZ uygular.**
    Ölçüm: aynı model, aynı poz; kök `(0,0,0)`'dayken `bodyPosition = (0.002, 0.999, -0.017)`,
    kök `(3,0,2)`'deyken `(2.955, 0.999, 1.952)`. `bodyRotation` da aynı şekilde dünya uzayındadır
    (kök yaw 115° → `bodyRotation` yaw 115°). Sonuç: gövde arenanın metrelerce dışına düşer,
    vuruş kutuları doğru yerde kaldığı için de "ateş ediyorum ama vurmuyor" diye okunur.
    ⚠️ **Aynı Mixamo iskeletini paylaşan iki model için kas uzayına HİÇ girilmez** —
    `SkeletonPoseMirror` gibi ada göre eşleyip `localRotation` kopyalayan bir ayna hem doğrudur
    (ölçülen sapma 1–2 cm, ki o da iki modelin gerçek oran farkıdır) hem daha ucuzdur ve
    oyuncunun boyunu kendiliğinden korur. Kas uzayı yalnız **kemik adları farklı** iskeletler için
    gerekir; orada da hedef kök dünya orijininde bırakılmalı ve `bodyPosition`
    `kaynakHumanScale / hedefHumanScale` ile ölçeklenmelidir.

119. **Kalibrasyonun ölçtüğü zemin ile gövde izlemenin varsaydığı zemin AYRI iki düzlemdir; aradaki
    fark doğrudan "ayaklar zeminin altında" olarak görünür.** `AlignRig` dünyayı
    `rise = VirtualFloorY − physicalB.y` kadar dikey kaydırır ve izlenen her şey bu kaydırmayı
    birlikte yer (`trackingSpace.localToWorldMatrix`). Ama gövde iskeletinin **kökü gözlüğün kendi
    zemin tahminine** (tracking `y ≈ 0`) çakılıdır — kafa ve eller doğrudan izlendiği için doğru
    yerde kalır, ayaklar kalmaz. Sonuç tek satır: **avatarlar tam `physicalB.y` kadar gömülür**,
    yani log'a basılan `floor R m` değerinin mutlak değeri kadar (`R` pozitifse aynı miktarda
    havada dururlar). ⚠️ **Sezgi terstir:** kumandayı zeminden yukarıda tutmak yakalanan noktayı
    yükseltir, dünyayı daha da aşağı indirir ve gömülmeyi ARTIRIR.
    ⚠️ **Ofset kumandanın YEREL ekseninde uygulanmaz, dünya -Y'sinde uygulanır.** Yerel eksende
    (`TransformPoint`) ölçüm kumandanın tutuş açısına bağlanır: eğik tutulan bir kumandada nokta
    hem yatayda kayar hem dikeyde eksik düşer, üstelik iki yakalama farklı açılarda yapıldığında
    aynı zemin iki farklı yükseklikte ölçülür ve yukarıdaki kapalı döngü ayarı anlamsızlaşır.
    Oyuncudan kumandayı dik tutmasını istemek bir kurulum talimatı olarak yazılabilir ama ölçüm
    ona BAĞLI OLMAMALIDIR.
    ⚠️ `floorMismatch` bu hatayı yakalayamaz: o kontrol iki ölçümün **birbirine** uyumuna bakar,
    gerçek zemine değil — iki noktada da aynı biçimde 10 cm yukarıdan yakalanan bir ölçü çifti
    kontrolden temiz geçer. 3–10 cm aralığı ayrıca yalnız konsola yazılır, haptik karşılığı yoktur.
    Telafi tek yerdedir (`floorProbeDropMeters`, `VA_CalibrationManager` prefabında) ve **kapalı
    döngü** ayarlanır: `y_yeni = y_eski + R`. Bu yüzden alan bir donanım sabiti DEĞİLDİR; guardian
    kurulmadığı için gözlüğün zemin tahmini oturumdan oturuma kayabilir ve tek bir sabit ancak
    tahmin kararlıysa yeter. Sayı kumandanın fiziksel pivot→uç mesafesinden belirgin biçimde
    büyükse (Touch Plus'ta o mesafe ~10 cm mertebesindedir) fazlası kumanda geometrisi değil
    **zemin tahmini sapmasıdır** — okuması budur.

120. **`SyntheticHand.OverrideAllJoints` TEK BAŞINA hiçbir şey yapmaz — parmakları büken şey
    serbestlik seviyesidir.** Çağrı yalnız hedef rotasyonları saklar; eklemin serbestlik seviyesi
    `Free` kaldığı sürece ISDK izlenen parmağı aynen geçirir ve saklanan poz hiç uygulanmaz.
    Parmakların gerçekten silaha sarılması `SetFingerFreedom` ile **birlikte** yazmaya bağlıdır —
    ISDK'nın kendi `HandGrabStateVisual.UpdateFingers`'ı da bu ikiliyi hep birlikte çağırır.
    Belirtisi sessizdir: hata yok, poz atanmış, el kıpırdamıyor; yani arayan kişi pozun kendisinde
    (eklem dizisi, el yönü, referans uzayı) hata arar. Genel kural: bir SDK'da "hedefi yaz" ile
    "hedefi uygula" ayrı iki çağrıysa ikincisi unutulduğunda ortada **hata değil sessizlik** olur —
    yazma API'sinin yanında bir etkinleştirme API'si var mı diye bak.

121. **Bir araç "boş iskelet" üretiyorsa, o iskeleti okuyan taraf "üretildi" ile "dolduruldu"yu
    ayırt edebilmelidir — ayırt edemezse aracı çalıştırmak mevcut davranışı BOZAR.** Silah kiti
    kavrama pozu düğümlerini bir zamanlar her `WPN_*` altında **silahın orijininde, sıfır
    transformla** açıyordu; o hâldeki bir düğümü "poz var" sayan bir tüketici bileği kavrama
    noktasına değil silahın orijinine kilitler ve eli nötr bind duruşunda dondurur. Belirti şudur:
    kullanıcı yalnız kiti tazelemek için aracı çalıştırır ve bütün silahlarda el bir anda bozulur,
    üstelik sebep az önce çalıştırılan araçta değil çok daha eski bir kapıda aranır. İki ayrı
    savunma birden konur ve **ikisi de kalır**: (a) araç boş düğüm HİÇ açmaz — düğümleri
    yalnız stüdyonun Kaydet'i üretir, yani her düğüm doğduğu anda doludur; (b) tüketicinin girişinde bir kapı
    durur (`ItemGripPoses.IsUnplaced`) ve ölçüsünü **tüketicinin gerçekten kullandığı büyüklükten**
    alır (`RelativePose`), düğümün ham yerel transformundan değil — araya bir ara düğüm (`GripPoses`)
    girdiği için ikisi ayrışabilir ve kapı o zaman yanlış soruyu cevaplardı. (a) tek başına yetmez:
    eski prefablarda o düğümlerden hâlâ var ve elle bir düğüm açan herkes aynı tuzağı geri getirir.

122. **`#if` ile ikiye ayrılmış bir ISDK alanına ALAN ADIYLA yazılmaz — public API'si çağrılır.**
    `HandGrabPose` poz verisini iki alanda tutar (`_handPose` / `_targetHandPose`) ve hangisinin
    canlı olduğunu `ISDK_OPENXR_HAND` seçer; aynı ayrım hayalet sağlayıcı alanında da vardır
    (`_ghostProvider` / `_handGhostProvider`). `SerializedObject` ile alan adına yazan bir araç,
    dal yanlış tahmin edildiğinde **ölü alanı doldurur**: düğüm vardır, `Uses Hand Pose` işaretlidir,
    hiçbir hata basılmaz — poz yalnızca hiç okunmaz. `InjectOptionalHandPose` gibi public metotlar
    ayrımı ISDK'nın KENDİ derlemesinde çözdüğü için bu tuzağı taşımaz.
    ⚠️ **Dalı `ProjectSettings`'e bakarak tahmin etme:** define oraya değil
    `Oculus.Interaction.asmdef`'in `versionDefines` listesine yazılmıştır ve koşulu boştur
    (`"expression": ""` → her sürümde tutar). `versionDefines` **yalnız onu bildiren assembly'ye**
    uygulanır, yani define ISDK derlenirken tanımlıdır ama bizim assembly'lerimizde tanımsızdır —
    proje ayarlarında aranınca bulunamaz ve "demek ki kapalı" diye okunur.
    ⚠️ Aynı sebeple **bizim asmdef'lerimize o define KOPYALANMAZ**: paketin iç ayrıntısını ikinci
    bir yerde saklamak, paket onu değiştirdiği gün sessiz bir sapma üretir. Alan adı yerine public
    API'nin yetmediği yerlerde (ör. hangi hayalet *asset*'inin yükleneceği) dal **ölçülür**:
    `_targetHandPose` alanının değeri public `HandPose` getter'ının döndürdüğü nesneyle aynı
    referanssa OpenXR dalı derlenmiştir. Genel kural: **koşullu derlenen bir bağımlılıkta dalı
    tahmin etme — ya ona sormayan bir API kullan ya da ölç.**
123. **Bir durumu SIFIRLAYAN komut, o durumun telde görünen değerine bakarak kısa devre edilmez.**
    Kalibrasyon sıfırlama zincirinin üç halkası da (admin satırı · sunucu · istemci) bunu ayrı ayrı
    yapıyordu: arayüz satır kalibresiz görünürken komutu hiç göndermiyor, sunucu `SetCalibration`
    `false` dönünce ("değer zaten öyle") erken çıkıyor, istemci de yerelde hizalama yoksa
    `Invalidate()` çağırmıyordu. Üçü de aynı yanlış varsayımdan besleniyor: *"kalibresiz görünen
    oyuncuda sıfırlanacak bir şey yoktur."* Oysa **yarım kalmış elle kalibrasyon** (A alındı, B
    alınmadı) tam da o görünümün altında yaşar — başlık henüz `set_calibration{true}` göndermediği
    için roster'da `calibrated:false` yazar. Operatör basar, komut ya hiç gitmez ya da hiçbir şey
    yapmaz; ardından oyuncunun tek bir tuş basışı **sıfırlamadan önceki A noktasıyla** kalibrasyonu
    tamamlayabilir. Genel kural: **sıfırlama koşulsuzdur.** Bir durum makinesinin ara aşamaları
    kaçınılmaz olarak dışarıdan görünenden fazladır; "değişen bir şey yok" kısayolu yalnız
    **yayın/idempotens** için kullanılır (gereksiz `lobby_state` üretmemek gibi), komutun kendisini
    iptal etmek için değil. Sıfırlanan taraf yarım kalmış her şeyi — yakalanan noktalar, bekleyen
    girdi sayaçları, uçuşta olan asenkron geri yüklemeler — birlikte atmalıdır.
124. **`OVRCameraRig` el anchor'larını KOŞULSUZ yazar — anchor'dan okunan poz "geçerli" demek
    DEĞİLDİR.** İki anchor ailesi aynı bileşende ama farklı sözleşmelerle güncellenir: göz
    anchor'ları `OVRNodeStateProperties.GetNodeStateProperty…` **başarılı olursa** yazılır, el
    anchor'ları ise doğrudan `OVRInput.GetLocalControllerPosition(aktif kumanda)` ile yazılır —
    geçerlilik hiç sorulmadan. Kumandanın pili bitince aktif kumanda `Controller.None` olur, o
    çağrı `(0,0,0)` döner ve el anchor'ı **rig orijinine, yani oyuncunun ayağının dibine** sıçrar.
    Sıfır oradan İKİ kanalı birden zehirler: `0x01 PoseUpdate` anchor'ı doğrudan okuyup yollar,
    body tracking çözümü de o eli hedef alıp iskeleti çökertir — yani SDK'nın yazdığı karakter
    kökü de sıçrar. Sahadaki belirti "oyuncunun konumu tamamen rastgele oluyor"dur.
    ⚠️ Belirti bir **paket/ofset** sorunu gibi okunur ama değildir: `0x01` sabit 95 B'dir, alan
    kaymaz ve hiçbir deserialization hatası oluşmaz. Bozulan şey **düzen değil içeriktir**, bu
    yüzden çözüm de tel formatında değil kaynaktadır (`ControllerTracking` + kafaya göreli tutma,
    §3.5). Genel kural: **rig anchor'ından el pozu okuyan her yeni kod `ControllerTracking.IsValid`
    kapısından geçer** — "anchor bir değer döndürdü" ile "o değer ölçüldü" ayrı şeylerdir.
125. **Kumandanın pil YÜZDESİ Quest'te okunamaz — okunuyormuş gibi bir arayüz yapılmaz.**
    `OVRInput.GetControllerBatteryPercentRemaining` kullanımdan kalktı ("no longer supported in
    OpenXR") ve daima `0` döner; `ControllerState*` yapılarının `LBatteryPercentRemaining` /
    `RBatteryPercentRemaining` alanları sıfırlanır; Unity'nin OpenXR sağlayıcısı da `batteryLevel`
    usage'ını hiç yayınlamaz. Yani bir kumanda pil çubuğu sahada **daima %0** gösterirdi ve
    operatör pili dolu, çalışan bir kumandayı bitmiş sanıp oyunu durdururdu — hiç bilgi vermeyen
    bir gösterge yanlış bilgi veren bir göstergedir. Gösterilebilecek ve eyleme çevrilebilir olan
    şey **durumdur** (`ArenaProtocol.CONTROLLER_*`): kumanda bağlı mı, izleniyor mu.
    ⚠️ `PlayerInfo.battery` bununla karıştırılmaz — o **gözlüğün** pilidir, gerçekten okunur ve
    admin satırında %25 altında kırmızı, %50 altında sarı gösterilir.
126. **`Collider.ClosestPoint` non-convex bir `MeshCollider`'da GİRDİ NOKTASINI AYNEN DÖNDÜRÜR —
    yani "bu nokta içeride mi" testi orada DAİMA 'evet' der.** Belgelenmiş davranıştır ama istisna
    atmaz, log basmaz, `false` dönmez: fonksiyon başarıyla çalışmış gibi görünür. Engel ihlali
    tespiti bu teste dayandığı için tek bir non-convex collider `Obstacle` layer'ına girdiğinde
    **o sahnedeki herkes anında ölmeye başlar** ve belirti hiçbir yerde bu collider'ı göstermez.
    Bu yüzden kural iki yerden bekçilenir: çalışma anında `ObstacleVolumes` böyle bir collider'ı
    kalıcı olarak eler ve bir kez hata basar, editörde `Engel Hacimlerini Denetle` sahneyi tarar.
    ⚠️ **Konveks olmak yetmez, KONVEKS OLARAK ÇİZİLMİŞ olmak gerekir:** içbükey bir mesh'i
    `Convex` işaretlemek hatayı sessizce başka bir kılığa sokar — hull çukuru doldurur, collider
    görünen yüzeyin dışına taşar ve oyuncu **boşlukta** ceza alır. Bunu da aynı editör aracı
    ("şişkin" raporu) yakalar; gözle görülmez, çünkü çizilen mesh doğrudur.
    ⚠️ Ters yöndeki kaçış da aynı derecede sessizdir: `Physics.CheckSphere` kapalı bir concave
    mesh'in **derinindeki** nokta için `false` döner (yalnız üçgen kesişimine bakar), yani "içeriden
    yüzey mesafesi" hiçbir yöntemle ölçülemez — kürenin tamamının içeride olup olmadığı bu yüzden
    **kabuk noktalarıyla** örneklenir, merkez-yüzey mesafesiyle değil.
127. **Bir hacmin İÇİNDE olmak, ona DEĞMEKTEN farklı bir sorudur; trigger olayları ikincisini
    cevaplar.** `OnTriggerEnter`/`Stay` "temas var" der ve temas **derinlik değildir**: kafasını
    duvara değdiren oyuncu ile duvarın içinde duran oyuncu trigger için aynı olaydır. "Kafa
    içeride mi" sorusu bu yüzden temasla değil **kabuk noktalarıyla** ölçülür (merkez + ±x/±y/±z);
    trigger ile ifade edilebilen tek kural "değdi" olurdu ve o, siperin yanından geçen herkesi
    cezalandırırdı.
128. **Yerel oyuncunun gövdesine collider EKLENEMEZ — atış ışını maskesizdir.**
    `ArenaCombat.TraceShot` menzil boyunca `~0` maskesiyle ışın atıyor (uzak isabet kutuları
    Default layer'ında), yani yerel gövdeye konan **katı** bir collider oyuncunun kendi kurşununu
    kendine yedirir. Kendi gövdesiyle ilgili her ölçüm (engel ihlali, temas, yakınlık) bu yüzden
    collider'sız yöntemlerle yapılır. ⚠️ Aynı sebeple sahneye eklenen **her yeni katı collider** bir
    atış hattı kararıdır: mermi ona çarpacaktır. Sanatın collider'ından ayrı, daha geniş bir "mantık
    hacmi" koymak mermileri objelerin birkaç santim önünde durdurur — bu yüzden engel hacmi ayrı bir
    collider değil, objenin **kendi** collider'ının layer'ıdır. ⚠️ **Trigger'lar atış ışınında
    bilerek elenir** (`QueryTriggerInteraction.Ignore`): proje ayarı `Queries Hit Triggers` açık ve
    sahnedeki silahların ISDK kavrama hacimleri trigger'dır — elenmeseydi tezgâhın önünden atılan
    mermi kavrama hacmine çarpıp dururdu. Yani "trigger koyarsam mermi geçer" **atış için**
    doğrudur, diğer sorgular için değildir.
129. **`ProjectSettings/TagManager.asset` elle düzenlenmez — layer'lar Unity'nin kendi arayüzünden
    (`Edit > Project Settings > Tags and Layers`) yazılır.** Unity boş layer satırını **tire +
    BOŞLUK** (`- `) olarak serileştirir; düz `-` yazılmış bir satır okuyucuyu düşürür ve **layer
    listesinin tamamı** kaybolur — yalnız o an eklenen ad değil, dosyada zaten duran adlar da.
    ⚠️ Belirti hata değil **sessizliktir**: layer'ını adla çözen her sistem (`ArenaRoof` çatı
    gizleme, `ArenaLayers` engel ihlali) `NameToLayer`'dan `-1` alıp devre dışı kalır, ama sahnedeki
    objeler sayısal index'lerini koruduğu için hiyerarşi doğru görünmeye devam eder. ⚠️ Dosya bu
    hâldeyken editör ProjectSettings'i diske geri yazarsa adlar repodan da silinir. Aynı sebeple
    araçlar layer'ı **index'le değil adla** çözer ve bulamayınca bağırır.
130. **`Physics.Raycast`, orijini İÇİNDE olduğu collider'ı HİÇ VURMAZ — mermi engeli deler
    geçer.** Belgelenmiş davranıştır ve hata üretmez: ışın "engel yokmuş gibi" ilerler. Sahadaki
    karşılığı düpedüz hiledir — oyuncu namlusunu sandığın içine sokar ve arkasındaki oyuncuyu
    vurur; namlunun ucunu ince bir duvarın öbür yüzüne geçirmek de aynı kapıdır (orijin artık
    duvarın ötesindedir). Düz bir raycast bunu **hiçbir maske ya da menzil ayarıyla** yakalayamaz:
    tek çözüm **orijini ayrıca sınamaktır** (nokta engelin içinde mi + namlu gövdesi engelin
    içinden geçiyor mu). Bu yüzden testin tek adresi `ArenaCombat.IsMuzzleBlocked`'tır: tetikli
    silahlarda tetiği tümden öldürür, `TraceShot` içinde ise tetiği olmayan hasar kaynakları için
    ikinci savunma hattı olarak durur — kendi `Physics.Raycast`'ini yazan her yeni hasar kaynağı
    kuralı kaybeder.
131. **Quest'te bacaklar ÖLÇÜLMEZ, ÜRETİLİR — türetilmiş bir eklemden kural çıkarılmaz.**
    `OVRBody` `FullBody` (generative legs) alt gövdeyi üst gövdeden tahmin eder; gözlükte alt
    gövde sensörü yoktur. Tahmin edilen bacak, oyuncu bel yüksekliğindeki bir siperin **arkasında**
    dururken siperin **içinde** çözülebilir — ve antropometride bacaklar gövdenin ~%32'sidir, yani
    "gövdenin %30'u içeride" gibi bir kuralı **tek başlarına** tetiklerler. Belirti "hiç dokunmadan
    hasar alıyorum"dur ve sebep ne collider'da ne eşiktedir. Engel kuralının yalnız **kafayı**
    yargılamasının sebebi budur. Genel kural: **ölçülmüş veriye dayanmayan bir eklem, ceza üreten
    hiçbir hesaba girmez** (aynısı poz geçersizken donan tüm iskelet için de geçerlidir).
132. **`welcome` oyuncunun `hp`/`alive` durumunu TAŞIMAZ — yeniden bağlanan istemci kendini canlı
    SANMAZ.** Bağlantı kurulduğunda "canlı + tam can" varsaymak bir tahmindir ve ölüyken kopup
    dönen oyuncuda yanlıştır: istemci ölüm ekranını kapatır, tetik açılır, mermi düşer, ses ve iz
    oynar — ama sunucu her `hit_report`'u "atıcı ölü" diye atar. Belirti teşhis edilemez bir
    *"vuruyorum ama adam ölmüyor"*dur ve **kendiliğinden geçmez**: sunucunun zamanlayıcı tabanlı
    bir canlandırması yoktur (§3.7) ve istemci ölüm ekranını göstermediği için oyuncu canlanma
    şartını sağlamaya hiç kalkışmaz — sapma kalıcıdır. Otoriter değer zaten telde: `lobby_state`'in
    `PlayerInfo.hp`/`alive` alanları. Genel kural: **bilinmeyen bir otoriter durum tahmin edilmez,
    öğrenilene kadar son bilinen değer korunur.**
133. **"En yüksek alfa kazanır" bir hakem, ikinci bir GÖRSEL katmanı sessizce yok eder.**
    `ScreenFade` tek bir quad'ı paylaştırıyor ve kazananı çizip diğerlerini atıyor — bu, aynı soruyu
    ("ekran ne kadar kararsın") soran kaynaklar için doğru, **farklı** bir şey söyleyen bir katman
    için felakettir. Can kaybının kırmızısını oraya bir kaynak olarak eklemek, engel karartması
    1.0'dayken onu tümden görünmez yapar: oyuncu kapkaranlık bir ekranda ölür ve **neden öldüğünü
    hiç göremez**. Ne hata basar ne de kod yanlış görünür — hakem tam olarak tarif edildiği gibi
    çalışmaktadır. Kural: **bir hakem yalnız kendi sorusunun cevaplarını sıralar**; üstte görünmesi
    gereken her şey kendi renderer'ıyla, daha yakında ya da `Overlay` kuyruğunda çizilir.
134. **Bir eşiği aynı zamanda şiddet rampası olarak kullanmak, kuralı tam da işe yarayacağı anda
    kapatır.** "Kafanın yedi noktasından kaçı içeride" ölçüsü hem *"ihlal mi"* sorusunu hem
    karartma alfasını sürüyordu; sonuç, kafasını duvara yarıya kadar sokan oyuncunun **ceza alıp
    yine de görmeye devam etmesiydi** (5/7 içeride ≈ 0.7 alfa). Rampa doğru geliyordu çünkü
    yumuşak geçiş istiyorduk — ama yumuşaklığın yeri **ihlalden önceki** banttır, ihlalin içi
    değil. Kural: **eşik ile şiddet ayrı beslenir**; bir kural tetiklendiyse sonucu koşulsuz
    uygulanır, tereddüt yalnız tetiklenmeden ÖNCE meşrudur.
135. **Yasak durumda İLERLEYEN bir sayaç, o durumun yasağını delip geçer — son eylemi kesmek
    yetmez.** "Engelin içinde canlanma yok" kuralı hem istemcide hem sunucuda duruyordu, ama
    `standstill` sabit durma sayacı engelin içinde de işliyordu: oyuncu korunaklı yerde bekliyor,
    dışarı adımını attığı **anda** canlanıyordu — beklemenin tamamını bedavaya getirmiş oluyordu.
    Kapı doğru çalışıyor, hata da vermiyordu; delinen şey kapı değil **beklemenin anlamıydı**.
    Kural: bir şart *"şu kadar süre şunu yap"* biçimindeyse, o sürenin **nerede** geçtiği şartın
    parçasıdır — yasak durumda sayaç ilerlemez, sıfırlanır. Aynı soruyu her bekleme sayacına sor:
    *"bu süreyi oyuncunun bulunmaması gereken bir yerde doldurabilir mi?"*
136. **Bir alanın telde `false` görünmesi "sıfırlandı" demek DEĞİLDİR — sıfırlama bir KOMUTTUR,
    bir değer değil.** İstemci roster'daki `calibrated:false`'u operatörün sıfırlaması sayıp kayıtlı
    `OVRSpatialAnchor`'ı siliyordu. Oysa sunucu aynı alanı her `hello`'da sıfırlıyor (§10.6):
    sıradan bir ağ dalgalanması bile onu bir kez `false` yayınlıyor ve o yayın, başlığın kendi
    yeniden bildirimiyle aynı sokette yarışıyor. Bedel **gecikmeli ve sessiz**: rig o an
    taşınmadığı için oturum düzgün görünüyor, hata ancak bir sonraki `load_match`'te — geri
    yüklenecek anchor kalmadığında — *"oyuncu herkese metrelerce kaymış görünüyor"* diye ortaya
    çıkıyor. Üstelik yeniden bildirim sunucuyu "kalibreli" yaptığı için elle kalibrasyon kapısı da
    kapanıyor, yani oyuncu kendi başına düzeltemiyor. Genel kural: **yıkıcı bir işlemi bir durum
    alanının anlık DEĞERİNE bağlama, o işlemi isteyen KOMUTA bağla.** Değer birden çok sebepten o
    hâle gelebilir (protokolün kendi sıfırlaması, yarış, yeniden bağlanma); komutta ise niyet
    vardır. Ayırt edici soru: *"bu alanı `false` yapan tek şey, benim tetiklemek istediğim olay
    mı?"* — hayırsa alan bir tetikleyici değil, yalnız bir aynadır.
137. **Kökü ölçeklenen bir hiyerarşiyi, o hiyerarşinin DIŞINDAN sürülen bir şey takip edemez —
    ölçek dönüşümü elle tekrarlanır.** Uzak avatarın boyu karakterin köküne yazılan üniform bir
    ölçekle taşınıyor (`bodyScale`, §10.8); elde çizilen silah ise karakterin altında DEĞİL ayrı
    bir kapta duruyor ve telden gelen **ham** el pozundan sürülüyor (bilinçli: silah gerçek boyunda
    kalmalı ve atışın bildirildiği poz ham pozdur). İkisi ölçek `1` iken çakışıyor, ölçek saptığı
    anda ayrılıyor: çizilen el `kök + ölçek × (ham − kök)` noktasına giderken silah ham pozda
    kalıyor ve `1.3` ölçekte elin yarım metre uzağında havada duruyor. Belirti *"silah elde değil"*
    olduğu için teşhis kavrama matematiğine gidiyor — oysa kavrama doğru, ayrılan şey **referans
    noktası**. Genel kural: **bir dönüşümü (ölçek/ofset/hizalama) hiyerarşinin bir dalına
    uygularken, aynı veriyi hiyerarşi dışından okuyan tüketicileri say.** Ölçeklenmemesi gereken şey
    (silahın boyu) ile ölçeğe uyması gereken şey (silahın yeri) aynı kararın iki ayrı yarısıdır;
    "ölçeklenmez" deyip ikisini birden dışarıda bırakmak ilkini korurken ikincisini bozar.
138. **İki ayrı SENSÖRDEN doğan iki poz, aynı uzayda olsalar bile aynı yeri göstermez — çizimde
    ikisini yan yana koyma.** Uzak avatarda el iki kaynaktan geliyor: telde giden `handL`/`handR`
    **kumandanın** pozu, çizilen bilek ise iskelet blob'undaki **body tracking** çözümü. İkisi de
    aynı rig dönüşümünden geçiyor, yani uzay sorunu YOK — buna rağmen aynı noktayı göstermiyorlar,
    çünkü arada modelin kol uzunluğu var. Proje gövde oranlarını **bilerek** kalibre etmiyor
    (`Calibrate()` blob'un eklem sıkıştırmasını bozardı, §10.8), yani model prefab oranlarını
    taşıyor: kol uzandıkça modelin eli kumandaya yetişemiyor, büküldükçe yetişiyor. Silahı ham el
    pozundan çizmek bu farkı doğrudan görünür kılıyordu ve belirtisi teşhisi yanlış yere
    götürüyordu — *"silah bazı duruşlarda elinde, bazılarında havada"* okunduğunda ilk bakılan yer
    kavrama matematiği oluyor, oysa kavrama doğru; ayrışan şey **referans noktası**. Aynı sebep
    takım gövdesinde bir kez daha var (ayrı FBX, ayrı kol uzunluğu), yani hata takıma göre de
    değişiyor. Genel kural: **bir şeyi bir gövdenin üstünde çizeceksen, konumunu o gövdenin
    ÇİZİLEN kemiğinden al** — telde gelen ölçüm otorite olarak doğrudur (atış, vuruş, hedef
    çözümü ondan gelir) ama çizimin referansı değildir. Ölçüm ile çizim aynı sayı olmak zorunda
    değildir; olmadıklarında hangisinin nerede kullanıldığı yazılı olmalıdır.
139. **Bir şeyi ÖLÇMEK ile TÜRETMEK arasında seçim varken, iki uçta aynı görünmesi gereken şey
    türetilir.** Uzak avatarın parmakları izlemeden gelip telde taşınıyordu. Ölçüm gibi görünüyordu
    ama değildi: kumanda tutan bir elin parmakları zaten gerçek duruşu göstermiyor, üstelik iki el
    ayrı ayrı ölçüldüğü için **aynı silah sol ve sağ elde farklı tutuluyordu** — oyuncunun gördüğü
    tutarsızlığın kaynağı buydu. Oysa "bu silah nasıl tutulur" sorusunun cevabı her istemcinin
    APK'sında zaten var (eşya tanımı) ve tek bir yerden okunduğunda iki el de aynı olur. Kesilen
    veri kanalın **%61'iydi** (66 eklemin 40'ı), yani doğruluk ile bant genişliği aynı yöne
    çekiyordu. Genel kural: **bir veri her uçta AYNI çıkmak zorundaysa, onu taşıma — tanımdan
    türet.** Taşınan her kopya kendi gürültüsünü de taşır ve iki kopya arasındaki fark, kaynağı
    olmayan bir hataya benzer. Ters yönde de sınırı hatırla: fiziksel olarak farklı olabilen şey
    (elin NEREDE olduğu) türetilemez, o ölçülür ve taşınır.
140. **Windows'ta bir "güvenlik uyarısı" antivirüs demek değildir — Defender dışlaması yalnız AV
    taramasını susturur, Code Integrity politikasını değil.** İki ayrı bekçi var ve ayrı listelerle
    çalışıyorlar: Defender AV (`ExclusionPath`/`ExclusionProcess`, `scripts\defender-exclusions.cmd`)
    ve **Smart App Control** — SAC bir CI politikasıdır, dışlama listesini **hiç okumaz**, üstelik
    açıkken AV dışlamalarını da geçersiz kılar. Unity'de çarpma noktası kaçınılmaz: **Burst**,
    `Library/BurstCache/JIT/` altına çalışma anında **imzasız** native DLL üretip yükler; SAC bunu
    engeller (`CodeIntegrity` olayı **3077** + `3118`), kod/paket her değiştiğinde Burst yeniden
    derlediği için uyarı tekrarlar ve Burst'lü işler sessizce yavaş yola düşer. Aynı politika
    imzasız kendi çıktılarımızı da (`deploy\*.exe`) engelleyebilir — işletme PC'si kurulumunda
    bu yüzden SAC'ın kapalı olduğu doğrulanır (`Docs/Isletme-Kurulum.md` §5).
    Teşhis sırası: `Get-MpThreatDetection` **boşsa** olay AV değildir →
    `Get-WinEvent -LogName Microsoft-Windows-CodeIntegrity/Operational`. Genel kural: **belirtiyi
    susturan ayarı değil, olayı ÜRETEN bileşeni bul** — gerçek zamanlı korumayı kapatmak uyarıyı
    yok eder, sebebi ve makinenin savunmasını da götürür. ⚠️ SAC kapatmak tek yönlüdür: bir kez
    kapatılınca Windows yeniden kurulmadan geri açılamaz.

140. **Bir ayarlama aracında GÖZLE DÜZELTİLEN şey, oyunun okuduğuyla AYNI ÇERÇEVEDE olmak
    zorundadır — yoksa aracın kendisi hata üretir.** Kavrama authoring'inde iki el çerçevesi var:
    ISDK el modelinin kök transformu **bilek** çerçevesindedir, `WD_*` kavrama alanları ise **kumanda
    anchor'ı** çerçevesinde tanımlıdır (`HandGripPivot`) ve ikisi arasındaki dönüş ölçülmemiş
    ergonomik tahminlerden türer (`HandGripConvention`). O çeviri authoring döngüsüne girdiği anda,
    kullanıcının gözüyle "kabzaya tam oturmuş" gördüğü el tahminin hatası kadar yanlış yere yazılır
    ve silah oyunda o kadar dönük çıkar (belirti: 90° roll). Hata sessizdir: araçta her şey doğru
    görünür, belirti yalnız gözlükte ortaya çıkar ve sayı elle yazılmış gibi okunur. Kural bu yüzden
    **çeviriyi hiç yaptırmamaktır**: kullanıcının sürüklediği el kökü bilek çerçevesidir, kaydedilen
    poz düğümü de bilek çerçevesindedir ve oyun tarafı o düğümü el MODELİNE aynen uygular, bilek
    NOKTASINI da canlı ölçülen bileğe oturtur (`ItemGripAuthority` +
    `HandGripPoser.TryGetAnchorToWrist`; silahın ROTASYONU bu döngünün dışındadır —
    `primaryGripEuler` sabiti) — yani dokunulan transform ile okunan veri aynı uzaydadır, arada
    tahmin edilmiş hiçbir sabit yoktur. Anchor'a çeviri yalnız İKİNCİL fallback alanlarının
    **yazımında**, tek yönde kalır. Genel kural: **bir düzenleyicide düzeltilen
    şeyle yazılan şey aynı uzayda değilse, arada duran dönüşüm er geç veriye kaçar** — ya o dönüşümü
    tümden kaldır (yolu ölçülebilir kıl), ya da aracın içine koy; kullanıcının gözüne bırakma. İkinci
    güvence ucuzdur: "aç, hiç dokunma, kaydet" değeri değiştirmemelidir; bu kimlik tutuyorsa kurulum
    ile kayıt gerçekten birbirinin tersidir. ⚠️ Türetilmiş bir sabit gerçekten gerekiyorsa
    **ölçülür**: anchor→bilek deltası rig'den her karede okunuyor ve kumanda sürümlü el pozları
    deterministik olduğu için her başlıkta aynı çıkıyor — yani kanoniklik tahminle değil ölçümle
    korunur.
    ⚠️ Aynı hikâyenin ikinci yarısı **referansın seçimidir**: sabit tutulan şey ölçünün paydası
    olmalı. Kavramada ölçü "silah ELE göre nerede" olduğu için sabit duran silahtır ve hareket eden
    eldir — üstelik iki el (ana kabza + ön kabza) birbirinden bağımsız ayarlanmak zorunda ve her
    silahın kabzası farklı açıdan tutuluyor. Hem referansı hem ölçüyü aynı anda oynatan bir araç,
    kullanıcıya "neyi düzelttiğini" söyleyemez.

141. **Bir şeyi ÇİZEN uçla onu KONUMLANDIRAN uç farklı kaynaklardan besleniyorsa, ikisini
    birleştiren bir adım YAZILMADIKÇA aradaki fark ekranda durur.** Uzak avatarda eşya, ana elin
    **kumanda anchor'ı** pozundan çiziliyordu; oyuncunun görülen eli ise retarget edilmiş
    **anatomik bilek**. İkisi tasarımı gereği farklı noktalar (aradaki fark `HandGripPivot`'un
    henüz ölçülmemiş avuç ofseti + retarget hatası) ve arada onları birleştiren hiçbir şey yoktu:
    her oyuncunun silahı, elinin birkaç santim ilerisinde duruyordu. Yerelde aynı boşluk yok, çünkü
    `HandGripPoser` sentetik bileği kavrama pozuna **sert kilitliyor** — yani hata yalnız
    "başkasının ekranında" görünüyordu ve silahtan silaha değişmiyordu (silahla ilgisi yoktu).
    Kural iki parçalı: (1) **yön tektir** — eşya otorite, el takipçi (eşyayı çizilen bileğe taşımak
    sol/sağ simetrisini bozar ve silahı oyuncunun nişan aldığı yerden kaydırır); (2) takip
    **rotasyonla** yapılır, konumla değil — bileği taşımak kemik uzunluğunu değiştirir ve
    `localRotation` kopyalayan ikinci gövde (`SkeletonPoseMirror`) onu takip edemez. Genel biçim:
    **"iki uç aynı yeri göstermeli" diyorsan, o eşitliği kuran kodu göster.** Yoksa eşitlik bir
    varsayımdır ve varsayımlar ekranda birikir.

142. **Bir SDK'nın "yaz" ve "oku" yolu ASİMETRİKSE, ham transformdan geri okumak veriye görsel bir
    ofset gömer.** ISDK parmak pozunu uygularken modelin kendi eklem ofsetini ekliyor
    (`HandPuppet.SetJointRotations`: `localRotation = HandJointMap.RotationOffset * jointRotations[i]`),
    yani kemiğin `localRotation`'ı **poz + model ofseti**dir. Poz düğümünü kemiklerin ham
    `localRotation`'ından yazan bir kayıt, o ofseti pozun içine gömer; poz bir dahaki uygulanışında
    ofset ikinci kez eklenir ve parmaklar yazılandan başka türlü kıvrılır — üstelik sapma her
    Kaydet'te büyür. Belirti sinsidir: araçta el doğru görünür (orada ofset zaten uygulanmıştır),
    yanlışlık yalnız oyunda ve yalnız "biraz tuhaf" olarak ortaya çıkar. Doğru yol SDK'nın kendi
    okuma yolundan geçmektir (`HandPuppet.CopyCachedJoints` → `HandJointMap.TrackedRotation`, ofsetin
    tersini alır). Genel kural: **bir veriyi geri okurken, onu yazan çağrının tam tersini kullan** —
    "aynı alandan okurum" refleksi, arada bir dönüşüm varsa sessiz bir birikim üretir.
143. **Editör asmdef'inde derlenen bir `MonoBehaviour` `AddComponent` ile EKLENEMEZ — Unity sessizce
    `null` döner.** Editör derlemesindeki bir bileşen tipi sahne objesine takılamaz ("it is an editor
    script"); dönüş değerini kullanan bir sonraki satır `NullReferenceException` atar ve kurulum
    yarıda kalır — geriye kimliği olmayan, aracın kendi temizliğinin bile göremediği bir kalıntı
    obje kalır. Sahne objesine takılacak authoring bileşeni **runtime asmdef'ine**, dosyanın tamamı
    `#if UNITY_EDITOR` sarmalında konur: tip build'e girmez, örnekler de `HideFlags.DontSave`
    olduğu için diske yazılmaz.

144. **`EditorSceneManager.sceneOpened` yalnız kullanıcının açtığı sahneler için koşmaz — prefab
    kipinin ÖNİZLEME sahnesi de bu olayı tetikler.** Prefab kipinde *Auto Save* açıkken sahnedeki
    herhangi bir objeyi (diske yazılmayan `DontSave` yardımcıları dahil) oynatmak stage'i kirletir,
    Unity prefabı kaydeder ve içeriği yeniden yükler; o yeniden yükleme önizleme sahnesini yeniden
    açar. "Sahne değişti, geçici objelerimi temizleyeyim" diye yazılmış bir kanca bu yüzden
    kullanıcı **hiçbir sahne açmadan**, hatta tam da o geçici objeyi sürüklerken koşar ve objeyi
    kendi eliyle siler. Belirti aracın kendi kodunda hiç görünmez: obje bir anda yok olur, pencere
    bir sonraki çizimde ölü referansla `MissingReferenceException` atar ve suçlu Unity sanılır.
    Kural iki maddedir: (1) temizlik kancası **olayın verdiği sahneyle sınırlanır** (o oturumdaki
    tüm sahneleri süpüren bir "hepsini temizle" yazılmaz), (2) önizleme sahneleri elenir — ⚠️ ayırt
    edici işaret "`scene.path` boş" DEĞİLDİR: önizleme sahnesinin path'i **prefabın asset yoludur**
    (`Assets/.../*.prefab`; ölçüldü). Gerçekten açılan sahne her zaman bir `.unity` dosyasıdır —
    eleme uzantıdan yapılır; açık stage referansı yeniden yükleme sırasında bir an bayat
    olabildiği için ona tek başına güvenilmez. İkinci savunma hattı çizen taraftadır: bir
    kare içinde toplanan obje listesi kullanılmadan önce Unity'nin `== null` karşılaştırmasıyla
    süzülür, çünkü stage'i dışarıdan yenileyen başka yollar da vardır.

145. **Prefab kipinin kaydı, önizleme sahnesindeki HER fazladan kökü `HideAndDontSave`'e
    çevirir — başlangıç bayrağı ne olursa olsun** (ölçüldü: `DontSave` de `None` da çevriliyor;
    kök prefaba sızmaz). Obje yok edilmez — yaşar, çizilmeye devam eder, sahnenin kök listesinde
    durur — ama Hierarchy panelinden düşer ve düzenlenemez olur. Belirti "geçici obje kaydda
    siliniyor" sanılır; oysa obje yerindedir, yalnız gizlenmiştir (görselinin ekranda kalması
    ayırt edici ipucudur). ⚠️ Tek atımlık bir olay kancasıyla düzeltilemez: çevirme kayıt akışının
    birden çok noktasında koşar (`prefabSaved` anında çoktan çevrilmiştir; olay içinde geri
    yazılan bayrak kayıt bittikten sonra YİNE çevrilir — ölçüldü: kayıt başına iki müdahale
    gerekir) ve `EditorApplication.delayCall` editör odaksızken hiç işlemez (`update` işler).
    Stage sahnesinde yardımcı kök tutan araç bu yüzden `EditorApplication.update`'te duran,
    yalnız bayrağı bozulmuş kökü düzelten ucuz bir bekçi taşımak zorundadır. Kavrama stüdyosunda
    bunu `RestoreHandFlags` yapar; "Elleri Oluştur" da bulduğu mevcut eli aynı yoldan diriltir.
146. **Movement SDK retargeter'ında `ApplyRootScale` KAPALI kalır — açıkken karakter kökünün
    pozisyonu bir DÜNYA noktası olmaktan çıkar.** Açık olduğunda `CharacterRetargeter.ApplyPose`
    her karede `transform.localScale = rootScale` yazar (oyuncunun boyu ÷ modelin boyu) ve
    retarget edilmiş pozları o ölçekli uzayda üretir; sonuç `_characterRoot.position`'ın
    `gerçekDünyaNoktası ÷ rootScale` olmasıdır — yani **dünya orijini etrafında ölçeklenmiş** bir
    nokta. `ArenaNetCharacterBehaviour` bu değeri tele koyduğu için uzak gövde yanlış yere çizilir.
    ⚠️ **Hata dünya orijininde SIFIRDIR ve orijinden uzaklıkla doğru orantılı büyür** (orijin
    etrafında ölçekleme orijini yerinde bırakır): arenası orijinde olan bir sahnede hiç fark
    edilmez, `VA_ArenaBoundary`'si 200 m öteye taşınmış bir sahnede aynı ayar gövdeyi onlarca
    metre uzağa fırlatır. Bu yüzden belirti "yeni harita bozdu" diye okunur — oysa bozan harita
    değil, haritanın **kaldıraç kolu**dur.
    **Belirtinin imzası:** kafa/eller doğru yerdedir (ikisi de rig'in ÇOCUĞUDUR, kalibrasyonu
    miras alır), yalnız gövde kayar → ad etiketi ve elindeki silah görünür, gövde görünmez; vuruş
    kutuları gövdeyle birlikte gittiği için kimse kimseyi vuramaz. Teşhis tek ölçüdür: aynı
    oyuncunun `RemoteSkeletonRegistry.TryGetInterpolatedRoot` kökü ile
    `RemotePlayerRegistry.GetInterpolatedPose` kafası karşılaştırılır. Aradaki fark sabit bir
    ÖTELEME değil sabit bir ORAN'sa (x ve z'de aynı katsayı, oyuncu yürürken de korunur) sebep
    ölçektir; oranın tersi doğrudan `rootScale`'i verir.
    ⚠️ Ayarın kapalı olması bir tercih değil, projenin zaten yazılı olan kuralının önkoşuludur:
    gövde ORANI kalibre edilmez, boy farkı yalnız `bodyScale` ile ve yalnız UZAK avatara taşınır
    (§10.8). `ApplyRootScale` açıkken gönderen sessizce kendi oranını uygular ve o kural delinir.
147. **`Awake` içinde `AddComponent` ile eklenen bir bileşende `enabled = false` HER ZAMAN
    TUTMAZ — kapanma niyeti alan kontrolüyle de yazılmalıdır.** Kurulumu başarısız olan bir
    bileşen kendini kapatmakla yetinirse ve kapanma tutmazsa, `Update`/`LateUpdate` yarı kurulmuş
    alanlara dokunup **kare başına** `NullReferenceException` basar (saniyede ~90 satır: konsol
    tamponu dolar ve ondan önceki gerçek uyarılar dışarı atılır — yani arıza kendi teşhisini de
    siler). Daha kötüsü, istisna metodun ORTASINDA atıldığı için geri kalan işler hiç koşmaz:
    `RemoteHandPoser`'da parmak duruşu düşünce el/kol IK'sı da hiç çalışmaz. Kural: kapıyı
    "kapandım mı" değil "elimdeki veri geçerli mi" sorusuna bağla.

---

## 8. Durum ve sıradaki işler

**Bugün çalışan sistem** (ayrıntı §2–§7): lobi + 20 Hz poz senkronu + **gövde senkronu**
(oyuncu kendi gövdesinden hiçbir şey görmez; gördüğü eller **rig'in sentetik elleridir** —
kumanda modeli ve mesafeli kavramanın hayalet elleri çizilmez) +
**elde tutulan eşya senkronu**
(uzak oyuncuların silahı kanonik kavramayla çizilir) + **çerçeveden silah seçimi** (sahnedeki silah
çerçevesinden ayrılmaz; `maxGrabDistance` mesafesinden nişan alınıp grip'e basılınca ele bir
klonu gelir, bırakılınca
gizlenir ve aynı silah aynı mermiyle geri çağrılır — oyuncu başına tek silah, harita başına
sıfırlanır) + **soket tabanlı kavrama** (elde tutulan eşyada: el yaklaşınca gösterge belirir,
soketin üstünde grip'e basılınca kavranır) + **UDP
atış/atma olay kanalı** (namlu alevi, ses, mermi izi — her olay kendi `serverTick`'inde,
interpolasyon saatiyle oynatılır) + sunucu-otoriter maç
(faz makinesi, vuruş hattı, free-roam canlanma, kill-feed/HUD) · **üç oyun modu** — `tdm` (Takım
Ölüm Maçı), `ffa` (Herkes Tek: takımsız, bireysel skor, sabit durma canlanması, grip'e basınca
elde rastgele silah) ve `tournament` (Turnuva: tur tabanlı takım elemesi — tur içinde canlanma yok,
tur bitince herkes tabanında toplanıp yeni tur başlar; §3.8.2) · **çok mod altyapısı** (`ModeRules`
şekil tanımı §3.9, bireysel skor, `MatchOutcome`, takım-agnostik `ModeHudBase`, admin'den maç
süresi/skor limiti/geri sayım) · **iki mekan** (`Outdoor12x12`, `VortexAntep`) ve her birinin kendi lobisi —
sunucu açılışta hangisinin oynatılacağını sorar (§3.8) + arena kurulum araç zinciri
(`Template Temellerini Yükle` → ölçü maketi → `Configure All Build Elements`) ·
admin **sahne-içi gözlemci** (üç kamera kipi + sahne üstü yönetim HUD'ı,
çoklu admin) · geliştirici araç seti (`Tools > VortexArena > Development > Dev`, `dev-targets.json`,
`Ctrl+Alt+R`) · rolden bağımsız adres zinciri + `ConnectionOverlay` bağlantı hata ekranı ·
**sunucu-otoriter kalibrasyon durumu** (§3.11: admin sıfırlar → oyuncu savaş dışı + avatarı parlar;
geri açmayı gözlük yapar) · **ağ telemetrisi** (§3.12/§6.7: sunucu konsolunda gerçek bayt-sn +
paket-sn + tik kayması + uplink jitter/kayıp; gözlükte ölçülen RTT/downlink jitter/kayıp → admin
istatistik panelinde **PING** kolonu) · WPF operatör launcher'ı (sunucuyu `--venue` ile başlatır) +
dört dağıtım betiği.

> **Sıradaki büyük iş: bulut kalibrasyonu** (Meta grup / paylaşılan uzamsal anchor ile toplu
> hizalama). Altyapısı hazır bırakıldı — `set_calibration.source` `"cloud"`'u kabul ediyor,
> `clear_calibration{playerId:0}` toplu sıfırlıyor, `ArenaCalibrator.AlignRigToAnchorPose`
> `internal` seam olarak açık. Protokol değişikliği gerekmiyor.

> **Yeni bir modun maliyeti ne olmalı:** `ffa` bugün protokolde **tek bir alan bile tutmuyor** ve
> TDM ile hiçbir kod paylaşmıyor — sunucuda bir `IGameMode` dosyası + tek satır kayıt, istemcide
> bir mod kutusu + bir paylaşımlı bileşen (`WeaponGranter`). Sonraki modlar (turnuva, silah
> yarışı, bölge kontrolü, zombi) aynı ucuzlukta gelmeli; gelmiyorsa eksik olan `ModeRules`'te bir
> kuraldır, istemcide bir `if` değil.

**Sıradaki planlanmış işler `plan/` altındadır** (`plan/README.md` tablosu); biten işin dosyası
silinir.

**Kapsam dışı — bilinçli kararlar** (yeniden gündeme gelirse bu gerekçeler tartışılmalı):

- **"Oyuncunun gözünden izleme" video akışıyla DEĞİL, oyun datasıyla.** MJPEG/video akışı (cosmos
  `CameraStreamer` portu) **kapsam dışıdır**: admin zaten sahneyi kendi makinesinde render ediyor ve
  poz/can/skor/olay verisi ağdan geliyor. İstenen görüntü bu mevcut datadan üretilecek (admin
  kamerasını hedef oyuncunun poz'una kilitlemek); protokole yeni binary kare tipi, sunucuya kare
  relay'i ve Quest'te encode maliyeti **girmeyecek**.
- **Kayıt/replay, ısı haritası, hasar istatistiği** — protokolde veri yok, ayrı iş.
- **Admin'in POV kipinde oyuncunun mod HUD'ını görmesi** — mod HUD'ı player-only; istenirse
  `ModeHudSpawner`'a "gözlemci kipi" eklenir.
- **Admin'den oyuncuya ses/mesaj** — `identify` dışında kanal yok.
- **Hile koruması** — ürün gözetimli özel alanda çalıştığı için bilinçli olarak yok
  (`Docs/ArenaNet-Protokol.md` §10.3).
- **Quaternion sıkıştırma / delta snapshot** — bir zamanlar "kalabalık maçta bant gerekirse"
  diye ufukta duruyordu; **listeden çıkarıldı.** Bant hiçbir zaman darboğaz değil (§3.12) ve
  sıkıştırma bağlayıcı kısıt olan paket sayısına dokunmuyor. Doğru kaldıraç **kanal
  birleştirmek**tir ve o yapıldı: `0x05` (§6.8) + `health_update`'in hedefli gönderimi paket
  sayısını ~%40 düşürdü, sıkıştırma bunun yanında hiçbir şey getirmezdi (§7, "kablosuzda maliyet
  BAYT değil ÇERÇEVE" maddesi).

**Planlanmamış ufuk:** yeni modlar (bölge kontrolü, turnuva, silah yarışı, zombi), dinamik obje senkronu
(`NetIdentity` + `NetSpawnCatalog` üzerinden), Meta colocation/paylaşımlı anchor araştırması
(offline çalışma şartıyla), launcher ekranından APK dağıtımı, eşzamanlı oyuncu kotası (lisanslama
katmanı geldiğinde).
