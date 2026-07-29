# Arena şablonları — OYNANMAZ içerik

Buradaki arenalar `Tools > VortexArena > Create Arena From Template` sihirbazının **kaynağıdır**;
sahada oynanmazlar.

- **Sunucuya export EDİLMEZLER.** `Tools > VortexArena > Export Server Config`
  `Assets/Arenas/Template/` altındaki `MapDefinition`'ları atlar — aksi hâlde sunucu açılışında
  şablonlar sahte bir mekan olarak listelenirdi.
- Aynı sebeple Build Settings'e ve `GameCatalog`'a da girmezler.
- Oynanacak her arena bir **mekana** aittir: `Assets/Arenas/Venues/<İşletme>/<Arena>/`.

`Default12x12` = harita dizaynı taşımayan, yalnız ağa bağlanmak için gerekenleri içeren tek kaynak
arena. Yeni arena üretmenin yolu onu elle kopyalamak değil, sihirbazı çalıştırmaktır.
