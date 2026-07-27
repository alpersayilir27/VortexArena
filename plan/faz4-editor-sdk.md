# Faz 4 — Editor SDK Araçları + İçerik Ölçekleme + İşletme Piloti

**Amaç:** İçerik üretimini (yeni arena/mod/işletme) araçlaştırmak; Mirror-vari Unity editor ergonomisinin temelini atmak; A12x12 arenayı araçla üretmek. Bu faz diğerlerinden daha serbest — maddeler bağımsız, sırayla zorunlu değil.

**Ön koşul:** Faz 3 tamam (oynanabilir TDM).

---

## Adım 1 — `VortexArena.Net.Editor` asmdef + temel araçlar

Klasör: `Assets/_Shared/Net/Scripts/Editor/` — asmdef: `includePlatforms:["Editor"]`, refs: `VortexArena.Net`, `VortexArena.Protocol`, `VortexArena.Core`.

- **`NetIdentity`** (runtime bileşen — `VortexArena.Net`'e eklenir): `uint sceneId` (sahne objesi için bake'li kimlik) — ileride dinamik obje senkronu (kapılar, pickup'lar) için altyapı. v1'de oyuncu senkronu playerId ile gittiğinden NetIdentity YALNIZ sahne objeleri içindir.
- **Hiyerarşi sağ-tık menüsü** (kullanıcının istediği genişleme noktası): `GameObject > VortexArena > Network Parent` — seçili objeye `NetIdentity` ekler, benzersiz `sceneId` atar (sahnedeki max+1), Undo destekli.
- **SceneId bekçisi:** sahne kaydında (`UnityEditor.SceneManagement.EditorSceneManager.sceneSaving`) çakışan/0 kalan `sceneId`'leri onarır (Mirror'ın sceneId bake deseninin sade hali).
- **`NetSpawnCatalog`** (SO, `_Shared/Data/`): `id → prefab` kayıtları — ileride server-komutlu spawn için rezerv altyapı. Şimdilik RemoteAvatar + FX kayıtları.

## Adım 2 — Map metadata export (server config otomasyonu)

Menü: `Tools > VortexArena > Export Server Config`:
- Tüm `WeaponDefinition` SO'larından `Server/config/weapons.json` üretir (Faz 3'teki elle senkron OTOMATİĞE bağlanır — tek doğruluk kaynağı Unity SO'ları olur).
  > ⚠️ **GEÇERSİZ (2026-07-27):** silah export'u kaldırıldı; sunucu silah tablosu tutmuyor,
  > hasarı istemci bildiriyor (§10.3). Export artık yalnız `maps.json` üretir.
- Tüm `MapDefinition`'lardan `Server/config/maps.json` (sceneName, boyut, takım başına slot sayısı) — server start_match doğrulaması + ileriki bölge-tabanlı modlar için.
- Çıktı deterministik (alfabetik sıralı, LF) → git diff'leri temiz.

## Adım 3 — Arena şablon sihirbazı (içerik ölçeklemenin kalbi)

Menü: `Tools > VortexArena > Create Arena From Template`:
1. Dialog: kaynak arena (varsayılan A10x10), yeni ad (`A12x12` veya işletme adı), boyut (12×12), hedef klasör (`Standard/` veya `Venues/<Ad>/`).
2. Yapılanlar: sahne dosyasını `AssetDatabase.CopyAsset` ile kopyala (YENİ GUID — kopya bilinçli) → yeni klasör yapısı `{Scenes,Data,Prefabs}` → sahnede `ArenaBoundary.halfExtent*` + duvar/zemin ölçekle + `BaseZone`/`SpawnPoint`'leri yeni kenarlara taşı → yeni `MapDefinition` asset'i (boyut, sceneName) → `GameCatalog`'a ekle → Build Settings'e sahneyi ekle.
3. Duvar/cover yerleşimi elle rötuşlanır (sihirbaz kaba yerleşim yapar, sanat geçişi insanda).

**Bu araçla `A12x12` üret** — Faz 4'ün somut doğrulama içeriği.

## Adım 4 — İşletme (venue) piloti — süreç provası

`Assets/Arenas/Venues/DemoVenue/` — sihirbazla üret (ör. 11×8 asimetrik boyut: gerçek işletme alanları kare olmayacak):
- İşletmeye özel görsel kimlik: logo/duvar materyali `Venues/DemoVenue/Art/`.
- Kontrol listesi çıkar (`Docs/Isletme-Kurulum.md`): fiziksel alan ölçüsü → sihirbaz → kalibrasyon işaretleri (2 nokta zemin bandı) → sunucu PC kurulumu (statik IP, firewall-kur.cmd, AP: 5GHz, client isolation KAPALI — cosmos Server/README kontrol listesi uyarlanır) → APK kurulumu (`install_game.bat`) → smoke test.

## Adım 5 — ~~Opsiyonel: MJPEG izleme (cosmos portu)~~ **İPTAL (2026-07-27)**

> ⚠️ **GEÇERSİZ — bu adım yapılmayacak.** "Oyuncunun gözünden izleme" ihtiyacı **video akışıyla
> değil, zaten aktarılan oyun datasıyla** karşılanacak: admin sahneyi kendi makinesinde render
> ediyor, poz/can/skor/olay ağdan geliyor → admin kamerası hedef oyuncunun poz'una kilitlenir.
> Bu sayede protokole yeni binary kare tipi, sunucuya kare relay'i ve Quest'te encode maliyeti
> girmez. Ayrıntılı tasarım sonraki bir fazda planlanacak (`Docs/Sistem-Ozeti.md` §8).
>
> Aşağıdaki özgün plan yalnız tarihsel kayıt olarak duruyor:
> ~~cosmos `CameraStreamer.cs` (URP `SingleCameraRequest` + `AsyncGPUReadback` + JPEG binary frame)
> → `VortexArena.Net`; protokole binary `0x03 VideoFrame`; sunucuda kare relay; admin'de video
> paneli; varsayılan KAPALI (`set_stream` deseni).~~

