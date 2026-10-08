# Burger mutfağı — cartoon kit ile yeniden giydirme (plan)

Burger modunun mutfağı, parça parça üretilen cartoon modellerle baştan giydirilir. Hedef görünüm
sevimli, tıknaz, sıcak renkli bir mutfaktır: beyaz karo duvar, siyah-beyaz dama zemin, kırmızı
tavan ve vurgular, ahşap alt dolaplar. Servis tarafı "SİPARİŞ" pencereli bir bankodur; müşteri
bankonun arkasında durur, numaralı zil ve fiş rayı vardır.

**Akış:** kullanıcı obje başına Gemini ile 2×2 dönüş sayfası (ön / sol / arka / sağ) üretir ve
dosya yolunu verir; ajan görseli PNG'ye çevirir, objeyi Blender MCP ile modeller, önizlemeyi
kullanıcının BurgerKit indirme klasörüne render eder, onay gelince Unity'ye alır ve sahneye
koyar. Kaynak dosya `blender/BurgerKitchen/BurgerKitchen.blend`, betikler
`blender/BurgerKitchen/scripts/` altındadır. Tripo kullanılmaz: çok açılı giriş abonelik ister,
tek görselden gelen kalite yetersizdir.

## 0. Değişmez kurallar

- **Oyun mantığı değişmez.** Kod, protokol, `PROTOCOL_VERSION` ve `<Sahne>_objects.json`
  değişmez. Bu iş yalnız görsel giydirmedir.
- **Oyun hacimleri yerinde kalır, görsel hacme oturtulur.** `GrillZone`, `CounterSlot_N` ve
  servis tahtası, dağıtıcıların `GripSocket`'i, `Stack`/`CargoAnchor` ve `CustomerPath`
  waypoint'leri korunur. Bir hacim taşınacaksa `Docs/Gelistirici/Yemek-Kitabi.md` "Bir oyun
  istasyonunu ikinci bir mekana kurmak" kurallarına uyulur: hacim görselin çocuğudur, kök
  `rot 0`/`scale 1` kalır.
- **Yerine koyma yalnız renderer düzeyindedir:** eski objenin mesh'i/materyali değişir,
  collider'ı ve oyun hacimleri olduğu gibi durur.
- **Malzemeler değişmez:** köfte, ekmek, domates vb. bugünkü paketten gelir.
- **Kit paylaşılan materyallerle çizilir** (§8). Obje eklemek yeni materyal açmaz; renk farkı
  palet UV'si ile verilir (§5).
- **Kabuk doku işidir.** Zemin, duvar, tavan ve uzun tezgâh gövdeleri düz geometriye döşenen
  dokudur; model olarak üretilmez.
- **Her model Blender'dan FBX + ayrı doku olarak geçer** (`Yapma-Listesi.md` GLB maddesi).
- **Gerçek zamanlı point/spot ışık yoktur.** Lambalar emissive materyal + bake ile çözülür.
- **Collider'lar elle kurulur:** kutu collider, convex `MeshCollider` yok; gövde `Obstacle`,
  pervaz `Default` çocukta (`Yemek-Kitabi.md` aynı bölüm, 6. madde).

## 1. Görsel yön — stil kartı

| Öğe | Karar |
|---|---|
| Biçim | Tıknaz, yuvarlatılmış köşeler, hafif abartılı oranlar. İnce detay yok: bir obje 2 m'den okunur |
| Yüzey | Mat, el boyaması hissi, yumuşak geçişler. Metal ayna krom değil, açık mavi-gri boyalı çeliktir |
| Duvar | Krem-beyaz metro karo, koyu derz |
| Zemin | Siyah-beyaz dama, ~30 cm kare |
| Tavan | Kırmızı, yuvarlak gömme lambalar |
| Alt dolap | Sıcak ahşap kapaklar, açık gri / beyaz tezgâh üstü |
| Vurgu | Kırmızı (davlumbaz, ızgara gövdesi, banko önü, süpürgelik), hardal sarı (tepsi), diner turkuazı (servis penceresinin arkası) |

**Renk paleti** (her promptta aynı satır):

