# Bulut kalibrasyonu (`anchor_cloud`) — plan

Tek bir gözlüğün hizası, Meta **paylaşılan uzamsal çapa** (grup paylaşımı) ile diğer bütün
gözlüklere dağıtılır: operatör bir gözlüğü dikkatle kalibre eder, kalanlar A/B almadan aynı
hizaya oturur. Henüz **hiçbir şey yazılmadı**.

## 0. Değişmez kural — `two_anchor` ve `saved_anchor` bugünkü gibi kalır

Bu işin kabul şartıdır; her madde koda yazılırken tek tek tutulur.

- **Yeni kod yalnız yeni mesajlarla tetiklenir** ve sunucu o mesajları **yalnız mod
  `anchor_cloud` iken** gönderir. Diğer modlarda oyuncu teline yeni hiçbir şey çıkmaz.
- **`PROTOCOL_VERSION` artmaz:** yeni mesaj türleri ve `admin_state` alanı eklemedir; bilmeyen uç
  yok sayar. `hello` / `welcome` baytları değişmez.
- **Bulut yolu eski kalibrasyon kaydına YAZMAZ:** `AnchorUuidKey` (PlayerPrefs) ve
  `sessionAnchorUuid` bulut yolundan hiç yazılmaz; bulut oturumu kendi statik alanında yaşar.
  Böylece moda geri dönüldüğünde `saved_anchor` gözlüğün **kendi** eski çapasını, `two_anchor`
  elle A/B'yi bugünkü gibi bulur.
- **`DiskRestoreAllowed` değişmez:** `anchor_cloud` açılışta `two_anchor` gibi davranır (diskten
  geri yükleme yok); hizayı bulut komutu getirir.
- **`ArenaCalibrator`'da mevcut satıra dokunan yalnız üç yer vardır**, üçü de bulut oturumu boşken
  etkisizdir: `Start`'taki dal, `RaiseCalibrated`'daki "bulut dışı hizalama bulut oturumunu
  düşürür" satırı, `RealignFromAnchor`'ın bildirdiği kaynak etiketi. Gerisi yeni metottur.
- **Her bulut hatası bugünkü "kalibre değil" hâline düşer:** elle A basılıyken B×2 bulut modunda
  da açıktır; operatör modu `two_anchor`'a çevirince başka hiçbir adım gerekmez.
- **Build genelinde değişen tek şey** `sharedAnchorSupport` proje ayarıdır (APK'ya isteğe bağlı
  `IMPORT_EXPORT_IOT_MAP_DATA` izni ekler). Doğrulamada `two_anchor` modunda açılışta yeni bir
  sistem penceresi çıkmadığına bakılır.
- **Geri dönüş yolu:** iş ayrı dalda yapılır; sahadaki APK + admin + sunucu çıktıları yeni tur
  dağıtılmadan önce saklanır.

## 1. Ön koşullar (kod dışı)

| Koşul | Kaynak | Yokken görülen hata |
|---|---|---|
| Gözlüklerin Wi-Fi'ı **internete çıkar** (paylaşan ve yükleyen, ikisi de) | SDK `LoadUnboundSharedAnchorsAsync` yorumu; Meta SSA sorun giderme | `Failure_SpaceNetworkRequestFailed` / `…NetworkTimeout` |
| Her gözlükte **Enhanced spatial services** açık (Ayarlar → Gizlilik ve güvenlik → Cihaz izinleri) | Meta SSA dokümanı | `Failure_SpaceCloudStorageDisabled` (sistem bir kez sorabilir — odak çalan pencere) |
| Proje ayarında **Shared Spatial Anchor Support** açık | `OVRManifestPreprocessor` | `Failure_PermissionInsufficient` |
| Uygulama geliştirici panelinde kayıtlı + Age Group Self-Certification tamam | Meta SSA sorun giderme ("package is not trusted") | logcat'te `Package … is not trusted` |

- Grup paylaşımı **User ID / User Profile (Data Use Checkup), entitlement ve Platform SDK
  İSTEMEZ**; App ID projeye girilmez, Platform SDK paketi eklenmez.
- ⚠️ Panel kaydının grup paylaşımında zorunlu olup olmadığı Meta dokümanlarında çelişkilidir;
  kayıt ucuz olduğu için önceden yapılır. "not trusted" yine görülürse sıradaki adım aynı paket
  adı + keystore ile bir build'i bir yayın kanalına yüklemektir.
