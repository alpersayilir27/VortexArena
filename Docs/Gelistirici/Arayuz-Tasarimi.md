---
title: Arayüz tasarımı — 2D'yi nerede bulurum, nasıl düzenlerim
---

# Arayüz tasarımı (2D / UI)

Projedeki tüm arayüz **uGUI**'dir: `Canvas` + `TextMeshPro`. **UI Toolkit kullanılmıyor** —
projede tek bir `.uxml`/`.uss` yok, aramayın.

Admin ekranları ile oyuncunun maç sonu ekranı **"Girdap" arayüz kitiyle koddan üretilir**: prefab
bir çıktıdır, kaynak builder'dır. Oyuncunun görüş alanındaki HUD'lar, ölüm/bildirim ekranları ve
yükleme/bağlantı ekranları bunun dışındadır ve **elle düzenlenir**.

## Nerede ne var

**Kit** — `Assets/_Shared/Core/UI/` (asmdef `VortexArena.Core`, namespace `VortexArena.Core.UI`):

| Tip | Ne yapar |
|---|---|
| `UiPolygonGraphic` | Köşe kesimli (chamfer) + yan eğimli (slant) konveks poligon tabanı; yarım düzlem kırpma ve üçgen fan yardımcıları burada |
| `UiShape` | Temanın kutusu: dış çerçeve halkası + gradyan dolgu + dış/iç parıltı + 0,75 px kenar yumuşatma, **tek draw**. Akıcı API: `Chamfer` · `Slant` · `Fill` · `Outline` · `Glow` · `InnerGlow` · `Antialias` |
| `UiStripes` | CSS `repeating-linear-gradient` karşılığı çapraz şeritler, poligona kırpılı (takım plakaları, canlı ihlal bandı) |
| `UiSegmentBar` | Dilimli, eğik can çubuğu (`SetFill` · `SetFillColors` · `SetTrack` · `SetMetrics`) |
| `UiButtonStyle` | Bir düğmenin TÜM görünümü: `SetKind(UiButtonKind)` · `SetInteractable` · `SetLabel` · `SetHold(0..1)`; parçaları `Bind(...)` bağlar |
| `UiChip` | Rozet: `Set(text, UiChipKind, icon)`; genişliğini kendi ölçer |
| `Girdap` | Palet ve yardımcılar: `Hex` · `Rgba` · `TeamHi/TeamLo/TeamInk(team)` · `Spacing(em)` · `Upper` · `Font(GirdapFont)` · `Icon(name)` · `Assets` |
| `GirdapAssets` | ScriptableObject kabı: TMP fontları + ikon sprite'ları |
| `UiButtonKind` · `UiChipKind` · `UiGradientMode` · `GirdapFont` | Prefablara **serileşen** enum'lar |

**Builder** — `Assets/_Shared/App/Scripts/Editor/GirdapUi/`, `partial static class GirdapUiBuilder`
(ns `VortexArena.App.Editor`). `GirdapUiBuilder.cs` ortak yardımcıları taşır
(`Node` · `Place`/`PlaceRight`/`PlaceTopCenter`/`PlaceMiddleLeft`/`PlaceBottom*` · `Stretch`/
`StretchTop`/`StretchBottom`/`StretchLeft` · `Shape`/`ShapeBox` · `Stripes` · `SegmentBar` ·
`Image` · `Icon` · `Text` · `Kbd` · `Button` · `Chip` · prefab kaydetme), ekran başına bir kardeş
partial durur (`.Assets` · `.PlayerRow` · `.Stats` · `.Preferences` · `.Hud` · `.MatchResult`).

**Menü** — `Tools > VortexArena > UI > Girdap`: *Fontları ve asset kabını üret* · *Tüm prefabları
üret* · *Yalnız `<Ekran>`*. Toplu üretimin sırası PlayerRow → StatsRow → StatsPanel →
PreferencesPanel → Hud → MatchResult'tır; HUD, panelleri ve satır prefabını içine gömer.

**Üretilen prefablar** — `Assets/_Shared/App/Resources/UI/`: `AdminPlayerRow` · `AdminStatsRow` ·
`AdminStatsPanel` · `AdminPreferencesPanel` · `AdminHud` · `MatchResultOverlay`. Builder prefabı
**yerinde** düzenler: içeriği yükler, kendi ürettiği çocukları siler, yeniden kurar, alanları
`SerializedObject` ile bağlar, kaydeder.

