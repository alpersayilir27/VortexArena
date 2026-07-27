# İşletme (Venue) Kurulum Kontrol Listesi

Bu liste, VortexArena'yı yeni bir işletmeye kuran ekibin fiziksel alan ölçümünden kabul testine kadar sırayla uygulayacağı adımları içerir.

> Kısaltmalar: **Sunucu PC** = arenayı yöneten Windows bilgisayar · **Başlık** = Meta Quest 3 / 3S gözlük · **Admin** = Windows masaüstü uygulaması (yönetim + izleme) · **Player** = başlıktaki VR uygulaması.
> Teknik referanslar: `Server/README.md` (sunucu), `Docs/ArenaNet-Protokol.md` (portlar/sabitler), `CLAUDE.md` (proje mimarisi).
> Kurulum bittikten sonra **işletme personelinin günlük kullanacağı** kılavuz: `Docs/Kullanim-Kilavuzu.md` (teknik olmayan dille açılış sırası, 2×A IP paneli, kalibrasyon, dashboard, sorun giderme).

---

## 1. Ön koşullar

**Fiziksel alan**

- [ ] Serbest (engelsiz) oyun alanını ölç: arena boyutu = ölçülen alan − **0.5 m güvenlik payı** (her iki eksende).
- [ ] Standart `A10x10` arenayı olduğu gibi kullanacaksan alan en az **10.5 × 10.5 m** olmalı; daha küçük/asimetrik alanlarda arena şablon sihirbazıyla özel arena üretilir (Bölüm 2).
- [ ] Zemin düz, kaygan değil, seviye farkı ve kablo/eşik yok; alan içinde sütun, sabit mobilya, cam yüzey yok.
- [ ] Aydınlatma homojen ve gölgesiz; doğrudan güneş ışığı, güçlü spot ve ayna/parlak yansıtıcı yüzey yok (inside-out takip bozulur, lensler zarar görür).
- [ ] Tavan yüksekliği yeterli (kollar yukarıda serbest hareket edebilmeli).

> **Not:** minimum tavan yüksekliği (öneri ~3 m) ve minimum aydınlatma seviyesi (lux) sahada ölçülüp buraya yazılacak — **doğrulanacak**.
> **Not:** tek renkli/parlak, desensiz zeminlerde takip zayıflayabilir; mat ve dokulu zemin tercih edilir — **doğrulanacak**.

**Donanım**

- [ ] 1 adet **Sunucu PC** (Windows, **.NET 10 ASP.NET Core Runtime** kurulu — sunucu Kestrel kullandığı için düz .NET Runtime yetmez), tercihen kablolu Gigabit Ethernet ile AP'ye bağlı.
- [ ] 1 adet **5 GHz AP/router** (tercihen Wi-Fi 6), arenaya özel SSID.
- [ ] Oyuncu sayısı kadar **Quest 3 / 3S** başlık + kumandalar (protokol üst sınırı **16 oyuncu**) + şarj istasyonu/powerbank.
- [ ] USB-C kablo + `adb` (Meta Quest Developer Hub kuruluysa birlikte gelir).
- [ ] **Zemin bandı** (kalibrasyon işaretleri için, kalıcı ve renkli), şerit metre, işaretleme kalemi.

---

## 2. Alanı ölç ve arenayı üret (Unity, ofiste)

- [ ] Fiziksel alanı ölç (X ve Z, metre). Arena boyutu = ölçü − 0.5 m.
- [ ] Unity'de `Tools > VortexArena > Create Arena From Template` menüsünü aç ve doldur:
  - **Kaynak sahne:** `Assets/Arenas/Standard/A10x10/Scenes/Arena10x10.unity`
  - **Kaynak MapDefinition:** `Assets/Arenas/Standard/A10x10/Data/A10x10.asset`
  - **Arena Id (klasör):** işletme/arena adı · **Sahne adı:** katalog anahtarı (benzersiz olmalı) · **Gösterim adı:** admin panelinde görünecek ad
  - **Genişlik X / Derinlik Z:** hesapladığın arena boyutu · **Takım başına spawn:** takım başına en fazla oyuncu sayısı
  - **Kutu:** `Venue` · **İşletme adı (klasör):** işletme adı → hedef `Assets/Arenas/Venues/<İşletme>/`
  - **GameCatalog:** `Assets/_Shared/Data/GameCatalog.asset`
