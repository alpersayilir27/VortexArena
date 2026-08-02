# Meta Movement SDK ile full body avatar — kalan iş

Kod, protokol ve doküman tarafı bitti. Kalan iş **editörde** yapılır: ölçüm, retarget config'i,
prefablar, sahne bağları. Bunlar bitene kadar uzak avatarlar **kapsül** yolundan çizilir
(`RemoteAvatar.character` boşken devreye giren yedek) — yani sistem çalışır durumda kalır ama gövde
yoktur.

## 1. Ölçüm — diğer her şeyden önce

Blob boyutu (`B`) bilinmeden bütçe de sınır da doğrulanamaz.

- [ ] `AdvancedSamples` örneğini import et → `MovementNetworking.unity`.
      `NetworkCharacterBehaviourLocal` ekrana **kb/s** basıyor.
- [ ] **Ch15 karakteriyle** ölç ve not et: delta kapalıyken ortalama paket ve **en büyük** paket.
- [ ] En büyük paket `SKELETON_MAX_BLOB_BYTES` (1024 B) altında mı? Değilse:
      `BodyIndicesToSend`/`BodyIndicesToSync`'ten **parmak eklemlerini çıkar** (kumandayla
      oynanıyor, parmak pozu gerçek veri taşımıyor) ve yeniden ölç. Hâlâ aşıyorsa sınır
      büyütülemez — `0x07`/`0x08`'e parçalama eklemek gerekir (bugün yok, bilerek).
- [ ] Sunucu konsolundaki `[state]` satırında `iskelet … p/s` değerini oku, §3.12 bütçesiyle
      karşılaştır.

## 2. Karakter prefabı

- [ ] Ch15 için SDK'nın retarget config'ini (`.json`) editör aracıyla üret.
- [ ] `GameObject > Movement SDK > Networking > Add Networked Character Retargeter` ile karakteri
      kur. Üstüne `ArenaNetCharacterBehaviour` ekle (`NetworkCharacterHandler` zorunlu bileşen
      olarak zaten gelir).
- [ ] `NetworkCharacterRetargeter` ayarları:
      **`UseDeltaCompression = false`** (⚠️ açılırsa ack yolu ve `RemoteSkeletonRegistry`'nin tek
      kareli blob yuvası kuyruğa çevrilmek zorundadır — §6.9),
      `CompressionType = High`, `UseInterpolation = true`,
      `IntervalToSendData = 1/SKELETON_RATE_HZ`.
- [ ] `MetaSourceDataProvider` retargeter ile **aynı GameObject'te** durmalı
      (`CharacterRetargeter.Awake` onu `GetComponent` ile arıyor). Kapatmayı kod yapıyor, prefabdan
      silme.
- [ ] `SkeletonRetargeter.ScaleRange` varsayılanı `0.8–1.2`; oynayan boy aralığına göre genişlet.
      `ApplyHeadScale` **açık kalsın** (birinci şahısta yakayı near-clip'in dışında tutan şey odur).

## 3. Prefab bağları

- [ ] `Avatars/Resources/LocalBodyAvatar.prefab`: içeriği yeni karakter olacak
      (⚠️ **ad ve konum değişmez** — `Resources.Load` ile yükleniyor). Eski `ThreePointBodyIK`
      bileşeni silindiği için prefabda **eksik script** görünür, temizlenmeli.
      `LocalBodyAvatar` alanları: `character`, `retargeter`, `visualRoot`.
      `LocalAvatarBoneHider` kalır (kafa/boyun/üst bacak gizlenir).
- [ ] `_Shared/App/Prefabs/RemoteAvatar.prefab`: aynı karakter + `RemoteAvatar.character` alanı
      bağlanır. Burada da eksik script temizliği var.
- [ ] `RemoteHitBox` collider'ları retarget edilmiş iskelet kemiklerine yeniden bağlanır.
- [ ] Yerel gövdede `SkinnedMeshRenderer.quality = Bone4` (§7): uzak avatarlarda Auto kalır.

## 4. Body tracking ayarları

- [ ] Bacak isteniyorsa `OVRRuntimeSettings`: `BodyJointSet.FullBody` +
      `BodyTrackingFidelity2.High` (IOBT). Varsayılanlar `UpperBody` + `Low`.
      ⚠️ `FullBody` bacakları **izlemez, ÜRETİR** — eklem sayısını artırır, blob'u büyütür.
- [ ] İzin (`com.oculus.permission.BODY_TRACKING`) ve `requestBodyTrackingPermissionOnStartup`
      zaten açık; doğrula.

## Doğrulanacaklar (derlemeyi kullanıcı koşar)

- Yerel oyuncu kendi kollarını/bileklerini doğru görüyor; kafa/boyun görünmüyor.
- Uzak avatar full body, doğru boyda, arena zemininde; kalibrasyondan sonra kaymıyor.
- Gövde ile eller arasında zaman kayması yok (ikisi de `INTERP_DELAY_MS` tamponunda).
- Harita değişiminde ve avatar başka oyuncuya devredilince gövde bozulmuyor.
- Silah iki tarafta da elde doğru duruyor; çift ellide boş el kabzada.
- Vuruş kutuları gövdeyle örtüşüyor (kafa vuruşu çarpanı doğru).
- Admin (Windows) gözlemcide gövdeler çiziliyor.
- Editörde başlıksız Play'de karakter T-pozunda kalıyor ve konsolu boğmuyor.
- Sunucu `[state]` satırındaki paket sayısı §3.12 bütçesinin içinde.

## Açık riskler

| Risk | Karşılığı |
|---|---|
| Keyframe blob'u 1024 B'yi aşarsa | Parmak eklemlerini akıştan çıkar; yetmezse `0x07`/`0x08`'e parçalama ekle |
| Paket sayısı §3.12 bütçesini zorlarsa | `SKELETON_RATE_HZ` düşürülür (SDK interpolasyonu var) |
| SDK'nın interpolasyonu bizim ortak saatimizle uyuşmazsa | Saat `RemotePlayerRegistry.TryGetServerTimeSeconds`'tan geliyor; tutmazsa `UseInterpolation = false` + kök interpolasyonuna benzer kendi tamponumuz |
| Sunucu uzun koşunca ortak saatin `float` çözünürlüğü kabalaşırsa | Bir haftalık koşuda ~60 ms, bir iskelet karesinin (83 ms) altında; mekân sunucusu günlük yeniden başlatılıyor |
