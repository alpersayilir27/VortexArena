# İşletme (Venue) Kurulum Kontrol Listesi

Bu liste, VortexArena'yı yeni bir işletmeye kuran ekibin fiziksel alan ölçümünden kabul testine kadar sırayla uygulayacağı adımları içerir.

> Kısaltmalar: **Sunucu PC** = arenayı yöneten Windows bilgisayar · **Başlık** = Meta Quest 3 / 3S gözlük · **Admin** = Windows masaüstü uygulaması (yönetim + izleme) · **Player** = başlıktaki VR uygulaması.
> Teknik referanslar: `Server/README.md` (sunucu), `Docs/ArenaNet-Protokol.md` (portlar/sabitler), `CLAUDE.md` (proje mimarisi).
> Kurulum bittikten sonra **işletme personelinin günlük kullanacağı** kılavuz: `Docs/Kullanim-Kilavuzu.md` (teknik olmayan dille açılış sırası, 2×A IP paneli, kalibrasyon, dashboard, sorun giderme).

---

## 1. Ön koşullar

**Fiziksel alan**

- [ ] Serbest (engelsiz) oyun alanını ölç: arena boyutu = ölçülen alan − **0.5 m güvenlik payı** (her iki eksende).
- [ ] Standart `A12x12` arenayı olduğu gibi kullanacaksan alan en az **12.5 × 12.5 m** olmalı; daha küçük/asimetrik alanlarda arena şablon sihirbazıyla özel arena üretilir (Bölüm 2).
- [ ] Zemin düz, kaygan değil, seviye farkı ve kablo/eşik yok; alan içinde sütun, sabit mobilya, cam yüzey yok.
- [ ] Aydınlatma homojen ve gölgesiz; doğrudan güneş ışığı, güçlü spot ve ayna/parlak yansıtıcı yüzey yok (inside-out takip bozulur, lensler zarar görür).
- [ ] Tavan yüksekliği yeterli (kollar yukarıda serbest hareket edebilmeli).

