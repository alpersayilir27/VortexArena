# Arena şablonları — OYNANMAZ içerik

Buradaki arenalar bir **referanstır** — "ağa bağlanmış bir sahne neye benzer" sorusunun cevabı;
sahada oynanmazlar.

- **Sunucuya export EDİLMEZLER.** `Tools > VortexArena > Export Server Config`
  `Assets/Arenas/Template/` altındaki `MapDefinition`'ları atlar — aksi hâlde sunucu açılışında
  şablonlar sahte bir mekan olarak listelenirdi.
- Aynı sebeple Build Settings'e ve `GameCatalog`'a da girmezler.
- Oynanacak her arena bir **mekana** aittir: `Assets/Arenas/Venues/<İşletme>/<Arena>/`.

`Default12x12` = harita dizaynı taşımayan, yalnız ağa bağlanmak için gerekenleri içeren referans
arena. ⚠️ **Yeni arena bunu kopyalayarak üretilmez** (sahne kopyalayan sihirbaz kaldırıldı): boş
bir sahne açılır ve `Tools > VortexArena > Template Temellerini Yükle` ile donatılır — altyapı
prefab **örneği** olarak gelir, yani rig/kalibrasyon kurulumundaki bir düzeltme bütün arenalara
kendiliğinden ulaşır. Kopyalanan sahnede bu bağ kopardı.
