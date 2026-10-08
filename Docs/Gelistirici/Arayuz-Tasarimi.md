---
title: Arayüz tasarımı — 2D'yi nerede bulurum, nasıl düzenlerim
---

# Arayüz tasarımı (2D / UI)

Projedeki tüm arayüz **uGUI**'dir: `Canvas` + `TextMeshPro`. **UI Toolkit kullanılmıyor** —
projede tek bir `.uxml`/`.uss` yok, aramayın.

Admin ekranları, oyuncunun maç sonu ekranı, kafaya kilitli şerit, öldün kartı ve aşçı modunun
lokanta arayüzü **koddan üretilir** (Girdap arayüz kiti): prefab bir çıktıdır, kaynak builder'dır.
Bildirim, yükleme ve bağlantı ekranları bunun dışındadır ve **elle düzenlenir** ("Girdap dışında
kalan arayüz").

## Nerede ne var

**Kit** — `Assets/_Shared/Core/UI/` (asmdef `VortexArena.Core`, namespace `VortexArena.Core.UI`):

| Tip | Ne yapar |
|---|---|
| `UiPolygonGraphic` | Köşe kesimli (chamfer) + yan eğimli (slant) + **köşe başına yarıçaplı** (radius) konveks poligon tabanı; yarım düzlem kırpma ve üçgen fan yardımcıları burada. Yay iki kenara da teğettir; aynı köşede chamfer yarıçapı **ezer**, aynı kenardaki iki yarıçap yaylar kesişmesin diye küçültülür, üst üste düşen ardışık köşe noktaları atılır (yoksa sıfır uzunluklu kenar outline'da diken üretir) |
| `UiShape` | Temanın kutusu: dış çerçeve halkası + gradyan dolgu + dış/iç parıltı + 0,75 px kenar yumuşatma, **tek draw**. Akıcı API: `Chamfer` · `Radius` · `Slant` · `Fill` · `Outline` · `Glow` · `InnerGlow` · `Antialias` |
| `UiStripes` | CSS `repeating-linear-gradient` karşılığı çapraz şeritler, poligona kırpılı (takım plakaları, canlı ihlal bandı); `Drift(sn)` = CSS `drift` keyframe’i: bir periyotluk sonsuz yavaş kayma, `Update` her kare meshi yeniler |
| `UiSegmentBar` | Dilimli, eğik can çubuğu (`SetFill` · `SetFillColors` · `SetTrack` · `SetMetrics`) |
| `UiButtonStyle` | Bir düğmenin TÜM görünümü: `SetKind(UiButtonKind)` · `SetInteractable` · `SetLabel` · `SetHold(0..1)`; parçaları `Bind(...)` bağlar |
| `UiChip` | Rozet: `Set(text, UiChipKind, icon)`; genişliğini kendi ölçer |
| `Girdap` | Palet ve yardımcılar: `Hex` · `Rgba` · `TeamHi/TeamLo/TeamInk(team)` · `Spacing(em)` · `Upper` · `Font(GirdapFont)` · `Icon(name)` · `Assets` |
| `Lokanta` | Aşçı modunun paleti ve ölçüleri (statik): mürekkep/krem/kâğıt yüzeyler, kırmızı-hardal-yeşil vurgular, malzeme dilimi renkleri, termometre cıvası, `Outline`/`Shadow`/`CardRadius` ve sabır eşikleri (`PatienceWarn`/`PatienceBad`). `Girdap` ile aynı kural: renk literali çağrı yerinde yazılmaz |
| `GirdapAssets` | ScriptableObject kabı: TMP fontları + ikon sprite'ları |
| `UiButtonKind` · `UiChipKind` · `UiGradientMode` · `GirdapFont` | Prefablara **serileşen** enum'lar |

**Builder** — `Assets/_Shared/App/Scripts/Editor/GirdapUi/`, `partial static class GirdapUiBuilder`
(ns `VortexArena.App.Editor`). `GirdapUiBuilder.cs` ortak yardımcıları taşır
(`Node` · `Place`/`PlaceRight`/`PlaceTopCenter`/`PlaceMiddleLeft`/`PlaceBottom*` · `Stretch`/
`StretchTop`/`StretchBottom`/`StretchLeft` · `Shape`/`ShapeBox` · `Stripes` · `SegmentBar` ·
`Image` · `Icon` · `Text` · `Kbd` · `Button` · `Chip` · prefab kaydetme), ekran başına bir kardeş
partial durur (`.Assets` · `.PlayerRow` · `.Stats` · `.Preferences` · `.Hud` · `.MatchResult` ·
`.PlayerHud`). Aşçı modunun lokanta teması kendi partial ailesindedir (`.Lokanta` ortak
primitifler — `LkCard`/`LkPill`/`LkRibbon`/`LkFace`/`LkText`/`TextShadow`/`TextOutline`/`LkSprite`
— ve `.LokantaHud` · `.LokantaBubble` · `.LokantaThermometer` · `.LokantaResult` · `.LokantaIcons`
+ sprite üreteci `LokantaSprites`).

**Menü** — `Tools > VortexArena > UI > Girdap`: *Fontları ve asset kabını üret* · *Tüm prefabları
üret* · *Lokanta sprite'larını üret* · *Yalnız `<Ekran>`* (aşçı tarafında *Yalnız Aşçı HUD* ·
*… sipariş baloncuğu* · *… termometre* · *… sonuç ekranı*). Toplu üretimin sırası PlayerRow →
StatsRow → StatsPanel → PreferencesPanel → Hud → MatchResult → PlayerHud → Lokanta'dır; HUD,
panelleri ve satır prefabını içine gömer.

**Üretilen prefablar** — `Assets/_Shared/App/Resources/UI/`: `AdminPlayerRow` · `AdminStatsRow` ·
`AdminStatsPanel` · `AdminPreferencesPanel` · `AdminHud` · `MatchResultOverlay` · `HealthHud` ·
`DeathHud`; `Assets/Modes/Burger/UI/`: `BurgerHud` · `LokantaThermometer` ·
`BurgerResultOverlay` (+ `NO_customer` prefabındaki `Bubble` alt ağacı). Builder prefabı
**yerinde** düzenler: içeriği yükler, kendi ürettiği çocukları siler, yeniden kurar, alanları
`SerializedObject` ile bağlar, kaydeder.

**Asset kabı** — `Assets/_Shared/App/Resources/UI/Girdap.asset` (`GirdapAssets`): fontlar + ikon
dizisi + desen sprite'ları. `Girdap.Assets` onu `Resources`'tan tembel yükler; yoksa arayüz
fontsuz/ikonsuz çizilir ve konsola tek seferlik hata düşer.

**Fontlar** — kaynak TTF'ler `Assets/_Shared/App/UI/Fonts/`, menünün ürettiği TMP SDF asset'leri
`Assets/_Shared/App/Resources/UI/Fonts/` (dinamik atlas; Türkçe glifler ve ayraçlar öne pişirilir).
Girdap ekranları Chakra/Saira/Barlow ailesindedir; Fredoka (başlık ve sayı) ile Nunito (küçük
kapitaller) **yalnız lokanta temasındadır**. Aynı klasördeki `<Font> SDF Glow <renk>.mat` dosyaları
metin parıltısı (CSS `text-shadow`), `<Font> Shadow <RRGGBBAA>.mat` / `<Font> Outline <RRGGBBAA>.mat`
dosyaları lokantanın sert gölge/konturu için preset'tir: builder üretir, elle düzenlenmez;
silinirse bir sonraki üretimde geri gelir.
**İkonlar** — `Assets/_Shared/App/UI/Sprites/Ic_<Ad>.png`; kodda **çıplak adla** istenir
(`Girdap.Icon("Skull")`). Kaynak çizim mockup'ın `tema.js` dosyasındaki 24×24 çizgi ikon
haritasıdır (`P`, 2 px kalın, yuvarlak uçlu); Unity kopyası 64×64, beyaz + alfa PNG'dir ve import
ayarları mevcut ikonlarla aynıdır (Sprite · Single · 100 ppu · sıkıştırmasız). Yeni ikon önce
`tema.js`'e, sonra aynı geometriyle PNG'ye yazılır; kaba `Fontları ve asset kabını üret` toplar.
Bir kısmı (`Ic_Flame` · `Ic_Check`) builder'ın kendi çizdiği PNG'dir (`EnsureGirdapIcons`, kap
toplanmadan önce koşar) — dosyası silinirse bir sonraki üretimde geri gelir.

**Mockup'lar** — `Docs/Gelistirici/Arayuz/` (ortak stil `tema.css`, parça kiti `kit.html`, ekran başına
bir HTML; tarayıcıda çift tıkla açılır). Her ölçünün kaynağı orasıdır ve kalıcıdır: görsel değişiklik
**önce mockup'ta** yapılır, onaylanınca builder'a geçirilir — oyuna doğrudan dokunulmaz.

