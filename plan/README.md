# VortexArena — Uygulama Planı (sıradaki işler)

> Bu klasör **yalnız henüz yapılmamış işleri** tutar: biten işin dokümanı silinir, kalıcı bilgisi
> `CLAUDE.md` + `Docs/` altına işlenir (eski metin git geçmişinde kalır).

| Planlanmış iş | Dosya |
|---|---|
| **Meta Movement SDK ile full body avatar**: kod, protokol (`0x07`/`0x08`), doküman ve **prefab kurulumu bitti** (iki avatar da Ch15 retarget config'i + `ArenaNetCharacterBehaviour` ile kurulu, 66 eklem). Kalan: blob boyutu/paket bütçesi ölçümü ve boy-bacak ayarları | `meta-movement-full-body.md` |
| **Maç sonu bekleme · toplanma · dost ateşi anahtarı**: kod/prefab/doküman **bitti**, sunucu derlemesi temiz. Kalan: Unity derlemesi + panel ve davranış doğrulaması | `mac-sonu-toplanma-dost-atesi.md` |
| **Arenaya dağılmış silah**: raf sistemi kaldırıldı, kaynağın adı `WeaponCanvas` oldu ve yerleşim **elle** yapılacak (kod bitti) → kalan iş üç arenaya silah yerleştirmek; o zamana kadar TDM · turnuva silahsız, FFA ve lobi `random` kullandığı için çalışıyor | `arenaya-dagilmis-silah.md` |
| **Turnuva modu** (`tournament`): kod/asset/export **bitti**. Kalan: doğrulama listesi + silah kaynağı (yukarıdaki iş) | `turnuva-modu.md` |
| Elde tutulan eşya + atış olayları: **Faz 0–3 + soket kavrama + olay zamanlaması bitti**. Kalan: ⚠️ **kavrama pozu ayarı** (araç hazır — `Grip Socket` işaretçisi + `Write Grip Sockets To Definition`; sayılar hâlâ sıfır) · tracer/soket değerleri · bomba | `elde-tutulan-esya-ve-atis-olaylari.md` |
| Kırılabilir objeleri sunucu-otoriter yapmak (yerel `Health` kaldırıldı, yerine ağsal obje canı) | `agsal-kirilabilir-objeler.md` |
| **Mekan boyut maketi**: kod, boyut dosyaları, prefablar, sahneler ve doküman **bitti**. Kalan: doğrulama (maket gidiş-dönüşü · loader'ın ikinci çalıştırması · Play'de karartma rampası · admin paneli) | `mekan-boyut-maketi.md` |

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
