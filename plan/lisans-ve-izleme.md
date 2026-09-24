# Lisans ve izleme — yapılacak iş

Her işletmeye süreli lisans, gözlük limiti ve maç bütçesi; kurulumun başka yerde çalıştırılmasını,
gözlüğün ya da APK'nın kopyalanmasını engelleyen katmanlı koruma; oynanan maçların merkezde toplanıp
analiz edilmesi. Bugünkü OTA dağıtımı (`updater/README.md`) bu işle **tamamen değişir**.

İlk teslimatta kullanım kayıtları **istatistik içindir**; oyun başına ücret düşülmez. Buna rağmen
kayıt zorunludur ve lisans denetiminin parçasıdır. İleride oyun bazlı lisans aynı kayıt defterini
kullanır; ücretlendirme politikası ayrıca açılır. Bu dosya uygulama planıdır, korumalar henüz yoktur.

**Koltuk:** lisansın izin verdiği bir gözlük yeri. Gözlük limiti 8 olan lisansta 8 koltuk vardır;
aktive edilen her gözlük birini doldurur.

## Karar özeti (yeniden açılmaz)

### Kimlik

- Koltuk sayacı ve aktif cihaz listesi **backend'de** tutulur; işletmedeki her şey onun imzalı
  kopyasıdır. Kopyalanan bir kurulum koltuk bulamaz.
- Gözlük kimliği **ANDROID_ID**'dir. Gerçek seri numarası uygulama içinden okunamaz: Android 10+ onu
  ayrıcalıklı izne ya da device owner'a bağlar; Quest'te `Build.SERIAL` "unknown", `ro.serialno` boş
  döner. Device owner yolu gözlükteki tüm Meta hesaplarının silinmesini ister ve MDM'lerle
  (ArborXR, ManageXR, Meta HMS) çakışır — kullanılmaz.
- ⚠️ ANDROID_ID **imza anahtarı + kullanıcı + cihaz** başınadır: paket adına bağlı değildir, uygulama
  silinip kurulunca değişmez, fabrika ayarında değişir. Bu yüzden **updater ve oyun TEK keystore ile
  imzalanır** — farklı anahtarla aynı gözlükte farklı kimlik görürler ve aktivasyon eşleşmez.
  `com.vortex.arenav<N>` sürüm paketleri aynı anahtarla aynı kimliği görür.
- ⚠️ **Keystore kaybı geri dönüşsüzdür:** güncelleme imzası tutmaz → uygulama kaldırılır →
  ANDROID_ID değişir → bütün işletmelerin bütün koltukları gider. Keystore ve imza özel anahtarı iki
  ayrı çevrimdışı yedekte durur.
- Gerçek seri numarası yalnız **envanter etiketi**dir (portaldaki kayıt kayışın içindeki etiketle
  eşleşsin diye): updater'ın ilk USB kurulumunda `adb get-serialno` ile okunur. Güvenlik değeri yoktur.

### Aktivasyon

- Lisans anahtarı (`VTX-XXXX-XXXX-XXXX`, Crockford Base32 + checksum — telefonda okunur, `0/O` `1/I`
  karışmaz) **yalnız sunucu PC'sine**, bir kez girilir. Gözlüğe hiç gitmez.
- Lisans başına **tek aktif sunucu kurulumu**; yeni PC aktive edilince eskisi bir sonraki
  heartbeat'te düşer. PC kimliği donanım parmak izidir (anakart UUID, disk seri, fiziksel NIC MAC,
  `MachineGuid`) ve **5'te 3 eşleşme** yeter — tek parça değişimi işletmeyi kilitlemez.
- Gözlük **kısa kodla** aktive edilir: launcher'da "Gözlük Ekle" → backend 6 haneli, 10 dk geçerli,
  tek kullanımlık kod üretir → gözlükte updater'a girilir (gözlük o an **internette olmak zorunda**) →
  ANDROID_ID kaydolur, koltuk düşer, imzalı cihaz token'ı iner.
- Aynı ANDROID_ID yeniden aktive olursa koltuk düşmez, yalnız token yenilenir — yoksa her güncelleme
  bir koltuk yerdi.
- **Koltuk serbest bırakma** (bozulan, değişen ya da fabrika ayarına dönen gözlüğün kaydını silip
  yerini açmak): operatör launcher'daki cihaz listesinden **ayda 2** kez yapar, fazlası destekten
  (panelde sınırsız). Sınırsız olsa 8 koltuk sırayla 16 gözlükte döndürülebilirdi.

### Lease, süre ve maç bütçesi

- Sunucunun güvendiği şey yıllık lisans değil, ondan türetilen kısa ömürlü **lease**'dir: `tenantId`,
  `installationId`, `ledgerEpoch`, `notAfter`, `matchBudget`, aktif cihaz listesi, `leaseSeq`,
  dayandığı merkez makbuzu ve `kid`.
  `notAfter = min(şimdi + ~10 gün, lisans bitişi)`.
- Lease iki bütçe taşır, hangisi önce biterse: **süre** ve **maç sayısı** (`matchBudget`, varsayılan
  70 = günde 10 × 7; panelden işletme başına ayarlanır). Maç bütçesi saat geri alma savunmasıdır —
  sayaç zamandan bağımsızdır.
- Lease bütçesi yalnız çevrimdışı çalışma sınırıdır; satın alınmış oyun bakiyesi değildir.
  Daha yüksek `leaseSeq` ile verilen yeni lease yalnız kendi kullanım sayacını başlatır; aynı
  lease tekrar indirilirse sayaç sıfırlanmaz. Ömür boyu toplam ve kullanım kayıtları **hiçbir lease
  yenilemesinde sıfırlanmaz**. Yenileme, önce kayıtların merkezle mutabakatını gerektirir.