| Ad | Hex |
|---|---|
| Domates kırmızısı | `#D8342C` |
| Krem beyaz | `#F5F0E6` |
| Sıcak ahşap | `#A8662F` |
| Çelik gri-mavi | `#B8C4CE` |
| Kömür | `#2B2B2B` |
| Hardal sarı | `#F4BE2C` |
| Diner turkuazı | `#2FA49B` |

## 2. Yerleşim — bugünkü oyun hacimlerine göre

Oda kabuğu 10 × 9 m, tavan 5,5 m. Oyun alanı ölçüsü `Data/<Mekan>_dimensions.json`'dadır.
**Tezgâh çalışma yüzeyi 1,04 m**dir; bütün eşya hacimleri ve kitin tezgâh üstleri bu
yüksekliğe göre kurulu. Alçaltmak (çocuk boyu) bütün hacimleri, soketleri ve tezgâhları birlikte
kaydırmayı gerektirir.

```
                       KUZEY  — pişirme hattı
  ┌──────────────────────────────────────────────────────┐
  │        [ DAVLUMBAZ + boru ............................ ]│
  │        [IZGARA 1]           [IZGARA 2]     [Fritöz]     │
  │                                                         │
 B│ müşteri ║ SERVİS  ║                         [Lavabo]    │D
 A│ koridoru║ BANKOSU ║   [Montaj    [Montaj    [Buzdolabı] │O
 T│ (diner  ║ ORDER 1 ║    adası]     adası]                │Ğ
 I│ arkası) ║ ORDER 2 ║                          [Çöp]      │U
  │         ║ ORDER 3 ║                                     │
  │ [Kapı]                                                  │
  │   [ MALZEME RAFI: 9 kap + tavandan inen renkli tüpler ] │
  └──────────────────────────────────────────────────────┘
                       GÜNEY  — malzeme duvarı
```

Bugünkü hacimlerin dünya konumu (x, z; metre):

| Hacim | Konum | Ölçü |
|---|---|---|
| Izgara pişirme hacmi ×2 | (0,56 · 3,63) ve (2,99 · 3,58), y≈1,02 | 1,07 × 0,65 |
| Servis yuvası ×3 | x −2,96; z 2,21 · 0,17 · −1,88; y≈1,20 | 1,00 × 1,60 |
| Müşteri noktası ×3 | x −3,75; aynı z'ler | — |
| Müşteri yolu | güneybatı köşe (−4,40 · −3,85) → (−3,75 · 0,17) | — |
| Dağıtıcı ×9 | z −3,48; x −1,67 … 3,53, ~0,65 m arayla; y≈1,18 | kap 0,22 × 0,22 |

- **Kuzey — pişirme hattı:** iki ızgara bugünkü hacimlerin altında kalır. Üstte tek parça
  kırmızı davlumbaz (alt kenarı ~2,0 m), yanında dekor fritöz.
- **Batı — servis alanı:** banko bugünkü yerinde, önü kırmızı panel + krom şerit. Her yuvanın
  üstünde bir "SİPARİŞ" pencere modülü durur: iki dikme, üst tabela, fiş rayı. Önünde numaralı
  zil ve 1-2-3 tabelası, üstünde sarı tepsi (servis tahtası). Müşteri koridoru ~0,75 m'dir,
  koridorun arkasındaki duvar diner olarak giydirilir (turkuaz alt panel + kırmızı şerit).
- **Güney — malzeme duvarı:** 9 kap bugünkü soketlerinde. Her kabın üstünde tavandan inen
  renkli bir dağıtıcı tüpü ve malzeme ikon kartı.
- **Orta — montaj adaları:** bugünkü masalar cartoon tezgâh olarak yeniden giydirilir.
- **Doğu — hazırlık:** buzdolabı, lavabo, çöp kovası, duvar rafı, tencereler.
- **Tavan:** yuvarlak lambalar, birkaç boru ve havalandırma kanalı.

## 3. Üretim akışı — her obje için

