# Elde tutulan eşya + atış olayları — KALAN İŞLER

> **Faz 0–3 + soket kavrama + olay zamanlaması BİTTİ** (`PROTOCOL_VERSION` 4). Kalıcı bilgi
> dokümana taşındı: tel formatı + kurallar `Docs/ArenaNet-Protokol.md` §6.2–§6.6 · akış/bileşenler
> `Docs/Sistem-Ozeti.md` §3.5, §3.5b, §3.6, §4 · tuzaklar §7 · editör aracı `CLAUDE.md`.
> Bu dosya yalnız **yapılmamış** olanı tutar; hepsi bitince silinir.

Biten iş özeti (bir daha yapılmayacak, referans için): eşya durumu telde (`0x01` 95 B / `0x02` 88 B
+ kavrama bitleri) · UDP atış/atma olay kanalı (`0x03`/`0x04`, `shot_fired` WS'ten kaldırıldı) ·
oktahedral yön sıkıştırma · `ItemDefinition` tabanı + `NetItemCatalog` + kimlik bekçisi · kanonik
kavrama · uzak avatarda eşya çizimi + eşikli ön-kabza yapıştırma · havuzlu tracer ·
**soket tabanlı kavrama** (`ItemGripSockets`, ISDK `_interactorFilters` kapısı; mesafeden kavrama
kaldırıldı) · **olayların `serverTick`'te oynatılması** (`RemotePlayerRegistry.TryGetPlaybackTimeMs`).

## 1. Kavrama ayarı — ⚠️ SIRADAKİ İŞ (araç hazır, silahların kavraması yazılmadı)

Kavrama sayı girerek değil **Kavrama Pozu Stüdyosu**'nda, prefab kipinde yazılıyor: silah prefab
kökünde sabit durur, sen elleri kabzalara oturtursun. Araç hazır; kalan iş altı silahı tek tek
geçmek. Tam reçete: `Docs/Gelistirici/Yemek-Kitabi.md` §11.0 A.

**İş akışı (editörde, APK build'i gerekmez):**

1. `WPN_*.prefab`'ı **prefab kipinde** aç (çift tık); stüdyo penceresi stage'i tanır.
2. **Ana Kabza Ellerini Oluştur** (çift elli silahta ayrıca **Ön Kabza Ellerini Oluştur**). Eller
   mevcut veriden yerleşir — sıfırdan başlamazsın.
3. Elleri kabzalara oturt/çevir (Scene view'da yardımcı çizim yoktur, gözle değerlendirirsin);
   pencere kaydedilecek sayıları canlı gösterir.
4. Parmakları elin `GripHandAuthoring` Inspector'ından (ya da Hierarchy'den kemiği seçip) bük.
5. İstersen **Karşı Ele Aynala**, sonra aynalanan eli elle düzelt (kabza simetrik değildir).
6. **Kaydet** → her elin duruşu + parmakları `GripPoses/Pose_<Kind>_<L|R>`'ye, sağ elden türetilen
   fallback alanları `WD_*.asset`'e yazılır. Sonra **prefabı kaydet** (düğümler stage içeriğidir).
7. Kontrol: camgöbeği tel küre (SO'nun dediği) sağ elin bileğiyle **çakışmalı**.
8. Başlıkta yalnız **hissi** doğrula (nişan alırken rahat mı); geometri editörde bitiyor.

⚠️ **İlk silahta bir sağlama yap:** elleri oluştur → hiç dokunmadan Kaydet → ne poz düğümleri ne
`WD_*.asset` değerleri DEĞİŞMELİ. Kurulum (`ToWristLocal`) ile kayıt (`FromWrist`) birbirinin
tersidir; değişiyorsa uzay yönlerinden biri terstir ve yeri tek dosyadır (`ItemHandGripBake`).

**Bilinmesi gerekenler:**

- ⚠️ **Ölçünün paydası silahtır, taşınan eldir** — ölçü "silah ele göre nerede" olduğu için. Her
  silahın kabzası farklı açıdan tutulur ve kabza–tetik mesafesi aynı değildir; **iki el de ayrı
  yazılır** (poz düğümleri el başınadır, ayna yalnız bir başlangıçtır).
- ⚠️ **El ham sürüklenir, çeviri yoktur:** elin kökü ISDK bilek çerçevesidir ve oyun poz düğümünü
  canlı ölçülen bileğe hizalar — gözle doğru gördüğün yerleşim doğrudur. Anchor çerçevesine çeviri
  yalnız `WD_*` fallback alanlarının yazımında kalır
  (`Docs/Sistem-Ozeti.md` §7, "gözle düzeltilen şey oyunun okuduğuyla aynı çerçevede olmalı").
- ⚠️ İki alanın **uzayı terstir**: `primaryGrip` = eşyanın ELE göre pozu, `secondaryGrip` = ön kabza
  noktasının EŞYAYA göre pozu (§6.6). Dönüşümü `ItemHandGripBake` yapıyor — elle Inspector'a
  yazarken yapılan en sık hata buydu ve o yol artık yok.
- ⚠️ `GripPoses/Pose_*` kaydın **çıktısıdır**, elle düzenlenmez: bir sonraki kayıt üzerine yazar.
- ⚠️ `WeaponKitBuilder` kavrama alanlarını **ezmez**; koşu sonunda kavraması yazılmamış silahları
  listeler, eski `GripSocket_*` işaretçilerini ve prefabta kalmış `Hands/Hand_*` rig'ini siler.
  `netItemId`/`holdMode` tablodan gelir ve EZİLİR.
- Aynı kayıt **üç yeri** besliyor: yerel tutuş · uzak oyuncudaki çizim · kavrama soketinin yeri.
  Biri düzelince üçü düzelir; ana soketin yeri türetilir (`PrimaryGripPointOnItem`), elle girilmez.
- Sıra: önce `primaryGrip` (silah elde doğru dursun), sonra `secondaryGrip`.
- Yarıçaplar silah başınadır (`primaryGripRadius`/`secondaryGripRadius`, varsayılan 12 cm) ve
  Inspector'dan girilir — bake onlara dokunmaz.

## 2. Tracer + soket görünüm değerleri — playtest ayarı

`ItemDefinition`'daki `tracerColor` / `tracerWidth` / `tracerLifetime` / `tracerEveryNthRound`
(varsayılan 3) sahada gözle ayarlanır. Dokümana sayı yazılmaz.
Karar verilecek: her silahın tracer'ı farklı mı görünecek, yoksa hepsi aynı mı kalacak (altyapı
ikisini de destekliyor — alanlar silah başına, değerler şu an aynı).

Soket tarafında ayarlanacaklar: **kavrama yarıçapı silah başınadır** (`primaryGripRadius` /
`secondaryGripRadius`, varsayılan 12 cm — Inspector'dan girilir). `ItemGripSockets`
sabitleri (kod içinde, tüm eşyalarda ortak): `HoverRadius` (0.30 m) · halka
yarıçapı/kalınlığı/rengi.
⚠️ Ön kabza yarıçapı ana kabzadan **daha cömert** olmalı: ana soket dururken kavranıyor, ön kabza
ise silah zaten ana elde SALLANIRKEN. Hareketli bir hedefe 12 cm dar geliyorsa önce bu sayıyı
büyüt — kod değişikliği değil, silah başına bir ayar.
İsteğe bağlı: `WeaponCatalog.gripSocketPrefab`'a düzgün bir gösterge prefabı koymak — boş kalırsa
prosedürel halka çiziliyor, yani **iş yapılmadan da çalışıyor**. Prefab konursa gösterge eşyanın
dönüşünü alır (halka yedeği kameraya döner) — uzamsal bir işaret çizmek isteniyorsa yolu bu.

## 3. İki elli yerel nişan kuralı — his kararı

Silah sabit ana eli mi izleyecek (bugünkü davranış), yoksa iki elin doğrultusuna mı hizalanacak?
⚠️ **Tel formatını ETKİLEMEZ**: iki elin pozu da telde olduğu için uzak istemci aynı kuralı kendi
tarafında yeniden uygular. Yani playtest'te serbestçe değiştirilebilir, protokol sabit kalır.

## 4. Faz 4 — bomba (gelecek)

- `ThrowableDefinition : ItemDefinition`. Bileklikten alma = yalnız `itemId`, altyapı **hazır**
  (Faz 2 bedavaya kapsıyor); bileklik kılıfı avatarın parçası, telde karşılığı yok.
- Atış = `0x04` `kind=1` (yön + hız). `ArenaCombat.ReportThrow` **yazıldı ve bekliyor**;
  `RemoteShotFx` `KIND_THROW`'u şu an sessizce atıyor (yorumla işaretli) — tüketiciyi orada aç.
- Her istemci aynı balistiği **yerel simüle eder** (yerçekimi tek kuvvet → deterministik, akış
  gerekmez). Patlama mevcut yoldan: `ArenaCombat.ReportAreaHit` → hedef başına bir `hit_report`.
- Soket tarafında iş: `ItemGripSockets` `Weapon` taşımayan eşya için serialize `definition`
  yedeğiyle çalışıyor (alan hazır). Bomba tek elli olduğu için ön kabza soketi hiç açılmaz.
- Kırılabilir objelere hasar bu planın DIŞINDA (`agsal-kirilabilir-objeler.md`);
  ⚠️ `hit_report`'a bu plandan alan **eklenmedi ve eklenmez** — spekülatif tel alanı kalıcı borçtur.

## 5. Dağıtım — ⚠️ ÜÇÜ BİRLİKTE

`PROTOCOL_VERSION` 3 → **4** ve `0x01`/`0x02` byte düzeni değişti. Sürüm uyuşmazlığı yalnız log
uyarısı ürettiği için (§1) **karışık kurulumda pozlar sessizce bozuk parse edilir** — v3 istemci ile
v4 sunucu bir arada çalışmaz. `scripts\deploy-player-apk.bat` + `deploy-admin-game.bat` +
`deploy-server.bat` aynı turda koşmalı.
