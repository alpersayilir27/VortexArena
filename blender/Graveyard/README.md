# Graveyard — tek katlı mezarlık arenası (betikle üretilir)

Demir çitli bir avlu: çit mekanın duvar çizgisinde durur (oyuncu sınırı oradan okur), her yanın ortasında bir
kapı vardır; zombiler dışarıdaki mezarlıktan kapıya inen patikalarla gelir. Avlunun içi boştur — içerideki tek
yapı, ölçüdeki fiziksel kolonları saran taş payelerdir. Takımsız (zombi) arenasıdır: sahnede taban bölgesi yoktur.

**Arena her mekana uyar:** her şey hedefin zemin halkasından ve kolonlarından türetilir. Hedef ya `Map`
(genel 10 × 10 m kare → `Assets/Maps/Graveyard/Art`) ya da bir mekan adıdır
(`Assets/Arenas/Venues/<Mekan>/Data/<Mekan>_dimensions.json` okunur →
`Assets/Arenas/Venues/<Mekan>/Scenes/Graveyard_<Mekan>/Art`).

## Yeniden üretmek

Blender'da `Graveyard.blend` açıkken (betikler yolu `.blend`'in yanındaki `scripts/` klasöründen bulur):

```python
import bpy
bpy.context.scene["gy_target"] = "Map"        # ya da mekan adı
bpy.context.scene["gy_export"] = 1            # 0: yalnız kur, dışa aktarma
exec(open(bpy.path.abspath("//scripts/build_all.py"), encoding="utf-8").read(), {"__name__": "__main__"})
```

Gece önizlemesi (Eevee, `renders/` — git'e girmez): `bpy.context.scene["gy_preview_side"] = "S"` (kapı:
`E` · `W` · `N` · `S`) + `scripts/preview_night.py`. Parçacık dokuları haritaya bir kez üretilir:
`scripts/fx_textures.py` → `Assets/Maps/Graveyard/FX/`.

