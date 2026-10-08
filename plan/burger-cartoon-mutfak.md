# Burger mutfağı — cartoon kit ile yeniden giydirme (plan)

Burger modunun mutfağı, parça parça üretilen cartoon modellerle baştan giydirilir. Hedef görünüm
sevimli, tıknaz, sıcak renkli bir mutfaktır: beyaz karo duvar, siyah-beyaz dama zemin, kırmızı
tavan ve vurgular, ahşap alt dolaplar. Servis tarafı "ORDER" pencereli bir bankodur; müşteri
bankonun arkasında durur, numaralı zil ve fiş rayı vardır. Akış: her obje GPT/Gemini ile **çok
açıdan** çizilir, Tripo'nun *Çok Açılı Görsellerden 3D* girişiyle modele çevrilir. Ölçek, pivot,
UV2, materyal, collider, prefab, yerleşim ve bake ajan tarafından (Blender MCP + Unity MCP)
yapılır. Henüz **hiçbir şey üretilmedi**.

## 0. Değişmez kurallar

- **Oyun mantığı değişmez.** Kod, protokol, `PROTOCOL_VERSION` ve `<Sahne>_objects.json`
  değişmez. Bu iş yalnız görsel giydirmedir.
- **Oyun hacimleri yerinde kalır, görsel hacme oturtulur.** `GrillZone`, `CounterSlot_N` ve
  servis tahtası, dağıtıcıların `GripSocket`'i, `Stack`/`CargoAnchor` ve `CustomerPath`
  waypoint'leri korunur. Bir hacim taşınacaksa `Docs/Gelistirici/Yemek-Kitabi.md` "Bir oyun
  istasyonunu ikinci bir mekana kurmak" kurallarına uyulur: hacim görselin çocuğudur, kök
  `rot 0`/`scale 1` kalır.
- **Malzemeler değişmez:** köfte, ekmek, domates vb. bugünkü paketten gelir.
- **Kabuk Tripo'dan gelmez.** Zemin, duvar, tavan ve uzun tezgâh gövdeleri düz geometriye
  döşenen dokudur. Tripo büyük düz yüzeyi dalgalı üretir ve ölçüye uymaz.
- **Tripo'nun kendi materyali ve dokusu sahneye girmez.** Her model Blender'dan FBX + ayrı doku
  olarak geçer (`Yapma-Listesi.md` GLB maddesi).
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
**Tezgâh çalışma yüzeyi bugün ~1,04 m**dir; bütün eşya hacimleri bu yüksekliğe göre kurulu
(bkz. §9 karar 1).

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
  üstünde bir "ORDER" pencere modülü durur: iki dikme, üst tabela, fiş rayı. Önünde numaralı
  zil ve 1-2-3 tabelası, üstünde sarı tepsi (servis tahtası). Müşteri koridoru ~0,75 m'dir,
  koridorun arkasındaki duvar diner olarak giydirilir (bkz. §9 karar 3).
- **Güney — malzeme duvarı:** 9 kap bugünkü soketlerinde. Her kabın üstünde tavandan inen
  renkli bir dağıtıcı tüpü ve malzeme ikon kartı.
- **Orta — montaj adaları:** bugünkü masalar cartoon tezgâh olarak yeniden giydirilir.
- **Doğu — hazırlık:** buzdolabı, lavabo, çöp kovası, duvar rafı, tencereler.
- **Tavan:** yuvarlak lambalar, birkaç boru ve havalandırma kanalı.

## 3. Üretim akışı — her obje için

1. **Görsel üret** (§4). Ön görünüm zorunludur. Simetrik objede ön + sol yeter; arkası farklı
   objede (buzdolabı, davlumbaz, kapı) arka da eklenir. Sağ yalnız asimetrik objede gerekir.
2. **Görselleri kontrol et** (§4.6 listesi). Tutarsız görünüm Tripo'da bozuk mesh demektir;
   kredi harcamadan önce elenir.
