# Kalibre modu · T-poz sağlamlaştırma · ölçek geri bildirimi — kalan iş

Kod (protokol v13, sunucu, istemci, admin arayüzü, panel prefabındaki üç mod düğmesi) ve
dokümanlar yazıldı. Kalıcı bilgi: `Docs/ArenaNet-Protokol.md` (§1 sabitler, §5.1/§5.2/§5.3,
§6.9 T-poz, §10.6 kalibre modu + zemin sağlığı, §10.8 ölçüm geri bildirimi),
`Docs/Sistem-Ozeti.md` (§3.11, §4, §7) ve `Docs/Kullanim-Kilavuzu.md` (§4.3 kalibre modu,
§4.4 alan verisi temizliği, §8).

---

## 1. Doğrulama (kullanıcı koşar)

- [ ] Mod değişimi o an bağlı başlığa işlemiyor (yeniden bağlanınca işliyor) — panel bunu yazıyor
- [ ] Admin `KAL` sıfırlaması sonrası ne disk ne bellek yolundan geri yükleme oluyor
- [ ] Zemin sapması: kalibre kasıtlı yükseltide alınınca (uç masada) duyuru + satırda turuncu
      `KAL ?`; doğru zeminde alınca temizleniyor
- [ ] T-poz (ilk açılış): body tracking izinsizken oyuncu diğer ekranlarda konumunu izleyen
      T-pozda (görünmez değil)
- [ ] T-poz (oyun içi): kök saçmalayınca (çok katlı senaryo) uzak taraf ışınlanma yerine T-poz
      görüyor; veri düzelince ~1 sn içinde normale dönüyor; konsolda giriş/çıkış logları tek atışlık
- [ ] Ölçeklenmiş/T-pozlu oyuncuda regresyon yok: normal oturumda yedek hiç devreye girmiyor

⚠️ **Protokol v13** — tüm başlıklara yeni APK + admin build + sunucu dağıtımı gerekir.

Alan verisi temizliği yalnız gözlüğün Ayarlar menüsünden yapılır (`Docs/Kullanim-Kilavuzu.md`
§4.4); `vrguardianservice` paketi bu sistem sürümünde yok, adb ile temizlik yolu kapalı.

## 2. T-poz yedeği — dört kaçak

Yedek tek makinedir, iki tetiği vardır (`ArenaNetCharacterBehaviour.TickTPoseFallback`):
**soğuk başlangıç** (`LocalBodyAvatar.TickBodyTrackingWatchdog` 5 sn boyunca `RetargeterValid`
görmeyince `RequestTPoseFallback`) ve **uçuşta arıza** (`AppliedPose` iken kök akıl sağlığı
denetimi `_poseSuspect` ya da `StaleFrameSeconds` = 1 sn temiz SDK karesi yok). İkisi de aynı
kareyi yollar: SDK bind pozu + HMD zemin izdüşümü, yalnız yaw. Alıcı canlı akışı sorgulamaz —
`SkeletonDeadAfterMs` = 1 sn içinde kök örneği varsa iskelet çizilir, yoksa `IsPoseDriven`
açılır ve `RemotePoseBody` kafa + iki kol IK'sını poz kanalından çözer. Telde geçerlilik biti
**yoktur**, "izleme bozuk" yalnız T-poz yükü olarak ifade edilir; aşağıdakilerin hiçbiri
protokol/sunucu değişikliği istemez, sürüm **artmaz**.

⚠️ Dokunulacak dosyalar (`ArenaNetCharacterBehaviour.cs` · `LocalBodyAvatar.cs`) boy ölçeği
işiyle aynı dosyalardır; bu iş **o iş commit'lendikten sonra** başlar, paralel yürümez.

- [ ] **(a) Lobi ile maç farklı görünüyor.** İskelet yolunda faz kapısı yok; lobide görülen
      "canlı" gövde, iskelet akışı hiç gelmediği için `IsPoseDriven` yolunun kol IK'sıdır (bacak
      hiç oynamaz — ayırt edici işaret), maçta görülen T-poz ise yedek akışın o sırada başlamasıdır.
      Ürün kararı: izin verilmemiş oyuncu **lobiden itibaren** T-pozdadır — T-poz operatöre
      "izleme yok" sinyalidir ve maç başlamadan görülmelidir (§1'deki doğrulama maddesi zaten
      bunu ister). Yedeğin neden geç kurulduğu statik okumayla kapanmıyor; adaylar: `LocalBodyAvatar`
      bekçisinin `_initialized` (aktif `OVRCameraRig` + `PlayerId > 0`) sonrası başlaması ve 5 sn
      sabır, `SendTPoseFrame`'in kafa pozu olmadan dönmesi, `SerializeSkeletonAndFace`'in hiç
      retarget koşmamış tutamaçta `false` dönmesi. İş: (1) izin durumu **doğrudan** sorulur
      (`Permission.HasUserAuthorizedPermission` BODY_TRACKING) ve reddedilmişse yedek 5 sn
      beklemeden kurulur; (2) `SendTPoseFrame`'de serileştirme başarısızlığı ve bekçinin kurulma
      anı **tek atışlık** loglanır ki sahada hangi aday olduğu tek koşuda ayrılsın.
