# Meta Movement SDK ile full body avatar — kalan iş

Kod, protokol, doküman ve **prefab kurulumu** bitti. Her iki avatar prefabında da (yerel + uzak)
aynı Ch15 retarget config'i, `NetworkCharacterRetargeter` + `MetaSourceDataProvider` +
`NetworkCharacterHandler` + `ArenaNetCharacterBehaviour` kurulu ve 66 eklem eşleşmiş durumda.
Kalan iş **ölçüm ve ayar**dır.

## 1. Ölçüm — bütçe doğrulaması

Eklem sayısı 66 ve sıkıştırma `High`; kaba hesapla blob ~300-400 B, yani
`SKELETON_MAX_BLOB_BYTES` (1024 B) rahat altında görünüyor. **Doğrulanmadı.**

- [ ] Oyunu koştur ve konsolda `[UdpStateChannel] İskelet blob'u … B — tavan …` uyarısının
      çıkmadığını gör (çıkarsa blob sınırı aşıyor demektir).
- [ ] Sunucu konsolundaki `[state]` satırında `iskelet … p/s` değerini oku, `Docs/Sistem-Ozeti.md`
      §3.12 paket bütçesiyle karşılaştır.
- [ ] Aşım varsa: `BodyIndicesToSend`/`BodyIndicesToSync`'ten **parmak eklemlerini çıkar**
      (kumandayla oynanıyor, parmak pozu gerçek veri taşımaz). Hâlâ aşıyorsa sınır büyütülemez —
      `0x07`/`0x08`'e parçalama eklemek gerekir (bugün yok, bilerek).

## 2. Ayar

- [ ] `SkeletonRetargeter.ScaleRange` varsayılanı `0.8–1.2`; oynayan boy aralığına göre genişlet.
      `ApplyHeadScale` **açık kalsın** (birinci şahısta yakayı near-clip'in dışında tutan şey odur).
- [ ] Bacak isteniyorsa `OVRRuntimeSettings`: `BodyJointSet.FullBody` +
      `BodyTrackingFidelity2.High` (IOBT). Varsayılanlar `UpperBody` + `Low`.
      ⚠️ `FullBody` bacakları **izlemez, ÜRETİR** — eklem sayısını artırır, blob'u büyütür.
- [ ] İzin (`com.oculus.permission.BODY_TRACKING`) ve `requestBodyTrackingPermissionOnStartup`
      zaten açık; doğrula.
- [ ] Yerel gövdede `SkinnedMeshRenderer.quality = Bone4` (§7) ayarlı mı kontrol et; uzak
      avatarlarda Auto kalır.

## Doğrulanacaklar (derlemeyi kullanıcı koşar)

- Yerel oyuncu kendi kollarını/bileklerini doğru görüyor; kafa/boyun görünmüyor.
- Uzak avatar full body, doğru boyda, arena zemininde; kalibrasyondan sonra kaymıyor.
- Gövde ile eller arasında zaman kayması yok (ikisi de `INTERP_DELAY_MS` tamponunda).
- Harita değişiminde ve avatar başka oyuncuya devredilince gövde bozulmuyor.
- Silah iki tarafta da elde doğru duruyor; çift ellide boş el kabzada.
- Vuruş kutuları gövdeyle örtüşüyor (kafa vuruşu çarpanı doğru) — kutular
  `mixamorig:Head`/`mixamorig:Spine1` kemiklerinde asılı, retargeter onları sürüyor.
- Admin (Windows) gözlemcide gövdeler çiziliyor.
- Editörde başlıksız Play'de karakter T-pozunda kalıyor ve konsolu boğmuyor.

## Açık riskler

| Risk | Karşılığı |
|---|---|
| Keyframe blob'u 1024 B'yi aşarsa | Parmak eklemlerini akıştan çıkar; yetmezse `0x07`/`0x08`'e parçalama ekle |
| Paket sayısı §3.12 bütçesini zorlarsa | `SKELETON_RATE_HZ` düşürülür (SDK interpolasyonu var) |
| SDK'nın interpolasyonu ortak saatimizle uyuşmazsa | Saat `RemotePlayerRegistry.TryGetServerTimeSeconds`'tan geliyor; tutmazsa `UseInterpolation = false` + kök interpolasyonuna benzer kendi tamponumuz |
| Sunucu uzun koşunca ortak saatin `float` çözünürlüğü kabalaşırsa | Bir haftalık koşuda ~60 ms, bir iskelet karesinin (83 ms) altında; mekân sunucusu günlük yeniden başlatılıyor |