3. **Tripo'da üret** (§5). Çok açılı giriş, ön + diğer görünümler.
4. **Tripo'da incele** ve gerekiyorsa Retopo / Smart Low Poly ile yüz sayısını düşür
   (hedef §6 tablosunda).
5. **İndir.** GLB ya da FBX fark etmez, hepsi Blender'dan geçer. Dosya adı §7'deki gibi.
6. **Ajana ver.** Ajan şunları yapar: Blender'da gerçek ölçüye ölçekleme, pivotu alt-orta
   noktaya alma (duvar objesinde arka-alt-orta), ön yüzü +Z'ye çevirme, gerekirse azaltma, UV2
   üretme, FBX + doku olarak kit klasörüne yazma. Unity'de materyal, collider, prefab, yerleşim,
   static flag ve bake de ajandadır.
7. **Toplu bake + bütçe ölçümü** her grup bitince bir kez yapılır (§8).

> **Gerçek boyut Tripo'ya aktarılmaz.** Çok açılı görsel yalnız **oranı** (genişlik : derinlik :
> yükseklik) doğru taşır; Tripo çıktısı keyfi ölçekte gelir. Mutlak ölçüyü Blender'da ajan
> §6'daki ölçülere göre verir. Oranın doğru çıkması için görünümler **aynı ölçekte** ve aynı
> taban çizgisinde olmalı. Promptta ölçüyü santimetreyle yazmak da oranı tutturmaya yardım eder.

## 4. Görsel üretim — promptlar

Promptlar İngilizcedir: görsel modelleri İngilizcede daha tutarlı çalışır. Her obje promptu şu
bloklardan kurulur: **[STİL] + [OBJE] + [GÖRÜNÜM]**. Pilotta GPT ile Gemini denenir, **tüm kit
için biri seçilir ve değiştirilmez**: iki modelin "cartoon"u birbirine benzemez.

### 4.1 [STİL] bloğu — her promptun başına, aynen

```
Stylized cartoon 3D game asset for a family-friendly VR burger-restaurant game.
Chunky, rounded, slightly exaggerated proportions; soft bevelled edges; simple
readable shapes, no tiny details; clean hand-painted look with soft color
gradients; matte surfaces; metal parts look like light blue-grey painted steel,
not mirror chrome. Palette: tomato red #D8342C, cream white #F5F0E6, warm wood
#A8662F, steel #B8C4CE, charcoal #2B2B2B, mustard yellow #F4BE2C, diner teal #2FA49B.
```

### 4.2 [GÖRÜNÜM] — yöntem A: tek görselde 2×2 dönüş sayfası (önerilen)

Dört görünüm tek üretimde çıktığı için obje görünümler arasında daha az değişir. Sonra görsel
dört kareye kırpılır.

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

### 4.3 [GÖRÜNÜM] — yöntem B: önce ön, sonra aynı görselden yan/arka

Önce yalnız ön görünüm:

```
FRONT view only. Orthographic, camera perfectly level at the object's mid-height,
object centered with about 10% empty margin on every side, pure white background,
soft even studio light, no cast shadow, no reflections, no text, nothing else in
the image. Square image.
```

Sonra ön görseli **ekleyerek** yan görünüm (arka için "rotate 180° / BACK view"):

```
This is the front view of an object. Generate the LEFT side view of exactly the
same object: rotate it 90° so we see its left side, with its front facing the LEFT
edge of the image. Keep identical colors, materials, proportions and details,
identical scale, the same bottom line, the same white background and lighting.
Orthographic, no perspective, no shadow, no text.
```

> ⚠️ **Sol / sağ yönü:** Tripo'da "Sol", objenin **kendi solu**dur. Bu yüzden sol görünümde
> objenin önü **görselin soluna** bakar. Pilotta ilk model aynalanmış çıkarsa sol ile sağ
> görselleri yer değiştirilir ve bu kural tüm kit için o yönde sabitlenir.

### 4.4 [OBJE] satırı

