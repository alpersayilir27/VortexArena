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
- [ ] **7.** Maç bitince oyuncular otomatik lobiye döner; yeni maç için 5. adıma dön.

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
Modlar     : tdm, ffa
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

### 3.2 Bağlanmazsa — gizli IP paneli: **sağ kumandada A tuşuna 2 kez**

Bazı ağlarda gözlük sunucuyu kendiliğinden bulamaz. Bu durumda adres **elle** girilir.
Panel normalde **gizlidir** (oyuncular karıştırmasın diye) ve şu kombinasyonla açılır:

> ### 🎮 Sağ kumandadaki **A** tuşuna **hızlıca İKİ KEZ** bas (2×A)
>
> - Sağ kumandanın **üstteki** düğmesi = **A**.
> - İki basış arası **yarım saniyeden kısa** olmalı — kapı zilini iki kez çalar gibi hızlı.
> - Doğru yaptıysan kumanda **kısa bir titreşim** verir ve panel açılır.
> - Aynı kombinasyon paneli **kapatır** da.

Panel açılmıyorsa: basışlar arası çok yavaştır → daha hızlı dene. Yanlış kumandaya
basıyor olabilirsin → **sağ** kumanda, üstteki düğme.

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
  *"Sunucu bulunamadı. Adresi elle girmek için sağ kumandada A'ya İKİ KEZ bas."*
  yazısı çıkar — yani kombinasyonu ezberlemek zorunda değilsin, ekran hatırlatır.
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
birbirini görür, silah rafından silah alıp hedef tahtalarına ateş edebilir (birbirlerine hasar
veremezler) ve zemindeki A/B işaretleri oradadır. Bir kez kalibre olan oyuncu maça hazır girer —
harita değişimi kalibrasyonu bozmaz. (Maç sırasında da aynı adımlarla yeniden kalibre edilebilir.)

- [ ] **1.** Oyuncu sağ kumandayı zemindeki **A** bandının üzerine, **kalem gibi dimdik** tutup
      ucunu yere değdirir. ⚠️ **Duruş önemli:** gözlük zemin yüksekliğini de bu ölçümden
      öğrenir; eğik tutulursa oyuncular birbirini havada ya da yere gömülü görür.
- [ ] **2.** Sağ kumandada **A + B tuşlarına birlikte 3 saniye basılı tutar.**
      Titreşim giderek artar → **tek titreşim** = A noktası alındı.
- [ ] **3.** Aynısını zemindeki **B** bandında, **aynı dik duruşla** yapar → **çift titreşim** =
      tamam, arena hizalandı.
- [ ] **4.** Hata sinyalleri — ikisinde de B'yi tekrar al:
      **üç kısa titreşim** = iki nokta arasındaki mesafe yanlış (bant ölçüsünü kontrol et) ·
      **bir uzun titreşim** = kumanda dik tutulmamış, zemin ölçümü tutmadı.

**Bilmen gerekenler**

- Kalibrasyon gözlükte **saklanır**; aynı gözlük ertesi gün açıldığında genelde kendiliğinden
  geri gelir.
- ⚠️ **Kalibrasyon bir kez alındıktan sonra oyuncu A+B ile onu değiştiremez.** Kumandaya bassa da
  bir uzun titreşim alır ve hiçbir şey olmaz. Bu bilerek böyledir: oyuncunun maç ortasında
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
      Kendi ekranında "Kalibrasyon gerekli — sağ kumandada A+B" yazar.
- [ ] **4.** Oyuncu §4'teki adımlarla **yeniden kalibre olur** (artık A+B açıktır).
      Bitince tik kendiliğinden yeşile döner.
- [ ] **5.** Oyuncu **kaldığı yerden devam eder** — canı, öldürme sayısı ve skoru sıfırlanmaz.

**Maç öncesi hepsini birden aldırmak için:** TERCİHLER > KALİBRASYON >
**"TÜM KALİBRASYONLARI SIFIRLA"** (iki kez basılır). Herkes aynı anda kalibresiz olur ve
sırayla A+B yapar.

> **Neden tik'i elle geri açamıyorsun:** hizalamanın gerçekten düzeldiğini yalnız gözlüğün
> kendisi bilir. Sen "tamam" diyebilseydin, aslında hâlâ kaymış bir oyuncu ateş etmeye ve
> vurulmaya devam ederdi — yani düzeltmeye çalıştığın sorunun ta kendisi sürerdi.

---

## 5. Maçı başlatmak ve yönetmek (Yönetim ekranı)

Yönetim ekranındaki dashboard'da elindeki kontroller:

