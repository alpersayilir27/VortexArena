---
title: Yüzey çarpma efektleri
---

# Yüzey çarpma efektleri — kalan iş: harita atamaları

Mermi neye çarptıysa **o yüzeyin** parçacığı ve sesi çıkar. Kod yerinde: yüzey kimliği
`SurfaceDefinition` + `SurfaceLibrary` + `SurfaceTag`'te, oynatma havuzlu `SurfaceImpactFx`'te, tek
kapı `ArenaCombat.ReportImpact`. Silahın kendi çarpma prefabı yoktur; uzak atışta çarpma noktası ışınla yerel çözülür, **protokole ekleme YOK**.
Sözleşmenin anlatımı `Docs/Sistem-Ozeti.md` §4.

## Kalan içerik işi

- [ ] `toprak` tanımının materyal listesi boş — bugünkü arenalarda toprak materyali yok. Toprak
      yüzeyli arena gelince listeye eklenir.
- [ ] Materyal ve etiket eşlemeleri **elle** yapılır — `Tools > VortexArena > Arena > Yüzey Atama`
      penceresi seçili objenin hangi yüzeye çözüldüğünü (ve neden) gösterir, etiketi ve materyal
      bağını oradan kurar. Göz kararı toplu eşleme yapılmaz: yanlış bağ ancak o duvara sıkılınca
      görülür.
- [ ] Arena kenar camı (`M_VortexGlassWall_*`) `default`a düşüyor. Cam efekti/sesi istenirse yeni
      bir tanım + prefab gerekir; materyalleri listeye bağlamak yeter.

## Tuzaklar

- ⚠️ **Materyal örneği ≠ materyal asset'i.** Sözlük `sharedMaterial` okur; çalışma anında
  `renderer.material` bir KOPYA üretir ve hiçbir zaman eşleşmez.
- ⚠️ **Deri giydirilmiş avatarın hitbox'ları materyale ÇÖZÜLMEZ.** `RemoteAvatar` prefabındaki 32
  `RemoteHitBox` collider'ı kemik transformlarında oturur; ne üstlerinde ne altlarında ne de
  üstlerinde `Renderer` vardır, dolayısıyla `Ch18_Body` materyalini listeye yazmak hiçbir şey
  yapmaz. Oyuncu yüzeyi prefab kökündeki `SurfaceTag` ile gelir. Aynısı ileride eklenecek her
  skinned mesh için geçerlidir.
- ⚠️ **Dekor haritalı arenada materyal eşlemesi işe yaramaz — `SurfaceTag` şart.** Görsel harita
  tek renderer'lı, çok materyalli ve collider'sız; mermi görünmez sınır kutularına
  (`ArenaBoundry*`), kulübe duvar collider'larına ve prop'lara çarpar. Renderer'sız collider
  materyale hiç çözülmez ve `default`a düşer → sınır kutularının köküne `beton`, kulübe prefabına
  `tahta` etiketi konur; **yeni sınır kutusu eklerken etiket de kopyalanır.**
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
- [ ] Dekor haritalı arenada görünmez sınır kutusuna sıkılınca `beton`, üs kulübesi duvarına
      sıkılınca `tahta` efekti çıkıyor (`default` değil).
- [ ] İki oyuncu: A'nın duvara sıktığı efekti B de görüyor ve aynı yüzeyden çıkıyor; tracer
      seyreltmesi (her N'inci mermi) efekti **seyreltmiyor**.
- [ ] Namlusu engelin içindeyken ateş → hiçbir çarpma efekti çıkmıyor.
- [ ] Tam otomatik ateşte (600 RPM) profiler'da atış başına ayırma (GC Alloc) yok.
