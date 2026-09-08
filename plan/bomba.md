# Bomba ve atılabilir eşyalar — KALAN İŞLER

> **Kod ve doküman yazıldı** (protokol sürümü DEĞİŞMEDİ). Kalıcı bilgi dokümana taşındı:
> atma sözleşmesi + determinizm `Docs/ArenaNet-Protokol.md` §6.4 · vuruş kapıları §10.2/§10.3 ·
> bileşenler `Docs/Sistem-Ozeti.md` §4 · tuzaklar §7 · reçete `Yemek-Kitabi` §3 ve §11.3.
> Bu dosya yalnız **yapılmamış** olanı tutar; hepsi bitince silinir.

Yazılanların özeti (referans için): sunucuda ölüm sonrası penceresi + kendine vuruş kapısı,
kendini öldürmede skor/kill yazılmaması · `ThrowableDefinition`/`ThrowableTrigger` ·
`Throwable` (nicelikleme, avatar muafiyeti, yetişme, fitil, temasta sönüm) · `ThrowableEffect`/
`BlastEffect` · `WristHolster` · `WeaponGranter.StowHeld/RestoreStowed` ·
`ArenaCombat.ReportAreaSelfHit` + `requireLineOfSight` · `HeldItems` atılabilir slotu ·
`RemoteShotFx` `KIND_THROW` tüketicisi · kill feed "kendini havaya uçurdu" satırı.

## 1. Unity içeriği

Reçetenin tamamı `Docs/Gelistirici/Yemek-Kitabi.md` §11.3.

- [ ] Kılıftaki `refillClip` boş (kod boş referansa dayanıklı).

## 2. Playtest ayarı

- [ ] **Görsel yarıçap ile `blastRadius` (4 m) okunabilir mi:** oyuncu tehlikeli alanı efekte bakarak
      tahmin ediyor. Efekt hasar alanından belirgin büyükse "vurulmayacağım yerde öldüm", belirgin
      küçükse "dumanın içindeydim, hasar almadım" olarak okunur — ikisi de balans değil **okunurluk**
      şikâyeti olarak gelir. Şok dalgası halkası (`Shockwave.startSize`) bu eşleşmenin en görünür
      ögesidir.

## 3. Kod tarafında bilinçli olarak YAPILMAYANLAR

- **Askıdan dönen tek kullanımlık silah dolu şarjörle gelir** — bugünkü "grip bırak-bas" davranışı da
  dolu silah veriyor, fark yalnız rastgeleliğin atlanması.
- Kılıf boşken **gizli**; "sönük siluet" sanat gerektiriyor.

## 4. Doğrulama (kullanıcı koşar)

- [ ] Aynı yerde ikinci bomba: siper artık kırık, soğurma yok, hasarın tamamı mesafe düşümüyle gider.
- [ ] Kırılamayan geometrinin (arena iç duvarı/sütunu) arkasındaki oyuncu hiç hasar almaz.
- [ ] Arka arkaya birkaç patlamada kare düşüşü/takılma yok; ilk patlama da takılmıyor (havuz fitil
      başında kuruluyor).