- ⚠️ Meta Horizon Managed Services'e kayıtlı cihazlarda çapa kaydetme/paylaşmanın
  `FailurePermissionInsufficient` ile düştüğü bildirilmiştir; yönetilen cihazda ayrıca denenir.
- ⚠️ Buluttaki çapanın **ömrü sınırlıdır** (Meta süre vermiyor; topluluk bir gün diyor). Kural:
  kaynak **her oturum gününün başında yeniden paylaşılır**. Yerel kopya (§5) bir kez yüklemiş
  gözlüğü bulut ömründen bağımsız kılar.
- ⚠️ İnternet açılınca gözlük sistem güncellemesi indirebilir; otomatik güncelleme kapatılır.
- ⚠️ Meta çapadan ~3 m ötede sapmanın büyüdüğünü söyler; çapa A işaretinin altındadır. Arenanın
  uzak ucunda hizalama yetersiz çıkarsa ikinci adım **iki çapa paylaşmaktır** (A ve B; alıcı
  mevcut iki nokta matematiğiyle hizalanır, A–B mesafesi sağlama olur). İlk sürümde yok.

Bunlar iş bitince `Docs/Isletme-Kurulum.md`'ye işlenir.

## 2. Akış

1. Operatör modu **ÇAPA BULUTU** yapar (`set_calibration_mode{anchor_cloud}`).
2. Bir gözlük elle A/B×2 ile kalibre edilir. Operatör o oyuncuyu seçip **KAYNAK YAP** der
   (`share_calibration{playerId}`).
3. Sunucu yeni bir grup UUID'si üretir, kaynağa `share_calibration{groupUuid}` yollar.
4. Kaynak, kayıtlı `worldAnchor`'ını gruba paylaşır ve sonucu bildirir
   (`share_calibration_result{groupUuid, anchorUuid, error}`).
5. Başarıda sunucu kaydı tutar (bellek + disk), adminlere duyurur ve **diğer bağlı oyunculara**
   `load_cloud_calibration{groupUuid, anchorUuid, force:true}` yollar.
6. Alıcı çapayı yükler, yerelleştirir, `AlignRigToAnchorPose` ile hizalanır ve bugünkü
   `set_calibration{source:"cloud"}` ile bildirir; başarısızsa dolu `error` (bugünkü
   `reload_calibration` sözleşmesinin aynısı → `calibration_result`).
7. Sonradan bağlanan gözlük `welcome`'dan sonra `load_cloud_calibration{force:false}` alır.

## 3. Protokol — önce `Docs/ArenaNet-Protokol.md`, sonra kod

| Mesaj | Yön | Alanlar |
|---|---|---|
| `share_calibration` | admin → sunucu | `playerId` |
| `share_calibration` | sunucu → oyuncu | `groupUuid` |
| `share_calibration_result` | oyuncu → sunucu | `groupUuid`, `anchorUuid`, `error` (boş = başarı) |
| `load_cloud_calibration` | sunucu → oyuncu | `groupUuid`, `anchorUuid`, `force` |
| `admin_state` (ek alan) | sunucu → admin | `cloudSource` (kaynak oyuncunun adı; boş = kayıt yok) |

- `set_calibration_mode` tablosunda `anchor_cloud` satırı "reddedilir"den "kabul edilir"e döner;
  kalibrasyon bölümündeki "Bulut kalibrasyonu (ileride)" paragrafı bu sözleşmeyle değiştirilir.
- **Otorite değişmez:** admin denemeyi başlatır, "hizalandım"ı yine başlık koyar.
- `force:false` (bağlanışta otomatik) şu durumda **yok sayılır**: başlık bu oturumda elle kalibre
  olmuşsa, zaten aynı `anchorUuid`'ye hizalıysa ya da operatör hizalamayı geçersiz kılmışsa
  (`autoRestoreBlocked`). Gerekçe: Wi-Fi kopup gelince maç ortasında rig oynamaz; operatörün
  sıfırlaması bağlantı tazelenince geri gelmez.
- `force:true` (paylaşım sonrası yayın, operatörün yeniden yükleme düğmesi) her zaman dener ve
  `autoRestoreBlocked`'ı açar (`RequestReload` ile aynı niyet).

## 4. Sunucu (`Server/VortexArena.Server.Core`)

