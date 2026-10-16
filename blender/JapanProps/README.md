# Japan prop kiti — Blender kaynağı

Stilize Tokyo ara sokağı temalı haritanın siper ve dekor objeleri. Her obje bir Python betiğiyle
sıfırdan kurulur ve FBX olarak doğrudan Unity'ye yazılır. `JapanProps.blend` çalışma dosyasıdır,
kaynak betiklerdir: elle yapılan düzenleme betiğe yazılmadıkça bir sonraki üretimde kaybolur.

## Yerleşim

| Yer | İçerik |
|---|---|
| `scripts/jp_common.py` | Ortak modül: palet (`PALETTE`), etiket atlası (`DECALS`), döşenen dokular (`TILES`), parça kurucular (`Builder.box` · `cyl` · `lathe` · `hollow_box` · `prism` · `hull` · `text` · `decal` · `transform_parts`), `bake()` (modifier + UV + z-fight denetimi + tek mesh), `export_fbx()` |
| `scripts/jp_textures.py` | Döşenen dokular: kaynağı dikişsiz yapar, parlaklıktan normal map türetir |
| `scripts/<id>_<ad>.py` | Obje başına bir betik. Koşunca objeyi kurar, FBX'ini yazar, `SUBMESH ORDER` ve Unity collider kutularını basar |
| `ref/<id>_<ad>_sheet.png` · `ref/L1_layout_concept.jpg` | Gemini'den gelen görünüm / konsept görselleri. Görünüm ve renk kaynağıdır, ölçü kaynağı değildir |
| `ref/decals/*.png` | Etiket atlasının kaynak görselleri (Gemini) |
| `ref/tex/*.png` | Döşenen dokuların kaynak görselleri (Gemini) |
| `Assets/Maps/Japan/Props/` | Çıktı: `Models/<Id>_<Ad>.fbx`, `Textures/` (`JP_Palette` · `JP_Decals` · `T_JP_<Doku>_A/_N`), `Materials/M_JP_*.mat`, `Prefabs/JP_<Id>_<Ad>.prefab` |

| Obje | Betik | Ayak izi (x × y, m) |
|---|---|---|
| Bira kasası + fıçı | `a1_beer_crates.py` | 1,41 × 0,81 |
| Yatai (oden tezgâhı) | `b1_yatai.py` | 2,0 × 1,0 (çatı 2,2 × 1,2) |
| Gachapon sırası | `c1_gachapon.py` | 1,4 × 0,6 |
| Sake fıçıları | `e1_sake.py` | 1,32 × 0,6 |
| Koli yığını | `g1_boxes.py` | 1,0 × 0,7 |
| Şantiye paneli | `j1_fence.py` | 2,0 × 0,5 |
| Emanet dolapları | `x1_lockers.py` | 1,4 × 0,6 |
| Pirinç çuvalları | `x3_rice.py` | 1,4 × 0,6 |
| Elektrik panosu | `y1_cabinet.py` | 1,0 × 0,7 |
| Yemek otomatı | `z1_vending.py` | 1,04 × 0,81 |

## Yeniden üretmek