| Betik | Kurduğu |
|---|---|
| `gy_common.py` | Hedef/ölçü okuma, çit halkası, kapı yeri, zemin yüksekliği, `Part` kurucusu, malzeme tablosu (`MATS`), z-fight denetimi, Unity sahne kuralları (`UNITY_OBJECTS`, `LIGHT_KINDS`) |
| `gy_textures.py` | Poly Haven CC0 dokuları (`tex_cache/`, git'e girmez — kimlikler `MATS`'ta) |
| `gy_assets.py` | Poly Haven CC0 modelleri (`asset_cache/`, git'e girmez — kimlikler parça betiklerinde): indir, gizli kütüphaneye bir kez al, indirge, yerleştirilen kopyaları tek nesnede birleştir |
| `p1_ground.py` | Avlu döşemesi (çit halkası, h = 0 = mekan zemini), dış zemin (çite 3 m'den sonra hafif tümsek, uzakta kenar), kapılardan dışarı patikalar |
| `p2_fence.py` | Taş kaide + demir çit, taş payeler (köşe, en fazla 3 m'de bir, çit çizgisindeki kolonları sarar), 4 kapı (açık kanat, kemer), avludaki kolonları saran payeler; fenerler ayrı nesnedir (`GY_Lanterns`) |
| `p3_graves.py` | Çit kenarlarına paralel sıralar halinde mezarlar (iki taş dokusu karışık); yakın sıralarda birkaç mumluk (`wooden_candlestick` + alev); patikalar ve doğum alanları boş kalır |
| `p4_surround.py` | Kuru ağaçlar, bir köşede türbe, her patikanın yanında demir direk + `street_lamp_02` feneri (camı `LampGlow`) |
| `p5_statue.py` | Poly Haven `gothic_statue`, karşı köşede kaide üstünde, ~9k üçgene indirgenir |
| `p6_nature.py` | Ufku kapatan köknar sırası (`fir_sapling`'den bir kez çizilen kart atlası, `asset_cache/_impostor/`), yosunlu kayalar, kütükler, eğrelti otları, kuru dallar — mezar sıralarının arasına |
| `p9_export.py` | Z-fight denetimi, malzeme başına `T_<Mat>_A/N/M.png`, parça başına FBX, `GY_Materials.json` · `GY_Lights.json` · `GY_Layout.json` · `GY_Scene.json`; artık üretilmeyen FBX/dokuları siler |
| `fx_textures.py` | `T_GY_Glow.png` (fener halesi, ruh ışıkları) · `T_GY_Mist.png` (yer sisi) |

## Unity tarafı

1. Çıktı `Art/` klasörünü seç → `Tools > VortexArena > Art > Blender Sanatını Kur (seçili Art klasörü)`.
2. Aynı klasör seçiliyken → `Tools > VortexArena > Art > Blender Sahnesini Diz (seçili Art klasörü)`:
   `GY_Scene.json`'a göre FBX'leri `Graveyard_Art` altına dünya orijinine koyar, nesne başına gölge / lightmap /
   NavMesh kuralını uygular, `GY_Lights.json`'dan baked nokta ışıklarını ve halelerini, yer sisini, ruh
   ışıklarını ve yansıma probunu kurar. Tekrar çalıştırmak aynı sahneyi üretir; kaydetmez, bake etmez.
3. Işık bake'i (sahnenin `.lighting` ayarı: lightmap çözünürlüğü 20, directional, AO) + yansıma probu bake'i,
   ardından `Graveyard_Art` üzerindeki `NavMeshSurface` (ajan tipi `Zombie`) bake'i. Yeniden dışa aktarılan
   FBX = sahne yeniden bake edilir.
4. `GY_Layout.json`: kapılar ve patika sonundaki zombi doğum noktası önerileri (`zombie_spawn` nesneleri buraya
   konur).

## Kurallar

- Koordinat: mekan (x, z) = Unity X/Z; Blender konumu = (−x, −z, h).
- Avlu döşemesinin üstü tam **h = 0**'dır (fiziksel zemin); dış zemin 3 cm aşağıda başlar (aynı düzlemi paylaşmaz).
- Avlunun içine fiziksel karşılığı olmayan nesne konmaz: oyuncu sanal engelden kaçar, oynanır alan daralır.
- Çit çizgisi zemin halkasının 0,25 m dışındadır (`FENCE_OFF`); kapı yeri kenar ortasına en yakın, çit
  çizgisindeki kolonlardan ve köşelerden uzak noktadır.
- **İçinde ışık duran nesne gölge düşürmez** (`GY_Lanterns`, `GY_LampHeads`, `GY_Candles`, `GY_CandleFlames`):
  düşürürse kendi baked ışığını keser. Kural `gy_common.UNITY_OBJECTS`'tadır, Unity'de elle ayarlanmaz.
- Unity'ye giden emisyon gücü Eevee önizlemesinden ayrıdır (`MATS` → `unity_emit`): Quest LDR çizer, 1'in
  üstündeki emisyon beyaza kırpılır ve fener camı rengini kaybeder.
- Fener halesi (`FX/GY_Halo.prefab`) ışıkla birlikte fener gövdesinin içinde doğar; renderer `pivot.z = −0,2`
  onu camın önüne alır. Sıfırlanırsa gövde halenin parlak ortasını örter, büyütülürse kenardaki fenerlerde hale
  yana kayar.
- Mum ışığı mezar taşına 0,24 m uzaklıktadır; `LIGHT_KINDS["Candle"]` şiddeti artarsa taşın yüzü düz turuncuya
  doyar.
- Eğrelti, dal, köknar kartı ve fenerler NavMesh'e girmez (`nav: ignore`); kaya, kütük, mezar ve ağaçlar
  zombi için engeldir — dağıtım patikalardan ve doğum alanlarından uzak durur.
- Z-fight üretilmez: `p9_export` her `Part` çağrısını birbirine karşı denetler (`gy_common.zfight_report`, 2 mm);
  rapor sıfır çift değilse dışa aktarım düzeltilmeden kullanılmaz. Gömülü (h < −4 cm) yüzler sayılmaz.
- Bütçe: ~107k üçgen, 21 malzeme (Quest).
