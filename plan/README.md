# VortexArena — Uygulama Planı (sıradaki işler)

> Bu klasör **yalnız henüz yapılmamış işleri** tutar: biten işin dokümanı silinir, kalıcı bilgisi
> `CLAUDE.md` + `Docs/` altına işlenir (eski metin git geçmişinde kalır).

| Planlanmış iş | Dosya |
|---|---|
| **Meta Movement SDK ile full body avatar**: kod, protokol (`0x07`/`0x08`), doküman ve **prefab kurulumu bitti** (iki avatar da Ch15 retarget config'i + `ArenaNetCharacterBehaviour` ile kurulu, 66 eklem). Kalan: blob boyutu/paket bütçesi ölçümü ve boy-bacak ayarları | `meta-movement-full-body.md` |
| **Maç sonu bekleme · toplanma · dost ateşi anahtarı**: kod/prefab/doküman **bitti**, sunucu derlemesi temiz. Kalan: Unity derlemesi + panel ve davranış doğrulaması | `mac-sonu-toplanma-dost-atesi.md` |
| Elde tutulan eşya + atış olayları: **Faz 0–3 + ön kabza kapısı/göstergesi + olay zamanlaması bitti**. Kalan: ⚠️ **13 silahın kavramasını yazmak** (araç hazır — `Kavrama Pozu Stüdyosu`, prefab kipinde, el başına poz + silaha özel parmak rigi) · tracer/gösterge değerleri · bomba | `elde-tutulan-esya-ve-atis-olaylari.md` |
| **Kavrama: avuç çerçevesi + ikinci el**: kod (`HandGripPivot`, `ItemGripSolver`) ve doküman **bitti**. Kalan: avuç ofsetini ve anchor→bilek sabitini başlıkta/editörde ölçmek + 13 silahın kavramasını stüdyoda yazmak (yazılmadan iki elli çözüm koşmaz: ön kabza ekseni oluşmaz) + doğrulama | `iki-elli-kavrama.md` |
| **Bağlantı kopması**: kod, protokol (v8) ve doküman **bitti** — "çevrimdışı" kalktı, yerine `connected`/`reconnecting`/`left` + maç katılımcısı defteri geldi. Kalan: `dotnet build` + Unity derlemesi + doğrulama listesi (⚠️ v8 tüm başlıklara yeni APK ister) | `baglanti-kopmasi-ve-mac-katilimcilari.md` |
| Kırılabilir objeleri sunucu-otoriter yapmak (yerel `Health` kaldırıldı, yerine ağsal obje canı) | `agsal-kirilabilir-objeler.md` |
| **Yüzey çarpma efektleri** (kar duvarda kar, tahtada talaş, metalde kıvılcım): yüzey kimliği **materyalden** çözülür (tag/layer değil) + `SurfaceTag` override; havuzlu prefab, `ArenaCombat.ReportImpact` tek kapısı. Protokole ekleme YOK — uzak taraf çarpma noktasını atış olayından türetiyor. Henüz kod yazılmadı | `yuzey-carpma-efektleri.md` |
| **Oyuncu boy ölçeği** (operatör düğmesiyle ölçülen üniform `bodyScale`, `lobby_state` ile bir kez taşınır; kalibrasyonda uç ofseti kumanda rotasyonundan bağımsızlaştı): kod, protokol (v9), prefablar ve doküman **bitti**. Kalan: doğrulama. ⚠️ v9 tüm başlıklara yeni APK ister | `oyuncu-boy-olcekleme.md` |
| **Engel ihlali** (kafası iç engele giren oyuncu kör kalır, 3 sn sonra canı erimeye başlar, 8. sn'de ölür; namlusu engelin içinden geçen silah ateş etmez): kod, protokol (v11) ve doküman **bitti** — sözleşme `Obstacle` layer'ı (⚠️ collider KONVEKS olmalı), bayrak `SnapshotEntry` bit5. Kural **yalnız kafayı** yargılar (el/kol/gövde ceza üretmez, oran kuralı yok), karartma içerideyken koşulsuz tam siyahtır, can kaybının kırmızısı karartmanın **üstünde** ayrı bir katmandır ve **ölümü tek kod yazar** (`KillPlayerLocked`; engelde canlanma yok). Kalan: arena sahnelerindeki iç engellerin layer'a alınması (sahne başına elle, açmadan görülemez) + kesişim VFX'i + doğrulama. ⚠️ Yeni APK ister | `engel-ihlali.md` |
| **Mekan boyut maketi**: kod, boyut dosyaları, prefablar, sahneler ve doküman **bitti**. Kalan: doğrulama (maket gidiş-dönüşü · loader'ın ikinci çalıştırması · Play'de karartma rampası · admin paneli) | `mekan-boyut-maketi.md` |
| **Admin: oyuncuyu canlandır düğmesi** (`revive_player` — operatör ölü oyuncuyu her modda elle canlandırır; canlanmanın tek yolu `revive_request` olduğu için şartı sağlayamayan oyuncunun tek kurtarma yolu budur). Protokol, sunucu, admin istemcisi, satır düğmesi (`CAN`) ve doküman **bitti**. Kalan: HUD'a toplu canlandırma düğmesi + doğrulama listesi | `admin-oyuncu-canlandirma.md` |
| **Kalibre modu + T-poz sağlamlaştırma + ölçek geri bildirimi**: kod (protokol **v13**, sunucu, istemci, admin paneli üç mod düğmesi) ve doküman **bitti**. Kalan: doğrulama listesi + adb alan-verisi temizliği denemesi. ⚠️ v13 tüm başlıklara yeni APK ister | `kalibre-modu-ve-olcek.md` |
| **Admin ihlal görünürlüğü** (engel + alan dışı halkada renk/frekans, sunucu-kaynaklı ihlal akışı ve defteri, alan dışında ateş kapısı): kod, protokol (**v14** — rezervdeki bit7 `FLAG_OUT_OF_BOUNDS`, tel formatı ve paket boyu DEĞİŞMEDİ) ve doküman **bitti**. Kalan: `GameSoundBank.adminViolation` uyarı ses klibi + doğrulama listesi. ⚠️ Silah/namlu engel durumu tele girmez — kaynağında zaten engelleniyor. ⚠️ v14 tüm başlıklara yeni APK ister | `admin-ihlal-gorunurlugu.md` |

> Sıradaki büyük iş **bulut kalibrasyonu** (Meta grup / paylaşılan uzamsal anchor ile toplu
> hizalama). Henüz planlanmadı; altyapısı hazır: `set_calibration.source` `"cloud"` değerini
> kabul ediyor, `clear_calibration{playerId:0}` toplu sıfırlama yapıyor ve
> `ArenaCalibrator.AlignRigToAnchorPose` `internal` seam olarak açık
> (`Docs/ArenaNet-Protokol.md` §10.6).

## Nereye bakmalı

| Konu | Dosya |
|---|---|
| Sistem bugün ne, nasıl çalışıyor, hangi bileşen ne yapıyor | `Docs/Sistem-Ozeti.md` |
| Protokol (mesajlar, sabitler, kurallar) — **TEK doğruluk kaynağı** | `Docs/ArenaNet-Protokol.md` |
| Mimari talimatlar + içerik ekleme reçeteleri | `CLAUDE.md` |
| Çalışma kuralları | `.claude/rules/` |

## Bir faz bitince

1. Kalıcı olan her şey dokümana yazılır (`.claude/rules/docs-sync.md` tablosu): protokol
   `Docs/ArenaNet-Protokol.md`'ye, bileşen/akış `Docs/Sistem-Ozeti.md`'ye, mimari/reçete
   `CLAUDE.md`'ye, tuzaklar `Sistem-Ozeti.md` §7'ye.
2. Faz dosyası **silinir** — planın kendisi arşivlenmez, doküman güncel kalır.
3. `plan/README.md`'den satırı çıkarılır.