| Kontrol | Ne yapar | Ne zaman kullanılır |
|---|---|---|
| **Oyuncu listesi (roster)** | Bağlı oyuncular, takımları, çevrimiçi/çevrimdışı durumu | Maç öncesi herkesin bağlı olduğunu doğrulamak için |
| **Kırmızı / Mavi** | Seçili oyuncunun takımını değiştirir | Takımları elle dengelemek için (boş bırakırsan sistem otomatik dengeler) |
| **Bu cihazı tanıt** | O gözlüğün ekranında büyük bir işaret gösterir | "Bu listedeki isim hangi gözlük?" sorusuna cevap |
| **Çıkar (kick)** | Oyuncuyu bağlantıdan atar | Yanlışlıkla bağlanan/oyunda olmaması gereken cihaz |
| **Mod seçimi** | Oyun türü: **Takım Ölüm Maçı** (kırmızı-mavi) veya **Herkes Tek** (takım yok, herkes herkese karşı) | Her maç öncesi — aşağıdaki "İki oyun modu" kutusuna bak |
| **Harita seçimi** | Hangi arenada oynanacağı | Her maç öncesi — sadece seçili modla uyumlu haritalar listelenir |
| **Maçı Başlat** | Herkesi arenaya alır, geri sayımı başlatır | Herkes bağlı ve hazır olduğunda |
| **Maçı İptal / Lobiye Dön** | Maçı erken bitirir, herkesi lobiye döndürür | Acil durum, oyuncu değişimi, yanlış harita |
| **Skor ve öldürme akışı** | Ortada canlı skor + faz/süre, sağ altta "kim kimi vurdu" listesi | Maç sırasında takip |
| **Kamera: Kuş bakışı** (`3`) | Arenayı yukarıdan görürsün; her oyuncunun **etrafında renkli halka, altında adı** yazar | Kimin nerede olduğunu görmek, güvenlik takibi — **varsayılan görünüm** |
| **Kamera: POV** (`1`) | Seçili oyuncunun **kendi gözünden** izlersin | "Bu oyuncu ne görüyor / neden takıldı?" |
| **Kamera: Serbest** (`2`) | Arenada özgürce dolaşırsın: **W A S D** yürü, **Q/E** in-çık, **sağ fare tuşunu basılı tutup** bakış çevir, **Shift** hızlı | Bir köşeye yakından bakmak |
| **İstatistik** | Skorun ortasındaki kutuya bas (veya `I`) | Oyuncu başına öldürme/ölüm/can/batarya **ve PING** tablosu |
| **Tercihler** | Sol üstteki düğme (veya `P`) | Mod/harita seçimi + başlat/iptal, görünüm ayarları, bağlantı |

**Oyuncu seçmek:** yandaki listede bir oyuncuya tıkla — seçili oyuncunun çerçevesi turuncu olur,
zemindeki halkası büyür. `Tab` ile sıradakine geçersin. Satırdaki küçük düğmeler: **POV** (o oyuncunun
gözünden izle), **MAVİYE/KIRMIZIYA** (takımını değiştir), **KİMLİK** (o gözlüğün ekranında büyük
işaret göster), **AT** (bağlantıdan çıkar — güvenlik için **iki kez** basmak gerekir, ilk basışta
düğme "EMİN?" olur).

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

### İki oyun modu — hangisini seçmeli?

| | **Takım Ölüm Maçı** | **Herkes Tek** |
|---|---|---|
| Takım | Kırmızı ve mavi (sistem otomatik dengeler) | **Yok** — herkes herkesi vurabilir |
| Skor | Takım puanı | **Kişi başına puan**; her öldürme öldürene +1 |
| Kazanan | Puan limitine ilk ulaşan takım; süre biterse önde olan | Puan limitine ilk ulaşan **oyuncu**; süre biterse en yüksek puanlı. Tepede eşitlik varsa berabere |
| Silah | Arenadaki **taban raflarından** alınır | **Raf yok.** Oyuncu kumandanın **yan tuşunu (grip) basılı tutunca** eline rastgele bir silah gelir; bıraktığında silah kaybolur, tekrar bastığında **başka** bir silah gelir |
| Şarjör | Boşalınca kendiliğinden dolar | **Dolmaz** — oyuncu silahı bırakıp yenisini çeker |
| Ölünce | 5 saniye bekle, sonra **kendi renkli tabanına yürü** | Tabana gitmek yok: **3 saniye boyunca olduğun yerde kıpırdamadan dur** (1 metreden fazla yürürsen sayaç başa döner) |
| Varsayılan süre / puan | 300 sn (5 dk) / 30 | 300 sn (5 dk) / 20 |