1. **Görsel** (§4): kullanıcı Gemini'de 2×2 dönüş sayfasını üretir ve dosya yolunu verir.
   Basit biçimli objelerde görsel isteğe bağlıdır (§6 "Görsel" sütunu): ajan görünümü paletten
   önerir, onay önizlemeden alınır.
2. **Dönüştürme:** ajan `.jfif` görseli PNG'ye çevirir.
3. **Model:** ajan Blender MCP ile modeller. Her obje `blender/BurgerKitchen/scripts/` altında
   kendi betiğidir (`a1_griddle.py` gibi); ortak palet, kurucu yardımcılar, bake ve FBX dışa
   aktarma `bk_common.py`'dedir. Gerçek ölçü, alt-orta pivot (duvar objesinde arka-alt-orta),
   ön yüz +Z, UV2 burada verilir.
4. **Önizleme:** ajan objeyi kullanıcının BurgerKit indirme klasörüne render eder (§7 ad).
   Kullanıcı onaylar; onaylamazsa betik düzeltilir, yeniden render edilir.
5. **Unity:** ajan FBX + dokuyu `Assets/Modes/Burger/KitchenKit/` altına alır (§7), materyal,
   collider, prefab ve import ayarlarını kurar.
6. **Sahneye koyma:** eski objenin **yalnız renderer'ı** değişir; collider'ı ve oyun hacimleri
   yerinde kalır.
7. **Işık:** sahneye konan dekor Static işaretlenir ve renderer'ı `Receive GI = Lightmaps` yapılır
   (kit prefabı Light Probes ile gelir); ardından sahne bake edilir.

## 4. Görsel üretim — promptlar

Promptlar İngilizcedir: görsel modelleri İngilizcede daha tutarlı çalışır. Her obje promptu
**[STİL] + [OBJE] + [GÖRÜNÜM]** bloklarından kurulur; [OBJE] satırı §6'daki tanım ve ölçüdür.

### 4.1 [STİL] bloğu — her promptun başına, aynen

```
Stylized cartoon 3D game asset for a family-friendly VR burger-restaurant game.
Chunky, rounded, slightly exaggerated proportions; soft bevelled edges; simple
readable shapes, no tiny details; clean hand-painted look with soft color
gradients; matte surfaces; metal parts look like light blue-grey painted steel,
not mirror chrome. Palette: tomato red #D8342C, cream white #F5F0E6, warm wood
#A8662F, steel #B8C4CE, charcoal #2B2B2B, mustard yellow #F4BE2C, diner teal #2FA49B.
```

### 4.2 [GÖRÜNÜM] — tek görselde 2×2 dönüş sayfası

Dört görünüm tek üretimde çıktığı için obje görünümler arasında daha az değişir. **2×2 ızgarada
tam dört görünüm olur, başka hiçbir şey olmaz. Dört görünüm de aynı boyuttadır.**

```
Show this single object as a 2x2 orthographic turnaround sheet on a pure white
background. Top-left: FRONT view. Top-right: LEFT side view (the camera stands at
the object's left side, so the object's front faces the LEFT edge of the image).
Bottom-left: BACK view. Bottom-right: RIGHT side view (the object's front faces the
RIGHT edge of the image). All four views show exactly the same object with
identical colors, proportions and details, at identical scale, with the bottom of
the object on the same horizontal line. Camera perfectly level at the object's
mid-height, orthographic, no perspective, no tilt. Soft even studio light, no cast
shadows, no reflections, no floor, no text, no labels, no other objects.
Square image, high resolution.
```

### 4.3 Düz dokular ve 2D tabelalar

**Döşenen doku** (kare, 1024 px; prompt sonuna her zaman `seamless tileable texture, flat
orthographic top-down, even lighting, no perspective, no shadows` eklenir):

| Doku | Prompt gövdesi |
|---|---|
| Duvar karosu | `cartoon cream white subway wall tiles with dark charcoal grout, slightly rounded tile edges, hand-painted game texture, 4 tiles wide` |
| Zemin daması | `cartoon black and white checkerboard floor tiles, slightly worn hand-painted look, 4x4 squares` |
| Ahşap dolap kapağı | `cartoon warm wood cabinet door panel with a raised frame and simple round knob, hand-painted, front view` (döşenmez, tek kapak) |
| Ahşap | `cartoon warm wood planks, simple stylized grain, hand-painted` |
| Tezgâh üstü | `cartoon light grey speckled countertop surface, very subtle pattern` |
| Diner duvarı | `cartoon retro diner wall, teal lower half and cream upper half with a red stripe, hand-painted` |