**Girdap dışında kalan arayüz** (elle düzenlenen prefablar, hepsi
`Assets/_Shared/App/Resources/UI/` altında): `RoundNoticeHud` · `LoadingOverlayScreen`/`…World` ·
`ConnectionOverlayScreen`/`…World` · `AdminPlayerMarker`. Mod HUD'ları mod
kutusundadır (`Assets/Modes/<Mod>/UI/<Mod>Hud.prefab`) ve içleri bilerek boştur: taşıdıkları şey
yukarıdaki iç içe prefab örnekleridir — kendi şeridini builder'dan üreten mod bunun dışındadır
("Aşçı (Burger) — lokanta teması"). Cephane göstergesi silahın kendi üstündedir
(`Assets/_Shared/Arsenal/Prefabs/AmmoCanvas.prefab`). Bu grupta alan bağları, `onClick` boşluğu ve
"metin yer tutucudur" kuralları geçerlidir → `Yapma-Listesi.md` "Sahne ve prefab".

> **Neden `Resources/` altında?** Prefablar **sahneye KONMAZ** — çalışırken `Resources.Load` ile
> yüklenip örneklenirler; sahneye konsalardı her yeni arena sahnesine elle bir kurulum adımı
> doğardı. Klasörden çıkarılırsa ilgili arayüz sessizce hiç çizilmez (konsola `… prefabı
> bulunamadı` düşer).