- Sayılan olay sunucuda doğrulamalardan geçmiş, diske kaydedilmiş **maç başlatma kabulüdür**;
  gelen her `start_match` paketi değildir. Gözlük aynı `matchId` için en fazla bir kez sayar;
  sahne ön yüklemesi, yeniden bağlanma, lobi, kalibrasyon ve turnuvanın alt turları yeni maç sayılmaz.
- Eşikler — uyarı **her yerde** görünür (sunucu konsolu, launcher, admin paneli, gözlük lobisi):

  | Eşik | Davranış |
  |---|---|
  | 50 maç ya da 3 gün kala | Sarı: "Lisans yenilemesi için sunucu PC'sini internete bağlayın" |
  | 63 maç ya da 1 gün kala | Kırmızı, admin panelinde kalıcı |
  | 70 maç ya da süre bitti | Yeni maç başlamaz; süren maç biter |

  Uyarı 70'te değil 50'de başlar: yoğun bir günde 30 maç oynanır, operatöre bağlanacak zaman kalmalı.
- **Tam kilit:** açılışta önce kullanım defteri doğrulanır; yoksa/bozuksa sunucu **çıkış kodu 3**
  ile kapanır. Sağlam defter varsa internetten mutabakat ve lease yenilemesi denenir; geçerli
  lease yoksa yine kapanır. İnternet yokken sağlam defter + geçerli lease ile çalışır.
  Çalışırken bütçe ya da süre biterse yalnız yeni maç reddedilir.
- Lease, sayaç ve maç kayıtları aynı korunan yerel veritabanında durur. Dosya silme/değiştirme
  sonrasında normal heartbeat, yeniden kurulum veya lisans anahtarını tekrar girme **boş defter
  oluşturamaz**. İlk aktivasyon ile destek denetimindeki kurtarma ayrı yetkilerdir.
- Saat geri alma ayrıca **yüksek su seviyesiyle** yakalanır: görülen en ileri zaman saklanır, saat
  onun gerisine düşerse yeni lease gelene kadar yeni maç reddedilir.

### İşletme istatistiği ve korunan yerel kullanım defteri

**Sorumluluk:** `Server/` işletmedeki oyun sunucusudur ve kayıtların tek yazıcısıdır; `Backend/`
bizim merkezimizdir, imzalı lisans/kurtarma yetkisi verir ve kayıtları toplar. Launcher yalnız
sunucunun yerel, erişim denetimli IPC arayüzünden durum okur/işlem ister; dosyayı değiştirmez.
Oyun sunucusu kapalıyken aktivasyon/kurtarma için aynı sunucu bileşeninin **bakım modu** çalışır;
oyun portlarını açmaz. Admin ve gözlükler doğrudan deftere yazamaz.

#### Hangi istatistikler tutulur?

- İşletme/şube/kurulum bazında başlatılan, gerçekten oyuna geçen, tamamlanan, iptal edilen ve
  yarıda kalan maç sayısı; gün/saat yoğunluğu; oyun modu/harita dağılımı; oyuncu katılımı ve süre.
  Sekiz kişinin oynadığı bir maç **1 maç, 8 katılım**dır. Turnuvanın turları ayrı maç sayılmaz.
- Her maçta `matchId`, `tenantId`, `branchId`, `installationId`, `ledgerEpoch`, `leaseSeq`, mod,
  harita/mekan, sunucu/oyun sürümü, başlangıçtaki ve toplam benzersiz katılımcı sayısı, skor özeti,
  kopma sayısı ve bitiş nedeni bulunur. Katılımcı kimliği yalnız maç içinde geçerli takma kimliktir;
  oyuncu adı, hesap ve hareket/poz akışı toplanmaz. Cihaz envanteri ayrı yetkili tabloda tutulur.
- Olaylar: `MatchStartCommitted` (başlatma kabulü), `MatchBecameLive`, `MatchFinished`,
  `MatchAborted`, `MatchInterrupted`. Başlangıç ve bitiş aynı satırı ezmez; olay eklenir,
  `MatchRecords` bu olaylardan üretilen raporlama görünümüdür. Düzeltme de yeni olaydır.
- Zaman: PC'nin `occurredAtUtc` değeri + o anki işletme saat dilimi/UTC ofseti + backend'in
  `receivedAtUtc` değeri + son imzalı merkez zaman referansı + `timeConfidence` saklanır.
  Aktif süre aynı süreçte monotonik saatle ölçülür; duraklama süresi ayrıdır. Çökme sonrası
  kesin bitiş/süre bilinmiyorsa bilinmiyor olarak raporlanır; yeniden açılış saati bitiş yapılmaz.
  Çevrimdışı PC saati kesin zaman kanıtı değildir; şüpheli saatler raporda işaretlenir.

#### Yerel dosya ve anahtarlar

- Yol: `%ProgramData%\VortexArena\lisans\usage.db`. SQLite, lisans durumu ile olay eklemesini
  **aynı transaction** içinde tutar. Önerilen ilk yapı `journal_mode=DELETE` + `synchronous=FULL`;
  günlük/yan dosyalar da korunur, çalışma anında yalnız `.db` kopyalamak yedek sayılmaz.
  Depolamanın flush davranışı ve elektrik kesintisi testleri teslimat şartıdır.
- Dosya uzantısını gizlemek koruma sayılmaz. Olay içeriği AES-256-GCM ile şifrelenir; sürümlü ve
  deterministik bayt kodlaması kullanılır. `tenantId`, `installationId`, `ledgerEpoch`, `eventSeq`,
  olay türü ve önceki olay özeti doğrulanan başlığa bağlanır. Her şifreleme için CSPRNG'den yeni
  96-bit nonce üretilir; geri alınabilen sıra sayacı tek başına nonce olamaz. Anahtar başına
  kullanım limiti/rotasyon belirlenir. SQLite şeması görülebilir; içerik değişikliği kabul edilmez.
