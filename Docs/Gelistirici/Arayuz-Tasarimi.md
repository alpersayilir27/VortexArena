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
| **`AdminHud.prefab`** | Admin ekranının kalıcı katmanı: skorlar, chip, takım kolonları, kamera şeridi, ölüm akışı. Tercihler ve istatistik panelleri içinde **nested prefab örneği** olarak durur | `AdminSpectator` |
| **`AdminStatsPanel.prefab`** | İstatistik paneli: kart + takım özeti + **oyuncu satırı listesi** (kaydırılabilir) + maç bilgisi + alttaki toplu eylem şeridi — soldan sağa `Fill/BottomBar/ClearAllCalibration` (herkesin kalibrasyonunu sıfırlar; küçük, kırmızı yazılı ve iki adımlı onaylı), `Fill/BottomBar/CalibrateAll` (herkesin kayıtlı hizalamasını yeniden yükletir), `Fill/BottomBar/MeasureAll` (herkesin gövde ölçüsü) + kendi kapanan uyarı penceresi — sürücü bileşeni kökünde | `AdminHud` içinde nested örnek |
| **`AdminPreferencesPanel.prefab`** | Tercihler paneli (mod/harita/süre/kalibrasyon/görünüm/bağlantı) — sürücü bileşeni kökünde; kart kabuğu `AdminStatsPanel` ile aynıdır (`PreferencesPanel` → `Fill`; `PanelBG` arka planı + `ChamferRect_20` dolgu). Kalibrasyon bölümü yalnız **kalibre modu** düğmelerini taşır (iki çapa / kayıtlı çapa / bulut-rezerve); toplu eylemler (kalibrasyon sıfırlama · kalibrasyonu yeniden yükletme · gövde ölçümü) `AdminStatsPanel`'in alt şeridinde, oyuncu adı ise `AdminStatsRow`'dadır. Başlık çubuğunda `KAPAT`'ın yanında **pencere kipi** düğmesi (`ScreenMode`), bağlantı satırının sağında **`QuitGame`** durur. ⚠️ Panel 1080p referansta **tavandadır** (alttan ~22 px pay): yeni bir tam satır SIĞMAZ — sonraki ekleme içeriği kaydırılabilir yapmayı gerektirir, bu ikisi de bu yüzden var olan satırların boş yerine kondu | `AdminHud` içinde nested örnek |
| **`AdminPlayerRow.prefab`** | Kolonlardaki tek oyuncu satırı (ad, HP barı, POV/KAL/ÖLÇ/CAN/TAKIM/KİMLİK/AT) | `AdminHud` örnekler |
| **`AdminStatsRow.prefab`** | İstatistik panelindeki tek oyuncu satırı (takım şeridi, ad + `#id`, K/D/K-D hücreleri, ayrıntı şeridi, İSİM/AT/ÖLÇ/KALİBRE düğmeleri + satır içi ad yazı kutusu). Oyuncunun adı **yalnız burada** düzenlenir. `AdminPlayerRow` ile karıştırmayın: o dar yan panel kartı, bu geniş liste satırıdır | `AdminStatsPanel` örnekler ve havuzlar |
| **`AdminPlayerMarker.prefab`** | Oyuncunun zemindeki halkası + ad etiketi (dünya uzayı) | `AdminPlayerMarkers` örnekler |
| **`ConnectionOverlayScreen.prefab`** | Bağlantı hata ekranı — masaüstü (scrim + "Yeniden Bağlan" düğmesi) | `ConnectionOverlay` |
| **`ConnectionOverlayWorld.prefab`** | Bağlantı hata ekranı — VR (world-space kart, düğmesiz) | `ConnectionOverlay` |
| **`LoadingOverlayScreen.prefab`** | Sahne geçişi yükleme ekranı — masaüstü (scrim + kart + ilerleme barı) | `LoadingOverlay` |
| **`LoadingOverlayWorld.prefab`** | Sahne geçişi yükleme ekranı — VR (world-space kart, **scrim YOK**) | `LoadingOverlay` |
| **`AmmoHud.prefab`** | VR'da sağ altta cephane göstergesi | `AmmoHud` |
| **`IdentifyDisplay.prefab`** | `identify` komutunda göz hizasında beliren "SEN BUSUN" kartı | `IdentifyOverlay` |
| **`MatchResultOverlay.prefab`** | Maç sonu ekranı: sonuç kartı (KAZANDIN/KAYBETTİN/BERABERE) + genel skor tablosu. İkisi aynı prefabta iki paneldir ve **ikisi de `AdminStatsPanel`'in kart kabuğunu** kullanır (`StatsPanel` → `Fill`) | `MatchResultOverlay` |

Oyuncu HUD'ları ayrı yerdedir (mod kutularında):

| Prefab | Ne çizer |
|---|---|
| `Assets/Modes/TeamDeathmatch/UI/TdmHud.prefab` | TDM oyuncu HUD'ı |
| `Assets/Modes/FreeForAll/UI/FfaHud.prefab` | FFA oyuncu HUD'ı |

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
| `PanelBG` | Panel kartının **tek parça AI arka planı** (başlık bandı + chevron + takım parlamaları dahil) — `Image.Type: Simple`, karta gerilir; kartın en-boy oranı görsele uydurulur |
| `BtnDark` / `BtnRed` / `BtnCyan` | AI paralelkenar buton zeminleri (pasif · tehlike · seçili) — Simple, gerilir |
| `RowPlate` | Çok geniş AI satır/bant plakası — Simple |

AI setinin üretim brief'i (tasarım dili, üretim kuralları, kalan parçaların envanteri ve
prompt'ları): `plan/design.md`.

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
  sayısı sınırı yoktur — satırı büyütmek kimseyi listeden düşürmez.
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
  arka planı + `ChamferRect_20` dolgu, 1265×705). ⚠️ **Kartın en-boy oranını değiştirmeyin** —
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
