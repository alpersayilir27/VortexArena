# Zombi konağı — Blender kaynağı

Zombi modu için üç katlı perili konak arenası. Her parça bir Python betiğiyle sıfırdan kurulur;
`ZombieManor.blend` çalışma dosyasıdır, kaynak betiklerdir: elle yapılan bir düzenleme betiğe
yazılmadıkça bir sonraki üretimde kaybolur. Plan ve kalan partlar: `plan/zombie-manor-sanat.md`.

## Yerleşim

| Yer | İçerik |
|---|---|
| `scripts/zm_common.py` | Ortak modül: mekân ölçüsünün okunması (`DIMENSIONS_JSON`), kat yükseklikleri (`FLOORS`, `STOREY`, `CEIL`), geçit ve pencere ölçüleri (`DOOR_H` kapı/gedik baş hizası, `WIN_Z` pencere bandı, `SHED_TOP` teras barakası/kafes tavanı), şerit çizgileri (`ZONE_Z`), portal yerleri (`PORTALS`), koordinat eşlemesi (`bl`), graybox kurucuları, malzemeler (`MATS`, `material()`, `tile_of()`), z-fight denetimi (`zfight_report`), sanat parçası kurucusu (`Part`: `box` · `prism` · `obox` · `wall` · `cyl` · `pipe` · `hull` · `slab` · `sphere` · `lathe` · `skin` · `finish`), ağaç (`tree`; `leaves=True` koyu yaprak kümeleri). `Part.smooth = True` iken eklenen yüzler yumuşak gölgelenir (heykel, dağ); varsayılan düz |
| `scripts/p0_graybox.py` | Part 0: kat kat hacim maketi (`ZM_P0_Graybox` koleksiyonu). Yerleşim onayı içindir, dışa aktarılmaz; sanatı gelen graybox parçası gizlenir |
| `scripts/zm_rooms.py` | Yapı taşları (kat yüksekliği `fh` parametreli): `lining()` duvar kaplaması, `north_window()`, `fake_door()` kör kapı, `chimney_breast()` şömine bacası + `fireplace()` Viktorya şöminesi (dökme demir kemerli iç plaka, ızgara + korlar, majolika çini yanaklar, sütunlu-konsollu oymalı ahşap çerçeve, mermer ocak taşı + pirinç paravan, isteğe bağlı kemerli ayna; raf üstü `MANTEL_TOP`), `lift_cage()` kafes asansör (dökme demir + pirinç, halkalı süs bandı, rakamlı kat göstergesi, düğme paneli, katlanmış makaslı kapı; `sgn` ile aynalanır), `text_mesh()` kabartma yazı, `panel_door()` dört panelli Viktorya kapısı (pervaz/kayıt, pahlı yükseltilmiş paneller, pirinç tokmak; herhangi bir yerel çerçevede: açık, duvara yaslı, yerde), `shutter()` panjur (çerçeve + 40° eğik lameller), `frame_box()` yerel çerçevede kutu, `balustrade()` korkuluk (çoklu doğru, köşe fenerleri), `post_lantern()`, `face_box()` + `flat_window()` herhangi yöne bakan dış duvar yüzüne, `wall_decay()` sıva yaması + kıvrık kâğıt şeridi |
| `scripts/p1_courtyard.py` | Part 1: avlu zemini, çit + zincirli kapı, ön cephe (kapı, pencereler, yıkık açıklık, kanopi, fenerler), sınır dışında sokak (çöp kutuları, posta kutusu, rögar kapağı, gazeteler) ve kuru ağaçlar (`ZM_P1_Courtyard`) |
| `scripts/p2_interior.py` | Part 2: zemin kat içi — zeminler (hol damalı, salon ahşap), çevre + ara duvar, lambri/korniş, sıva tavan, hol direği (Kolon_2), şömine bacası (Kolon_1), yıkık duvar + çerçeveli kapı, kör kapılar, P1 kafes asansörü (`ZM_P2_Interior`) |
| `scripts/p3_balcony.py` | Part 3: Kat1 balkonu — damalı döşeme (Kat0 tavanının üstü), korkuluk, kanat duvarları + panjurlu pencereler, kemerli iki kapılı cephe, silah panosu, fenerler, korniş, kaide üstünde kanatlı bronz melek heykeli — gövde `skin()` iskeletinden, etek kıvrımlı `lathe()`, ellerinde kafesli ışıklı küreler (Kolon_2) (`ZM_P3_Balcony`) |
| `scripts/p4_upstairs.py` | Part 4: Kat1 odaları — yatak odası (batı) + çalışma odası (doğu), kapılı ara duvar, ikinci şömine (Kolon_1), lambri/korniş, tavan, kör kapılar, kuzey penceresi, P1 varış ve P2 kalkış kafesleri (`ZM_P4_Upstairs`) |
| `scripts/p5_terrace.py` | Part 5: Kat2 çatı terası — ahşap döşeme, açık kenarlarda korkuluk (köşe fenerli), parapet harpuştası, Kolon_1 çevresinde baca, çatı kulübesi içinde P2 varış kafesi, jeneratör + tesla bobini (`ZM_P5_Terrace`) |
| `scripts/p6_exterior.py` | Part 6: oyun alanı dışı — üçgen alınlıklı iki kanat (ön alınlık cephe hattında, iç çatı eğimi terasın yanında yükselir; düz pencereler), avlu zemini, arka bahçe mezarları + ağaçlar (yarısı yapraklı), terk edilmiş Viktorya komşu evleri (tam beşik çatı, baca, veranda, panjurlu / tahtayla kapatılmış / kırık pencereler), kesintisiz sıradağ halkası (açıya bağlı gürültüyle kutupsal yükseklik alanı) (`ZM_P6_Exterior`) |
| `scripts/p7_decor.py` | Part 7: dekor kiti (`ZM_P7_Kit`, gizli) — prop başına bir obje (`ZM_D_<Ad>`). Hazır CC0 modeller (kaynak kimliği betikte ve objenin `zm_source` özelliğinde): avize, şamdan, gaz lambası, çerçeveler, süslü ayna, sarkaçlı saat, yatak, konsol masa, şifonyer, büst, berjer, sallanan sandalye, sokak lambası, örtülü araba. Elle yapılanlar: etli kök kütlesi (kütle `skin()`, damar ve sarkan saçaklar boru; damarlar küpeşte ile korniş arasındaki elipste kalır, saçaklar lambriden 6 cm+ açıkta; pivot yerde), daktilo, gotik zırh + teber (lameller kenar kenara, yaldızlı kenar ve perçinler, gaga vizör), desenli halı; toz çarşafları (koltuk, hayalet) kumaş simülasyonuyla (`drape()`: gizli gövdeye düşen kumaşın son karesi). `ZM_P7_Layout` odalara (ve sınır dışındaki sokağa) yerleştirilmiş bağlı kopyalardır (ilk yerleşim) |
| `scripts/zm_assets.py` | CC0 modeller (Poly Haven): glTF (importer'ın okuyamadığı varlıkta FBX) `asset_cache/`'e (git dışı) indirilir, tek meshe birleştirilir, Quest bütçesine indirgenir (Decimate), kit pivotuna oturtulur; `restyle()` gevşek parçaları bir malzemeye taşır (avize abajurları → `Shade`), `glow()` alev/ampul slotunu ışıklı yapar, `drop()` bir slotun yüzlerini siler (çerçeve camı resmin üstünde siyah ayna gibi okunur) |
| `scripts/zm_textures.py` | CC0 dokular: `MATS[...]["tex"] = (site, id, çözünürlük)` (Poly Haven `ph` / ambientCG `acg`) indirilip `tex_cache/`'e (git dışı) yazılır; malzemeye renk (isteğe bağlı gri + ton), normal (GL), pürüz ve metal haritaları bağlanır |
| `scripts/preview_night.py` | Gece önizlemesi (EEVEE, **Rendered** görünüm): koyu gök, soluk ay ışığı, ince sis, her ışıklı yüzeye (fener, abajur, kor, küre; CC0 propta `zm_glow` etiketli alev/ampul) sıcak nokta ışık. Dışa aktarılmaz |
| `scripts/build_all.py` | Bütün sahneyi sıfırdan kurar: sanat malzemelerini siler, Part 0–7'yi sırayla koşar, z-fight denetimi + toplam üçgen |
| `scripts/p8_export.py` | Her malzemeyi (sanat `MATS` + CC0 propların `ZM_A_*` malzemeleri) Cycles ile pişirir: albedo (ton işlenmiş, alfa varsa A'da), tangent normal (güç işlenmiş), maske (R metallic · G AO · A smoothness); sanat objesi başına FBX, dekor yerleşimi, malzeme değerleri, pratik ışık listesi (gece önizlemesinin ışıklarından), bütçe raporu. `ZM_EXPORT_OUT` ortam değişkeni çıktıyı başka klasöre yazar (deneme koşusu), `ZM_EXPORT_MATS` yalnız verilen malzemeleri pişirir |
| `ref/pz_<no>_<konu>.png` | Referans oyunun fragman kareleri (kırpılmış, 1024 px). Görünüm ve atmosfer kaynağıdır; ölçü kaynağı değildir |

## Yeniden üretmek

`ZombieManor.blend`'i aç, betikleri Blender MCP'den (`execute_blender_code`) ya da Text Editor'den koş:
`exec(open(r"<repo>\blender\ZombieManor\scripts\<betik>.py", encoding="utf-8").read(), {"__name__": "__main__"})`.

1. `build_all.py` — sahneyi kurar; çıktıda `z-fight check: … 0 pairs` görülmeden devam edilmez.
   Tek part da kendi betiğiyle yeniden koşulabilir (betik kendi koleksiyonunu temizler); `MATS`
   değiştiyse `build_all.py` koşulur (`material()` var olan malzemeyi yeniden kurmaz).
2. `preview_night.py` — dışa aktarmadan önce koşulur: pratik ışık listesi onun ışıklarından yazılır.
3. `p8_export.py` — çıktı arenanın kendi kutusuna yazılır (bu sanat mekânın başka arenasında
   kullanılmaz): `Assets/Arenas/Venues/<Mekan>/Scenes/<SahneAdı>/Art/` altında `Models/<obje>.fbx`,
   `Textures/T_<Malzeme>_A|N|M.png` (albedo · normal · maske), `ZM_Layout.json` (dekor propu · Unity
   konumu · Y açısı · ölçek), `ZM_Materials.json` (malzeme başına harita dosyaları, alpha clip, çift
   yüz, emission; doku import önerileri) ve `ZM_Lights.json` (pratik ışık konum/renk).
4. Unity'de `Art/` klasörü seçilip `Tools > VortexArena > Art > Blender Sanatını Kur` koşulur: doku
   import ayarları, `Art/Materials/` altında URP Lit malzemeleri ve FBX importer'ları (Scale 1, Use
   File Scale, **Generate Lightmap UVs açık**, Read/Write kapalı, slotlar ada göre remap) tek adımda
   kurulur; doku tiling'i **1** kalır. Dışa aktarma her tekrarlandığında araç yeniden koşulur.
5. Kuş bakışı (admin): kat ayrımı kamera kırpma bandıyla kendiliğinden olur; bandın altında kalıp
   içeriyi örten parçalar ayrı obje çıkar ve sahnede `ArenaRoof` kökünün altına konur — kafes
   tavanları (`*LiftCage*Roof`), teras kulübesinin çatısı (`ZM_K2_ShedRoof`), kapı saçağı (`ZM_K0_Canopy`).
6. ⚠️ Lightmap'li sahnede kullanılan bir FBX yeniden dışa aktarılırsa sahne aynı işte yeniden bake
   edilir (lightmap UV yeniden üretilir, eski lightmap kayar ve hata vermez).

## Kurallar

- **Bütün ölçüler mekân koordinatındadır:** `(x, z)` = Unity dünya X/Z = ölçü JSON'unun `(x, y)`.
  Blender konumu `bl(x, z, h)` = `(−x, −z, h)`; Unity `(x, y, z)` = Blender `(−x, z, −y)`
  (`blender/BurgerKitchen/README.md` ile aynı dışa aktarma ekseni). Betiğe Blender koordinatı
  yazılmaz — mekân koordinatı yazılıp `bl()`'den geçirilir.
- **Sınır duvarları içten sınıra oturur:** dış hatta giden `wall(..., side=1)` kalınlığını
  dışarı verir, oyun alanını daraltmaz. Dış hat JSON'da saat yönünün tersinedir (NW, SW, SE, NE);
  duvar o yönde çizilmezse `side=1` içeri döner.
- **Fiziksel kolonlar her katta sanatın içinde kalır:** oyuncu her katta aynı kolona fiziksel
  olarak çarpar, o yüzden kolonu saran görsel hacim (şömine bacası, hol direği, heykel kaidesi,
  kapı ayağı) kolon tabanını her yönden en az 10 cm aşar ve baş yüksekliğinin üstüne çıkar.
- **Kat yükseklikleri `FLOORS`'tadır** ve Unity'deki kat portallarının `upperHeight`'ı ile aynı
  olmak zorundadır; plaka, portal kökünü taşıdığı için gerçek yükseklikte kurulur. Katlar eşit
  aralıklıdır (`STOREY` = 3,5 m; `zm_common` bunu assert ile denetler) — `upperHeight` = `STOREY`.
- **Geçit ve pencere ölçüleri sabitlerdedir:** kapı/gedik baş hizası `DOOR_H` (2,4 m), pencere bandı
  `WIN_Z` (0,9–2,2 m, kat zemininden). Pencere insan ölçüsünde kalır: kat yükselince büyümez,
  yalnız üst kattaki kopyası `FLOORS[1] + WIN_Z` olur. Kat aralığı değişirse `DOOR_H`/`WIN_Z`
  değişmez, duvar ve kaplama yükseklikleri `STOREY`/`CEIL` üzerinden kendiliğinden gelir.
- **Dokular ve hazır modeller yalnız CC0 kaynaktan:** Poly Haven ve ambientCG; doku kimliği `MATS`'ta,
  model kimliği `p7_decor.py`'dedir, dosyalar `tex_cache/` ve `asset_cache/`'ten yeniden indirilir.
  Hazır model kendi malzemesiyle (`ZM_A_<kimlik>_<n>`) gelir ve bütçeye indirgenir: kahraman prop
  ≤ 10k, diğerleri ≤ 6k üçgen; sahne toplamı (bağlı kopyalar dahil) ≤ 300k. ⚠️ İndirgeme sahne
  kökünde yapılır: gizli koleksiyondaki (kit) obje değerlendirilmez, Decimate hata vermeden hiçbir şey
  yapmaz. Gerçek dokulu malzemede `T` dokunun **gerçek boyutudur**
  (Poly Haven `/info` `dimensions`, mm). Projede CS haritalarından çıkarılmış dokular lisanssızdır,
  kullanılmaz. Boyalı bir dokuyu başka renge çevirmek için `grey=True` + `tint` (yalnız gölgelemesi
  kalır). `T` tek sayı ya da `(yatay, dikey)` olabilir (lambri: panelleri uzatır); `nstr` normal
  haritanın gücüdür, dışa aktarmada normal haritaya pişirilir.
- **CC0 karşılığı olmayan albedo betikle çizilir** (`zm_textures.py`): `tex=("gen", ad, çözünürlük)` +
  `gen=dict(kind=...)` — damask kâğıt (`base` dokusunun normal/pürüzlülüğü ve lekeleriyle), pencere
  arkası düz gece göğü ve desenli halı. Çıktı `tex_cache/`'te **`gen` parametrelerinin özetiyle** saklanır: çizim
  kodu değişip parametre değişmezse eski PNG kullanılır ve hata vermez — çizim kodu değişince
  `gen`'deki `v` artırılır.
- **İki süs malzemesi:** dış cephe, korkuluk ve dış pencere süsü `TrimPaint` (kirli beyaz boya); oda
  içi pervaz, lambri bandı ve korniş `Trim` (koyu ahşap). İki yüzü de görünen kemer kasası her yüzde
  kendi malzemesini alır. Kat0 kâğıdı `Wallpaper` (bej), Kat1 odaları `WallpaperGrey`.
- **Malzeme = bir doku karosu:** `MATS[...]["T"]` bir karonun kapladığı metredir. UV'ler kutu
  izdüşümünün `T`'ye bölünmüşüdür (karo birimi), bu yüzden Blender önizlemesi, pişmiş PNG ve Unity
  (tiling 1) aynı görünür. Desen bir karoda tam tekrar eder: gürültü/hücre 4B torus üstünde koşar,
  şerit/tahta/tuğla/dama ölçüleri `T`'yi tam böler. ⚠️ Desen ölçüsü (`scale`, `size`) değişirse `T`
  de tam kata getirilir — yoksa karo sınırında dikiş çıkar ve hata vermez. Tek görselli malzemede
  (halı) `T` nesnenin ölçüsüdür, `off` görseli 0–1'e kaydırır. Renkler doğrusaldır (linear). Pişirmede metalik geçici sıfırlanır (metalin difüz rengi siyah pişer).
- **Oyun alanının içine yalnız şunlar girer:** zemin, sınırdaki duvar/çit, kolonu saran hacimler ve
  baş hizasının üstündeki parçalar (kanopi, fener: alt kenar ≥ 2,1 m), bir de geçitleri kapatmayan
  dekor propları (yatak duvara yaslı, kafes kapılarının ve oda kapılarının önü boş). Sokak lambası,
  ağaç ve araç sınırın dışındadır.
- ⚠️ **Z-fight yok:** aynı yöne bakan iki yüz aynı düzlemde üst üste binmez (Quest'te titrer).
  Her `Part` çağrısı yüzlerine bir parça kimliği (`zm_part` özniteliği) yazar;
  `zm_common.zfight_report(objeler)` bütün parçaları objeler arası da dahil karşılaştırır ve
  `Z-FIGHT <obje>#<parça> / …` satırı basar — bu satırla bir part bitmiş sayılmaz. Çözüm kalıpları:
  dikme kaideden **2 cm önde** durur (`PROUD`), zemine oturan parça **2 cm gömülür** (`SINK`; alt
  yüzler de sayılır), pervaz açıklığın iç yüzüne 1 cm taşar, bir parçanın ucu komşusunun yüzünde
  bitmez — ya içine girer ya 1 cm kısa kalır. Bir yüze gömülen parçalar (şömine çerçevesi) arkalarını
  ayrı derinliklere gömer, lambrinin 4/8/12 mm'sinden de ≥ 2 mm uzak. Döndürülmemiş özdeş küreler
  birbirine girmez (yüzeyleri paralel, bir düzleme denk gelir); üst üste binecek kümeler `hull()`
  rastgele noktalarıyla kurulur. Duvar düzleminde yatan boru çift kenarlı (`segs=6`) kurulur: köşesi
  duvara bakar, yüzü duvara paralel düşmez. Gizli koleksiyondaki obje denetimde dünya konumu güncellenmeden
  sayılır: denetimden önce sanat koleksiyonları görünür olmalı.
- **Duvar kaplaması (`lining()`):** lambri, küpeşte, süpürgelik ve korniş ayrı katmanlardır; her
  katmanın arkası duvarın içinde **kendi derinliğinde** biter, uçları açıklıkta katman başına 1 cm
  kaydırılır (`door`: pervazın altına girer, `hole`: çıplak açıklıktan geri çekilir), köşede dik
  komşunun katmanında durur. Pervaz/kasa arkası 2 cm gömülür (bütün katmanlardan derin). Mekân dış
  hattı ~1,5° eğik olduğundan dik kesilen uçlar 4 mm fazla kısaltılır.
- **Dökülmüş kâğıt (`wall_decay()`):** yırtık kenarlı sıva yaması kâğıttan 3,5 mm önde durur
  (denetimin eşiği `ZF_GAP` 2 mm; tam 2 mm'lik katman kayan noktayla bazen yakalanır), kıvrılan
  şerit yamanın üstüne yapışıp sarkar. Yamalar küpeşte ile korniş arasında tutulur.
- **Kat geçişleri kafes asansördür:** portal diski kafesin ortasındadır, kafes duvarları (Unity'de
  `Obstacle`) diski üç yandan çevreler, kapı 1 m'dir. Üst katlarda aynı kafes dili sürer.
- **Silah panosu yeri:** balkon cephesindeki çerçeveli koyu pano (`BOARD_X`, `BOARD_H`) Unity'deki
  silah tuvalinin asılacağı yerdir; tuval ölçüsü değişirse pano betikten büyütülür.
- **Kat dilimleri:** bir alt katın tavanı `CEIL`–`CEIL+0.06`, üst katın döşeme plakası
  `CEIL+0.06`–kat yüksekliği arasındadır (ayrı objeler: Unity'de plaka `Default` + `ArenaRoof`).
  Alt kat duvarları plakanın içinde biter (iç duvar `STOREY−0,05`), üst kat duvarı plakanın içinden
  başlar (`FLOORS[1]−0,04`) — iki katın duvar yüzleri aynı düzlemde üst üste binmez.
- **Dekor kiti sözleşmesi:** her prop dünya orijininde kurulur; ön yüzü mekân +z (Unity +Z), pivotu
  dayandığı yerdedir — yer propu: alt, arka kenar ortası (halı, hayalet, koltuk, zırh, şamdan, daktilo:
  alt orta); duvar propu: duvara değen arka yüzün ortası; tavan propu (avize): göbeğin üstü. Duvar
  propu kâğıttan 3 mm, yer propu küpeşteden 4 cm açıkta durur. **Yerleşimin sahibi Unity sahnesidir:**
  `ZM_P7_Layout` yalnız ilk öneridir; betiği yeniden koşmak kit mesh'lerini yeniler.
- ⚠️ **Avize alt ucu kat zemininden ≥ 2,1 m yukarıda kalır** (VR'da baş + gözlük); tavan göbeği
  `CEIL−0,06`'da (3,24), avize boyu 0,6 m.
- `VOID_*` mesh'leri üst kattaki boşluk şeritlerini işaretler (zemini olmayan fiziksel alan);
  sanat değildir, dışa aktarılmaz.
