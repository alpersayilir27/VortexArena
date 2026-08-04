# Kavrama: kalan iş (kavrama pozu ayarı + doğrulama)

Kod, çözücü ve doküman yerinde (`Docs/Sistem-Ozeti.md` §4 `HandGripPivot` / `ItemGripSolver` /
`ItemGripSockets` / `WeaponGranter`, §7 son iki madde). Kalan iş **asset/prefab tarafında** ve
başlıkta doğrulamada.

---

## 1. Avuç ofsetini ÖLÇ (bir kez, başlıkta)

`HandGripPivot.LeftPalmOffset` / `RightPalmOffset` bugün **ergonomik tahmindir**.

1. Başlıkta bir oturum aç, iki kumandayı da normal tut.
2. `HandGripCalibrationProbe` (`VA_CameraRig` üstünde) bir kez log basıp kendini kapatır.
3. Log'daki iki `PalmOffset` satırını `HandGripPivot`'a yapıştır.

⚠️ Ön koşul `OVRManager.controllerDrivenHandPosesType ≠ None` (prefabda `Natural`) — kapalıyken prob
**hatasız ama yanlış** bir sabit basar (bind pozu).

---

## 2. Altı silahın kavrama pozunu YAZ

⚠️ Bugün `WD_*.asset`'lerin hepsinde `primaryGripEuler` **sıfır** ve `primaryGripPosition` ≈ 0:
silah kumandanın ekseninde duruyor. ⚠️ **İki elli çözüm de bu yüzden koşmaz** — ön kabza ekseni
(`secondaryGrip − primaryGripPointOnItem`) yazılmamış bir silahta 1 cm eşiğinin altında kalır ve
çözücü sessizce tek elli davranır.

Her `WPN_*` prefabında:
1. `GripSocket_Primary` işaretçisini kabzaya sürükle ve **elin gireceği açıyla döndür**.
2. `GripSocket_Secondary`'yi ön kabzaya taşı.
3. `Tools > VortexArena > Weapons > Write Grip Sockets To Definition`.
4. **Camgöbeği tel küre sarı dolu küreyle ÇAKIŞMALI** — çakışmıyorsa yazma gitmemiştir.

Silahlar: `WPN_AK47` · `WPN_M4A1` · `WPN_M16` · `WPN_G36C` · `WPN_FAMAS` · `WPN_SCARL`.

---

## 3. Doğrulama (başlıkta + iki uçta)

- [ ] Silah ele geldiğinde `GripSocket_Primary` avucun ortasında; el döndükçe kaymıyor.
- [ ] Boş el ön kabzaya yaklaşınca soket **mavi**, kabul mesafesinde **yeşil** ve büyük.
- [ ] Grip'e basınca silahın yönü ikinci ele döner; bırakınca ~0.08 sn'de yumuşak geri gelir.
- [ ] Ana kavrama noktası iki elli tutuşta da ana avuçta duruyor (silah ikinci ele kaymıyor).
- [ ] Silahı önce sol elle tutarsan primary sol olur (el ataması sabit değil).
- [ ] İkinci admin ekranında **uzak** oyuncunun silahı aynı açıda duruyor (iki uç sapmıyor).
- [ ] **Raf değişimi:** elde tüfek varken başka bir çerçeveye nişan alıp grip'e basınca yeni silah
      geliyor (eski silaha kilitlenme yok). Çift elli seçimde elde her zaman **tek** silah kalıyor.
- [ ] FFA'da bir elde çift elli silah varken öteki ele ikinci bir silah **verilmiyor** (o el ön
      kabzaya aday oluyor).
- [ ] FFA'da (`random`) verilen tüfeğin ön kabzası tutulabiliyor ve ikincil soket çiziliyor.
- [ ] Tek elli yol: bir `WD_*` kopyasında `holdMode = OneHand` → iki elde iki klon, ayrı şarjör.
      (Kayıtlı altı silahın hepsi `TwoHand` olduğu için bu yol başka türlü görünmez.)