1. `JapanProps.blend`'i aç. Betik Blender MCP'den (`execute_blender_code`) ya da Text Editor'den koşar:
   `exec(open(r"<repo>\blender\JapanProps\scripts\<id>_<ad>.py", encoding="utf-8").read(), {"__name__": "__main__"})`
   ⚠️ **Canlı Blender oturumu paylaşılır** (başka bir kitin `.blend`'i açık olabilir): `Builder` açık dosya
   `JapanProps.blend` değilse kurmayı reddeder; MCP'den koşmadan önce açık dosyaya bakılır, `save_mainfile`
   yalnız açık dosya `JapanProps.blend` iken çağrılır. Oturum başka işteyken betik ayrı bir arka plan
   süreciyle koşar (canlı oturuma ve `.blend`'e dokunmaz, yalnız çıktıları yazar):
   `blender.exe --background <repo>\blender\JapanProps\JapanProps.blend --python-expr "exec(open(r'<betik>', encoding='utf-8').read(), {'__name__': '__main__'})"`
2. Her dışa aktarma paleti, etiket atlasını ve döşenen dokuları da yeniden yazar; Unity hepsini
   yeniden içe aktarır.
3. Import ayarı: FBX Scale 1, Use File Scale, Generate Lightmap UVs açık, Read/Write kapalı,
   Material Import **None**. `JP_Palette` Bilinear, mip yok, 256, Clamp; `JP_Decals` Trilinear, mip,
   2048, Clamp; `T_JP_*_A` mip, 1024, Repeat; `T_JP_*_N` **Normal map** tipinde.
4. Prefab: kök GameObject (MeshFilter + MeshRenderer) layer **`Obstacle`**, üzerinde engel
   `BoxCollider`'ları; child **`<Prefab>_BulletCollider`** layer **`Default`**, render mesh'iyle konkav
   `MeshCollider`. Renderer materyalleri **`SUBMESH ORDER` sırasıyla** atanır (palet, etiket, sonra
   döşenen dokular ada göre); Material Import None olduğundan sıra FBX'ten okunamaz.
   ⚠️ Betiğin bastığı `COLLIDER` satırları objenin **dış zarfıdır**, engel kutusu değildir: engel
   kutusu parça başına mesh'in birkaç cm **içine** çekilir (kural ve gerekçe
   `Docs/Gelistirici/Yemek-Kitabi.md` "Engel kutusu mermiyi de durdurur"). Zarf kutusu üst üste
   yığılı, yuvarlak ya da pencereli objede mermiyi havada keser. Açık çerçeveli objede (kasa) kutu
   yalnız dolu bantlara verilir; ince panelde sacın kendi kalınlığı kadardır.
   Betik `OBSTACLE cx cy cz sx sy sz` basıyorsa kutu **Unity yerel** koordinatında ve mesh'in ≥ 3 cm
   içine çekilmiş gelir: doğrudan girilir, eksen çevrilmez, küçültülmez.

## Kurallar

- **Yön ve pivot:** ön yüz Blender'da −Y, pivot ayak izinin alt-ortası; Unity'de ön yüz +Z, ölçek 1.
- ⚠️ **Eksen eşlemesi: Unity yerel `(x, y, z)` = Blender `(−x, z, −y)`.** Betiğin bastığı collider
  merkezi Unity'ye girilirken x işareti çevrilir, y ile z yer değiştirir; boyutta y/z takası. Simetrik
  olmayan objede yanlış girilen kutu objenin ayna görüntüsüne oturur ve hata vermez.
- ⚠️ **Ayak izi yerleşim sözleşmesidir:** arena yerleşimi (geçit ≥ 0,9 m, görüş hatları) bu ölçülerle
  doğrulandı. Bir objeyi büyütmek yerleşimi sessizce geçersiz kılar — yerleşim yeniden ölçülür.
  Yalnız baş üstündeki çatı taşabilir.
- ⚠️ **Siper `Obstacle` layer'ında değilse siper değildir** — gerekçe `Docs/Gelistirici/Yemek-Kitabi.md`
  "Ağ nesnesi eklemek".
- ⚠️ **Mekanın fiziksel kolonunu saran obje bu kitten bir siperle değiştirilmez.** Kit objeleri yalnız
  sanal siperlerin yerini alır.
- ⚠️ **Görsellerde gerçek marka, logo ya da var olan karakter kullanılmaz** (sake markası, oyuncak
  serisi, firma adı); Gemini isteğinde adlar uydurulur. Ticari üründe telif ve marka riski taşır.
- ⚠️ **Palet sırası yalnız sona eklenir** (`PALETTE`): UV'ler sütun indeksini gösterir. `COLS`
  değişirse bütün objeler yeniden dışa aktarılır (sütun aralığı değişir).
- **Etiket atlası:** 2048², 8 × 8 birim (256 px). `DECALS` girdisi `(anahtar, dosya, kırpım, yuva)`;
  yuva açıkça verilir, UV yuvadan gelir. Kırpım kaynağın kesir dikdörtgenidir (sol-üst köşe) —
  kaynaktaki gri kenar kırpılmazsa modelde görünür. ⚠️ Silindire sarılan etiketin kırpım oranı yüzeyin
  oranına (çevre : yükseklik) eşit olmalı, yoksa yazı yatayda gerilir (fıçı ≈ 3:1).
- **Döşenen dokular** UV'yi metre cinsinden kutu izdüşümüyle alır (`TILES` → tekrar uzunluğu).
  Normal şiddeti `jp_textures.NRM_GAIN` ve `TILES` girdisindeki çarpandır.
- ⚠️ **Boyalı etiketle aynı detayı 3D yapma:** görselde çizili olan donanım (gachapon kolu, otomatın
  para yuvası / servis kapağı) 3D olarak tekrarlanmaz — yanına düşer ve çift görünür. Kaçınılmazsa
  çizimin tam üstüne oturtulur (gachapon `KNOB_U/V`).
- ⚠️ **`yaw` ve `decal` açıları radyandır**; `rot_x`/`rot_y` derecedir. `transform_parts` mesh verisini
  yerinde değiştirir: `bake()`'ten önce ve aynı önek için bir kez.
- ⚠️ **Z-fight yok:** `bake()` her objede `zfight_pairs()` koşar ve `Z-FIGHT <obje>: <a> / <b>` yazar —
  bu satırla dışa aktarılmış obje bitmiş sayılmaz. Yaygın sebepler: aynı yüksekliğe biten üst yüzler,
  bindirilen parçaların aynı düzlemdeki yan yüzleri (halka parçalarında kalınlık dönüşümlü), uç uca
  biten şeritler (uç diğer parçaya gömülür).
- Bütçe: obje başına ≤ 8k üçgen.
- Önizleme render'ı geçicidir, repoya girmez.
