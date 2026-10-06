# Dev kalibre atlama + çubukla yürüme — plan

Editörde (Quest Link + Multiplayer Play Mode) test ederken fiziksel A/B kalibrasyonu alınmaz: Dev
penceresindeki anahtar açıkken oyuncu mekanın **A işaretinde**, **1,80 m boy** varsayımıyla ve
sunucuya **kalibreli** bildirilmiş olarak başlar; **sol çubuk** rig'i yatayda yürütür, **sağ çubuk**
30° döndürür. Kod ve doküman **bitti**; kalan: doğrulama listesi (§9).

## 0. Değişmez kurallar

Bu işin kabul şartıdır; her madde koda yazılırken tek tek tutulur.

- **Build'e tek satır girmez.** Yeni kodun tamamı `#if UNITY_EDITOR` içindedir (`DevSession` ve
  `WeaponGranter.SequentialGrant` ile aynı çizgi). Sahadaki APK, admin ve sunucu davranışı
  değişmez; `PROTOCOL_VERSION` artmaz, sunucuya kod yazılmaz.
- **"Kalibreliyim"i yine yalnız başlık söyler ve yine aynı yoldan:** dev hizalama
  `ArenaCalibrator.Calibrated` olayını yayar, `CalibrationState` bugünkü `set_calibration` ile
  bildirir. `CalibrationState.IsCalibrated` / `ManualAllowed` yerelde **ezilmez** — ezilirse
  istemci açılır ama sunucu ateşi, hasarı ve canlanmayı reddetmeye devam eder.
- **Rig kökünü oynatan yine yalnız `ArenaCalibrator`'dır.** Yürüme ayrı bir bileşen değil,
  kalibratörün editöre özel parçasıdır. Meta rig'indeki kapalı locomotion yığını **açılmaz**,
  `VA_CameraRig` prefabına dokunulmaz.
- **Operatör sıfırlaması her zaman kazanır.** `clear_calibration` dev hizalamayı da düşürür ve dev
  hizalama kendiliğinden geri gelmez (`autoRestoreBlocked` kapısına uyar). Geri getiren tek şey
  operatörün yeniden yükleme düğmesi ya da Play'in yeniden başlatılmasıdır.
- **Dev hizalama kalibrasyon kaydına YAZMAZ ve dokunmaz:** çapa oluşturulmaz, `AnchorUuidKey`
  (PlayerPrefs) ve `sessionAnchorUuid` değişmez, `capturedCount` artırılmaz (artırılsa elle alınan
  ilk A noktası cihaz kaydını silerdi). Anahtar kapatılınca başlık bugünkü kaydını bugünkü gibi bulur.
- **Bu süreçte alınmış gerçek hizalama dev hizalamayı yener:** `sessionAnchorUuid` doluysa dev dalı
  (açılış ve yeniden yükleme) koşmaz, bugünkü oturum-içi geri yükleme çalışır. Diskteki kaydı ise
  dev dalı yener — amaç zaten o kaydı beklememektir.
- **APK'ya taşınmaz** (karar verildi): yalnız editör + Quest Link.
- **Çubuk yalnız dev hizalama yürürlükteyken çalışır.** Gerçek bir hizalama (elle A/B, çapadan
  geri yükleme) geldiği anda rig fiziksel alana oturmuştur ve çubuk susar.
- **Anahtar kapalıyken hiçbir dal koşmaz:** açılış, ön-hizalama, geri yükleme bugünkü satırlarla
  aynı sırada çalışır.

## 1. Test edenin gördüğü

1. `Tools > VortexArena > Development > Dev` → **Kalibrasyonu atla** işaretlenir (kişisel seçim,
   `EditorPrefs`).
2. Play. Sahne açılınca kafa **A işaretinin üstünde**, A→B'ye bakar, göz hizası 1,69 m
   (1,80 m boyun göz yüksekliği). Admin satırı kalibreli görünür; silah, hasar, canlanma açıktır.
