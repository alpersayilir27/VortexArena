---
title: Geliştirici Rehberi
---

# Geliştirici Rehberi

Bu bölüm **oyun tarafını yazan geliştirici** içindir. Ağ katmanını yazmadıysan, protokolü hiç
okumadıysan ve "silahımı ateşleyince ne çağıracağım?" sorusunun cevabını arıyorsan doğru yerdesin.

## Okuma sırası

| # | Sayfa | Ne verir |
|---|---|---|
| 1 | **[İlk Adımlar](Ilk-Adimlar.md)** | Projeyi aç, rolünü seç, sunucusuz test et, botlarla maç kur. ~15 dakika |
| 2 | **[Yemek Kitabı](Yemek-Kitabi.md)** | "Şunu yapmak istiyorum" → kopyala-yapıştır reçete. **Günlük olarak burayı kullanacaksın** |
| 3 | **[API Referansı](API-Referansi.md)** | Çağırabileceğin her şey: tip tip, üye üye, ne zaman çağrılır |
| 4 | **[Sahne Kurulumu](Sahne-Kurulumu.md)** | Yeni arena/sahne yaparken sahnede bulunması gerekenler |
| 5 | **[Yapma Listesi](Yapma-Listesi.md)** | Pahalıya öğrenilmiş tuzaklar. Bir şey "sessizce çalışmıyorsa" önce buraya bak |

---

## Bilmen gereken tek kural

Sistemin tamamı tek bir ayrım üzerine kurulu:

| | Kim karar verir | Örnek |
|---|---|---|
| **Poz** (kafa + iki el) | **İstemci** — yani sen | Oyuncu nerede duruyor, eli nereye bakıyor |
| **Her şey** | **Sunucu** | Can, ölüm, skor, maç fazı, takım, kurallar |

Pratikte bu şu demek:

```csharp
// ❌ YAPMA — canı yerelde düşürmek iki tarafı saptırır
target.hp -= 25f;

// ✅ YAP — sunucuya BİLDİR, sonucu ondan bekle
ArenaCombat.ReportHit(targetPlayerId, hitPoint, 25f, "ak47");
// ...canı sunucu düşürür ve NetEvents.OnHealthUpdate ile geri gönderir
```

**İyi haber:** hasarın SAYISINI sen belirlersin. Sunucuda silah tablosu yoktur — gönderdiğin sayı
aynen uygulanır. Yani denge ayarları, mesafeye göre düşen patlama, kafa vuruşu çarpanı, yay çekiş
gücü... hepsi senin tarafında yaşar ve **sunucuya hiçbir şey eklemeden** yeni silah ekleyebilirsin.

---

## Aklında tutman gereken üç şey

**1. Oyuncu fiziksel olarak yürür.** Rig'i, kamerayı, oyuncunun transformunu **asla taşıma**.
Işınlanma, knockback, "spawn noktasına götür" — hiçbiri yok. Ölüp canlanmak bile bir *konum*
değişimi değil *durum* değişimidir.

**2. Bağlantı yokken de çalışmalı.** Sunucusuz editör oturumunda oyun kodun aynen koşar; ağ
çağrılarının hepsi sessizce no-op'tur. `if (bağlıysa)` sarmalayıcıları yazma.

**3. Modun ne olduğunu tahmin etme.** `if (modeId == "ffa")` gibi bir zincir **yazma**. Modun şekli
telden gelir; `ModeRuntime`'dan oku. Bu sayede yeni mod eklemek senin kodunu değiştirmez.

---

## Nereye ne yazılır

```
Assets/
  _Shared/           ← "ikinci bir mod/arena bunu aynen kullanır mı?" → EVET olan her şey
    Core/            oyun kodu: arena, savaş, oyuncu, UI, katalog SO'ları, FX
    Arsenal/         silah prefabları + WeaponDefinition SO'ları
    FX/              paylaşılan efektler
  Arenas/            arena kutuları — arenaya ÖZEL kod yazılmaz
  Modes/<Mod>/       mod kutuları: {Scripts, Data, UI} — modlar birbirini REFERANSLAMAZ
```

Karar kuralı tek soru: **"İkinci bir mod ya da arena bunu aynen kullanır mı?"**
Evet → `_Shared`. Hayır → kendi kutusu.

> ⚠️ `_Shared` köküne asmdef'siz gevşek script koyma — `Assembly-CSharp`'a düşer ve hiçbir asmdef
> onu göremez. Kodun her zaman bir asmdef'in altında olmalı.