**Herkes Tek'te oyunculara söylenecek iki cümle:**
1. *"Silah almak için kumandanın yan tuşunu basılı tut — bıraktığında silah kaybolur."*
2. *"Öldüğünde bir yere yürüme; olduğun yerde 3 saniye kıpırdamadan dur, kendiliğinden canlanacaksın."*

> Herkes Tek'te arenadaki silah rafları ve renkli taban şeritleri **kendiliğinden gizlenir** —
> ayrıca bir şey yapman gerekmez. Aynı arenalar iki modda da oynanır; harita listesi değişmez.

**Maç başlatma sırası**

- [ ] Listede **tüm oyuncular çevrimiçi** görünüyor mu? (Eksik varsa o gözlüğü kontrol et.)
- [ ] Takımlar istediğin gibi mi? (Değilse satırdaki takım düğmesiyle ata.)
- [ ] **Tercihler**'i aç, **Mod** ve **Harita**'yı seç.
- [ ] **BAŞLAT**'a bas.
- [ ] Tüm gözlükler arenayı yükler → **5 saniye geri sayım** → maç başlar.
- [ ] Oyuncular arenaya girdikten sonra **kalibrasyonu yaptır** (Bölüm 4).

**Maç başlamıyorsa** en sık iki sebep: (1) hiç bağlı oyuncu yok, (2) gözlüklerden birinde
farklı/eski sürüm var. Sunucu penceresinde sebep tek satır olarak yazar; teknik ekibe o satırı
ilet.

---

## 6. Maç sırasında ne oluyor?

- Oyuncular arenada serbest yürür; **kimse ışınlanmaz**, ekranda "hareket" tuşu yoktur.
- Vurulan oyuncunun canı azalır; canı bitince **ölüm ekranı** görür ve ateş edemez.
- **Canlanma fiziksel bir iştir** ve moda göre değişir:
  - **Takım Ölüm Maçı:** ölen oyuncu **5 saniye** bekler, sonra **kendi takımının renkli taban
    bölgesine yürüyerek girer** → orada canlanır.
    Oyuncuya söylenecek cümle: **"Kendi renginin olduğu köşeye yürü, orada canlanacaksın."**
  - **Herkes Tek:** taban yok — oyuncu **öldüğü yerde 3 saniye kıpırdamadan durur** → canlanır.
    Bir metreden fazla yürürse sayaç sıfırlanır. Ekranında kalan saniye yazar.
    Oyuncuya söylenecek cümle: **"Öldüğünde yürüme, olduğun yerde bekle."**
  - Her iki modda da oyuncu şartı yerine getirmezse sistem bir süre sonra onu zaten canlandırır
    (maç kilitlenmesin diye).
- Oyuncu arena sınırına yaklaşırsa duvarlar belirginleşir; dışarı çıkarsa ekranı kararır ve
  uyarı çıkar → geri içeri girmesi yeterli.
- Maç, süre dolunca veya skor limitine ulaşılınca biter; kazanan duyurulur ve **~10 saniye
  sonra tüm gözlükler kendiliğinden lobiye döner.**

**Maçı geçici olarak durdurmak (DURAKLAT)**

Sahada bir şey olduğunda — biri gözlüğünü düzeltiyor, bir oyuncu düştü, seyirciyle konuşulacak —
**Tercihler → DURAKLAT**:

- Süre durur, kimse kimseye hasar veremez, **skorlar olduğu gibi kalır.**
- Oyuncular arenada kalır, hiçbir yere ışınlanmaz; maç bittiği sanılmaz.
- Aynı düğme **DEVAM ET**'e döner; bastığında maç **kaldığı yerden** sürer.
- Maçı gerçekten bitirmek istiyorsan duraklatma değil **İPTAL** (ya da **LOBİYE DÖN**) kullan.

> Düğme yalnız **maç koşarken** ve **senin duraklattığın maçta** çalışır. Geri sayım sırasında ya
> da lobide sönük durur — orada duraklatılacak bir şey yoktur. Birden çok yönetim ekranı varsa
> hepsi aynı düğmeyi görür: biri duraklatınca diğerinin ekranında da **DEVAM ET** yazar ve kimin
> yaptığı durum satırına düşer.

---

## 7. Maçlar arası ve gün sonu

**Sıradaki maç**

- [ ] Oyuncular lobiye döndü mü kontrol et (yönetim listesinde görünürler).
- [ ] Gerekiyorsa oyuncu değişimi yap (yeni oyuncu gözlüğü açar, kendiliğinden bağlanır).
- [ ] Mod/harita seç → **Maçı Başlat**. (Kalibrasyon genelde gözlükte saklı kalır; oyuncular
      birbirini yanlış yerde görüyorsa tekrar yaptır.)

**Gün sonu kapatma sırası** — açılışın tam tersi:

