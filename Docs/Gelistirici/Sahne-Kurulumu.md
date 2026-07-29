---
title: Sahne Kurulumu
---

# Sahne Kurulumu

Bir arena sahnesinin ağa bağlanması için sahnede bulunması gerekenler.

> **En kolay yol sihirbaz:** `Tools > VortexArena > Create Arena From Template` bu listenin
> tamamını üretir. Bu sayfa "sihirbazın ürettiği şey ne işe yarıyor" ve "elle bir sahne
> düzeltiyorum" durumları içindir.

---

## Zorunlu bileşenler

| Bileşen | Nerede durur | Ne yapar | Atlarsan |
|---|---|---|---|
| **`VA_CameraRig`** | Sahne kökü, **prefab örneği** (`_Shared/App/Prefabs/`) | Kamera/kumanda rig'i + etkileşim rig'i (`OVRComprehensiveInteractionRig`) + yerel gövde avatarı (`PlayerBodyAvatar`) tek pakette | Oyuncu hiçbir şey görmez |
| **`ArenaBoundary`** | Arena **merkezinde**, duvarlara hizalı | Sınır uyarısını çizer (duvar alfası + karartma) + arena ölçüsünün tek kaynağıdır (admin kuş bakışı kadrajı bunu okur). Arena dikdörtgen değilse `shape` alanına bir `ArenaShapeDefinition` bağlanır | Alan-dışı uyarısı hiç çıkmaz, admin kuş bakışı kadrajsız kalır (konsola uyarı düşer) |
| **`ArenaCalibrator`** | Sahne kökü — **`VA_CalibrationManager`** prefabıyla gelir | Zemindeki A–B işaretleriyle fiziksel hizalama | Sanal arena fiziksel odayla örtüşmez |
| **`BaseZone` × 2** | Karşı kenarlarda (Red / Blue) | **Taban bölgesi** — kırmızı/mavi şerit, canlanma kapısı | TDM'de kimse canlanamaz |
| **`SpawnPoint` × 1** | Arena uzayının sıfırı olacak yerde, **zemin seviyesinde** | **Arena orijini** — ağa giden/gelen tüm pozlar buna göre çevrilir; ayrıca maç öncesi yerleşim göstergesidir | Tüm uzak oyuncular dünya orijinine yığılır (konsola `ArenaSpace` uyarısı düşer) |
| **`PlayerPoseTracker`** | Sahne kökü — **`VA_PoseSync`** prefabıyla gelir | Kafa + iki eli 20 Hz gönderir | Kimse seni göremez |
| **`RemotePlayerSpawner`** | Sahne kökü — aynı **`VA_PoseSync`** prefabında | Uzak oyuncu avatarlarını üretir | Sen kimseyi göremezsin |
| **`ModeHudSpawner`** | Sahne kökü — **`VA_ModeHud`** prefabıyla gelir | Aktif modun HUD prefabını örnekler | HUD çizilmez |

> ⚠️ **Yukarıdaki prefablar (`VA_CameraRig`, `VA_PoseSync`, `VA_CalibrationManager`, `VA_ModeHud`)
> sahneye ÖRNEK olarak konur** — başka bir sahneden kopyalanmaz, unpack edilmez. Kopya konursa
> rig ya da kalibrasyon kurulumundaki tek bir düzeltme arena sayısı kadar elle iş doğurur.
> Aynı sebeple sahneye **Building Blocks rig'i ya
> da ayrı bir `OVRComprehensiveInteractionRig` EKLENMEZ**: ikisi de `VA_CameraRig`'in içindedir.

> `VA_CalibrationManager`'ın `anchorA` / `anchorB` / `rigRoot` alanları sahneye bakar, bu yüzden
> **örnek üstünde** (prefab override) doldurulur: sırasıyla `anchor_a`, `anchor_b` ve sahnedeki
> `VA_CameraRig`. Prefab asset'inde üçü de boştur — normaldir, hata değil.

> `SpawnPoint` sihirbaz tarafından ÜRETİLMEZ — nereye konacağı tasarım kararıdır.
> `GameObject > VortexArena > Spawn Point` ile ekle ve elle yerleştir. Arena başına **bir tane**;
> ikincisini eklersen konsola uyarı düşer (origin ilk kaydolana bağlanır).

> ⚠️ **Bir kez yerleştirilir, sonra TAŞINMAZ.** Marker arena uzayının sıfırı olduğu için yerini
> ya da dönüşünü değiştirmek arenadaki **tüm** oyuncuların koordinatını kaydırır.
> ⚠️ **Zemin seviyesinde durmalı:** uzak avatarların bastığı zemin Y'si
> `ArenaSpace.ArenaToWorld(Vector3.zero).y`'den türetilir (`ThreePointBodyIK`) — marker havada
> kalırsa avatarların ayakları da havada kalır.