> **Not:** minimum tavan yüksekliği (öneri ~3 m) ve minimum aydınlatma seviyesi (lux) sahada ölçülüp buraya yazılacak — **doğrulanacak**.
> **Not:** tek renkli/parlak, desensiz zeminlerde takip zayıflayabilir; mat ve dokulu zemin tercih edilir — **doğrulanacak**.
> **Zemin DÜZ olmalı.** Bu bir tercih değil gereksinimdir: (1) serbest dolaşımda koşan oyuncu için eğim düşme riskidir, (2) kalibrasyon zemini tek bir yükseklik olarak alır — eğim telafisi **yoktur** ve bilerek yapılmamıştır (iki nokta bir düzlem tanımlamaz; ayrıca sanal dünyayı eğmek VR'da mide bulantısı yapar). Arena alanı içinde A–B işaretleri arasındaki yükseklik farkı **3 cm'i geçmemeli**.

**Donanım**

- [ ] 1 adet **Sunucu PC** (Windows, **.NET 10 ASP.NET Core Runtime** kurulu — sunucu Kestrel kullandığı için düz .NET Runtime yetmez), tercihen kablolu Gigabit Ethernet ile AP'ye bağlı.
- [ ] 1 adet **5 GHz AP/router** (tercihen Wi-Fi 6), arenaya özel SSID.
- [ ] Oyuncu sayısı kadar **Quest 3 / 3S** başlık + kumandalar + şarj istasyonu/powerbank. **Yazılımda eşzamanlı oyuncu sınırı yoktur** — pratik sınırı fiziksel alan (kişi başına güvenli hareket payı) ve AP kapasitesi belirler.
- [ ] USB-C kablo + `adb` (Meta Quest Developer Hub kuruluysa birlikte gelir).
- [ ] **Zemin bandı** (kalibrasyon işaretleri için, kalıcı ve renkli), şerit metre, işaretleme kalemi.

---

## 2. Alanı ölç ve arenayı üret (Unity, ofiste)

- [ ] Fiziksel alanı ölç (X ve Z, metre). Arena boyutu = ölçü − 0.5 m.
- [ ] Unity'de `Tools > VortexArena > Create Arena From Template` menüsünü aç ve doldur:
  - **Kaynak sahne:** `Assets/Arenas/Standard/Default12x12/Scenes/Default12x12.unity`
  - **Kaynak MapDefinition:** `Assets/Arenas/Standard/Default12x12/Data/Default12x12.asset`
  - **Arena Id (klasör):** işletme/arena adı · **Sahne adı:** katalog anahtarı (benzersiz olmalı) · **Gösterim adı:** admin panelinde görünecek ad
  - **Kutu:** `Venue` · **İşletme adı (klasör):** işletme adı → hedef `Assets/Arenas/Venues/<İşletme>/`
  - **GameCatalog:** `Assets/_Shared/Data/GameCatalog.asset`
- [ ] "Oluştur" → sihirbaz `{Scenes, Data, Prefabs}` kutusunu üretir, sahneyi **bire bir kopyalar**, `MapDefinition` yazar, `GameCatalog`'a ve **Build Settings**'e ekler.
  > ⚠️ **Sihirbaz boyut sormaz ve geometriyi ölçeklemez.** Sahne 12×12 şablonundan olduğu gibi gelir; arena planını sen çizersin. Sebebi: her işletmenin alanı farklı ve çoğu kare/dikdörtgen bile değil — orantılı ölçekleme işe yarar bir taslak üretmez. Sihirbazın işi, sahnenin ağ bileşenlerini (kalibratör, poz senkronu, sınır, taban bölgeleri, rig) eksiksiz getirmesi.
- [ ] **Arena planını çiz** (duvar/cover yerleşimi, ölçüler) ve şu ikisini gerçek ölçüye getir: sahnedeki **`ArenaBoundary.halfExtentX/Z`** · **kalibrasyon işaretçilerinin konumu** (Bölüm 3).
- [ ] Sihirbazın kalan uyarılarını uygula: **NavMesh ve ışık verisi kaynak sahneden miras kalır** → yeni plana göre yeniden bake et; **tek `SpawnPoint`** elle konur.
- [ ] `Tools > VortexArena > Export Server Config` çalıştır → `Server/config/maps.json` üretilir. Çıkan uyarıları oku; özellikle "sceneName Build Settings'te YOK / KAPALI" uyarısı varsa düzelt ve tekrar çalıştır.
- [ ] Build Settings'te yeni sahnenin **listede ve işaretli (enabled)** olduğunu doğrula. Sahne adı = `start_match` katalog anahtarı; boşluk/typo dahil birebir eşleşmeli.
- [ ] Android APK'yı **yeniden al**: `scripts\deploy-player-apk.bat` (Unity editörü kapalı) → `deploy\player\game.apk`. Yeni arena APK'da yoksa o başlık maçı engeller (Bölüm 8).

---

## 3. Kalibrasyon işaretleri (zemin bandı)

Arena, her başlıkta **2 nokta** ile fiziksel alana hizalanır (`ArenaCalibrator`). Sahnede iki sanal işaretçi vardır: **`anchor_a`** ve **`anchor_b`**.

> **Kalibrasyon 6 serbestlik derecesini de kurar:** yönü (yaw) ve yatay konumu A→B çiftinden, **zemin yüksekliğini B noktasında kumandanın ucundan** alır. Zemin yüksekliği gözlüğün kendi "floor level" bilgisinden ALINMAZ — başlıklarda alan kurulumu yapılmadığı için (§5) o değer bir tahmindir: gözlük havadayken açılırsa yanlış başlar, oturum içinde tracking kaybı sonrası kayabilir. Bu yüzden her kalibrasyon zemini de yeniden ölçer.

- [ ] Unity'de arena sahnesini aç → `anchor_a` / `anchor_b` objelerinin Inspector'daki **Position** değerlerini oku ve aralarındaki mesafeyi hesapla. ⚠️ Objeler `VA_CalibrationManager`'ın altında DEĞİL, arena kökündeki **`Ground`** grubunun altındadır ve sahnede **kapalı** görünür (kalibrasyonda açılırlar) — hiyerarşide arama kutusuna `anchor_` yazmak en hızlısı. `VA_CalibrationManager` yalnız `ArenaCalibrator` bileşenini taşır ve iki objeye referans verir.
  - `Default12x12` şablonunda işaretçiler arena-yerel X ekseninde **±3 m** (yani **aralarında 6 m**) ve arena merkezine göre simetriktir.
  - Sihirbazla üretilen venue arenasında işaretçiler **ÖLÇEKLENMEZ** — kaynak arenadaki yerlerinde dururlar (sihirbaz sonuç ekranında bunu uyarır). Yerleri arena boyutundan değil sahadaki zemin bandından geldiği için yerleştirme bilinçli olarak elle bırakılmıştır. **Varsayma, sahneden oku.**
- [ ] İşaretçileri sahnede fiziksel alana uyacak şekilde **taşı** (odanın içinde, geçiş güzergâhının dışında kalsınlar) ve yeni mesafeyi not et; sahneyi kaydedip APK'yı yeniden al. Yeni üretilmiş bir venue arenasında bu adım **zorunludur** — işaretçiler kaynak arenadan olduğu gibi gelir.
- [ ] Zemine iki bant işareti yapıştır: **A** ve **B**. Aralarındaki mesafe sahneden okuduğun değere eşit olmalı; bandın üzerine büyük harfle "A" ve "B" yaz. ⚠️ Ölçü **±%20 tolerans** içinde olmalı — dışındaysa başlık kalibrasyonu reddeder (üç kısa titreşim), çünkü yanlış mesafe sessizce bozuk bir hizalama üretirdi.
- [ ] **A → B doğrultusu arenanın yönünü belirler.** A ve B karıştırılırsa arena 180° ters döner — işaretleri kalıcı ve okunur biçimde etiketle.
- [ ] İşaretler kalıcı olmalı (bant + gerekiyorsa zemine dayanıklı işaret); temizlik/mobilya hareketiyle kaymamalı. **Her başlık aynı iki noktayı kullanır.**
- [ ] Ölçüyü (A–B mesafesi, hangi duvara göre nerede) teslim paketine yaz.

**Başlıkta kalibrasyon prosedürü** (operatörün her başlıkta yapacağı):

1. Arena sahnesindeyken sağ kumandayı **A** işaretinin üzerine, **yere 90° dik** tutup **ucunu zemine değdir**. Bu duruş her iki noktada da aynı olmalı — zemin yüksekliği bu ölçümden çıkar.
2. Sağ kumandada **A + B tuşlarına 3 saniye basılı tut** — titreşim giderek artar; **tek titreşim** = A noktası alındı.
3. Aynısını **B** işaretinde yap — **çift titreşim** = B alındı ve arena hizalandı.
4. Hata sinyalleri:
   - **Üç kısa titreşim** = iki nokta arasındaki mesafe sahnedeki `anchor_a`–`anchor_b` mesafesine uymuyor (±%20 dışında); bant ölçüsünü kontrol et, B'yi tekrar al.
   - **Bir uzun titreşim** = iki noktanın ölçülen zemin yüksekliği 10 cm'den fazla ayrışıyor; kumanda dik tutulmamış (ya da zemin eğimli). Duruşu düzelt, B'yi tekrar al.
5. Kalibrasyon başlıkta kaydedilir (uzamsal anchor) ve sonraki açılışta **otomatik geri yüklenir**. Yeniden kalibre etmek için A+B'yi tekrar 3 sn tut — kalibrasyon sıfırdan başlar.

> **Kumanda tutuşu neden önemli:** başlığın takip ettiği nokta kumandanın **ucu değil gövdesinin içindeki pivottur**. Yazılım bu farkı `ArenaCalibrator.tipLocalOffset` ile telafi eder; değer kumanda modeline özgüdür ve **bir kez ölçülür**: alan kurulumu YAPILMIŞ bir gözlükte kumandayı dik tutup yere değdir, `rightControllerAnchor.position.y` değerini oku ve alanın Y'sine (eksi işaretle) gir. Ölçülene kadar varsayılan **-0.08 m** bir tahmindir. Doğrulaması: kalibrasyondan sonra sanal işaretçi küpleri görünür olur — kumandanın ucu küpün tabanıyla aynı hizada olmalı.

> Kalibrasyon **işletmenin lobi sahnesinde** yapılır (§5, `server.json → lobbyScene`) — maçtan önce, arenaya girmeden. Lobi sahnesi de bir arena kutusudur: `ArenaCalibrator` ve A/B işaretçileri oradadır, harita değişimi kalibrasyonu sıfırlamadığı için hizalama maça taşınır. ⚠️ **`lobbyScene` yapılandırılmamışsa** oyuncular kalibratörsüz kabuk lobide kalır; o durumda kalibrasyon arena sahnesinde yapılır ve kabuk lobide avatarların fiziksel olarak örtüşmesi beklenmez (normaldir). Bir başlık kalibre olmadan da poz gönderir — ama o pozlar arena ile örtüşmez: avatar kaymış görünür, hareket ettiği yine de izlenir. Örtüşme kalibrasyondan sonra oturur.

> **Kalibrasyon durumu sunucuda tutulur ve operatör tarafından sıfırlanabilir** (`Docs/ArenaNet-Protokol.md` §10.6). Bir başlığın hizalaması sahada kayarsa operatör admin ekranındaki **KAL** düğmesiyle o oyuncuyu savaş dışı bırakır (ateş edemez, hasar yemez, canlanamaz; avatarı diğerlerinin ekranında parlar), oyuncu yeniden kalibre olunca kaldığı yerden devam eder. **Kalibre durumdayken A+B kilitlidir** — oyuncu kendi hizalamasını kazara bozamaz. Operatör yönergesi: `Docs/Kullanim-Kilavuzu.md` §4.1.

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

- [ ] `Server/config/server.json` dosyasını düzenle — genelde yalnız **`venueName`** ve **`lobbyScene`** işletmeye çekilir, portlar varsayılan kalır:
  ```json
  { "controlPort": 47821, "beaconPort": 47820, "statePort": 47822, "venueName": "<İşletme Adı>",
    "tickHz": 20, "venue": "", "lobbyScene": "" }
  ```
  `venue` = **açılışta oynatılacak mekan** (§11.1). Boş bırakılırsa sunucu her açılışta konsolda
  sorar (saha kullanımı budur; operatör listeden işletmeyi seçer). O oturumda **yalnız o mekanın
  haritaları** başlatılabilir ve admin panelinde yalnız onlar görünür. Kiosk/otomatik açılış
  isteniyorsa işletmenin adı buraya yazılır — mekan adı, arenaların durduğu klasörün adıdır
  (`Assets/Arenas/Venues/<İşletme>/`).

  `lobbyScene` = lobi sahnesi. **Normalde BOŞ bırakılır** — seçilen mekanın lobi haritası otomatik
  bulunur. Yalnız bir mekanda birden çok lobi varsa doldurulur. Maç koşmadığı sürece oyuncular orada bekler: birbirlerini görürler, **kalibrasyonlarını orada yaparlar**, raftan silah alıp hedef tahtalarına ateş edebilirler — birbirlerine hasar veremeden. Doldurulacaksa değer, odanın ölçüsüne uyan lobi olmalıdır (12×12 → `Lobby12x12`, işletmeye özel ölçü → o işletmenin kendi lobisi) ve seçilen mekanda bulunmalıdır. Mekanda hiç lobi haritası yoksa oyuncular kabuk bekleme ekranında kalır ve kalibrasyonu arenada yapmak zorunda kalırlar.
- [ ] `Server/config/maps.json` dosyasının Bölüm 2'deki export'tan geldiğini doğrula (silah tablosu yoktur — hasarı istemci bildirir).
- [ ] **Dağıtım paketlerini üret** (ofiste, geliştirme makinesinde):
  - `scripts\deploy-server.bat` → `deploy\server\` (self-contained; işletme PC'sine .NET kurmak gerekmez)
  - `scripts\deploy-admin-game.bat` → `deploy\admin\` (**Unity editörü kapalı olmalı**)
  - `scripts\deploy-player-apk.bat` → `deploy\player\` (**Unity editörü kapalı olmalı** + Android Build Support modülü; aktif platformu Android'e çevirir, ilk geçiş 20-40 dk)
  - `scripts\deploy-launcher.bat` → `deploy\launcher\` (Windows Developer Mode açık olmalı)
  - Klasörlerin **tamamını** kopyala — exe'ler tek başına çalışmaz.
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
- [ ] APK kurulumu: `install_game.bat` çalıştır — **repo kökündeki de `deploy\player\` altındaki kopya da olur**. Betik APK'yı sırayla kendi yanında, `deploy\player\` ve `Builds\player\` altında arar; bulduğu dosyanın yolunu, boyutunu ve tarihini yazar (doğru build'i kurduğunu böyle görürsün), sonra `adb devices` listesini gösterip `adb install -r -g` ile kurar.
  - `adb` bulunamıyorsa Android platform-tools (veya Meta Quest Developer Hub) kur ve PATH'e ekle.
  - Kablosuz kurulum için önce `adb connect <gozluk-ip>:5555`.
  - Aynı anda birden fazla cihaz bağlıysa diğerlerini çıkar.
- [ ] **Tüm başlıklarda aynı APK sürümü** olmalı — sahne listesi farklı olan bir başlık maçın başlamasını engeller (bkz. Bölüm 8).
- [ ] Cihaz kimlikleri: başlık ilk bağlandığında sunucu ona havuzdan rastgele bir **ad** ve 1'den itibaren ilk boş **forma numarasını** (1..99) atar, `Server/config/devices.json`'a (`deviceId → {name, number}`) yazar. Ad tekrar edebilir, **numara tüm kayıtlı cihazlar arasında benzersizdir**.
  - [ ] Admin panelindeki **"Bu cihazı tanıt" (identify)** komutuyla hangi kimliğin hangi fiziksel başlık olduğunu bul, başlığa **numarasını** fiziken etiketle.
  - [ ] Ad/numara değiştirmek: admin **Tercihler → OYUNCU KİMLİĞİ** bölümünden, oyuncu seçiliyken, sunucuyu durdurmadan yapılır. (Elle `devices.json` düzenlemek gerekmez; gerekirse **sunucu kapalıyken** düzenle — UTF-8, **BOM'suz** — ve yeniden başlat.)
- [ ] **Guardian/alan kurulumu YAPILMAZ.** Her başlıkta geliştirici ayarlarından fiziksel alan özellikleri kapatılır; oyuncu tüm alanda serbest dolaşır. Oyun içi güvenlik `ArenaBoundary` ile sağlanır (kenara yaklaşınca duvarlar belirginleşir, dışarı çıkınca ekran kararır + uyarı) — **guardian uyarısı olmadığı için tek fiziksel güvenlik ağı budur**, kalibrasyonun doğruluğu bu yüzden bir konfor değil güvenlik meselesidir.
  - Bunun iki sonucu var ve ikisi de yazılımda karşılanmıştır: (1) sistemin zemin seviyesi güvenilmez → kalibrasyon zemini kumandadan ölçer (§3), (2) tracking origin kayması kalibrasyonu bozardı → sahneler **Stage** tracking origin kullanır (sistem recenter'ı kapalı) ve yine de bir kayma olursa `ArenaCalibrator` kayıtlı anchor'dan kendini yeniden hizalar.
- [ ] Uyku/ekran kapanma: başlıkların maç arasında uykuya geçmemesi için ekran/uyku süresi en uzun değere alındı.

> **Not:** Guardian'ın hangi ayardan devre dışı bırakılacağı (geliştirici ayarları / boundaryless) ve uyku süresi menü yolu cihaz yazılımı sürümüne göre değişir — kurulumda ekran görüntüsüyle belgelenecek, **doğrulanacak**.
> **Doğrulanacak (ilk saha testi):** alan kurulumu kapalıyken (1) Stage tracking origin zemin yüksekliğini veriyor mu, (2) `OVRSpatialAnchor` kalıcılığı çalışıyor mu — uygulamayı kapatıp açtığında logda `rig aligned from saved anchor` görünmeli. Görünmüyorsa her gözlük devrinde elle kalibrasyon gerekir.

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
- [ ] **12.** Ölen oyuncu ölüm ekranını gördü → **5 sn** sonra kendi takımının taban bölgesine (`BaseZone` — renkli şerit) fiziken girdi → **canlandı** (`canlandı: Gözlük NN`). Zorla canlandırma satırı (`zorla canlandırma`) görünüyorsa taban/ölüm akışını gözden geçir.
- [ ] **13.** Maç süresi/skor limiti dolunca `maç sonu — kazanan: …` yayınlandı; 10 sn sonra tüm başlıklar **lobiye döndü**.
- [ ] **14.** Bir başlığı kapat-aç → kendiliğinden yeniden bağlandı ve roster'da göründü.
- [ ] **15.** İşletme personeline: sunucu başlatma/kapatma, maç başlatma, kalibrasyon ve sorun giderme tablosu bir kez uygulamalı gösterildi.

---

## 8. Teslim paketi ve sorun giderme

**Teslim paketi (işletmede kalacaklar)**

- [ ] Sunucu PC'de: `deploy\server\` klasörü (exe + `config/server.json`, `maps.json`, `devices.json` + `firewall-kur.cmd`) ve masaüstünde **sunucu exe** kısayolu.
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
| Avatarlar fiziksel olarak örtüşmüyor | Bir başlıkta kalibrasyon yapılmadı; A ile B karışmış (arena 180° ters); zemin işaretleri kaymış; işaret mesafesi sahnedeki `anchor_a`/`anchor_b` mesafesiyle uyuşmuyor | Her başlıkta A+B ile **yeniden kalibre et** (tamamlanmış kalibrasyonda A+B tutmak sıfırlar); bant ölçüsünü sahnedeki değerle karşılaştır. **Kalibratörsüz kabuk lobide örtüşme beklenmez** — kontrolü lobi sahnesinde (`lobbyScene`) ya da arenada yap |
| Avatarlar doğru yerde ama **yükseklikleri yanlış** (yere gömük / havada) | Kalibrasyonda kumanda dik tutulmamış ya da ucu yere değmemiş; `ArenaCalibrator.tipLocalOffset` o kumanda modeli için henüz ölçülmemiş (varsayılan -0.08 m tahmindir) | O başlıkta kumandayı **dik tutup ucunu zemine değdirerek** yeniden kalibre et. Tüm başlıklarda aynı yönde sapma varsa offset yanlıştır: §3'teki reçeteyle ölç, alanı güncelle, APK'yı yeniden al |
| Oyun ortasında arena birden kaydı | Tracking origin değişti (recenter / tracking kaybı sonrası geri kazanım) | Normalde kendini onarır — logda `tracking origin changed, realigning from the saved anchor` satırını ara. Onarım gelmiyorsa kayıtlı anchor yok demektir; A+B ile yeniden kalibre et |
| Vuruş kaydolmuyor | Sunucu reddediyor: dost ateşi, hedef zaten ölü, faz `Live` değil | Sunucu konsolundaki `hit_report reddedildi (…): <sebep>` satırını oku. Atış hızı / silah tablosu denetimi YOKTUR, sebep bunlardan biri olamaz |
| Hasar beklenenden farklı | Başlıklarda farklı APK sürümü var: denge sayıları istemcide yaşar (sunucuda silah tablosu yok) | Tüm başlıklara **aynı APK**'yı kur; denge değişikliği yeni bir istemci build'i gerektirir |
| Maç başlamıyor: "sahne build listesinde yok" / `start_match reddedildi` | Sahne bazı başlıkların APK'sında yok (eski sürüm); sahne Build Settings'te yok/kapalı; sahne adı ≠ katalog anahtarı (typo) | Tüm başlıklara **aynı APK**'yı kur; Build Settings'te sahneyi ekle+işaretle; `MapDefinition.sceneName` ile sahne dosya adını birebir eşitle, `Export Server Config`'i tekrarla |
| Maç `Loading`'de takılıyor | Bir başlık `set_ready` göndermedi (sahne yüklenemedi) | 20 sn sonra sunucu yine de devam eder; konsoldaki `loading zaman aşımı — hazır olmayanlar: …` satırındaki başlığı kontrol et |
| Ölen oyuncu canlanmıyor | 5 sn gecikme dolmadı; oyuncu **kendi takımının** taban bölgesine fiziken girmedi | Oyuncuya doğru tabana (takım rengi) yürümesi söylenir; 20 sn sonunda sunucu zaten zorla canlandırır (`zorla canlandırma` satırı) |
| Ses yok / silah sesi gelmiyor | Başlık sistem sesi kısık; oyuncu kulaklık takmış; silah prefabındaki ses kaynağı/klipler boş; build'de spatializer ayarı bozulmuş | Başlık sesini aç; silah prefabında `WeaponAudio` kaynağı ve klipleri dolu mu bak; ses ayarında spatializer'ın **Meta XR Audio** olduğunu doğrula ve APK'yı yeniden al |
| Oyuncunun ekranı kararıyor, "alan dışı" uyarısı çıkıyor | Oyuncu arena sınırının dışına çıktı; arena boyutu fiziksel alandan büyük girilmiş; kalibrasyon kaymış | Fiziksel alanı yeniden ölç (0.5 m güvenlik payı!); gerekirse arenayı sihirbazla doğru boyutta yeniden üret; kalibrasyonu tekrarla |