§6 tablosundaki İngilizce tanım, ölçüyle birlikte yazılır. Örnek (ızgara):

```
A chunky cartoon flat-top griddle station: red metal cabinet body with two small
doors and big round knobs on the front, a thick black cast-iron cooking plate on top
with a low steel splash rim on the back and sides, short stubby steel legs. About
120 cm wide, 75 cm deep, 100 cm tall.
```

Birden çok renkte kullanılacak objede (dağıtıcı tüpü, zil) obje **beyaz/açık gri**
üretilir. Unity'de materyal rengiyle boyanır; renkli üretilirse her renk için ayrı kredi gider.

### 4.5 Düz dokular ve 2D tabelalar (Tripo'ya girmez)

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

Okunması gereken yazı (ORDER, rakamlar) modelin dokusuna **gömülmez**: Tripo yazıyı bulanıklaştırır.
Model düz panelle üretilir, yazı ayrı tabela olarak üstüne basılır.

### 4.6 Tripo'ya göndermeden önce görsel kontrolü

- Tüm görünümlerde **aynı obje** var mı? Kapak, düğme, ayak sayısını say.
- Taban çizgisi ve yükseklik görünümler arasında aynı mı?
- Perspektif yok mu? Ön görünümde üst yüz neredeyse hiç görünmemeli.
- Arka plan düz beyaz mı? Gölge, zemin, el ya da ikinci obje var mı?
- Işık düz mü? **Güçlü ışık-gölge dokuya pişer:** Tripo görseldeki gölgeyi renk sanar, Unity'nin
  ışığı üstüne ikinci kez gölge koyar.
- Kapılar ve çekmeceler kapalı mı? Cam varsa opak, renkli mi? Tripo saydamlığı üretemez.
- Yüzen ya da çok ince parça (tel ızgara, ince sap) var mı? Varsa kalınlaştır ya da çıkar.

## 5. Tripo ayarları

| Ayar | Değer | Neden |
|---|---|---|
| Giriş | *Çok Açılı Görsellerden 3D* — Ön (zorunlu) + Sol (+ Arka / Sağ gerekiyorsa) | Tek görselde arka ve derinlik tahmin edilir |
| Kalite | HD Model, AI Model **H3.1** | — |
| Parçalar Halinde Üret | **Kapalı** | Her parça ayrı mesh/materyal = fazladan draw call. Birleştirmeyi ajan yapar |
| 8K Doku | **Kapalı** | Quest'te doku en fazla 1024'e iner, 8K boşa kredidir |
| Üretim sonrası | Retopo / Smart Low Poly, hedef yüz sayısı §6'dan | Ham çıktı çok yoğundur |
| Smart UV | İsteğe bağlı | UV2'yi ajan Unity/Blender'da zaten üretir |
| Dışa aktarma | GLB ya da FBX, doku 2K yeterli | Blender'dan geçip FBX olarak girer |

- **Kredi:** ekranda HD üretim **55 kredi** gösteriyor. Hesapta 200 kredi var, yani ~3 model.
  Kitte Tripo'ya önerilen ~20 obje var (~1100+ kredi, retopo hariç). Bu yüzden §6'da her objeye
  "önerilen kaynak" yazıldı: kutu biçimli ve içi boş basit objeler Blender'da ajan tarafından
  bedavaya yapılır.
- **İçi boş hazneler** (çöp kovası, malzeme kabı, tepsi): Tripo hazneyi sık sık doldurur.
  Bunlar Blender'da yapılır. Tripo denenecekse görünüm hafif yukarıdan, içini gösteren tek
  bir 3/4 görsel olur.
- **Üretimden sonra Tripo'da kontrol:** objeyi çevir; alt yüzü, arkayı, simetriyi ve delikleri
  kontrol et. Bozuksa yeniden üretmek yerine önce görseli düzelt.

## 6. Kit listesi

