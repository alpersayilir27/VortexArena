# Admin Arayüzü Görsel Yenileme — Tasarım Brief'i

> Bu dosya bir tasarım AI'ına (Gemini, Recraft, ChatGPT vb.) verilecek **üretim brief'idir**:
> tasarım dili + üretilecek parçaların envanteri + her parçanın hazır prompt'u.
> Üretilen dosyalar `Assets\_Shared\App\UI\Sprites\` altına atılır; import ayarı ve prefaba
> montaj kod tarafında yapılır (elle Unity işi gerekmez).

## 1. Bağlam

- Ürün: LBE VR PvP arena. Bu brief **masaüstü admin/operatör ekranının** görsel yenilemesi.
- Ekran: Windows, 1920×1080 referans çözünürlük. Panellerin **arkasında canlı oyun görüntüsü
  akar** → paneller yarı saydamdır. ⚠️ Saydamlık motorda verilir: assetler **OPAK** üretilir.
- Motor: Unity uGUI + TextMeshPro. **Tüm yazılar motordan çizilir** → assetlerde yazı OLMAZ.
- Stil referansı: esports/sci-fi skorboard (Portal Strike tarzı) — koyu camsı paneller,
  45° pahlı köşeler, kırmızı-mavi takım kimliği, turkuaz seçim vurgusu, altın/turuncu skor
  vurgusu, açık gri başlık bantları, paralelkenar (yatık kenarlı) butonlar.

## 2. Tasarım dili

| Rol | Renk | Kullanım |
|---|---|---|
| Zemin (derin) | `#12151C` | scrim, en koyu dolgu |
| Kart dolgusu | `#1B2029` | panel gövdeleri |
| Kenar/ayraç | `#2E3542` | ince çizgiler |
| Metin (parlak) | `#F5F7FA` | başlıklar, değerler |
| Metin (soluk) | `#9AA4B2` / `#6E7A8A` | ikincil metin, kolon başlığı |
| Takım kırmızısı | `#D93333` | sol kenar, kırmızı takım |
| Takım mavisi | `#3366E6` | sağ kenar, mavi takım |
| Seçim/aksan | `#3EC6DB` (turkuaz) | aktif sekme, seçili kip |
| Vurgu | `#F2A33C` (turuncu/altın) | skor satırı, lider adı |
| Tehlike | `#C62C2C` | KAPAT, at, iptal |

Biçim dili: 45° pahlı köşeler (~20 px @1080p) · paralelkenar butonlar (~15° yatıklık) ·
1-2 px açık gri kontur (`#AEBECF` ~%40) · kenarlardan içeri eriyen takım gradyanları ·
başlık bandının orta-altında küçük aşağı chevron.

## 3. Üretim kuralları (her asset için geçerli)

1. **Yazı, harf, rakam, watermark YOK.** (Yazılar TMP'den gelir; içinde yazı olan asset çöptür.)
2. **Düz karşıdan, dik açılı, kadrajı dolduran** görsel — eğik duran "mockup" değil.
3. **Saf siyah zemin** üstüne üret, sonra arka planı temizle (şeffaf PNG olarak teslim).
   Araç doğrudan şeffaf PNG verebiliyorsa (Recraft, ChatGPT) daha iyi.
4. **Silüetin dışına taşan gölge/parıltı YOK** — dış gölge arka plan silmeyi bozar.
5. Opak üret (yarı saydamlık motorda verilir), PNG, aşağıdaki tablodaki minimum çözünürlükte.
6. **Stil tutarlılığı:** önce paneli üret ve beğen; diğer her parçayı AYNI oturumda,
   panel görselini referans göstererek "same style as this panel" diye iste.
7. **Her parça AYRI görsel** — tek sayfaya kolaj/kit üretme (kesmek piksel kirliliği bırakır).
8. Oranı birebir tutturamazsan en yakın oranda üret — karta oturtma bizde.

## 4. Element envanteri

### Faz 1 — İstatistik paneli (öncelik)

| Dosya adı | Ekrandaki boyut | Üretim boyutu (oran) | Ne |
|---|---|---|---|
| `PanelBG.png` | 1265×660 | ≥1600 px genişlik (~2:1) | Panel gövdesi: başlık bandı + chevron + kırmızı/mavi kenarlar DAHİL tek parça |
| `BtnDark.png` | 110×34 · 160×36 (çok yerde) | 1024×256 (4:1) | Koyu lacivert paralelkenar buton (pasif/normal) |
| `BtnRed.png` | 110×34 | 1024×256 (4:1) | Kırmızı buton (KAPAT, tehlikeli işlem) |
| `BtnCyan.png` | 160×36 | 1024×256 (4:1) | Turkuaz parlayan buton (seçili sekme/kip) |
| `RowPlate.png` (ops.) | ~1200×40 | 2048×128 (16:1) | Tablo satır plakası (çok geniş ince bar) |

**`PanelBG.png` prompt'u:**
```
Flat 2D video game UI panel background, dark slate blue-gray glassy surface,
wide rectangle with chamfered 45-degree cut corners, a lighter gray header
strip across the full top edge with a small downward triangle notch at the
top center, left edge glowing dark red gradient fading inward, right edge
glowing dark blue gradient fading inward, thin light-gray outline around the
panel, very subtle faint tech grid texture, straight-on orthographic view,
panel fills the entire frame, isolated on a pure black background, no text,
no letters, no icons, no watermark, clean sharp edges, sci-fi esports
scoreboard style
```

**Buton prompt'u** (renk kelimesini değiştirerek üç kez kullan —
`dark navy` → `deep crimson red` → `bright glowing cyan turquoise`):
```
Flat 2D video game UI button, wide dark navy rectangle with slightly slanted
parallelogram sides, thin light-gray outline, subtle vertical gradient,
straight-on orthographic view, button fills the frame, isolated on pure black
background, no text, no icons, same sci-fi esports style as the panel above
```

