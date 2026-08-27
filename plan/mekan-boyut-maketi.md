# Mekan boyut maketi — kalan iş

Kod, boyut dosyaları, prefablar, sahneler ve dokümanlar bitti. Kalan tek şey **doğrulamadır**;
kalıcı bilgi `Docs/Sistem-Ozeti.md` (§4 bileşen sözlüğü, §7 tuzaklar) ·
`Docs/Gelistirici/{Yemek-Kitabi,Sahne-Kurulumu,API-Referansi,Yapma-Listesi}.md` ·
`Docs/Isletme-Kurulum.md` içine işlendi.

## Doğrulama

- Bir arena sahnesini aç: konsolda `ArenaBoundary` hatası **olmamalı**, gizmo taban halkasını ve
  kolon prizmalarını çizmeli.
- `JSON'dan DimensionMesh Üret` → maket **`ArenaBoundary`'nin altında**, yerel-kimlikte (muhafaza
  yoksa sahne kökünde, dünya orijininde ve dönüşsüz); kolon sayısı
  dosyayla aynı, `Plane` ölçüsü dosyadaki ölçüyle birebir; `anchor_a` (kırmızı) / `anchor_b` (mavi)
  küpleri `calibration` noktalarında. Maketin **kökü ve kalibrasyon küpleri build'e girer**
  (kalibrasyon onlara bağlı); yalnız görsel dalı (`Plane` + `Columns`) `DimensionMeshBuildStripper`
  ile build'e giden geçici sahne kopyasından silinir — o dal `ProBuilderMesh` taşıyor ve
  `Unity.ProBuilder`'ı runtime derlemesine sokardı. Editör Play kipinde aynı dalın
  `Renderer.enabled`'ını `ArenaDimensionMesh.Awake()` kapatır, yani Play'de görsel
  **görünmemeli** ama küpler sahnede durmalı.
- Bir köşeyi ProBuilder ile, bir de kalibrasyon küpünü sürükleyerek oynat →
  `DimensionMesh'i JSON'a Çevir` → dosyada yalnız o iki değer değişmiş olmalı (gidiş-dönüş
  kayıpsız).
- Boş sahnede `Template Temellerini Yükle` → muhafazanın `head`/`fadeRenderer`/`warningText`
  alanları dolmalı, taban şeritleri kırmızı/mavi gelmeli, `anchor_a`/`anchor_b` dosyadaki
  noktalara oturmalı (rapor A–B mesafesini yazar); ikinci çalıştırma hiçbir şeyi ikilememeli.
- `VA_CalibrationManager`'ın `anchorA`/`anchorB` alanlarını **boşalt** → Play → konsolda
  "işaretçiler bulunamadı" uyarısı **olmamalı** (adlarından çözülmeli) ve maketin küpleri
  seçilmemeli.
- Play'de A→B kalibrasyonu al: konsolda `1/2 — A yakalandı` ve `2/2 — B yakalandı` satırları,
  yazılan sanal konumlar dosyadaki noktalarla aynı olmalı.
- `Configure All Build Elements` → `maps.json` tazelenmeli, sağlık raporu okunmalı.
- Play: alan kenarına yaklaşınca karartma **rampası** başlamalı, sınır aşılınca tam kararma +
  uyarı.
- Admin tercihler panelinde duvar saydamlığı satırı **olmamalı**, altındaki satırlarda boşluk
  kalmamalı; kuş bakışı kadrajı doğru kalmalı.