**Rol:** *oyun* = mevcut ağ nesnesi ya da hacmin görseli değişir, hacim aynı kalır. *dekor* =
yalnız görsel. **Kaynak:** T = Tripo, B = Blender (ajan), 2D = görsel + quad. **Öncelik:** P1
pilot ve oyun parçaları, P2 mutfağın karakteri, P3 süs. Ölçüler G × D × Y metredir. Yüz ve doku
sütunları Quest hedefidir.

### A — Oyun parçaları (giydirme)

| ID | Obje | Ölçü | Görünüm | Yüz | Doku | Rol | Kaynak | Öncelik | [OBJE] tanımı |
|---|---|---|---|---|---|---|---|---|---|
| A1 | Izgara ×2 | 1,20 × 0,75 × 1,00 (plaka üstü ~1,00) | ön+sol+arka | 6k | 1024 | oyun | T | P1 | §4.4'teki örnek |
| A2 | Malzeme kabı ×8 | 0,24 × 0,24 × 0,12 | — | 0,6k | 512 | oyun | B | P1 | (Blender: yuvarlak köşeli çelik kap, kalın kenar) |
| A3 | Sos dağıtıcı şişe | 0,08 × 0,08 × 0,22 | ön+sol | 0,8k | 512 | oyun | T | P2 | `a chunky cartoon red ketchup squeeze bottle with a yellow pointed cap` |
| A4 | Bıçak ×6 | 0,45 boy | ön (yassı yüz) + sol (kenardan) | 0,8k | 512 | oyun | T/B | P2 | `a chunky cartoon chef knife with a thick wide steel blade and a rounded warm wood handle with two rivets` |
| A5 | Spatula ×6 | 0,41 boy | ön + sol | 0,8k | 512 | oyun | T/B | P2 | `a chunky cartoon burger spatula with a wide flat slotted steel blade and a red rounded handle` |
| A6 | Servis tepsisi ×3 | 0,36 × 0,24 × 0,03 | — | 0,3k | 256 | oyun | B | P1 | (Blender: sarı, kalkık kenarlı tepsi) |
| A7 | Kesme tahtası ×6 | 0,30 × 0,21 × 0,03 | — | 0,3k | 512 | oyun | B | P2 | (Blender: ahşap, sapında delik) |
| A8 | Çöp kovası | 0,45 × 0,45 × 0,75, **ağzı açık** | — | 1,5k | 512 | oyun | B | P1 | (Blender: kırmızı pedallı kova, kapaksız) |

- A1'de **pişirme hacmi değişmez**: plakanın üstü hacmin içinde, ~1,00 m'de kalır. Gövdenin katı
  kutusuna `CounterSurface` konur.
- A4/A5 elde tutulur: sap konumu bugünkü `GripSocket`'e oturtulur, yoksa el pozu kayar. Ajan
  hizalar.
- A8 için hazne kuralları geçerlidir (`Yemek-Kitabi.md` istasyon bölümü, çöp kovası satırı):
  taban kutusu haznenin görünen tabanının birkaç mm üstünde olur, kenar kutuları ağız
  yüksekliğine kadar çıkar.
- Bu nesneler `Modes/Burger/Prefabs/` altındaki **ortak** prefablardır. Değişiklik bu prefabları
  kullanan **her** Burger arenasına yansır (bkz. §9 karar 4).

### B — Pişirme hattı

| ID | Obje | Ölçü | Görünüm | Yüz | Doku | Kaynak | Öncelik | [OBJE] tanımı |
|---|---|---|---|---|---|---|---|---|
| B1 | Davlumbaz | 3,40 × 0,90 × 0,70, alt kenarı 2,0 m | ön+sol | 3k | 1024 | T | P1 | `a big chunky cartoon red kitchen exhaust hood with rounded corners, a light steel trim band along the bottom edge, three vent grilles on the underside front, and a short square duct on top` |
| B2 | Fritöz | 0,50 × 0,75 × 1,04 + sepet | ön+sol | 4k | 1024 | T | P2 | `a chunky cartoon deep fryer: steel cabinet with a red front panel and a round dial, an oil well on top with two wire baskets hanging on the back rim` |
| B3 | Kepçe askısı | 1,00 × 0,15 × 0,50 | ön+sol | 2k | 512 | T | P3 | `a cartoon wall-mounted steel utensil rail with hanging ladle, whisk, tongs and a frying pan` |
| B4 | Boru ve kanal seti | modüler | — | — | — | B | P2 | (Blender: kalın kırmızı/gri borular, dirsekler) |

