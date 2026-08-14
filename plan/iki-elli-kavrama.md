# Kavrama: kalan iş (yakalama + doğrulama)

Kalan iş **asset tarafında** ve başlıkta doğrulamada.

---

## 1. Avuç ofsetini ÖLÇ (bir kez, başlıkta)

`HandGripPivot.LeftPalmOffset` / `RightPalmOffset` bugün **ergonomik tahmindir**.

1. Başlıkta bir oturum aç, iki kumandayı da normal tut.
2. `HandGripCalibrationProbe` (`VA_CameraRig` üstünde) bir kez log basıp kendini kapatır.
3. Log'daki iki `PalmOffset` satırını `HandGripPivot`'a yapıştır.

⚠️ Ön koşul `OVRManager.controllerDrivenHandPosesType ≠ None` (prefabda `Natural`) — kapalıyken prob
**hatasız ama yanlış** bir sabit basar (bind pozu).

---

## 2. 13 silahın kavramasını YAKALA

⚠️ Kavraması yakalanmamış silah kumandanın ekseninde durur ve **iki elli çözüm de koşmaz**: ön kabza
ekseni (ikincil kayıt − ana kavrama noktası) 1 cm eşiğinin altında kalır, çözücü sessizce tek elli
davranır.

`Tools > VortexArena > Development > Dev` → **Rol: Silah** → silahı seç → Play
(tam reçete: `Docs/Gelistirici/Yemek-Kitabi.md` §11.0):

1. Kumandaları **bırak** (ölçüm el takibiyle yapılır).
2. Sırayla dört ölçü: ana kabza sağ → ana kabza sol → ön kabza sağ → ön kabza sol.
3. Her aşamada eli tutacağın yere getir, **pinch** yap, 5 sn'lik sayaç boyunca elini açıp kabzayı
   sar; sayaç bitince ölçü `WD_*.asset`'e iner.
4. Dördü de yakalanmalı: eksik el öteki elin kaydına düşer ve o el silahı yanlış tutar.

Kapsam: `WeaponKitBuilder` tablosundaki **tüm** `WPN_*`'lar; hangilerinin eksik olduğunu
`Build Weapon Prefabs` koşusunun sonundaki uyarı listeler.

---

## 3. Doğrulama (başlıkta + iki uçta)

- [ ] Silah ele geldiğinde ana kavrama noktası avucun ortasında; el döndükçe kaymıyor.
- [ ] Namlu kumandanın ileri ekseninde (dönüş kimliktir; yatıksa sorun `Model` yerleşimindedir).
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
- [ ] **Tetik çekilince işaret parmağı kıpırdıyor** (silah tutan elde parmaklar serbest).
- [ ] İkinci admin ekranında **uzak** oyuncunun silahının duruşu sapmıyor (iki uç aynı kaydı okuyup
      aynı formülle çiziyor).
- [ ] **Raf değişimi:** elde tüfek varken başka bir çerçeveye nişan alıp grip'e basınca yeni silah
      geliyor (eski silaha kilitlenme yok). Çift elli seçimde elde her zaman **tek** silah kalıyor.
- [ ] FFA'da bir elde çift elli silah varken öteki ele ikinci bir silah **verilmiyor** (o el ön
      kabzaya aday oluyor).
- [ ] FFA'da (`random`) verilen tüfeğin ön kabzası tutulabiliyor ve ikincil soket çiziliyor.
- [ ] Tek elli yol: bir `WD_*` kopyasında `holdMode = OneHand` → iki elde iki klon, ayrı şarjör.
      (Kayıtlı silahların hepsi `TwoHand` olduğu için bu yol başka türlü görünmez.)
