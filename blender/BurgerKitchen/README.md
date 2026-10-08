# Burger mutfağı kiti — Blender kaynağı

Burger modunun cartoon mutfak objeleri. Her obje bir Python betiğiyle sıfırdan kurulur ve FBX
olarak doğrudan Unity'ye yazılır. `BurgerKitchen.blend` çalışma dosyasıdır, kaynak betiklerdir:
elle yapılan bir düzenleme betiğe yazılmadıkça bir sonraki üretimde kaybolur.

## Yerleşim

| Yer | İçerik |
|---|---|
| `scripts/bk_common.py` | Ortak modül: palet tanımı ve dokusu, parça kurucular (`Builder.box` · `cyl` · `profile_x`), `bake()` (modifier uygula + palet UV'si + tek mesh), `export_fbx()` |
| `scripts/<id>_<ad>.py` | Obje başına bir betik. Koşunca objeyi kurar, birleştirir ve FBX'ini yazar |
| `ref/<id>_<ad>_sheet.png` | Gemini'den gelen 2×2 görünüm sayfası (ön · sol · arka · sağ). Görünüm ve renk kaynağıdır; ölçü kaynağı değildir |
| `Assets/Modes/Burger/KitchenKit/` | Çıktı: `Models/<Id>_<Ad>.fbx`, `Textures/BK_*.png`, `Materials/M_BK_*.mat`, `Prefabs/BK_<Id>_<Ad>.prefab` |

## Yeniden üretmek

1. `BurgerKitchen.blend`'i aç. Betik Blender MCP'den (`execute_blender_code`) ya da Text
   Editor'den koşar:
   `exec(open(r"<repo>\blender\BurgerKitchen\scripts\<id>_<ad>.py").read(), {"__name__": "__main__"})`
2. Betik `<Id>_<Ad>_src` koleksiyonunda parçaları kurar, `<Id>_<Ad>` adında tek mesh üretir,
   palet dokusunu yeniler ve FBX'i `KitchenKit/Models/` altına yazar.
3. Unity FBX'i yeniden içe aktarır. Import ayarı: Scale 1, Use File Scale, Generate Lightmap UVs
   açık, Read/Write kapalı, Material Import **None** (materyal `M_BK_Palette`'tir, prefab bağlar).
   ⚠️ Her dışa aktarma `BK_Palette.png` ve `BK_Decals.png`'yi de yeniden yazar; onlar da yeniden
   içe aktarılır. Aksi hâlde yeni eklenen renk ya da etiket Unity'de eski dokudan okunur (boş palet
   sütunu macenta görünür).

## Kurallar

- **Yön ve pivot:** ön yüz Blender'da −Y, pivot ayak izinin alt-ortası. Dışa aktarma
  (`axis_forward=-Z`, `axis_up=Y`, `bake_space_transform`) Unity'de ön yüzü +Z'ye, ölçeği 1'e
  getirir.
- ⚠️ **Eksen eşlemesi: Unity yerel `(x, y, z)` = Blender `(−x, z, −y)`.** Sahnedeki eski objenin
  collider'ından okunan Unity-yerel ölçü betiğe yazılırken **x işareti çevrilir**. Simetrik objede
  fark görünmez; asimetrik objede (L ada) model collider'ın ayna görüntüsüne oturur, hata vermez.
- ⚠️ **Palet sütun sırası yalnız sona eklenir** (`PALETTE`): UV'ler sütun indeksini gösterir; araya
  renk eklemek bütün kiti yanlış boyar ve hata vermez. ⚠️ **Palet 16 sütunda doludur:** dokuyu
  genişletmek (`COLS`) dışa aktarılmış her modelin UV'sini kaydırır; yeni renk ancak bütün obje
  betikleri yeniden koşulup hepsi birlikte dışa aktarılırsa eklenir.
- **Dolap stili tek yerde:** `bk_cabinet.py` (kalın 12 cm krem üst, dokulu koyu ahşap gövde,
  çerçeveli kapak). Tezgâh/ada/lavabo betikleri onu kullanır; ahşap parçalar `bk_wood` işaretiyle
  `M_BK_CabinetWood`'a (`BK_CabinetWood.png`, 0,5 m tekrar) gider. Üst yüzey yüksekliği collider
  sözleşmesidir, kalınlık aşağı doğru büyür.
- **Lambalar ve tablolar ayrı objedir** (`g_details.py`: tek lamba modeli + tablo başına bir model,
  pivot duvar/tavan teması). Lamba prefabı (`BK_G_CeilingLamp`) kendi pişmiş spotunu çocuk
  `Light` olarak taşır; ışığı lambadan ayrı yerleştirme, görsel ile ışık kayar. ⚠️ Konumlarının sahibi **sahnedir** (`Kitchen_Shell/CeilingLamps`,
  `Kitchen_Shell/WallArt`); betikteki `LAYOUT` yalnız ilk yerleşimdir, yeniden koşmak sahnedeki
  elle yapılmış yerleşimi bozmaz (yalnız modeller yazılır).
- **Düz görseller (etiket, tabela, ikon, poster) tek materyaldir:** `BK_Decals.png`
  (2040², 5×5 slot, 408 px; ızgara değişirse etiketli bütün modeller birlikte yeniden dışa aktarılır) + `M_BK_Decals`. Kaynak `ref/` altındaki PNG'dir; `DECALS` listesi
  dosyayı ve kırpma kutusunu tutar, `Builder.decal()` yuvarlak köşeli düz plakayı kurar.
  ⚠️ `DECALS` sırası da yalnız sona eklenir (slot indeksi UV'dir). Etiketli bir modelde materyal
  sırası sabittir: 0 = palet, 1 = etiket.
- **`ref/` görselleri en uzun kenarı 1024 px'e küçültülerek saklanır** (LFS boyutu). Kırpma kutusu
  görselin oranıdır, piksel değil; küçültme kırpımı kaydırmaz. ⚠️ Kırpım küçültmeden sonra da
  408 px'in (etiket yuvası) üstünde kalmalı, yoksa yuvaya büyütülüp bulanıklaşır — bu yüzden
  `E4_icons_sheet.png` 2048 px kalır (ikon kırpımları ~420 px).
- **Renk geçişi:** her parçanın UV'si kendi yüksekliğine göre sütunda dikey uzanır (alt koyu,
  %60'ta ana renk, üst açık). Tek renkli ince parça (derz, işaret) pahsız kalır.
- ⚠️ **Pahsız kutu (`bevel=0`) düz gölgelenir** (`Builder.box`): yumuşak gölgelemede köşe
  normalleri bütün yüzeye yayılır, geniş düz duvar kirli/gri yanar ve lightmap bunu düzeltmez.
- ⚠️ **Kaynak koleksiyon gizliyken bake edilmez:** gizli koleksiyonun modifier'ları hesaplanmaz,
  pahlar sessizce düşer. `Builder` koleksiyonu kurarken açar.
- **Ölçüler sahneye bağlıdır**, görsele değil:
  - A1 ızgara: çubuk üstü **0,95 m** = istasyondaki ızgara gövdesinin `CounterSurface` kutusunun
    üstü. Kayarsa köfte plakaya gömülür ya da havada durur.
  - A4 buzdolabı: eski buzdolabının engel kutusunun (1,61 × 0,86 × 2,05 m) içine sığar; kutu
    sahnede kalır.
  - G kabuk: duvarlar oyun alanının sınırında (x ±4,5, z ±4,0), görsel tavan `g_shell.CEIL`
    (3,2 m). Tavan değişirse davlumbaz bacası (`a2_hood.DUCT_TOP`) ve sahnedeki pişmiş spotların
    yüksekliği ile gücü birlikte değişir.
- **Materyaller paylaşılır, obje eklemek yeni materyal açmaz.** `bake()` parça türüne göre ayırır:
  palet (`M_BK_Palette`), etiket (`M_BK_Decals`), duvar karosu (`M_BK_Tiles`, `BK_WallTiles.png`,
  0,60 m tekrar; `Builder.quad(..., tile=True)`, UV metre cinsinden), lamba (`M_BK_Lamp`, ışıksız),
  parlama (`M_BK_Glow`, neon), dolap ahşabı (`M_BK_CabinetWood`). Ketçap ve ızgara efekt dokuları
  `k_ketchup.py` ve `fx_textures.py`'dan gelir.
- Editörün sahne görünümünde zemin beyaz görünür: mekan ölçü maketinin düzlemi (`VA_ArenaBoundary`
  altındaki `<Mekan>_DimensionMesh/Plane`) aynı yükseklikte durur. Oyunda gizlenir, build'den
  ayıklanır; düzlem taşınmaz, kapatılmaz.
- ⚠️ **Kit prefabları `Receive GI = Light Probes` ve static'siz gelir.** Sahneye konan hareketsiz
  kit objesi Static işaretlenir ve renderer'ı `Lightmaps`'e çevrilir, sonra sahne bake edilir;
  yalnız Static işaretlemek lightmap üretmez (obje probe'dan aydınlanmaya devam eder). Ağ nesnesi
  (dağıtıcı, el eşyası) ve hareket eden parça (kapı kanadı) static yapılmaz.
- Önizleme render'ı geçicidir, repoya girmez.