## Nasıl değiştirilir

⛔ **Girdap prefabları elle düzenlenmez.** Inspector'da yapılan her şey bir sonraki üretimde
kaybolur. Değişiklik **builder'a** yazılır, sonra ilgili `Yalnız <Ekran>` menüsü koşulur (asset
kabı değiştiyse önce *Fontları ve asset kabını üret*).

- **Ölçü = CSS px.** `CanvasScaler` 1920×1080 `Expand` olduğu için 1 CSS px = 1 birimdir; mockup'ta
  okunan sayı koda birebir yazılır. `Place` ailesi CSS kafasıyla çalışır: x/y **ebeveynin sol üst
  köşesinden** ölçülür, artı genişlik/yükseklik.
- ⛔ **Layout Group / ContentSizeFitter konulmaz.** Yerleşim sabit anchor'ladır; değişken
  genişlikli içerik kodda ölçülür (`TMP_Text.GetPreferredValues` + `anchoredPosition`/`sizeDelta`).
- **Şablon = prefabta pasif bırakılmış çocuk.** Satırlar, akış plakaları, dropdown öğeleri ve kat
  düğmeleri çalışırken o şablondan klonlanır. Şablonu açık bırakmak her tablonun altına boş bir
  satır çizer.
- **Yeni parça eklerken hangi yardımcı:** kutu → `Shape`/`ShapeBox`, düğme → `Button(...)`
  (`UiButtonKind` + isteğe bağlı ikon/keycap/hold), rozet → `Chip(...)`, yazı → `Text(...)`
  (`GirdapFont` + punto + `TextAlignmentOptions`), ikon → `Icon(...)`, klavye ipucu → `Kbd(...)`,
  şerit → `Stripes(...)`, can çubuğu → `SegmentBar(...)`, metin parıltısı (CSS `text-shadow`) →
  `TextGlow(...)`, tam ekran kart → `PlaceCenter(...)`.
- **Yeni bir alan bağlanacaksa** bileşene `[SerializeField]` eklenir ve builder onu
  `SerializedObject` üstünden yazar — elle sürüklenen bağ ilk üretimde silinir.
