# Yerel kol görseli (oyuncunun kendi kollarını görmesi)

**Hedef:** oyuncu aşağı baktığında kendi **kollarını** görsün (omuzdan ele), sadece elleri değil.
Free-roam'da kalibre edilen rig'i bozmadan.

## Bugünkü durum

- **Yerel gövde avatarı YOK.** Oyuncu kendinden yalnız BB etkileşim rig'inin ellerini görür
  (`OVRHandVisualLeft`/`OVRHandVisualRight`); kumanda modelleri ve sentetik el kopyaları
  `ControllerModelHider` ile gizlenir.
- `_Shared/Avatars/PlayerBodyAvatar.prefab` (Mixamo Ch15 + Movement SDK `CharacterRetargeter` +
  `MetaSourceDataProvider`) **asset olarak duruyor ama hiçbir sahnede bağlı değil** —
  `VA_CameraRig`'den çıkarıldı. Yanındaki üç bileşen de yalnız ona hizmet ediyor:
  `LocalAvatarHeadHider`, `LocalBodyOverlayCamera`, `LocalAvatarRootDetacher`.
- Uzak oyuncuların gövdesi bundan **bağımsızdır** ve çalışıyor: `ThreePointBodyIK`, ağdan gelen
  üç pozdan (kafa + iki el) çözülen gerçek IK. Bu iş ona hiç dokunmaz.

## Neden kaldırıldı — tekrarlanmaması gereken hata

Movement SDK'nın retarget çıktısı **dünya uzayındadır**:
`SkeletonUtilities.GetPosesFromTheTracker` her kemiğe
`OVRCameraRig.trackingSpace.localToWorldMatrix`'i baskılar, `ConvertWorldToLocalPoseJob` kök
eklemi ebeveynine göre yerelleştirmeden bırakır ve `ApplyPoseJob` onu
`SetLocalPositionAndRotation` ile yazar. Bu projede kök eklem avatarın **kendi transformudur**;
avatar `VA_CameraRig`'in altındayken rig transformu **iki kez** uygulanıyordu → kalibrasyondan
sonra avatar oyuncudan tam bir kalibrasyon ofseti kadar uzağa oturuyordu (ayrıntı ve belirti
`Docs/Sistem-Ozeti.md` §7, "retarget avatarı hareket eden kökün altına konmaz" maddesi).

Sahne köküne ayırma (`LocalAvatarRootDetacher`) denendi ve **yetmedi** — çakışma tek bir
parenting düzeltmesiyle kapanmıyor.

⚠️ Yani bu işe girişirken **`PlayerBodyAvatar`'ı olduğu gibi rig'e geri asmak bir seçenek
değildir.** Kolları rig'e (kamera/el anchor'larına) göre konumlandıran bir yol seçilmeli.

## Yapılacak

1. **Kol modeli:** gövdesiz, omuzdan ele bir kol çifti (ayrı mesh). Ch15'in tek
   SkinnedMeshRenderer'ından renderer kapatarak kol izole edilemez — kollar omurga zincirinin
   çocuğudur, göğsü gizlemek kolları da götürür. Yani ya arms-only mesh ya shader maskesi.
2. **Sürücü:** kolların başlangıç noktası (omuz) **kamera rig'ine göre** sabitlenir; el hedefi
   `leftHandAnchor`/`rightHandAnchor`. Yani Movement SDK gövde takibi DEĞİL, iki kemiklik basit
   bir kol IK'sı — `ThreePointBodyIK`'in kol çözümü (`SolveArm`, CCD) buna zaten hazır bir
   referans. ⚠️ Kemik dizisi UÇ→KÖK ve tolerans KARE (§7'deki CCD maddesi).
3. **Near-clip:** kol kameraya çok yaklaştığında içi görünür. `LocalBodyOverlayCamera` bu sorunu
   çözmek için yazılmıştı (ayrı katman + daha büyük near-clip'li URP overlay kamera); yeniden
   kullanılabilir, ama **ek bir kamera geçişi maliyeti** var — önce kolu near-clip'e hiç
   sokmayan bir yerleşimle denenmeli.
4. **Silahla ilişki:** silah kavrandığında el pozu `ApplyCanonicalGrip` ile sürülüyor; kol IK'sı
   o pozu hedef almalı, yoksa kol silahtan kopuk görünür.

## Nereye dokunulur

- `Assets/_Shared/Avatars/` — kol modeli + prefab
- `Assets/_Shared/Core/Player/` — kol sürücüsü (yeni bileşen)
- `Assets/_Shared/App/Prefabs/VA_CameraRig.prefab` — kol prefabı buraya bağlanır
- Ağ tarafında **iş yoktur**: bu tamamen yerel bir görseldir, protokole hiçbir şey eklenmez.

## Bittiğinde

`Docs/Sistem-Ozeti.md` §4'teki "Yerel gövde avatarı bugün YOKTUR" notu güncellenir, `CLAUDE.md`'nin
`VA_CameraRig` satırındaki uyarı düzeltilir, bu dosya **silinir** ve `plan/README.md`'den satırı
çıkarılır.
