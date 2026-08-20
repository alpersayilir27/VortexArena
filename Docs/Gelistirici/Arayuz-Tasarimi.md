---
title: Arayüz tasarımı — 2D'yi nerede bulurum, nasıl düzenlerim
---

# Arayüz tasarımı (2D / UI)

Projedeki tüm arayüz **uGUI**'dir: `Canvas` + `TextMeshPro`. **UI Toolkit kullanılmıyor** —
projede tek bir `.uxml`/`.uss` yok, aramayın.

**Arayüzün tamamı prefabtır ve elle düzenlenir.** Kodda görsel kurulum kalmadı; sınıflar yalnız
veri yazar (metin, renk, görünürlük, konum). Yerleşim, punto, renk, sprite — hepsi prefabta.

## Nerede ne var

Tüm arayüz prefabları **tek klasörde**: `Assets/_Shared/App/Resources/UI/`

| Prefab | Ne çizer | Kim kullanır |
|---|---|---|
| **`PanelGlow.prefab`** | İki büyük kartın (`AdminStatsPanel` · `AdminPreferencesPanel`) kenarını saran **yumuşak lacivert ışıma**. Kök `PanelGlow` karta tam gerili, grafiksiz bir çerçevedir; ışımanın kendisi çocuğu **`Glow`**'dadır (tek `Image`, `PanelGlow` sprite'ı, `Simple`). Kartın kökünde **ilk çocuk** olarak nested örnek: `PanelBG`'nin üstünde, `Fill`'in (yani tüm içeriğin) altında çizilir. `Glow` karta **anchor'la** bağlıdır ve dışarı taşması orantılıdır (`anchorMin` (−0,075 · −0,133), `anchorMax` (1,075 · 1,133), `sizeDelta` 0 → kart genişliğinin %7,5'i / yüksekliğinin %13,3'ü; 16:9 kartta dört kenarda eşit, 1440×810'da 108 px) — dokudaki tepe çizgisi tam kart kenarına oturur, kart büyüyüp küçülünce kalınlık onunla birlikte ölçeklenir, elle sayı girilmez. ⚠️ Pay ÇOCUKTA durur, kökte değil: nested örneğin KÖK `RectTransform`'u örnek başına saklanır (varsayılan override) ve asset'teki değişiklik oraya yayılmaz — pay kökte olsaydı her kartta elle güncellenirdi. Rengi ve saydamlığı `Glow`'un `Image.color`'undadır (sprite beyaz+alfa; koyu lacivert ton ve yoğunluk buradan gelir, alfa 0,6), `raycastTarget` KAPALI — tıklamayı yutmaz. Işımayı başka bir karta vermek = bu prefabı o kartın kökünün altına ilk çocuk olarak koymak, sprite'ı ikinci kez kullanmak değil | `AdminStatsPanel` · `AdminPreferencesPanel` içinde nested örnek |
| **`AdminHud.prefab`** | Admin ekranının kalıcı katmanı: skorlar, chip, takım kolonları, sağ üstte **kamera kipi düğmeleri** (`CameraBar` → `Mode1` SERBEST · `Mode2` KUŞ BAKIŞI; ⚠️ POV düğmesi YOKTUR — `AdminHud.modeButtons[0]` yuvası bilerek boştur, POV oyuncu kartından girilir), alt ortada **maç kontrol şeridi** (`MatchBar` → `Start`/`Pause`/`Abort` ikon düğmeleri, her birinin `Icon` çocuğu; sürücü bileşeni `AdminMatchControls` bu objededir), ölüm ve ihlal akışları. ⚠️ **Durum/bilgi metni taşımaz** — maç·harita satırı, bağlantı göstergesi, çoklu admin satırı, seçili oyuncu satırı ve şeridin durum satırı yoktur; ekranda yalnız veri ve kontrol durur. Tercihler ve istatistik panelleri içinde **nested prefab örneği** olarak durur | `AdminSpectator` |
| **`AdminStatsPanel.prefab`** | İstatistik paneli: kart + takım özeti + **oyuncu satırı listesi** (kaydırılabilir) + maç bilgisi + alttaki toplu eylem şeridi — soldan sağa `Fill/BottomBar/ClearAllCalibration` (herkesin kalibrasyonunu sıfırlar; küçük ve kırmızı yazılı — ⚠️ **kısa basış yumuşak, 1 sn basılı tutmak cihaz kayıtlarını da siler**, ayrı bir `PurgeAllCalibration` düğmesi YOKTUR), `Fill/BottomBar/CalibrateAll` (herkesin kayıtlı hizalamasını yeniden yükletir), `Fill/BottomBar/MeasureAll` (herkesin gövde ölçüsü) + kendi kapanan uyarı penceresi — sürücü bileşeni kökünde. **Liste iki sütunludur:** `Fill/Scroll/Viewport/Content` altında `RedColumn` ve `BlueColumn` durur (ikisi de boş `RectTransform`; satırlar bunların altına kurulur) ve başlık şeridi ikilidir — `Fill/TableHeader` HERKES TEK kipinin tek şeridi, `Fill/TeamHeaders/RedHeader` + `BlueHeader` takımlı kipin çiftidir. ⚠️ **Sütunların ve takım başlıklarının yatay sınırları KODDAN sürülür** (`AdminStatsPanel._columnGap`): prefabta elle daraltılırlarsa değer ilk tazelemede geri yazılır, ara boşluğu değiştirmenin yeri o alandır. Takım başlığının `IconKills`/`IconDeaths`/`HeaderKd` ögeleri **dar satırın** hücre merkezlerine göre kaydırılmıştır — dar satırdaki hücreleri oynatırsan bu üçünü de oynat | `AdminHud` içinde nested örnek |
| **`AdminPreferencesPanel.prefab`** | Tercihler paneli — **dört sekmeli** — sürücü bileşeni kökünde; kart kabuğu `AdminStatsPanel` ile aynıdır ve **aynı ölçüdedir** (`PreferencesPanel` 1440×810 — 16:9, 1920×1080 referansta ekranın %75'i: yatay 1440, dikey 810; `PanelBG` bu oranda çizilir → `PanelGlow` (kenar ışıması, nested) → `Fill`). ⚠️ **`Fill`'in koyu dolgu `Image`'ı iki panelde de KAPALIDIR** (`ChamferRect_20`, bileşen enabled=0): açılırsa `PanelBG` sanatının üstüne yarı saydam bir perde çeker ve arka plan "gelmemiş" görünür — kapatmak için objeyi değil yalnız Image bileşenini kapat, obje çocukların kökü. Sekme çubuğu `Fill/Tabs` (`Tab_Mac` · `Tab_Gorunum` · `Tab_Baglanti` · `Tab_Ses`; zemini `BtnDark`/`BtnCyan`, hangisinin çizileceğini kod seçer), sayfalar `Fill/Page_Mac` · `Page_Gorunum` · `Page_Baglanti` · `Page_Ses` (hepsi `Fill`'i dolduran boş kökler; hangisinin açık olduğunu kod belirler, prefabta açık olan yalnız tasarım kolaylığıdır). Satırlar sayfanın altında **70 px adımla** üstten dizilir (ilk satır y = −186; başlık ve `X` istatistik paneliyle birebir aynı ölçüdedir, iki panel arasında geçişte kabuk zıplamaz — birini oynatırsan ötekini de oynat); yeni satır = o sayfada en alta bir satır daha (kart yüksekliği en dolu sayfa olan MAÇ'a göre seçilmiştir). MAÇ: mod/harita açılır listeleri, süre/skor limiti/geri sayım/dost ateşi adımlayıcıları, KALİBRASYON alt bölümü (yalnız **kalibre modu** düğmeleri — iki çapa / kayıtlı çapa / bulut-rezerve); toplu eylemler (kalibrasyon sıfırlama · yeniden yükletme · gövde ölçümü) `AdminStatsPanel`'in alt şeridinde, oyuncu adı `AdminStatsRow`'dadır. GÖRÜNÜM: halkalar · ad etiketleri · ihlal sesi · kamera hızı · çatı · ses çıkışı. BAĞLANTI: bağlantı metni, `Reconnect` · `Disconnect` · `QuitGame`. SES: kanal başına bir satır (`AudioChannel` sırasında — ambiyans · silah · seslendirme · müzik), satır ögeleri `Label_` · `Mute_` · `Prev_` · `Value_` · `Next_`; diziler `_audioValues` · `_audioPrev` · `_audioNext` · `_audioMuteButtons` · `_audioMuteLabels` ve **hepsi aynı kanal sırasındadır** — biri kaydırılırsa yanlış kanal kısılır. ⚠️ **BAŞLAT/DURAKLAT/İPTAL bu prefabta YOKTUR** — `AdminHud.prefab`'ın `MatchBar`'ındadır. Başlık çubuğunda yalnız `X` vardır (`Close`, `BtnRed`); ⚠️ **pencere kipi düğmesi YOKTUR** — tam ekran/pencereli yalnız `F11`'dedir. ⚠️ `X` sağ kenardan **52 birim** içeride durur (iki panelde de aynı): `PanelBG`'nin sağ üst köşesi ~52 birim pahlıdır, daha sağa alınırsa düğmenin köşesi sanatın dışına taşar. ⚠️ **Kırmızı zemin boştayken GÖRÜNMEZ** ve prefabta böyle kalmalıdır: `Button.colors.normalColor`'ın alfası 0, `highlightedColor`'ınki 1 — kırmızı yalnız fare üstüne gelince gelir (`selectedColor` da 0, yoksa tıklandıktan sonra kırmızı asılı kalırdı). Zemini `Image.color`'dan söndürme: o kapı hover'ı da söndürür | `AdminHud` içinde nested örnek |
| **`AdminPlayerRow.prefab`** | Kolonlardaki tek oyuncu satırı (ad, HP barı, POV/KAL/ÖLÇ/TAKIM/AT — beş eylem sütunu `Fill` içinde eşit paylaşır, biri eklenir/çıkarılırsa hepsinin anchor'ı yeniden bölünür). ⚠️ **CAN (canlandırma) düğmesi YOKTUR ve eklenmez** — operatörün elle canlandırması yoktur, canlanmanın tek yolu oyuncunun kendi `revive_request`'idir | `AdminHud` örnekler |
| **`AdminStatsRow.prefab`** | İstatistik panelindeki tek oyuncu satırı (takım şeridi, ad + `#id`, K/D/K-D hücreleri, ayrıntı şeridi, soldan sağa `Fill/Purge` (SIFIRLA — ⚠️ obje adı tarihseldir, düğme artık **iki kipi de** taşır: kısa basış hizalamayı düşürür, 1 sn basılı tutmak cihaz kaydını siler) /İSİM/AT/ÖLÇ/KALİBRE düğmeleri + satır içi ad yazı kutusu; ⚠️ ayrıntı şeridi (`Fill/Stats`) ile ad yazı kutusunun sağ ucu SIFIRLA'nın soluna kadardır, düğmeyi genişletirsen ikisini de kısalt). Oyuncunun adı **yalnız burada** düzenlenir. `AdminPlayerRow` ile karıştırmayın: o dar yan panel kartı, bu geniş liste satırıdır | `AdminStatsPanel` örnekler ve havuzlar |
| **`AdminStatsRowNarrow.prefab`** | Yukarıdakinin **prefab VARYANTI** — takımlı kipte panel ikiye bölününce sütuna sığan satır. Aynı ögeler üç şeride yığılmıştır (1: şerit + ad + `#id` + K/D/K-D · 2: ayrıntı şeridi tam genişlik · 3: sağa yaslı beş **ikon düğmesi**), yükseklik 74 yerine 124'tür. Düğmelerde yazı yerine `Fill/<Düğme>/Icon` durur; etiket düğmenin altındaki ince şeride iner ve **yalnız durum yazar** (`EMİN?` · `TAMAM` · `HATA` · `!` · `×ölçek`), boştayken boştur — anahtarı `AdminStatsRow.iconButtons` alanıdır ve **yalnız bu varyantta açıktır**. ⚠️ **Varyant olması sözleşmedir:** rect'ler ve ikonlar dışında her şey tabandan miras alınır, yani geniş satıra eklenen bir öge buraya da gelir — kopyalanmış ikinci bir prefaba dönüştürmeyin. ⚠️ Sütun genişliği ~675 px'dir: sağa yaslı düğme şeridi 272 px yer kaplar, ayrıntı şeridi onun bir alt satırındadır — düğmeleri genişletirsen şeridin sol ucu adı ezmeye başlar | `AdminStatsPanel` örnekler ve havuzlar (yalnız takımlı kipte) |
| **`AdminPlayerMarker.prefab`** | Oyuncunun zemindeki halkası + ad etiketi (dünya uzayı) | `AdminPlayerMarkers` örnekler |
| **`ConnectionOverlayScreen.prefab`** | Bağlantı hata ekranı — masaüstü (scrim + "Yeniden Bağlan" düğmesi) | `ConnectionOverlay` |
| **`ConnectionOverlayWorld.prefab`** | Bağlantı hata ekranı — VR (world-space kart, düğmesiz) | `ConnectionOverlay` |
| **`LoadingOverlayScreen.prefab`** | Sahne geçişi yükleme ekranı — masaüstü (scrim + kart + ilerleme barı) | `LoadingOverlay` |
| **`LoadingOverlayWorld.prefab`** | Sahne geçişi yükleme ekranı — VR (world-space kart, **scrim YOK**) | `LoadingOverlay` |
| **`MatchResultOverlay.prefab`** | Maç sonu ekranı: sonuç kartı (KAZANDIN/KAYBETTİN/BERABERE) + genel skor tablosu. İkisi aynı prefabta iki paneldir ve **ikisi de `AdminStatsPanel`'in kart kabuğunu** kullanır (`StatsPanel` → `Fill`) | `MatchResultOverlay` |

Oyuncu HUD'ları ayrı yerdedir (mod kutularında):

| Prefab | Ne çizer |
|---|---|
| `Assets/Modes/TeamDeathmatch/UI/TdmHud.prefab` | TDM oyuncu HUD'ı |
| `Assets/Modes/FreeForAll/UI/FfaHud.prefab` | FFA oyuncu HUD'ı |

Cephane göstergesi bu ikisinin de dışındadır: **silahın kendi üstünde** durur
(`Assets/_Shared/Arsenal/Prefabs/AmmoCanvas.prefab` — dünya uzayı, `WeaponAmmoPanel` sürer) ve
bütün `WPN_*` prefablarına iç içe geçmiş örnek olarak girer. Puntosu/ayracı/ikonu/rengi orada
düzenlenir, **tek yerde** — silah başına kopyası yoktur. ⚠️ Görüş alanına düşen ayrı bir cephane
paneli yoktur ve geri eklenmez: aynı sayının iki yerde çizilmesi demek olurdu.

Ortak görseller: `Assets/_Shared/App/UI/Sprites/` — yuvarlak köşe (`RoundedRect_4/8/12/20`,
9-slice kenarları ayarlı), halka (`Ring_16`), pahlı tema kiti ve ikonlar:

| Sprite | Ne işe yarar |
|---|---|
| `ChamferRect_20` | Dört köşesi 45° pahlı dolu plaka (panel gövdesi) — 9-slice, border 28 |
| `ChamferRectBottom_20` | Üst köşeler kare, alt köşeler pahlı (başlık bandı / alt şerit) — 9-slice, border 28 |
| `ChamferOutline_20` | Aynı oktagonun 3px kenarlığı, içi boş — 9-slice, border 28 |
| `SlantButton_20` | Paralelkenar düğme/sekme zemini — yatay 9-slice (border sol/sağ 30) |
| `FadeH_256` / `FadeV_256` | Eriyen beyaz gradyan (takım kenar şeridi, bant parlaması) — `Image.Type: Simple` |
| `ArrowDown_128` | Aşağı bakan dolu üçgen (başlık chevron'u) |
| `skull` / `crosshair` / `calibrate` / `scale` / `settings` | İkonlar (ölüm · öldürme · kalibrasyon · gövde ölçeği · ayarlar). Ekranda 24–30 px çizilirler: **kalın gövdeli** üretilir (kıl çizgi bu boyutta örneklemeye düşer, noktalı görünür), import'ta mipmap açık + max 128 px |
| `trash` / `cross` / `pencil` | Dar istatistik satırının ikon düğmeleri (kayıt sil · at · isim). ⚠️ **Yer tutucudurlar** — koddan üretilmiş düz beyaz siluetler, komşularının (`calibrate`/`scale`) çizim diliyle henüz eşleşmezler; yerlerine çizilmiş PNG konulacaktır. Değiştirirken dosya adı ve import ayarı korunur (`AdminStatsRowNarrow` prefabındaki bağ dosyaya bakar), boyut 128 px + mipmap açık |
| `play` / `pause` / `stop` | Maç kontrol şeridinin ikonları (▶ başlat/devam · ⏸ duraklat · ■ iptal) — saf geometrik beyaz silüetler, aynı import ayarı. `AdminMatchControls.playSprite`/`pauseSprite` alanları DURAKLAT düğmesinin iki hâlini taşır; renk kod tint'inden gelir (yeşil/başlık/kırmızı, pasifte soluk) |
| `PanelGlow` | Kart kenarı ışıması: beyaz+alfa, kenar çizgisinde tepe yapıp içe ve dışa aynı Gauss eğrisiyle sönen **pahlı sekizgen** hale — köşeleri `PanelBG` ile aynı 45° kesiktir (pah ≈ kart genişliğinin %3,6'sı; 1440'ta 52 birim), tepe çizgisi `PanelBG`'nin görünür kenarına (kart kenarından ~2 sprite px içeride) oturur (552×342: 480×270'lik 16:9 kart bölgesi + dört yanda 36 tx pay, σ ≈ 10,5 tx; ortası tümden saydam). ⚠️ `PanelBG` değişirse (pah/kenar) doku da yeniden üretilir, yoksa hale köşelerde sanattan sapar. Pay/kart oranı (36/480 · 36/270) `PanelGlow.prefab`'taki `Glow` anchor'larıyla BİREBİR aynıdır — dokuyu yeniden üretirken pay değişirse anchor'lar da değişir — `Image.Type: Simple`, `Uncompressed` + `FullRect` mesh (yumuşak alfa bantlanmasın/kırpılmasın diye), mipmap yok. Rengi sprite'ta değil `PanelGlow.prefab`'ın `Image.color`'unda; kartın oranı 16:9 kaldığı sürece kalınlık dört kenarda eşittir |
| `PanelBG` | Panel kartının **tek parça AI arka planı** (başlık bandı + chevron + takım parlamaları dahil) — `Image.Type: Simple`, karta gerilir; kartın en-boy oranı görsele uydurulur |
| `BtnDark` / `BtnCyan` / `BtnRed` | AI paralelkenar buton zeminleri (pasif · seçili · tehlike). Tercihler panelinde `AdminPreferencesPanel`'in `_buttonIdleSprite` / `_buttonActiveSprite` / `_buttonDangerSprite` alanlarına bağlanır ve **sekme · kalibre kipi · pencere kipi · çıkış** düğmelerinin hepsini besler. ⚠️ **Pasif ve seçili İKİSİ birden bağlı olmalı** — biri boşsa panel eski düz renk tintine düşer (yarım bağ yanlış renkli bir görsel bırakmasın). Bağlıyken `Image.color` **beyaz** olur: rengi görselin kendisi taşır |
| `RowPlate` | Çok geniş AI satır/bant plakası — **bugün hiçbir yerde kullanılmıyor.** ⚠️ İstatistik satırına (`AdminStatsRow`) uymaz, denemeye değmez: (1) plakanın kendi rengi amber, satırın koyu paletiyle çakışıyor; (2) satır ~20:1 iken plaka ~6:1 — 9-slice uçları kaynak piksel genişliğinde çizdiği için 65 px yüksekliğinde eğim uzun ve yatık bir kama olarak okunuyor. Yeri, en-boy oranı kaynağa yakın (~6:1) ve amber tonun kasıtlı olduğu bir bant olurdu |

⚠️ **AI zeminlerinin 9-slice border'ı**: dördü de paralelkenar, yani yatay 9-slice ister — border'sız
gerildiklerinde eğim açısı ve kenar kalınlığı orana göre bozulur. Ölçülmüş yatay border değerleri
(sol/sağ, üst/alt 0): `BtnDark` 56 · `BtnCyan` 60 · `BtnRed` 64 · `RowPlate` 108. Import ayarı
Inspector'dan verilir (Sprite Editor → Border); bileşenlerde `Image.Type` `Sliced`'tır. Yürürlükteki
değerler: `BtnDark` 56/56 · `BtnCyan` 56/60 · `BtnRed` 62/62 · `RowPlate` 132/132 (hepsi üst/alt 0).
⚠️ Border **eğimin yatay uzanımından küçük olamaz** (sırasıyla 45 · 50 · 53 · 73 px) — küçükse
ortadaki gerilen bant eğik ucun içine taşar.

Tema kiti ve ikonlar (dişli hariç) **beyaz üretilir** — renk `Image.color` tint'inden verilir,
böylece tek sprite hem kırmızı hem mavi takım için kullanılır. Ayna görünüm (sağ kenar, ters
paralelkenar) yeni sprite değil, RectTransform'da `scale.x = -1` ile alınır.

> **Neden `Resources/` altında?** Prefablar **sahneye KONMAZ** — çalışırken `Resources.Load`
> ile yüklenip örneklenirler. Sahneye konsalardı her yeni arena sahnesine elle bir kurulum adımı
> doğardı ve bir gün unutulurdu. Bu yüzden `Resources/` klasöründen **çıkarılmamalıdırlar**;
> taşınırlarsa ilgili arayüz sessizce hiç çizilmez (konsola `… prefabı bulunamadı` hatası düşer).

> **Panelleri KENDİ prefabında düzenleyin** (`AdminStatsPanel.prefab` /
> `AdminPreferencesPanel.prefab` çift tıklanır): `AdminHud` içindeki örneğin üstünde yapılan
> değişiklik instance override olarak birikir ve panel prefabındaki sonraki düzeltmelerle
> çatışır. Örneği **unpack etmeyin** — bağ kopunca panel prefabında yapılan düzeltmeler
> AdminHud'a bir daha inmez.

## Düzenlerken nelere dikkat edilir

Prefabı Project penceresinden çift tıklayıp prefab kipinde açın. **Serbestçe yapabilecekleriniz:**
konum/boyut/anchor değiştirmek, renk, punto, font, sprite değiştirmek, öge eklemek/çıkarmak,
görsel efekt (Shadow, Outline) eklemek.

**Dikkat edilecek üç şey var:**

1. **Bileşen alan bağlarını koparmayın.** Kök objedeki bileşende (`AdminHud`,
   `AdminPreferencesPanel`, `AdminPlayerRow`…) her ögenin bir alanı var (`scoreRedText`,
   `hpFill`, `_modeDropdown`…). Bir ögeyi **silerseniz** o alan boşalır ve **hata vermez —
   sessizce çizilmez.** Silmek yerine objeyi devre dışı bırakın ya da alfasını 0 yapın.
   Yeni bir öge ekleyip koda bağlamak gerekiyorsa geliştiriciye söyleyin (yeni alan gerekir).

2. **Düğme `onClick` kayıtlarını inspector'dan doldurmayın.** Prefablarda bilerek boştur;
   davranış çalışırken koddan bağlanır (`WireButtons` / `Initialize`). Inspector'dan eklenen
   kalıcı bir kayıt, kodun koşullarını atlar — ör. oyuncu satırındaki "AT" düğmesi iki adımlı
   onayı atlayıp doğrudan atardı, ya da tercihler panelindeki **OYUNDAN ÇIK** ilk basışta
   uygulamayı kapatırdı ("EMİN? ÇIK" adımını yazan `AdminPreferencesPanel.ArmQuit` hiç koşmazdı).

3. **Metinleri yazmayın, yer tutucu sayın.** `ScoreRed`, `KillFeed`, `Name` gibi ögelerin
   içeriği çalışırken koddan yazılır; prefabtaki yazı yalnız tasarım yaparken görebilmeniz
   içindir. **Punto/renk/hizalama kalıcıdır**, metnin kendisi değil.

Ayrıca birkaç teknik not:

- **Font atamayın** (ya da atarken dikkat edin): varsayılan TMP fontu Türkçe glifleri taşıyor.
  Türkçe karakteri olmayan bir fonta geçerseniz `İ Ş Ğ Ü Ö Ç` kutu (□) çizilir. Aynı sebeple
  arayüzde `✓ ✗ ⚠ → •` gibi sembol **kullanılmaz** — garantisi yok. (Açılır listedeki ok ve
  seçim işareti birer **sprite**tır, glif değil — onlar bu kuraldan etkilenmez.)
- **Açılır listelerde (`Dropdown_Mod`, `Dropdown_Harita`) `Template` çocuğunu AÇMAYIN.** Prefabta
  bilerek kapalıdır: TMP_Dropdown onu tıklanınca kopyalayıp açar, açık kaydedilirse liste panelin
  üstünde sürekli asılı durur. Rengini/puntosunu/satır yüksekliğini (`Item`) değiştirmek serbest.
  ⚠️ Listedeki **seçenek metinleri yer tutucudur** ("katalog yok"): gerçek mod/harita adlarını
  çalışırken kod doldurur, prefabta yazdığınız satırlar temizlenir.
- **Zengin metin (rich text) bayrağını kurcalamayın.** Oyuncu satırındaki `Stats` metni ve
  istatistik satırının ayrıntı şeridi, kodun ürettiği `<color=…>` etiketlerini taşır (pil ve
  kumanda simgeleri token başına renklenir — tek TMP'nin tek rengi olduğu için başka yolu yok).
  Bayrağı **kod açar**, yani inspector'dan kapatmanız görünümü değiştirmez. ⚠️ Diğer metinlerde
  bayrak **KAPALI kalmalı**: `Name` ve `OYUNCU` kolonunda oyuncunun kendi yazdığı ad var,
  `<b>` içeren bir ad biçimi bozardı.
- **Toplu sıfırlama düğmesinin metnini ve rengini kod sürer.** `BottomBar/ClearAllCalibration`'ın
  etiketi iki adımlı onayla değişiyor (boştaki hâli ↔ onay bekleyen hâli) ve rengini de
  `AdminStatsPanel` yazıyor; prefabtaki metin ile renk yalnız tasarım yaparken düğmeyi görebilmeniz
  içindir. Punto, hizalama, font ve düğmenin ölçüsü prefabta kalıcıdır — metin ve renk değil.
- **Tercihler panelinin sekmelerini ve maç şeridinin ikonlarını kod sürer.** `Tabs/Tab_*`
  düğmelerinin zemin/etiket rengi ve `Page_*` sayfalarının açık/kapalı hâli çalışırken
  `AdminPreferencesPanel` tarafından yazılır (aktif sekme `UiKit.Accent`); prefabta hangi sayfanın
  açık, hangi sekmenin boyalı olduğu yalnız tasarım kolaylığıdır. `MatchBar`'daki üç düğmenin
  `interactable`'ı ve `Icon` renkleri `AdminMatchControls`'tan gelir; DURAKLAT düğmesinin ikonu faza
  göre `playSprite`/`pauseSprite` arasında değişir — prefabtaki sprite yalnız başlangıç hâlidir.
  Sekme eklemek yeni alan gerektirir (`AdminPreferencesTab`'a SONA değer + üç diziye birer öge).
  ⚠️ Satır deseni sayfadan sayfaya aynı değildir: SES sayfasında satır başına **beşinci** bir öge
  vardır (`Mute_` düğmesi), diğer sayfalarda yoktur.
- **Ad renkleri koddan sürülür.** Oyuncu satırındaki `Name` ve sahnedeki ad etiketleri **takım
  renginde** yazılır (ölüde karartılmış); prefabtaki renk yalnız tasarım yaparken görebilmeniz
  içindir. Punto, hizalama ve font kalıcıdır — renk değil.
- **Satır yüksekliğini değiştirebilirsiniz.** `AdminPlayerRow` prefabının yüksekliğini
  büyütürseniz kolon yerleşimi kendiliğinden uyar (kod yüksekliği prefabtan okur). Satır arası
  boşluk ve kolon başına satır sayısı `AdminHud` bileşeninde alandır (`rowGap`,
  `maxRowsPerColumn`).
- **Aynısı istatistik listesi için de geçerli:** `AdminStatsRow` prefabının yüksekliğini
  büyütürseniz liste yerleşimi kendiliğinden uyar (kod yüksekliği prefabtan okur), satır arası
  boşluk `AdminStatsPanel` bileşenindeki `_rowGap` alanıdır. Liste kaydırılabilir olduğu için satır
  sayısı sınırı yoktur — satırı büyütmek kimseyi listeden düşürmez. **İki prefabın yüksekliği
  ayrı ayrı okunur** (`AdminStatsRow` geniş, `AdminStatsRowNarrow` dar kip): birini büyütmek
  diğerini etkilemez, ikisini birden istiyorsan ikisini de büyüt.
- ⚠️ **Panel prefabının KÖK objesini KAPATMAYIN** (`AdminStatsPanel`, `AdminPreferencesPanel`).
  Paneli açan tuş ve sunucudan gelen tazeleme kökteki bileşenin kendi `Start`/`Update`'inde
  koşuyor; kök kapalıyken panel **hiçbir tuşla açılmaz** ve hata da vermez. Gizlenecek olan içteki
  kart objesidir (bileşenin `_root` alanına bağlı olan) — çalışırken kod zaten onu açıp kapatıyor, prefabta hangi hâlde
  bıraktığınız yalnız tasarım kolaylığıdır.
- **`AdminHud.rowPrefab` alanı doluysa bırakın.** Kopması hâlinde `AdminPlayerRow.prefab`'ı
  inspector'da o alana sürükleyin — yoksa oyuncu satırları hiç çizilmez.
- **Seçili oyuncunun halkası:** `AdminPlayerMarker` bileşeninde `ringNormal` ve `ringSelected`
  iki ayrı sprite alanıdır. Şu an ikisi de aynı görseldir (seçim yalnız boyut artışıyla
  anlatılır); `ringSelected`'a **daha kalın** bir halka koyarsanız seçim belirginleşir.
- **`ConnectionOverlay`'in iki varyantı ayrı prefabtır** ve farklı alanları dolu olur:
  masaüstü varyantında `_hudFollow` boştur, VR varyantında `_reconnectButton`/`_reconnectLabel`
  boştur (VR'da düğme yoktur, yerine "joystick'i 1 sn basılı tut" ipucu vardır).
  **Bu boşluklar normaldir, doldurmayın.**
- **`LoadingOverlay` de iki varyattır** ve aynı kural geçerlidir: masaüstü varyantında `_hudFollow`
  boştur, VR varyantında **scrim objesi hiç yoktur**. Scrim'i VR prefabına EKLEMEYİN — oyuncu
  fiziksel alanda yürüyor, görüşü karartmak tehlikelidir (`ConnectionOverlayWorld` ile aynı
  gerekçe). Kart üstündeki `Title` ve `Hint` metinleri **sabittir** (kod onlara dokunmaz);
  `SceneLine`, `Percent` ve barın `Fill`'i çalışırken sürülür.
- **İlerleme barının `Fill`'ine dokunurken:** dolum `anchorMax.x` ile sürülür
  (`UiKit.SetBarFill` deseni). Pivot `(0, 0.5)` ve offsetler 0 kalmalı; `Fill`'i ortalarsanız ya da
  `Image.Type`'ını `Filled` yaparsanız bar sessizce hep boş görünür.
- **`MatchResultOverlay`'in iki paneli aynı prefabta durur** (`ResultPanel` ve `ScoreboardPanel`) ve
  hangisinin ne zaman açılacağını kod belirler — prefabta ikisinin de açık/kapalı olması yalnız
  tasarım kolaylığıdır (çalışırken ikisi de kapatılıp sırayla açılır). Panelin **oyun içi HUD'dan
  büyük** olması bilinçlidir: kök 1400×860 birim, ölçek 0,0007 (≈0,98 m) ve `HudFollow` mesafesi
  1,5 m — mod HUD'ı 900×520 / 0,0005 / 1,1 m'dir. Küçültürseniz maç sonu ekranı HUD'la karışır.
- **Her iki panel de `AdminStatsPanel`'in kart kabuğunu taşır** (`StatsPanel` → `Fill`; `PanelBG`
  arka planı + `ChamferRect_20` dolgu, **1440×810** — 16:9, 1920×1080 referansta ekranın %75'i:
  yatay 1440, dikey 810). ⚠️ **Kartın en-boy oranını değiştirmeyin** —
  `PanelBG` tek parça bir görseldir (başlık bandı, chevron, takım parlamaları görselin içinde) ve
  oran bozulunca sanat gerilir. Kolon ekleyip çıkarmak yerine mevcut kolonların genişliğini
  değiştirin.
- **Skor tablosu kolonları `Header0..5` / `Column0..5` çiftleridir** ve sıraları koddaki
  `ColumnOrder` ile eşleşmek zorundadır (OYUNCU · TAKIM · SKOR · K · D · K/D). Bir kolonu
  **silerseniz** kod onu sessizce atlar; yenisini eklemek prefab + kod işidir.
  ⚠️ `Header3`/`Header4`'ün metni **bilerek boştur** — K ve D başlıkları `IconKills`/`IconDeaths`
  (crosshair/skull) ikonlarıyla anlatılır, admin kartındaki gibi.
- **`Headline` otomatik küçülür** (`enableAutoSizing`, 44→30) ve **sarmaz**: kazanan + skor tek
  satırda taşınıyor, sarmasına izin verilirse alttaki takım özetinin üstüne biner.
- **`AdminHud`'ın `sortingOrder`'ı 4000, yükleme ekranınınki 4500, bağlantı ekranınınki 5000.**
  Bu sıra bilinçlidir: yükleme HUD'ın üstünü, bağlantı hatası ise her şeyin üstünü kaplamalı.
  Canvas bileşeninde değiştirmeyin.
- **Admin arayüzü 16:9 içindir** (`CanvasScaler` 1920×1080 referans, eşleme `Expand`): tüm
  yerleşim bu orana göre çizilir, iki büyük panel (`AdminStatsPanel` · `AdminPreferencesPanel`)
  16:9 kartla ekranın %75'ini kaplar (1440×810). Başka oranda bir pencere **kırpılmaz**, kenarda boşluk
  bırakır — 16:9 dışı bir düzen için ayrı yerleşim YOKTUR ve yapılmaz. Kartı büyütmek/küçültmek
  gerekirse ölçüyü **oranı bozmadan** ve içindeki her ögeyi (konum, boyut, punto) aynı çarpanla
  değiştirin; yalnız kartı büyütüp içeriği bırakmak sanatı bir tarafa yığar.

## Renk paleti

⚠️ **Renkler artık prefablarda gömülüdür** — paleti tek yerden değiştirmek mümkün değil.
Kodda kalan `UiKit` paleti (`Assets/_Shared/App/Scripts/UiKit.cs`) yalnız **çalışırken sürülen**
renkler için kullanılır: HP barının yeşil/turuncu/kırmızısı, seçim vurgusu, kalibresiz satırın
kenarlığı, bağlantı noktasının rengi. Ton değişikliği yaparken **hem prefabları hem `UiKit`
paletini** güncelleyin, yoksa statik ögelerle durum renkleri birbirini tutmaz.

⚠️ **Takım renkleri iki yerde birden yaşar:** `UiKit.TeamRed`/`TeamBlue` ve
`Core.Player.RemoteAvatar`. Aynı oyuncu HUD'da ve sahnede farklı renkte görünürse operatör
yanılır — ikisini birlikte değiştirin.

## Bu prefablar nasıl doğdu

Elle çizilmediler: arayüzü kuran prosedürel kod bir kereliğine çalıştırılıp sonucu prefab olarak
kaydedildi (geçici bir editör aracıyla). Bu yüzden tasarım, koddaki hâliyle **piksel piksel
aynıdır**. Aynı geçiş sırasında `UiKit`'in çalışırken ürettiği yuvarlak köşe ve halka görselleri
de gerçek PNG asset'lerine yazıldı ve 9-slice kenarları ayarlandı.

**O araç işini bitirdiği için silindi.** Görünümün tek doğruluk kaynağı artık prefablardır;
araç dursaydı ikinci ve sessizce bayatlayan bir kaynak olurdu (ve yanlışlıkla çalıştırılması
elle yapılmış tüm tasarımı ezerdi).
