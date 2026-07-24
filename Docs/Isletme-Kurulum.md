# İşletme (Venue) Kurulum Kontrol Listesi

Bu liste, VortexArena'yı yeni bir işletmeye kuran ekibin fiziksel alan ölçümünden kabul testine kadar sırayla uygulayacağı adımları içerir.

> Kısaltmalar: **Sunucu PC** = arenayı yöneten Windows bilgisayar · **Başlık** = Meta Quest 3 / 3S gözlük · **Admin** = Windows masaüstü uygulaması (yönetim + izleme) · **Player** = başlıktaki VR uygulaması.
> Teknik referanslar: `Server/README.md` (sunucu), `Docs/ArenaNet-Protokol.md` (portlar/sabitler), `CLAUDE.md` (proje mimarisi).

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

- [ ] 1 adet **Sunucu PC** (Windows, **.NET 8** kurulu), tercihen kablolu Gigabit Ethernet ile AP'ye bağlı.
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

- [ ] Tercihen **kablolu (GbE)** bağlantı + **statik IP**; IP'yi teslim paketine yaz.
- [ ] Windows ağ profili **Özel (Private)**.
- [ ] `Server/firewall-kur.cmd` dosyasına **sağ tık → "Yönetici olarak çalıştır"**. Betik, Windows'un otomatik eklediği **ENGELLE** kurallarını siler ve şu portlara **İZİN** verir:
  - **TCP 47821** — WebSocket kontrol kanalı (`/ws`)
  - **UDP 47820** — keşif beacon'ı
  - **UDP 47822** — poz/state kanalı
- [ ] Sunucu ilk açılışta Windows "izin ver?" sorusu gösterirse **"İzin ver"e** bas. İptal edilirse kalıcı engelle kuralı oluşur → `firewall-kur.cmd`'yi tekrar çalıştır.

**Ağ doğrulaması**

- [ ] Sunucu açıkken bir başlığı lobiye al: adres alanı **beacon ile kendiliğinden dolmalı**.
- [ ] Dolmuyorsa lobideki numpad ile **IP:port** (ör. `192.168.1.10:47821`) elle gir → elle girilen adres beacon'ı **ezer** ve başlıkta kalıcı olarak saklanır.
- [ ] Beacon hiç gelmiyorsa kalıcı çözüm: `Assets/StreamingAssets/arena.json` içine sunucunun statik IP'sini yaz (`{"serverIp":"192.168.x.y","serverPort":47821}`) — **bu dosya APK'nın içindedir, değişiklik yeni APK gerektirir**. Sahada hızlı çözüm her zaman lobide elle IP girmektir.

---

## 5. Sunucu kurulumu

- [ ] `Server/config/server.json` dosyasını düzenle — genelde yalnız **`venueName`** işletme adına çekilir, portlar varsayılan kalır:
  ```json
  { "controlPort": 47821, "beaconPort": 47820, "statePort": 47822, "venueName": "<İşletme Adı>", "tickHz": 20 }
  ```
- [ ] `Server/config/weapons.json` ve `Server/config/maps.json` dosyalarının Bölüm 2'deki export'tan geldiğini doğrula (hasar **her zaman** sunucudaki tablodan uygulanır).
- [ ] Sunucuyu başlat:
  - Geliştirme/kurulum: `dotnet run --project Server/VortexArena.Server.App`
  - Derlenmiş exe: `Server/VortexArena.Server.App/bin/Debug/net8.0/VortexArena.Server.App.exe`
  - Admin uygulamasının launcher ekranı da bu exe'yi başlatır (varsayılan yol `Server/VortexArena.Server.App/bin/Release/net8.0/VortexArena.Server.App.exe` — kurulumdaki gerçek yola göre düzelt).
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

