# Çocuk Oyunları — Hamburgerci: kalan iş

Kod, protokol ve doküman yerinde. Sistemin anlatımı dokümanlarda: mod sözleşmesinin tamamı
(türler, olaylar, `stage`/`s`, servis jesti, red yolu, skor kanalları, `modeState`)
`Docs/ArenaNet-Protokol.md` §10.5 · sunucu ve istemci bileşenlerinin sorumlulukları
`Docs/Sistem-Ozeti.md` §4 (`Modes/BurgerMode` + `VortexArena.Modes.Burger` kutusu) · kural şekli
`Server/README.md` mod tablosu · yeni bir çocuk oyununun reçetesi
`Docs/Gelistirici/Yemek-Kitabi.md` "Çocuk oyunu eklemek".

Bu dosya yalnız **içerik** işini tutar; kod, protokol ve prefab alanları yerindedir.

## Kalan içerik işi

⚠️ **Sahnedeki dükkânın çoğu hâlâ prototiptir:** kutu/silindir primitifleriyle kurulmuş banko,
kesme tahtası, montaj masası, dağıtıcılar, müşteri kapısı ve kapsül müşteri.
Gerçek modeller gelince yerine geçecek; **yerleşim korunmalı** — istasyon bileşenleri, banko
slotlarının hacimleri ve müşteri yolu ona bağlı. Her banko slotunda bir servis tahtası durur
(taşınacak bir tahta yok). İç engeller `Obstacle` layer'ındadır, müşterinin collider'ı **yoktur**
(free-roam alanda gerçek bir bedeni engellemesin diye).

- [ ] **Gerçek modeller + animasyonlar.** Servis tahtası `BurgerKit`'ten, malzemeler ve ızgara asset
      paketinden geliyor. Kalanlar: müşteri (yürüme/bekleme/mutlu/mutsuz animasyonlarıyla), bütün
      ekmek, bıçak, spatula, kesme tahtası, dağıtıcılar ve banko.
      ⚠️ Yerleşim korunmalı: bileşenler ona bağlı.
- [ ] **Yüksek poligonlu malzeme modellerinin materyalleri** asset başına tek tek atanır — paketten
      gelen modeller materyalsiz geliyor.
- [ ] **Dört prop tanımının kavrama pozu** (`Kavrama Pozu Stüdyosu`) — yazılmadan obje ele gelir ama
      kumanda anchor'ında durur.
- [ ] **Taşıma ankorlarının gerçek modele göre ayarı** (`BurgerCarrier`): spatulanın `Cargo` hacmi +
      `CargoAnchor`'ı bıçağın, servis tahtasınınki yüzeyin ölçüsüne göre yazıldı; model değişince
      ikisi de yeniden konumlanır. `slotHeight` katman kalınlığına, `spillAngle` istenen dökülme
      hissine göre ayarlanır. ⚠️ Ankor yanlış yerdeyse yük görsel olarak modele gömülür — hata vermez.
- [ ] **Sesler** — alanlar prefabda hazır, klip bekliyor (boş alan sessizdir): servis tahtasının red
      sesi (`BurgerServingBoard.rejectSound`) · müşteri geldi/mutlu/mutsuz (`BurgerCustomer`
      `arriveSound`/`happySound`/`unhappySound`) · köfte cızırtısı ve pişti vuruşu (`BurgerPatty`
      `sizzleSource` loop'lu `AudioSource` + `cookedClip`) · dağıtıcı `takeSound` · bıçak `cutSound`.
- [ ] **Balon ölçüsü gerçek modele göre:** `NO_customer/Bubble/Panel/Dilimler` dilim kökü ve
      `BurgerOrderBubble.sliceSize` (canvas birimi, 0.001 ölçek) metin alanıyla birlikte modelin
      başına göre yeniden konumlanır; dilim renkleri (`sliceColors`) malzeme modellerinin renkleriyle
      eşitlenir.

Doğrulama listesi Notion'da: Todo → "Doğrulama 34 — Çocuk Oyunları: Hamburgerci (davranış testi)";
içerik işi "Çocuk oyunları + kavrama: kalan içerik işi" kartında.