- **Satır yüksekliği koddan okunur:** `AdminPlayerRow.Height` · `AdminStatsRow.Height` ·
  `AdminKillFeedRow.Height`/`NewHeight` · `AdminViolationFeedRow.Height`/`LiveHeight`. Yerleşim bu
  sabitlerden türer; prefabtaki rect ile sabit **birlikte** değişir. Maç sonu tablosunun adımı
  (`MatchResultOverlay`'in `rowHeight`/`rowGap`/`rowsTop` alanları) ise **prefabta serileşir**;
  varsayılanları Girdap ölçüleridir, satır yerleşimini değiştiren bir tema onları builder'ından
  yazar.
- **Moda özel maç sonu ekranı iki yoldan birindedir** ve ikisi de aynı `MatchResultOverlay`
  alanlarını bağlar: (a) `MatchResultOverlay.prefab`'ın **prefab varyantı** — sprite/renk/kelime
  farkı; alan bağları tabandan miras gelir, builder varyantların ezdiği düğümleri silmez, yeniden
  ebeveynler (`ResultKeptNodes`), silinen düğüm override'ı sarkıtır ve o modun sanatı kaybolur.
  (b) Modun kendi `GirdapUiBuilder` partial'ından **sıfırdan üretilen** prefab — yerleşimi de
  değişen temalar için; alanları ad üstünden bağlar. Elle kopya ikisinin de yerine geçmez.

## Tuzaklar

- ⚠️ **`RequireComponent` Graphic'ten MİRAS ALINMAZ.** Özel bir `Graphic` türevi kendi
  `[RequireComponent(typeof(CanvasRenderer))]`'ını taşımak zorundadır; yoksa `AddComponent`
  hiçbir şey çizmeyen bir grafik üretir ve obje pasifleşirken `MissingComponentException` fırlatır.
- ⚠️ **TMP `Ellipsis` DİKEY de keser.** Metin kutusu fontun satır kutusunu kapsamalıdır (Saira
  Condensed 24 px ≈ 38 px); daha kısa bir rect'te metin kısalmaz, **tamamen kaybolur**. Kutuyu
  büyütüp midline hizalamak görsel konumu korur.
- ⚠️ **Düğme/rozet rengi ekrandan ayarlanmaz.** Düğme çalışırken tür değiştiriyor (BİTİR → BİTİR?,
  tehlike → onay) ve elle boyanmış dört katmanı geri almak gerekiyor: tek kapı
  `UiButtonStyle.SetKind`/`SetInteractable`, `UiChip.Set`.
- ⚠️ **`raycastTarget` `Graphic`'in `true` varsayılanında kalır** (serileşen taban alanı; constructor
  ya da `Reset` ondan geri yazar). Builder'ın fabrika yardımcıları şekil/yazı/ikon başına **kapatır**,
  tıklanabilir yüzey kendi açar. Yeni bir dekoratif grafik eklerken kapatmayı unutma: üstündeki
  düğmenin tıklamasını yutar.
- ⚠️ **`UiKit` paleti Girdap ekranlarında kullanılmaz** (`Assets/_Shared/App/Scripts/UiKit.cs`
  yalnız elle düzenlenen ekranlar ve sahne işaretçileri içindir). Admin tarafında ondan kalan tek
  çağrı `UiKit.EnsureEventSystem()`'dir — arena sahnelerinde `EventSystem` yoktur.
- ⚠️ **Serileşen enum'a yeni değer SONA eklenir** (`UiButtonKind` · `UiChipKind` ·
  `UiGradientMode` · `GirdapFont`): Unity sayısal indeks saklar, araya giren değer üretilmiş her
  prefabın görünümünü kaydırır.
- ⚠️ **Var olan TMP font asset'i yeniden üretilmez.** Taze atlas, kayıtlı prefablardaki glif
  indekslerini geçersiz kılar ve yazılar yeniden import edilene kadar boş çizilir; menü bu yüzden
  mevcut asset'e dokunmaz.