- [ ] "Oluştur" → sihirbaz `{Scenes, Data, Prefabs}` kutusunu üretir, sahneyi yeni boyuta ölçekler, `MapDefinition` yazar, `GameCatalog`'a ve **Build Settings**'e ekler.
- [ ] Sihirbazın uyarılarını uygula: **duvar/cover yerleşimi kabadır** → sanat geçişini elle yap; **NavMesh ve ışık verisi kaynak sahneden miras kalır** → yeni boyutta yeniden bake et.
- [ ] `Tools > VortexArena > Export Server Config` çalıştır → `Server/config/weapons.json` + `Server/config/maps.json` üretilir. Çıkan uyarıları oku; özellikle "sceneName Build Settings'te YOK / KAPALI" uyarısı varsa düzelt ve tekrar çalıştır.
- [ ] Build Settings'te yeni sahnenin **listede ve işaretli (enabled)** olduğunu doğrula. Sahne adı = `start_match` katalog anahtarı; boşluk/typo dahil birebir eşleşmeli.
- [ ] Android APK'yı **yeniden al** ve repo kökünde `game.apk` adıyla kaydet (`install_game.bat` bu adı bekler).
- [ ] (Test edilecekse) `Server/VortexArena.PoseBot` içindeki `BuildScenes` sabitine yeni sahne adını ekle — yoksa PoseBot ile `start_match` reddedilir.

---

## 3. Kalibrasyon işaretleri (zemin bandı)

Arena, her başlıkta **2 nokta** ile fiziksel alana hizalanır (`ArenaCalibrator`). Sahnede iki sanal işaretçi vardır: **`anchor_a`** ve **`anchor_b`**.

- [ ] Unity'de arena sahnesini aç → `CalibrationManager` altındaki `anchor_a` / `anchor_b` objelerinin Inspector'daki **Position** değerlerini oku ve aralarındaki mesafeyi hesapla.
  - `A10x10` şablonunda işaretçiler arena-yerel X ekseninde **±3 m** (yani **aralarında 6 m**) ve arena merkezine göre simetriktir.
  - Sihirbazla üretilen venue arenasında bu mesafe genişlik oranıyla **ölçeklenir** (ör. 10 m → 10.5 m genişlikte 6 × 1.05 = 6.3 m). **Varsayma, sahneden oku.**
- [ ] Gerekirse işaretçileri sahnede fiziksel alana uyacak şekilde taşı (odanın içinde, geçiş güzergâhının dışında kalsınlar) ve yeni mesafeyi not et; sahneyi kaydedip APK'yı yeniden al.
- [ ] Zemine iki bant işareti yapıştır: **A** ve **B**. Aralarındaki mesafe sahneden okuduğun değere eşit olmalı; bandın üzerine büyük harfle "A" ve "B" yaz.
- [ ] **A → B doğrultusu arenanın yönünü belirler.** A ve B karıştırılırsa arena 180° ters döner — işaretleri kalıcı ve okunur biçimde etiketle.
- [ ] İşaretler kalıcı olmalı (bant + gerekiyorsa zemine dayanıklı işaret); temizlik/mobilya hareketiyle kaymamalı. **Her başlık aynı iki noktayı kullanır.**
- [ ] Ölçüyü (A–B mesafesi, hangi duvara göre nerede) teslim paketine yaz.

**Başlıkta kalibrasyon prosedürü** (operatörün her başlıkta yapacağı):

1. Arena sahnesindeyken sağ kumandayı **A** işaretinin üzerine, zemine değecek şekilde koy (her başlıkta aynı tutuş).
2. Sağ kumandada **A + B tuşlarına 3 saniye basılı tut** — titreşim giderek artar; **tek titreşim** = A noktası alındı.
3. Aynısını **B** işaretinde yap — **çift titreşim** = B alındı ve arena hizalandı.
4. **Üç kısa titreşim** = iki nokta birbirine 1 m'den yakın; B'yi tekrar al.
5. Kalibrasyon başlıkta kaydedilir (uzamsal anchor) ve sonraki açılışta **otomatik geri yüklenir**. Yeniden kalibre etmek için A+B'yi tekrar 3 sn tut — kalibrasyon sıfırdan başlar.

> Kalibrasyon **arena sahnesinde** yapılır; lobide kalibratör yoktur, bu yüzden lobide uzak avatarların fiziksel olarak örtüşmesi beklenmez (normaldir). Bir başlık kalibre olana kadar arena sahnesinde poz göndermez.