3. Sol çubuk: baktığın yöne göre her yöne yürüme (ileri/geri/yana). Sağ çubuk: 30° adımlı dönüş.
   Sol çubuğa **kısa basış**: boyu yeniden oturt (bulunduğun yerde göz hizası yeniden 1,69 m). Sol
   çubuğa **1 sn basış**: A işaretine dön.
4. Harita değişiminde yeni sahnede yine A'dan başlanır.
5. Gerçek kalibrasyonu denemek için: admin'den kalibrasyon sıfırlanır → çubuk susar, elle A/B
   kapısı açılır. Dev hizalamaya dönmek için admin'in yeniden yükleme düğmesi.

## 2. Anahtar — Dev penceresi

- `DevSession`: yeni `KeySkipCalibration` (`VortexArena.Dev.SkipCalibration`) + `SkipCalibration`
  özelliği (varsayılan kapalı). `Summary` satırına etiketi eklenir.
- `DevSession.ApplySelection` (`BeforeSceneLoad`): **en başta ve koşulsuz**
  `ArenaCalibrator.DevSkipRequested = Enabled && SkipCalibration;` — `Enabled` erken dönüşünden
  önce, çünkü domain reload kapalıyken statik önceki Play'den kalır.
- `DevWindow.DrawSelection`: Sandbox satırının yanına `ToggleLeft` — "Kalibrasyonu atla (A noktası ·
  boy 1,80 · sol çubukla yürü)".
- ⚠️ `EditorPrefs` makine çapındadır: MPPM sanal oyuncuları da aynı değeri okur. Zararsızdır — admin
  süreci rig'i ve kalibratörü kapatır (§3'teki "rig kapalıysa koşmaz" şartı), ikinci bir oyuncu
  süreci de aynı şekilde A'dan başlar.
- Sandbox kipiyle birlikte de çalışır: bağlantı olmadığı için bildirim gitmez, yerleştirme ve
  yürüme aynıdır.

## 3. Dev hizalama — `ArenaCalibrator.Dev.cs`

`ArenaCalibrator` `partial` yapılır; dev kodu ayrı dosyada (`Assets/_Shared/Core/Arena/
ArenaCalibrator.Dev.cs`, tamamı `#if UNITY_EDITOR`) durur. Ana dosya yalnız gövdesiz
`partial void` kancaları taşır — build'de çağrılar derleyici tarafından silinir, ana dosyaya `#if`
girmez.

| Kanca (ana dosyada çağrıldığı yer) | Dev dosyasındaki işi |
|---|---|
| `Start` — işaretçiler yerleştirildikten sonra, `ResolveSavedUuid`'den önce: `DevStart(ref handled)`; `handled` ise ön-hizalama ve geri yükleme **başlatılmaz** | Anahtar açık ve `autoRestoreBlocked` değilse dev hizalama korutinini başlatır |
| `Update` — **en başta** (aşağıdaki erken dönüşlerden önce): `DevUpdate()` | Yürüme, yeniden oturtma, A'ya dönüş (§4) |
| `AlignRig`, `AlignRigToAnchorPose`, `ResetAlignmentState`: `DevAlignmentReplaced()` | `devAligned = false` — çubuk susar |
| `BeginForcedReload` — başında: `DevReload(onResult, ref handled)` | Anahtar açıksa dev hizalamayı yeniden kurar, `onResult("")` |

