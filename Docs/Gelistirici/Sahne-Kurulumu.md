---
title: Sahne Kurulumu
---

# Sahne Kurulumu

Bir arena sahnesinin ağa bağlanması için sahnede bulunması gerekenler.

> **En kolay yol araç:** `Tools > VortexArena > Arena > Template Temellerini Yükle` bu listeyi boş bir
> sahneye prefab örneği olarak koyar (idempotent — var olanı atlar). Bu sayfa "aracın koyduğu şey
> ne işe yarıyor" ve "elle bir sahne düzeltiyorum" durumları içindir.
> Ölçü maketi (`… > Arena > JSON'dan DimensionMesh Üret`) ayrı ve **sırasız** ama **zorunlu** bir
> adımdır: sahneden bağımsız, dünya orijininde ve dönüşsüz kurulur, ve sahnenin kalibrasyon
> işaretçilerini o üretir.

---

## Zorunlu bileşenler

| Bileşen | Nerede durur | Ne yapar | Atlarsan |
|---|---|---|---|
| **`VA_CameraRig`** | Sahne kökü, **prefab örneği** (`_Shared/App/Prefabs/`) | Kamera/kumanda rig'i + etkileşim rig'i (`OVRComprehensiveInteractionRig`) tek pakette. ⚠️ Yerel gövde avatarı burada DEĞİLDİR: `LocalBodyAvatar` kendini önyükleyen tekildir ve sahne köküne kurulur (rig'in altına konsa rig transformu iki kez uygulanırdı) | Oyuncu hiçbir şey görmez |
| **`ArenaBoundary`** | Alana hizalı bir obje (plan koordinatları onun yerel XZ'si) — **`VA_ArenaBoundary`** prefabıyla gelir | Sınır uyarısını sürer: kenara yaklaşınca HMD'ye bağlı karartma quad'ı hafifçe koyulaşır, dışarı çıkınca tam kararma + uyarı. Ölçüyü **bağlı boyut dosyasından** okur (admin kuş bakışı kadrajı da oradan gelir). `dimensionsJson` = mekanın boyut dosyası — **ZORUNLU**, bileşende ölçü tutan başka alan yoktur. ⚠️ Yarı saydam duvar göstergesi KALDIRILDI (`wallRenderers` yok): arenanın duvarları environment sanatına aittir | Boyut dosyası bağlı değilse muhafaza konsola hata basıp **kendini kapatır**: alan-dışı uyarısı hiç çıkmaz, admin kuş bakışı kadrajsız kalır |
| **Boyut dosyası** (`ArenaDimensions` JSON) | **Mekan** kutusunun `Data/` klasöründe (`<İşletme>_dimensions.json`); `ArenaBoundary.dimensionsJson` alanına bağlı | **Ölçünün tek kaynağı:** `plane` (tabanın sıralı köşeleri) + `columns` (her biri kendi köşe halkası) + `calibration` (zemin bandının A/B noktaları). Alan tam kare olsa bile dört köşe yazılır — "dikdörtgen kipi" YOKTUR; içbükey alan da tek halkadır, birleştirme yoktur. Aynı dosya ölçü maketini üretir, muhafazayı besler, kuş bakışı kadrajını verir ve kalibrasyon işaretçilerini yerleştirir. **Mekanın tüm sahneleri aynı dosyayı gösterir** | Muhafaza tümden kapanır (üstteki satır). ⚠️ Dosyayı yazıp alana bağlamamak onu **build'in dışında** da bırakır |
| **Ölçü maketi** (`<Mekan>_DimensionMesh`) | **Sahne kökü**, dünya orijininde ve dönüşsüz — `… > Arena > JSON'dan DimensionMesh Üret` koyar | Fiziksel alanın sahnedeki referansı (taban + kolonlar) **ve sahnenin kalibrasyon işaretçileri** (`anchor_a`/`anchor_b` küpleri). Arena sanatı bunun üstüne kurulur; istersen maketi elle taşı/döndür (geri okuma kendi kökünü referans alır, ⚠️ **ölçeğini değiştirme**). ⚠️ Build'e **kök + işaretçiler** girer (kalibrasyon onlara bağlı, `EditorOnly` etiketlenmez); görsel dal (`Plane` + `Columns`) build'e alınmaz — `ProBuilderMesh` runtime'a ProBuilder'ı sokardı. Duvar üretmez | Sahne kalibre edilemez (işaretçi yok) ve sanatı neye göre yerleştireceğini bilemezsin |
| **`ArenaCalibrator`** | Sahne kökü — **`VA_CalibrationManager`** prefabıyla gelir | Zemindeki A–B işaretleriyle fiziksel hizalama; maketin `anchor_a`/`anchor_b` küplerini `Start`'ta boyut dosyasındaki noktalara oturtur | Sanal arena fiziksel odayla örtüşmez |
| **`BaseZone` × 2** | Karşı kenarlarda (Red / Blue) | **Taban bölgesi** — kırmızı/mavi şerit, canlanma kapısı | TDM'de kimse canlanamaz |
| **`PlayerPoseTracker`** | Sahne kökü — **`VA_PoseSync`** prefabıyla gelir | Kafa + iki eli 20 Hz gönderir | Kimse seni göremez |
| **`RemotePlayerSpawner`** | Sahne kökü — aynı **`VA_PoseSync`** prefabında | Uzak oyuncu avatarlarını üretir | Sen kimseyi göremezsin |
| **`ModeHudSpawner`** | Sahne kökü — **`VA_ModeHud`** prefabıyla gelir | Aktif modun HUD prefabını örnekler | HUD çizilmez |
| **Silahlar** (`WPN_*` prefab örnekleri) | Arenada tasarımın uygun gördüğü yerlerde | `weaponSource:"weaponcanvas"` modlarında (TDM · turnuva) oyuncunun silah aldığı yer. Silah `WeaponFrame.maxGrabDistance` mesafesinden seçilir, **klonu** ele gelir ve **tükenmez** | Oyuncunun eline hiç silah gelmez (hata basılmaz — silahsız arena geçerli bir sahnedir) |

> ⚠️ **Yukarıdaki prefablar (`VA_ArenaBoundary`, `VA_CameraRig`, `VA_PoseSync`,
> `VA_CalibrationManager`, `VA_ModeHud`)
> sahneye ÖRNEK olarak konur** — başka bir sahneden kopyalanmaz, unpack edilmez. Kopya konursa
> rig ya da kalibrasyon kurulumundaki tek bir düzeltme arena sayısı kadar elle iş doğurur.
> Aynı sebeple sahneye **Building Blocks rig'i ya
> da ayrı bir `OVRComprehensiveInteractionRig` EKLENMEZ**: ikisi de `VA_CameraRig`'in içindedir.

> `VA_CalibrationManager`'ın `anchorA` / `anchorB` / `rigRoot` alanları sahneye bakar, bu yüzden
> **örnek üstünde** (prefab override) doldurulur; prefab asset'inde üçü de boştur — normaldir,
> hata değil. `rigRoot` = sahnedeki `VA_CameraRig`. `anchorA`/`anchorB` **boş bırakılabilir**:
> kalibratör işaretçileri maketin `DimensionAnchor` küplerinden çözer (ad araması yalnız maketi
> olmayan eski sahneler için son basamaktır). Yerleri ise sahneden değil boyut dosyasından gelir —
> küpü elle taşımanın kalıcı etkisi yoktur, ölçü `calibration` alanına yazılır.
> ⚠️ İşaretçinin konumu **zemin noktasının kendisidir**; küp o noktada merkezlenir, yarısı zeminin
> altında kalır.

> ⚠️ **Silahı sahneye koyan bir bileşen YOKTUR ve yazılmayacak** — yerleşim arena kararıdır,
> harita tasarlanırken elle yapılır. Silah `WPN_*` prefabının **ÖRNEĞİ** olarak konur
> (kopyalanmaz, unpack edilmez); örnekleri bir `WeaponCanvas` prefabında toplayıp onu her sahneye
> `BaseZone` gibi tek örnek olarak koymak yerleşimi tek yerden düzeltilebilir kılar.
> ⚠️ Hangi silahın duracağını **arena** belirler, `ModeDefinition.loadout` değil — moda silah
> eklemek arenaları değiştirmez. `loadout` yalnız `random` modlarında (FFA, lobi) okunur.
> Çerçeve görselini örnek başına `WeaponFrame.isFrameVisible` ile aç/kapat.

> ⚠️ **Arena geometrisi dünya orijinine göre kurulur:** zemin dünya y=0'da, arena merkezi dünya
> (0,0,0) civarında. Arena uzayı dünya uzayıdır — sahneyi topluca kaydırmak ya da döndürmek
> arenadaki **tüm** oyuncuların ağ koordinatını kaydırır.
> ⚠️ Dikey sapma özellikle görünürdür: uzak avatarların kökü arena koordinatına oturur, zemin
> y=0'da değilse herkes aradaki fark kadar havada durur. Aynı sebeple `VA_CameraRig`'in kökü de
> Y=0'dadır (tracking origin `Stage` onu fiziksel zemin sayar).

**Admin gözlemci için ek adım YOKTUR.** `AdminSpectator` kendini önyükler ve sahneyi devralır:
`VA_CameraRig`'i kapatır, `ArenaCalibrator`/`BaseZone`'u kapatır, `ArenaBoundary`'yi **kapatmaz** —
`SetSpectatorMode(true)` ile susturur: admin'in başlığı olmadığı için muhafaza mesafesi anlamsız
veri üretir, ama duvarlar çizilmeye devam etmeli.

---

## İsteğe bağlı

| Bileşen | Ne zaman | Nasıl |
|---|---|---|
| **`ArenaObstacle`** | Sahneye elle konmuş bir engel (kolon, kasa, direk) muhafaza uyarısına girecekse | Engel objesine ekle, `size` alanına zemindeki ölçüsünü yaz (X = genişlik, Y = derinlik). ⚠️ **Collider EKLEMEZ, fizik yapmaz** — tek işi `ArenaBoundary`'nin oyuncuyu engele yaklaşırken uyarmasıdır. Plandan üretilen kolonlara aracın kendisi ekler |
| **`ArenaRoof`** | Arenanın çatısı varsa | Çatı hiyerarşisinin köküne: `GameObject > VortexArena > Arena Roof`. Admin kuş bakışına geçince çatı çizilmez (gölgesi kalır). Açık tavanlı arenada hiç yapılmaz → [Çatı Gizleme](../Cati-Gizleme.md) |
| **`FX_SnowStorm`** | Kar/hava efekti isteniyorsa | `Arenas/Venues/Outdoor12x12/Scenes/IceWorld/Prefabs/` altındaki prefabı arena orijinine (0,0,0) bırak. 12×12 değilse `Snow_A/B/E` shape scale'lerini arena boyutu + ~3 m payla ölçekle |
| **`ProximityWarning`** | Çarpışma önleme isteniyorsa | Elle eklenir; `head` ve `haloMaterial` (`_Shared/FX/M_ProximityHalo`) Inspector'dan verilir |
| **`NetIdentity`** | Dinamik obje senkronu gerekiyorsa | `GameObject > VortexArena > Network Parent` — benzersiz `sceneId` damgalar |

---

## Sahneyi kataloğa bağlamak

Sahne dosyası tek başına yetmez — üç kayıt daha gerekir:

1. **`MapDefinition` SO'su** — sahnenin kendi kutusunda ve **sahneyle aynı adla**:
   `Venues/<İşletme>/Scenes/<SahneAdı>/Data/<SahneAdı>.asset`. Alanları `sceneName`, `displayName`,
   `supportedModeIds`. Arena ölçüsü burada değil, arenanın **boyut dosyasındadır**.
   ⚠️ Başka bir yere konursa `Configure All Build Elements` onu "yanlış yerde" diye uyarır ve
   kutuyu eksik sayar.
2. **`GameCatalog.asset`** → `maps[]` listesine ekle.
3. **Build Settings** → sahneyi ekle ve **enabled** bırak.
4. **`Tools > VortexArena > Server > Export Server Config`** → `Server/config/maps.json` tazelenir
   (dosyaya `sceneName` + `modes` yazılır; arena ölçüsü sunucuya gitmez).

> ⚠️ **Sahne adı = katalog anahtarıdır.** `load_match` bu string'i taşır; Build Settings'teki adla
> boşluk/harf farkı dahil birebir eşleşmeli. Sonradan değiştirme — değiştirirsen dokümanlarda da
> ara ve düzelt.

> ⚠️ **Export'u unutma.** Unutursan `start_match` "harita tabloda yok" ya da "bu modu
> desteklemiyor" diye **sessizce** reddedilir; sebep yalnız sunucu konsolunda tek satırdır.

> ⚠️ **Yeni sahneyi Build Settings'e eklemeyi unutma.** Sunucu sahneyi TÜM oyuncuların
> `hello.scenes` listesinde arar (bu liste Build Settings'ten üretilir); listesi eksik kalan bir
> istemci maçı bloklar.

### Lobi sahnesi kuruyorsan

Lobi de bir arena kutusudur; yukarıdaki dört kayıt aynen geçerlidir. Üç fark:

- `supportedModeIds` **yalnız `["lobby"]`** — ⚠️ boş bırakılırsa "kısıtsız" sayılır ve lobide maç
  başlatılabilir hâle gelir.
- Sahnede **`BaseZone` ve `ModeHudSpawner` YOK** (lobide ölüm ve maç HUD'u yok) ve **silah da
  konmaz**: lobinin kaynağı `weaponSource:"random"`, yani serbest atış silahı grip'e basılınca
  `ModeDefinition.loadout`'tan gelir (`WeaponGranter` kendini önyükler — sahnede iş yoktur).
- Sunucuya `IGameMode` eklenmez: `lobby` başlatılabilir bir mod değil bir **profildir**
  (`ModeDefinition.lobbyProfile ✔`) — admin mod seçicisinde de görünmez.

Hangi lobinin oynatılacağı sunucuda seçilir: `server.json → lobbyScene`. Lobi **fiziksel odanın
ölçüsünde** olmalıdır, bu yüzden kaynağı o ölçüdeki arenadır (ölçekleme yoktur). Ayrıntı:
`Docs/ArenaNet-Protokol.md` §10.7.

---

## Doğrulama

Sahneyi kaydettikten sonra:

```bash
unity cmd recompile && unity cmd get_console_logs --json
```

Sonra sunucuyu çalıştır, bir admin istemciden bu haritada maç başlat ve editörden bağlan: arena
yükleniyorsa, HUD geliyorsa ve konsolda hata yoksa sahne bağlıdır.

Gerçek testte üç şeye bak:
- **Uzak avatarlar doğru yerde mi?** Değilse arena geometrisi dünya orijininde değildir — sahnenin
  tamamı kaymış ya da dönmüştür. Ayakları havadaysa zemin dünya y=0'ın üstündedir.
- **Ölen oyuncu canlanabiliyor mu?** Canlanamıyorsa `BaseZone`'un takımı oyuncunun
  takımıyla eşleşmiyor ya da **bileşeni kapalı** demektir (kapalı bölge açık sayılmaz).
- **Harita değişince kalibrasyon duruyor mu?** Yeni arenada oyuncu fiziksel olarak nerede duruyorsa
  orada kalmalı. Kalmıyorsa kayıtlı `OVRSpatialAnchor` geri yüklenememiştir — konsolda
  `ArenaCalibrator` uyarısını ara. Aynı yerde "boyut dosyasında kalibrasyon noktası yok" uyarısı
  varsa `calibration` alanı boştur ve işaretçiler prefabtaki yerlerinde kalmıştır.