**Asset kabı** — `Assets/_Shared/App/Resources/UI/Girdap.asset` (`GirdapAssets`): fontlar + ikon
dizisi + desen sprite'ları. `Girdap.Assets` onu `Resources`'tan tembel yükler; yoksa arayüz
fontsuz/ikonsuz çizilir ve konsola tek seferlik hata düşer.

**Fontlar** — kaynak TTF'ler `Assets/_Shared/App/UI/Fonts/`, menünün ürettiği TMP SDF asset'leri
`Assets/_Shared/App/Resources/UI/Fonts/` (dinamik atlas; Türkçe glifler ve ayraçlar öne pişirilir).
**İkonlar** — `Assets/_Shared/App/UI/Sprites/Ic_<Ad>.png`; kodda **çıplak adla** istenir
(`Girdap.Icon("Skull")`).

**Mockup'lar** — `plan/arayuz-yenileme/` (ortak stil `tema.css`, parça kiti `kit.html`, ekran
başına bir HTML). Her ölçünün kaynağı orasıdır; plan maddesi açık olduğu sürece orada durur.

**Girdap dışında kalan arayüz** (elle düzenlenen prefablar, hepsi
`Assets/_Shared/App/Resources/UI/` altında): `HealthHud` (kafaya kilitli şerit: can + saat + durum
+ takım skoru + tur sonucu) · `DeathHud` · `RoundNoticeHud` · `LoadingOverlayScreen`/`…World` ·
`ConnectionOverlayScreen`/`…World` · `AdminPlayerMarker`. Mod HUD'ları mod
kutusundadır (`Assets/Modes/<Mod>/UI/<Mod>Hud.prefab`) ve içleri bilerek boştur: taşıdıkları şey
yukarıdaki iç içe prefab örnekleridir. Cephane göstergesi silahın kendi üstündedir
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
  şerit → `Stripes(...)`, can çubuğu → `SegmentBar(...)`.
- **Yeni bir alan bağlanacaksa** bileşene `[SerializeField]` eklenir ve builder onu
  `SerializedObject` üstünden yazar — elle sürüklenen bağ ilk üretimde silinir.
- **Satır yüksekliği koddan okunur:** `AdminPlayerRow.Height` · `AdminStatsRow.Height` ·
  `AdminKillFeedRow.Height`/`NewHeight` · `AdminViolationFeedRow.Height`/`LiveHeight` ·
  `MatchResultOverlay.RowHeight`/`RowGap`/`RowsTop`. Yerleşim bu sabitlerden türer; prefabtaki rect
  ile sabit **birlikte** değişir.
- **Mod varyantları tabandan miras alır.** `MatchResultOverlay` builder'ı varyantların ezdiği
  düğümleri silmez, yeniden ebeveynler (`ResultKeptNodes`) — silinen bir düğüm varyantın
  override'ını sarkıtır ve o modun sanatı kaybolur.

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
  gözlük pili · iki kumanda tiki · gövde ikonu) ve dört düğme: POV · ÖLÇ · takım (MAVİ/KIRMIZI) ·
  AT (EMİN? onayı). Çerçeve önceliği: ihlal > seçim > kalibresiz > normal; yeniden bağlanan/ayrılan
  kart soluklaşır. ⚠️ Kartta **kalibrasyon düğmesi yoktur** — o iş istatistik satırındadır.
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
  `Canvas` + `HudFollow`. Önce **sonuç kartı**: MAÇ SONUCU şeridi, sonuç kelimesi (vertex gradyanı
  sonucun tonunu taşır), kazanan satırı, skor plakası. Sonra **skor tablosu**: başlıkta kazanan
  satırı + skor plakası, gövdede takım başına bir blok (başlık plakası + şablondan klonlanan
  satırlar: sıra · ad · SEN çipi · `#id` · skor/öldürme/ölüm/K-D), altta oyuncunun kendi şeridi.
  Kendi satırı vurgulanır, ayrılan oyuncunun satırı soluklaşır. ⚠️ Eski kolon metinleri (`Column0..5`
  + `Header0..5`) **bağlı ve çalışır durumda tutulur**: bir mod varyantı onların etrafına
  giydirilmişse `boardRowTemplate` alanını boşaltmak tabloyu kolonlara döndürür.
