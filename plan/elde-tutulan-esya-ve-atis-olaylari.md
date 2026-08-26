# Elde tutulan eşya + atış olayları — KALAN İŞLER

> **Faz 0–3 + ön kabza kapısı/göstergesi + olay zamanlaması BİTTİ** (`PROTOCOL_VERSION` 4). Kalıcı bilgi
> dokümana taşındı: tel formatı + kurallar `Docs/ArenaNet-Protokol.md` §6.2–§6.6 · akış/bileşenler
> `Docs/Sistem-Ozeti.md` §3.5, §3.5b, §3.6, §4 · tuzaklar §7 · editör aracı `Docs/Sistem-Ozeti.md` §4.
> Bu dosya yalnız **yapılmamış** olanı tutar; hepsi bitince silinir.

Biten iş özeti (bir daha yapılmayacak, referans için): eşya durumu telde (`0x01` 95 B / `0x02` 88 B
+ kavrama bitleri) · UDP atış/atma olay kanalı (`0x03`/`0x04`, `shot_fired` WS'ten kaldırıldı) ·
oktahedral yön sıkıştırma · `ItemDefinition` tabanı + `NetItemCatalog` + kimlik bekçisi · kanonik
kavrama · uzak avatarda eşya çizimi + eşikli ön-kabza yapıştırma · havuzlu tracer ·
**ön kabza kapısı + göstergesi** (`Weapon.IsHandOnSecondaryGrip` / `TickSecondaryGripIndicator`,
sanat `WeaponCatalog.secondaryGripIndicatorPrefab`; kökte mesafeden kavrama yok) ·
**olayların `serverTick`'te oynatılması** (`RemotePlayerRegistry.TryGetPlaybackTimeMs`).

## 1. 13 silahın kavramasını YAZ — ⚠️ SIRADAKİ İŞ

Kalan iş silahları tek tek geçmek. Tam reçete: `Docs/Gelistirici/Yemek-Kitabi.md` §11.0.

**İş akışı (editörde, prefab kipinde; Play ve APK gerekmez):**

1. `Tools > VortexArena > Items > Kavrama Pozu Stüdyosu` → pencereyi aç.
2. `WPN_*` prefabını **prefab kipinde** aç.
3. **Ana Kabza Ellerini Oluştur** (+ iki elli silahta **Ön Kabza Ellerini Oluştur**).
4. Kumanda çerçevelerini Scene View'da kabzalara oturt; el modelini o kumandanın üstüne yerleştir
   (*El Modeli* → taşı ve **çevir**; silah kımıldamaz); sonra parmakları o silaha göre **rigle**
   (penceredeki parmak listesinden eklemi seç → Scene View'da çevir). Gerekirse **Karşı Ele Aynala**
   ile başlat (el yerleşimini ve parmakları da taşır).
5. **Kaydet** → dört kayıt `WD_*.asset`'e iner ve silah kiti kendiliğinden eşitlenir (ayrı bir
   senkronize adımı yok; tezgâhtan kalkan eller *Elleri Oluştur* ile kayıttan geri gelir).
6. Başlıkta yalnız **hissi** doğrula (nişan alırken rahat mı).

**Bilinmesi gerekenler:**

- ⚠️ **Dört kaydın dördü de ayrı yazılır** (`primaryGripRight/Left`, `secondaryGripRight/Left`):
  kabza simetrik değildir, aynalama yalnız başlangıçtır. Eksik el öteki elin kaydına düşer —
  çalışır ama yanlış tutar.
- ⚠️ **Kumanda kökü yalnız TAŞINIR** — çevirmenin oyunda karşılığı yoktur ve araç kökü silahla
  hizalı hâline geri alır (`item = anchor ∘ Inverse(kayıt)`); ön kabzada el silaha yapışır. Silah
  elde yatık görünüyorsa tek aday prefabtaki `Model` yerleşimidir. **El** yatık görünüyorsa aday
  başkadır: o slotun el yerleşimi (stüdyoda `Hand`'i çevirerek düzeltilir).
- ⚠️ Eller prefabın içine sürüklenmez (stage'in ayrı kökleri) — kaçak el arenada havada görünür.
- ⚠️ `WeaponKitBuilder` kavrama kayıtlarını **ezmez**; koşu sonunda kavraması yazılmamış silahları
  listeler, eski `GripSocket_*` işaretçilerini, `GripPoses` ağacını, prefabta kalmış `Hands/Hand_*`
  rig'ini ve sızmış `[VA El_*]` köklerini siler. `netItemId`/`holdMode` tablodan gelir ve EZİLİR.
- Aynı kayıt **üç yeri** besliyor: yerel tutuş · uzak oyuncudaki çizim (parmaklar dahil — uzak
  humanoid ele riglenmiş duruştan ölçülen kapanma oranı gider) · ön kabza kapısının/göstergesinin
  yeri. Biri düzelince üçü düzelir.
- Ön kabza soket yarıçapı silah başınadır (`secondaryGripRadius`, varsayılan 0.10 = 20 cm çap;
  görülen küre = kabul hacmi, ölçülen nokta boş elin KUMANDA ANCHOR'IDIR) ve Inspector'dan girilir —
  stüdyo ona dokunmaz.

## 2. Tracer + ön kabza göstergesi görünüm değerleri — playtest ayarı

`ItemDefinition`'daki `tracerColor` / `tracerWidth` / `tracerLifetime` / `tracerEveryNthRound`
(varsayılan 3) sahada gözle ayarlanır. Dokümana sayı yazılmaz.
Karar verilecek: her silahın tracer'ı farklı mı görünecek, yoksa hepsi aynı mı kalacak (altyapı
ikisini de destekliyor — alanlar silah başına, değerler şu an aynı).

Ön kabza tarafında ayarlanacaklar: **soket yarıçapı silah başınadır** (`secondaryGripRadius`,
varsayılan 0.10 = 20 cm çap — Inspector'dan girilir; görülen küre = kabul hacmi). `Weapon`
sabitleri (kod içinde, tüm silahlarda ortak): `SecondaryGripHoverRadius` (0.30 m — kürenin
görünmeye başladığı kumanda uzaklığı) · `IndicatorHoverAlpha`/`IndicatorReadyAlpha` · `IndicatorColor`.
Kürenin sanatı (`VA_GripSocket.prefab` + `M_GripSocket.mat`: renk/materyal) prefabtır, orada
düzenlenir; **1 m çap sözleşmesi** korunur (ölçeği `Weapon` verir).
⚠️ Ön kabza silah ana elde SALLANIRKEN tutuluyor: hareketli bir hedefe 10 cm dar geliyorsa önce
`secondaryGripRadius`'u büyüt — kod değişikliği değil, silah başına bir ayar (küre de büyür).
İsteğe bağlı: `WeaponCatalog.secondaryGripIndicatorPrefab`'a tasarlanmış bir soket sanatı bağlamak —
varsayılan küre silah kiti koşusuyla (`Configure All Build Elements`) üretilip bağlanıyor, yani **iş
yapılmadan da çalışıyor**. Soket silahın dönüşünü alır (küre için önemsiz).

## 3. İki elli yerel nişan kuralı — his kararı

Silah sabit ana eli mi izleyecek (bugünkü davranış), yoksa iki elin doğrultusuna mı hizalanacak?
⚠️ **Tel formatını ETKİLEMEZ**: iki elin pozu da telde olduğu için uzak istemci aynı kuralı kendi
tarafında yeniden uygular. Yani playtest'te serbestçe değiştirilebilir, protokol sabit kalır.

## 4. Faz 4 — bomba

Kendi dosyasında: `bomba.md` (ağ nesnesi değil; kendine hasar = dost ateşi anahtarı; protokol
sürümü sabit). Bu plandan `hit_report`'a alan **eklenmez** — spekülatif tel alanı kalıcı borçtur.

## 5. Dağıtım — ⚠️ ÜÇÜ BİRLİKTE

`PROTOCOL_VERSION` 3 → **4** ve `0x01`/`0x02` byte düzeni değişti. Sürüm uyuşmazlığı yalnız log
uyarısı ürettiği için (§1) **karışık kurulumda pozlar sessizce bozuk parse edilir** — v3 istemci ile
v4 sunucu bir arada çalışmaz. `scripts\deploy-player-apk.bat` + `deploy-admin-game.bat` +
`deploy-server.bat` aynı turda koşmalı.
