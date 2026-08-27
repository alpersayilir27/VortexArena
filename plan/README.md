# VortexArena — Uygulama Planı (sıradaki işler)

> Bu klasör **yalnız henüz yapılmamış işleri** tutar: biten işin dokümanı silinir, kalıcı bilgisi
> `Docs/` altına işlenir (eski metin git geçmişinde kalır).

| Planlanmış iş | Dosya |
|---|---|
| **Meta Movement SDK ile full body avatar**: kod, protokol (`0x07`/`0x08`), doküman ve **prefab kurulumu bitti** (iki avatar da Ch15 retarget config'i + `ArenaNetCharacterBehaviour` ile kurulu, 66 eklem). Kalan: blob boyutu/paket bütçesi ölçümü ve boy-bacak ayarları | `meta-movement-full-body.md` |
| **Maç sonu bekleme · toplanma · dost ateşi anahtarı**: kod/prefab/doküman **bitti**, iki taraf da derleniyor. Kalan: panel ve davranış doğrulaması | `mac-sonu-toplanma-dost-atesi.md` |
| Elde tutulan eşya + atış olayları: kod, tel formatı, stüdyo ve **13 silahın kavrama kayıtları bitti**. Kalan: tracer/ön kabza göstergesi değerlerinin sahada oturması · iki elli nişan kuralının his kararı | `elde-tutulan-esya-ve-atis-olaylari.md` |
| **Bomba ve atılabilirler**: sunucu, istemci kodu ve doküman **bitti** — protokol sürümü DEĞİŞMEDİ (sol bilek kılıfı · silah askıya alma · 5 sn fitil · patlamadan 3 sn sonra dolum · mermi sınırı yok · ağ nesnesi değil, yerel deterministik balistik · kendine hasar = dost ateşi anahtarı · atıcı ölse de hasar verir). Molotof/flashbang/sis aynı zemine oturur, **yeni tür sürüm artırmaz**. Prefab, tanım asset'i ve rig'deki kılıf kuruldu. Kalan: bombanın kavrama kaydı + model/FX/ses içeriği + sunucu derlemesi + doğrulama | `bomba.md` |
| **El kavrama sistemini eşya geneline açmak**: **F1 (stüdyo hedefi = eşya tanımı) · F2 (`HeldItems` slot defteri) · F3 (üç bağımsız eksen — alma yolu / örnekleme / bırakma, `GripSocket`, `NetObjectGrabBridge`, kural↔prefab bekçisi) yazıldı**; mevcut silah kayıtlarına dokunulmadı (üç eksenin 0. indeksi bugünkü davranış); ilk dünya propları Hamburgerci ile kuruldu. Kalan: propların kavrama pozu · doğrulama · fırlatma hızı ayarı kararı | `el-kavrama-genellestirme.md` |
| **Ağ nesnesi modeli** (`NetObject`): **B1 + B2 + B3 yazıldı** (**protokol v18**) — kimlik/tür, sahiplik (`object_grab` · elden çıkış/durma ayrımı · kopmada serbest bırakma), UDP obje pozu (`0x09` + `0x05` obje bölümü), obje olayları, dinamik doğuş/ölüm, `IGameMode.OnObjectEvent`; ilk tam tüketicisi Hamburgerci ile içerik kurulumu da yapıldı. Kalan: doğrulama + yeni build turu | `ag-nesne-modeli.md` |
| **Oyun tipi / tur tipi**: Hızlı Savaş · Çocuk Oyunları — kod ve doküman **bitti** (katalog, `maps.json.gameType`, `start_match` doğrulaması, `WeaponSource.None`, `ScoreKind.PlayerAndShared`); admin panelinin "Oyun tipi" satırı prefabda **bağlı**. kooperatif skorun yazan yolu ilk kooperatif modla birlikte yazıldı. Kalan: doğrulama | `oyun-tipi-ve-tur-tipi.md` |
| **Çocuk Oyunları — Hamburgerci** (ağ nesnesi modelinin ilk tam tüketicisi): mod, oyun mantığı, doküman ve prototip içerik kurulumu **bitti** — dükkân sahnesi, tür asset'leri, spawn kataloğu ve HUD'lar yerinde. Kalan: gerçek modeller/animasyonlar/sesler, dört propun kavrama pozu, doğrulama | `cocuk-oyunlari-hamburgerci.md` |
| **Çocuk Oyunları — Köstebek Ezme** (ailenin ilk yarışmalı oyunu): kırmızı–mavi takımlar, iki elde balyoz, zemindeki deliklerden 2 sn'liğine çıkan köstebekler; kendi rengini ezmek takıma +puan, rakip renge vurmak kendi takımından −puan; ölüm/taban yok, süre bitince yüksek skor kazanır, oyuncu başına doğru/yanlış/katkı tutulur. Tasarım + ağ sözleşmesi planda hazır — **henüz kod yazılmadı** | `cocuk-oyunlari-kostebek-ezme.md` |
| **Sunucu kapanışı**: kod ve doküman **bitti** (`ServiceShutdown` + dört serviste `StopAsync`, `Program.ShutdownAsync`; Ctrl+C · pencere kapatma · süreç çıkışı aynı idempotent yolu koşar). Kalan: derleme + doğrulama listesi | `sunucu-kapanis.md` |
| **Kavrama: avuç çerçevesi + ikinci el**: kod (`HandGripPivot`, `ItemGripSolver`), doküman ve 13 silahın dört kavrama kaydı **bitti**. Kalan: doğrulama | `iki-elli-kavrama.md` |
| **Bağlantı kopması**: kod, protokol (v8) ve doküman **bitti** — "çevrimdışı" kalktı, yerine `connected`/`reconnecting`/`left` + maç katılımcısı defteri geldi. Kalan: `dotnet build` + doğrulama listesi (⚠️ v8 tüm başlıklara yeni APK ister) | `baglanti-kopmasi-ve-mac-katilimcilari.md` |
| **Kırılabilir objeler** — ağ nesnesi modelinin ilk tüketicisi: kod ve doküman **bitti** (`BreakableObject` sunumu, patlamanın ağ nesnesine geçmesi, hasar collider'ı bekçisi). Shader, iki tür asset'i, iki prefab, kırılma efekti ve bir lobi yerleşimi kuruldu. Kalan: arenalara siper yerleşimi (seviye tasarımı), gerçek kırılma sesi + doğrulama | `agsal-kirilabilir-objeler.md` |
| **Yüzey çarpma efektleri** (kar duvarda kar, tahtada talaş, metalde kıvılcım): yüzey kimliği **materyalden** çözülür (tag/layer değil) + `SurfaceTag` override; havuzlu prefab, `ArenaCombat.ReportImpact` tek kapısı. Protokole ekleme YOK — uzak taraf çarpma noktasını atış olayından türetiyor. Henüz kod yazılmadı | `yuzey-carpma-efektleri.md` |
| **Oyuncu boy ölçeği** (operatör düğmesiyle ölçülen üniform `bodyScale`, `lobby_state` ile bir kez taşınır; kalibrasyonda uç ofseti kumanda rotasyonundan bağımsızlaştı): kod, protokol (v9), prefablar ve doküman **bitti**. Kalan: doğrulama. ⚠️ v9 tüm başlıklara yeni APK ister | `oyuncu-boy-olcekleme.md` |
| **Engel ihlali** (kafası iç engele giren oyuncu kör kalır, 3 sn sonra canı erimeye başlar, 8. sn'de ölür; namlusu engelin içinden geçen silah ateş etmez): kod, protokol (v11) ve doküman **bitti** — sözleşme `Obstacle` layer'ı (⚠️ collider KONVEKS olmalı), bayrak `SnapshotEntry` bit5. Kural **yalnız kafayı** yargılar (el/kol/gövde ceza üretmez, oran kuralı yok), karartma içerideyken koşulsuz tam siyahtır, can kaybının kırmızısı karartmanın **üstünde** ayrı bir katmandır ve **ölümü tek kod yazar** (`KillPlayerLocked`; engelde canlanma yok). Kalan: arena sahnelerindeki iç engellerin layer'a alınması (sahne başına elle, açmadan görülemez) + kesişim VFX'i + doğrulama. ⚠️ Yeni APK ister | `engel-ihlali.md` |
| **Mekan boyut maketi**: kod, boyut dosyaları, prefablar, sahneler ve doküman **bitti**. Kalan: doğrulama (maket gidiş-dönüşü · loader'ın ikinci çalıştırması · Play'de karartma rampası · admin paneli) | `mekan-boyut-maketi.md` |
| **Kalibre modu + T-poz sağlamlaştırma + ölçek geri bildirimi**: kod (protokol **v13**, sunucu, istemci, admin paneli üç mod düğmesi) ve doküman **bitti**. Kalan: doğrulama listesi + adb alan-verisi temizliği denemesi. ⚠️ v13 tüm başlıklara yeni APK ister | `kalibre-modu-ve-olcek.md` |
| **Admin ihlal görünürlüğü** (engel + alan dışı halkada renk/frekans, sunucu-kaynaklı ihlal akışı ve defteri, alan dışında ateş kapısı): kod, protokol (**v14** — rezervdeki bit7 `FLAG_OUT_OF_BOUNDS`, tel formatı ve paket boyu DEĞİŞMEDİ) ve doküman **bitti**. Kalan: `GameSoundBank.adminViolation` uyarı ses klibi + doğrulama listesi. ⚠️ Silah/namlu engel durumu tele girmez — kaynağında zaten engelleniyor. ⚠️ v14 tüm başlıklara yeni APK ister | `admin-ihlal-gorunurlugu.md` |

> Sıradaki büyük iş **bulut kalibrasyonu** (Meta grup / paylaşılan uzamsal anchor ile toplu
> hizalama). Henüz planlanmadı; altyapısı hazır: `set_calibration.source` `"cloud"` değerini
> kabul ediyor, `clear_calibration{playerId:0}` toplu sıfırlama yapıyor ve
> `ArenaCalibrator.AlignRigToAnchorPose` `internal` seam olarak açık
> (`Docs/ArenaNet-Protokol.md` §10.6).

## Dağıtım — protokol sürümü artınca

⚠️ **Sürüm artışı = tüm başlıklara yeni APK + admin + sunucu, AYNI turda**
(`scripts\deploy-player-apk.bat` + `deploy-admin-game.bat` + `deploy-server.bat`). Karışık kurulum
hata vermez, **sessizce yanlış çalışır**: bugünkü sürümde (`v18`) `0x05`'in başlığı kaydığı için
bozulan şey obje değil **snapshot'ın tamamıdır** — uzak oyuncular çöp pozlara ışınlanır.

Yukarıdaki dosyalarda bekleyen doğrulamaların **hepsi aynı tura bağlıdır**; ayrı ayrı koşturulacak
işler değildir. Sunucu derlemesi (`dotnet build`) de o turun içindedir.

## Değişmeyecekler (karar verildi, yeniden açılmaz)

- Sunucu incelemesinin şu maddeleri **uygulanmaz**; bugünkü davranışlar bilinçli kararlardır: maç
  başı sıfırlama listesi, mod duraklamasında maç bitişi, tek lider kuralı, anında `match_state`, WS
  gönderim kuyruğu, konsol QuickEdit. (Ölüm sonrası hasar penceresi bu listede DEĞİL: bomba
  kararıyla `bomba.md`'ye girdi.)
- Lag compensation / rewind, interest management: oyun hiç online olmayacak, gerekmez.
- Sunucu test projesi: ileride.

## Nereye bakmalı

| Konu | Dosya |
|---|---|
| Sistem bugün ne, nasıl çalışıyor, hangi bileşen ne yapıyor | `Docs/Sistem-Ozeti.md` |
| Protokol (mesajlar, sabitler, kurallar) — **TEK doğruluk kaynağı** | `Docs/ArenaNet-Protokol.md` |
| İçerik ekleme reçeteleri | `Docs/Gelistirici/Yemek-Kitabi.md` |
| Yapılmayacaklar (yasaklar) | `Docs/Gelistirici/Yapma-Listesi.md` |
| Hangi soru hangi dokümanda (giriş kapısı) | `CLAUDE.md` |
| Çalışma kuralları | `.claude/rules/` |

## Bir faz bitince

1. Kalıcı olan her şey dokümana yazılır (`.claude/rules/docs-sync.md` tablosu): protokol
   `Docs/ArenaNet-Protokol.md`'ye, bileşen/akış `Docs/Sistem-Ozeti.md`'ye, reçete
   `Docs/Gelistirici/Yemek-Kitabi.md`'ye, yasak `Docs/Gelistirici/Yapma-Listesi.md`'ye,
   tuzaklar `Sistem-Ozeti.md` §7'ye.
2. Faz dosyası **silinir** — planın kendisi arşivlenmez, doküman güncel kalır.
3. `plan/README.md`'den satırı çıkarılır.
