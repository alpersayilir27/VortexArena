---
title: Yüzey çarpma efektleri
---

# Yüzey çarpma efektleri — kalan iş: ses

Mermi neye çarptıysa **o yüzeyin** parçacığı ve sesi çıkar. Kod yerinde: yüzey kimliği
`SurfaceDefinition` + `SurfaceLibrary` + `SurfaceTag`'te, oynatma havuzlu `SurfaceImpactFx`'te, tek
kapı `ArenaCombat.ReportImpact`. Silahın kendi çarpma prefabı kalktı (`Weapon.hitEffectPrefab`
alanı silindi); uzak atışta çarpma noktası ışınla yerel çözülür, **protokole ekleme YOK**.
Sözleşmenin anlatımı `Docs/Sistem-Ozeti.md` §4.

## Kalan içerik işi

- [ ] Çarpma sesleri: `default` dışındaki altı tanımın `impactClips` listesi boş. Yüzey başına
      2-3 kısa klip (~0.3 sn) → `Assets/Audio/World/Impacts/`. Seviye ve perde bandı tanımın
      içinde ayarlı, klip bağlamak yeter.
- [ ] `toprak` tanımının materyal listesi boş — bugünkü arenalarda toprak materyali yok. Toprak
      yüzeyli arena gelince listeye eklenir.
- [ ] `tahta`ya bağlı `M_BreakableCover` ve `M_TargetBoard` göz kararı eşlendi (ikisi de aynı
      `Textures1` atlasını kullanıyor) — oyunda bakılıp doğrulanır.

## Tuzaklar

- ⚠️ **Materyal örneği ≠ materyal asset'i.** Sözlük `sharedMaterial` okur; çalışma anında
  `renderer.material` bir KOPYA üretir ve hiçbir zaman eşleşmez.
- ⚠️ **Deri giydirilmiş avatarın hitbox'ları materyale ÇÖZÜLMEZ.** `RemoteAvatar` prefabındaki 32
  `RemoteHitBox` collider'ı kemik transformlarında oturur; ne üstlerinde ne altlarında ne de
  üstlerinde `Renderer` vardır, dolayısıyla `Ch18_Body` materyalini listeye yazmak hiçbir şey
  yapmaz. Oyuncu yüzeyi prefab kökündeki `SurfaceTag` ile gelir. Aynısı ileride eklenecek her
  skinned mesh için geçerlidir.
- ⚠️ **Çok materyalli mesh DESTEKLENMEZ** (yalnız ilk materyal çözülür): hangi submesh'e vurulduğunu
  bilmek `hit.triangleIndex` ister, o da mesh'te Read/Write (bellek iki katı) + `MeshCollider` şartı
  koyar. O objeyi ikiye bölmek ya da `SurfaceTag` koymak yeterlidir.
- ⚠️ **Aynı adlı materyal her sahnede AYRI asset'tir.** `M_Snow`/`M_Brick`/`M_Marble` her arena
  sahnesinin kendi `Art/Materials` klasöründe kopyalanmış durumda; eşleme listesine hepsi tek tek
  girer, biri unutulursa o arena sessizce `default`'a düşer.
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
