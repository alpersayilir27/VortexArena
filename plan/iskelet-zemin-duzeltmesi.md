# İskelet konum sabitlemesi — kalan doğrulama

Düzeltme kodda: `ArenaNetCharacterBehaviour.UpdateHeadPin` (alıcıda, gövde poz kanalının kafasına
sabitlenir; gerekçe `Docs/Sistem-Ozeti.md` §7 "iskelet kökü ... alıcıda poz kanalına sabitlenir"
maddesi).

## Yapılacaklar

- Editörde doğrulama (admin + APK oyuncu kurulumu): `[AvatarTanı]`'da `kök` kafanın dibine oturmalı
  (kök y ≈ 0, `sınırMerkez` ≈ kafa), kafa hızlı dönerken gövde savrulmamalı — hem origin arenada
  hem VortexAntep'te.
- Gözlük YENİDEN BAŞLATILDIKTAN sonra VortexAntep tekrarı: "kök donmuş" arıza kipi (body
  tracking'in app-açılışında tutmaması) sağlıklı oturumda kaybolmalı; tekrarlıyorsa gönderici
  logcat'inden body tracking durumuna bakılacak.
- `bodyScale ≠ 1` ile bir tur: `ScalePointAboutRoot` ölçekleme merkezi çizilen köke taşındı —
  büyütülmüş uzak avatarda elde çizilen silah elden kopmamalı.
- Sahaya iniş: oyuncu APK + admin build (değişiklik alıcıda — TÜM alıcılar yenilenmeli).