**`RowPlate.png` prompt'u:**
```
Very wide thin dark charcoal bar with slightly slanted ends, thin lighter
outline, subtle glassy look, flat 2D game UI element, straight-on, fills the
frame, pure black background, no text, same sci-fi esports style as the panel
```

### Faz 2 — HUD bantları (üst skor bandı + alt kamera şeridi)

| Dosya adı | Ekrandaki boyut | Üretim boyutu (oran) | Ne |
|---|---|---|---|
| `TopBarPlate.png` | ~620×70 | 2048×256 (8:1) | Üst orta plaka: ortada süre/faz alanı, sol kanat kırmızıya sağ kanat maviye çalar |
| `BottomBarPlate.png` | ~700×90 | 2048×256 (8:1) | Alt kamera şeridi zemini: pahlı üst köşeli yamuk plaka |

```
Flat 2D esports game HUD top bar plate, wide dark slate shape with angled
ends, slightly raised center section for a timer, left wing tinted deep red,
right wing tinted deep blue, thin light outline, straight-on, fills the
frame, pure black background, no text, no icons, same style as the panel
```
```
Flat 2D esports game HUD bottom bar plate, wide dark slate trapezoid with
chamfered top corners, thin light outline, straight-on, fills the frame,
pure black background, no text, no icons, same style as the panel
```

### Faz 3 — Tercihler paneli parçaları

| Dosya adı | Ekrandaki boyut | Üretim boyutu (oran) | Ne |
|---|---|---|---|
| `ToggleOff.png` | 28×28 | 256×256 (1:1) | Pahlı köşeli boş kare (kapalı anahtar) |
| `ToggleOn.png` | 28×28 | 256×256 (1:1) | Aynı kare, içi turkuaz dolu (açık anahtar) — işaret glifi YOK |
| `StepArrow.png` | 32×32 | 256×256 (1:1) | İçinde sola bakan dolu ok olan küçük koyu buton (sağ için aynalanır) |

Açılır liste zemini için ayrı görsel GEREKMEZ (düz koyu renk yeter); buton zeminleri
`BtnDark`/`BtnCyan`'dan gelir.

### Faz 4 — İkonlar (istatistik paneli)

İkonlar **saf beyaz silüet** olarak üretilir, renk motorda `Image.color` tint'inden verilir
(skull ile aynı kural). Ekranda 24–30 px çizildikleri için **kalın gövdeli** olmalılar: ince
çizgili bir ikon bu boyutta örneklemeye düşer ve noktalı/kırık görünür. Import: Sprite/Single,
mipmap açık, Trilinear, max 128 px, sıkıştırmasız (montaj kod tarafında).

| Dosya adı | Ekrandaki boyut | Üretim boyutu | Ne | Nerede |
|---|---|---|---|---|
| `calibrate.png` | 30×30 | 512×512 (1:1) | Kalibrasyon: köşe parantezli hedef çerçevesi, ortasında dolu elmas | `AdminStatsPanel` → `BottomBar/CalibrateAll/Icon` (sol kenar) |
| `scale.png` | 30×30 | 512×512 (1:1) | Gövde ölçeği: dikey çift başlı ok, uçlarında kısa çubuk | `AdminStatsPanel` → `BottomBar/MeasureAll/Icon` (sağ kenar — iki düğme aynalıdır) |
| `crosshair.png` | 24×24 | 512×512 (1:1) | Öldürme: kalın halkalı nişangâh, dört kalın tik, ortada dolu nokta | Tablo başlıkları (`IconKills`) + admin kartı — aynı adla üstüne yazılırsa GUID korunur, referanslar kendiliğinden yenilenir |

