# Köstebek eldiveni — Blender kaynağı

Köstebek modunda oyuncunun gözlükte kendi elinde gördüğü eldivenin kaynağı: taba deri iş eldiveni, avuçta
dikişli takviye yaması, elin sırtında tokmak + toprak tümseği armalı yuvarlak deri etiket, bilekte cırt
cırtlı elastik kayış. Oyundaki karşılığı `Assets/Modes/Mole/Avatars/MoleGlove/` (mesh asset'leri, dokular,
iki materyal, `MoleGloveSkin`). Moda nasıl bağlandığı `Docs/Sistem-Ozeti.md` (`LocalGloves`, `GloveSkin`);
yeni bir mod eldiveni eklemenin reçetesi `Docs/Gelistirici/Yemek-Kitabi.md`.

| Dosya | İçerik |
|---|---|
| `MoleGlove.blend` | Meta'nın OpenXR elleri (`OXRRightHand` / `OXRLeftHand` iskeletleri, `RightHand` / `LeftHand` kaynak mesh'leri), üretilen `MoleGlove_R` / `MoleGlove_L`, `MolePreview` önizleme sahnesi. Dokular paketli değildir, `Assets/` altındaki PNG'leri gösterir |
| `mole_builder.py` | Geometri: yalnız manşet profili (`CUFF_RINGS`) ve şişirme kalınlığı; kurma kodu `../ChefGlove/chef_builder.py`'den içe aktarılır |
| `mole_tex.py` | UV, prosedürel doku seti (albedo + normal) ve Unity için mesh dökümü |
| `export/` | Betiğin ürettiği mesh dökümleri (`MoleGlove_R/L.txt`) — git'e girmez |
| `renders/` | `render()` önizlemeleri — git'e girmez |

⚠️ Bu klasörde kopya yardımcı yoktur: geometri `../ChefGlove/chef_builder.py`'den, raster/gürültü/UV/döküm
yardımcıları `../TacticalGlove/glove_tex.py`'den, manşet yardımcıları `../ChefGlove/chef_tex.py`'den gelir.
Oralardaki genel bir yardımcıyı değiştirmek bu eldiveni de değiştirir.

## Bölgeler

Her yüzün `zone` attribute'u vardır; doku betiği deseni buna göre çizer:

| `zone` | Bölge |
|---|---|
| 0 | Deri eldiven (kayışın altına giren kısmı dahil) |
| 1 | Kayışın altındaki deri kenar + kıvrık ağız |
| 3 | Bileklik kayışı — Unity'de **alt-mesh 1**, takım rengini alır |
| 5 | Eldivenin iç yüzü |

Kayış ve kenar desenleri (dikiş sıraları, cırt cırt kapağı, kenar dikişi) yüz bölgesine değil manşet
profiline göre yerleşir: builder her vertex'e `prof` (bilek ağzından profil boyunca mm) yazar ve
`CUFF_RINGS`'teki etiketli halkaların değerini nesneye (`ring_prof`) koyar. Etiket adlarını (`s_front`,
`s_top`, `s_bot`, `s_end`, `rim`, `lip`) değiştirmek `mole_tex.py`'yi kırar.

## Değişiklik akışı

1. `MoleGlove.blend`'i aç.
   - **Geometri** (kayış genişliği/kalınlığı, deri kenar boyu, şişirme): `mole_builder.py`'deki sabitler
     (`INFLATE`, `CUFF_RINGS`) → *Run Script*. İki eli de baştan kurar; sol el sağın birebir X aynasıdır.
     Elle modelleme yapma: bir sonraki betik koşusu silip yeniden kurar.
   - **Desen/renk**: yalnız `mole_tex.py` (`C_*` renkleri, `BADGE_*`, `TAB_*` sabitleri, `stage_material()`).
     Renkler tonemapping'siz Unity'ye göre ayarlıdır; Blender önizlemesi (AgX) onları soluk gösterir.
   - Kemiklere, kemik adlarına ve iskelet hiyerarşisine dokunma — Unity tarafı kemikleri **adla** eşler,
     bindpose'lar paketin mesh'inden gelir.
   - ⚠️ Eklenen her yüzün normali **dışa** bakmalıdır. Blender arka yüzü de çizer, Unity çizmez: ters parça
     Blender'da düzgün görünür, oyunda görünmez. Builder manşet yüzlerini denetler, terse hata verir.
2. *Scripting* sekmesinde `mole_tex.py`'yi aç → *Run Script*. Betik `build()`'i koşar:
   - builder'dan sonra ilk koşuda UV'leri açar ve UV adalarının çakışmadığını denetler (çakışma bir
     yüzün dokusunu başka yüze boyar);
   - dokular doğrudan `Assets/Modes/Mole/Avatars/MoleGlove/`'a, mesh dökümleri `export/`'a yazılır.
3. Unity'de `Tools > VortexArena > Avatars > Eldiven Mesh'ini İçe Aktar > Köstebek Eldiveni`: dökümleri
   okur, `MoleGlove_R/L.asset`'i **yerinde** yeniden yazar (GUID değişmez, `MoleGloveSkin` bağı korunur).
   Blender'ın FBX'ini doğrudan mesh olarak atama — kemik sırası ve bindpose farklıdır, el parçalanmış çizilir.

Köstebek takımlı moddur: kayış dokusu bilerek açık gridir (`C_BAND*`) ki materyal rengi onu boyayabilsin.
Oyunda rengi `LocalGloves` verir; `M_MoleGlove_Band`'in kendi rengi takım atanmamışken görünen renktir.
Blender'daki kırmızı kayış yalnız önizlemedir (`BAND_PREVIEW_TINT`).