### C — Servis alanı

| ID | Obje | Ölçü | Görünüm | Yüz | Doku | Kaynak | Öncelik | [OBJE] tanımı |
|---|---|---|---|---|---|---|---|---|
| C1 | ORDER pencere modülü ×3 | 2,00 × 0,30 × 1,40 (tezgâh üstünden) | ön+sol | 3k | 1024 | T | P1 | `a cartoon retro diner order-window frame: two chunky red posts with steel caps, a top header panel (plain blank red panel, no text), a horizontal steel ticket rail under the header with three blank paper order tickets clipped on it` |
| C2 | Servis zili ×3 | Ø 0,12 × 0,10 | ön+sol | 0,6k | 512 | T | P1 | `a chunky cartoon service desk bell: shiny red dome on a round black base with a small steel push button on top` |
| C3 | Banko gövdesi | yuvalar boyunca, ön yüz | — | — | — | B | P1 | (Blender: kırmızı panel, krom şerit, açık gri tezgâh üstü) |
| C4 | Diner kapısı (müşteri girişi) | 1,00 × 0,10 × 2,10 | ön+arka | 2k | 1024 | T | P2 | `a cartoon retro diner swinging double door, red with a round porthole window in each leaf and steel kick plates` |
| C5 | Diner kanepesi | 1,20 × 0,60 × 1,00 | ön+sol | 2,5k | 1024 | T | P3 | `a cartoon retro diner booth seat with teal and cream vinyl upholstery and a chrome trim` (yalnız §9 karar 3 "evet" ise) |
| C6 | Kasa (yazar kasa) | 0,40 × 0,40 × 0,30 | ön+sol+arka | 1,5k | 512 | T | P3 | `a chunky cartoon retro cash register, red body with big round keys and a pop-up number display, cash drawer closed` |

- C1'in dikmeleri yuva **sınırlarına** oturur (z ≈ 1,19 ve −0,86 civarı). Servis tahtası
  ile slot hacmi arasına girmez.
- C1 tabelasındaki "ORDER" ve zil önündeki 1-2-3, §4.5'teki 2D tabela olarak basılır.
- Zil ve fiş rayı **dekordur**. Basınca çalan zil ya da sipariş tüpü yeni bir mekaniktir; ayrı
  iştir (bkz. §9 karar 2).

### D — Malzeme duvarı

| ID | Obje | Ölçü | Görünüm | Yüz | Doku | Kaynak | Öncelik | [OBJE] tanımı |
|---|---|---|---|---|---|---|---|---|
| D1 | Malzeme rafı / tezgâhı | 9 kap boyunca, üst ~1,04 | — | — | — | B | P1 | (Blender: ahşap alt dolap + tezgâh) |
| D2 | Dağıtıcı tüpü ×9 | Ø 0,25 × 1,20, alt ucu kabın ~0,4 m üstünde | ön+sol | 1,5k | 512 | T | P2 | `a chunky cartoon ingredient dispenser tube hanging from the ceiling: white cylindrical body with a clear-looking opaque light blue window strip, a ribbed collar and a round nozzle at the bottom` (beyaz üretilir, ajan malzemeye göre boyar) |
| D3 | Malzeme ikon kartı ×9 | 0,20 × 0,20 | — | — | — | 2D | P2 | §4.5 |

### E — Hazırlık ve genel dekor