## Adım 6 — Doğrulama

1. `Network Parent` menüsü: ekle → sceneId atanır; sahne kopyala-kaydet → çakışma bekçisi onarır.
2. `Export Server Config` → weapons.json/maps.json server'la uyumlu (TDM raundu değerleri SO'dan geliyor).
3. Sihirbazla A12x12 üretildi → Build Settings'te → admin harita seçiminde görünüyor → TDM raundu A12x12'de oynanabiliyor.
4. DemoVenue piloti: kurulum kontrol listesi baştan sona bir kez yürütüldü.
5. ~~(Yapıldıysa) MJPEG: 1 oyuncu akışı açıkken Quest fps kaybı ölçülüp not edildi.~~ — Adım 5 iptal edildi, doğrulama konusu yok.
6. Commit: `Faz 4: editor SDK araçları + arena şablon sihirbazı + A12x12 + işletme piloti`

## Çıktı kontrol listesi

- [x] Net.Editor asmdef + Network Parent menüsü + sceneId bekçisi + NetSpawnCatalog
- [x] Export Server Config (weapons/maps.json otomasyonu) + sunucu `MapTable` doğrulaması
- [x] Arena sihirbazı çalışıyor; A12x12 sihirbazla üretildi ve oynanabilir (loopback TDM raundu)
- [x] DemoVenue (11×8, sihirbazla) + `Docs/Isletme-Kurulum.md`
- [x] (Ops.) MJPEG izleme — **İPTAL EDİLDİ** (video akışı yerine oyun datasıyla izleme; gerekçe aşağıda)
- [ ] DemoVenue kurulum kontrol listesinin sahada bir kez yürütülmesi (kullanıcıda — fiziksel alan gerekir)
- [x] Commit atılmış

## Uygulama notları (planından sapan/eklenen kararlar)

- **Şablon sahnesinde tek yapısal düzeltme:** `PlayArea/Ground` artık saf konteyner; zemin mesh'i
  `Ground/GroundMesh` alt objesine taşındı. Sebep: zemini ölçeklemek çocuk prop'ları (Covers,
  anchor'lar) da ölçekliyor; döndürülmüş çocuklarda non-uniform ölçek KAYMA (shear) üretir.
  Ayrı mesh objesi sayesinde sihirbaz zemini serbestçe ölçekler, prop'lar yalnız KONUM olarak
  oranlanır (biçimleri bozulmaz).
- **Sihirbaz spawn'ları sıfırdan yerleştirir** (oranlamaz): `z_i = -halfZ + (i+1)·2·halfZ/(n+1)`;
  istenen slot sayısı şablondakinden farklıysa klonlar/siler, `slot` alanlarını 0..n-1 yazar,
  yönlerini arena merkezine çevirir. BaseZone'lar duvara YASLANIR (`x = ±(sizeX/2 − halfExtentX)`),
  `halfExtentZ` arena derinliğiyle ölçeklenir.
- **`maps.json` sunucuda opsiyonel:** dosya yoksa `MapTable` boş kalır ve harita doğrulaması
  ATLANIR (Faz 3 davranışı) — sunucu içerik projesi olmadan da koşabilsin. Doluysa `start_match`
  bilinmeyen sahneyi/modu reddeder ve spawn slotu `% spawnSlotsPerTeam` ile sınırlanır.
- **weapons.json export'u mevcut dosyayla bayt-bayt aynı çıktı** (git diff boş) — biçim disiplini
  (alfabetik, LF, BOM'suz, 2 boşluk) tutuyor demektir.
- **PoseBot `--map` / `--mode` bayrakları eklendi** ve `BuildScenes` listesine yeni arenalar girdi;
  aksi hâlde bot'un `hello.scenes`'i eksik kalıp `start_match`'i engelliyordu.
- **MJPEG izleme (Adım 5) İPTAL EDİLDİ** (2026-07-27; önce ertelenmişti). Gerekçe: video akışı
  protokole yeni binary kare tipi, sunucuya kare relay'i ve Quest'te encode/GPU readback maliyeti
  getiriyor — oysa admin sahneyi **zaten kendi makinesinde render ediyor** ve ihtiyaç duyduğu her
  şey (poz, can, skor, olaylar) ağdan **oyun datası olarak** geliyor. "Oyuncunun gözünden izleme"
  bu datadan üretilecek: admin kamerası hedef oyuncunun poz'una kilitlenir. Tasarımı sonraki bir
  fazda planlanacak. Faz 4'ün asıl hedefi (içerik üretimini araçlaştırmak) bundan bağımsız
  tamamlandı.

---

## Faz 4 sonrası ufuk (plana dahil değil — not)

- Quaternion sıkıştırma (smallest-three) + delta snapshot (oyuncu sayısı >16 gerekirse).
- Yeni modlar: FFA, bölge kontrolü (maps.json'daki bölge verisi + server bölge skoru).
- Dinamik obje senkronu (NetIdentity + NetTransform + spawn kataloğu üzerinden pickup/kapı).
- Meta colocation discovery / paylaşımlı uzamsal anchor (elle 2-nokta kalibrasyonun Meta-first alternatifi olarak araştır — offline çalışma ŞART).
- Launcher ekranına APK dağıtımı (adb üzerinden gözlük güncelleme — install_game.bat'in UI'lı hali).
