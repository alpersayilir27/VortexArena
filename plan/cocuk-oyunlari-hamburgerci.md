# Çocuk Oyunları — Hamburgerci (ilk oyun)

**Bağımlılık:** `ag-nesne-modeli.md` **B2 + B3**, `oyun-tipi-ve-tur-tipi.md`. Bomba ve kırılabilir
objelerden bağımsız. Protokol işi modelin sürüm artışı #2'siyle birlikte.

## 1. Oyun

- Free-roam alanda hamburger dükkânı: kesme tahtası (bıçak), ızgara (spatula), montaj tahtası
  (servis tahtası = tabak), servis bankosu, müşteri yolu ve banko slotları.
- Müşteri yürüyerek gelir, bankoda bekler, **kendi siparişini** gösterir (her müşteri farklı tarif:
  soğansız, iki köfteli…); oyuncular ekmeği keser, köfteyi pişirir, tahtaya dizer, bankoya koyar →
  sipariş doğruysa puan, müşteri **mutlu** gider. **Sabır 2 dk** (`CustomerPatienceSeconds = 120`,
  sunucu sabiti); dolarsa müşteri **mutsuz** gider — puan cezası yok, mutsuz sayacı artar.
- Mutlu/mutsuz sayaçları HUD'ın tepesinde; **admin sahne üstü yönetim HUD'ında da** görünür.
- Süre `roundSeconds` = vardiya; bitince `IsMatchOver` → skor ekranı (`HoldsResultForOperator=true`,
  operatör kapatır). Kazanan yok: toplam + bireysel sıralama.
- Silah yok (`weaponSource:"none"`), hasar yok, canlanma yok, takım yok (`teamMode:"none"`);
  skor `scoring:"shared"` (servis eden oyuncuya bireysel puan + ortak toplam).

## 2. Otorite dağılımı (modelle birebir)

| Şey | Nerede | Nasıl |
|---|---|---|
| Sipariş üretimi, müşteri yaşam döngüsü, sabır, puan, sayaçlar, süre | Sunucu `BurgerMode` | `OnTick` sayaçları. Müşteri = dinamik ağ nesnesi (`kind:"customer"`): `object_spawn{stage:Walking, s:<tarif>}` → `Waiting` → `Happy`/`Unhappy` (gidiş animasyonu) → `object_despawn` |
| Müşterinin yürümesi | İstemci sunumu | sahnede bake'li **waypoint yolu** (`CustomerPath` + banko slotları); spawn anından (`serverTick`) deterministik animasyon, **poz paketi yok** |
| Malzeme | Dinamik ağ nesnesi | dağıtıcı objeye `object_event(dispenser, "take")` → sunucu spawn (`owner` = isteyen, doğrudan elinde) — malzeme sonsuz |
| Kesme | Sunucu durumu | bıçak sahibi, bıçak bütün ekmeğe temas edince `object_event(bun_whole, "cut")` → sunucu bütün ekmeği despawn eder, aynı pozda `bun_bottom` + `bun_top` spawn eder |
| Pişirme | Sunucu durumu + mod sayacı | ızgaraya bırakılan köfte: sahip `object_event(patty, "grill", i:[1])` → mod sayacı `stage` Raw→Cooked→Burnt; kaldırınca `i:[0]` sayaç durur. Görünüm `stage`'den materyale (kitte tek köfte mesh'i, renk tonu shader'da) |
| Tutma / taşıma / fırlatma | Sahiplik + UDP poz (B2) | ISDK grab → `object_grab`; bırakınca sahip poz akıtır, dururken `object_release` |
| Dizme | İstemci fiziği + sahip raporu | Tahta **bankoda durur**, hamburger orada kurulur (yığın tahtaya bağlanmaz, dolu tahtayı taşımak malzemeleri yerinde bırakır). **Üst ekmeği koymak servis jestidir**: yığının en üstü `bun_top` olunca `object_event(board, "serve", i:[müşteri netId, malzeme netId'leri alttan üste])` → sunucu tarifle ve malzeme durumlarıyla karşılaştırır → doğruysa puan + müşteri `Happy`; yanlışsa aynı `serve` olayı relay edilir (red sesi/HUD), tahta yerinde kalır |
| Temizlik | Sunucu | servis edilen yığının malzemeleri `object_despawn` (tahta kalır); ayrıca canlı malzeme tavanı — taşınca en eski **serbest** malzeme despawn, eldekine dokunulmaz |
| HUD | İstemci `BurgerClientController : ModeHudBase` | süre/toplam/bireysel `match_state` + `PlayerInfo.score`; siparişler müşteri objelerinin `s` alanından; **mutlu/mutsuz sayaçları `modeState`** (`"h:3;u:1"` — çekirdek yorumlamaz, HUD ve admin HUD okur) |

## 3. Tarif ve doğrulama

