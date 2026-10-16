# Ara sokak arenası — yeni siper yerleşimi ve prop kiti

Tokyo ara sokağı temalı arenanın oyun alanındaki tekrar eden otomatlar ve makineler kaldırılır,
yerine üç koridorlu (sol · orta · sağ), takımlar için 180° nokta simetrik bir siper yerleşimi
kurulur. Propların kaynağı `blender/JapanProps/` (README orada), çıktısı ve prefabları
`Assets/Maps/Japan/Props/`.

## Yerleşim

- Koordinatlar: `plan/sokak-prop-kiti/yerlesim.txt` — satır başına `Tür x z rotY ayna`
  (arena dünya koordinatı, metre; `ayna = 1` → `(4.4 − x, 4.6 − z, rotY + 180)` kopyası da konur).
  Konsept görseli `blender/JapanProps/ref/L1_layout_concept.jpg`.
- Tür kodları: `B` yatai · `C` gachapon sırası · `E` sake fıçısı yığını · `G` koli yığını ·
  `J` şantiye paneli · `K` mevcut vinçli oyuncak makinesi (bir tane kalır).
- ⚠️ Mekanın fiziksel kolonlarını saran iki otomat yerinde kalır; yerleşim onlara göre kuruldu.
- Kabul ölçütleri (yerleşim değişirse yeniden ölçülür): yürüme geçidi ≥ 0,9 m, iki üsten bütün
  koridorlara erişim, üs bölgeleri arasında ayakta görüş yok.

## Yapılacaklar

1. Işık bake'i. ⚠️ Kural: oyun alanında aynı prefab en fazla 2 kez kullanılır;
   yerleşim dosyası (`yerlesim.txt`) ilk dizilimi tutar, tekrarı kıran değişimler (dolap, pirinç,
   elektrik dolabı, sıcak yemek otomatı) sahnededir — sahnenin sahibi sahnedir. Sahne hazır: yerleşim `JP_CoverLayout` altında, proplar Static
   (Contribute GI · Batching · Reflection Probe), renderer'lar `Lightmaps` kipinde. İstisna vinçli
   oyuncak makinesidir (`K`): modeli bozuk indeksli bir alt mesh taşıdığı için Contribute GI kapalı,
   `Receive GI = Light Probes` — lightmap'e hiç girmez, light probe'larla aydınlanır
   (gerekçe → Sistem Özeti, Tuzaklar "Bozuk indeksli mesh bütün lightmap atlasını beyaz yapar").
   ⚠️ Bake alınana kadar bu proplar lightmap'siz
   kalır — dolaylı ışık almaz, sahnenin geri kalanından koyu görünür; bake'siz build alınmaz.
2. Gözlük doğrulaması (Notion kartı).
