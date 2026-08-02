# İşletme (Venue) Kurulum Kontrol Listesi

Bu liste, VortexArena'yı yeni bir işletmeye kuran ekibin fiziksel alan ölçümünden kabul testine kadar sırayla uygulayacağı adımları içerir.

> Kısaltmalar: **Sunucu PC** = arenayı yöneten Windows bilgisayar · **Başlık** = Meta Quest 3 / 3S gözlük · **Admin** = Windows masaüstü uygulaması (yönetim + izleme) · **Player** = başlıktaki VR uygulaması.
> Teknik referanslar: `Server/README.md` (sunucu), `Docs/ArenaNet-Protokol.md` (portlar/sabitler), `CLAUDE.md` (proje mimarisi).
> Kurulum bittikten sonra **işletme personelinin günlük kullanacağı** kılavuz: `Docs/Kullanim-Kilavuzu.md` (teknik olmayan dille açılış sırası, gizli IP paneli, kalibrasyon, dashboard, sorun giderme).

---

## 1. Ön koşullar

**Fiziksel alan**

- [ ] Serbest (engelsiz) oyun alanını ölç: oyun alanı = ölçülen alan − **0.5 m güvenlik payı** (her duvardan).
- [ ] Standart `A12x12` arenayı olduğu gibi kullanacaksan alan en az **12.5 × 12.5 m** olmalı; daha küçük/asimetrik alanlarda o işletmeye özel arena kurulur (Bölüm 2).
- [ ] Zemin düz, kaygan değil, seviye farkı ve kablo/eşik yok; alan içinde sütun, sabit mobilya, cam yüzey yok.
- [ ] Aydınlatma homojen ve gölgesiz; doğrudan güneş ışığı, güçlü spot ve ayna/parlak yansıtıcı yüzey yok (inside-out takip bozulur, lensler zarar görür).
- [ ] Tavan yüksekliği yeterli (kollar yukarıda serbest hareket edebilmeli).

