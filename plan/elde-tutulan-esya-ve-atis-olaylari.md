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

## 1. Kavrama pozu ayarı — ⚠️ SIRADAKİ İŞ (araç hazır, sayılar girilmedi)

Altı silahın `primaryGripPosition/Euler` ve `secondaryGripPosition/Euler` değerleri **hepsi sıfır**.
Yani silahlar şu an el anchor'ının tam üstünde, dönüşsüz duruyor ve soketler silah modelinin
kökünde — kabzada değil. Altyapı ve araç çalışıyor, **sayılar girilmedi**.

**İş akışı (editörde, APK build'i gerekmez):**

1. Bir `WPN_*.prefab`'ı sahneye bırak (scratch sahne yeter) ve seç.
2. `GameObject > VortexArena > Grip Socket (Primary)` → silahın altında işaretçi doğar
   (mevcut SO değerlerinden başlar, yani ayarı sıfırlamaz).
3. İşaretçiyi **kabzaya sürükle** ve elin gireceği açıyla **döndür** — Scene view'de sarı saydam
   küre soketi gösterir; yarıçapı Inspector'dan (`radius`) büyüt/küçült.
4. Çift elli silahta aynısını `Grip Socket (Secondary)` ile ön kabza için yap.
5. `Tools > VortexArena > Write Grip Sockets To Definition` → değerler `WD_*.asset`'e yazılır.
6. Kontrol: camgöbeği tel küre (SO'nun dediği) sarı küreyle **çakışmalı**. Çakışmıyorsa yazma
   gitmemiş. İşaretçi sonra silinebilir ya da bırakılabilir — oyun onu okumaz.
7. Başlıkta yalnız **hissi** doğrula (nişan alırken rahat mı). Geometri editörde bitti.

**Bilinmesi gerekenler:**

- ⚠️ İki alanın **uzayı terstir**: `primaryGrip` = eşyanın ELE göre pozu, `secondaryGrip` = ön kabza
  noktasının EŞYAYA göre pozu (§6.6). **Araç bu dönüşümü yapıyor** — elle Inspector'a yazarken
  yapılan en sık hata buydu.
- ⚠️ `WeaponKitBuilder` bu alanları **ezmez** (tabloda karşılığı yok) — girilen değerler araç
  tekrar koşsa da kalır. `netItemId`/`holdMode` ise tablodan gelir ve EZİLİR.
- Aynı ölçü **üç yeri** besliyor: yerel tutuş · uzak oyuncudaki çizim · kavrama soketinin yeri.
  Biri düzelince üçü düzelir; ana soketin yeri türetilir (`PrimaryGripPointOnItem`), elle girilmez.
- Sıra: önce `primaryGrip` (silah elde doğru dursun), sonra `secondaryGrip`.
- Yarıçaplar silah başınadır (`primaryGripRadius`/`secondaryGripRadius`, varsayılan 12 cm) ve
  işaretçinin `radius` alanından yazılır.

## 2. Tracer + soket görünüm değerleri — playtest ayarı

`ItemDefinition`'daki `tracerColor` / `tracerWidth` / `tracerLifetime` / `tracerEveryNthRound`
(varsayılan 3) sahada gözle ayarlanır. Dokümana sayı yazılmaz.
Karar verilecek: her silahın tracer'ı farklı mı görünecek, yoksa hepsi aynı mı kalacak (altyapı
ikisini de destekliyor — alanlar silah başına, değerler şu an aynı).

Soket tarafında ayarlanacaklar (`ItemGripSockets` sabitleri — kod içinde, silah başına DEĞİL):
`HoverRadius` (0.30 m) · `GrabRadius` (0.12 m) · halka yarıçapı/kalınlığı/rengi.
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