- Tarif = `bun_bottom` + köfte ×(1–2) + {`cheese`, `bacon`, `lettuce`, `onion`, `pickle`, `tomato`,
  `sauce`} alt kümesi (0–3) + `bun_top`; mod her müşteri için rastgele üretir (mod içi sabit
  malzeme listesi ve olasılıklar, v1).
- `s` alanı alttan üste virgüllü liste (`"bun_bottom,patty,cheese,bun_top"`); HUD balonu ikonlara
  çevirir.
- Doğrulama: alt ekmek en altta, üst ekmek en üstte, **ortadaki sıra serbest**, malzeme çokluğu
  birebir (fazla/eksik = red), her köfte `Cooked` (Raw/Burnt = red).

## 4. Tür tablosu (v1)

**Kaynak kit:** `BurgerKit` (Blender + FBX/GLB + `T_BurgerKit_Baked` doku): `Burger_BunBottom`,
`Burger_BunTop`, `Burger_Patty` (tek mesh; pişme tonu materyalden), `Burger_Cheese`, `Burger_Bacon`,
`Burger_Lettuce`, `Burger_OnionRing`, `Burger_PickleSlice`, `Burger_TomatoSlice`,
`Burger_SauceLayer`, `Burger_ServingBoard`, birleşik referans `Burger_Assembled`.

| `kind` | Kaynak | Not |
|---|---|---|
| `customer` | **kitte yok** — karakter + yürüme/bekleme/mutlu/mutsuz animasyonu üretilecek | dinamik, `grab:"none"` |
| `bun_whole` | **kitte yok** — bütün ekmek modeli üretilecek | dağıtıcıdan; `cut` → iki yarım |
| `bun_bottom`, `bun_top` | kit | `cut` sonucu |
| `patty` | kit | `stage` Raw/Cooked/Burnt |
| `cheese`, `bacon`, `lettuce`, `onion`, `pickle`, `tomato`, `sauce` | kit | dağıtıcıdan |
| `board` (servis tahtası) | kit `Burger_ServingBoard` | dizme + servis |
| `knife`, `spatula` | **kitte yok** | sahne objesi, paylaşılan tek örnek, `grab:"anyone"`; obje ele bağlanır, **kavrama pozu stüdyoda yazılır**. ⚠️ `WorldSingle` olduğu için `itemL/R` baytı **`0` kalır** ve avatar katalogdan kopya **üretmez** — uzak el objenin kendi örneğini çizer (`Docs/ArenaNet-Protokol.md` §6.6) |
| `dispenser_<malzeme>`, `grill`, `cutting_board`, `counter_slot` | **kitte yok** | sahne objesi, `grab:"none"` |

Eksik modeller ayrı içerik işidir (Blender köprüsü ya da mağaza); plan onları bekler, tasarım
beklemez.

## 5. Sahne / katalog

- `Assets/Arenas/Venues/<İşletme>/Scenes/<Sahne>/` — arena zinciri aynen (boundary, ölçü dosyası,
  `Template Temellerini Yükle` → maket → `Configure All Build Elements`); `MapDefinition.gameType =
  Kids`, `supportedModeIds = ["burger"]`; `CustomerPath` waypoint'leri + banko slotları sahnede.
- `Assets/Modes/Burger/`: `VortexArena.Modes.Burger.asmdef` (refs Core, Net, Protocol),
  `BurgerClientController`, `UI/BurgerHud.prefab`, `Data/BURGER.asset` (`ModeDefinition`: `weapons=None`,
  `teamMode=None`, `revive=None`, `scoring=PlayerAndShared`, `gameType=Kids`).
- `GameCatalog.modes[]` + `Export Server Config` (objeler, `kinds`, `gameType` dahil).
- Admin: sahne üstü yönetim HUD'ına `modeState` satırı (mutlu/mutsuz).

## 6. Sunucu

- `BurgerMode : IGameMode` — `GameType="kids"`, `Rules { Teams=None, Scoring=PlayerAndShared,
  Revive=None, Weapons=None }`, `HoldsResultForOperator=true`, `OnTick` (müşteri spawn aralığı, sabır,
  pişme), `OnObjectEvent` (`take`/`cut`/`grill`/`serve`), `IsMatchOver` (süre), `SetModeState("h:…;u:…")`.
- Çekirdek API'si **hazır:** `SpawnObject(kind, pose)` · `DespawnObject` · `SetObjectStage` ·
  `SetObjectFlags` · `OnObjectEvent` kancası. **Eksik iki parça, bu modla birlikte yazılır:**
  - `SpawnObject`'in **sahipli** biçimi (malzeme doğrudan isteyenin elinde doğsun); bugünkü biçim
    objeyi serbest doğuruyor.
  - `scoring:"shared"`'ın **yazan yolu** (ortak toplam + bireysel katkı) → `oyun-tipi-ve-tur-tipi.md`.

## 7. İş listesi