> **Not:** minimum tavan yüksekliği (öneri ~3 m) ve minimum aydınlatma seviyesi (lux) sahada ölçülüp buraya yazılacak — **doğrulanacak**.
> **Not:** tek renkli/parlak, desensiz zeminlerde takip zayıflayabilir; mat ve dokulu zemin tercih edilir — **doğrulanacak**.
> **Zemin DÜZ olmalı.** Bu bir tercih değil gereksinimdir: (1) serbest dolaşımda koşan oyuncu için eğim düşme riskidir, (2) kalibrasyon zemini tek bir yükseklik olarak alır — eğim telafisi **yoktur** ve bilerek yapılmamıştır (iki nokta bir düzlem tanımlamaz; ayrıca sanal dünyayı eğmek VR'da mide bulantısı yapar). Arena alanı içinde A–B işaretleri arasındaki yükseklik farkı **3 cm'i geçmemeli**.

**Donanım**

- [ ] 1 adet **Sunucu PC** (Windows, **.NET 10 ASP.NET Core Runtime** kurulu — sunucu Kestrel kullandığı için düz .NET Runtime yetmez), **kablolu** Gigabit Ethernet ile AP'ye bağlı (gerekçe: Bölüm 4).
- [ ] 1 adet **AP** (tercihen Wi-Fi 6E / 6 GHz), arenaya özel SSID.

> ### ⚠️ AP seçerken bakılacak spec: nominal hız DEĞİL
>
> **Datasheet'teki "1000 Mbps / AX3000 / 1200 Mbps" rakamı bu iş için anlamsızdır.** O sayı
> **büyük TCP paketleriyle** ölçülür; bu ürünün yükü ise ~1.200 **minik** çerçeve/sn'dir
> (10 oyuncuda toplam ~2,3 Mbps — nominalin binde ikisi, ama tek radyonun airtime'ının %15–20'si →
> `Docs/Sistem-Ozeti.md` §3.12). Bu profilde tıkanan şey radyo değil **AP'nin CPU'su / küçük paket
> iletim hızıdır (pps)** ve o sayı hiçbir datasheet'te yazmaz.
>
> **Bu yüzden seçim ölçütü yönetilebilirliktir.** Aşağıdaki dördü **elle yapılandırılabiliyor mu**
> diye bak — hepsi Bölüm 4'te ayarlanacak maddelerdir ve tüketici router arayüzlerinde çoğu
> kilitlidir:
>
> | Ayar | Neden gerekli |
> |---|---|
> | **OFDMA (DL **ve** UL)** açılabiliyor mu | Bu iş yükü (çok istemci, minik paket) tam OFDMA'nın hedefi; çerçeve başı hava bedelini belirgin düşürür |
> | **Kanal sabitlenebiliyor** mu (otomatik kanal kapatılabiliyor mu) | Maç ortasında kanal değişimi kesinti demektir |
> | **DTIM** ayarlanabiliyor mu | DTIM=1 olmazsa başlığın radyosu uyur ve 20 Hz akışa gecikme ekler |
> | **WMM/QoS** elle yapılandırılabiliyor mu | Poz kanalını (UDP 47822) video/voice sınıfına almak için |
>
> **Sonuç: yönetilebilir sınıf bir AP (Ubiquiti UniFi / TP-Link Omada vb.), tüketici router'ının
> nominal hız etiketinden bu iş için kat kat daha değerlidir.** Ek olarak bu sınıf AP'ler bağlı
> istemci başına airtime/retry istatistiği gösterir — sahada "kimin Wi-Fi'ı kötü" sorusunun tek
> doğrudan cevabı budur.
>
> ⚠️ **Mevcut bir tüketici router'ı kullanılacaksa** yukarıdaki dördünden hangilerinin
> yapılandırılamadığını **teslim paketine yaz** — sahada "neden takılıyor" tartışması çıktığında
> ilk bakılacak yer o listedir.
- [ ] Oyuncu sayısı kadar **Quest 3 / 3S** başlık + kumandalar + şarj istasyonu/powerbank. **Yazılımda eşzamanlı oyuncu sınırı yoktur** — pratik sınırı fiziksel alan (kişi başına güvenli hareket payı) ve AP kapasitesi belirler.
- [ ] USB-C kablo + `adb` (Meta Quest Developer Hub kuruluysa birlikte gelir).
- [ ] **Zemin bandı** (kalibrasyon işaretleri için, kalıcı ve renkli), şerit metre, işaretleme kalemi.

---

## 2. Alanı ölç ve arenayı üret (Unity, ofiste)

- [ ] Fiziksel alanı ölç (metre) ve **alanın köşelerini sırayla yaz**: bir köşeyi başlangıç (0,0)
      kabul edip duvar duvar ilerle. Alan kare değilse (yamuk, L, kırık duvarlı) bu zaten tek yoldur;
      **kare olsa bile dört köşe olarak yazılır** — arenanın ölçüsünü tarif etmenin tek bir yolu
      vardır, "eni-boyu şu kadar" diye kısa bir kip yoktur. Oyun alanı = ölçülen alan − 0.5 m
      güvenlik payı. Odanın içindeki kolon/direkleri de not al (merkez + genişlik/derinlik).
- [ ] **Alanın ölçüsünü boyut dosyasına gir.** Bu dosya işletmenin **tek** ölçü kaynağıdır: hem sahnedeki ölçü maketi, hem oyuncuya çıkan "alan dışına çıktın" uyarısı, hem de yöneticinin kuş bakışı görüntüsü buradan beslenir. Ölçüyü ikinci bir yere yazmazsın.
  > **Dosya nerede:** `Assets/Arenas/Venues/<İşletme>/Data/<İşletme>_dimensions.json` (örnek: `VortexAntep/Data/VortexAntep_dimensions.json`). Düz metin dosyasıdır — sahadan aldığın metreleri **Unity açmadan** girip güncelleyebilirsin.
  > **Dosya İŞLETME başınadır**, arena başına değil: aynı fiziksel odada kaç arena ve lobi oynatılırsa oynatılsın hepsi bu tek dosyayı gösterir. İkinci bir kopya çıkarma — kaçınılmaz olarak birbirinden sapar.
  > **İçine ne yazarsın:** `plane` = alanın çevresini dolaşarak sırayla yazdığın köşeler; `columns` = her kolonun kendi köşe listesi (`points`) + yüksekliği; `calibration` = zemine yapıştıracağın **A ve B** bantlarının yeri (Bölüm 3). **Alan tam kare olsa bile dört köşe** yazılır; girintili/L şeklinde bir alan da aynı tek listeye sığar.
  > Ayrıntılı reçete: `Docs/Gelistirici/Yemek-Kitabi.md`.
- [ ] Unity'de yeni bir sahne aç ve arena kutusuna kaydet: `Assets/Arenas/Venues/<İşletme>/<Arena>/Scenes/<SahneAdı>.unity`.
  > **Mekan klasörü zorunludur** — sunucunun açılışta sorduğu listede görünecek ad odur; aynı işletmenin ikinci arenası da **aynı** mekan klasörünün altına açılır. Sahne adı katalog anahtarıdır ve benzersiz olmalıdır.
- [ ] `Tools > VortexArena > Arena > Template Temellerini Yükle` → sahneye ağ altyapısı (muhafaza + kalibrasyon işaretçileri + rig + poz senkronu) prefab örneği olarak konur ve boyut dosyası muhafazaya bağlanır.
  > Lobi sahnesi kuruyorsan penceredeki **taban bölgeleri** ve **VA_ModeHud** kutularını kapat.
- [ ] `Tools > VortexArena > Arena > JSON'dan DimensionMesh Üret` → boyut dosyasını seç, **Üret**. Sahnede alanın ölçü maketi (taban + kolonlar) belirir; arena sanatını bunun üstüne kurarsın.
  > ⚠️ **Bu maket oyuna girmez** — yalnız ölçü referansıdır. Oyuncunun gördüğü duvar/zemin senin koyduğun environment sanatıdır ve **gerçek duvarlar fiziksel sınırla çakışmalıdır**: sanat duvarı alandan içeride ya da dışarıda durursa oyuncu yanlış yere göre uyarılır.
  > **Ölçü tutmadıysa** maketin köşesini ProBuilder ile yerine taşı, sonra `Tools > VortexArena > Arena > DimensionMesh'i JSON'a Çevir` — düzeltilmiş ölçü aynı dosyaya geri yazılır.
- [ ] **Kalibrasyon noktalarını boyut dosyasına yaz** (Bölüm 3) — sahnedeki işaretçiler oradan yerleşir, elle taşınmaz.
- [ ] **Tek `SpawnPoint`**'i yerine taşı — bu marker arena uzayının sıfırıdır, **zemin seviyesine** konur ve sonradan taşınmaz (taşımak tüm oyuncuların arenadaki koordinatını kaydırır). NavMesh ve ışık verisini bake et.
- [ ] `Tools > VortexArena > Build > Configure All Build Elements` çalıştır → `MapDefinition`, katalog kaydı, Build Settings girdisi ve `Server/config/maps.json` tek geçişte üretilir. Çıkan sağlık raporunu ve uyarıları oku; özellikle "sceneName Build Settings'te YOK / KAPALI" uyarısı varsa düzelt ve tekrar çalıştır.
- [ ] Build Settings'te yeni sahnenin **listede ve işaretli (enabled)** olduğunu doğrula. Sahne adı = `start_match` katalog anahtarı; boşluk/typo dahil birebir eşleşmeli.
- [ ] Android APK'yı **yeniden al**: `scripts\deploy-player-apk.bat` (Unity editörü kapalı) → `deploy\player\game.apk`. Yeni arena APK'da yoksa o başlık maçı engeller (Bölüm 8).

---

## 3. Kalibrasyon işaretleri (zemin bandı)

Arena, her başlıkta **2 nokta** ile fiziksel alana hizalanır (`ArenaCalibrator`). Sahnede iki sanal işaretçi vardır: **`anchor_a`** ve **`anchor_b`** — **yerleri boyut dosyasından gelir**, sahneden değil. Yani zemin bandının nereye çekileceği de bir ölçüdür ve alanın köşeleriyle aynı dosyaya yazılır.

> **Kalibrasyon 6 serbestlik derecesini de kurar:** yönü (yaw) ve yatay konumu A→B çiftinden, **zemin yüksekliğini B noktasında kumandanın ucundan** alır. Zemin yüksekliği gözlüğün kendi "floor level" bilgisinden ALINMAZ — başlıklarda alan kurulumu yapılmadığı için (§5) o değer bir tahmindir: gözlük havadayken açılırsa yanlış başlar, oturum içinde tracking kaybı sonrası kayabilir. Bu yüzden her kalibrasyon zemini de yeniden ölçer.

- [ ] **Noktaları boyut dosyasına yaz**, sahneye değil: `Venues/<İşletme>/Data/<İşletme>_dimensions.json` → `calibration` alanı.
  ```json
  "calibration": {
    "a": { "x": 3.17, "y": 1.82 },
    "b": { "x": 3.17, "y": 7.19 }
  }
  ```
  Koordinatlar `plane` ile **aynı uzaydadır** (metre; JSON'daki `y` = zemindeki ileri eksen), yani alanın köşelerini nasıl ölçtüysen bantların yerini de öyle ölçersin. Dosya **mekan başınadır**: aynı odadaki tüm arenalar ve lobi aynı iki fiziksel işareti kullanır — ikinci bir yere yazma.
  - Sahnedeki `anchor_a` / `anchor_b` objeleri **elle TAŞINMAZ**: `Template Temellerini Yükle` onları bu noktalara oturtur ve başlık da her açılışta aynısını yapar. Sahnede taşımanın kalıcı etkisi yoktur.
  - Nokta seçerken **sabit bir referans** kullan (kolon köşesi, duvar dibi): bant bir gün kayarsa aynı yere geri konabilsin. Örnek: iki kolonun aynı hizadaki köşesinden 10 cm açıkta.
  - **Aralarını olabildiğince aç.** İki nokta arasında en az **0,5 m** olmalı (altındaki çift yok sayılır), ama yön hatası mesafeyle ters orantılı büyür: 5 m'lik bir aralık 1 m'linin beş katı hassasiyet verir.
- [ ] Zemine iki bant işareti yapıştır: **A** ve **B**, dosyaya yazdığın yerlere. Bandın üzerine büyük harfle "A" ve "B" yaz. ⚠️ Aralarındaki mesafe dosyadaki ölçüye **±%20 tolerans** içinde uymalı — dışındaysa başlık kalibrasyonu reddeder (üç kısa titreşim), çünkü yanlış mesafe sessizce bozuk bir hizalama üretirdi.
- [ ] **A → B doğrultusu arenanın yönünü belirler ve sıra ZORUNLUDUR: önce A, sonra B.** Yazılım hangi noktanın önce alındığını geometriden çıkaramaz (iki nokta bunu söylemez, mesafe kontrolü de her iki sırada aynı sonucu verir) — tek güvence senin sıraya uymandır. Karıştırılırsa arena 180° ters döner ve mesafe kontrolü bunu **yakalamaz**. İşaretleri kalıcı ve okunur biçimde etiketle. Başlıktaki doğrulaman şu: **ilk yakalamada tek bir çapa belirir** ve o çapa A'nın üstünde durmalıdır — başka bir yerde beliriyorsa yanlış işaretten başlamışsındır, kombinasyonu bırak ve baştan al.
- [ ] İşaretler kalıcı olmalı (bant + gerekiyorsa zemine dayanıklı işaret); temizlik/mobilya hareketiyle kaymamalı. **Her başlık aynı iki noktayı kullanır.**
- [ ] Ölçüyü (A–B mesafesi, hangi duvara/kolona göre nerede) teslim paketine yaz.
- [ ] Sahneyi kaydet ve APK'yı yeniden al — boyut dosyası çalışma anında okunuyor, yani `calibration` değişikliği yeni bir APK ister.

**Başlıkta kalibrasyon prosedürü** (operatörün her başlıkta yapacağı):

1. Arena sahnesindeyken sağ kumandayı **A** işaretinin üzerine, **yere 90° dik** tutup **ucunu zemine değdir**. Bu duruş her iki noktada da aynı olmalı — zemin yüksekliği bu ölçümden çıkar.
2. Sağ kumandada **A tuşunu basılı tutarken B tuşuna 1 saniye içinde iki kez bas** — **kısa titreşim (0,3 sn)** = A noktası alındı. Basılı tutma süresi yoktur; ucu zemine değdirdiğin anda kombinasyonu yap.
3. Aynısını **B** işaretinde yap — **uzun titreşim (1 sn)** + iki çapa birden belirip **1 saniye sonra kaybolur** = B alındı ve arena hizalandı.
4. Uzun titreşimden sonra **doğrul ve 3 saniye normal duruşta bekle** (çömelme, eğilme, zıplama yok). Oyuncunun avatar boyu tam o anda ölçülür: başlığın zeminden yüksekliği alınır ve avatar o boya ölçeklenir (kısa oyuncuda küçülür, uzun oyuncuda büyür). Eğilmiş hâlde beklenirse avatar olduğundan kısa kalır — düzeltmesi kalibrasyonu sıfırlayıp baştan almaktır.
5. Hata sinyalleri (ikisinde de ikinci çapa hiç belirmez):
   - **Üç kısa titreşim** = iki nokta arasındaki mesafe boyut dosyasındaki `calibration.a`–`calibration.b` mesafesine uymuyor (±%20 dışında); bant ölçüsünü kontrol et, B'yi tekrar al.
   - **Tek titreşim (0,6 sn)** = iki noktanın ölçülen zemin yüksekliği 10 cm'den fazla ayrışıyor; kumanda dik tutulmamış (ya da zemin eğimli). Duruşu düzelt, B'yi tekrar al.
6. Kalibrasyon başlıkta kaydedilir (uzamsal anchor) ve sonraki açılışta **otomatik geri yüklenir**. Yeniden kalibre etmek için kombinasyonu tekrarla — kalibrasyon sıfırdan başlar. ⚠️ Geri yükleme de bir kalibrasyon tamamlanmasıdır: avatar boyu orada da yeniden ölçülür, yani başlığı takan oyuncu sahne açılırken **dik durmalıdır**.

> **Kumanda tutuşu neden önemli:** başlığın takip ettiği nokta kumandanın **ucu değil gövdesinin içindeki pivottur**. Yazılım bu farkı `ArenaCalibrator.tipLocalOffset` ile telafi eder; değer kumanda modeline özgüdür ve **bir kez ölçülür**: alan kurulumu YAPILMIŞ bir gözlükte kumandayı dik tutup yere değdir, `rightControllerAnchor.position.y` değerini oku ve alanın Y'sine (eksi işaretle) gir. Ölçülene kadar varsayılan **-0.08 m** bir tahmindir. Doğrulaması: kalibrasyondan sonra sanal çapa işaretçileri **bir saniye** görünür olur — o pencerede kumandanın ucu çapanın tabanıyla aynı hizada olmalı. Pencere kısadır çünkü işaretçiler kurulum aracıdır, arena dekoru değil; süresi `ArenaCalibrator.markerVisibleSeconds` alanından uzatılabilir.

> Kalibrasyon **işletmenin lobi sahnesinde** yapılır (§5, `server.json → lobbyScene`) — maçtan önce, arenaya girmeden. Lobi sahnesi de bir arena kutusudur: `ArenaCalibrator` ve A/B işaretçileri oradadır, harita değişimi kalibrasyonu sıfırlamadığı için hizalama maça taşınır. ⚠️ **`lobbyScene` yapılandırılmamışsa** oyuncular kalibratörsüz kabuk lobide kalır; o durumda kalibrasyon arena sahnesinde yapılır ve kabuk lobide avatarların fiziksel olarak örtüşmesi beklenmez (normaldir). Bir başlık kalibre olmadan da poz gönderir — ama o pozlar arena ile örtüşmez: avatar kaymış görünür, hareket ettiği yine de izlenir. Örtüşme kalibrasyondan sonra oturur.

> **Kalibrasyon durumu sunucuda tutulur ve operatör tarafından sıfırlanabilir** (`Docs/ArenaNet-Protokol.md` §10.6). Bir başlığın hizalaması sahada kayarsa operatör admin ekranındaki **KAL** düğmesiyle o oyuncuyu savaş dışı bırakır (ateş edemez, hasar yemez, canlanamaz; avatarı diğerlerinin ekranında parlar), oyuncu yeniden kalibre olunca kaldığı yerden devam eder. **Kalibre durumdayken elle kalibrasyon kilitlidir** — oyuncu kendi hizalamasını kazara bozamaz. Operatör yönergesi: `Docs/Kullanim-Kilavuzu.md` §4.1.

---

## 4. Ağ (AP + sunucu PC + firewall)

> **Bu bölümün mantığı:** ürünün trafiği **bant değil airtime** tüketir — 10 oyuncuda ~2,3 Mbps
> (1 Gbps'lik bir AP'nin %0,2'si) ama ~1.200 çerçeve/sn, yani tek radyonun ~%15–20'si. Aşağıdaki
> maddelerin hemen hepsi bant kazanmak için değil **airtime korumak** içindir; sayılar ve gerekçe:
> `Docs/Sistem-Ozeti.md` §3.12.

**Erişim noktası**

- [ ] **6 GHz** yayın (Wi-Fi 6E AP + Quest 3/3S destekliyor). 6 GHz yoksa 5 GHz; **2.4 GHz tamamen kapalı** (ya da başlıkların göremeyeceği ayrı SSID). Gerekçe: 20 Hz'lik düzenli minik paketlerin düşmanı bant değil **girişim**.
- [ ] Kanal genişliği **80 MHz** — 160 MHz'e **çıkma**. Bant zaten %0,2 kullanılıyor; 160 MHz yalnız girişim/DFS yüzeyi ekler. **Dar + temiz > geniş + kirli.**
- [ ] Arenaya özel **tek SSID** (tüm başlıklar ve sunucu PC aynı SSID/subnet'te).
- [ ] **Client isolation / AP isolation KAPALI** (cihazlar sunucuyu görebilmeli — kapalı değilse hiçbir başlık bağlanamaz).
- [ ] Kanal **sabit** (otomatik kanal değil), WMM/QoS **açık**, band steering kapalı.
- [ ] **Tek AP, tek kanal — ikinci AP koyulmaz.** Tek arenalık alanda ikinci AP roaming/handoff kesintisi üretir, karşılığı sıfırdır.
- [ ] **OFDMA (DL + UL) ve airtime fairness açık.** Bu iş yükü (çok istemci, minik paket) tam OFDMA'nın tasarım hedefidir; çerçeve başı bedeli belirgin düşürebilir — ama consumer AP'lerde tutarsız çalıştığı için **garanti sayılmaz**, plan %15–20 airtime üzerinden yapılır.
- [ ] **DTIM = 1.** Daha yükseği başlığın Wi-Fi radyosunu uyutup 20 Hz akışa gecikme ekler.
- [ ] Mümkünse QoS'ta **UDP 47822'yi video/voice sınıfına** al (WMM AC_VI/AC_VO).
- [ ] AP arenanın görüş alanına, oyuncuların üstünde/kenarında konumlandırıldı.

- [ ] ⚠️ **Ağ izole: internet YOK, oyun dışı cihaz YOK.** Sahadaki en büyük tek risk budur — aynı kanaldaki tek bir APK indirmesi / Windows update / telefon senkronu, 10 başlığın airtime'ını yer ve 2,3 Mbps'lik oyun trafiği 900 Mbps'lik bir transferin arkasında kuyruğa girer. Router'ın WAN'ı boş kalsın; misafir/ofis cihazları bu SSID'ye alınmasın.

> **Not:** sabit kanal seçimi (DFS dışı kanal tercihi), WMM ve band steering ayarları saha tecrübesiyle netleşecek — **doğrulanacak**. OFDMA ve DTIM ayarlarının menü yolu AP markasına göre değişir.

**Sunucu PC**

- [ ] ⚠️ **Kablolu (GbE) bağlantı — Wi-Fi'a bırakılmaz.** Sunucu Wi-Fi'daysa her downstream paket havayı **iki kez** geçer (sunucu→AP, AP→istemci) ve airtime ikiye katlanır (~%15–20 → ~%30–40). Bant için değil, **havadaki çerçeve sayısını yarıya indirdiği için** zorunlu sayılır.
- [ ] **Statik IP** (router'da DHCP rezervasyonu tercih edilir); IP'yi teslim paketine yaz. IP değişirse `arena.json` / başlıklardaki kayıtlı adres bozulur.
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
- [ ] Bağlanmıyorsa ~8 sn sonra lobide "sağ kumandada joystick'e 1 sn basılı tut" ipucu çıkar → **joystick'i 1 sn basılı tutarak** gizli IP panelini aç, numpad ile **IP:port** (ör. `192.168.1.10:47821`) gir, **Bağlan**'a bas. Girilen adres beacon'ı **ezer** ve başlıkta kalıcı saklanır; sonraki açılışlarda panel gerekmez.
- [ ] Beacon hiç gelmiyorsa kalıcı çözüm: `Assets/StreamingAssets/arena.json` içine sunucunun statik IP'sini yaz (`{"serverIp":"192.168.x.y","serverPort":47821}`) — **bu dosya APK'nın içindedir, değişiklik yeni APK gerektirir**. Sahada hızlı çözüm her zaman gizli panelden (joystick 1 sn) elle IP girmektir.

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
  bulunur. Yalnız bir mekanda birden çok lobi varsa doldurulur. Maç koşmadığı sürece oyuncular orada bekler: birbirlerini görürler, **kalibrasyonlarını orada yaparlar**, duran silahlardan birini seçip hedef tahtalarına ateş edebilirler — birbirlerine hasar veremeden. Doldurulacaksa değer, odanın ölçüsüne uyan lobi olmalıdır (12×12 → `Lobby12x12`, işletmeye özel ölçü → o işletmenin kendi lobisi) ve seçilen mekanda bulunmalıdır. Mekanda hiç lobi haritası yoksa oyuncular kabuk bekleme ekranında kalır ve kalibrasyonu arenada yapmak zorunda kalırlar.
- [ ] `Server/config/maps.json` dosyasının Bölüm 2'deki export'tan geldiğini doğrula (silah tablosu yoktur — hasarı istemci bildirir).
- [ ] **Dağıtım paketlerini üret** (ofiste, geliştirme makinesinde):
  - `scripts\deploy-server.bat` → `deploy\server\` (self-contained; işletme PC'sine .NET kurmak gerekmez)
  - `scripts\deploy-admin-game.bat` → `deploy\admin\` (**Unity editörü kapalı olmalı**)
  - `scripts\deploy-player-apk.bat` → `deploy\player\` (**Unity editörü kapalı olmalı** + Android Build Support modülü; platform değişen koşu 20-40 dk sürer)
  - `scripts\deploy-launcher.bat` → `deploy\launcher\` (self-contained; tek ön koşul .NET 10 SDK)
  - Klasörlerin **tamamını** kopyala — exe'ler tek başına çalışmaz.
- [ ] Sunucuyu başlat:
  - İşletmede: **launcher'dan** — *1 · Sunucu* bölümünde exe + **mekan** seçili, **Sunucuyu Başlat**. Launcher mekanı `--venue` ile geçtiği için açılışta soru sorulmaz ve yanlış mekan açılmaz.
  - Alternatif (ya da launcher kurulmadan önce): `deploy\server\VortexArena.Server.App.exe` çift tıkla; mekan konsolda sorulur.
  - Geliştirme: `dotnet run --project Server/VortexArena.Server.App`
  - **Launcher sunucuyu kapatmaz** — ömrü operatör uygulamasına bağlı değildir. Kapatmak: sunucunun kendi penceresinde **Ctrl+C**.
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
  - Cihaz `unauthorized` çıkarsa betik kurmadan durur ve sırayla: onayınla `adb kill-server` + `adb start-server` çalıştırıp onay penceresini yeniden tetikler, gözlükte izni verdiğini sorar, cevabı `adb devices` ile teyit eder ve ancak o zaman kurar. Gözlükte pencereyi görmek için başlığı **takmış** olman gerekir (masada ekran kapalıyken çizilmez) ve bir uygulamanın içindeysen önce ana ekrana çık. ⚠️ **"Bu bilgisayardan her zaman izin ver"i işaretle** — işaretlenmezse yetki adb yeniden başladığında düşer ve her kurulumda yeniden sorulur.
  - `adb` bulunamıyorsa Android platform-tools (veya Meta Quest Developer Hub) kur ve PATH'e ekle.
  - Kablosuz kurulum için önce `adb connect <gozluk-ip>:5555`.
  - Aynı anda birden fazla cihaz bağlıysa diğerlerini çıkar.
- [ ] **Tüm başlıklarda aynı APK sürümü** olmalı — sahne listesi farklı olan bir başlık maçın başlamasını engeller (bkz. Bölüm 8).
- [ ] Cihaz kimlikleri: başlık ilk bağlandığında sunucu ona havuzdan rastgele bir **ad** ve 1'den itibaren ilk boş **forma numarasını** (1..99) atar, `Server/config/devices.json`'a (`deviceId → {name, number}`) yazar. Ad tekrar edebilir, **numara tüm kayıtlı cihazlar arasında benzersizdir**.
  - [ ] Admin panelindeki **"Bu cihazı tanıt" (identify)** komutuyla hangi kimliğin hangi fiziksel başlık olduğunu bul, başlığa **numarasını** fiziken etiketle.
  - [ ] Ad/numara değiştirmek: admin **Tercihler → OYUNCU KİMLİĞİ** bölümünden, oyuncu seçiliyken, sunucuyu durdurmadan yapılır. (Elle `devices.json` düzenlemek gerekmez; gerekirse **sunucu kapalıyken** düzenle — UTF-8, **BOM'suz** — ve yeniden başlat.)
- [ ] **Guardian/alan kurulumu YAPILMAZ.** Her başlıkta geliştirici ayarlarından fiziksel alan özellikleri kapatılır; oyuncu tüm alanda serbest dolaşır. Oyun içi güvenlik `ArenaBoundary` ile sağlanır (kenara yaklaşınca ekran hafifçe kararmaya başlar, dışarı çıkınca tümden kararır + uyarı) — **guardian uyarısı olmadığı için tek fiziksel güvenlik ağı budur**, kalibrasyonun doğruluğu bu yüzden bir konfor değil güvenlik meselesidir. ⚠️ Bunun ikinci yarısı **environment sanatının gerçek duvarlarıdır**: oyuncu duvarı gözüyle gördüğü için sanat duvarı fiziksel sınırla çakışmalıdır.
  - Bunun iki sonucu var ve ikisi de yazılımda karşılanmıştır: (1) sistemin zemin seviyesi güvenilmez → kalibrasyon zemini kumandadan ölçer (§3), (2) tracking origin kayması kalibrasyonu bozardı → sahneler **Stage** tracking origin kullanır (sistem recenter'ı kapalı) ve yine de bir kayma olursa `ArenaCalibrator` kayıtlı anchor'dan kendini yeniden hizalar.
- [ ] Uyku/ekran kapanma: başlıkların maç arasında uykuya geçmemesi için ekran/uyku süresi en uzun değere alındı.
- [ ] ⚠️ **Casting / kayıt / streaming KAPALI (her başlıkta).** Cast eden tek bir başlık kendi başına 10–20 Mbps ve ağır airtime tüketir — oyunun tümü ~2,3 Mbps olduğu için bu, tüm maçın gecikmesini bozar (gerekçe: `Docs/Sistem-Ozeti.md` §3.12). Sahada "birden herkes takılmaya başladı" şikayetinde **ilk bakılacak yer budur.** Seans tanıtımı için görüntü isteniyorsa admin PC'nin gözlemci kamerası kullanılır, başlıktan cast edilmez.

> **Not:** Guardian'ın hangi ayardan devre dışı bırakılacağı (geliştirici ayarları / boundaryless) ve uyku süresi menü yolu cihaz yazılımı sürümüne göre değişir — kurulumda ekran görüntüsüyle belgelenecek, **doğrulanacak**.
> **Doğrulanacak (ilk saha testi):** alan kurulumu kapalıyken (1) Stage tracking origin zemin yüksekliğini veriyor mu, (2) `OVRSpatialAnchor` kalıcılığı çalışıyor mu — uygulamayı kapatıp açtığında logda `rig aligned from saved anchor` görünmeli. Görünmüyorsa her gözlük devrinde elle kalibrasyon gerekir.

- [ ] Şarj düzeni: her başlık için sabit şarj yeri ve etiket; seans öncesi pil seviyesi kontrol ediliyor.

> **Not:** sahaya çıkarma için minimum pil eşiği (öneri %50) işletmeyle birlikte kararlaştırılacak — **doğrulanacak**.

---

## 7. Smoke test (kurulum kabulü)

Sırayla uygula; her madde geçmeden sonrakine geçme.

- [ ] **1.** **Launcher** (`deploy\launcher\VortexArena.Launcher.exe`) açıldı; *1 · Sunucu*'da sunucu exe'si + **doğru mekan** seçili, *2 · Bağlantı*'da **Sunucu IP** sunucunun statik IP'si, *3 · Yönetim oyunu*'nda `deploy\admin\VortexArena.exe` seçili.
- [ ] **2.** **Sunucuyu Başlat** → sunucu penceresinde `[Venue] '<Mekan>' yapılandırmadan seçildi` satırı ve açılış özeti doğru (Bölüm 5). *(Sunucu ayrı bir PC'deyse orada elle başlatılır; bu maddede mekanın konsolda doğru seçildiği doğrulanır.)*
- [ ] **3.** Launcher'da **Yönetimi Başlat** → admin uygulaması açıldı ve **IP sormadan** doğrudan **oyuncuların bulunduğu sahneye** düştü (bağlanma ekranında takılı kalmıyor); ortada skor bandı, yanlarda oyuncu listeleri, altta kamera şeridi görünüyor.
- [ ] **4.** İki başlık açıldı → uygulama doğrudan **Lobi**'ye düştü → ikisi de **kendiliğinden** bağlandı (IP girilmedi).
- [ ] **5.** Admin **roster**'ında iki oyuncu da çevrimiçi görünüyor; sunucu konsolunda `[+] Gözlük NN bağlandı` ve `[u] UDP kayıt` satırları var.
- [ ] **6.** Admin **kuş bakışı** kipinde (`3`) iki oyuncu da hareket ediyor; her birinin etrafında **halka**, altında **adı** var (lobide henüz hizalı değiller — normal).
- [ ] **6b.** Kamera kipleri çalışıyor: `1` POV (bir oyuncu seçilip onun gözünden), `2` serbest (WASD/QE + sağ tuş bakış), `3` kuş bakışı. `P` tercihler / `I` istatistik panelleri açılıyor ve **arkada sahne görünmeye devam ediyor**.
- [ ] **7.** Admin **Tercihler** panelinden **mod (tdm) + harita (işletme arenası)** seçildi → **BAŞLAT**. Admin de aynı arena sahnesini yükledi (gözlemci). Sunucu konsolunda `start_match: mod 'tdm', sahne '<Arena>'` satırı görünüyor; iki başlık da arena sahnesini yükledi.
- [ ] **8.** **Her iki başlıkta da kalibrasyon yapıldı** (Bölüm 3 prosedürü, A ve B işaretleri üzerinde) ve uzun titreşimden sonra oyuncu **3 saniye dik durdu** → karşıdakinin avatarı gerçek boyuyla uyumlu görünüyor (boy tam o pencerede ölçülüyor).
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
- [ ] Launcher'da üç bölüm de bir kez dolduruldu (sunucu exe + **mekan**, Sunucu IP, admin exe) ve **Sunucuyu Başlat** + **Yönetimi Başlat** ile doğrulandı; ayarlar `%APPDATA%\VortexArena\launcher\settings.json` içinde kalıcı saklanır (launcher klasörü yeniden dağıtılsa da korunur).
- [ ] Bilgi kartı: SSID + Wi-Fi parolası, sunucu statik **IP:port** (`…:47821`), arena **sahne adı**, A–B zemin işaretleri arası **mesafe**, APK sürümü + kurulum tarihi.
- [ ] Başlık etiketleri ↔ `devices.json` adları eşleşme listesi.
- [ ] Tek sayfalık operatör kartı: **launcher'ı aç → Sunucuyu Başlat → Yönetimi Başlat → başlıkları aç (kendiliğinden bağlanır) → maç başlat → kalibrasyon → maç sonu.**
- [ ] **`Docs/Kullanim-Kilavuzu.md`** (operatörün günlük kullanım kılavuzu — teknik olmayan dille: açılış sırası, gizli IP paneli, kalibrasyon, dashboard kontrolleri, sorun giderme) yazdırılıp işletmede bırakıldı; sondaki kumanda hatırlatma kartı ayrıca yönetim masasına asıldı.

**Sorun giderme**

| Belirti | Olası sebep | Çözüm |
|---|---|---|
| Ekranda **"SUNUCUYA BAĞLANILAMIYOR"** yazıyor (altında adres + `N sn · M. deneme`) | Adres **biliniyor** ama sunucuya erişilemiyor: sunucu exe'si kapalı/çökmüş; firewall engelliyor; cihaz farklı SSID/subnet'te; sunucunun IP'si değişmiş (DHCP) — ekrandaki adres artık yanlış | Ekranda yazan **adresi oku** (sahada ilk teşhis budur): sunucu PC'sinin gerçek IP'siyle aynı mı? 1) sunucuyu başlat, 2) `Server/firewall-kur.cmd`'yi **yönetici** olarak çalıştır, 3) sunucu PC'sinde `netstat -ano` çıktısında **`0.0.0.0:47821`** görün. Bağlantı kurulunca ekran kendiliğinden kaybolur; masaüstünde beklemek istemezsen **"Yeniden Bağlan"**a bas. Ekranın alt satırındaki **"Son hata"** mesajı (ör. bağlantı reddedildi / zaman aşımı) firewall ile kapalı sunucuyu ayırt ettirir |
| Ekranda **"SUNUCU BULUNAMADI"** yazıyor ("adres yok") | Cihazın elinde **hiç adres yok**. Gözlük: beacon gelmiyor ve kayıtlı IP de `arena.json` da yok. Admin: oyun **launcher'sız** (exe'ye çift tıklanarak) açılmış → `--server-ip` gelmemiş | Gözlük: lobide sağ kumandada **joystick'e 1 sn basılı tut** → gizli IP paneli → `IP:port` gir (kalıcı saklanır). Admin: uygulamayı **launcher'dan** başlat (*3 · Yönetim oyunu*'nda admin exe + *2 · Bağlantı*'da Sunucu IP dolu olmalı). Bu ekranda **"Yeniden Bağlan" devre dışıdır** — denenecek adres olmadığı için bilinçlidir |
| Başlık sunucuyu bulamıyor / bağlanamıyor | Firewall'da engelle kuralı; AP'de **client isolation açık**; başlık farklı SSID/subnet'te; PC ağ profili "Genel" | `firewall-kur.cmd`'yi **yönetici** olarak çalıştır; AP'de isolation'ı kapat; SSID'yi kontrol et; ağ profilini **Özel** yap |
| Başlık kendiliğinden bağlanmıyor, "sunucu aranıyor" | AP broadcast'i kesiyor / VLAN ayrımı → beacon gelmiyor | Lobide sağ kumandada **joystick'e 1 sn basılı tut** → gizli IP paneli → **IP:port** elle gir (beacon'ı ezer, kalıcı saklanır); kalıcı çözüm için `StreamingAssets/arena.json` + yeni APK |
| Admin uygulaması "Sunucu adresi yok" diyor (3 sn sonra tam ekran **"SUNUCU BULUNAMADI"**) | Oyun launcher'sız (elle) açılmış — `--server-ip` gelmemiş | Oyunu **launcher'dan** başlat; launcher'da admin exe (*3 · Yönetim oyunu*) ve Sunucu IP (*2 · Bağlantı*) dolu olmalı |
| Launcher "Admin exe bulunamadı" diyor | `deploy\admin\` silinmiş/taşınmış veya build alınmamış | `scripts\deploy-admin-game.bat` (editör kapalıyken) çalıştır, launcher'da exe'yi yeniden seç |
| Launcher **"Mekan seçilmedi"** diyor, sunucuyu başlatmıyor | Bilinçli: mekansız başlatılsa sunucu **alfabetik ilk mekanı** sessizce açardı ve yanlış işletmenin arenaları yönetilirdi | *1 · Sunucu* bölümündeki listeden bu işletmeyi seç. Liste boşsa sunucu exe'si seçilmemiş ya da `config\maps.json` yok → **Yenile** |
| Admin harita seçicisinde **başka işletmenin arenaları** var / beklenen arena yok | Sunucu yanlış mekanla açılmış | Sunucu penceresindeki `[Venue] …` satırını oku. Sunucuyu kapat (Ctrl+C), launcher'da doğru mekanı seçip yeniden başlat — **mekan çalışırken değişmez** |
| Sunucu penceresi açılıp hemen kapanıyor (**çıkış kodu 2**) | Açık sahne çözülemedi: seçilen mekanda lobi haritası yok, ya da `maps.json` eksik/bayat | O mekanın `Lobby` kutusunu ekle (`supportedModeIds = ["lobby"]`), Unity'de **Export Server Config** çalıştır, sunucuyu yeniden dağıt. Launcher listesinde lobisiz mekanlar **kırmızı** görünür |
| Bağlanıyor ama roster'da "çevrimdışı" düşüyor | 15 sn boyunca status gelmedi (Wi-Fi zayıf, başlık uykuya geçti) | AP kapsamasını/kanalı kontrol et; başlıkta uyku süresini uzat |
| **Birden herkesin avatarı takılmaya/zıplamaya başladı** (tek oyuncu değil, hepsi) | Airtime doldu — ağ bandı değil. Sıralı şüpheliler: (1) bir başlık **cast/kayıt** ediyor, (2) ağa oyun dışı bir cihaz/indirme girdi, (3) sunucu PC **Wi-Fi'a** düşmüş (kablo çıkmış / Ethernet devre dışı), (4) komşu ağ aynı kanala oturdu | **Önce ölç, sonra tahmin et:** admin **İstatistikler** panelindeki **PING** kolonuna bak. ① *Herkesin ping'i yüksek* → airtime/ağ: başlıklarda casting'i kapat, router'a bağlı oyun dışı cihazları çıkar, sunucu PC'de `ipconfig` ile **Ethernet'in aktif** olduğunu doğrula (Wi-Fi'daysa airtime iki katına çıkar), gerekirse AP kanalını değiştir. ② *Ping'ler normal ama görüntü takılıyor* → sorun ağda değil: sunucu konsolundaki `[state]` satırında **tik sapma** değerine bak (yüksekse sunucu PC yükleniyor). ③ *Yalnız bir oyuncunun ping'i yüksek* → o başlığın kapsaması/konumu. Gerekçe: `Docs/Sistem-Ozeti.md` §3.12 |
| Admin panelinde bir oyuncunun **PING'i `-`** görünüyor | O başlıkta eski APK var (ölçüm göndermiyor) ya da başlık henüz UDP kaydını tamamlamadı | Birkaç saniye bekle; geçmiyorsa o başlığa güncel APK'yı kur. Karışık APK sürümü zaten desteklenmiyor (protokol v5) |
| Avatarlar fiziksel olarak örtüşmüyor | Bir başlıkta kalibrasyon yapılmadı; A ile B karışmış (arena 180° ters); zemin işaretleri kaymış; işaret mesafesi sahnedeki `anchor_a`/`anchor_b` mesafesiyle uyuşmuyor | Her başlıkta **yeniden kalibre et** (§3'teki kombinasyon; kalibre durumdaysa önce admin ekranındaki **KAL** düğmesiyle sıfırla); bant ölçüsünü sahnedeki değerle karşılaştır. **Kalibratörsüz kabuk lobide örtüşme beklenmez** — kontrolü lobi sahnesinde (`lobbyScene`) ya da arenada yap |
| Avatarlar doğru yerde ama **yükseklikleri yanlış** (yere gömük / havada) | Kalibrasyonda kumanda dik tutulmamış ya da ucu yere değmemiş; `ArenaCalibrator.tipLocalOffset` o kumanda modeli için henüz ölçülmemiş (varsayılan -0.08 m tahmindir) | O başlıkta kumandayı **dik tutup ucunu zemine değdirerek** yeniden kalibre et. Tüm başlıklarda aynı yönde sapma varsa offset yanlıştır: §3'teki reçeteyle ölç, alanı güncelle, APK'yı yeniden al |
| Bir oyuncunun **avatarı boyuna göre çok kısa/uzun** (kendi kolları yanlış yerde, karşıdakiler cüce görünüyor) | Avatar boyu kalibrasyondan 3 sn sonra ölçülüyor; o anda oyuncu hâlâ eğilmiş/çömelmiş ya da elini gökyüzüne kaldırmış olabilir | O oyuncuda admin ekranındaki **KAL** düğmesiyle kalibrasyonu sıfırlat, §3'teki kombinasyonu tekrarlat ve uzun titreşimden sonra **3 saniye dik durmasını** söyle. Boy ölçümü her kalibrasyonda yeniden alınır, tek başına düzelmez |
| Oyun ortasında arena birden kaydı | Tracking origin değişti (recenter / tracking kaybı sonrası geri kazanım) | Normalde kendini onarır — logda `tracking origin changed, realigning from the saved anchor` satırını ara. Onarım gelmiyorsa kayıtlı anchor yok demektir; §3'teki kombinasyonla yeniden kalibre et |
| Vuruş kaydolmuyor | Sunucu reddediyor: dost ateşi, hedef zaten ölü, faz `Live` değil | Sunucu konsolundaki `hit_report reddedildi (…): <sebep>` satırını oku. Atış hızı / silah tablosu denetimi YOKTUR, sebep bunlardan biri olamaz |
| Hasar beklenenden farklı | Başlıklarda farklı APK sürümü var: denge sayıları istemcide yaşar (sunucuda silah tablosu yok) | Tüm başlıklara **aynı APK**'yı kur; denge değişikliği yeni bir istemci build'i gerektirir |
| Maç başlamıyor: "sahne build listesinde yok" / `start_match reddedildi` | Sahne bazı başlıkların APK'sında yok (eski sürüm); sahne Build Settings'te yok/kapalı; sahne adı ≠ katalog anahtarı (typo) | Tüm başlıklara **aynı APK**'yı kur; Build Settings'te sahneyi ekle+işaretle; `MapDefinition.sceneName` ile sahne dosya adını birebir eşitle, `Export Server Config`'i tekrarla |
| Maç `Loading`'de takılıyor | Bir başlık `set_ready` göndermedi (sahne yüklenemedi) | 20 sn sonra sunucu yine de devam eder; konsoldaki `loading zaman aşımı — hazır olmayanlar: …` satırındaki başlığı kontrol et |
| Ölen oyuncu canlanmıyor | 5 sn gecikme dolmadı; oyuncu **kendi takımının** taban bölgesine fiziken girmedi | Oyuncuya doğru tabana (takım rengi) yürümesi söylenir; 20 sn sonunda sunucu zaten zorla canlandırır (`zorla canlandırma` satırı) |
| Ses yok / silah sesi gelmiyor | Başlık sistem sesi kısık; oyuncu kulaklık takmış; silah prefabındaki ses kaynağı/klipler boş; build'de spatializer ayarı bozulmuş | Başlık sesini aç; silah prefabında `WeaponAudio` kaynağı ve klipleri dolu mu bak; ses ayarında spatializer'ın **Meta XR Audio** olduğunu doğrula ve APK'yı yeniden al |
| Oyuncunun ekranı kararıyor, "alan dışı" uyarısı çıkıyor | Oyuncu arena sınırının dışına çıktı; arena boyutu fiziksel alandan büyük girilmiş; kalibrasyon kaymış | Fiziksel alanı yeniden ölç (0.5 m güvenlik payı!); gerekirse mekanın boyut dosyasını düzeltip maketi yeniden üret; kalibrasyonu tekrarla |
