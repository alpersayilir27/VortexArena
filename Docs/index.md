---
title: VortexArena — Dokümantasyon
---

# VortexArena

Free-roam VR PvP arena. Oyuncular fiziksel alanda **1:1 yürür** — ışınlanma yoktur, joystick'le
hareket yoktur. Meta Quest 3/3S, Unity 6000.3.20f1, URP. Ağ tarafı kendi .NET sunucumuz
(Mirror/NGO yok), tamamen offline LAN'da çalışır.

---

## Kim hangi kapıdan girer

| Sen kimsin? | Buradan başla |
|---|---|
| **Oyun geliştiricisiyim** — silah, mod, mekanik, efekt yazacağım | 👉 **[Geliştirici Rehberi](Gelistirici/index.md)** |
| Ağ/protokol üzerinde çalışıyorum | [ArenaNet Protokolü](ArenaNet-Protokol.md) — tel üzerindeki her mesajın tek doğruluk kaynağı |
| Sistemi bütün olarak anlamak istiyorum | [Sistem Özeti](Sistem-Ozeti.md) — ne var, nasıl çalışıyor, hangi bileşen ne yapıyor |
| İşletmede seansı ben yöneteceğim | [Kullanım Kılavuzu](Kullanim-Kilavuzu.md) — teknik olmayan dille operatör kılavuzu |
| Yeni bir işletmeye kurulum yapacağım | [İşletme Kurulumu](Isletme-Kurulum.md) — donanım, ağ, kalibrasyon, smoke test |

---

## Otuz saniyede sistem

```
   Quest 3 (oyuncu)  ─┐
   Quest 3 (oyuncu)  ─┤   WS 47821 (kontrol: JSON)
   Quest 3 (oyuncu)  ─┼── UDP 47822 (poz: 20 Hz binary) ──►  .NET Sunucu
   Windows (admin)   ─┘   UDP 47820 (beacon: sunucu kendini duyurur)      │
                                                                          │
                          maç fazı · can · skor · kurallar ◄──────────────┘
```

**Tek cümlelik otorite kuralı:**

> **Pozlar istemci-otoriter** (kafan/ellerin nerede — buna kimse karışmaz).
> **Geri kalan her şey sunucu-otoriter** (can, skor, maç fazı, kurallar).

Bu ayrım bütün mimarinin temelidir. Oyun kodu yazarken tek soruyu sor: *"bu bilgi hangi tarafa
ait?"* — canı yerelde düşürdüğün an iki taraf sapar.

---

## Bu siteyi çalıştırmak

```bat
docs-serve.bat          :: repo kökünde, çift tıkla → http://localhost:1111
```

İçerik doğrudan repodaki `Docs/` klasörüdür — bir `.md` dosyasını kaydettiğin anda tarayıcı
kendini yeniler. Yeni bilgisayarda bir kez `scripts\docs-setup.bat` çalıştırılır.

> **Doküman kodun bir parçasıdır.** Bu projede kural şudur: temel bir kodda değişiklik yapıldıysa
> (protokol, ağ akışı, maç kuralı, bileşen sorumluluğu, editör aracı) ilgili doküman **aynı
> commit'te** güncellenir. Kod ile doküman arasındaki sapma bu projede bug sayılır.
