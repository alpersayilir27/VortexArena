# Meta Movement SDK ile full body avatar — kalan iş

Kod, protokol, doküman ve **prefab kurulumu** bitti. Her iki avatar prefabında da (yerel + uzak)
aynı Ch15 retarget config'i, `NetworkCharacterRetargeter` + `MetaSourceDataProvider` +
`NetworkCharacterHandler` + `ArenaNetCharacterBehaviour` kurulu ve 66 eklem eşleşmiş durumda.
Kalan iş **ölçüm ve ayar**dır.

## 1. Ölçüm — bütçe doğrulaması

Eklem sayısı 66 ve sıkıştırma `High`; kaba hesapla blob ~300-400 B, yani
`SKELETON_MAX_BLOB_BYTES` (1024 B) rahat altında görünüyor. **Doğrulanmadı.**

- [ ] Aşım varsa: `BodyIndicesToSend`/`BodyIndicesToSync`'ten **parmak eklemlerini çıkar**
      (kumandayla oynanıyor, parmak pozu gerçek veri taşımaz). Hâlâ aşıyorsa sınır büyütülemez —
      `0x07`/`0x08`'e parçalama eklemek gerekir (bugün yok, bilerek).

## 2. Ayar

- [ ] `SkeletonRetargeter.ScaleRange` varsayılanı `0.8–1.2`; **yalnız gövde oranı kalibrasyonu
      açılırsa** anlamlıdır (kapalıyken herkes prefabın oranlarını kullanır ve ölçek uygulanmaz).
      Açılacaksa önce uzak gövdedeki bozulma çözülmelidir: blob `High` sıkıştırmada eklem
      uzunluklarına dayanıyor (§7). **Karar gözle değil konsoldan verilir:** `LocalBodyAvatar`
      kalibrasyondan sonra uygulanan
      gövde ölçeğini bir kez basar ve değer aralığın sınırındaysa uyarıya çevirir — sınıra dayanmış
      bir ölçek, karakterin oyuncunun boyuna yetişemediği, yani **diğer oyuncuların** onu yanlış
      boyda gördüğü anlamına gelir (yerelde çizilmediği için gözle anlaşılmaz).
- Body tracking ayarı **hazır**: `Assets/Resources/OculusRuntimeSettings.asset` (⚠️ dosya adı
  `OVRRuntimeSettings` DEĞİL) `bodyTrackingJointSet: FullBody` + `bodyTrackingFidelity: High`.
  ⚠️ `FullBody` bacakları **izlemez, ÜRETİR**. `OVRBody.StartBodyTracking` bu asset'i okur —
  bileşendeki `ProvidedSkeletonType` alanını DEĞİL; ikisi ayrışırsa SDK uyarı basar.
- İzinler hazır: manifest'te `BODY_TRACKING`/`USE_ANCHOR_API`/`USE_SCENE`, `OVRManager`'da
  `requestBodyTrackingPermissionOnStartup` ve `requestScenePermissionOnStartup` açık.

## Doğrulanacaklar (kullanıcı koşar)

- Uzak avatar full body, doğru boyda, arena zemininde; kalibrasyondan sonra kaymıyor.
- Editörde başlıksız Play'de karakter T-pozunda kalıyor ve konsolu boğmuyor.

## Açık riskler

| Risk | Karşılığı |
|---|---|
| Keyframe blob'u 1024 B'yi aşarsa | Parmak eklemlerini akıştan çıkar; yetmezse `0x07`/`0x08`'e parçalama ekle |
| Paket sayısı §3.12 bütçesini zorlarsa | `SKELETON_RATE_HZ` düşürülür (SDK interpolasyonu var) |
| SDK'nın interpolasyonu ortak saatimizle uyuşmazsa | Saat `RemotePlayerRegistry.TryGetServerTimeSeconds`'tan geliyor; tutmazsa `UseInterpolation = false` + kök interpolasyonuna benzer kendi tamponumuz |
| Sunucu uzun koşunca ortak saatin `float` çözünürlüğü kabalaşırsa | Bir haftalık koşuda ~60 ms, bir iskelet karesinin (83 ms) altında; mekân sunucusu günlük yeniden başlatılıyor |