- ⚠️ **Tam ekran panel kartı merkeze ankrajlanır** (`PlaceCenter`), sol-üstten `Place` ile değil:
  CanvasScaler `Expand` 16:9 dışı pencerede tuvale fazladan alan verir ve sol-üstten konan kart
  merkezden kayar. Kenara yapışan HUD parçaları (kolonlar, akışlar, çubuklar) kendi kenarına
  ankrajlıdır, onlar etkilenmez.
- ⚠️ **Gölge/parıltı CSS gibi Gauss'tur** (`UiShape.Glow`): `w` blur yarıçapı, σ = w/2. Kalın bir
  plaka kenarında alfanın yarısını, 2 px bir çizgi onda birinden azını alır — bölüm başlığı
  çizgisinin "parlaması" istenmiyorsa alfayı değil kalınlığı düşün; CSS de aynı sonucu verir.
- ⚠️ **Metin parıltısı (TMP Underlay) çalışma anında keyword açarak yapılmaz.** `UNDERLAY_ON`
  bir `shader_feature`'dır: build'de o varyantı yalnız bir **material asset** kullanıyorsa kalır,
  yoksa atılır ve editörde görünen parıltı gözlükte/admin build'inde sessizce kaybolur. Kapı
  builder'ın `TextGlow(...)` preset'idir; çalışma anında yalnız `fontMaterial` örneğinin
  `_UnderlayColor`'u değişir (maç sonu kelimesinin tonu).
- ⚠️ **Parıltı yarıçapı atlas dolgusuyla sınırlıdır.** Underlay'in yumuşaklığı SDF yayılımını
  aşamaz: 286 px kelimede ~30 px halo çıkar, 48 px saatte birkaç px — CSS'in 24 px'i birebir
  gelmez, fontu yeniden üretmeden büyütülemez (bkz. "Var olan TMP font asset'i yeniden üretilmez").
- ⚠️ **Kayan şerit editörde durur.** `UiStripes.Drift(sn)` fazı `Update`'te ilerletir; Play dışında
  ve pasif nesnede şerit sabittir, hareket ancak oyunda görülür — editör önizlemesinden "animasyon
  yok" sonucu çıkarılmaz. CSS karşılığı `drift` keyframe'i; süre iki tarafta aynı sayıdır.

## Renk ve yazı

- **Paletin tek yeri `Girdap`** (`Assets/_Shared/Core/UI/Girdap.cs`): yüzeyler, yazı tonları, takım
  renkleri, durum renkleri, düğme çerçeve/dolgu çiftleri, tablo/akış tonları. Alan adları ve CSS
  karşılıkları dosyanın kendisinde; çağrı yerinde elle renk seçilmez — kopyalanan bir literal token
  değişince sessizce sapar ve arayüzde iki ayrı "kırmızı" doğar.
- **Takım rengi üç yardımcıdan gelir:** `Girdap.TeamHi(team)` (gradyan tepesi) ·
  `TeamLo(team)` (gradyan tabanı) · `TeamInk(team)` (koyu zeminde yazı). Takımsızda nötr griye
  düşerler. ⚠️ Düz takım kırmızısı/mavisi (`Girdap.Red`/`Blue`) avatar materyali ve `UiKit` ile
  **birebir aynıdır**: oyuncu eşlemeyi avatardan öğreniyor, arayüzde başka bir ton eşlemeyi koparır.
- **Yazı rolleri `GirdapFont`'tur**, font asset'i `Girdap.Font(...)` ile çözülür. Punto, harf
  aralığı (`Girdap.Spacing(em)`) ve hizalama builder'da durur. Dinamik metni büyütmek için
  `Girdap.Upper` kullanılır: TMP'nin `UpperCase` stili ve `ToUpperInvariant` `i → I` yapar, oyuncu
  adları `İ` ister.
- **İkonlar `GirdapAssets` kabından** gelir (`Girdap.Icon("<Ad>")`, dosyada `Ic_<Ad>.png`). Eksik
  ikon ad başına tek uyarı basar ve çizilmez.

## Ekran başına notlar