**Admin gözlemci için ek adım YOKTUR.** `AdminSpectator` kendini önyükler ve sahneyi devralır:
`VA_CameraRig`'i kapatır, `ArenaCalibrator`/`BaseZone`'u kapatır, `ArenaBoundary`'yi **kapatmaz** —
`SetSpectatorMode(true)` ile susturur: admin'in başlığı olmadığı için muhafaza mesafesi anlamsız
veri üretir, ama duvarlar çizilmeye devam etmeli.

---

## İsteğe bağlı

| Bileşen | Ne zaman | Nasıl |
|---|---|---|
| **Silah rafı** | Arena raf kipinde (`weaponSource: rack`, ör. TDM) oynanacaksa | **ELLE kurulur** — şablonda (`Standard/Default12x12`) raf yoktur. Raf kökünde `WeaponRackSpawner`, altında yalnız KONUM tutan `RackSlot` gözleri; hangi silahın duracağını mod belirler, sahneye `WPN_*` örneği koyulmaz |
| **Arena planı** (`ArenaShapeDefinition`) | Arena dikdörtgen DEĞİLSE (yamuk, L, kırık duvarlı) | `Create > VortexArena > Arena Shape Definition` ile asset üret, köşeleri + kolonları gir, `Tools > VortexArena > Build Arena From Shape` ile geometriyi üret ve asset'i `ArenaBoundary.shape`'e bağla. Bağlanmazsa muhafaza dikdörtgen kalır → [Yemek Kitabı](Yemek-Kitabi.md) |
| **`ArenaObstacle`** | Sahneye elle konmuş bir engel (kolon, kasa, direk) muhafaza uyarısına girecekse | Engel objesine ekle, `size` alanına zemindeki ölçüsünü yaz (X = genişlik, Y = derinlik). ⚠️ **Collider EKLEMEZ, fizik yapmaz** — tek işi `ArenaBoundary`'nin oyuncuyu engele yaklaşırken uyarmasıdır. Plandan üretilen kolonlara aracın kendisi ekler |
| **`ArenaRoof`** | Arenanın çatısı varsa | Çatı hiyerarşisinin köküne: `GameObject > VortexArena > Arena Roof`. Admin kuş bakışına geçince çatı çizilmez (gölgesi kalır). Açık tavanlı arenada hiç yapılmaz → [Çatı Gizleme](../Cati-Gizleme.md) |
| **`FX_SnowStorm`** | Kar/hava efekti isteniyorsa | `Arenas/Standard/IceWorld/Prefabs/` altındaki prefabı arena orijinine (0,0,0) bırak. 12×12 değilse `Snow_A/B/E` shape scale'lerini arena boyutu + ~3 m payla ölçekle |
| **`ProximityWarning`** | Çarpışma önleme isteniyorsa | Elle eklenir; `head` ve `haloMaterial` (`_Shared/FX/M_ProximityHalo`) Inspector'dan verilir |
| **`NetIdentity`** | Dinamik obje senkronu gerekiyorsa | `GameObject > VortexArena > Network Parent` — benzersiz `sceneId` damgalar |

---

## Sahneyi kataloğa bağlamak

Sahne dosyası tek başına yetmez — üç kayıt daha gerekir:

1. **`MapDefinition` SO'su** (`Arenas/<...>/Data/`): `sceneName`, `displayName`,
   `supportedModeIds`. Arena ölçüsü burada değil, sahnedeki `ArenaBoundary.halfExtentX/Z`'dedir
   (plan bağlıysa planın sınırlayıcı kutusundan gelir).
2. **`GameCatalog.asset`** → `maps[]` listesine ekle.
3. **Build Settings** → sahneyi ekle ve **enabled** bırak.
4. **`Tools > VortexArena > Export Server Config`** → `Server/config/maps.json` tazelenir
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
- Sahnede **`BaseZone` ve `ModeHudSpawner` YOK** (lobide ölüm ve maç HUD'u yok), **silah rafı VAR**.
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

Sonra dev penceresinden sentetik maçla aç: arena yükleniyorsa, HUD geliyorsa ve konsolda
`ArenaSpace` uyarısı yoksa sahne bağlıdır.

Gerçek testte üç şeye bak:
- **Uzak avatarlar doğru yerde mi?** Değilse sahnede `SpawnPoint` yoktur, kapalıdır ya da yeri
  değişmiştir — arena orijini oradan gelir. Ayakları havadaysa marker zeminin üstündedir.
- **Ölen oyuncu canlanabiliyor mu?** Canlanamıyorsa `BaseZone`'un takımı oyuncunun
  takımıyla eşleşmiyor ya da **bileşeni kapalı** demektir (kapalı bölge açık sayılmaz).
- **Harita değişince kalibrasyon duruyor mu?** Yeni arenada oyuncu fiziksel olarak nerede duruyorsa
  orada kalmalı. Kalmıyorsa kayıtlı `OVRSpatialAnchor` geri yüklenememiştir — konsolda
  `ArenaCalibrator` uyarısını ara.