Ortak kurallar: tek parça, ortalanmış, tuvalin ~%80'ini dolduran, **kalın** (en ince çizgi
tuval genişliğinin en az %8'i), düz karşıdan, gölge/parıltı/gradyan YOK, yazı YOK, saf beyaz
(`#FFFFFF`) üstüne saf siyah zemin (ya da doğrudan şeffaf PNG). Yeni ikonlar mevcutlarla **aynı
oturumda**, aynı kalınlık ve aynı köşe dilinde üretilir. İkonun kendisi RENKSİZ üretilir — ton
motorda verilir.

**`calibrate.png` prompt'u:**
```
Flat 2D game UI icon, pure solid white silhouette on a pure black background,
a square viewfinder frame made of four thick L-shaped corner brackets, with a
solid filled diamond (or map-pin) marker exactly in the center, very bold
uniform stroke thickness, minimal geometric shapes, centered, icon fills
about 80% of the frame, straight-on orthographic view, no gradients, no
shadows, no glow, no outline, no text, no letters, single-color pictogram
```

**`scale.png` prompt'u:**
```
Flat 2D game UI icon, pure solid white silhouette on a pure black background,
a simple standing human figure (round head, solid body) on the left, and a
tall vertical double-headed arrow with a short horizontal end bar at top and
bottom on the right, showing height measurement, very bold uniform stroke
thickness, minimal geometric shapes, centered, icon fills about 80% of the
frame, straight-on orthographic view, no gradients, no shadows, no glow, no
text, no letters, single-color pictogram
```

**`crosshair.png` prompt'u:**
```
Flat 2D game UI icon, pure solid white silhouette on a pure black background,
a bold gun crosshair reticle: one thick ring, four thick tick marks pointing
outward at top, bottom, left and right, and a solid filled dot in the center,
very bold uniform stroke thickness, minimal geometric shapes, centered, icon
fills about 80% of the frame, straight-on orthographic view, no gradients, no
shadows, no glow, no thin lines, no text, single-color pictogram
```

### Var olanlar (ÜRETME)

`skull` (ölüm) · `crosshair` (öldürme) · `calibrate` · `scale` · `settings` (dişli dekor) —
beyaz, motor içinde renklendiriliyor. İleride istenirse aynı tarzda (saf beyaz, siyah zeminde,
tek parça, kalın): göz (POV), kuş (kuş bakışı), mikrofon, çarpı (at/kick), çift ok (takım değiş).

## 5. Teslim

- Klasör: `Assets\_Shared\App\UI\Sprites\` — dosya adları YUKARIDAKİ TABLODAKİ gibi
  (`PanelBG.png`, `BtnDark.png`, `BtnRed.png`, `BtnCyan.png`, …).
- Arka planı temizlenmiş şeffaf PNG.
- Sonrası kod tarafında: import ayarları + prefaba montaj + saydamlık/yerleşim ayarı.

## 6. Araç önerileri

| Araç | Uygunluk |
|---|---|
| **Gemini (Nano Banana)** | Ana öneri: referans görselle stil eşleme çok iyi — paneli üret, sonra her parçayı panel görselini yükleyerek iste. Şeffaflık vermez → siyah zemin + temizlik |
| **Recraft** (recraft.ai) | Bu işin özel aracı: oyun UI asseti odaklı, **gerçek şeffaf PNG** üretir, stil seti ile parçalar arası tutarlılık kurar |
| **ChatGPT (gpt-image)** | Prompt sadakati yüksek, **şeffaf PNG destekler** |
| **Midjourney** | Doku kalitesi en yüksek; şeffaflık yok, stil sürekliliği `--sref` ister |
| **Google Stitch** | App/web ekran mockup'ı üretir — oyun sprite'ı VERMEZ; yalnız ilham/keşif için |
| **Google Flow** | Video aracı (Veo) — bu iş için uygun değil |

## 7. Kabul kontrolü (her görsel için)

- [ ] İçinde yazı/harf/rakam yok
- [ ] Düz karşıdan, kadrajı dolduruyor, perspektif/masa gölgesi yok
- [ ] Silüet dışı piksel/gölge yok (şeffaf alan temiz)
- [ ] Köşe pahları net, kontur ince ve düzgün
- [ ] Zemin koyuluğu palete yakın (`#1B2029` civarı — ton ince ayarı motorda yapılabilir)