- [ ] **1.** Sunucu PC'de sunucu çalışıyor, açılış özeti doğru (Bölüm 5).
- [ ] **2.** Admin uygulaması açıldı, sunucuya bağlandı, **dashboard** ekranı geldi.
- [ ] **3.** İki başlık açıldı → uygulama doğrudan **Lobi**'ye düştü → ikisi de bağlandı.
- [ ] **4.** Admin **roster**'ında iki oyuncu da çevrimiçi görünüyor; sunucu konsolunda `[+] Gözlük NN bağlandı` ve `[u] UDP kayıt` satırları var.
- [ ] **5.** Admin **taktik (üstten) görünümde** iki oyuncu da hareket ediyor (lobide henüz hizalı değiller — normal).
- [ ] **6.** Admin panelinden **mod (tdm) + harita (işletme arenası)** seçildi → **Maçı Başlat**. Sunucu konsolunda `start_match: mod 'tdm', sahne '<Arena>'` satırı görünüyor; iki başlık da arena sahnesini yükledi.
- [ ] **7.** **Her iki başlıkta da kalibrasyon yapıldı** (Bölüm 3 prosedürü, A ve B işaretleri üzerinde).
- [ ] **8.** İki oyuncu fiziksel olarak **yan yana / aynı noktada** durdu → birbirlerinin avatarını doğru yerde görüyor; admin taktik görünümünde de iki nokta üst üste geliyor. (Örtüşme yoksa Bölüm 8'e bak.)
- [ ] **9.** Maç `Countdown (5 sn)` → `Live` geçişini yaptı; iki başlıkta da skor/can HUD'ı görünüyor.
- [ ] **10.** Bir oyuncu diğerine ateş etti → **can azaldı**, admin panelinde **skor + kill-feed** güncellendi; sunucu konsolunda `öldürme: … — skor kırmızı N : mavi N` satırı var.
- [ ] **11.** Ölen oyuncu ölüm ekranını gördü → **5 sn** sonra kendi takım tabanına (`BaseZone`) fiziken girdi → **canlandı** (`canlandı: Gözlük NN`). Zorla canlandırma satırı (`zorla canlandırma`) görünüyorsa taban/ölüm akışını gözden geçir.
- [ ] **12.** Maç süresi/skor limiti dolunca `maç sonu — kazanan: …` yayınlandı; 10 sn sonra tüm başlıklar **lobiye döndü**.
- [ ] **13.** Bir başlığı kapat-aç → kendiliğinden yeniden bağlandı ve roster'da göründü.
- [ ] **14.** İşletme personeline: sunucu başlatma/kapatma, maç başlatma, kalibrasyon ve sorun giderme tablosu bir kez uygulamalı gösterildi.

---

## 8. Teslim paketi ve sorun giderme

**Teslim paketi (işletmede kalacaklar)**

- [ ] Sunucu PC'de: `Server/` klasörü (exe + `config/server.json`, `weapons.json`, `maps.json`, `devices.json`) ve masaüstünde admin uygulaması kısayolu.
- [ ] Bilgi kartı: SSID + Wi-Fi parolası, sunucu statik **IP:port** (`…:47821`), arena **sahne adı**, A–B zemin işaretleri arası **mesafe**, APK sürümü + kurulum tarihi.
- [ ] Başlık etiketleri ↔ `devices.json` adları eşleşme listesi.
- [ ] Tek sayfalık operatör kartı: sunucuyu başlat → başlıkları aç → admin'den maç başlat → kalibrasyon → maç sonu.

**Sorun giderme**

| Belirti | Olası sebep | Çözüm |
|---|---|---|
| Başlık sunucuyu bulamıyor / bağlanamıyor | Firewall'da engelle kuralı; AP'de **client isolation açık**; başlık farklı SSID/subnet'te; PC ağ profili "Genel" | `firewall-kur.cmd`'yi **yönetici** olarak çalıştır; AP'de isolation'ı kapat; SSID'yi kontrol et; ağ profilini **Özel** yap |
| Beacon gelmiyor, adres alanı boş | AP broadcast'i kesiyor / VLAN ayrımı | Lobide **IP:port** elle gir (beacon'ı ezer, kalıcı saklanır); kalıcı çözüm için `StreamingAssets/arena.json` + yeni APK |
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
