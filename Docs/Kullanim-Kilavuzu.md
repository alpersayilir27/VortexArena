# VortexArena — Günlük Kullanım Kılavuzu (Operatör)

Bu kılavuz **sahada sistemi çalıştıran kişi** içindir. Bilgisayar/ağ bilgisi gerektirmez;
adımları sırayla uygulaman yeterlidir. Teknik detay aramıyorsan bu dosyadan başka bir şey
okumana gerek yok.

> Kurulum (ilk defa gelen ekip, kablolama, zemin işaretleri) bu kılavuzun konusu değildir →
> `Docs/Isletme-Kurulum.md`.

---

## Sistem üç parçadan oluşur

| Parça | Nerede | Ne işe yarar |
|---|---|---|
| **Sunucu** | Sunucu bilgisayarında bir siyah konsol penceresi | Oyunun beyni. Canları, skoru, maçı o yönetir. **Her zaman ilk açılan, en son kapanan.** |
| **Yönetim (Admin) ekranı** | Yönetim bilgisayarında bir pencere | Senin panelin: **oyuncuların içinde olduğu sahneyi canlı görürsün**, üstündeki menülerden harita/mod seçip maçı başlatırsın. |
| **Gözlükler (Quest)** | Oyuncuların başında | Oyuncunun oynadığı yer. |

**Altın kural:** Sıra her zaman **Sunucu → Yönetim ekranı → Gözlükler**. Sunucu kapalıyken
diğer ikisi hiçbir işe yaramaz.

---

## 0. Seans öncesi 60 saniyelik özet

Deneyimli operatör için kısa liste — detaylar aşağıdaki bölümlerde.

- [ ] **1.** Yönetim bilgisayarında **Launcher**'ı aç → **Sunucuyu Başlat** (siyah pencere açık kalsın).
      *Sunucu ayrı bir bilgisayardaysa oradan elle başlat ve sorduğunda **mekanı seç**.*
- [ ] **2.** Launcher'da **Yönetimi Başlat**.
- [ ] **3.** Gözlükleri aç, oyunu başlat → kendiliğinden bağlanırlar.
- [ ] **4.** Yönetim ekranında oyuncuların listede göründüğünü doğrula.
- [ ] **5.** Mod + harita seç → **Maçı Başlat**.
- [ ] **6.** Her oyuncuya zemindeki **A** ve **B** işaretlerinde kalibrasyon yaptır.
- [ ] **7.** Maç bitince **kazanan ekranı ekranda kalır ve seni bekler** — sıradaki haritayı (ya da
      Lobi'yi) seçtiğinde herkes oraya geçer. Yeni maç için 5. adıma dön.

---

## 1. Sunucuyu başlatmak

Sunucu **kendiliğinden açılmaz**, her seansta başlatılır. Bu bilinçli bir tercihtir: oyunun tek
karar mercii odur, yanlışlıkla kapanmasın diye kimse onu senin yerine kapatmaz.

Sunucu hangi bilgisayarda kuruluysa oradan açılır. İki yol vardır; kurulumunda hangisi geçerliyse
onu uygula (bilgi kartında yazar).

### 1.A Sunucu yönetim bilgisayarındaysa — Launcher'dan

- [ ] Masaüstündeki **VortexArena Launcher** kısayoluna çift tıkla.
- [ ] **1 · Sunucu** bölümünde listeden **işletmenin adını** seç (kurulumda seçili bırakılmış
      olmalı; değiştirmen gerekmez).
- [ ] **Sunucuyu Başlat**'a bas → siyah sunucu penceresi açılır.
- [ ] Aşağıdaki **"Ne görmelisin"** listesini kontrol et.

> Launcher mekanı sunucuya kendisi bildirir; sana soru sorulmaz. Bu yüzden **yanlış işletmenin
> açılması mümkün değildir** — listede yanlış satır seçiliyse zaten gözle görürsün.
> Mekan seçilmeden **Sunucuyu Başlat** çalışmaz; "Mekan seçilmedi" uyarısı görürsen listeden seç.

### 1.B Sunucu ayrı bir bilgisayardaysa — elle

- [ ] Sunucu bilgisayarını aç, ağ kablosunun/Wi-Fi'nin bağlı olduğunu gör.
- [ ] Masaüstündeki **VortexArena Sunucu** kısayoluna çift tıkla.
      (Kısayol yoksa: `deploy\server\` klasöründeki **`VortexArena.Server.App.exe`** dosyası.)
- [ ] Siyah bir pencere açılır ve **hangi mekanın açılacağını sorar:**

```
Hangi mekan açılsın?
  1) Outdoor12x12  (3 harita)
  2) VortexAntep   (2 harita)
