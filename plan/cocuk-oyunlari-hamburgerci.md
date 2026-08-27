# Çocuk Oyunları — Hamburgerci: kalan iş

Kod, protokol ve doküman yerinde. Sistemin anlatımı dokümanlarda: mod sözleşmesinin tamamı
(türler, olaylar, `stage`/`s`, servis jesti, red yolu, skor kanalları, `modeState`)
`Docs/ArenaNet-Protokol.md` §10.5 · sunucu ve istemci bileşenlerinin sorumlulukları
`Docs/Sistem-Ozeti.md` §4 (`Modes/BurgerMode` + `VortexArena.Modes.Burger` kutusu) · kural şekli
`Server/README.md` mod tablosu · yeni bir çocuk oyununun reçetesi
`Docs/Gelistirici/Yemek-Kitabi.md` "Çocuk oyunu eklemek".

## Kalan içerik işi

⚠️ **Sahnedeki dükkân ve bütün eşyalar prototiptir:** kutu/silindir primitifleriyle kurulmuş banko,
ızgara, kesme tahtası, montaj masası, dağıtıcılar, müşteri kapısı, malzemeler ve kapsül müşteri.
Gerçek modeller gelince yerine geçecek; **yerleşim korunmalı** — istasyon bileşenleri, banko
slotlarının hacimleri ve müşteri yolu ona bağlı. Her banko slotunda bir servis tahtası durur
(taşınacak bir tahta yok). İç engeller `Obstacle` layer'ındadır, müşterinin collider'ı **yoktur**
(free-roam alanda gerçek bir bedeni engellemesin diye).

- [ ] **Gerçek modeller + animasyonlar.** Malzemeler ve servis tahtası `BurgerKit`'ten geliyor;
      kitte **olmayanlar** ayrı içerik işidir: müşteri (yürüme/bekleme/mutlu/mutsuz animasyonlarıyla),
      bütün ekmek, bıçak, spatula, ızgara, kesme tahtası, dağıtıcılar ve banko.
      ⚠️ Yerleşim korunmalı: bileşenler ona bağlı.
- [ ] **Dört prop tanımının kavrama pozu** (`Kavrama Pozu Stüdyosu`) — yazılmadan obje ele gelir ama
      kumanda anchor'ında durur.
- [ ] **Sesler:** servis tahtasının red sesi (`BurgerServingBoard.rejectSound`) ve müşteri
      gelme/gitme sesleri.

## Doğrulama (kullanıcı koşar)

- İki başlık: biri ekmeği keser, diğeri iki yarımı aynı anda görür; köfte iki başlıkta aynı anda
  pişmiş olur, renk tonu aynı.
- Aynı bıçağı iki kişi aynı anda tutmaya çalışır: biri alır, diğerinin eli boş kalır, çalamaz.
- Bankodaki tahtaya doğru sırayla dizip **üst ekmeği koyunca** servis olur: servis edenin puanı +
  toplam artar, müşteri mutlu gider, `h` artar. Yarım yığın (üstte ekmek yokken) hiç rapor edilmez.
  Yanlış tarifte üst ekmek konunca red gelir, müşteri bekler; sabrı dolunca mutsuz gider, `u` artar,
  puan düşmez.
- Dolu elle dağıtıcıyı sıkmak yeni malzeme doğurmaz; malzeme doğar doğmaz ele yapışır (havada
  asılı kalmaz). Kesilen ekmeğin iki yarısı iç içe geçmez.
- Admin HUD'ında mutlu/mutsuz sayaçları oyuncu HUD'ıyla aynı.
- Geç katılan başlık: bekleyen müşterileri (siparişleriyle) ve masadaki malzemeleri doğru yerde görür.
- Sahip koparsa elindeki malzeme yere düşer (serbest kalır), oyun sürer.
