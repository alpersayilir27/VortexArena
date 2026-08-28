---
title: Yüzey çarpma efektleri
---

# Yüzey çarpma efektleri — kalan iş: içerik

Mermi neye çarptıysa **o yüzeyin** parçacığı ve sesi çıkar. Kod yerinde: yüzey kimliği
`SurfaceDefinition` + `SurfaceLibrary` + `SurfaceTag`'te, oynatma havuzlu `SurfaceImpactFx`'te, tek
kapı `ArenaCombat.ReportImpact`. Silahın kendi çarpma prefabı kalktı (`Weapon.hitEffectPrefab`
alanı silindi); uzak atışta çarpma noktası ışınla yerel çözülür, **protokole ekleme YOK**.
Sözleşmenin anlatımı `Docs/Sistem-Ozeti.md` §4.

## Kalan içerik işi

- [ ] `SurfaceLibrary.asset` (`_Shared/Data/Resources/`) + yüzey tanımları: kar, tahta, metal,
      beton, toprak ve `default`. ⚠️ `default` **boş bırakılmaz** — eşleşmeyen yüzeyde hiçbir şey
      çıkmaması "efekt bozuk" diye okunur, jenerik bir toz okunmaz. `FX_HitSpark.prefab`
      `default`'un prefabı olur.
- [ ] Her tanıma parçacık prefabı + çarpma sesleri (ses seviyesi ve perde bandı tanımın içinde).
      ⚠️ Tanımın ömrü parçacığın kendi ömründen kısa olursa efekt yarıda kesilir.
- [ ] Oynanan arenaların environment materyalleri tanımların materyal listesine eşlenir.
      ⚠️ Işın maskesizdir: eşlenmemiş her şey `default`'a düşer ve belirti "her yerde toz çıkıyor"
      olur — eşleme listesi arena bittiğinde gözden geçirilir.
- [ ] Oyuncuya isabet: kütüphaneye bir `body` tanımı, `RemoteHitBox` taşıyan collider'ların
      materyali ona eşlenir — kan/darbe efekti ayrı bir kod yolu açmadan gelir.

## Tuzaklar

- ⚠️ **Materyal örneği ≠ materyal asset'i.** Sözlük `sharedMaterial` okur; çalışma anında
  `renderer.material` bir KOPYA üretir ve hiçbir zaman eşleşmez.
- ⚠️ **Çok materyalli mesh DESTEKLENMEZ** (yalnız ilk materyal çözülür): hangi submesh'e vurulduğunu
  bilmek `hit.triangleIndex` ister, o da mesh'te Read/Write (bellek iki katı) + `MeshCollider` şartı
  koyar. O objeyi ikiye bölmek ya da `SurfaceTag` koymak yeterlidir.
- ⚠️ **Mermi deliği (decal) bu işin parçası değil, ayrı bir adım.** URP Decal Renderer Feature
  Quest'te tam ekran bir geçiş ekler; yapılacaksa havuzlu quad + sert bir tavan (en eski delik geri
  alınır) ile yapılır.

## Doğrulama

- [ ] Kar duvara, tahtaya ve metale sıkılınca üç ayrı efekt + üç ayrı ses çıkıyor.
- [ ] Materyali eşlenmemiş bir yüzey `default` efekti veriyor (hiçbir şey çıkmaması DEĞİL).
- [ ] `SurfaceTag` konmuş bir obje, materyali başka bir yüzeye eşli olsa da tag'i kazanıyor.
- [ ] Aynı materyal iki tanıma bağlanınca konsolda **tek** uyarı düşüyor ve ilk bağ geçerli oluyor.
- [ ] İki oyuncu: A'nın duvara sıktığı efekti B de görüyor ve aynı yüzeyden çıkıyor; tracer
      seyreltmesi (her N'inci mermi) efekti **seyreltmiyor**.
- [ ] Namlusu engelin içindeyken ateş → hiçbir çarpma efekti çıkmıyor.
- [ ] Tam otomatik ateşte (600 RPM) profiler'da atış başına ayırma (GC Alloc) yok.