**Yazıldı — mod uçtan uca seçilebilir ve başlatılabilir:** sunucuda `BurgerMode` (`modeId:"burger"`,
`gameType:"kids"`, süreyle bitiş, kazanan yok, sonuç operatörde durur) + kaydı; ortak skorlu maçta
kazanan sızıntısının kapatılması; Unity'de `Assets/Modes/Burger/` kutusu (`BurgerClientController` ·
`UI/BurgerHud.prefab` · `Data/BURGER.asset`); 14×16 kutusunda Çocuk Oyunları arenası (arena zinciri
+ **prototip** dükkân geometrisi); admin panelinin "Oyun tipi" satırı; katalog kaydı ve export.

⚠️ **Sahnedeki dükkân ve bütün eşyalar prototiptir:** kutu/silindir primitifleriyle kurulmuş banko,
ızgara, kesme tahtası, montaj masası, 9 dağıtıcı, müşteri kapısı, malzemeler ve kapsül müşteri.
Gerçek modeller gelince yerine geçecek; **yerleşim korunmalı** — istasyon bileşenleri, banko
slotlarının hacimleri ve müşteri yolu ona bağlı. Üç banko slotunun her birinde bir servis tahtası
durur (taşınacak bir tahta yok). İç engeller `Obstacle` layer'ındadır, müşterinin
collider'ı **yoktur** (free-roam alanda gerçek bir bedeni engellemesin diye).

**Oyun mantığı da yazıldı** (protokol **v18** içinde kaldı, sürüm artmadı): `object_state.s` örnek
verisi ve "duyuran = yazan" sözleşmesi; sahipli `SpawnObject`, `AddSharedScore`, `SetObjectPayload`,
`TryReadObject`; `BurgerMode`'un tarif üreteci, müşteri/sabır/pişme sayaçları, `take`/`cut`/`grill`/
`serve` doğrulaması ve `modeState` yazımı; istemcide `NetObject.Payload`,
`NetObjectPoseSender.RestSent` ve dokuz Burger bileşeni (`BurgerKinds` · `BurgerDispenser` ·
`BurgerKnife` · `BurgerGrill` · `BurgerCounterSlot` · `BurgerServingBoard` · `BurgerCustomerPath` ·
`BurgerCustomer` · `BurgerOrderBubble`); `Yemek-Kitabi` "Çocuk oyunu eklemek" reçetesi.

**İçerik kurulumu da yapıldı:** 24 `NetObjectKind` asset'i · dört `PropDefinition` (`WorldSingle` +
`ProximitySocket` + `Physics`) · 24 prototip prefab · `Resources/NetSpawnCatalog.asset` (12 dinamik
tür) · sahne yerleşimi (9 dağıtıcı, ızgara bölgesi, üç banko slotu + müşteri çıpaları, müşteri yolu,
tahta/bıçak/spatula) · admin HUD'ının müşteri sayacı satırı · export.

**Kalan:**

- [ ] Gerçek modeller (müşteri, bütün ekmek, bıçak, spatula, ızgara, tahta, dağıtıcılar, banko),
      animasyonlar, sesler — prototip kutuların yerine geçecek. ⚠️ Yerleşim korunmalı: bileşenler
      ona bağlı.
- [ ] Dört prop tanımının **kavrama pozu** (`Kavrama Pozu Stüdyosu`) — yazılmadan obje ele gelir ama
      kumanda anchor'ında durur.
- [ ] Servis tahtasının red sesi (`BurgerServingBoard.rejectSound`) ve müşteri gelme/gitme sesleri.

## Doğrulama (kullanıcı koşar)

- İki başlık: biri ekmeği keser, diğeri iki yarımı aynı anda görür; köfte iki başlıkta aynı anda
  `Cooked` olur, renk tonu aynı.
- Aynı bıçağı iki kişi aynı anda tutmaya çalışır: biri alır, diğerinin eli boş kalır, çalamaz.
- Bankodaki tahtaya doğru sırayla dizip **üst ekmeği koyunca** servis olur: servis edenin puanı +
  toplam artar, müşteri mutlu gider, `h` artar. Yarım yığın (üstte ekmek yokken) hiç rapor edilmez.
  Yanlış tarifte üst ekmek konunca red gelir, müşteri bekler; 2 dk dolunca mutsuz gider, `u` artar,
  puan düşmez.
- Dolu elle dağıtıcıyı sıkmak yeni malzeme doğurmaz; malzeme doğar doğmaz ele yapışır (havada
  asılı kalmaz). Kesilen ekmeğin iki yarısı iç içe geçmez.
- Admin HUD'ında mutlu/mutsuz sayaçları oyuncu HUD'ıyla aynı.
- Geç katılan başlık: bekleyen müşterileri (siparişleriyle) ve masadaki malzemeleri doğru yerde görür.
- Sahip koparsa elindeki malzeme yere düşer (serbest kalır), oyun sürer.