- Her olayın özeti önceki olaya bağlanır (`prevHash`); kimliği
  `(installationId, ledgerEpoch, eventSeq)` olur. Durum kaydı `headHash`, `eventSeq`,
  `lifetimeStarts`, lease kullanımı, saat yüksek su seviyesi, açık maç ve son merkez makbuzunu
  taşır; ayrı anahtarla HMAC-SHA-256 doğrulaması yapılır. AEAD/HMAC yalnız seçili alanları değil,
  güvenlik kararında kullanılan bütün alanları kapsar. Zincirin sonu ve sayaçlar karşılaştırılır.
- İşletme kurulumunda ayrı, rastgele veri anahtarı ve kurulum kimlik anahtarı üretilir; tüm
  müşterilere ortak EXE anahtarı konmaz. Donanım parmak izi **gizli anahtar değildir** ve ondan
  anahtar türetilmez. Tercih TPM ile korunan anahtar ve dışa aktarılamayan kurulum imza anahtarıdır.
  Desteklenmeyen PC için yalnız istatistik aşamasında ayrı servis hesabı altında DPAPI + ACL
  seçeneği değerlendirilebilir; daha zayıf koruma seviyesi merkezde görünür.
- Sunucu ayrı Windows servis hesabıyla çalışır; dosya/anahtar izinleri bu hesaba verilir.
  `DPAPI LocalMachine` tek başına erişim denetimi değildir: aynı makinedeki kullanıcılar blob'a
  erişirse çözebilir. Yerel yönetici, süreç belleğine/koduna müdahale edebilir; TPM'de anahtar
  saklamak da tek başına değiştirilmiş programı güvenilir yapmaz.
- Backend'in lisans/kurtarma **özel imza anahtarı PC'ye gelmez**. Yerel veri doğrulama anahtarı,
  kurulum kimlik anahtarı ve merkez imza anahtarı ayrı amaçlara sahiptir. Anahtar kaybı veya TPM
  sıfırlama yeni otomatik aktivasyon değildir; kurtarma gerektirir.

#### Maç başlatma ve çökme tutarlılığı

1. `MatchDirector.StartMatchAsync` içindeki mod/harita/istemci kontrolleri tamamlanır; reddedilen
   istek istatistik veya bütçe tüketmez. Kabul süreci tek sıra üzerinden yürür; aynı anda iki
   admin isteği bütçe denetimini yarışarak geçemez. `startRequestId` tekrar denemeyi tekilleştirir;
   `matchId` sunucuda üretilir. Aynı kimlikte farklı içerik reddedilir.
2. Defter, lisans, süre, bütçe ve geri alma göstergeleri kontrol edilir. Tek transaction içinde
   `MatchStartCommitted`, toplam/lease sayaçları ve yeni zincir başı kalıcılaştırılır. Yeni maç
   mevcut maçı değiştiriyorsa önceki maçın iptali de aynı işlemde kayda girer.
3. **Commit diske gitmeden** maç durumu değiştirilmez, `load_match`/oyun izni gönderilmez.
   Yazma hatası, disk doluluğu veya dosyaya erişilememe yeni maçı engeller. Gözlük `matchId`
   üzerinden tekilleştirir; ön yükleme mesajıyla gerçek maç izni protokolde ayrılır.
4. Commit sonrası ağ gönderimi veya süreç çökerse başlatma kaydı korunur. Sonraki açılışta
   kapanmamış maç `MatchInterrupted` ile kapatılır; tekrar sayılmaz, sayaç geri verilmez.
   Bu nedenle başlatılan ve gerçekten oynanan maç raporları ayrıdır. İleride teknik arıza iadesi
   yalnız backend'in imzaladığı ayrı düzeltme ile yapılır; operatör geçmişi silemez.
5. Bitiş/iptal olayı skorlar sıfırlanmadan yakalanır. Yaşam döngüsü sıralayıcısı disk yazımını
   oyun tick'inin `_gate` kilidi dışında yapar; eşzamanlı başlatma/bitirme yollarını birlikte
   yönetir. Kayıt hatasında yeni maçlar kilitlenir; devam eden maçın bitmesine izin verilir,
   tamamlanma kaydedilemiyorsa sonucun belirsiz olduğu gösterilir.

#### Her açılışta ve çalışma sırasında doğrulama

- Oyun portları/beacon açılmadan dosyanın varlığı, şema sürümü, anahtar erişimi, işletme/kurulum
  eşleşmesi, bütün tutulan olayların AEAD doğrulaması ve zinciri, durum HMAC'i, sayaçların olaylarla
  uyumu, merkez makbuzu ve varsa donanım sayacı kontrol edilir. SQLite'ın bütünlük kontrolü
  kriptografik doğrulamanın yerine geçmez. Çok büyüyen defter için onaylı arşivleme aşağıdadır.
- İlk kurulumda defteri sadece backend'in tek kullanımlık, kurulum anahtarına bağlı imzalı
  `GenesisGrant` yetkisi oluşturur. Backend aktivasyon geçmişini tutar: kurulu lisansın dosyası
  kayboldu diye ikinci genesis vermez. Yarım kalan ilk aktivasyon aynı isteği idempotent sürdürür.
- Her maç öncesinde durum/zincir başı/lease tekrar denetlenir; çalışırken periyodik tam tarama
  yapılır. Dosya kilidiyle ikinci yazıcı engellenir; dosyanın silinmesi/değiştirilmesi ayrıca
  izlenir. Dosya izleyicisi tek başına güvenlik kontrolü değildir.
- Eksik dosya, bozuk etiket/zincir, eski duruma dönüş veya anahtar kaybı `recovery_required`
  üretir. Açılışta kod 3; çalışırken yeni maç yasağı ve operatöre açık hata nedeni. Bozuk dosyayı
  yeniden oluşturma, otomatik eski yedeğe dönme veya internet geldi diye sessiz düzeltme yoktur.

#### İnternet gelince merkezle eşitleme