**2D tabela / poster** (düz quad'a basılır):

| Görsel | Prompt gövdesi |
|---|---|
| Burger posteri | `cartoon retro poster of a giant stacked cheeseburger with flying ingredients, bold flat colors, dark purple background, no text, portrait 2:3` |
| "ORDER" tabelası | `retro diner sign reading "ORDER" in chunky yellow letters with dark outline on a red panel, front view, flat, 4:1` |
| Numaralar 1-2-3 | `big chunky cartoon number "1" in white on a red rounded square, flat, front view` (2 ve 3 için tekrar) |
| Malzeme ikonları ×9 | `cartoon icon of a [raw beef patty / sesame burger bun / tomato slice / lettuce leaf / cheese slice / bacon strip / onion ring / pickle slice / ketchup bottle], bold outline, flat colors, white rounded-square card, centered` |
| Menü panosu | `cartoon diner menu board with burger and fries illustrations, chalkboard style, no readable text, landscape` |

Okunması gereken yazı (SİPARİŞ, rakamlar) modelin dokusuna **gömülmez**: model düz panelle
üretilir, yazı ayrı tabela olarak üstüne basılır.

## 5. Palet ve tek materyal

Kitteki her obje tek materyali (`M_BK_Palette`, URP Lit, smoothness 0,15, metallic 0) ve tek
paleti (`BK_Palette.png`) paylaşır. Quest'te bütün mutfak böylece tek materyal kalır.

| Öğe | Karar |
|---|---|
| Doku | `BK_Palette.png`, 256 × 64; renk başına 16 px genişliğinde bir sütun |
| Sütun içi | Dikey geçiş: alt koyu, **%60 yükseklikte temel renk**, üst açık |
| UV | Her parça yüksekliğine göre sütunun içine yazılır — gölgelendirme UV'den gelir |
| Sütun sırası | **Yalnız sona eklenir**; var olan sütun yer değiştirmez, yoksa üretilmiş UV'ler kayar |
| Çok renkli obje | Parça parça farklı sütuna bakar; yeni materyal açılmaz |

## 6. Kit listesi

Hedefler Quest içindir; "Üçgen/adet" tek kopyanın bütçesidir. "Görsel" sütunu, 2×2 dönüş
sayfasının gerekli mi olduğunu söyler.

**A (pişirme hattı) ve B (tezgâh, mobilya) sahnededir** — betikleri `blender/BurgerKitchen/scripts/`
(`a*`, `b*`, `f5_trash_bin`). Bu objelerden kalan iş yalnız kapanıştaki static + bake ve saha
doğrulamasıdır (§9).

### C — Doğu duvarı küçük cihazlar

**Sahnededir** (`c_appliances`: mikrodalga, tek gözlü ocak).

### D — Servis alanı

**Sahnededir** (`d1_order_window` — 1-2-3 kartları dahil, `d2_bell`, `d4_door`). Zil ve ORDER
penceresi dekordur. Kapı (`Furniture/DinerDoor`, `BurgerDinerDoor`) müşteri yaklaşınca yalnız
görsel olarak açılır; arkasında duvar boşluğu ve kapalı giriş holü vardır (`g_shell.vestibule`,
oyun alanı dışı), müşteri yolu `WP_0` holün içindedir ve `WP_1` kapının tam ortasından geçer
(kanatların arasından). Müşteri bekleme noktaları (`CustomerAnchor`) pass duvarından 0,65 m
geridedir (x −4,2): iri kafalı görünümler öne 0,5 m taşar.

### E — Malzeme duvarı

**Sahnededir** (`e1_dispenser`, `e2_ketchup`). Dağıtıcılar yalnız görünüm olarak Cook'd Up tarzı
makinedir; alma mekaniği aynıdır, düğme yoktur. Sahnede her dağıtıcının `Sample_*` örnekleri
+0,066 m, `GripSocket`'i +0,074 m pede kaldırılmıştır (sahne override'ı; ortak `NO_dispenser_*`
prefabları değişmedi) — model yeniden üretilirken ped yüksekliği (`PAD_TOP`) değişirse bu kaydırma
da güncellenir.

