# VortexArena — Uygulama Planı (sıradaki işler)

> Bu klasör **yalnız henüz yapılmamış işleri** tutar. Tamamlanan fazların dokümanları silindi;
> kalıcı bilgileri `CLAUDE.md` + `Docs/` altına işlendi (eski metinler git geçmişinde durur).

| Dosya | İçerik | Durum |
|---|---|---|
| `faz8-ffa-modu.md` | **Herkes Tek (FFA)** modu: bireysel skor, "sabit dur" canlanması, raf yerine hold ile rastgele silah. Protokole yeni alan EKLEMEZ | 📋 planlandı |

> **Faz 7 (mod altyapısı) bitti** ve dosyası silindi: `ModeRules` şekil tanımı, bireysel skor,
> `MatchOutcome`, takım-agnostik `ModeHudBase`, admin'den maç süresi/skor limiti. TDM davranışı
> birebir korundu. Kalıcı bilgi: `Docs/ArenaNet-Protokol.md` §10.5 + `Docs/Sistem-Ozeti.md` §3.9.
> **Faz 8 o yüzeyin ilk tüketicisidir** ve protokole yeni alan eklemez.

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
