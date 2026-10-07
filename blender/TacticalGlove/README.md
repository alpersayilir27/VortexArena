# Taktik eldiven — Blender kaynağı

Oyuncunun gözlükte kendi elinde gördüğü eldivenin kaynağı. Oyundaki karşılığı
`Assets/_Shared/Avatars/TacticalGlove/` (mesh asset'leri, dokular, materyaller); rig'e nasıl
bağlandığı `Docs/Sistem-Ozeti.md` (`GloveTeamBand` ve yerel el görünümü), dokunulmayacak yerler
`Docs/Gelistirici/Yapma-Listesi.md` "Yerel elin görünümünü rig'in kemiklerine/hiyerarşisine
dokunarak değiştirme" bölümü.

| Dosya | İçerik |
|---|---|
| `TacticalGlove.blend` | `Glove_R` / `Glove_L` mesh'leri, OpenXR el iskeletleri (`OXRRightHand` / `OXRLeftHand`, `XRHand_*` kemik adları), `GlovePreview` önizleme sahnesi. Dokular paketli değildir, `Assets/` altındaki PNG'leri gösterir |
| `glove_tex.py` | Prosedürel doku seti (albedo + normal) ve Unity için mesh dökümü |
| `export/` | Betiğin ürettiği mesh dökümleri (`TacticalGlove_R/L.txt`) — git'e girmez |
| `renders/` | `render()` önizlemeleri — git'e girmez |

## Bölgeler

Her yüzün `zone` adlı tamsayı attribute'u vardır; doku betiği deseni buna göre çizer:

| `zone` | Bölge |
|---|---|
| 0 | Kumaş |
| 1 | Deri (avuç, parmak uçları) |
| 2 | Kauçuk (boğum barı, parmak pedleri, manşet kenarı, kayış etiketi) |
| 3 | Bileklik kayışı — Unity'de **alt-mesh 1**, takım rengini alır |
| 5 | İç yüz |

Bölge değiştirmek: Edit Mode'da yüzleri seç → *Mesh > Set Attribute* → `zone`.

## Değişiklik akışı

1. `TacticalGlove.blend`'i aç, değişikliği yap.
   - ⚠️ Geometri `Glove_R`'de değişir; `Glove_L` onun **birebir X aynası** kalmalıdır (UV ve
     `zone` sağdan sola ayna eşleşmesiyle aktarılır, eşleşmeyen yüzde betik hata verir). Aynı
     sebeple iki el tek doku setini paylaşır.
   - Kemiklere, kemik adlarına ve iskelet hiyerarşisine dokunma — Unity tarafı kemikleri
     **adla** eşler, bindpose'lar paketin mesh'inden gelir.
2. *Scripting* sekmesinde `glove_tex.py`'yi aç → *Run Script*. Betik `build()`'i koşar:
   - dokular doğrudan `Assets/_Shared/Avatars/TacticalGlove/`'a yazılır (Unity olduğu gibi alır);
   - mesh dökümleri `export/`'a yazılır.

   Geometri değiştiyse UV'ler yeniden açılmalıdır: son satırı `build(unwrap_uv=True)` yap.
   UV'ler yeniden açılınca doku yerleşimi tamamen değişir; yalnız desen/renk değişikliğinde
   kullanma.
3. Unity'de `Tools > VortexArena > Avatars > Taktik Eldiven Mesh'ini İçe Aktar`: `export/`'taki
   dökümleri okur, `TacticalGlove_R/L.asset`'in mesh verisini **yerinde** yeniden yazar (GUID
   değişmez, `VA_CameraRig` bağı korunur). Blender'ın FBX'ini doğrudan mesh olarak atama — kemik
   sırası ve bindpose farklıdır, el parçalanmış çizilir.

Bölge renkleri `glove_tex.py`'deki `C_*` sabitleridir, desenler `stage_material()`'dadır.
Kayış dokusu bilerek açık gridir (`C_BAND*`) ki materyal rengi onu boyayabilsin; Blender'daki kayış
rengi yalnız önizlemedir (`setup_band_material(tint)`), oyunda rengi `GloveTeamBand` verir.
