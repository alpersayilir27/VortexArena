# Zombi modu — vuruş hissi (`zombie`)

Zombi modunda silah ağırlığı, isabet onayı, zombi tepkisi ve gore katmanı. Referans:
Propagation VR / Paradise Hotel, After the Fall (Quest co-op ölçeği), Arizona Sunshine.

## ⛔ Sınır: Hızlı Savaş'a dokunulmaz

Her hissiyat parçası `Assets/Modes/Zombie/` altında yaşar. Ortak koda (`_Shared/Core/Combat`)
yalnız **varsayılanı boş kanca** girer (`Weapon.DamageOverride` deseni): kanca `null` iken ortak kod
yolu bugünküyle birebir aynıdır. Kancayı zombi kurar ve yalnız `ModeRuntime.ModeId == zombie` +
oyuncu rolündeyken tutar, mod bitince yalnız kendi kancasını düşürür. Sunucu değişikliği yalnız
`ZombieMode` ve zombi nesnesi dallarında; protokol alanları isteğe bağlıdır, PvP mesajı değişmez.

## Verilmiş kararlar

| Konu | Karar |
|---|---|
| Ekran sarsıntısı | **Yok.** Kamera hiç oynamaz; ağırlık silah modeli, ses ve titreşimle verilir |
| Geri tepme | Yaylı (hafif aşmalı) silah başına profil: yukarı + rastgele yana sapma + dönme + geri kayma. İki el çarpanı 0,55, tek el 1,3 |
| Haptic | Meta Haptics SDK (`com.meta.xr.sdk.haptics` @203.0.0), yalnız bu modda; `.haptic` klipleri `Assets/Modes/Zombie/Haptics/` |
| Haptic olayları | Atış (silah sınıfına göre) · isabet onayı (atış titreşiminden sonra) · kafa · öldürme · zombi darbesi (iki el) · uzuv kopması |
| İsabet sesi | Vurana 2B kısa onay "tık"ı + kafa çatırtısı + öldürme onayı; zombiye acı sesi |
| Öldürme | Vuranın ekranında **tahminle hemen**: son bilinen can − yerelde gönderilen hasar ≤ 0 → ragdoll, ses, haptic; sunucu onayı yalnız teyit |
| Kafa atışı | Ölümcül kafa atışı her zaman **kafa patlatır**. Çarpan: hafif silahta 2 kafa, ağır silahta (magnum, AK) tek kafa yürüyeni indirir |
| Uzuv kopması | Ölümcül vuruşta isabet alan uzuv kopar (kafa · kol · bacak); canlıyken yalnız bacak: bacak hasarı eşiği aşan yürüyen/koşucu/çığlıkçı **sürünen** olur (sunucu durumu) |
| Ragdoll itkisi | Bölge itkisi × vuruş hasarı ölçeği (pompalı/magnum belirgin sert devirir) |
| Mikro duraklama | Kafa / ağır isabette yalnız o zombinin animatörü 40–80 ms yavaşlar, yalnız oyuncu başlığında |
| Sendeleme | Kısa pencerede biriken hasar eşiği → sendeleme + geri itme (≤ 0,4 m, admin beyninde) |
| Kan | Zombinin arkasındaki duvara/zemine havuzlu kan izi; zombi gövdesinde kemiğe bağlı yara izi |
| HUD | Hitmarker kafa (kırmızı, büyük) ve öldürme (kalın, uzun) varyantı; hasar sayısı/nişangâh yok |
| Zombi darbesi | Oyuncuya titreşim + acı sesi + yön yayı (sunucu `health_update`'e kaynak zombiyi koyar) |
| Ayar | Tüm sayılar zombi SO'larında (`ZombieFeel`, `ZombieReactionTuning`); kodda sabit kalmaz |
| Protokol | `PROTOCOL_VERSION` 26 → 27: `health_update` kaynak nesnesi + zombinin "sürünen oldu" durumu |

## Kalan işler

- [ ] **Dağıtım** — `PROTOCOL_VERSION` 27: sunucu + admin + oyuncu APK'sı **aynı turda**.
- [ ] **Gözlükle doğrulama.**