- **AdminHud** — üst şerit: TERCİHLER `[P]`, skor plakası (kırmızı skor · süre · mavi skor),
  İSTATİSTİK `[I]` çipi, kamera segmentleri SERBEST `[2]` / KUŞ BAKIŞI `[3]`. ⚠️ `modeButtons[0]`
  (POV) yuvası bilerek **boştur**: POV karttan/klavyeden girilir, o kipte hiçbir segment yanmaz.
  İki takım sütunu, sütun başına en çok `maxRowsPerColumn` kart (fazlası "+N oyuncu daha
  (istatistiklerde)"), sütun başlığında takım adı · oyuncu sayısı · "N KALİBRESİZ" çipi; takımsız
  kipte tek sütun. Solda ihlal akışı (`AdminViolationFeedView` + havuzlu `AdminViolationFeedRow`),
  sağda öldürme akışı (`AdminKillFeedView` + `AdminKillFeedRow`). Alt şerit `AdminMatchControls`
  (BAŞLAT · DURAKLAT/DEVAM · BİTİR→BİTİR? · İPTAL) + KAT grubu (`AdminFloorControls`; yalnız
  `ArenaFloors.Count > 1` iken görünür, şerit onunla genişler). İstatistik ve tercihler panelleri
  bu prefaba **gömülüdür**, görünürlüğü `AdminSession.OpenPanel` sürer.
- **AdminPlayerRow** (yan sütun kartı) — forma numarası plakası (takım gradyanı; `0` → boş), ad
  (takım mürekkebi) + `#playerId`, durum çipi, can sayısı + dilimli çubuk, telemetri satırı (K/D ·
  gözlük pili · iki kumanda tiki · gövde ikonu) ve beş düğme: POV · ÖLÇ · takım (MAVİ/KIRMIZI) ·
  SIFIRLA · AT (EMİN? onayı). Çerçeve önceliği: ihlal > seçim > kalibresiz > normal; yeniden
  bağlanan/ayrılan kart soluklaşır. ⚠️ Karttaki SIFIRLA istatistik satırındakiyle **aynı komut ve
  aynı dilbilgisidir** (`HoldButton`: kısa basış yumuşak, basılı tutma kaydı da siler) — biri
  değişirse öteki de; KALİBRE (geri yükleme) yalnız istatistik satırındadır. ⚠️ Beş eşit sütun
  düğme başına ~63 px bırakır: karttaki etiketler kısa tutulur (ölçüm hatası `HATA`).
  ⚠️ Kartın yalnız yüksekliği sabittir; genişliği sütundan gelir, bu yüzden her parça ya kenara
  çapalıdır ya `Bind`'da ölçülür.
- **AdminStatsRow** — plaka · ad + `#playerId` · durum çipi · öldürme/ölüm/K-D/skor · pil ·
  kumanda · gövde · kat · ping · ihlal defteri çipleri. Düğmeler: **KALİBRE** (kalibresizse
  "KALİBRE !" uyarı dolgusuyla; yüklenirken YÜKLENİYOR, sonra TAMAM/HATA; zemin sapması
  `ArenaProtocol.CALIB_FLOOR_WARN_METERS`'i aşarsa yalnız yazı mürekkebi uyarıya döner —
  kalibresiz bağırır, sapan yalnız boyanır) · **ÖLÇ** (ölçülmüşse ×ölçek) · **kalem ikonlu düğme**
  (ad/numara) ·
  **SIFIRLA** · **AT** (EMİN?). ⚠️ Kalibresizlik **durum çipine yansımaz**, uyaran KALİBRE
  düğmesidir. ⚠️ SIFIRLA tek düğmede iki kip taşır (`HoldButton`): kısa basış hizalamayı geçersiz
  kılar, basılı tutma gözlükteki kaydı siler (SİLİNİYOR → SİLİNDİ) — basış süresi onayın kendisidir,
  üstüne ikinci adım konmaz.
- **AdminStatsPanel** — tek tablo; takımlı kipte takım grup satırları (takım adı · "N oyuncu · M
  canlı" · takım öldürme/ölüm/skor), takımsız kipte grup satırsız aynı tablo. Bilgi şeridi faz ·
  kalan · mod · harita · süre · skor limiti · sunucu · poz akışı · bağlı admin taşır (sığmazsa kod
  sıkıştırır). Alt düğmeler: GÖVDE YENİLE · TÜMÜNÜ ÖLÇEKLENDİR · TÜMÜNÜ KALİBRE ET. ⚠️ Toplu bir
  "hizalamaları sıfırla" düğmesi **yoktur**. Esc / `I` kapatır.
- **AdminPreferencesPanel** — sekmeler MAÇ · GÖRÜNÜM · BAĞLANTI · SES (aktif sekme
  `UiButtonKind.SegOn`, diğerleri `Tab`); içerik dropdown'lar (liste şablonundan klonlanır), iki
  durumlu anahtarlar (KAPALI/AÇIK), adımlayıcılar ve kalibrasyon kipi düğmeleri. Esc kapatır.
- **MatchResultOverlay** (oyuncunun maç sonu ekranı, `VortexArena.App`) — prefab kökü dünya uzayı
  `Canvas` + `HudFollow`; derinlikten bağımsız çizim (`UiDrawOnTop`) ve duvar kaçınması **koddan**
  gelir, prefabta ayarlanmaz. Önce **sonuç kartı**: MAÇ SONUCU şeridi, sonuç kelimesi (vertex gradyanı
  sonucun tonunu taşır), kazanan satırı, skor plakası. Sonra **skor tablosu**: başlıkta kazanan
  satırı + skor plakası, gövdede takım başına bir blok (başlık plakası + şablondan klonlanan
  satırlar: sıra · ad · SEN çipi · `#id` · skor/öldürme/ölüm/K-D), altta oyuncunun kendi şeridi.
  Kendi satırı vurgulanır, ayrılan oyuncunun satırı soluklaşır. ⚠️ Eski kolon metinleri (`Column0..5`
  + `Header0..5`) **bağlı ve çalışır durumda tutulur**: bir mod varyantı onların etrafına
  giydirilmişse `boardRowTemplate` alanını boşaltmak tabloyu kolonlara döndürür.
  **Görünümü veriden gelir:** satır ölçüleri, satır/kendi-satırı dolgu-kontur-mürekkep renkleri,
  ko-op özet biçimi, sonuç tonuyla boyama ve ayrı ayrı yazılabilen ko-op/tablo metin hedefleri
  bileşenin kendi alanlarıdır; varsayılanları Girdap görünümünü verir, bağlanmayan alan eski
  birleşik metnin yazılmasına döner. Aşçı modunun ekranı bu alanları builder'dan doldurur
  (`.LokantaResult`), bu yüzden `MatchResultOverlay` kodunda `modeId`'ye bakan bir dal yoktur.
- **HealthHud** (oyuncunun kafaya kilitli şeridi, `VortexArena.Core.UI`) — saat (`Clock`, süre
  yokken kapalı) · can şeridi (`HpBar`, `HealthStrip`) · skor bandı (`RoundScore`, `TeamScorePanel`;
  TUR çipi `RoundFrame` etiket boşken kapanır) · durum plakası (`Status`, `StatusPlate`) · tur
  sonucu (`RoundResult`, `RoundResultBanner`). ⚠️ Builder **yerinde** düzenler ve `RoundScore` ile
  `RoundResult` düğümlerini **korur**: mod HUD'ları bu prefabı iç içe örnekler, mod denetleyicileri
  o iki düğüme bağlıdır ve Mole kendi metinlerini `RoundScore/Panel` altına ekler — düğüm
  silinse referanslar ve eklenen çocuklar sessizce kaybolurdu. Üretim sonunda `Assets/Modes/**`
  altındaki her `ModeHudBase` prefabının alanları yeniden bağlanır (`BindModeHuds`); Mole'da
  `HpBar` kapatılıp band yukarı alınır ve vuruş sayaçları yeniden biçimlenir. ⚠️ **İçinde
  `HealthHud` örneği olmayan mod prefabı bu geçişte ATLANIR** — şeritten çıkmanın tek yolu budur
  (aşçı modu böyle çıkar ve kendi şeridini kurar). Kaynak `oyuncu-hud.html`.
- **DeathHud** (öldün kartı) — kırmızı vinyet + çentikli kart: MAÇ SÜRÜYOR etiketi, ÖLDÜN (beyaz →
  kırmızı vertex gradyanı + hale), katil satırı (`Card/KillerLine`, `richText` açık — ad takım
  mürekkebiyle `<noparse>` içinde gelir), durum plakası (`Card/StatusFrame`). Kart mod HUD
  canvas'ında durur, şerit kafada: üst üste binmezler. CSS'teki `skewX(-7deg)` eğimi yoktur — TMP
  italik eğimi font asset'inden gelir, metin başına verilemez.

## Aşçı (Burger) — lokanta teması

Aşçı modunun arayüzü Girdap ekranlarıyla aynı kitten (`UiShape`/`UiPolygonGraphic`) çizilir ama
**kendi paletini ve kendi fontlarını** kullanır: palet `Lokanta`, fontlar Fredoka (başlık/sayı) ve
Nunito (küçük kapitaller). Girdap ekranları bu palete ve bu fontlara dokunmaz, tersi de geçerli.

**Ölçünün kaynağı mockup'tır ve kalıcıdır:** `Docs/Gelistirici/Arayuz/asci.html` (+ kendi
`asci.css`'i; gezinme `tema.css`/`tema.js` ile ortaktır) — oyuncu HUD'u için `oyuncu-hud.html` ne
ise lokanta için odur. Kareleri: vardiya şeridi · sipariş baloncuğu · köfte termometresi · vardiya
sonu kartı · günün hesabı tablosu · durumlar. Görsel değişiklik **önce mockup'ta** yapılır, sonra
builder'a geçirilir.

**Modun arayüz prefablarının hepsi builder çıktısıdır** (`Tools > VortexArena > UI > Girdap`; elle
düzenleme bir sonraki üretimde kaybolur, Girdap prefablarıyla aynı kural):

| Prefab | Menü | Ölçü |
|---|---|---|
| `Assets/Modes/Burger/UI/BurgerHud.prefab` (kökü `BurgerClientController`) | *Yalnız Aşçı HUD* | `LokantaStrip` 560×176 (kafaya kilitli; saat pili · ekip/sen/müşteri bandı · durum plakası) |
| `Assets/Modes/Burger/Prefabs/NO_customer.prefab` → `Bubble` | *Yalnız Aşçı sipariş baloncuğu* | dünya uzayı 420×300, ölçek 0,001 |
| `Assets/Modes/Burger/UI/LokantaThermometer.prefab` (`NO_patty` → `Thermometer` içine girer) | *Yalnız Aşçı termometre* | 100×200, ölçek 0,0005 |
| `Assets/Modes/Burger/UI/BurgerResultOverlay.prefab` (kökü `MatchResultOverlay`) | *Yalnız Aşçı sonuç ekranı* | kart **1400×860** — kart sanatı bu orana göre çizilir |

Modun sprite'ları (`Assets/Modes/Burger/UI/Sprites/`: yüz ifadeleri, yıldız, dama ve nokta
desenleri) *Lokanta sprite'larını üret* ile doğar; desenli olanlar `Repeat` sarmalıdır. Girdap
ikonlarından alev ve tik (`Ic_Flame` · `Ic_Check`) ortak kaba girer.

⚠️ **Hangi değer prefabta, hangisi kodda:** çalışma anında yazılan her hedef (metin, dolgu,
grafik, sprite, satır/dilim havuzları) bileşende `[SerializeField]` alandır ve bağlantıyı
**builder** `SerializedObject` ile yazar — elle sürüklenen bağ ilk üretimde silinir. Prefabta
yalnız **ölçü** yaşar: tüp boyu, baloncuk kartı, şerit genişliği; cıva ve yeşil bölge yüksekliklerini
bileşen ebeveyn rect'inden okur, ikinci bir sayı kodda tutulmaz.

⚠️ **Aşçı HUD'ı `HealthHud` örneği taşımaz** (canı yok): şeridini builder kurar, `BindModeHuds`
geçişi de bu yüzden onu atlar. Tabandan yalnız saat/çerçeve/durum plakası alanları bağlanır;
can/skor/müşteri sayacı gibi eski alanlar bilerek boştur — bağlı olmayan alan hata vermeden
çizilmez.