| ID | Obje | Ölçü | Görünüm | Yüz | Doku | Kaynak | Öncelik | [OBJE] tanımı |
|---|---|---|---|---|---|---|---|---|
| E1 | Buzdolabı | 1,20 × 0,75 × 2,00 | ön+sol+arka | 4k | 1024 | T | P1 | `a chunky cartoon double-door commercial refrigerator, light steel body with rounded edges, two tall doors with big chrome bar handles, a small red badge on top, closed doors` |
| E2 | Lavabo ünitesi | 1,00 × 0,65 × 1,04 + musluk | ön+sol | 3k | 1024 | T | P2 | `a chunky cartoon kitchen sink cabinet: warm wood lower doors, light grey countertop with a deep steel sink basin and a tall curved goose-neck faucet` |
| E3 | Tencere ve tava | 0,30 – 0,40 | ön+sol | 1k | 512 | T | P3 | `a chunky cartoon steel stock pot with two side handles and a lid` / `a chunky cartoon black frying pan with a red handle` |
| E4 | Tabak yığını | Ø 0,25 × 0,20 | — | 0,5k | 256 | B | P3 | (Blender) |
| E5 | Sebze kasası | 0,50 × 0,35 × 0,30 | ön+sol | 1,5k | 512 | T | P3 | `a cartoon wooden crate full of big round tomatoes and lettuce heads` |
| E6 | Duvar rafı | 1,20 × 0,30 × 0,30 | — | — | — | B | P3 | (Blender: ahşap raf, çelik konsol) |
| E7 | Duvar saati | Ø 0,40 | ön | 0,5k | 512 | T | P3 | `a cartoon retro red wall clock with big white face and black hands` |
| E8 | Yangın söndürücü | Ø 0,18 × 0,55 | ön+sol | 0,8k | 512 | T | P3 | `a chunky cartoon red fire extinguisher with a black hose and a steel handle` |

### F — Tavan

| ID | Obje | Ölçü | Görünüm | Yüz | Doku | Kaynak | Öncelik | [OBJE] tanımı |
|---|---|---|---|---|---|---|---|---|
| F1 | Yuvarlak tavan lambası | Ø 0,50 × 0,12 | — | 0,4k | 256 | B | P1 | (Blender: krom çerçeve + emissive disk) |
| F2 | Havalandırma kanalı | modüler | — | — | — | B | P3 | (Blender) |

### G — Kabuk (Tripo yok)

Zemin, duvar, tavan, süpürgelik, montaj adaları, banko ve raf gövdeleri Blender/ProBuilder
geometrisidir. Üstlerine §4.5 dokuları döşenir. Kabuk prefaba girmez, sahnede kalır
(`Yemek-Kitabi.md` istasyon bölümü, 1. madde). Zemin collider'ı kalın levhadır (aynı bölüm,
7. madde).

## 7. Teslim — dosya adı ve yer

