# VortexArena — Uygulama Planı (sıradaki işler)

> Bu klasör **yalnız henüz yapılmamış işleri** tutar. Tamamlanan fazların dokümanları silindi;
> kalıcı bilgileri `CLAUDE.md` + `Docs/` altına işlendi (eski metinler git geçmişinde durur).

**Şu an planlanmış iş yok** — klasör boş (yalnız bu README).

> **Faz 7 (mod altyapısı) ve Faz 8 (Herkes Tek / FFA modu) bitti**, dosyaları silindi.
> Faz 7: `ModeRules` şekil tanımı, bireysel skor, `MatchOutcome`, takım-agnostik `ModeHudBase`,
> admin'den maç süresi/skor limiti. Faz 8: `ffa` modu — takımsız, bireysel skor, sabit durarak
> canlanma, raf yerine grip ile rastgele silah (`WeaponGranter`). **Faz 8 protokole tek bir alan
> bile eklemedi** ve TDM'in davranışını değiştirmedi — Faz 7'nin doğru kesildiğinin kanıtı.
> Kalıcı bilgi: `Docs/ArenaNet-Protokol.md` §10.5 (kayıtlı modlar tablosu) +
> `Docs/Sistem-Ozeti.md` §3.9/§4/§7 + `CLAUDE.md` (silah rafsız mod reçetesi).

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