- [ ] **(b) Ayarlardan gövde takibi kapatınca T-poz gelmiyor, Meta tuşunda geliyor.** Göndericide
      geçerlilik tek bittir ve kaynağı `OVRBody`: `GetBodyState4` başarılıysa `IsDataValid`
      **koşulsuz true**, güven değeri geçerliliğe girmez, geçersiz eklem **önceki karenin değerini
      korur** ve geçerli diye raporlanır. Meta tuşu `GetBodyState4`'ü düşürdüğü için SDK susar ve
      1 sn bayatlama T-pozu tetikler; Ayarlar kapatması çalışma zamanının cevap vermeye devam ettiği
      (çıkarım) donmuş veriyle akışı sürdürür, kök denetimi de kökü kafanın altında bulduğu için
      geçirir. İş: `AcceptSdkFrame`'e **donmuş akış dedektörü** — eklem-yerel poz
      (`_retargeter.GetCurrentBodyPose`, kök değil; kök HMD'yi izlediği için donmaz) art arda
      `StaleFrameSeconds` boyunca birebir aynıysa kare bayat sayılır ve uçuşta arıza tetiği düşer;
      ek olarak `OVRBody.BodyState.Confidence` ve anahtar eklemlerin `PositionValid` biti okunur,
      sıfır güven/geçersiz anahtar eklem = geçersiz kare. Mevcut histerezis (`RecoverStreakFrames`)
      aynen kullanılır, yeni tesisat yok.
- [ ] **(c) Arka plana atınca 1-2 sn T-poz, sonra donuk tuhaf poz.** Kodda hiçbir yaşam döngüsü
      kancası yok (`OnApplicationPause`/`OnApplicationFocus` hiçbir dosyada geçmiyor). Zincir:
      odak kaybı → SDK susar → 1 sn sonra T-poz kareleri (12 Hz) → uygulama duraklar, `LateUpdate`
      durur, kareler kesilir → alıcıda kök 1 sn'de ölür → `IsPoseDriven` açılır → `RemotePoseBody`
      T-pozlu iskeletin üstüne **son el pozlarına** kol IK'sı çözer (kollar T'den kırılır, bacak/omurga
      T'de kalır) ve poz kanalı da sustuğu için orada donar. Oyuncu bağlı listelendiğinden avatar
      düşmez. İş: (1) `RemotePoseBody` poz kanalının **tazeliğine** kapılanır — son örnek yaşı
      `SkeletonDeadAfterMs` gibi bir eşiği aşıyorsa IK çözülmez, iskelet son uygulanan T-pozda
      kalır (belgelenen "konumunu izleyen donuk T-poz"); yaş `RemotePlayerRegistry`'den okunur, açık
      değilse eklenir. (2) İsteğe bağlı: `LocalBodyAvatar`'a `OnApplicationFocus(false)` kancası —
      1 sn bayatlamayı beklemeden yedeği kurar; duraklamada gönderim zaten kesilir, kanca yalnız
      geçişi hızlandırır.
- [ ] **(d) Bozuk veride (merdiven, örtülme) T-poz nadiren geliyor, gövde deforme oluyor.** Tek
      akıl sağlığı denetimi kökü bakar (`SuspectHorizontalMeters` 1.5 m · `SuspectRootHeightMeters`
      1 m); eklem uzunluğu, eklem hızı, ayak-zemin gibi bir ölçüt **hiçbir yerde yok**, alıcı da
      yapamaz (blob opak, yalnız SDK açar; alıcı yalnız yaw + ofset görür). İş: `AcceptSdkFrame`'e
      eklem makullüğü — bind pozuna göre birkaç anahtar kemik oranı (kalça→baş, üst/alt bacak)
      `[0.5, 2.0]` dışında, ayak kök zemininin 0.3 m altında/kalçanın üstünde, ya da ardışık iki
      kare arasında eklem sıçraması eşik üstü → kare şüpheli. Yalnız bir avuç eklem, 12 Hz'de ucuz;
      çıkış yine `RecoverStreakFrames` histereziyle.
- [ ] Doküman: `Docs/ArenaNet-Protokol.md` §6.9 T-poz tetik listesi (donmuş akış, eklem makullüğü,
      odak kaybı) · `Docs/Sistem-Ozeti.md` §4 `ArenaNetCharacterBehaviour` ve `RemotePoseBody`
      maddeleri (poz kanalı tazelik kapısı).
- [ ] Doğrulama (Notion kartına, iş bitince): izin reddi → lobide de T-poz · Ayarlar kapatma →
      ~1 sn'de T-poz · arka plana atma → T-poz T-poz kalır, kol kırılmaz · merdiven/örtülme →
      T-poz daha sık, veri düzelince ~1 sn'de dönüş · normal oturumda yedek hiç girmiyor.
