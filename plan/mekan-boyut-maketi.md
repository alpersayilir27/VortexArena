# Mekan boyut maketi — kalan iş

Kod, boyut dosyaları, prefablar, sahneler ve dokümanlar bitti. Kalan tek şey **doğrulamadır**;
kalıcı bilgi `CLAUDE.md` · `Docs/Sistem-Ozeti.md` (§4 bileşen sözlüğü, §7 tuzaklar) ·
`Docs/Gelistirici/{Yemek-Kitabi,Sahne-Kurulumu,API-Referansi,Yapma-Listesi}.md` ·
`Docs/Isletme-Kurulum.md` içine işlendi.

## Doğrulama

- Bir arena sahnesini aç: konsolda `ArenaBoundary` hatası **olmamalı**, gizmo taban halkasını ve
  kolon prizmalarını çizmeli.
- `JSON'dan DimensionMesh Üret` → maket `ArenaBoundary`'nin altında, `EditorOnly` etiketli, kolon
  sayısı dosyayla aynı.
- Bir köşeyi ProBuilder ile oynat → `DimensionMesh'i JSON'a Çevir` → dosyada yalnız o köşe değişmiş
  olmalı (gidiş-dönüş kayıpsız).
- Boş sahnede `Template Temellerini Yükle` → muhafazanın `head`/`fadeRenderer`/`warningText`
  alanları dolmalı, taban şeritleri kırmızı/mavi gelmeli; ikinci çalıştırma hiçbir şeyi
  ikilememeli.
- `Configure All Build Elements` → `maps.json` tazelenmeli, sağlık raporu okunmalı.
- Play: alan kenarına yaklaşınca karartma **rampası** başlamalı, sınır aşılınca tam kararma +
  uyarı.
- Admin tercihler panelinde duvar saydamlığı satırı **olmamalı**, altındaki satırlarda boşluk
  kalmamalı; kuş bakışı kadrajı doğru kalmalı.

## Elde kalan yerleştirme işi

- `VA_ArenaRoot` prefabındaki `anchor_a`/`anchor_b` işaretçileri ±3,6 m'de duruyor (referans
  arenanın ölçüsü). Yeni bir arenada bunlar mekanın gerçek zemin işaretlerine göre taşınır.
- `VA_BaseZone` şeridi 1×12 m; farklı ölçüdeki arenada `halfExtentX`/`halfExtentZ` ile birlikte
  ölçeklenir.
