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
| **BB Camera Rig** | Sahne kökü | Meta Building Blocks kamera/kumanda rig'i | Oyuncu hiçbir şey görmez |
| **`ArenaBoundary`** | Arena **merkezinde**, duvarlara hizalı | **Arena orijinini kaydeder** + sınır uyarısını çizer | Tüm uzak oyuncular dünya orijinine yığılır |
| **`ArenaCalibrator`** | Sahne kökü | Zemindeki A–B işaretleriyle fiziksel hizalama | Sanal arena fiziksel odayla örtüşmez |
| **`BaseZone` × 2** | Karşı kenarlarda (Red / Blue) | **Taban bölgesi** — kırmızı/mavi şerit, canlanma kapısı | TDM'de kimse canlanamaz |
| **`SpawnPoint` × 1** | Arenada nereye istersen | Maç öncesi **yerleşim göstergesi** (kod okumaz) | Operatörün oyuncuyu yönlendireceği ortak bir nokta olmaz |
| **`PlayerPoseTracker`** | Sahne kökü | Kafa + iki eli 20 Hz gönderir | Kimse seni göremez |
| **`RemotePlayerSpawner`** | Sahne kökü | Uzak oyuncu avatarlarını üretir | Sen kimseyi göremezsin |
| **`ModeHudSpawner`** | Sahne kökü | Aktif modun HUD prefabını örnekler | HUD çizilmez |

> `SpawnPoint` sihirbaz tarafından ÜRETİLMEZ — nereye konacağı tasarım kararıdır.
> `GameObject > VortexArena > Spawn Point` ile ekle ve elle yerleştir. Arena başına **bir tane**;
> ikincisini eklersen konsola uyarı düşer.

**Admin gözlemci için ek adım YOKTUR.** `AdminSpectator` kendini önyükler ve sahneyi devralır:
BB rig'i kapatır, `ArenaCalibrator`/`BaseZone`'u kapatır, `ArenaBoundary`'yi **kapatmaz** —
`SetSpectatorMode(true)` ile susturur.

---

## İsteğe bağlı

| Bileşen | Ne zaman | Nasıl |
|---|---|---|
| **`ArenaRoof`** | Arenanın çatısı varsa | Çatı hiyerarşisinin köküne: `GameObject > VortexArena > Arena Roof`. Admin kuş bakışına geçince çatı çizilmez (gölgesi kalır). Açık tavanlı arenada hiç yapılmaz → [Çatı Gizleme](../Cati-Gizleme.md) |
| **`FX_SnowStorm`** | Kar/hava efekti isteniyorsa | `Arenas/Standard/IceWorld/Prefabs/` altındaki prefabı arena orijinine (0,0,0) bırak. 12×12 değilse `Snow_A/B/E` shape scale'lerini arena boyutu + ~3 m payla ölçekle |
| **`ProximityWarning`** | Çarpışma önleme isteniyorsa | Elle eklenir; `head` ve `haloMaterial` (`_Shared/FX/M_ProximityHalo`) Inspector'dan verilir |
| **`NetIdentity`** | Dinamik obje senkronu gerekiyorsa | `GameObject > VortexArena > Network Parent` — benzersiz `sceneId` damgalar |

---

## Sahneyi kataloğa bağlamak

Sahne dosyası tek başına yetmez — üç kayıt daha gerekir:

1. **`MapDefinition` SO'su** (`Arenas/<...>/Data/`): `sceneName`, `displayName`, `size` (metre),
   `supportedModeIds`.
2. **`GameCatalog.asset`** → `maps[]` listesine ekle.
3. **Build Settings** → sahneyi ekle ve **enabled** bırak.
4. **`Tools > VortexArena > Export Server Config`** → `Server/config/maps.json` tazelenir.

> ⚠️ **Sahne adı = katalog anahtarıdır.** `load_match` bu string'i taşır; Build Settings'teki adla
> boşluk/harf farkı dahil birebir eşleşmeli. Sonradan değiştirme — değiştirirsen dokümanlarda da
> ara ve düzelt.

> ⚠️ **Export'u unutma.** Unutursan `start_match` "harita tabloda yok" ya da "bu modu
> desteklemiyor" diye **sessizce** reddedilir; sebep yalnız sunucu konsolunda tek satırdır.

> ⚠️ **Yeni arena eklerken `Server/VortexArena.PoseBot`'taki `BuildScenes` listesini de güncelle.**
> Sunucu sahneyi TÜM oyuncuların `hello.scenes` listesinde arar; listesi eksik kalan bir bot maçı
> bloklar.

---

## Doğrulama

Sahneyi kaydettikten sonra:

```bash
unity cmd recompile && unity cmd get_console_logs --json
```

Sonra dev penceresinden sentetik maçla aç: arena yükleniyorsa, HUD geliyorsa ve konsolda
`ArenaSpace` uyarısı yoksa sahne bağlıdır.

Gerçek testte üç şeye bak:
- **Uzak avatarlar doğru yerde mi?** Değilse `ArenaBoundary` arena merkezinde ve duvarlara hizalı
  değildir — orijin oradan gelir.
- **Ölen oyuncu canlanabiliyor mu?** Canlanamıyorsa `BaseZone`'un takımı oyuncunun
  takımıyla eşleşmiyor ya da **bileşeni kapalı** demektir (kapalı bölge açık sayılmaz).
- **Harita değişince kalibrasyon duruyor mu?** Yeni arenada oyuncu fiziksel olarak nerede duruyorsa
  orada kalmalı. Kalmıyorsa kayıtlı `OVRSpatialAnchor` geri yüklenememiştir — konsolda
  `ArenaCalibrator` uyarısını ara.
