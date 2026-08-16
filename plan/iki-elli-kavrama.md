# Kavrama: kalan iş (kayıtları yazmak + doğrulama)

Kalan iş **asset tarafında** ve başlıkta doğrulamada.

---

## 1. Avuç ofsetini ÖLÇ (bir kez, başlıkta)

`HandGripPivot.LeftPalmOffset` / `RightPalmOffset` bugün **ergonomik tahmindir**.

1. Başlıkta bir oturum aç, iki kumandayı da normal tut.
2. `HandGripCalibrationProbe` (`VA_CameraRig` üstünde) bir kez log basıp kendini kapatır.
3. Log'daki iki `PalmOffset` satırını `HandGripPivot`'a yapıştır.

⚠️ Ön koşul `OVRManager.controllerDrivenHandPosesType ≠ None` (prefabda `Natural`) — kapalıyken prob
**hatasız ama yanlış** bir sabit basar (bind pozu).

### 1b. `HandGripConvention.*AnchorToWrist` sabitini de yapıştır

Anchor→bilek deltası rig varken canlı ölçülür; rig'i olmayan izleyici (admin gözlemci) bu sabite
düşer ve sabit kimlik kaldığı sürece uzak silahları deltanın dönüşü kadar yanlış çizer.

1. Editörde **player** rolüyle Play, iki kumandayı da normal tut.
2. `HandGripPoser` kararlı ölçümü el başına **bir kez** loglar (30 kare / 2 mm / 0.5°).
3. İki satırı `LeftAnchorToWrist` / `RightAnchorToWrist`'e yapıştır.

---

## 2. 13 silahın kavramasını YAZ

⚠️ Kavraması yazılmamış silahta el `Idle`'da kalır ve **iki elli çözüm de koşmaz**: ön kabza ekseni
(ikincil kayıt − ana kavrama noktası) 1 cm eşiğinin altında kalır, çözücü sessizce tek elli davranır.

`Tools > VortexArena > Weapons > Kavrama Pozu Stüdyosu`, prefab kipinde
(tam reçete: `Docs/Gelistirici/Yemek-Kitabi.md` §11.0):

1. `WPN_*`'ı prefab kipinde aç → **Ana Kabza Ellerini Oluştur** (+ **Ön Kabza Ellerini Oluştur**).
2. Dört el de yazılmalı: ana kabza sağ/sol, ön kabza sağ/sol. Eksik el öteki elin kaydına düşer ve
   o el silahı yanlış tutar; **Karşı Ele Aynala** yalnız başlangıçtır.
3. Her elin parmak preset'ini Inspector'dan seç (ana kabza `Firing`, ön kabza `Grip`).
4. **Kaydet** → dördü `WD_*.asset`'e iner.

Kapsam: `WeaponKitBuilder` tablosundaki **tüm** `WPN_*`'lar; hangilerinin eksik olduğunu
`Build Weapon Prefabs` koşusunun sonundaki uyarı listeler.

---

## 3. Doğrulama (başlıkta + iki uçta)

- [ ] Silah ele geldiğinde ana kavrama noktası avucun ortasında; el döndükçe kaymıyor.
- [ ] Silahın eldeki açısı stüdyoda görülenle aynı (ana elde kayıt silahı döndürür); ana elin bileği
      kumandayla serbestçe dönüyor, kilitlenmiyor.
- [ ] Stüdyoda **oluştur → hiçbir şeye dokunma → Kaydet** kayıtlı değeri değiştirmiyor (kimlik
      testi).
- [ ] Boş el ön kabzaya yaklaşınca soket **mavi**, kabul mesafesinde **yeşil** ve büyük.
- [ ] Grip'e basınca silahın yönü ikinci ele döner; bırakınca ~0.08 sn'de yumuşak geri gelir.
- [ ] **Bağ yalnız tuşla kopar:** ön kabza tutulduktan sonra grip'e basılı tutarken kol uzatılıp
      toplanınca, silah yukarı/aşağı/yana çevrilince ve gövde döndürülünce bağ **kopmuyor**;
      yumuşak geri dönüş yalnız tuş bırakıldığında başlıyor.
- [ ] **Savrulma yok:** ikinci el silahın arkasına geçecek kadar sola/geriye çekilince silah ters
      yöne **atlamıyor**; takibi yumuşakça bırakıp ana elin duruşuna dönüyor ve el geri gelince
      aynı yumuşaklıkla tekrar nişanlıyor (`ItemGripSolver.ReachWeight` bandı).
- [ ] Ana kavrama noktası iki elli tutuşta da ana avuçta duruyor (silah ikinci ele kaymıyor).
- [ ] Silahı önce sol elle tutarsan primary sol olur (el ataması sabit değil).
- [ ] **Sol el kaydı ayrı doğrulanır:** aynı silah sol elde de kabzada duruyor, içine gömülmüyor.
- [ ] **Ön kabzada ikinci el silaha yapışık kalıyor:** grip basılıyken kol uzatılıp toplanınca el
      silahtan kopmuyor (kolun gerilmesi beklenen davranıştır).
- [ ] **Tetik çekilince işaret parmağı kıpırdıyor** (`Firing` preset'inde işaret serbest, kalan
      dördü kabzayı sarıyor); boş elde parmaklar `Idle` duruşunda.
- [ ] Uzak avatarın parmakları o slotun preset'iyle çiziliyor (stüdyoda `Grip` seçilen ön kabza elde
      uzakta da sarılı).
- [ ] İkinci admin ekranında **uzak** oyuncunun silahının duruşu sapmıyor (iki uç aynı kaydı okuyup
      aynı formülle çiziyor). ⚠️ Admin'de dönük çiziliyorsa bakılacak yer §1b'deki sabittir.
- [ ] **Raf değişimi:** elde tüfek varken başka bir çerçeveye nişan alıp grip'e basınca yeni silah
      geliyor (eski silaha kilitlenme yok). Çift elli seçimde elde her zaman **tek** silah kalıyor.
- [ ] FFA'da bir elde çift elli silah varken öteki ele ikinci bir silah **verilmiyor** (o el ön
      kabzaya aday oluyor).
- [ ] FFA'da (`random`) verilen tüfeğin ön kabzası tutulabiliyor ve ikincil soket çiziliyor.
- [ ] Tek elli yol: bir `WD_*` kopyasında `holdMode = OneHand` → iki elde iki klon, ayrı şarjör.
      (Kayıtlı silahların hepsi `TwoHand` olduğu için bu yol başka türlü görünmez.)