- [ ] **1.** Gözlüklerdeki uygulamayı kapat, gözlükleri şarja tak.
- [ ] **2.** Yönetim oyununu kapat (pencereyi kapat veya launcher'da **Durdur**).
- [ ] **3.** Launcher'ı kapat. *(Launcher'ı kapatmak sunucuyu kapatmaz — o ayrı bir penceredir.)*
- [ ] **4.** **En son** sunucu penceresini kapat (Ctrl + C veya pencereyi kapat).

---

## 8. Sorun giderme — basit dille

| Ne görüyorsun | Muhtemel sebep | Ne yapacaksın |
|---|---|---|
| Gözlükte "Sunucu bulunamadı" | Sunucu kapalı ya da gözlük sunucuyu bulamıyor | Önce sunucu penceresi açık mı bak. Açıksa: **sağ kumandada 2×A** → adresi elle gir (Bölüm 3.2) |
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
| Oyuncular birbirini yanlış yerde görüyor | Kalibrasyon yapılmadı ya da A–B ters alındı | Arenada **A + B ile yeniden kalibrasyon** yaptır (Bölüm 4) |
| Oyuncular birbirini **havada / yere gömülü** görüyor | Kalibrasyonda kumanda dik tutulmamış | O oyuncuya kalibrasyonu tekrarlat; kumanda **kalem gibi dik**, ucu yere değecek (Bölüm 4). Herkeste aynı sorun varsa teknik ekibi ara |
| Oyun ortasında arena birden kaydı | Gözlüğün konum takibi sıfırlandı | Genelde kendiliğinden düzelir. Düzelmezse o oyuncuya kalibrasyonu tekrarlat |
| Ateş ediyor ama can azalmıyor | Aynı takımdalar (dost ateşi kapalı) ya da maç henüz başlamadı | Takımları kontrol et; geri sayım bitmiş mi bak |
| Ölen oyuncu canlanmıyor | Kendi takımının tabanına girmemiş | Oyuncuya **kendi renginin köşesine yürümesini** söyle |
| Maç başlamıyor | Bağlı oyuncu yok ya da bir gözlükte eski sürüm var | Listede oyuncu var mı bak; varsa sunucu penceresindeki son satırı teknik ekibe ilet |
| Ses gelmiyor | Gözlüğün sesi kısık | Gözlüğün ses seviyesini aç |
| Oyuncunun ekranı karardı, uyarı çıktı | Oyun alanının dışına çıkmış | Oyuncuya geri içeri girmesini söyle |
| Bir oyuncu yanlış yerde görünüyor / "nişan aldığım yere gitmiyor" diyor | O gözlüğün kalibrasyonu kaymış | Satırındaki **KAL** düğmesiyle sıfırla, yeniden A+B yaptır (§4.1) |
| Bir avatar yanıp sönüyor | O oyuncu kalibresiz — ateş edemez, vurulamaz | Yeniden kalibre olmasını söyle (§4.1); bitince kendiliğinden düzelir |
| Oyuncu "silahım çalışmıyor" diyor, ekranında kalibrasyon yazısı var | Kalibrasyonu sıfırlanmış | A+B ile yeniden kalibre olsun (§4.1/4) |
| Oyuncu öldü ama canlanmıyor | Kalibresiz oyuncu canlanmaz | Önce kalibre olsun; hemen ardından kendiliğinden canlanır |

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
- [ ] **Sunucu bilgisayarının adresi** (ör. `192.168.1.10`) — 2×A paneline girilecek numara
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
│  A tuşuna hızlıca 2 KEZ   →  Gizli IP paneli (aç/kapat)  │
│                              (lobide, bağlanamayınca)    │
│                                                          │
│  A + B  3 saniye basılı   →  Kalibrasyon noktası al      │
│  (kumanda KALEM GİBİ DİK,    1 titreşim = A alındı       │
│   ucu yere değecek)          2 titreşim = B alındı, tamam│
│                              3 titreşim = mesafe yanlış, │
│                                           B'yi tekrar al │
│                              1 UZUN     = dik tutulmadı, │
│                                           B'yi tekrar al │
├──────────────────────────────────────────────────────────┤
│  OYUNCUYA — "HERKES TEK" MODUNDA                         │
│                                                          │
│  Yan tuş (grip) basılı    →  Elinde rastgele silah       │
│                              belirir; BIRAKINCA kaybolur │
│                              tekrar bas = başka silah    │
│  Şarjör bitti             →  Bırak, yenisini çek         │
│                              (şarjör dolmaz)             │
│  Öldün                    →  YÜRÜME. Olduğun yerde       │
│                              3 saniye kıpırdamadan dur   │
└──────────────────────────────────────────────────────────┘
```