- `HandleSetCalibrationModeAsync`: `anchor_cloud` reddi kalkar; `CalibrationModeLabel`'a etiket.
- `share_calibration` (admin): yalnız mod `anchor_cloud` iken, hedef bağlı + kalibreli oyuncuysa
  ve **maç koşmuyorken** kabul edilir; aksi hâlde gerekçesi `admin_state.notice` ile döner
  (maç ortasında kaynak değişimi herkesi aynı anda yeniden hizalar).
- `share_calibration_result`: `groupUuid` beklenenle eşleşmiyorsa yok sayılır. Başarıda kayıt
  `{groupUuid, anchorUuid, kaynak cihaz kimliği, kaynak adı, venueId}` — bellek + sunucunun
  yanındaki `cloud-calibration.json` (`VenueSurveyStore` yazma deseni: temp → `.prev` → taşı).
  Hatada kayıt değişmez, gerekçe adminlere duyurulur.
- Kayıt açılışta okunur; `venueId` sunucununkiyle uyuşmuyorsa yok sayılır (çapa mekâna aittir).
  ⚠️ Mod kalıcılaştırılmaz — sunucu bugünkü gibi `two_anchor` ile açılır; kayıt durduğu için
  yeniden paylaşım gerekmez, operatör yalnız modu yeniden seçer.
- `HandleHelloAsync`: mod `anchor_cloud` + kayıt varsa oyuncuya `welcome` sonrası
  `load_cloud_calibration{force:false}`.
- `HandleReloadCalibrationAsync`: mod `anchor_cloud` + kayıt varsa hedeflere `reload_calibration`
  yerine `load_cloud_calibration{force:true}` gider (tek oyuncu ve `playerId:0`). Kayıt yoksa
  bugünkü davranış.
- `ClientConnection`: iki yeni `case` (admin kapısı `share_calibration`'da, oyuncu kapısı
  sonuçta).

## 5. Gözlük (`ArenaCalibrator`, `CalibrationState`, `ArenaClient`, `NetEvents`)

- **Bulut oturumu:** yeni statik `(groupUuid, anchorUuid)`; PlayerPrefs'e yazılmaz, uygulama
  kapanınca gider.
- **Paylaş** (`share_calibration`): şart — hizalı, `worldAnchor` bağlı ve **kaydedilmiş**
  (`sessionAnchorUuid == worldAnchor.Uuid`; `worldAnchor` kayıttan önce atanıyor, tek başına
  yeterli değil). `OVRSpatialAnchor.ShareAsync(new[]{worldAnchor}, groupUuid)`. Şart tutmuyorsa
  hata: "kayıtlı çapa yok — önce elle kalibre edin". Hata metni Meta sonuç adını taşır.
- **Yükle** (`load_cloud_calibration`), sırayla:
  1. Zaten bu `anchorUuid`'ye bağlı ve hizalıysa: iş yok, başarı.
  2. Yerel depo: `LoadUnboundAnchorsAsync([anchorUuid])` — kaynağın kendisi ve çapayı daha önce
     yerelde saklamış alıcı internetsiz hizalanır.
  3. Bulut: `LoadUnboundSharedAnchorsAsync(groupUuid, [anchorUuid])` → `LocalizeAsync` → bağla →
     `MeasureFloorOffset` → `AlignRigToAnchorPose` → `RaiseCalibrated("cloud")`; ardından
     **en iyi çaba** `SaveAnchorAsync` (yerel kopya; başarısızlığı yalnız loglanır).
  - Deneme sayısı/aralığı mevcut geri yükleme sabitleriyle aynı (yerelleştirme oyuncunun etrafa
    bakmasını isteyebilir).
  - Başarısızlıkta rig'e dokunulmaz, mevcut durum korunur; gerekçe `set_calibration{error}` ile
    gider (`HandleReloadResult` deseni).
- **Harita değişimi:** `Start`, bulut oturumu doluysa ve `autoRestoreBlocked` değilse eski geri
  yükleme yerine aynı yükleme rutinini koşar (2. adım sayesinde normalde internetsiz).
- **Bulut oturumunu düşürenler:** bulut dışı her başarılı hizalama (`RaiseCalibrated` içinde tek
  satır: elle yakalama, eski yeniden yükleme).
- **Takip bozulması:** `RealignFromAnchor` `worldAnchor`'ı kullandığı için aynen çalışır; bildirdiği
  kaynak, çapa bulut çapasıysa `"cloud"` olur.
