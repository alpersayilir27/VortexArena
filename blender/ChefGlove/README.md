# Aşçı eldiveni — Blender kaynağı

Burger modunda oyuncunun gözlükte kendi elinde gördüğü eldivenin kaynağı: beyaz pamuk eldiven, kırmızı
biyeli ve iki düğmeli aşçı ceketi manşeti, elin sırtında kep nakışı. Oyundaki karşılığı
`Assets/Modes/Burger/Avatars/ChefGlove/` (mesh asset'leri, dokular, materyal, `ChefGloveSkin`). Moda nasıl
bağlandığı `Docs/Sistem-Ozeti.md` (`LocalGloves`, `GloveSkin`); yeni bir mod eldiveni eklemenin reçetesi
`Docs/Gelistirici/Yemek-Kitabi.md`.

| Dosya | İçerik |
|---|---|
| `ChefGlove.blend` | Meta'nın OpenXR elleri (`OXRRightHand` / `OXRLeftHand` iskeletleri, `RightHand` / `LeftHand` kaynak mesh'leri), üretilen `ChefGlove_R` / `ChefGlove_L`, `ChefPreview` önizleme sahnesi. Dokular paketli değildir, `Assets/` altındaki PNG'leri gösterir |
| `chef_builder.py` | Geometri: Meta elini şişirir, bilek kapağını açar, manşet + kol halkalarını ekler |
| `chef_tex.py` | UV, prosedürel doku seti (albedo + normal) ve Unity için mesh dökümü |
| `export/` | Betiğin ürettiği mesh dökümleri (`ChefGlove_R/L.txt`) — git'e girmez |
| `renders/` | `render()` önizlemeleri — git'e girmez |

⚠️ `chef_tex.py` raster, gürültü, UV ve döküm yardımcılarını `../TacticalGlove/glove_tex.py`'den içe aktarır
(kopyası yoktur): oradaki genel bir yardımcıyı değiştirmek iki eldiveni de değiştirir.

## Bölgeler

Her yüzün `zone` attribute'u vardır; doku betiği deseni buna göre çizer:

| `zone` | Bölge |
|---|---|
| 0 | Pamuk eldiven (manşetin içine giren kısmı dahil) |
| 1 | Manşet: biyeli ön kenar + katlanmış bant |
| 2 | Manşetin arkasındaki kol + kol ağzı dudağı |
| 5 | Kolun iç yüzü |

Manşet desenleri (biye, dikiş sıraları, düğmeler, kıvrım) yüz bölgesine değil kol profiline göre yerleşir:
`chef_builder.py` her vertex'e `prof` (bilek ağzından kol profili boyunca mm) yazar ve `SLEEVE_RINGS`'teki
etiketli halkaların değerini nesneye (`ring_prof`) koyar. Profil değişince desenler kendiliğinden yeni yerine
oturur; etiket adlarını (`front`, `lip`, `band_top`, `band_bot`, `fold`, `rim`) değiştirmek `chef_tex.py`'yi kırar.

## Değişiklik akışı

1. `ChefGlove.blend`'i aç.
   - **Geometri** (manşet boyu, kol genişliği, şişirme kalınlığı): `chef_builder.py`'deki sabitler
     (`INFLATE`, `SLEEVE_RINGS`) → *Run Script*. İki eli de baştan kurar; sol el sağın birebir X aynasıdır.
     Elle modelleme yapma: bir sonraki betik koşusu silip yeniden kurar.
   - **Desen/renk**: yalnız `chef_tex.py` (`C_*` renkleri, `HAT_*`, `BUTTON_*` sabitleri, `stage_material()`).
   - Kemiklere, kemik adlarına ve iskelet hiyerarşisine dokunma — Unity tarafı kemikleri **adla** eşler,
     bindpose'lar paketin mesh'inden gelir.
   - ⚠️ Eklenen her yüzün normali **dışa** bakmalıdır. Blender arka yüzü de çizer, Unity çizmez: ters parça
     Blender'da düzgün görünür, oyunda görünmez. `chef_builder.py` kol yüzlerini denetler, terse hata verir.
2. *Scripting* sekmesinde `chef_tex.py`'yi aç → *Run Script*. Betik `build()`'i koşar:
   - builder'dan sonra ilk koşuda UV'leri açar ve UV adalarının çakışmadığını denetler (çakışma bir
     yüzün dokusunu başka yüze boyar);
   - dokular doğrudan `Assets/Modes/Burger/Avatars/ChefGlove/`'a, mesh dökümleri `export/`'a yazılır.
3. Unity'de `Tools > VortexArena > Avatars > Eldiven Mesh'ini İçe Aktar > Aşçı Eldiveni`: dökümleri okur,
   `ChefGlove_R/L.asset`'i **yerinde** yeniden yazar (GUID değişmez, `ChefGloveSkin` bağı korunur).
   Blender'ın FBX'ini doğrudan mesh olarak atama — kemik sırası ve bindpose farklıdır, el parçalanmış çizilir.

Burger takımsız moddur: kırmızı biye, düğme ve nakış dokuya gömülüdür (`C_RED`), takım rengi almaz.