### F — Elde tutulanlar (ortak `NO_*` prefablar)

**Sahnededir** (`f_handhelds`: bıçak, spatula, servis tepsisi, kesme tahtası; çöp kovası
`f5_trash_bin`). Görsel ortak `NO_knife` / `NO_spatula` / `NO_board` / `NO_cutting_board`
prefablarında `CartoonVisual` çocuğudur, eski `Visual`'ın yalnız renderer'ı kapalıdır — o
prefabları kullanan **her** Burger arenası cartoon görünür. Model ölçüleri prefab kökünün yerel
birimindedir (bıçak ve spatula kökü 1,5 ölçekli); sap eski görselin sapıyla aynı yerdedir, yoksa
el pozu kayar.

### G — Kabuk ve duvar detayları

**Sahnededir** (`g_shell`: zemin, duvarlar, tavan, süpürgelik, korniş, batı duvarının diner paneli;
`g_details`: 12 tavan lambası, menü panosu, poster). Görsel tavan 3,2 m'dir; 12 pişmiş spot
3,05 m'ye indirilip gücü (3,05/4,85)² ile düşürülmüştür, davlumbaz bacası bu tavana göre kısadır.
Her lamba kendi pişmiş spotunu taşır (`BK_G_CeilingLamp` içinde `Light`, 120° koni, yumuşak
gölge): lamba taşınınca ışığı da gider, bake yeniden alınır. Dolgu ışığı tavanın hemen altındaki
pişmiş alan ışıklarıdır (`Kitchen_Lights/Area_*`, mutfakta 2×2, dinerde 1; şiddet 1,2). Kapalı
odaya gökyüzü ortam ışığı girmez; aydınlık, gölgesiz görünüm alan ışığından ve 3 sekmeden gelir —
alan ışığı kaldırılırsa duvar üstleri ve dolap önleri kararır. Eski kabuğun
collider'ları yerindedir.

### Yerleşim A ve diner

**Sahnededir.** Ortada iki çalışma adası (`b7_island`, `Furniture/Islands/Island_1..2`: tek kutu
collider, `Obstacle` layer, `CounterSurface`); eski orta mobilya (uzun masa, orta masa, 4 L ada)
**pasif** (silinmedi). 6 kesme tahtası + 6 bıçak + 6 spatula adalarda, ada başına 3 istasyon.
Üçüncü çöp kovası ada 1'in güney ucunda, tepsi rafı güneybatı köşesinde. Diner (`i_diner`,
`Kitchen_Props/Diner`): oyun alanının dışında (x < −4,5), yalnız görüntü, collider yok; 3 loca,
müzik kutusu, kapı güney diner duvarında; müşteri yolu kapıdan başlar (`WP_0..2` taşındı). Diner
üstünde 3 pişmiş spot (`Kitchen_Lights/Spot_Diner_1..3`). Mutfakla diner arası duvardan duvara
kapalıdır: SİPARİŞ çerçevesi ve servis bankoları ortada, uçlar ve çerçevenin üstü kabuğun "pass"
duvarıdır (`g_shell.pass_wall`, x −3,55..−3,43); oyun alanında kalan iki ucu
`Kitchen_Shell/PassWall_Colliders` (`Obstacle`) korur. Müşteri koridoru dinerin parçasıdır.

### H — Süs