Seçim [1-2]:
```

      İşletmenin adını içeren satırın numarasını yazıp **Enter**'a bas. (Tek mekan varsa soru
      çıkmaz, kendiliğinden onu açar.) **Yanlış seçersen** yönetim ekranında başka bir işletmenin
      haritalarını görürsün — o zaman sunucuyu kapatıp yeniden aç, mekan çalışırken değişmez.

### Ne görmelisin (iki yolda da aynı)

- [ ] Sunucu penceresinde şuna benzer bir liste:

```
Mekan      : <İşletmenin adı>
Aktif alan : <seçtiğin mekan>
Modlar     : tdm, ffa, tournament
Haritalar  : Arena12x12, IceWorld, Lobby12x12
Lobi       : Lobby12x12
```

- [ ] **Bu üç satırı görüyorsan sistem hazırdır:** `Mekan`, `Modlar`, `Haritalar`.
- [ ] **Pencereyi KAPATMA.** Küçültebilirsin, ama kapatırsan oyun durur. Seans boyunca açık kalır.

**Dikkat edilecekler**

- İlk açılışta Windows "bu uygulamanın ağa erişmesine izin verilsin mi?" diye sorarsa
  **"İzin ver"e** bas. Yanlışlıkla "İptal" dendiyse teknik ekibi ara (güvenlik duvarı ayarı
  yeniden yapılmalı).
- `Haritalar : yok` yazıyorsa maç başlatılamaz → teknik ekibi ara.
- Sunucuyu kapatmak için pencereye tıklayıp **Ctrl + C** yap ya da pencereyi kapat.
  Bunu **günün sonunda**, herkes çıktıktan sonra yap.

---

## 2. Yönetim (Admin) panelini açmak

- [ ] Launcher zaten açık olmalı (Bölüm 1). Değilse masaüstündeki **VortexArena Launcher**
      kısayoluna çift tıkla.
      (Kısayol yoksa: `deploy\launcher\` klasöründeki **`VortexArena.Launcher.exe`**.)
- [ ] **2 · Bağlantı** bölümünde **Sunucu IP** kutusunda sunucu bilgisayarının adresi yazıyor
      olmalı (ör. `192.168.1.10`). Yazmıyorsa bilgi kartındaki adresi yaz. **Port** kutusuna
      dokunma (`47821` kalsın).
- [ ] **3 · Yönetim oyunu** bölümündeki dosya yolu doluysa (`...\deploy\admin\VortexArena.exe`)
      hazırsın. Boşsa **Gözat** ile o dosyayı bir kez seç — bir daha sormaz.
- [ ] Büyük **Yönetimi Başlat** düğmesine bas.
- [ ] Oyun penceresi açılır ve **IP sormadan doğrudan yönetim ekranına** düşer.

> ⚠️ **Oyunun kendi exe dosyasına doğrudan çift tıklama.** Adres bilgisi launcher tarafından
> verilir; doğrudan açarsan oyun "Sunucu bulunamadı" der ve hiçbir şey yapamazsın.
> Her zaman launcher'dan başlat.

**Ekranda ne görürsün**

- Bağlantı kurulana kadar kısa bir "bağlanılıyor" yazısı.
- Bağlantı yoksa birkaç saniye sonra turuncu şeritli bir **hata kartı** çıkar: hangi adrese
  bağlanmaya çalıştığını, kaç saniyedir denediğini yazar ve bir **Yeniden Bağlan** düğmesi
  sunar. Bu kartı görüyorsan önce **sunucu penceresi açık mı** diye bak.
- Her şey yolundaysa **oyuncuların bulunduğu sahne** açılır (lobide lobi, maçta arena) ve
  üstünde yönetim bilgileri görünür: ortada skor, yanlarda oyuncu listeleri, altta kamera
  seçenekleri. Ayrı bir "dashboard" ekranı yoktur; her şey bu canlı görüntünün üstündedir.

**Launcher'daki diğer düğmeler**

- **Durdur:** launcher'dan başlattığın yönetim oyununu kapatır.
- **Sunucuyu Başlat:** Bölüm 1'deki sunucuyu açar (mekanı listeden alır).
- **Yenile:** mekan listesini sunucunun dosyalarından yeniden okur — yeni bir arena eklendikten
  sonra liste eksik görünüyorsa buna bas.
- Launcher sunucuyu **kapatmaz**. Kapatmak için sunucunun kendi siyah penceresinde **Ctrl + C**.

---

## 3. Quest gözlüklerini bağlamak

### 3.1 Normal yol — otomatik

- [ ] Gözlüğü tak/aç, doğru Wi-Fi'ye bağlı olduğundan emin ol (arenaya özel ağ).
- [ ] VortexArena uygulamasını başlat.
- [ ] Uygulama **lobiye** düşer ve **sunucuyu kendisi bulup bağlanır** — kimseye adres
      sorulmaz. Ekranda "Bağlı — oyuncu N" benzeri bir durum satırı görürsün.
- [ ] Yönetim ekranındaki listede o gözlüğün **numarası ve adı** (ör. `7 · ertu`) belirir.
      Görünüyorsa iş tamam.

Bir gözlük ilk bağlandığında sunucu ona kalıcı bir **isim** (hazır listeden rastgele) ve kalıcı bir
**forma numarası** (1'den başlayarak ilk boş sayı) verir. İsimler tekrar edebilir, **numara asla** —
iki oyuncuyu ayırt eden şey numaradır. Hangi kimliğin hangi fiziksel gözlük olduğunu bulmak için
yönetim ekranındaki **"Bu cihazı tanıt"** düğmesini kullan: o gözlüğün ekranında büyük bir uyarı
belirir.

İsmi veya numarayı değiştirmek istersen: listeden oyuncuya tıkla, **Tercihler** panelini aç ve
**OYUNCU KİMLİĞİ** bölümünden düzenleyip **KİMLİĞİ UYGULA**'ya bas. Verdiğin numara o an bağlı
başka bir oyuncudaysa değişiklik kabul edilmez ve durum satırında sebebi yazar.

### 3.2 Bağlanmazsa — gizli IP paneli: **sağ kumandada joystick'e 1 saniye bas**

Bazı ağlarda gözlük sunucuyu kendiliğinden bulamaz. Bu durumda adres **elle** girilir.
Panel normalde **gizlidir** (oyuncular karıştırmasın diye) ve şu jestle açılır:

> ### 🎮 Sağ kumandadaki **joystick'e bastır ve 1 saniye basılı tut**
>
> - Joystick'i yana itmek değil, **aşağı bastırmak** (tıklamak) gerekir.
> - Parmağını **bir saniye boyunca kaldırma** — arada bırakırsan sayaç sıfırlanır.
> - Süre dolduğunda kumanda **kısa bir titreşim** verir ve panel açılır.
> - Aynı jest paneli **kapatır** da.

Panel açılmıyorsa: basılı tutma süresi yetmemiştir → biraz daha uzun bekle. Yanlış kumandaya
basıyor olabilirsin → **sağ** kumanda.

**Panelde adresi girmek**

- [ ] Panelde bir **sayı tuş takımı** (0–9, `.`, `:`) ve **Bağlan** düğmesi vardır.
      Kumandanın işaretçisiyle tuşlara nişan alıp tetikle bas.
- [ ] Sunucu bilgisayarının adresini nokta nokta gir: örneğin **`192.168.1.10`**
      → yazarken `1` `9` `2` `.` `1` `6` `8` `.` `1` `.` `1` `0` tuşlarına bas.
- [ ] **Port yazmana gerek yok** — boş bırakırsan otomatik doğru port kullanılır.
      (Yine de yazmak istersen: `192.168.1.10:47821`)
- [ ] Yanlış tuşa bastıysan **geri silme** tuşuyla düzelt, hepsini silmek için **Temizle**.
- [ ] **Bağlan**'a bas. Durum satırı "Bağlanıyor…" → "Bağlı" olmalı.
- [ ] "Geçersiz adres" yazıyorsa yazımı kontrol et (fazladan nokta, eksik rakam).

> ✅ **Bir kez girmen yeterli:** girdiğin adres o gözlükte **kalıcı olarak saklanır**.
> Sonraki açılışlarda gözlük doğrudan oraya bağlanır, paneli bir daha açman gerekmez.
> Sunucu bilgisayarının adresi değişirse bu adresi güncellemek gerekir.

**Yardımcı ipuçları**

- Gözlük sunucuyu ~8 saniye bulamazsa lobide zaten
  *"Sunucu bulunamadı. Adresi elle girmek için sağ kumandada joystick'e 1 sn basılı tut."*
  yazısı çıkar — yani jesti ezberlemek zorunda değilsin, ekran hatırlatır.
- Maç sırasında bağlantı koparsa oyuncunun ekranında turuncu şeritli bir kart belirir ve
  aynı ipucunu verir. Gözlük arka planda **kendiliğinden yeniden bağlanmayı dener**;
  sunucu geri geldiğinde kart kaybolur.

### 3.3 Sunucu bilgisayarının adresini (IP) nereden bulurum?

- **En kolay:** kurulumda bırakılan **bilgi kartında** yazar (SSID, Wi-Fi şifresi, sunucu adresi).
- **Kart yoksa:** sunucu bilgisayarında Başlat menüsüne `cmd` yazıp aç, `ipconfig` yazıp Enter'a
  bas. Çıkan listede **"IPv4 Adresi"** satırındaki `192.168...` ile başlayan numara odur.
- Bu numarayı bilgi kartına yaz; her seferinde aramak zorunda kalma.

---

## 4. Kalibrasyon — zemindeki **A** ve **B** işaretleri

Oyuncular fiziksel alanda gerçekten yürüdüğü için, her gözlüğün "odanın neresindeyim"
bilgisini bir kez öğrenmesi gerekir. **Kalibrasyon yapılmazsa oyuncular birbirini yanlış
yerde görür.**

Kalibrasyon **lobide, maç başlamadan önce** yapılır. Lobi de gerçek bir oda: oyuncular orada
birbirini görür, kumandanın **yan tuşunu (grip) basılı tutarak** eline gelen silahla hedef
tahtalarına ateş edebilir (birbirlerine hasar
veremezler) ve zemindeki A/B işaretleri oradadır. Bir kez kalibre olan oyuncu maça hazır girer —
harita değişimi kalibrasyonu bozmaz. (Maç sırasında da aynı adımlarla yeniden kalibre edilebilir.)

- [ ] **1.** Oyuncu sağ kumandanın **ucunu** zemindeki **A** bandına değdirir. ⚠️ **Nasıl tuttuğu
      önemli değil** (dik, eğik, yan — fark etmez); önemli olan **ucun yere değmesidir**.
- [ ] **2.** Sağ kumandada **A tuşunu basılı tutarken B tuşuna hızlıca iki kez basar**
      (iki basış arası 1 saniyeyi geçmemeli) → **kısa titreşim** = A noktası alındı ve
      zeminde A çapası belirir. Basılı tutma/bekleme yoktur.
- [ ] **3.** Aynısını zemindeki **B** bandında yapar → **uzun titreşim** (~1 sn) + **iki çapa da
      belirip bir saniye sonra kaybolur** = tamam, arena hizalandı. Bekleme yoktur, oyuncu
      doğrulup devam edebilir.
- [ ] **4.** Hata sinyalleri — ikisinde de B'yi tekrar al (çapalar kaybolmaz, ikinci çapa hiç
      belirmez): **üç kısa titreşim** = iki nokta arasındaki mesafe yanlış (bant ölçüsünü
      kontrol et) · **tek titreşim** = bir noktada kumandanın ucu yere değmemiş, iki zemin ölçümü
      birbirini tutmadı.

> ⚠️ **Karakterin boyu burada ölçülmez** — o ayrı bir adımdır ve **sana aittir**: §4.2.

**Bilmen gerekenler**

- **Uygulama her açıldığında kalibrasyon baştan alınır** — kutudan çıkan ayar budur. Gözlükte
  saklanan eski hizalamanın açılışta geri yüklenmesini istiyorsan **kalibre modunu** değiştirirsin
  (§4.3).
- **Oyun içinde kalibrasyon korunur:** harita değişse de, maç bitip lobiye dönülse de oyuncu
  yeniden kalibre olmaz. Kalibre modu yalnız **uygulamanın ilk açılışını** ilgilendirir.
- ⚠️ **Kalibrasyon bir kez alındıktan sonra oyuncu kumandayla onu değiştiremez.** Kombinasyonu
  yapsa da tek titreşim alır ve hiçbir şey olmaz. Bu bilerek böyledir: oyuncunun maç ortasında
  kazara kendi hizalamasını bozmasını engeller. **Yeniden kalibre ettirmek senin işin** →
  aşağıdaki §4.1.
- **Harita değiştirmek kalibrasyonu bozmaz.** Yeni arenaya geçince oyuncular yeniden kalibre
  etmez, bir yere de "ışınlanmaz" — oldukları yerde kalırlar, yeni arena üzerlerine hizalanır.
  Yeni harita yüklenirken birkaç saniye halkalar kaybolabilir; hizalama oturunca geri gelir.
  ⚠️ Bunun şartı: **aynı işletmede hep aynı ölçüdeki arenayı oynat** (zemindeki A/B işaretleri
  değişmediği sürece kalibrasyon geçerli kalır).
- **A ile B'yi karıştırma!** Ters alınırsa arena 180° dönük olur, oyuncular birbirini
  ters tarafta görür.
- **Henüz kalibre olmamış** oyuncuların birbirinin üstünde/yanlış yerde görünmesi **normaldir** —
  örtüşme kalibrasyondan sonra oturur. Bağlantı ekranında (gözlük sunucuyu henüz bulamadıysa)
  gösterilen basit bekleme sahnesinde de örtüşme beklenmez.

### 4.1 Bir oyuncunun kalibrasyonu kaydıysa — **KAL** düğmesi

Maç sırasında bir oyuncunun ekrandaki yeri fiziksel yerinden kaymış görünüyorsa (arkadaşları onu
duvarın içinde/yanlış yerde görüyor, kendisi "ben oraya nişan almadım" diyor), o oyuncunun
kalibrasyonu bozulmuştur. **Maçı durdurmana gerek yok:**

- [ ] **1.** Yönetim ekranında o oyuncunun satırındaki **KAL** düğmesine bas.
      Düğme **yeşil KAL** ise kalibrasyonu iyidir; basınca **EMİN?** yazar.
- [ ] **2.** Tekrar bas → oyuncunun kalibrasyonu sıfırlanır. Satırın çerçevesi kırmızı olur,
      düğme **kırmızı KAL !** olur ve kolon başlığında "1 KALİBRESİZ" yazar.
- [ ] **3.** O andan itibaren oyuncu **ateş edemez, vurulamaz ve ölürse canlanmaz**;
      diğer oyuncuların ekranında **avatarı yanıp söner** (kimin sorunlu olduğu belli olsun).
      Kendi ekranında "Kalibrasyon gerekli — sağ kumandada A basılıyken B×2" yazar.
- [ ] **4.** Oyuncu §4'teki adımlarla **yeniden kalibre olur** (artık kombinasyon açıktır).
      Bitince tik kendiliğinden yeşile döner.
- [ ] **5.** Oyuncu **kaldığı yerden devam eder** — canı, öldürme sayısı ve skoru sıfırlanmaz.

**Maç öncesi hepsini birden aldırmak için:** TERCİHLER > KALİBRASYON >
**"TÜM KALİBRASYONLARI SIFIRLA"** (iki kez basılır). Herkes aynı anda kalibresiz olur ve
sırayla §4'teki adımları yapar.

> **Neden tik'i elle geri açamıyorsun:** hizalamanın gerçekten düzeldiğini yalnız gözlüğün
> kendisi bilir. Sen "tamam" diyebilseydin, aslında hâlâ kaymış bir oyuncu ateş etmeye ve
> vurulmaya devam ederdi — yani düzeltmeye çalıştığın sorunun ta kendisi sürerdi.

### 4.2 Karakterlerin boyunu ayarlamak — **ÖLÇ** düğmesi

Herkesin karakteri varsayılan olarak **aynı boydadır**. Oyuncunun kendi boyunda görünmesi için
ölçüsünü **senin** aldırman gerekir; kalibrasyonla birlikte olmaz, çünkü oyuncu kalibrasyon
sırasında kumandayı yere değdirmek için **eğilmiş** durumdadır.

- [ ] **1.** Oyuncu kalibre olduktan sonra **ayakta ve dik** dursun (çömelme, eğilme, yürüme yok).
- [ ] **2.** Yönetim ekranında o oyuncunun satırındaki **ÖLÇ** düğmesine bas. Onay istemez.
- [ ] **3.** Düğmenin etiketi ölçülen çarpanı gösterir (`×1.04` gibi) — bu, ölçümün oturduğu
      anlamına gelir. Karakteri o anda herkeste yeniden boyutlanır.
- [ ] **4.** Ölçüm tutmazsa düğmede **ÖLÇÜLEMEDİ** yazar (eski değer olduğu gibi durur) ve
      ekranın duyuru satırında sebebi görünür. İki sebep vardır:
      - *"oyuncu hareketli/eğilmiş"* → en sık olan. Dik durmasını söyle ve tekrar bas.
      - *"gövde pozu yok"* → o gözlük gövde takibi üretemiyor; ölçüm hiç yapılamaz.
        Aynı oyuncu başkalarının ekranında **donuk bir T-pozunda** duruyorsa teşhis kesindir →
        §4.4'teki bakım adımlarını uygula.

**Hepsini birden ölçmek için:** TERCİHLER > **"TÜM OYUNCULARI ÖLÇEKLE"**. Herkesin dik durduğu
bir an seç — maç başlamadan önceki hazırlık en uygunu.

**Bilmen gerekenler**

- Düğme **pasifse** oyuncu kalibresizdir: önce kalibrasyon, sonra ölçüm.
- **Kalibrasyonu sıfırlarsan ölçü de sıfırlanır** (zemin geçersizleştiği için). Oyuncu yeniden
  kalibre olunca ÖLÇ'e tekrar basman gerekir.
- Ölçü gözlükte saklanır: aynı oyuncu ertesi gün bağlandığında boyu kendiliğinden geri gelir.
- Aynı oyuncuyu **iki kez ölçmek zararsızdır** — aynı sonucu verir.

### 4.3 Kalibre modu — uygulama açılışında ne olsun?

Gözlük kalibrasyonu kendi içinde saklar. **Açılışta o kaydın kullanılıp kullanılmayacağını sen
seçersin:** TERCİHLER > **KALİBRASYON** bölümündeki üç düğme.

| Düğme | Ne olur | Ne zaman seçilir |
|---|---|---|
| **2 ÇAPA** *(varsayılan)* | Uygulama her açıldığında eski kayıt **kullanılmaz**; oyuncu kumandayla A ve B noktalarını yeniden alır | **Güvenli seçim, açık bırak.** Bina çok katlıysa, gözlükler başka odalarda da kullanılıyorsa ya da hizalama sorunları yaşanıyorsa mutlaka bu |
| **ESKİ KALİBRE** | Uygulama açılırken gözlükteki son kalibrasyon geri yüklenir; oyuncu hiçbir şey yapmadan hazır gelir | Tek katlı, hep aynı odada oynatılan, sorunsuz çalışan bir kurulumda seansı hızlandırır |
| **ÇAPA BULUTU** | Bugün **hiçbir şey yapmaz** — ileride kullanılmak üzere ayrılmış bir seçenek | Seçme |

**Bilmen gerekenler**

- Ayar **sunucuda** durur: tüm yönetim ekranları aynı değeri gösterir, birinden değiştirmen yeter.
- ⚠️ **Değişiklik o an bağlı gözlüklere işlemez.** Her gözlük ayarı **bağlandığı anda** bir kez
  okur. Yeni modu bir gözlüğe uygulatmak istiyorsan o gözlükte uygulamayı kapatıp yeniden aç.
  Pratik sonuç: modu **seans başlamadan**, gözlükler bağlanmadan önce seç.
- Mod ne olursa olsun **oyun içinde kalibrasyon korunur** — harita değiştirmek kimseyi yeniden
  kalibre ettirmez. Seçim yalnız "uygulama açılırken eski kayda güvenilsin mi" sorusudur.
- **ESKİ KALİBRE'de bir oyuncu yanlış yerde başlarsa** çözüm bellidir: satırındaki **KAL**
  düğmesiyle sıfırla, elle yeniden kalibre olsun (§4.1). Bu tekrarlıyorsa modu **2 ÇAPA**'ya al.

### 4.4 Bakım — gözlüğün kendi alan verisini temizlemek

Gözlük, içinde bulunduğu ortamın haritasını **kendi başına** çıkarır ve saklar. Bu harita oyuna ait
değildir; **oyun onu silemez**, yalnız gözlüğün kendi ayarlarından temizlenir. Guardian
(oyun alanı çizme) kapalı olsa bile bu harita arka planda birikmeye devam eder — aynı gözlük başka
bir katta veya odada kullanıldığında ortamlar birbirine karışır ve tipik olarak
**"oyuncunun yeri doğru ama yüksekliği yanlış"** sonucunu verir.

**Şu belirtilerden biri varsa temizlik yap:**

- Yönetim ekranında bir oyuncunun kalibre etiketi turuncu **KAL ?** oldu (aşağıda).
- Gözlük başka bir kata/odaya götürülüp geri getirildi.
- Kalibrasyon tuttu ama oyuncu diğer ekranlarda **havada ya da yere gömülü** duruyor; boyu
  saçmalıyor.
- Oyuncu diğer ekranlarda sürekli **donuk bir T-pozunda** duruyor (kolları yana açık, hiç
  kıpırdamıyor ama konumu doğru takip ediyor).

**Zemin sapması uyarısı (KAL ?) ne demek**

Oyuncu elle kalibre olurken sistem iki şeyi karşılaştırır: gözlüğün kendi zemin tahmini ile
kumandanın gerçekten yere değdiği nokta. Aradaki fark büyükse yönetim ekranında bir duyuru düşer ve
o oyuncunun satırındaki kalibre etiketi turuncu **KAL ?** olur (kırmızı **KAL !** ile karıştırma —
o "kalibresiz" demektir ve oyuncu oynayamaz).

- **Kalibrasyon yine de geçerlidir** — ölçüm zemini zaten düzeltiyor, maça devam edebilirsin.
- **KAL ?** bir **bakım sinyalidir**: o gözlüğün alan verisi bozulmuş demektir. Seans arasında
  temizle, yoksa aynı gözlükte yükseklik sorunları tekrarlar.

**Temizlik adımları**

- [ ] **1.** Oyuncu gözlüğü çıkarmadan VortexArena uygulamasından çıksın (ya da uygulamayı kapat).
- [ ] **2.** Gözlüğün kendi **Ayarlar** menüsünü aç → **fiziksel alan / oyun alanı / izleme**
      ile ilgili bölümü bul → **kayıtlı alan verilerini temizle** seçeneğini uygula.
      *(Meta menü adlarını sürümden sürüme değiştiriyor; aradığın şey "oyun alanı", "fiziksel
      alan" ya da "izleme" başlığı altındaki **temizleme/sıfırlama** seçeneğidir.)*
- [ ] **3.** Gözlüğü arenanın ortasında **birkaç saniye dolaştır** ki ortamı yeniden tanısın.
- [ ] **4.** VortexArena'yı aç, gözlük bağlansın.
- [ ] **5.** Oyuncuya §4'teki adımlarla **yeniden kalibrasyon** yaptır, sonra **ÖLÇ**'e bas (§4.2).

> Temizlikten sonra da T-poz sürüyorsa sorun alan verisinde değildir → teknik ekibi ara.
> T-poz **yalnız başkalarının ekranında** görünür; oyuncunun kendi ekranında hiçbir belirti
> olmaz, yani "bende bir şey yok" demesi normaldir.

---

## 5. Maçı başlatmak ve yönetmek (Yönetim ekranı)

Yönetim ekranındaki dashboard'da elindeki kontroller:

| Kontrol | Ne yapar | Ne zaman kullanılır |
|---|---|---|
| **Oyuncu listesi (roster)** | Bağlı oyuncular, takımları, çevrimiçi/çevrimdışı durumu | Maç öncesi herkesin bağlı olduğunu doğrulamak için |
| **Kırmızı / Mavi** | Seçili oyuncunun takımını değiştirir | Takımları elle dengelemek için (boş bırakırsan sistem otomatik dengeler) |
| **Bu cihazı tanıt** | O gözlüğün ekranında büyük bir işaret gösterir | "Bu listedeki isim hangi gözlük?" sorusuna cevap |
| **Çıkar (kick)** | Oyuncuyu atar — **o gözlükteki oyun kapanır** | Yanlışlıkla bağlanan/oyunda olmaması gereken cihaz |
| **Mod seçimi** | Oyun türü: **Takım Ölüm Maçı** (kırmızı-mavi), **Herkes Tek** (takım yok, herkes herkese karşı) veya **Turnuva** (turlar hâlinde takım elemesi). Satıra bas, liste aşağı açılır, seçeceğine tıkla | Her maç öncesi — aşağıdaki "Üç oyun modu" kutusuna bak |
| **Harita seçimi** | Hangi arenada oynanacağı — mod seçimiyle aynı açılır liste. Listenin **ilk satırı "Lobi"dir**: seçersen herkes lobiye döner | Her maç öncesi — sadece seçili modla uyumlu haritalar listelenir |
| **Maçı Başlat** | Herkesi arenaya alır, geri sayımı başlatır | Herkes bağlı ve hazır olduğunda |
| **Maçı İptal** | Maçı erken bitirir, herkesi lobiye döndürür | Acil durum, oyuncu değişimi, yanlış harita |
| **Dost ateşi** | Takım arkadaşının da vurulup vurulamayacağını belirler (Tercihler → MAÇ bölümü). **Kapalı** (varsayılan): takım arkadaşına ateş etsen de canı azalmaz. **Açık**: azalır. Satırdaki iki düğmeden hangisine bassan aç/kapa yapar; açıkken değer kırmızı yanar | Diğer satırların aksine **maç sırasında da değiştirilebilir** — maçı iptal etmen gerekmez, etkisi anında geçer. Takım arkadaşını öldürmek **puan kazandırmaz** (ceza da yoktur, öldürme listesinde yine görünür). Ayar sunucu kapanana kadar kalır: maç bitince, harita değişince kendiliğinden kapanmaz |
| **Skor ve öldürme akışı** | Ortada canlı skor + faz/süre, sağ altta "kim kimi vurdu" listesi | Maç sırasında takip |
| **Kamera: Kuş bakışı** (`3`) | Arenayı yukarıdan görürsün; her oyuncunun **etrafında renkli halka, altında adı** yazar | Kimin nerede olduğunu görmek, güvenlik takibi — **varsayılan görünüm** |
| **Kamera: POV** (`1`) | Seçili oyuncunun **kendi gözünden** izlersin | "Bu oyuncu ne görüyor / neden takıldı?" |
| **Kamera: Serbest** (`2`) | Arenada özgürce dolaşırsın: **W A S D** yürü, **Q/E** in-çık, **sağ fare tuşunu basılı tutup** bakış çevir, **Shift** hızlı | Bir köşeye yakından bakmak |
| **İstatistik** | Skorun ortasındaki kutuya bas (veya `I`) | Oyuncu başına öldürme/ölüm/can/batarya **ve PING** tablosu |
| **Tercihler** | Sol üstteki düğme (veya `P`) | Mod/harita seçimi + başlat/iptal, görünüm ayarları, bağlantı |
| **Tam ekran / Pencereli** | `F11`, ya da Tercihler panelinin başlığında **KAPAT**'ın yanındaki düğme | Yönetim penceresini tüm ekrana yay veya küçült. Seçimin bu bilgisayarda **hatırlanır**: uygulama bir dahaki açılışta aynı kiple gelir |
| **Oyundan çık** | Tercihler → **BAĞLANTI** satırının sağındaki **OYUNDAN ÇIK** | Yönetim uygulamasını kapatır. Güvenlik için **iki kez** basmak gerekir (ilk basışta düğme "EMİN? ÇIK" olur). Sunucuyu ve maçı **kapatmaz** — o ayrı bir penceredir |

**Oyuncu seçmek:** yandaki listede bir oyuncuya tıkla — seçili oyuncunun çerçevesi turuncu olur,
zemindeki halkası büyür. `Tab` ile sıradakine geçersin. Satırdaki küçük düğmeler: **POV** (o oyuncunun
gözünden izle), **MAVİYE/KIRMIZIYA** (takımını değiştir), **KİMLİK** (o gözlüğün ekranında büyük
işaret göster), **CAN** (ölü kalan oyuncuyu ayağa kaldır — aşağıdaki bölüm), **AT** (bağlantıdan çıkar — güvenlik için **iki kez** basmak gerekir, ilk basışta
düğme "EMİN?" olur). ⚠️ **AT o gözlükteki oyunu kapatır** ve **satırı listeden siler**: oyuncu
birkaç saniye içinde Quest'in kendi menüsünde bulur kendini, geri dönmesi için oyunun elle yeniden
açılması gerekir. Yani "AT" molaya çıkarmak için değil, o cihazı oturumdan çıkarmak içindir.
Listede kalmış **çevrimdışı** bir satırı temizlemek için de AT kullanılır. Atmak yasaklamak
değildir: aynı gözlük oyunu tekrar açarsa adıyla ve numarasıyla geri gelir.

**Panel açıkken oyun durmaz:** Tercihler/İstatistik panelleri yarı saydamdır, arkada sahneyi
görmeye devam edersin. `Esc` ile kapatırsın.

**PING kolonu ne demek:** o gözlükle sunucu arasındaki gecikme (milisaniye). **Düşük iyidir.**
Sorun yaşandığında ilk bakılacak yer burasıdır ve sana üç şeyi ayırt ettirir:

- **Herkesin ping'i yüksek** → ağ sorunu (birazdan sorun giderme bölümündeki adımlar).
- **Tek bir oyuncunun ping'i yüksek** → o gözlüğün Wi-Fi kapsaması zayıf; oyuncuyu alanın ortasına
  doğru yönlendir.
- **`-` yazıyor** → o gözlük ölçüm göndermiyor (büyük ihtimalle eski sürüm APK) ya da daha yeni
  bağlandı. Birkaç saniye bekle; geçmezse teknik ekibe söyle.

> **Normal değer aralığı işletmeye özeldir ve kurulumda ölçülüp bilgi kartına yazılır.** Kendi
> arenanın "normal"ini ilk sakin seansta not et — sonraki günlerde kıyaslayacağın sayı odur.

### Üç oyun modu — hangisini seçmeli?

| | **Takım Ölüm Maçı** | **Herkes Tek** | **Turnuva** |
|---|---|---|---|
| Takım | Kırmızı ve mavi (sistem otomatik dengeler) | **Yok** — herkes herkesi vurabilir | Kırmızı ve mavi |
| Skor | Takım puanı | **Kişi başına puan**; her öldürme öldürene +1 | **Kazanılan tur sayısı** — öldürme puan yazmaz |
| Kazanan | Puan limitine ilk ulaşan takım; süre biterse önde olan | Puan limitine ilk ulaşan **oyuncu**; süre biterse en yüksek puanlı. Tepede eşitlik varsa berabere | **4 tur** kazanan takım (en fazla 7 tur oynanır) |
| Silah | Arenaya yerleştirilmiş silahlardan seçilir: oyuncu silaha ~2 metreye kadar yaklaşıp nişan alır, yan tuşa (grip) basınca silahın bir kopyası eline gelir. Silah yerinden kaybolmaz, sınırsız kez alınabilir. ⚠️ Silahların arenaya konması **haritayı yapan kişinin işidir** — konmamış bir arenada oyuncunun eline silah gelmez | Oyuncu kumandanın **yan tuşunu (grip) basılı tutunca** eline rastgele bir silah gelir; bıraktığında silah kaybolur, tekrar bastığında **başka** bir silah gelir | Takım Ölüm Maçı ile aynı (arenadaki silahlardan seçilir) |
| Şarjör | Boşalınca kendiliğinden dolar | **Dolmaz** — oyuncu silahı bırakıp yenisini çeker | Her **tur başında** herkes tam dolu başlar |
| Ölünce | 5 saniye bekle, sonra **kendi renkli tabanına yürü** | Tabana gitmek yok: **5 saniye boyunca olduğun yerde kıpırdamadan dur** (1 metreden fazla yürürsen sayaç başa döner) | **Canlanma yok** — tur bitene kadar beklersin, yeni tur herkesi tam canla ayağa kaldırır |
| Varsayılan süre / puan | 300 sn (5 dk) / 30 | 300 sn (5 dk) / 20 | **Tur başına** 2 dk / 4 tur |

**Herkes Tek'te oyunculara söylenecek iki cümle:**
1. *"Silah almak için kumandanın yan tuşunu basılı tut — bıraktığında silah kaybolur."*
2. *"Öldüğünde bir yere yürüme; olduğun yerde 5 saniye kıpırdamadan dur, kendiliğinden canlanacaksın.
   Ekranın kapkara ve 'engelden çık' yazıyorsa bir cismin içindesin — önce dışarı çık, sayaç ondan
   sonra başlar."*

> Herkes Tek'te arenadaki silahlar ve renkli taban şeritleri **kendiliğinden gizlenir** (şeritler
> siz modu seçer seçmez, maçı beklemeden) —
> ayrıca bir şey yapman gerekmez. Aynı arenalar üç modda da oynanır; harita listesi değişmez.

#### Turnuva modu — turlar hâlinde eleme

Turnuva bir maçı **turlara** böler. Tur içinde canlanma yoktur: ölen oyuncu turun sonuna kadar
izler. Bir takımın sahadaki **herkesi** ölünce tur biter ve diğer takım **+1 tur** alır. Tur süresi
(varsayılan 2 dk) dolarsa **ayakta kalan sayısı fazla olan** takım turu alır; sayı eşitse o tur
kimseye puan yazmaz. **4 turu kazanan maçı kazanır** (bu yüzden en fazla 7 tur oynanır).

**Turlar arası TOPLANMA — operatörün asıl işi burada:**

- Tur biter bitmez maç duraklar ve herkes **kendi renkli tabanına yürür**. Ekranda kaç kişinin
  toplandığı yazar (ör. "TOPLANMA 4/6").
- **Herkes tabanına girdiğinde** geri sayım başlar (varsayılan 5 saniye) ve yeni tur açılır —
  herkes **tam can, tam şarjörle** ayağa kalkar.
- ⚠️ **Geri sayım sırasında biri tabanından çıkarsa sayım iptal edilir** ve toplanmaya dönülür.
  Kural "tabana uğra" değil, "tabanda **bekle**"dir. Oyunculara bunu bir kez söyle:
  *"Turu bekleyeceğin yer kendi renginin köşesidir, sayım bitene kadar oradan çıkma."*
- ⚠️ **Bekleme süresizdir: eksik oyuncuyla tur başlamaz.** Bir oyuncu takılırsa (gözlüğü düştü,
  bağlantısı koptu, oyundan çıktı) sistem kendiliğinden devam etmez — çözüm sende:
  - o oyuncuyu satırındaki **AT** düğmesiyle çıkar → kalanlar zaten tabanındaysa tur **hemen**
    başlar, ya da
  - **İPTAL** ile maçı bitir.
  Bu bilinçlidir: turnuvada eksik oyuncuyla açılan bir tur, hakemin istemediği bir turdur.

> Süre ve tur sayısını Tercihler'den değiştirebilirsin: **Süre** turnuvada **turun** süresidir
> (maçın değil), **Skor limiti** ise maçı kazanmak için gereken tur sayısıdır. Geri sayım
> uzunluğu da ayarlanabilir (5–30 saniye).

**Maç başlatma sırası**

- [ ] Listede **tüm oyuncular çevrimiçi** görünüyor mu? (Eksik varsa o gözlüğü kontrol et.)
- [ ] Takımlar istediğin gibi mi? (Değilse satırdaki takım düğmesiyle ata.)
- [ ] **Tercihler**'i aç, **Mod** ve **Harita**'yı seç (satıra bas → liste açılır → tıkla).
- [ ] **BAŞLAT**'a bas.
- [ ] Tüm gözlükler arenayı yükler → **5 saniye geri sayım** → maç başlar.
- [ ] Oyuncular arenaya girdikten sonra **kalibrasyonu yaptır** (Bölüm 4).

> ⚠️ **BAŞLAT'a bastıktan sonra harita/mod satırları kilitlenir** ve maç bitene (ya da **İPTAL**'e
> basana) kadar sönük durur — yükleme ve geri sayım sırasında da. Sebebi: harita seçmek **tüm
> gözlüklere** sahne yükletir, kurulmakta olan maçın altından çekilirse oyuncular yarı yüklü kalır.
> Yanlış harita seçtiysen **İPTAL** → doğru haritayı seç → **BAŞLAT**.

**Herkesi lobiye almak:** harita listesini aç, ilk satırdaki **Lobi**'yi seç — tüm gözlükler lobi
sahnesine döner. Ayrı bir "Lobiye Dön" düğmesi yoktur. Maç koşarken bu satır da kilitlidir; koşan
maçı bitirmenin yolu **İPTAL**'dir (ikisi de aynı işi yapar).

> **Harita satırı "şu an açık olan sahneyi" gösterir.** Lobiye dönüldüğünde (senin seçmenle ya da
> maç bitince) satır **Lobi**'ye döner — arena adı orada kalmaz. Aynı arenayı tekrar açmak için
> listeden yeniden seç, herkes ona geçer.
>
> **Lobi açıkken BAŞLAT çalışmaz** ve durum satırına *"Lobi açık — önce bir arena seç"* yazar:
> lobide maç başlamaz. Önce arenayı seç (herkes yükler), sonra BAŞLAT.

**Maç başlamıyorsa** en sık iki sebep: (1) hiç bağlı oyuncu yok, (2) gözlüklerden birinde
farklı/eski sürüm var. Sunucu penceresinde sebep tek satır olarak yazar; teknik ekibe o satırı
ilet.

### Ölü kalan oyuncuyu ayağa kaldırmak — **CAN** düğmesi

Normalde oyuncu kendi başına canlanır: modun kuralına göre ya kendi renkli tabanına yürür ya
olduğu yerde bekler. Bunu yapamayan oyuncu (gözlüğü donmuş, tabanına yürüyemiyor, turnuvada tur
bitmesini bekliyor) **maçın sonuna kadar ölü kalır.** Onu ayağa kaldıran düğme satırdaki **CAN**'dır.

- [ ] **1.** Ölü satırdaki **CAN** düğmesine bas. **Onay istemez, tek basışta çalışır.**
- [ ] **2.** Oyuncu tam canla ayağa kalkar: ölüm ekranı kapanır, ateş edebilir. Bulunduğu yerden
      hiçbir yere taşınmaz — nerede duruyorsa orada canlanır.
- [ ] **3.** Öldüğü sayılmaya devam eder: skor ve ölüm sayacı değişmez, canlandırmak ölümü silmez.

**Herkesi birden kaldırmak:** yönetim ekranındaki toplu canlandırma düğmesi o an **ölü olan**
herkesi ayağa kaldırır; canlı oyunculara dokunmaz.

**Bastın ama oyuncu canlanmadıysa — düğme bozuk değildir, iki sebebi vardır:**

- **Oyuncu kalibresiz** (satırı kırmızı, düğmesinde **KAL !** yazıyor). Kalibresiz oyuncu zaten
  ateş edemez ve vurulamaz; onu "canlı" göstermek sahada hiçbir şeyi değiştirmezdi. Önce
  kalibrasyonu yaptır (Bölüm 4), sonra CAN'a bas.
- **Oyuncu bir engelin/duvarın içinde duruyor** (sütun, kasa, blok — halkası kırmızı yanıp
  sönüyorsa odur). Orada canlansa saniyeler içinde yeniden ölürdü. Oyuncuya **oradan çıkmasını
  söyle**, sonra CAN'a bas.

Maç koşmuyorken (lobide, maç bitmişken, duraklatılmışken) düğmenin işi yoktur; maç başladığında
zaten herkes canlı kalkar.

> ⚠️ **Turnuvada dikkat: CAN turun sonucunu değiştirebilir.** Tur, bir takımın **herkesi** ölünce
> biter — son ölü oyuncuyu kaldırırsan tur bitmez ve oyun devam eder. Turnuvada bu düğmeyi bilinçli
> kullan: takılan bir oyuncuyu kurtarmak içindir, oyuncunun eleme sonucunu geri almak için değil.

### Kural dışı duran oyuncuyu görmek — halka renkleri ve ihlal listesi

Oyuncunun fiziksel olarak olmaması gereken iki yer vardır: **bir engelin içi** (sütun, kasa, blok)
ve **arenanın dışı**. İkisini de sen görürsün, oyuncu da kendi ekranından anlar.

| Ne görüyorsun | Ne demek | Ne yapman gerekiyor |
|---|---|---|
| Halkası **kırmızı**, hızlı yanıp sönüyor, adının yanında **DUVAR** | Kafası bir engelin **içinde**. Ekranı kapkaranlık; 3 saniye sonra canı erimeye başlar ve 8. saniyede ölür | Hemen sesle uyar: **"Duvardan çık."** Ölmesi normaldir, sistem bunun için var |
| Halkası **turuncu**, daha yavaş yanıp sönüyor, adının yanında **ALAN DIŞI** | Oyun alanının **dışına** çıkmış. Ekranı kararır ve **ateş edemez**; canı gitmez | Sesle içeri çağır. Tekrar tekrar oluyorsa kalibrasyonu kaymış olabilir (Bölüm 4.1) |
| Halkası normal renkte | Kural dışı bir durum yok | — |

- **İkisi aynı anda olursa halka kırmızı kalır** — canı giden durum daha acildir.
- **Halkalar yalnız kuş bakışında (`3`) çizilir.** POV veya serbest kipteyken oyuncu listesindeki
  **satır kenarlığı** aynı şekilde yanıp söner, yani ihlali orada da görürsün.
- Sağ alttaki **ihlal listesi** kim, ne zaman, ne kadar süre kural dışı kaldığını yazar
  (ölüm listesinden ayrıdır — o maçın hikâyesi, bu senin iş listen). **Yarım saniyeden kısa
  temaslar yazılmaz**: sınır çizgisinde gidip gelen bir oyuncu listeyi doldurup okunmaz hâle
  getirirdi. Halka yine de yanar.
- **İhlal başlayınca kısa bir uyarı sesi çalar** — ekrana bakmıyorken de haberin olsun diye.
  Sesi kapatmak istersen **Tercihler** (`P`) → **GÖRÜNÜM** bölümünde **İhlal sesi** satırı vardır.
  Bu satır **yalnız senin ekranına aittir**: sen kapatınca diğer operatörün sesi susmaz, o da
  kendi ekranından kapatır.
- **Ses kalabalıkta sirene dönmez:** aynı anda kaç kişi kural dışına çıkarsa çıksın en fazla
  birkaç saniyede bir çalar. Yani duyduğun her ses "en az bir kişi" demektir, "tam bir kişi"
  demek değildir — kimin olduğunu ihlal listesinden ve halkalardan görürsün. Sesin bitişi ayrıca
  duyurulmaz; ihlalin bittiğini listeden okursun.
- Maç sonunda istatistik panelinin **İHLAL** kolonunda oyuncu başına **kaç kez ve toplam kaç saniye**
  kural dışı kaldığı durur (hiç ihlal etmemiş oyuncuda `-`). Oyuncuyla konuşurken elindeki somut
  veri budur; lobiye dönünce skorla birlikte sıfırlanır.
- ⚠️ **Ceza vermek senin kararın.** Sistem alan dışına çıkanı öldürmez — kalibrasyonu birkaç santim
  kaymış bir gözlük yüzünden oyuncu durduk yere ölmesin diye böyle. Israrla tekrarlayan oyuncuyu
  uyarırsın, gerekirse **AT**'arsın.
- Hiç kimsenin halkası yanmıyorsa ve arenada engel olduğunu biliyorsan bir sorun yoktur — halka
  yalnız gerçekten ihlal varken yanar.

---

## 6. Maç sırasında ne oluyor?

- Oyuncular arenada serbest yürür; **kimse ışınlanmaz**, ekranda "hareket" tuşu yoktur.
- Vurulan oyuncunun canı azalır; canı bitince **ölüm ekranı** görür ve ateş edemez.
- **Canlanma fiziksel bir iştir** ve moda göre değişir:
  - **Takım Ölüm Maçı:** ölen oyuncu **5 saniye** bekler, sonra **kendi takımının renkli taban
    bölgesine yürüyerek girer** → orada canlanır.
    Oyuncuya söylenecek cümle: **"Kendi renginin olduğu köşeye yürü, orada canlanacaksın."**
  - **Herkes Tek:** taban yok — oyuncu **öldüğü yerde 5 saniye kıpırdamadan durur** → canlanır.
    Bir engelin (sütun, kasa, blok) içinde öldüyse sayaç **hiç işlemez**: önce dışarı çıkması,
    5 saniyeyi ondan sonra beklemesi gerekir. Ekranında "Engelden çık ve canlan" yazar.
    Bir metreden fazla yürürse sayaç sıfırlanır. Ekranında kalan saniye yazar.
    Oyuncuya söylenecek cümle: **"Öldüğünde yürüme, olduğun yerde bekle."**
  - **Turnuva:** canlanma **yoktur** — ölen oyuncu turun bitmesini bekler, yeni tur onu tam canla
    ayağa kaldırır. Oyuncuya söylenecek cümle: **"Elendin, takımın turu bitirene kadar izle."**
  - ⚠️ Şartı yerine getirmeyen oyuncu **canlanmaz ve beklemekle de canlanmaz** — canlanmak
    oyuncunun kendi işidir.
- Oyuncu arena sınırına yaklaşırsa ekranı hafifçe kararmaya başlar; dışarı çıkarsa tümden kararır,
  uyarı çıkar ve **ateş edemez** → geri içeri girmesi yeterli, silahı anında geri çalışır. Dışarıda
  kalmak **can götürmez**; onu senin görmen için ekranında işaretlenir (aşağıdaki bölüm).
- Maç, süre dolunca veya skor limitine ulaşılınca biter; kazanan duyurulur ve **kazanan ekranı
  sen bir şey seçene kadar ekranda kalır.** Kendiliğinden lobiye dönülmez: sıradaki haritayı seç
  (herkes oraya geçer), harita listesinden **Lobi**'yi seç ya da **İPTAL**'e bas. Böylece maç
  sonunu konuşmak, ödül vermek ya da sıradaki turu anlatmak için istediğin kadar vaktin olur.

**Maçı geçici olarak durdurmak (DURAKLAT)**

Sahada bir şey olduğunda — biri gözlüğünü düzeltiyor, bir oyuncu düştü, seyirciyle konuşulacak —
**Tercihler → DURAKLAT**:

- Süre durur, kimse kimseye hasar veremez, **skorlar olduğu gibi kalır.**
- Oyuncular arenada kalır, hiçbir yere ışınlanmaz; maç bittiği sanılmaz.
- Aynı düğme **DEVAM ET**'e döner; bastığında maç **kaldığı yerden** sürer.
- Maçı gerçekten bitirmek istiyorsan duraklatma değil **İPTAL** kullan.
- ⚠️ **Duraklatılmış maçta harita/mod değiştirilemez** — donmuş da olsa maç kuruludur. Önce
  **İPTAL**, sonra yeni harita.

> Düğme yalnız **maç koşarken** ve **senin duraklattığın maçta** çalışır. Geri sayım sırasında ya
> da lobide sönük durur — orada duraklatılacak bir şey yoktur. Birden çok yönetim ekranı varsa
> hepsi aynı düğmeyi görür: biri duraklatınca diğerinin ekranında da **DEVAM ET** yazar ve kimin
> yaptığı durum satırına düşer.

---

## 7. Maçlar arası ve gün sonu

**Sıradaki maç**

- [ ] Maç bitti ve kazanan ekranı duruyorsa: sıradaki haritayı seç (ya da harita listesinden
      **Lobi**) — gözlükler ancak o zaman oradan çıkar.
- [ ] Gerekiyorsa oyuncu değişimi yap (yeni oyuncu gözlüğü açar, kendiliğinden bağlanır).
- [ ] Mod/harita seç → **Maçı Başlat**. (Kalibrasyon maçlar arasında korunur; yalnız oyuncular
      birbirini yanlış yerde görüyorsa o oyuncuya tekrar yaptır — §4.1.)

**Gün sonu kapatma sırası** — açılışın tam tersi:

- [ ] **1.** Gözlüklerdeki uygulamayı kapat, gözlükleri şarja tak.
- [ ] **2.** Yönetim oyununu kapat (Tercihler → **OYUNDAN ÇIK**, pencereyi kapat veya launcher'da **Durdur**).
- [ ] **3.** Launcher'ı kapat. *(Launcher'ı kapatmak sunucuyu kapatmaz — o ayrı bir penceredir.)*
- [ ] **4.** **En son** sunucu penceresini kapat (Ctrl + C veya pencereyi kapat).

---

## 8. Sorun giderme — basit dille

| Ne görüyorsun | Muhtemel sebep | Ne yapacaksın |
|---|---|---|
| Gözlükte "Sunucu bulunamadı" | Sunucu kapalı ya da gözlük sunucuyu bulamıyor | Önce sunucu penceresi açık mı bak. Açıksa: **sağ kumandada joystick'e 1 sn basılı tut** → adresi elle gir (Bölüm 3.2) |
| Gözlük yanlış Wi-Fi'de | Gözlük ev/misafir ağına bağlanmış | Gözlüğün Wi-Fi ayarından arenaya özel ağı seç |
| Yönetim ekranı turuncu hata kartı gösteriyor | Sunucuya ulaşamıyor | Sunucu penceresi açık mı? Launcher'daki **Sunucu IP** doğru mu? Sonra **Yeniden Bağlan** |
| Yönetim ekranında arena görünüyor ama oyuncu yok | Henüz kimse bağlanmadı ya da gözlükler kalibre değil | Oyuncu listesi boşsa gözlükleri kontrol et (Bölüm 3). Liste doluysa ama halkalar yoksa **kalibrasyon** yaptır (Bölüm 4) |
| POV kipinde "poz yok" yazıyor | O gözlükten konum bilgisi gelmiyor (kalibre değil ya da ağ koptu) | O oyuncuya kalibrasyonu tekrar yaptır; sağ üstteki nokta kırmızıysa bağlantı sorunu var |
| Fareyle bakış çevirmiyorum | Serbest kipte bakış **sağ tuş basılıyken** çalışır (imleç serbest kalsın diye) | `2` ile serbest kipe geç, sağ tuşu basılı tutarak fareyi oynat |
| Yönetim ekranı "Sunucu adresi yok" diyor | Oyun launcher'sız, doğrudan açılmış | Oyunu kapat, **Launcher**'dan **Yönetimi Başlat** ile aç |
| Launcher "Admin exe bulunamadı" diyor | Oyun dosyası taşınmış/silinmiş | Launcher > **3 · Yönetim oyunu > Gözat** ile `deploy\admin\VortexArena.exe` dosyasını yeniden seç. Dosya yoksa teknik ekibi ara |
| Launcher "Mekan seçilmedi" diyor, sunucu açılmıyor | İşletme listede seçili değil | **1 · Sunucu** bölümündeki listeden işletmenin adına tıkla. Liste boşsa **Yenile**'ye bas; yine boşsa teknik ekibi ara |
| Yönetim ekranında **başka bir işletmenin haritaları** çıkıyor | Sunucu yanlış mekanla açılmış | Sunucu penceresini kapat (Ctrl + C), Launcher'da doğru işletmeyi seçip **Sunucuyu Başlat**. Mekan sunucu çalışırken değişmez |
| Oyuncu listede "çevrimdışı" düşüyor | Wi-Fi zayıf ya da gözlük uykuya geçmiş | Gözlüğü uyandır; kapsama sorunu tekrarlıyorsa teknik ekibi ara |
| **Birden bire HERKES takılmaya başladı** (tek oyuncu değil, hepsi) | Wi-Fi'ı oyun dışı bir şey doldurdu | **Önce İstatistikler panelini aç ve PING kolonuna bak** (aşağıda). Herkesinki yüksekse sırayla: 1) bir gözlükte **ekran yayını (cast/kayıt) açık mı** — en sık sebep budur, kapat. 2) Arena Wi-Fi'ına telefon/dizüstü bağlanmış mı, indirme mi var — çıkar. 3) Sunucu bilgisayarının **ağ kablosu takılı mı** — çıkmışsa tak. Düzelmezse teknik ekibi ara |
| **Tek bir oyuncu** takılıyor, diğerleri normal | O gözlüğün Wi-Fi kapsaması zayıf | İstatistiklerde o satırın PING'i diğerlerinden belirgin yüksekse oyuncuyu alanın ortasına doğru yönlendir; sürekli tekrarlıyorsa teknik ekibi ara |
| Oyuncular birbirini yanlış yerde görüyor | Kalibrasyon yapılmadı ya da A–B ters alındı | Arenada **yeniden kalibrasyon** yaptır (Bölüm 4) |
| Oyuncular birbirini **havada / yere gömülü** görüyor | Kalibrasyonda kumandanın **ucu yere değmemiş** (havada yakalanmış) | O oyuncuya kalibrasyonu tekrarlat; kumandayı nasıl tuttuğu önemli değil, **ucu yere değecek** (Bölüm 4). Herkeste aynı sorun varsa teknik ekibi ara |
| Bir oyuncunun karakteri **olduğundan kısa/uzun** görünüyor | Boyu hiç ölçülmemiş ya da ölçüm eğilmişken alınmış | Oyuncuyu dik durdurup satırındaki **ÖLÇ** düğmesine bas (§4.2) |
| **ÖLÇ**'e bastın, düğmede **ÖLÇÜLEMEDİ** yazdı | Duyuru satırında sebebi yazar: oyuncu hareketli/eğilmişti **ya da** o gözlük gövde takibi üretemiyor | Önce oyuncuyu dik durdurup tekrar bas. Sebep "gövde pozu yok" ise §4.4'teki temizliği yap |
| Bir oyuncu diğer ekranlarda **kolları yana açık, donuk** duruyor (T-poz) ama konumu doğru | O gözlükte gövde takibi arızalı — oyuncu görünmez kalmasın diye sistem onu bu şekilde çiziyor | §4.4'teki alan verisi temizliğini yap, sonra yeniden kalibre + **ÖLÇ**. Geçmezse teknik ekibi ara. Oyuncunun kendi ekranında belirti olmaz |
| Kalibre etiketi turuncu **KAL ?** oldu | Gözlüğün zemin tahmini ile gerçek zemin arasında büyük fark var — o gözlüğün alan verisi bozulmuş | Maça devam edebilirsin (kalibrasyon geçerli). Seans arasında §4.4'teki temizliği yap |
| Oyuncular uygulamayı her açtığında yeniden kalibre olmak zorunda kalıyor | Kalibre modu **2 ÇAPA** (varsayılan) | Beklenen davranış. Tek katlı, sorunsuz bir kurulumda hızlandırmak istersen TERCİHLER > KALİBRASYON > **ESKİ KALİBRE** (§4.3) |
| Kalibre modunu değiştirdin ama hiçbir şey değişmedi | Gözlükler ayarı yalnız **bağlanırken** okur | O gözlüklerde uygulamayı kapatıp yeniden aç; modu bundan sonra seans başında seç (§4.3) |
| Oyun ortasında arena birden kaydı | Gözlüğün konum takibi sıfırlandı | Genelde kendiliğinden düzelir. Düzelmezse o oyuncuya kalibrasyonu tekrarlat |
| Ateş ediyor ama can azalmıyor | Aynı takımdalar (dost ateşi kapalı) ya da maç henüz başlamadı | Takımları kontrol et; geri sayım bitmiş mi bak. Takım arkadaşlarının birbirini vurabilmesini istiyorsan Tercihler → MAÇ → **Dost ateşi**'ni aç |
| Turnuvada ekranda **"TOPLANMA 4/6"** yazıyor, yeni tur bir türlü başlamıyor | Bir ya da iki oyuncu kendi tabanına dönmedi (takıldı, koptu, oyundan çıktı) | Ekranda kimin eksik olduğunu bul: listedeki çevrimdışı satırı ya da tabanına yürümeyen oyuncuyu **AT** ile çıkar → kalanlar hazırsa tur hemen başlar. Vazgeçtiysen **İPTAL**. Tur eksik oyuncuyla kendiliğinden başlamaz |
| Turnuvada geri sayım başlıyor ama hep iptal oluyor | Biri sayım bitmeden tabanından çıkıyor | Oyunculara "sayım bitene kadar kendi renginin köşesinden çıkmayın" de |
| Ölen oyuncu canlanmıyor | Kendi takımının tabanına girmemiş | Oyuncuya **kendi renginin köşesine yürümesini** söyle — **kendiliğinden canlanmaz**, mutlaka tabana girmeli. Şartı yerine getiremiyorsa (gözlüğü donmuş, yürüyemiyor) satırındaki **CAN** düğmesiyle sen kaldır |
| **CAN** düğmesine bastın, oyuncu yine ölü | Oyuncu kalibresiz ya da bir engelin/duvarın içinde duruyor | Satırı kırmızıysa önce kalibre olsun (Bölüm 4); halkası kırmızı yanıp sönüyorsa oyuncuya **engelin içinden çıkmasını** söyle, sonra CAN'a tekrar bas. İkisi de değilse sunucu penceresindeki son satırı teknik ekibe ilet |
| Maç başlamıyor | Bağlı oyuncu yok ya da bir gözlükte eski sürüm var | Listede oyuncu var mı bak; varsa sunucu penceresindeki son satırı teknik ekibe ilet |
| Ses gelmiyor | Gözlüğün sesi kısık | Gözlüğün ses seviyesini aç |
| Oyuncunun ekranı karardı, uyarı çıktı | Oyun alanının dışına çıkmış | Oyuncuya geri içeri girmesini söyle |
| Bir oyuncu yanlış yerde görünüyor / "nişan aldığım yere gitmiyor" diyor | O gözlüğün kalibrasyonu kaymış | Satırındaki **KAL** düğmesiyle sıfırla, yeniden kalibre ettir (§4.1) |
| Bir avatar yanıp sönüyor | O oyuncu kalibresiz — ateş edemez, vurulamaz | Yeniden kalibre olmasını söyle (§4.1); bitince kendiliğinden düzelir |
| Oyuncu "silahım çalışmıyor" diyor, ekranında kalibrasyon yazısı var | Kalibrasyonu sıfırlanmış | Bölüm 4'teki adımlarla yeniden kalibre olsun (§4.1/4) |
| Oyuncu öldü ama canlanmıyor | Kalibresiz oyuncu canlanmaz — **CAN** düğmesi de onu kaldırmaz | Önce kalibre olsun; ardından canlanma şartını yerine getirsin (tabanına girmek / olduğu yerde beklemek) ya da **CAN**'a bas |

---

## 9. Asla yapılmaması gerekenler

- ❌ **Sunucu penceresini maç sırasında kapatma** — maç anında durur.
- ❌ **Oyunun exe dosyasına doğrudan çift tıklama** — her zaman launcher'dan başlat.
- ❌ **Zemindeki A / B bantlarını kaldırma veya kaydırma** — tüm kalibrasyon onlara bağlıdır.
- ❌ **Yönetim panelini oyunculara bırakma** — maçı iptal edebilir, oyuncu atabilirler.
- ❌ Sunucu klasöründeki ayar dosyalarını kurcalama (`config` klasörü) — teknik ekibin işi.
- ❌ **Gözlükten ekran yayını (cast / kayıt) açma** — tek bir yayın tüm oyuncuların Wi-Fi'ını
  doldurur ve herkesin görüntüsü takılır. Tanıtım görüntüsü gerekiyorsa **yönetim ekranının
  gözlemci kamerası** kullanılır.
- ❌ **Arena Wi-Fi'ına oyun dışı cihaz bağlama** (telefon, dizüstü, misafir) — aynı sebep.

---

## 10. Elinin altında bulunması gerekenler

Kurulumda bırakılan **bilgi kartında** şunlar yazmalı; yoksa teknik ekipten iste:

- [ ] Wi-Fi ağ adı (SSID) ve şifresi
- [ ] **Sunucu bilgisayarının adresi** (ör. `192.168.1.10`) — gizli IP paneline girilecek numara
- [ ] Zemindeki **A–B işaretleri arası mesafe** (bantlar kayarsa yeniden yerleştirmek için)
- [ ] Kullanılacak arena/harita adı
- [ ] Gözlük etiketleri ↔ ekrandaki kimlikler listesi (`7 · ertu` = hangi fiziksel gözlük)
- [ ] Teknik destek telefonu

---

### Kumanda hatırlatma kartı (yazdırılabilir)

```
┌──────────────────────────────────────────────────────────┐
│  SAĞ KUMANDA — OPERATÖR KISAYOLLARI                      │
├──────────────────────────────────────────────────────────┤
│  JOYSTICK'e bas, 1 sn tut →  Gizli IP paneli (aç/kapat)  │
│                              (lobide, bağlanamayınca)    │
│                                                          │
│  A BASILIYKEN B'ye 2 KEZ  →  Kalibrasyon noktası al      │
│  (kumandanın UCU yere         kısa titr. = A alındı      │
│   değecek; nasıl tuttuğun     UZUN titr. = B alındı, tamam│
│   önemli değil)              3 titreşim = mesafe yanlış, │
│                                           B'yi tekrar al │
│                              1 titreşim = uç yere        │
│                                değmemiş, B'yi tekrar al  │
├──────────────────────────────────────────────────────────┤
│  OYUNCUYA — "HERKES TEK" MODUNDA                         │
│                                                          │
│  Yan tuş (grip) basılı    →  Elinde rastgele silah       │
│                              belirir; BIRAKINCA kaybolur │
│                              tekrar bas = başka silah    │
│  Şarjör bitti             →  Bırak, yenisini çek         │
│                              (şarjör dolmaz)             │
│  Öldün                    →  YÜRÜME. Olduğun yerde       │
│                              5 saniye kıpırdamadan dur   │
├──────────────────────────────────────────────────────────┤
│  OYUNCUYA — "TURNUVA" MODUNDA                            │
│                                                          │
│  Öldün                    →  Tur bitene kadar izle,      │
│                              canlanma yok                │
│  Tur bitti                →  KENDİ RENGİNİN köşesine     │
│                              yürü ve ORADA BEKLE         │
│                              (çıkarsan sayım iptal)      │
└──────────────────────────────────────────────────────────┘
```
