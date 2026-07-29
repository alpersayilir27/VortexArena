# VortexArena — Uygulama Planı (sıradaki işler)

> Bu klasör **yalnız henüz yapılmamış işleri** tutar: biten işin dokümanı silinir, kalıcı bilgisi
> `CLAUDE.md` + `Docs/` altına işlenir (eski metin git geçmişinde kalır).

| Planlanmış iş | Dosya |
|---|---|
| Sahada yapılacak duman testi (durum modeli + mekan yapısı + duraklatma + WPF launcher) — ⚠️ önce APK/admin build'i yenilenmeli, `PROTOCOL_VERSION` 2→3 | `duman-testi.md` |
| Kırılabilir objeleri sunucu-otoriter yapmak (yerel `Health` kaldırıldı, yerine ağsal obje canı) | `agsal-kirilabilir-objeler.md` |

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