Saat ve yangın söndürücü doğu duvarında, neon "BURGER" kuzey duvarında **sahnededir**
(`h_decor`; neon `M_BK_Glow`). Diner duvarlarında 4 çerçeveli poster (gece manzarası, patates,
milkshake, "OPEN") `g_details.DINER_ART`, mutfakta 5 kırmızı çerçeveli poster (maskot, sebzeler,
sosisli, ızgara, kola) `g_details.KITCHEN_ART` ile sahnededir. Etiket dokusunda 4 boş yer kalır;
diner kapı köşesi (patenli garson) ve diner kuzey duvarı (dondurma) için görsel bekleniyor. Kalan isteğe bağlı süsler (kepçe askısı, duvar rafı + tencereler,
sebze kasası, baharat şişeleri) seçilirse buraya yazılır.

**Sahnedeki kit:** ~75k görünür üçgen; materyaller palet, etiket, karo, lamba, parlama, dolap
ahşabı (+ ketçap efektleri). Tezgâhlar, adalar ve lavabo Cook'd Up stilindedir (`bk_cabinet.py`).
Lambalar ve tablolar ayrı sahne objeleridir; konumları sahnede elle ayarlanır.

## 7. Teslim — dosya adı ve yer

- **Kaynak görsel:** kullanıcı yolu verir, ajan PNG'ye çevirip `blender/BurgerKitchen/ref/`'e
  koyar (LFS). Etiket dokusu (`BK_Decals.png`) bu dosyalardan yeniden kurulur.
- **Blender:** `blender/BurgerKitchen/BurgerKitchen.blend`; betikler
  `blender/BurgerKitchen/scripts/` (`bk_common.py` + obje başına bir betik).
- **Önizleme render'ı:** `<ID>_<ad>_BLENDER.png`, kullanıcının BurgerKit indirme klasörüne.
- **Unity'ye giren:** `Assets/Modes/Burger/KitchenKit/` altında `Models/` (FBX), `Textures/`,
  `Materials/`, `Prefabs/`. Kit yalnız Burger modunundur; `_Shared`'a girmez
  (`Yapma-Listesi.md` "`_Shared` mi, kutu mu" testi). Klasör ilk dosyayla açılır, boş klasör
  açılmaz.
- **Ajanın yaptığı adımlar:**
  - Import ayarı: Scale 1, Generate Lightmap UVs açık, Read/Write kapalı.
  - Palet dokusu: bilinear, mipmap kapalı (mip seviyesi sütunları birbirine karıştırır), Android
    ASTC 6x6. UV sütunun ortasında durduğu için 16 px sütunda 6x6 blok komşu renge taşmaz.
  - Materyal: tek `M_BK_Palette` (§5).
  - Collider ve layer, prefab, static flag, light probe, bake.
- Yeniden üretim akışının kalıcı kaynağı `blender/BurgerKitchen/README.md`'dir; bu plan dosyası
  iş bitince silinir.

## 8. Bütçe (Quest 3)

- **Kitin toplam görünür üçgen hedefi ~75k.** Sahne uyarı sınırı 1M'dir, ama dekor ucuz
  kalmalı; oyuncu avatarları ve efektler de bu bütçeden yer.
- **Kit altı paylaşılan materyalle çizilir:** palet (`M_BK_Palette`), etiketler (`M_BK_Decals`),
  duvar karosu (`M_BK_Tiles`), lamba (`M_BK_Lamp`), parlama (`M_BK_Glow`), dolap ahşabı
  (`M_BK_CabinetWood`) — obje eklemek yeni materyal açmaz.
- **Tekrarlanan obje tek prefabdır** (zil ×3, çöp kovası ×3, tezgâhlar). Renk ya da ikon farkı
  (dağıtıcılar) ayrı küçük mesh'tir, ayrı materyal değil.
- **Lambalar emissive + bake.** Gerçek zamanlı ışık eklenmez. Davlumbaz altı ve servis
  penceresi ışığı lightmap'ten gelir.
- **Kontrol:** her grup sonunda `Tools > VortexArena > Arena > Sahne Bütçesini Ölç` ve
  `Engel Hacimlerini Denetle`.

## 9. Sıra

1. **Kalan süs:** diner kapı köşesine patenli garson, diner kuzey duvarına dondurma posteri
   (görsel bekleniyor; etiket dokusunda yer var, §6 H). Eklenince dekor Static + bake (§3, 7. adım),
   bu plan dosyası silinir.