- Açılışta, bağlantı geri gelince ve örneğin 5 dakikalık heartbeat'te onaylanmamış olaylar sınırlı
  paketlerle gönderilir; hata halinde artan bekleme uygulanır. Ağ işlemleri maç başlatma
  transaction'ını bekletmez. Kurulum kimlik doğrulaması TLS üstündedir; paket özeti kurulum
  anahtarıyla imzalanır. Backend kimliği oturumdan bağlar, gönderilen `tenantId`'ye kör güvenmez.
- Backend, genesis/son makbuzdan itibaren kesintisiz sırayı, aynı kanonik içerikten hesaplanan
  zinciri, olay geçişlerini, sayaçları ve lease ilişkisini doğrular. Aynı olay + aynı içerik
  tekrar gelirse tek kayıt olur; aynı sıra + farklı içerik veya sıra boşluğu bütünlük hatasıdır.
  Paket kısmen geldiyse yalnız doğrulanıp transaction ile saklanan kesintisiz önek onaylanır.
- Cevap, backend imzalı makbuzdur: `installationId`, `ledgerEpoch`, `ackSeq`, `headHash`,
  `lifetimeStarts`, `receiptSeq`, `serverUtc`. Yerel ACK işareti tek başına kayıt sildiremez.
  Cevap kaybolursa aynı paket tekrar yollanır; çift sayım olmaz. Geç gelen eski makbuz ilerlemeyi
  geri alamaz. Backend verisi ve makbuz üretimi tutarlı transaction/outbox ile yürütülür.
- Lease yenileme isteği sabit bir defter başına bağlanır. O noktaya kadarki olaylar doğrulanmadan
  yeni bütçe verilmez; yenileme sırasında gelen maçlar da kaybolmadan eski lease'e yazılır.
  Yeni lease ve eski/yeni sayaç geçişi tek yerel transaction'dır. Aynı yenileme isteği aynı
  lease'i döndürür; tekrar denemek ek bütçe üretmez.
- Onaylanmayan olaylar **asla otomatik silinmez**. Merkezce alınmış önek ancak imzalı arşiv
  checkpoint'i diske atomik yazıldıktan sonra budanabilir; kalan zincir checkpoint'e bağlanır,
  toplamlar korunur. İlk sürümde budama kapalıdır; disk eşiği yaklaşınca uyarılır, yer kalmazsa
  yeni maç durur. Yedek, veritabanının tutarlı yedek API'siyle alınır.
- Merkez paneli son eşitleme zamanını, kapsanan son olayı ve veri güncelliğini gösterir. İnternet
  yokken merkez sonradan oynananları henüz bilemez; eksik günler **0 maç** gibi sunulmaz.

#### Dosyayı eski kopyasına döndürme ve oyun bazlı lisans

- HMAC/AEAD değiştirmeyi yakalar; eski **geçerli** dosya, anahtar ve saat durumunu birlikte geri
  getirmek imzayı bozmaz. Registry'ye veya ikinci dosyaya aynı sayacı koymak, hatta anahtarı TPM'de
  saklamak tek başına bu sorunu çözmez. Merkez yalnız daha önce gördüğü ilerlemeye göre gerilemeyi
  ya da çatalı tespit edebilir; hiç gönderilmemiş ve sonra silinmiş olayları sihirli biçimde bilemez.
- İlk istatistik sürümünde bu sınır kabul edilir ve panelde koruma seviyesi görünür. Kısa lease,
  merkez makbuzu ve sayaç mutabakatı kullanılır; **tam çevrimdışı geri alma koruması var** denmez.
- Çevrimdışı oyun bazlı lisansın ön şartı: diskin dışında geri alınamayan, fiziksel TPM NV sayacı
  veya güvenli donanımda eşdeğer tüketim kanıtı. Donanım/Windows API desteği, yetkilendirme,
  yazma ömrü/gecikmesi ve temizleme saldırısı prototiple ölçülmeden bu lisans türü açılmaz.
  VM/vTPM snapshot'ı fiziksel sayaçla eşdeğer kabul edilmez. Alternatif, her maçta merkezden
  çevrimiçi tüketim iznidir; bağlantı yoksa bu lisans türünde maç başlayamaz.
- Donanımlı akışta önce diske imzalı/korunan bir hazırlık kaydı, sonra donanım tüketim adımı,
  sonra yerel kesinleştirme gelir; oyun izni en son verilir. Güç kesintisindeki her ara durum için
  tekrar oynama hakkı üretmeyen kurtarma tablosu tasarlanır. Sayaç ve disk atomik transaction
  değildir; uyuşmazlıkta belirsiz tüketim korunur ve gerekirse destek kilidi uygulanır.
- Oyun kredisi backend'de tutulur; çevrimdışı kota verilirken kredi **rezerve edilir**. Lease
  yenilemek satın alınmış bakiyeyi doldurmaz; üst üste verilen kotalar aynı krediyi harcayamaz.
  Veri kaybında rezerve edilmiş kotanın harcanmayan kısmı kanıtsız iade edilmez. Başlatma kabulü
  varsayılan tüketim birimidir; iptal/teknik arıza iadesi ayrı, denetlenebilir merkez kararıdır.

#### Silme, arıza, PC değişimi ve kurtarma

- Launcher, “Kullanım kaydı bulunamadı/doğrulanamadı; sunucu başlatılmadı” mesajıyla destek
  referansı gösterir. Bakım modu yalnız tanılama/aktivasyon/kurtarma sunar; oyun başlatamaz.
- Backend'de kurtarma kaydı açılır: sebep, eski kurulum/defter başı, bilinen tüketim, olası kayıp
  aralığı ve yetkili işlemi saklanır. Destek onayıyla tek kullanımlık, hedef kurulum anahtarına,
  eski/yeni epoch'a ve korunacak sayaçlara bağlı imzalı `RecoveryGrant` verilir.