- **Tel:** `NetEvents`'e iki olay, `ArenaClient` dağıtım `switch`'ine iki `case`,
  `CalibrationState`'e iki abone (clear/reload deseni; kalibratör sahnede aranmaz, statik seam).

## 6. Admin arayüzü — yeni öğe: bir düğme + bir satır, yalnız bulut modunda görünür

- **Tercihler → Kalibrasyon:** mevcut **ÇAPA BULUTU** düğmesi bağlanır ve seçilebilir olur
  (`ApplyCalibrationMode`).
- Aynı bloğun altına, **yalnız mod `anchor_cloud` iken görünen**:
  - durum satırı: `Bulut kaynağı: <oyuncu adı>` / `Bulut kaynağı: yok — bir oyuncu seçip KAYNAK YAP`
  - **KAYNAK YAP** düğmesi: `AdminSession.SelectedPlayerId`'yi kaynak yapar; iki adımlı
    (`EMİN?`, AT düğmesinin onay penceresi) — yanlış basış herkesi yeniden hizalar. Seçili oyuncu
    yokken ya da kalibre değilken sönük.
- **Oyuncu satırları değişmez:** İstatistik satırındaki KALİBRE / KALİBRE ! aynı anlamda; kaynak etiketi mevcut ayrıntı
  satırında `cloud` için "bulut" yazar. Hata mevcut `calibration_result` balonuyla görünür.
- **Tek oyuncuyu yeniden denetmek:** mevcut KALİBRE (yeniden yükle) düğmesi — bulut modunda sunucu
  onu bulut yüklemesine çevirir (§4). Yeni düğme yok.
- `AdminCommands.ShareCalibration(playerId)`, `CalibrationModeLabel` bulut etiketi,
  `AdminSelection.CloudSource`.
- Prefab: panel prefabına bir `Button` + bir `TextMeshProUGUI`; alanlar geç bağlanır ve null
  güvenli okunur (bağlanmamış alan paneli durdurmaz).

## 7. Proje ayarı

- `OculusProjectConfig`: **Shared Spatial Anchor Support** açılır. `colocationSessionSupport`
  açılmaz (grup kimliğini kendi sunucumuz taşır), Passthrough eklenmez, elle tutulan
  `AndroidManifest.xml`'e dokunulmaz (izni build'de SDK ekler).

## 8. Dokümanlar (aynı commit)

`Docs/ArenaNet-Protokol.md` (sabit satırı, mesaj tabloları, kalibrasyon bölümü) ·
`Docs/Sistem-Ozeti.md` (ağ mantığı, bileşen sözlüğü, Tuzaklar: internet / Enhanced spatial
services / bulut ömrü / eski kayda yazmama) · `Docs/Isletme-Kurulum.md` (internet, gözlük ayarı,
otomatik güncelleme) · `Docs/Kullanim-Kilavuzu.md` (kalibre modu bölümü: üç adımlı akış) ·
`Docs/Gelistirici/Yapma-Listesi.md` (bulut yolu `AnchorUuidKey` / `sessionAnchorUuid`'ye yazmaz).

## 9. Doğrulama (kullanıcı koşar; maddeler Notion kartına)

- [ ] `two_anchor` ve `saved_anchor`: açılış, harita değişimi, KALİBRE / SIFIRLA bugünkü gibi; açılışta
      yeni sistem penceresi yok
- [ ] Bulut: KAYNAK YAP sonrası ikinci gözlük A/B almadan hizalanıyor, iki oyuncu birbirini doğru
      yerde görüyor (arenanın uzak ucunda da)
- [ ] Sonradan açılan gözlük bağlanınca kendiliğinden hizalanıyor; harita değişiminde hizalama
      korunuyor
- [ ] İnternet kapalıyken / Enhanced spatial services kapalıyken hata satırda okunuyor, elle A/B
      çalışıyor
- [ ] Moddan çıkış: `two_anchor`'a dönüp gözlüğü yeniden açınca elle kalibrasyon istiyor

## Kaynaklar

- https://developers.meta.com/horizon/documentation/unity/unity-shared-spatial-anchors/
- https://developers.meta.com/horizon/documentation/unity/unity-ssa-ts/
- https://developers.meta.com/horizon/documentation/unity/unity-spatial-anchors-best-practices/
- https://developers.meta.com/horizon/documentation/unity/unity-mrmotifs-colocated-experiences/
