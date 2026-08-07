# Kavrama: kalan iş (kavrama pozu ayarı + doğrulama)

Kalan iş **asset/prefab tarafında** ve başlıkta doğrulamada.

---

## 1. Avuç ofsetini ÖLÇ (bir kez, başlıkta)

`HandGripPivot.LeftPalmOffset` / `RightPalmOffset` bugün **ergonomik tahmindir**.

1. Başlıkta bir oturum aç, iki kumandayı da normal tut.
2. `HandGripCalibrationProbe` (`VA_CameraRig` üstünde) bir kez log basıp kendini kapatır.
3. Log'daki iki `PalmOffset` satırını `HandGripPivot`'a yapıştır.

⚠️ Ön koşul `OVRManager.controllerDrivenHandPosesType ≠ None` (prefabda `Natural`) — kapalıyken prob
**hatasız ama yanlış** bir sabit basar (bind pozu).

---

## 2. Altı silahın kavrama duruşunu ve el pozunu YAZ

⚠️ `primaryGripEuler` sıfır + `primaryGripPosition` ≈ 0 olan bir silah kumandanın ekseninde durur ve
**iki elli çözüm de koşmaz**: ön kabza ekseni (`secondaryGrip − primaryGripPointOnItem`) 1 cm
eşiğinin altında kalır, çözücü sessizce tek elli davranır.

Her `WPN_*` prefabında, `Tools > VortexArena > Weapons > Kavrama Pozu Stüdyosu` ile:
1. **Tezgâhı Aç** → `El_Primary`'yi kabzaya oturt ve **elin gireceği açıyla döndür** (silah sabit).
2. Parmakları Hierarchy'den bük: avuç kabzayı saracak, işaret parmağı tetiğe ulaşacak biçimde.
   ⚠️ Tüfekte **işaret parmağı `Free` bırakılır** — kilitli parmak ateş ederken kıpırdamaz ve oyuncu
   tetiği çektiğini elinde göremez.
3. **Kaydet** → tanım alanları + parmak pozu yazılır, sol el aynalanır.
4. **Ön kabza eli**'ni seçip ön kabzaya taşı, tekrar Kaydet.
5. **Camgöbeği tel küre tezgâhtaki elin bileğiyle ÇAKIŞMALI** — çakışmıyorsa kayıt gitmemiştir.

Silahlar: `WPN_AK47` · `WPN_M4A1` · `WPN_M16` · `WPN_G36C` · `WPN_FAMAS` · `WPN_SCARL`.

---

## 3. Doğrulama (başlıkta + iki uçta)

- [ ] Silah ele geldiğinde ana kavrama noktası avucun ortasında; el döndükçe kaymıyor.
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
- [ ] **Avuç kabzayı sarıyor:** parmaklar kabzanın içinden geçmiyor, havada da durmuyor.
- [ ] **Baş parmak tetiğe/kabzanın üstüne ulaşıyor**, tetik korkuluğunun içine gömülmüyor.
- [ ] **Ön kabzada ikinci el silaha yapışık kalıyor:** grip basılıyken kol uzatılıp toplanınca el
      silahtan kopmuyor (kolun gerilmesi beklenen davranıştır).
- [ ] **Tetik çekilince işaret parmağı kıpırdıyor** (poz onu kilitlememiş).
- [ ] Silah sol elle tutulduğunda **aynalanan poz** doğru: parmaklar aynı yöne sarılıyor, bilek ters
      dönmüyor.
- [ ] İkinci admin ekranında **uzak** oyuncunun silahının duruşu sapmıyor (el pozu yerelde
      uygulanıyor, silahın pozunu iki uç aynı formülle çiziyor).
- [ ] **Raf değişimi:** elde tüfek varken başka bir çerçeveye nişan alıp grip'e basınca yeni silah
      geliyor (eski silaha kilitlenme yok). Çift elli seçimde elde her zaman **tek** silah kalıyor.
- [ ] FFA'da bir elde çift elli silah varken öteki ele ikinci bir silah **verilmiyor** (o el ön
      kabzaya aday oluyor).
- [ ] FFA'da (`random`) verilen tüfeğin ön kabzası tutulabiliyor ve ikincil soket çiziliyor.
- [ ] Tek elli yol: bir `WD_*` kopyasında `holdMode = OneHand` → iki elde iki klon, ayrı şarjör.
      (Kayıtlı altı silahın hepsi `TwoHand` olduğu için bu yol başka türlü görünmez.)
