# Meta Movement SDK ile full body avatar — kalan iş

Kod, protokol, doküman ve **prefab kurulumu** bitti. Her iki avatar prefabında da (yerel + uzak)
aynı Ch15 retarget config'i, `NetworkCharacterRetargeter` + `MetaSourceDataProvider` +
`NetworkCharacterHandler` + `ArenaNetCharacterBehaviour` kurulu ve 66 eklem eşleşmiş durumda.
Kalan iş **ayar**dır.

## 1. Ayar

Gövde oranı kalibrasyonu açılmaz (`plan/README.md` "Değişmeyecekler"); `ScaleRange` bu yüzden
ayarlanmaz.

- Body tracking ayarı **hazır**: `Assets/Resources/OculusRuntimeSettings.asset` (⚠️ dosya adı
  `OVRRuntimeSettings` DEĞİL) `bodyTrackingJointSet: FullBody` + `bodyTrackingFidelity: High`.
  ⚠️ `FullBody` bacakları **izlemez, ÜRETİR**. `OVRBody.StartBodyTracking` bu asset'i okur —
  bileşendeki `ProvidedSkeletonType` alanını DEĞİL; ikisi ayrışırsa SDK uyarı basar.
- İzinler hazır: manifest'te `BODY_TRACKING`/`USE_ANCHOR_API`, `OVRManager`'da
  `requestBodyTrackingPermissionOnStartup` açık. Sahne (uzamsal veri) izni bilerek yok
  (`Yapma-Listesi.md`).

## Açık riskler

| Risk | Karşılığı |
|---|---|
| Paket sayısı §3.12 bütçesini zorlarsa | `SKELETON_RATE_HZ` düşürülür (alıcı kök ve kemikleri `INTERP_DELAY_MS` tamponunda interpole ediyor) |