**Dev hizalama korutini** (`PreAlignWhenTracked`'in ikizi; farkları kalın):

1. İzleme en fazla `PreAlignTrackingTimeout` kadar beklenir; süre dolunca yine uygulanır (HMD'siz
   editör).
2. Vazgeçme şartları ön-hizalamayla aynıdır: jest başladıysa, operatör sıfırladıysa, rig kökü
   kapalıysa (`activeInHierarchy`) koşmaz.
3. Yaw kafanın etrafında A→B'ye çevrilir; kafa **A işaretinin** üstüne,
   `VirtualFloorY + (BodyScaleState.DefaultStatureMeters − BodyScaleState.HeadTopAboveEyeMeters)`
   yüksekliğine taşınır (ön-hizalama A–B ortasını ve `uncalibratedHeadHeight`'i kullanır).
4. `ApplyFloorLift()` → `CalibrationGeneration++`.
5. **`LastFloorOffsetMeters = 0`, `devAligned = this`, `RaiseCalibrated(SourceDev)`** —
   `SourceDev = "dev"`. `devAligned` statik bir kalibratör referansıdır: sahne değişince eski
   örnek yok olur ve referans kendiliğinden düşer.
6. **`CreateAndSaveAnchorAsync` çağrılmaz**, `ReportHeadHeightAfterAlign` çağrılmaz.

- İşaretçi yoksa/aynıysa (kalibrasyon noktası tanımsız boyut dosyası): rig yatayda yerinde kalır,
  yalnız yükseklik oturtulur ve yine kalibreli bildirilir; log bunu söyler.
- "İlk kalibre noktası" `<İşletme>_dimensions.json` → `calibration.a`'dır; kalibratör işaretçiyi
  oradan yerleştirdiği için ayrı bir okuma yazılmaz.
- Yeniden bağlanmada bildirim kendiliğinden tekrarlanır (`CalibrationState._localCalibrated`).
- Takip bozulması yolu (`HandleTrackingDisturbed`) `worldAnchor` istediği için dev hizalamada
  koşmaz; recenter sonrası konum kayarsa A'ya dönüş basışı yeter.

## 4. Çubukla yürüme (`DevUpdate`, yalnız `devAligned` iken)

- Okuma `OVRInput` ile (proje kumandayı yalnız buradan okur): sol çubuk ekseni, sol çubuk basışı
  ve sağ çubuğun yatay ekseni — üçü de proje kodunda boştur (sağ çubuğun **basışı** IP panelinindir,
  dokunulmaz).
- **Yürüme (sol çubuk):** kafanın yatay bakış yönüne göre ileri/geri/yana, sabit hız (2 m/s), ölü
  bölge 0,15. Yalnız X/Z; Y'ye dokunulmaz (kat ofseti `SetFloorLift` deltasıyla bağımsız çalışmaya
  devam eder). ⚠️ `CalibrationGeneration` **artırılmaz** — sürekli harekette her artış bir boy
  ölçümü kurar ve sıçrama bastırıcılarını sıfırlar; yürüme hızı o eşiklerin çok altındadır.
- **Dönüş (sağ çubuk):** eşik 0,7, kenar tetikli (çubuk ortaya dönmeden ikinci adım yok), kafanın
  etrafında ±30°. Kesikli olduğu için `CalibrationGeneration` artar.
- **Yeniden oturt** (kısa basış): rig yalnız Y'de, göz hizası yeniden varsayılan yüksekliğe gelecek
  kadar kayar. Gerekçe: yerleştirme anında gözlük masadaysa ya da test eden oturup kalktıysa
  yükseklik bayatlar. `OVRPlugin.userPresent` yanlıştan doğruya dönünce (gözlük takıldı) aynı iş
  kendiliğinden yapılır.
- **A'ya dön** (1 sn basış): §3'teki 3. adım yeniden uygulanır.
- Kesikli iki hareket `CalibrationGeneration`'ı artırır (rig tek karede sıçrar).
- Duvar çarpışması yoktur ve eklenmez: `ArenaBoundary` ve `ObstacleViolationProbe` kafa konumuna
  baktığı için alan dışı karartması, engel karartması ve ateş kapıları yürüyerek de tetiklenir —
  bunlar test edilebilir kalır, kurtarma yolu A'ya dönüştür.

## 5. Boy

- Bağlantıda zaten 1,80 m varsayımı bildirilir (`BodyScaleState.ReportKnownScale`); dev hizalama
  kafayı o boyun göz hizasına koyduğu için iki sayı tutarlıdır.
- `BodyScaleState.TickAutoMeasure`: dev hizalama yürürlükteyken (`ArenaCalibrator.IsDevAligned`,
  editöre özel statik) ölçüm **başlatılmaz**, yerine `ReportKnownScale()` çağrılır. Gerekçe:
  hizalamadan 10 sn sonraki otomatik ölçüm o anki duruşu (oturan, eğilen test eden) boy diye
  bildirir; ayrıca operatör sıfırlamasından sonra sunucu ölçeği sıfırladığı için yeniden bildirim
  gerekir. Operatörün ÖLÇ düğmesi bugünkü gibi gerçek ölçüm alır.

## 6. Bilinen sınırlar

- **Oturarak test:** göz hizası 1,69 m'ye oturtulduğu için dünya ayakta görünür, ama gövde izleme
  kendi zeminini varsayar — karşı taraf avatarı oturur pozda ve zeminden yüksekte görür. Gövde
  görünümü test edilecekse ayakta durulur.
- **Hizalama testi değildir:** sanal alan fiziksel alanla örtüşmez. Çapa, zemin sapması, takip
  bozulması, A/B jesti bu kiple doğrulanmaz.
- **Birden çok oyuncu aynı A noktasında doğar** (yakınlık uyarısı titreşir); çubukla ayrılınır.

## 7. Protokol ve sunucu

- `Docs/ArenaNet-Protokol.md` → `set_calibration` satırındaki kaynak listesine `dev` eklenir:
  "yalnız editörün ürettiği etiket; fiziksel hizalama yoktur". Sıra: önce doküman, sonra kod.
- Sunucu `source`'u doğrulamadığı için kod değişmez; tel formatı ve sürüm aynıdır.

## 8. Dokümanlar (aynı commit)

- `Docs/Gelistirici/Yapma-Listesi.md` — "Rig'i, kamerayı, oyuncuyu taşıma": dikey ofsetin yanına
  ikinci istisna (yalnız editör, yalnız `ArenaCalibrator`'ın dev parçası, yalnız dev hizalama
  yürürlükteyken). "Kalibrasyon durumunu istemcide doğru kabul etme": dev atlamanın yerel ezme
  olmadığı, aynı bildirim yolunu kullandığı.
- `Docs/Sistem-Ozeti.md` — bileşen sözlüğü (`ArenaCalibrator`: dev hizalama paragrafı ve kancalar;
  `DevSession` / `DevWindow`: yeni anahtar; `BodyScaleState`: dev dalı), gözlüksüz/Link test bölümü,
  Tuzaklar ("Rig'i/kamerayı asla taşıma" maddesine istisna ve gerekçesi; "yapay hareket açılmaz"
  maddesinin Meta yığını için geçerliliğini koruduğu).
- `Docs/Gelistirici/Ilk-Adimlar.md` — Dev penceresi: anahtar, çubuk ve basış işlevleri, sınırlar.
- `Docs/Gelistirici/API-Referansi.md` — `ArenaCalibrator` bölümü.
- `Docs/ArenaNet-Protokol.md` — §7'deki satır.

## 9. Doğrulama (kullanıcı koşar; maddeler Notion kartına)

- [ ] Anahtar kapalı: açılış, ön-hizalama, elle A/B bugünkü gibi
- [ ] Anahtar açık: A'da doğuluyor, admin satırı kalibreli, ateş/hasar/canlanma çalışıyor
- [ ] Sol çubukla yürüme; karşı taraf (admin) avatarı doğru yerde ve gövde rig'le birlikte geliyor
- [ ] Harita değişiminde yeniden A'dan, kalibreli
- [ ] Admin sıfırlaması: çubuk susuyor, elle A/B açılıyor; yeniden yükleme dev hizalamayı geri kuruyor
- [ ] Kısa basış yüksekliği oturtuyor, 1 sn basış A'ya döndürüyor
- [ ] Anahtar açıkken alınan oturumdan sonra anahtar kapatılınca eski çapa kaydı duruyor

## 10. Diğer planlarla kesişim

- **`bulut-kalibre.md`:** iki iş de `ArenaCalibrator.Start`'a dal ekler. Dev dalı önce gelir ve
  `handled` ise bulut dalı koşmaz; bağlantıdaki `load_cloud_calibration{force:false}` dev hizalama
  yürürlükteyken yok sayılır. Hangisi sonra yazılırsa bu satırı kendi tarafında uygular.