- **Görsel dosya adı:** `<ID>_<ad>_<görünüm>.png`, örnek `A1_griddle_front.png`,
  `A1_griddle_left.png`. Kaynak görseller repoya **girmez** (PNG LFS'te değil, git şişer);
  ortak bir klasörde tutulur.
- **Tripo çıktısı:** `<ID>_<ad>.glb` (ya da `.fbx`) → `blender/BurgerKitchen/source/`. `.glb` ve
  `.fbx` LFS'tedir.
- **Unity'ye giren:** `Assets/Modes/Burger/KitchenKit/` altında `Models/` (FBX), `Textures/`,
  `Materials/`, `Prefabs/`. Kit yalnız Burger modunundur; `_Shared`'a girmez
  (`Yapma-Listesi.md` "`_Shared` mi, kutu mu" testi). Klasör ilk dosyayla açılır, boş klasör
  açılmaz.
- **Ajanın yaptığı adımlar:**
  - Import ayarı: Scale 1, Generate Lightmap UVs açık, Read/Write kapalı.
  - Doku: Android ASTC 6x6, büyük obje en fazla 1024, küçük obje 512.
  - Materyal: URP Lit, yalnız albedo, smoothness ~0,15, metallic 0. Tripo'nun roughness ve
    metal haritaları alınmaz: cartoon görünümü gerçekçiye çeker ve bellek yer.
  - Collider ve layer, prefab, static flag, light probe, bake.
- `blender/BurgerKitchen/README.md`, ilk model girdiğinde bu akışın kalıcı kaynağı olarak yazılır.
  Bu plan dosyası iş bitince silinir.

## 8. Bütçe (Quest 3)

- **Kitin toplam görünür üçgen hedefi ~150k.** Sahne uyarı sınırı 1M'dir, ama dekor ucuz
  kalmalı; oyuncu avatarları ve efektler de bu bütçeden yer.
- **Her Tripo modeli tek mesh + tek materyal.** SRP Batcher farklı materyalleri kaldırır, ama
  "Parçalar halinde" çıktı obje başına 5-10 draw call'a çıkar.
- **Tekrarlanan obje tek prefabdır** (kap ×8, zil ×3, tüp ×9). Renk farkı materyal
  varyantıyla verilir, yeni mesh üretilmez.
- **Lambalar emissive + bake.** Gerçek zamanlı ışık eklenmez. Davlumbaz altı ve servis
  penceresi ışığı lightmap'ten gelir.
- **Kontrol:** her grup sonunda `Tools > VortexArena > Arena > Sahne Bütçesini Ölç` ve
  `Engel Hacimlerini Denetle`.

## 9. Açık kararlar (kullanıcı)

1. **Tezgâh yüksekliği:** bugün ~1,04 m, standart mutfakta 0,90 m. Hedef kitle çocuksa
   alçaltmak mantıklıdır, ama bütün hacimler, soketler ve eşya konumları kayar. Öneri: bu turda
   **1,04 kalır**, kit bu yüksekliğe göre üretilir.
2. **Zil / sipariş tüpü** dekor mu kalsın, yoksa mekaniğe mi dönüşsün? Öneri: dekor. Mekanik
   olursa protokol + sunucu işidir, önce `Docs/ArenaNet-Protokol.md`'ye yazılır.
3. **Diner arka planı:** müşteri koridoru dar (~0,75 m). Seçenekler: (a) batı duvarı diner
   dokusuyla giydirilir; (b) batı kabuk duvarı oyun alanı sınırının dışına itilir, arkada
   kanepeli bir diner salonu görünür. (b) daha etkileyicidir, ama oyuncu sınırın ötesinde boş
   alan görür ve gerçek duvara yürümeye heveslenebilir. Öneri: **(a)**.
4. **Ortak prefablar:** A grubu (`NO_*`, `BurgerStation`) başka Burger arenalarında da
   kullanılıyor. Öneri: hepsi cartoon'a geçer, tek görünüm olur. Alternatif: prefab varyantı, ama
   o durumda iki bakım yolu oluşur.

## 10. Sıra

1. **Pilot (3 obje, ~165 kredi):** A1 ızgara (ön+sol+arka), C2 zil (ön+sol), E1 buzdolabı
   (ön+sol+arka). Bu pilotla doğrulananlar: GPT mi Gemini mi, sol/sağ yönü, ölçek ve pivot akışı,
   materyal görünümü. Ajan bu üçünü sahneye koyar, **stil onayı** alınır.
2. **P1 (Blender kısmı ajanda, paralel):** kabuk dokuları + geometri, banko, raf, adalar, A2,
   A6, A8, F1, C3, D1. Ardından B1 ve C1 Tripo'dan gelir.
3. **P2:** A3-A5, A7, B2, B4, C4, D2-D3, E2.
4. **P3:** kalan süsler, posterler, menü panosu.
5. **Kapanış:** bake, bütçe ölçümü, engel denetimi, saha doğrulaması. Kalıcı bilgi
   `blender/BurgerKitchen/README.md`'ye ve gerekiyorsa `Yemek-Kitabi.md`'ye yazılır; bu dosya
   silinir.