- Sağlam yedek merkez/donanım ilerlemesiyle uyumluysa geri yüklenir. Merkeze hiç gitmemiş kayıt
  kaybolmuşsa gerçek tarih/skor yeniden üretilemez; raporda veri kaybı aralığı görünür, toplam
  tahmini gerçek maç kaydı gibi gösterilmez. Yeni epoch eski geçmişe bağlı açılır; işletme
  toplamı ve varsa kredi borcu sıfırlanmaz. Eski kurulum/epoch merkezde emekliye ayrılır.
- PC değişimi de bu akıştır. Eski PC çevrimdışıysa iptali hemen öğrenmez; aynı lisansın iki
  yerde çalışması ancak eski lease bitince veya eski cihaz çevrimiçi olunca durdurulabilir.
  Ücretli çevrimdışı kota eski cihazdan geri alınmış varsayılmaz; rezervasyon korunur.
- Acil süre uzatma, kayıt kaybını onarmaz ve bütünlük kapısını atlamaz. Yeni sürüm/şema geçişi
  doğrulanmış defter üzerinde transaction ve tutarlı yedekle yapılır; downgrade kabul edilmez.

Teknik dayanaklar: [DPAPI kapsamı](https://learn.microsoft.com/en-us/windows/win32/api/dpapi/nf-dpapi-cryptprotectdata),
[Windows TPM anahtar koruması](https://learn.microsoft.com/en-us/windows/security/hardware-security/tpm/tpm-fundamentals),
[AES-GCM nonce koşulu](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt?view=net-10.0),
[SQLite atomik commit](https://www.sqlite.org/atomiccommit.html),
[geri alma saldırıları ve koruma gereksinimleri (ROTE)](https://eprint.iacr.org/2017/048).
Bu kaynaklar yapı taşlarını açıklar; yukarıdaki uçtan uca güvenlik tasarımı ayrıca doğrulanacaktır.

### Gözlük tarafı bağımsız zorlar

- Gözlük kendi token'ının imzasını, süresini ve gerçek maç iznine ait sayacını **sunucudan bağımsız**
  kontrol eder — sunucu patch'lense bile gözlükler bütçe sonunda durur.
- Token cihaz kimliği taşır; gözlük kendi ANDROID_ID'siyle eşleşmeyen token'ı yok sayar (başka
  gözlüğe kopyalanamaz).
- Gözlük token'ları **sunucu üzerinden** yenilenir: sunucunun heartbeat'i backend'den tüm aktif
  cihazların yeni token'larını alır, bağlanan gözlüğe verir. Gözlük imzayı doğrular; sunucu aracıdır,
  token üretemez. Operatörün tek işi sunucu PC'sini internete bağlamaktır.
- Sunucu token'ı yalnız imzayla değil **lease'teki aktif cihaz listesiyle** de kontrol eder —
  serbest bırakılan ya da iptal edilen gözlük, token süresi bitmeden reddedilir.

### Anahtarlar ve geliştirme

- İmza: ECDSA P-256. Her token ve lease `kid` taşır; uygulamalar birden çok açık anahtar tanır — özel
  anahtar sızarsa anahtar değiştirilebilir. Özel anahtar **repoya ve dağıtıma asla** girmez.
- ⛔ Sunucuda ve istemcide **"lisansı atla" bayrağı YOK** — o bayrak hazır bir crack'tir. Geliştirme,
  backend'den alınan kısa ömürlü `dev` lisansıyla yapılır; Unity editörü (`UNITY_EDITOR`, Multiplayer
  Play Mode dahil) muaftır; PoseBot `dev` token'ıyla bağlanır.
- ⛔ Tek noktada `bool isLicensed` yazılmaz: kontrol birden çok yerde, token doğrulaması üzerinden
  yapılır.

### Backend

- `Backend/` — ASP.NET Core 10 Minimal API + PostgreSQL + Blazor panel; bizim Windows sunucumuzda
  servis olarak; alan adı + TLS (Let's Encrypt). Bugünkü IIS/Python uçlarının yerini alır.
  PostgreSQL, analitik büyüyeceği için seçildi (SQL Server Express'in boyut tavanı var).
- ⚠️ Backend kesintisi lease/bütçesi biten müşterileri kilitler. Karşılığı: izleme ve yedek,
  bir de destek hattından verilen **acil uzatma belgesi** — launcher kurulum/lease/defter başına
  bağlı meydan okuma gösterir, panel backend özel anahtarıyla imzalı cevap üretir. PC yalnız açık
  anahtarla doğrular; cevap üretme sırrı PC'de bulunmaz. Belge tek kullanımlık, kısa süreli ve
  sınırlı kotalıdır; tüketimi deftere yazılır. Kayıt bütünlüğü bozuksa uzatma uygulanmaz.

## Bilinen sınırlar

- İşletme PC'sinde yönetici yetkisi olan birine karşı %100 garanti yoktur. Hedef dosya silme,
  elle değiştirme ve kurulum kopyalamayla kolay atlatmayı kapatmak; tespit edilebilen ihlalleri
  merkezde görünür kılmaktır. EXE'nin bize ait olması, işletmenin onu patch'leyemeyeceği anlamına
  gelmez. Çevrimdışı geri alma ve iptal gecikmesi yukarıdaki sınırlara tabidir; telemetri de
  hiç merkeze ulaşmamış sahteciliğin tamamını kanıtlayamaz.
- Arenalar APK içindeki Unity sahneleridir ve şifreli değildir. `maps.json`'u şifrelemek yalnız
  yavaşlatır (formatı protokol dokümanında açık). Sahne içeriğini kilitlemek Addressables'a geçiş
  ister — bu planın dışında, ayrı bir iş.

## Faz 0 — Ön şartlar (protokole dokunmaz)

- [ ] Release keystore üret; oyun (Player Settings → custom keystore) ve updater (Gradle
      `signingConfig`) **aynı** anahtarla imzalanır. Şifreler repoya girmez.
- [ ] Keystore için iki ayrı çevrimdışı yedek.
- [ ] Alan adı + TLS sertifikası.
- [ ] Geçici: bugünkü upload ucuna anahtar — anahtarsız uçtan yayınlanan APK işletme gözlüklerine
      kurulur. Yeni backend gelince bu uç kalkar.
- [ ] Geçiş: sahadaki updater'lar imza değiştiği için bir kez USB ile kaldırılıp yeniden kurulur.
- [ ] Sunucu PC'lerinde TPM/servis hesabı/anahtar saklama yeteneği envanteri; istatistik için
      desteklenen koruma seviyesi ve oyun bazlı lisans için donanım ön şartı belirlenir.

## Faz 1 — Backend (`Backend/`)

- [ ] Çözüm iskeleti ve PostgreSQL şeması:

  | Tablo | Ne tutar |
  |---|---|
  | `Tenants` | işletme, iletişim, sözleşme |
  | `Licenses` | anahtarın **hash'i** (düz metin saklanmaz), bitiş, gözlük limiti, `matchBudget` |
  | `Devices` | ANDROID_ID, işletme, aktivasyon / serbest bırakma zamanı, model, seri etiketi |
  | `ServerInstallations` | parmak izi, işletme/şube, kurulum açık anahtarı, koruma seviyesi, son görülme, aktif mi |
  | `Leases` | `leaseSeq`, veriliş / bitiş, dayandığı defter başı, bütçe ve yenileme istek kimliği |
  | `ActivationCodes` | kısa kod, işletme, son geçerlilik, kullanıldı mı |
  | `UsageEvents` | değişmez olaylar; kurulum/epoch/sıra benzersizliği, içerik özeti ve zincir |
  | `UsageReceipts` | imzalı kesintisiz kabul noktası ve toplamlar |
  | `MatchRecords` | olaylardan türeyen, maç kimliğine göre tekilleşen raporlama kaydı |
  | `LedgerEpochs` · `RecoveryGrants` | genesis, geçmişe bağlantı, veri kaybı ve denetlenebilir kurtarma |
  | `ApkVersions` · `DownloadLog` | hangi cihaz hangi sürümü ne zaman indirdi |

- [ ] Uçlar: sunucu aktivasyonu · kısa kod üretimi · cihaz aktivasyonu · cihaz serbest bırakma ·
      lease yenileme (cihaz token'larıyla birlikte) · sıralı olay alımı ve imzalı makbuz ·
      genesis/kurtarma · sürüm listesi · APK indirme · APK yükleme · panel.
- [ ] İmzalama: ECDSA P-256 + `kid`; özel anahtar gizli depoda (DPAPI / sertifika deposu), yedekli.
- [ ] Panel: işletme ve lisans açma, süre uzatma, gözlük limiti, `matchBudget`, cihaz listesi +
      serbest bırakma, iptal, acil uzatma belgesi, destek yetkili kurtarma ve işlem geçmişi.
- [ ] Olay alımı, raporlama görünümü ve makbuz aynı kabul noktasını temsil eder; tekrar deneme,
      çatallanan kayıt, sıra boşluğu ve kurtarmada geçmiş toplamını koruma kuralları uygulanır.
- [ ] Hız sınırı: sunucu aktivasyonu, kısa kod ve cihaz aktivasyonu uçlarında.
- [ ] İzleme (uç sağlığı) + günlük veritabanı yedeği.

## Faz 2 — Updater ve APK dağıtımı (protokole dokunmaz)

- [ ] Updater baştan: kısa kod ekranı → cihaz aktivasyonu → token.
- [ ] Token oyuna **`signature` seviyeli izinle korunan ContentProvider** üzerinden verilir — yalnız
      aynı anahtarla imzalı uygulama okuyabilir.
- [ ] Sürüm listesi ve APK indirme **token'la yetkili**; TLS; `usesCleartextTraffic` kalkar. Aktive
      olmayan gözlük APK'ya ulaşamaz. Her indirme loglanır.
- [ ] `deploy-player-apk.bat` yeni yükleme ucuna anahtarla yükler (anahtar ortam değişkeninden,
      repoda değil). Eski IIS/Python uçları kapatılır.
- [ ] `install_updater.bat`: `adb get-serialno` → gözlüğe dosya olarak bırakılır → updater
      aktivasyonda seri etiketi olarak gönderir.

## Faz 3 — Sunucu, launcher, oyun zorlaması (⚠️ `PROTOCOL_VERSION` 25 → 26)

⚠️ Sürüm artışı = tüm gözlüklere + admin'e + sunucuya **aynı turda** yeni sürüm (`plan/README.md`
dağıtım kuralı). Bu faz zaten yapılacak bir sürüm turuna bindirilir.

**Protokol — ÖNCE `Docs/ArenaNet-Protokol.md`, sonra iki uç**

- [ ] `hello.token` (Güvenlik bölümünde rezerve edilmiş alan): oyuncu rolünde zorunlu; admin rolü
      taşımaz, koltuk yemez.
- [ ] Sunucudan gözlüğe yenilenmiş token: `welcome` alanı + oturum içinde yeni token mesajı.
- [ ] Lisans durumu (kalan maç, kalan gün, eşik) admin paneline ve gözlük lobisine giden duruma.
- [ ] Red nedenleri (`kicked.reason`): lisans geçersiz · cihaz kayıtlı değil · lisans süresi doldu ·
      sürüm uyumsuz.
- [ ] ⚠️ Protokol sürüm uyumsuzluğu **reddedilir** — `LobbyService.HandleHelloAsync` şu an yalnız
      loglayıp devam ediyor; öyle kalırsa eski APK'ya dönmek lisans kontrolünü atlatır.
- [ ] Lisans bölümü: token ve lease formatı — tek doğruluk kaynağı; backend, sunucu, launcher,
      updater ve oyun buna uyar.
- [ ] `startRequestId`, sunucu üretimi `matchId`, gerçek maç izni/ön yükleme ayrımı ve gözlükte
      tekrar saymama kuralı; `ledgerEpoch`, `eventSeq`, `leaseSeq`, makbuz ve kurtarma şemaları.
      Kayıt hatası ve kurtarma gereksinimi admin/launcher durumuna eklenir.

**Sunucu (`Server/`)**

- [ ] `UsageLedger`: yukarıdaki `usage.db`, transaction, AEAD/zincir/HMAC, servis hesabı ve
      anahtar saklama kuralları. Üretimde tek yazıcı; deploy/güncelleme dosyayı silmez.
- [ ] `Program.cs`'te oyun portları açılmadan `ValidateUsageLedger` → mutabakat/lease yenileme →
      `ValidateLicense`; hata çıkış kodu 3. Eksik/bozuk defter normal açılışta oluşturulmaz.
- [ ] Ayrı bakım modu + erişim denetimli yerel IPC: genesis, durum okuma ve imzalı kurtarma.
- [ ] Heartbeat: önce olayları gönder/makbuzu sakla, sonra aynı defter başına bağlı lease ve
      cihaz token'larını al. Tanımadığı ama geçerli token'lı gözlükte hemen heartbeat dene.
- [ ] `LobbyService.HandleHelloAsync`: token doğrulaması (imza, `kid`, işletme, süre, aktif cihaz
      listesi) — "Sunucu dolu" bloğundaki `KickedMsg` + `CloseAfterKickAsync` deseniyle.
- [ ] `start_match` kapısı: defter, sayaç, bütçe, süre ve saat geri alma; kalıcı commit sonrası
      oyun izni. Yaşam döngüsü sıralayıcısı, istek tekilleştirme, bitiş/iptal/çökme kayıtları.
- [ ] Çalışırken bütünlük denetimi, disk eşiği uyarıları ve kayıt hatasında yeni maç yasağı.
- [ ] Acil uzatma belgesinin imzası ve tek kullanımı; bütünlük kilidini atlayamama.

**Launcher (`launcher/`)**

- [ ] Lisans ekranı: anahtarla ilk aktivasyon · durum (`23/70 maç · 6 gün` biçiminde) · uyarılar ·
      "Gözlük Ekle" (kısa kodu gösterir) · cihaz listesi (son görülme) + "Kaldır" (ayda 2) · acil
      belge aktarımı. Dosyayı açmak yerine sunucunun IPC/bakım moduyla çalışır.
- [ ] Çıkış kodu 3'ün operatör metni; lisans bitişi ile eksik/bozuk kayıt ayrımı, destek referansı,
      son eşitleme zamanı ve bekleyen olay sayısı. Yerel maç özeti sunucudan salt okunur gelir.

**Oyun (Quest)**

- [ ] ANDROID_ID JNI ile doğrudan okunur — `SystemInfo.deviceUniqueIdentifier` aynı değer
      olmayabilir; `devices.json` anahtarı değişmez.
- [ ] Token updater'ın ContentProvider'ından okunur, kendi kopyası saklanır; sunucudan gelen yenisi
      üzerine yazılır. Manifest'e signature izninin kullanımı eklenir.
- [ ] Token imza doğrulaması: BouncyCastle (saf C#, IL2CPP'de güvenli) — paketi eklemeden önce
      `Docs/Gelistirici/Yapma-Listesi.md` paket politikası.
- [ ] Gerçek maç izninde kendi `matchId` bazlı sayacı + süre/bütçe kapısı, sunucudan bağımsız;
      yeniden bağlanma/ön yükleme/aynı mesaj tekrarında tüketim yok.
- [ ] Lobide eşik uyarısı.

**Admin (Windows)**

- [ ] Panelde lisans durumu ve eşik uyarıları.

**Geliştirme akışı**

- [ ] `dev` lisansı: backend'den, kısa ömürlü; `dotnet run` ile açılan sunucu da onu kullanır. Editör
      muaf; PoseBot `dev` token'ıyla bağlanır. Kurulumu `Docs/Gelistirici/Ilk-Adimlar.md`'ye yazılır.

## Faz 4 — Telemetri ve analitik

Faz 3, korunan kayıt ve eşitleme olmadan yayınlanmaz; bu faz onların üzerine raporlama ekler.
JSONL veya sıradan uygulama logu lisans/kullanım kaydının doğruluk kaynağı değildir.

- [ ] `UsageEvents` → `MatchRecords` görünümü: başlangıç/canlı/bitiş/iptal/kesinti ayrımı,
      skor/kopma/süre özetleri; yeniden işlemede aynı sonuç ve çift sayım olmaması.
- [ ] Panel: işletme/şube ve tarih aralığı filtresi; gün/saat, mod/harita, maç sayısı, katılım ve
      aktif süre; işletme saat diliminde gösterim. Toplam katılım benzersiz kişi diye sunulmaz.
- [ ] Panelde veri güncelliği, şüpheli saat, yarıda kalan maç ve kurtarmadaki veri kaybı görünümü;
      kayıtlar gelmediyse kesin sıfır göstermeme.
- [ ] Anomali uyarıları: aynı lisans iki kurulumdan · `leaseSeq`/olay sırası gerilemesi · aynı
      sıra için farklı özet · büyük saat farkı · olağan dışı tüketim · sık kurtarma talebi.
- [ ] Saklama/budama politikası, imzalı arşiv checkpoint'i ve tutarlı yedekten geri dönüş.
      Backend felaket kurtarması eski makbuzları kaybettiyse istemciden yeniden gönderim ve
      mutabakat yapılır; backend'in eski yedeği sessizce yeni doğru kabul edilmez.

### Yayına çıkış doğrulamaları

- [ ] İnternetsiz birkaç açılış ve maç; bağlantı gelince bütün olayların bir kez sayılması.
- [ ] Dosya silme, içerik değiştirme, olay/sıra silme, zincir sonunu kesme, başka işletmenin
      dosyasını koyma: başlangıçta kilit; çalışırken yeni maç yasağı; otomatik boş defter yok.
- [ ] Aynı `startRequestId`, aynı `matchId` ve eşzamanlı admin istekleri; reddedilen komutta,
      ön yüklemede, yeniden bağlanmada ve turnuva turunda fazla tüketim olmaması.
- [ ] Commit öncesi/sonrası, oyun izni öncesi/sonrası elektrik kesintisi; disk dolması ve yazma
      izni kaybı: kayıtsız maç başlamaması, tamamlanamayan maçın belirsiz kalması.
- [ ] Upload/ACK/lease geçişinde kesinti ve tekrar deneme: çift kayıt, sayaç sıfırlama veya ek
      bütçe olmaması. Gönderilmemiş kayıtların budanamaması.
- [ ] Eski dosya/tam disk yedeği ve saat geri alma: merkez/donanımın bildiği geri dönüşü
      reddetme; donanımsız çevrimdışı tespit sınırının açıkça doğrulanması.
- [ ] Silme sonrası yeniden kurulum/aynı lisansı girme/acil uzatma geçmişi sıfırlayamamalı;
      yetkili kurtarma, PC değişimi ve anahtar/TPM kaybında sayaç ve veri kaybı kaydı korunmalı.
- [ ] Oyun bazlı lisans öncesi donanım sayacı prototipi, tüm ara çökme durumları, TPM temizleme,
      VM snapshot'ı ve eski PC çevrimdışıyken kota rezervasyonunun iki kez kullandırılmaması.

## Faz 5 — Sertleştirme

- [ ] Sunucu NativeAOT ile yayınlanır (IL kalmaz; JSON source-gen'e geçiş gerekir).
- [ ] Admin Windows build'i IL2CPP'ye geçer (Mono build decompile edilebilir).
- [ ] Oyun kendi APK imza sertifikasını çalışma anında kontrol eder.
- [ ] Kontroller dağıtılır, sonuçları gecikmeli olur — ⚠️ her bozulma yolu **bizim çözebileceğimiz**
      bir log işareti bırakır, yoksa saha hatası ayıklanamaz.
- [ ] Updater'da R8 küçültme ve karıştırma.

## Kod dışı işler

- [ ] Alan adı.
- [ ] Sözleşme: lisans şartları + KVKK veri işleme maddesi ve aydınlatma metni.
- [ ] ⚠️ Zorlayan sürüm (Faz 3) çıkmadan **önce** mevcut işletmelere lisans açılır ve gözlükleri
      aktive edilir — yoksa güncelleme günü hepsi kilitlenir.

## Doküman işleri (her faz kendi commit'inde)

- `Docs/ArenaNet-Protokol.md`: Güvenlik bölümü yeniden yazılır (kimlik doğrulama artık var); Lisans
  bölümü (token/lease/olay/makbuz/kurtarma formatı); sürüm 26; red nedenleri; sürüm uyumsuzluğunun
  reddi; maç isteği tekilleştirme ve gerçek maç izni.
- `Docs/Sistem-Ozeti.md`: repo haritasına `Backend/`; bileşen sözlüğüne lisans bileşenleri;
  Tuzaklar'a tek keystore / ANDROID_ID bağı ve "atlama bayrağı yok" gerekçesi.
- `Docs/Gelistirici/Yapma-Listesi.md`: ⛔ keystore'u değiştirme · ⛔ lisansı atlayan bayrak koyma ·
  ⛔ tek noktada lisans kontrolü · ⛔ imza özel anahtarını repoya koyma.
- `Docs/Isletme-Kurulum.md`: "internet YOK" kuralı yeniden yazılır — sunucu PC internetli, arena
  SSID'si internete kapalı kalır (AP/router'da SSID kuralı); lisans aktivasyonu ve gözlük ekleme
  adımları.
- `Docs/Kullanim-Kilavuzu.md`: lisans ekranı, uyarılar, gözlük ekleme/kaldırma, acil uzatma belgesi,
  kayıt hatasında destek akışı ve istatistiklerin güncelliği.
- `Server/README.md` (lisans dosyası, çıkış kodu 3, heartbeat) · `launcher/README.md` (lisans ekranı;
  internete çıkan kod artık var) · `updater/README.md` (baştan) · `scripts/README.md` (keystore,
  yükleme anahtarı) · yeni `Backend/README.md` · `CLAUDE.md` yerleşim listesine `Backend/` için tek
  satır.

## Önce ölçülecekler (tasarımı etkiler)

- [ ] Quest'te Meta hesabı değişince ANDROID_ID değişiyor mu (Android kullanıcı kapsamı). Değişiyorsa
      hesap değişimi koltuk yer — operatöre uyarı olarak yazılır.
- [ ] Unity `SystemInfo.deviceUniqueIdentifier` Android'de imza anahtarına bağlı mı. Bağlıysa keystore
      geçişinde `devices.json` isim/forma eşleşmeleri kopar — Faz 0 geçiş adımına eklenir.
- [ ] `signature` izinli ContentProvider Quest'te iki ayrı paket arasında çalışıyor mu.

## Açık kalanlar

- Sunucu PC'nin interneti ayrı bir ağ arayüzünden geliyorsa beacon'ın doğru arayüzden yayılması
  (`Docs/Isletme-Kurulum.md` "tek aktif ağ arayüzü" kuralı).
- Gözlük güncellemesi için LAN önbelleği: sunucu PC APK'yı bir kez indirir, gözlükler yerelden çeker —
  arena SSID'sine internet açmadan güncelleme.
- Sahne içeriği koruması (Addressables) — ayrı plan konusu.