---

## 4. Ağ (AP + sunucu PC + firewall)

**Erişim noktası**

- [ ] **5 GHz** yayın; arenaya özel **tek SSID** (tüm başlıklar ve sunucu PC aynı SSID/subnet'te).
- [ ] **Client isolation / AP isolation KAPALI** (cihazlar sunucuyu görebilmeli — kapalı değilse hiçbir başlık bağlanamaz).
- [ ] Kanal **sabit** (otomatik kanal değil), WMM/QoS **açık**, band steering kapalı.
- [ ] AP arenanın görüş alanına, oyuncuların üstünde/kenarında konumlandırıldı.

> **Not:** sabit kanal seçimi (DFS dışı kanal tercihi), WMM ve band steering ayarları saha tecrübesiyle netleşecek — **doğrulanacak**.

**Sunucu PC**

- [ ] Tercihen **kablolu (GbE)** bağlantı + **statik IP** (router'da DHCP rezervasyonu tercih edilir); IP'yi teslim paketine yaz. IP değişirse `arena.json` / başlıklardaki kayıtlı adres bozulur.
- [ ] `Server/firewall-kur.cmd` dosyasına **sağ tık → "Yönetici olarak çalıştır"**. Betik:
  - ağ profilini **Özel (Private)** yapar (Public profilde Defender gelen broadcast'i keser),
  - Windows'un otomatik eklediği **ENGELLE** kurallarını siler,
  - **UDP 47820** (beacon) + **TCP 47821** (WS kontrol) + **UDP 47822** (poz/state) için **İZİN** kuralı ekler (Private + Domain) — exe derlenmişse programa özel kural da ekler,
  - teşhis basar: aktif adaptörler, IPv4 adresleri, dinlenen portlar.
- [ ] **Tek aktif arayüz bırak.** Betiğin "birden fazla aktif adaptör" uyarısı çıkarsa: Ethernet + Wi-Fi aynı anda bağlı ya da VPN / Hyper-V / VMware / WSL sanal adaptörü var demektir → **beacon yanlış arayüzden yayılır ve başlıklar sunucuyu bulamaz.** Kullanılmayanları `Disable-NetAdapter -Name "<Ad>"` ile kapat.
- [ ] Sunucu ilk açılışta Windows "izin ver?" sorusu gösterirse **"İzin ver"e** bas. İptal edilirse kalıcı engelle kuralı oluşur → `firewall-kur.cmd`'yi tekrar çalıştır.

**Admin console çalıştıran diğer PC'ler**

- [ ] Sunucu PC ile **aynı subnet**te (aynı router, aynı `192.168.x.*` bloğu).
- [ ] Bu PC'lerde de `Server/firewall-kur.cmd` **yönetici olarak** çalıştırıldı — beacon *broadcast* paketi olduğu için stateful UDP eşleşmesine takılmaz; istemcide inbound izin yoksa Windows onu düşürür ve sunucu bulunamaz.
- [ ] Ağ profili bu PC'lerde de **Özel (Private)** (betik zaten yapar).

**Ağ doğrulaması**

- [ ] Sunucu çalışırken `netstat -ano | findstr 4782` → **`0.0.0.0:47821` görülmeli**. `127.0.0.1:47821` görülüyorsa sunucu yalnız loopback'e bind olmuştur, dışarıdan kimse bağlanamaz.
- [ ] Sunucu açıkken bir başlığı lobiye al: **kendiliğinden bağlanmalı** (beacon → otomatik bağlantı). Durum satırı `Bağlı — oyuncu N` göstermeli.
- [ ] Bağlanmıyorsa ~8 sn sonra lobide "sağ kumandada A'ya İKİ KEZ bas" ipucu çıkar → **A×2** ile gizli IP panelini aç, numpad ile **IP:port** (ör. `192.168.1.10:47821`) gir, **Bağlan**'a bas. Girilen adres beacon'ı **ezer** ve başlıkta kalıcı saklanır; sonraki açılışlarda panel gerekmez.
- [ ] Beacon hiç gelmiyorsa kalıcı çözüm: `Assets/StreamingAssets/arena.json` içine sunucunun statik IP'sini yaz (`{"serverIp":"192.168.x.y","serverPort":47821}`) — **bu dosya APK'nın içindedir, değişiklik yeni APK gerektirir**. Sahada hızlı çözüm her zaman A×2 ile elle IP girmektir.

---

## 5. Sunucu kurulumu

- [ ] `Server/config/server.json` dosyasını düzenle — genelde yalnız **`venueName`** işletme adına çekilir, portlar varsayılan kalır:
  ```json
  { "controlPort": 47821, "beaconPort": 47820, "statePort": 47822, "venueName": "<İşletme Adı>", "tickHz": 20 }
  ```
- [ ] `Server/config/weapons.json` ve `Server/config/maps.json` dosyalarının Bölüm 2'deki export'tan geldiğini doğrula (hasar **her zaman** sunucudaki tablodan uygulanır).
- [ ] **Dağıtım paketlerini üret** (ofiste, geliştirme makinesinde):
  - `scripts\deploy-server.bat` → `deploy\server\` (self-contained; işletme PC'sine .NET kurmak gerekmez)
  - `scripts\deploy-admin-game.bat` → `deploy\admin\` (**Unity editörü kapalı olmalı**)
  - `scripts\deploy-launcher.bat` → `deploy\launcher\` (Windows Developer Mode açık olmalı)
  - Üç klasörün **tamamını** kopyala — exe'ler tek başına çalışmaz.
- [ ] Sunucuyu başlat:
  - İşletmede: `deploy\server\VortexArena.Server.App.exe` (masaüstü kısayolu koyun)
  - Geliştirme: `dotnet run --project Server/VortexArena.Server.App`
  - Sunucu **her zaman elle** başlatılır; ne admin uygulaması ne launcher sunucuyu başlatır. Sebep: sunucu maçın tek otoritesidir, ömrü operatör uygulamasına bağlanmamalıdır.
- [ ] Açılış özetinde şunları gör ve doğrula:
  ```
  Mekan      : <İşletme Adı>
  WS kontrol : http://0.0.0.0:47821/ws
  UDP beacon : 47820 (her 2 sn)
  UDP state  : 47822
  Modlar     : tdm
  Silahlar   : ak47, m4
  Haritalar  : <arena sahne adları>
  Config     : <config klasörü>
  ```
- [ ] `Haritalar : yok (doğrulama kapalı)` yazıyorsa `maps.json` eksik/bozuk → Unity'de `Export Server Config`'i tekrar çalıştır ve sunucuyu yeniden başlat.
- [ ] Sunucu **Ctrl+C** ile temiz kapanıyor; işletme personeline başlat/kapat adımı gösterildi.

---

## 6. Başlıkların hazırlanması

- [ ] Her başlıkta geliştirici modu açık, USB hata ayıklama izni verildi.
- [ ] APK kurulumu: `game.apk` dosyasını repo köküne koy → `install_game.bat` çalıştır. Betik `adb devices` listesini gösterir ve `adb install -r -g game.apk` ile kurar.
  - `adb` bulunamıyorsa Android platform-tools (veya Meta Quest Developer Hub) kur ve PATH'e ekle.
  - Kablosuz kurulum için önce `adb connect <gozluk-ip>:5555`.
  - Aynı anda birden fazla cihaz bağlıysa diğerlerini çıkar.
- [ ] **Tüm başlıklarda aynı APK sürümü** olmalı — sahne listesi farklı olan bir başlık maçın başlamasını engeller (bkz. Bölüm 8).
- [ ] Cihaz adları: başlık ilk bağlandığında sunucu ona `Gözlük NN` adını atar ve `Server/config/devices.json`'a (`deviceId → ad`) yazar.
  - [ ] Admin panelindeki **"Bu cihazı tanıt" (identify)** komutuyla hangi adın hangi fiziksel başlık olduğunu bul, başlığa aynı numarayı **fiziken etiketle**.
  - [ ] Ad değiştirmek gerekirse `devices.json`'u **sunucu kapalıyken** düzenle (UTF-8, **BOM'suz**) ve sunucuyu yeniden başlat.
- [ ] Guardian/sınır ayarı: free-roam oyunda oyuncu tüm alanda yürür; oyun içi güvenlik `ArenaBoundary` ile sağlanır (kenara yaklaşınca duvarlar belirginleşir, dışarı çıkınca ekran kararır + uyarı).
- [ ] Uyku/ekran kapanma: başlıkların maç arasında uykuya geçmemesi için ekran/uyku süresi en uzun değere alındı.

> **Not:** Guardian'ın hangi ayardan devre dışı bırakılacağı (geliştirici ayarları / boundaryless) ve uyku süresi menü yolu cihaz yazılımı sürümüne göre değişir — kurulumda ekran görüntüsüyle belgelenecek, **doğrulanacak**.

- [ ] Şarj düzeni: her başlık için sabit şarj yeri ve etiket; seans öncesi pil seviyesi kontrol ediliyor.

> **Not:** sahaya çıkarma için minimum pil eşiği (öneri %50) işletmeyle birlikte kararlaştırılacak — **doğrulanacak**.

---

## 7. Smoke test (kurulum kabulü)

Sırayla uygula; her madde geçmeden sonrakine geçme.

- [ ] **1.** Sunucu PC'de sunucu **elle** çalıştırıldı, açılış özeti doğru (Bölüm 5).
- [ ] **2.** **Launcher** (`deploy\launcher\vortex_launcher.exe`) açıldı; Ayarlar'da `deploy\admin\VortexArena.exe` seçili, **Sunucu IP** sunucunun statik IP'si.
- [ ] **3.** Launcher'da **Yönetimi Başlat** → admin uygulaması açıldı ve **IP sormadan** doğrudan **oyuncuların bulunduğu sahneye** düştü (bağlanma ekranında takılı kalmıyor); ortada skor bandı, yanlarda oyuncu listeleri, altta kamera şeridi görünüyor.
- [ ] **4.** İki başlık açıldı → uygulama doğrudan **Lobi**'ye düştü → ikisi de **kendiliğinden** bağlandı (IP girilmedi).
- [ ] **5.** Admin **roster**'ında iki oyuncu da çevrimiçi görünüyor; sunucu konsolunda `[+] Gözlük NN bağlandı` ve `[u] UDP kayıt` satırları var.
- [ ] **6.** Admin **kuş bakışı** kipinde (`3`) iki oyuncu da hareket ediyor; her birinin etrafında **halka**, altında **adı** var (lobide henüz hizalı değiller — normal).
- [ ] **6b.** Kamera kipleri çalışıyor: `1` POV (bir oyuncu seçilip onun gözünden), `2` serbest (WASD/QE + sağ tuş bakış), `3` kuş bakışı. `P` tercihler / `I` istatistik panelleri açılıyor ve **arkada sahne görünmeye devam ediyor**.
- [ ] **7.** Admin **Tercihler** panelinden **mod (tdm) + harita (işletme arenası)** seçildi → **BAŞLAT**. Admin de aynı arena sahnesini yükledi (gözlemci). Sunucu konsolunda `start_match: mod 'tdm', sahne '<Arena>'` satırı görünüyor; iki başlık da arena sahnesini yükledi.
- [ ] **8.** **Her iki başlıkta da kalibrasyon yapıldı** (Bölüm 3 prosedürü, A ve B işaretleri üzerinde).
- [ ] **9.** İki oyuncu fiziksel olarak **yan yana / aynı noktada** durdu → birbirlerinin avatarını doğru yerde görüyor; admin kuş bakışında da iki halka üst üste geliyor. (Örtüşme yoksa Bölüm 8'e bak.)
- [ ] **10.** Maç `Countdown (5 sn)` → `Live` geçişini yaptı; iki başlıkta da skor/can HUD'ı görünüyor.
- [ ] **11.** Bir oyuncu diğerine ateş etti → **can azaldı**, admin HUD'ında **skor + HP barı + ölüm akışı** ve istatistik tablosundaki **K/D** güncellendi; sunucu konsolunda `öldürme: … — skor kırmızı N : mavi N` satırı var.
- [ ] **12.** Ölen oyuncu ölüm ekranını gördü → **5 sn** sonra kendi takım tabanına (`BaseZone`) fiziken girdi → **canlandı** (`canlandı: Gözlük NN`). Zorla canlandırma satırı (`zorla canlandırma`) görünüyorsa taban/ölüm akışını gözden geçir.
- [ ] **13.** Maç süresi/skor limiti dolunca `maç sonu — kazanan: …` yayınlandı; 10 sn sonra tüm başlıklar **lobiye döndü**.
- [ ] **14.** Bir başlığı kapat-aç → kendiliğinden yeniden bağlandı ve roster'da göründü.
- [ ] **15.** İşletme personeline: sunucu başlatma/kapatma, maç başlatma, kalibrasyon ve sorun giderme tablosu bir kez uygulamalı gösterildi.

---

## 8. Teslim paketi ve sorun giderme

**Teslim paketi (işletmede kalacaklar)**

- [ ] Sunucu PC'de: `deploy\server\` klasörü (exe + `config/server.json`, `weapons.json`, `maps.json`, `devices.json` + `firewall-kur.cmd`) ve masaüstünde **sunucu exe** kısayolu.
- [ ] Yönetim PC'sinde: `deploy\admin\` + `deploy\launcher\` klasörleri ve masaüstünde **launcher** kısayolu. Operatör yalnız launcher'ı açar — admin exe'sine doğrudan tıklanmaz (adres gelmez).
- [ ] Launcher'da Ayarlar (admin exe yolu) ve Sunucu IP bir kez doldurulup **Yönetimi Başlat** ile doğrulandı; ayarlar kalıcı saklanır.
- [ ] Bilgi kartı: SSID + Wi-Fi parolası, sunucu statik **IP:port** (`…:47821`), arena **sahne adı**, A–B zemin işaretleri arası **mesafe**, APK sürümü + kurulum tarihi.
- [ ] Başlık etiketleri ↔ `devices.json` adları eşleşme listesi.
- [ ] Tek sayfalık operatör kartı: **sunucu exe'sini başlat → launcher'ı aç → Yönetimi Başlat → başlıkları aç (kendiliğinden bağlanır) → maç başlat → kalibrasyon → maç sonu.**
- [ ] **`Docs/Kullanim-Kilavuzu.md`** (operatörün günlük kullanım kılavuzu — teknik olmayan dille: açılış sırası, 2×A gizli IP paneli, kalibrasyon, dashboard kontrolleri, sorun giderme) yazdırılıp işletmede bırakıldı; sondaki kumanda hatırlatma kartı ayrıca yönetim masasına asıldı.

**Sorun giderme**

| Belirti | Olası sebep | Çözüm |
|---|---|---|
| Ekranda **"SUNUCUYA BAĞLANILAMIYOR"** yazıyor (altında adres + `N sn · M. deneme`) | Adres **biliniyor** ama sunucuya erişilemiyor: sunucu exe'si kapalı/çökmüş; firewall engelliyor; cihaz farklı SSID/subnet'te; sunucunun IP'si değişmiş (DHCP) — ekrandaki adres artık yanlış | Ekranda yazan **adresi oku** (sahada ilk teşhis budur): sunucu PC'sinin gerçek IP'siyle aynı mı? 1) sunucuyu başlat, 2) `Server/firewall-kur.cmd`'yi **yönetici** olarak çalıştır, 3) sunucu PC'sinde `netstat -ano` çıktısında **`0.0.0.0:47821`** görün. Bağlantı kurulunca ekran kendiliğinden kaybolur; masaüstünde beklemek istemezsen **"Yeniden Bağlan"**a bas. Ekranın alt satırındaki **"Son hata"** mesajı (ör. bağlantı reddedildi / zaman aşımı) firewall ile kapalı sunucuyu ayırt ettirir |
| Ekranda **"SUNUCU BULUNAMADI"** yazıyor ("adres yok") | Cihazın elinde **hiç adres yok**. Gözlük: beacon gelmiyor ve kayıtlı IP de `arena.json` da yok. Admin: oyun **launcher'sız** (exe'ye çift tıklanarak) açılmış → `--server-ip` gelmemiş | Gözlük: lobide sağ kumandada **A×2** → gizli IP paneli → `IP:port` gir (kalıcı saklanır). Admin: uygulamayı **launcher'dan** başlat (Ayarlar→admin exe + Sunucu IP dolu olmalı). Bu ekranda **"Yeniden Bağlan" devre dışıdır** — denenecek adres olmadığı için bilinçlidir |
| Başlık sunucuyu bulamıyor / bağlanamıyor | Firewall'da engelle kuralı; AP'de **client isolation açık**; başlık farklı SSID/subnet'te; PC ağ profili "Genel" | `firewall-kur.cmd`'yi **yönetici** olarak çalıştır; AP'de isolation'ı kapat; SSID'yi kontrol et; ağ profilini **Özel** yap |
| Başlık kendiliğinden bağlanmıyor, "sunucu aranıyor" | AP broadcast'i kesiyor / VLAN ayrımı → beacon gelmiyor | Lobide sağ kumandada **A×2** → gizli IP paneli → **IP:port** elle gir (beacon'ı ezer, kalıcı saklanır); kalıcı çözüm için `StreamingAssets/arena.json` + yeni APK |
| Admin uygulaması "Sunucu adresi yok" diyor (3 sn sonra tam ekran **"SUNUCU BULUNAMADI"**) | Oyun launcher'sız (elle) açılmış — `--server-ip` gelmemiş | Oyunu **launcher'dan** başlat; launcher'da Ayarlar→admin exe ve Sunucu IP dolu olmalı |
| Launcher "Admin exe bulunamadı" diyor | `deploy\admin\` silinmiş/taşınmış veya build alınmamış | `scripts\deploy-admin-game.bat` (editör kapalıyken) çalıştır, launcher'da exe'yi yeniden seç |
| Bağlanıyor ama roster'da "çevrimdışı" düşüyor | 15 sn boyunca status gelmedi (Wi-Fi zayıf, başlık uykuya geçti) | AP kapsamasını/kanalı kontrol et; başlıkta uyku süresini uzat |
| Avatarlar fiziksel olarak örtüşmüyor | Bir başlıkta kalibrasyon yapılmadı; A ile B karışmış (arena 180° ters); zemin işaretleri kaymış; işaret mesafesi sahnedeki `anchor_a`/`anchor_b` mesafesiyle uyuşmuyor | Her başlıkta A+B ile **yeniden kalibre et** (tamamlanmış kalibrasyonda A+B tutmak sıfırlar); bant ölçüsünü sahnedeki değerle karşılaştır. **Lobide örtüşme beklenmez** — kontrolü arena sahnesinde yap |
| Vuruş kaydolmuyor | Sunucu reddediyor: dost ateşi, hedef zaten ölü, faz `Live` değil, atış hızı denetimi (`60/rpm × 0.8` sn), `weaponId` sunucu tablosunda yok | Sunucu konsolundaki `hit_report reddedildi (…): <sebep>` satırını oku; silah eklendiyse Unity'de `Export Server Config` çalıştır ve sunucuyu yeniden başlat |
| Hasar beklenenden farklı | İstemci silah SO'su ile `weapons.json` sapmış (`hasar uyumsuz: … tablo uygulandı`) | `Export Server Config` → sunucuyu yeniden başlat (hasarı **her zaman** sunucu tablosu belirler) |
| Maç başlamıyor: "sahne build listesinde yok" / `start_match reddedildi` | Sahne bazı başlıkların APK'sında yok (eski sürüm); sahne Build Settings'te yok/kapalı; sahne adı ≠ katalog anahtarı (typo) | Tüm başlıklara **aynı APK**'yı kur; Build Settings'te sahneyi ekle+işaretle; `MapDefinition.sceneName` ile sahne dosya adını birebir eşitle, `Export Server Config`'i tekrarla |
| Maç `Loading`'de takılıyor | Bir başlık `set_ready` göndermedi (sahne yüklenemedi) | 20 sn sonra sunucu yine de devam eder; konsoldaki `loading zaman aşımı — hazır olmayanlar: …` satırındaki başlığı kontrol et |
| Ölen oyuncu canlanmıyor | 5 sn gecikme dolmadı; oyuncu **kendi takımının** tabanına fiziken girmedi | Oyuncuya doğru tabana (takım rengi) yürümesi söylenir; 20 sn sonunda sunucu zaten zorla canlandırır (`zorla canlandırma` satırı) |
| Ses yok / silah sesi gelmiyor | Başlık sistem sesi kısık; oyuncu kulaklık takmış; silah prefabındaki ses kaynağı/klipler boş; build'de spatializer ayarı bozulmuş | Başlık sesini aç; silah prefabında `WeaponAudio` kaynağı ve klipleri dolu mu bak; ses ayarında spatializer'ın **Meta XR Audio** olduğunu doğrula ve APK'yı yeniden al |
| Oyuncunun ekranı kararıyor, "alan dışı" uyarısı çıkıyor | Oyuncu arena sınırının dışına çıktı; arena boyutu fiziksel alandan büyük girilmiş; kalibrasyon kaymış | Fiziksel alanı yeniden ölç (0.5 m güvenlik payı!); gerekirse arenayı sihirbazla doğru boyutta yeniden üret; kalibrasyonu tekrarla |

---

Bu liste `plan/faz4-editor-sdk.md` Adım 4'ün çıktısıdır.
