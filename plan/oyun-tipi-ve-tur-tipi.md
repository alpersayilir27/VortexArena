# Oyun tipi · tur tipi — kalan iş

Taksonomi yazıldı: `GameType` (Hızlı Savaş / Çocuk Oyunları) katalogda, `maps.json`'da ve
`start_match` doğrulamasında; kural zemininde `weaponSource:"none"` ve `scoring:"shared"`.
Kural `Docs/ArenaNet-Protokol.md` §10.5 ve §11, davranış `Docs/Sistem-Ozeti.md`.

Kooperatif skorun **yazan** yolu ilk kooperatif modla birlikte yazıldı
(`MatchDirector.AddSharedScore`, `BurgerClientController`'ın skor satırı). Admin tarafında ayrı bir
dal gerekmedi: panelin yerleşimi `Teams == None` ile karar veriyor (kooperatif modda doğru cevap
zaten bu) ve tek `ScoreKind.Player` karşılaştırması **öldürmeye** bağlı iyimser bir tahmin —
silahsız modda hiç koşmuyor.

## Doğrulama (kullanıcı koşar)

- Satırın altındaki bütün satırlar (mod, harita, süre, skor limiti, geri sayım, dost ateşi,
  kalibrasyon kipi) bir sıra aşağı kaydı ve **taşmadı** — kalibrasyon düğmeleri panelin içinde.
- Mevcut arenalar hiç dokunulmadan Hızlı Savaş: `Export Server Config` sonrası `maps.json`'da
  `"gameType": "quickbattle"`, sunucu açılış özetinde harita adının yanında tip görünüyor.
- `weapons:"none"` bir modla (geçici test SO'su) lobiden maça: çerçeve yok, grant yok, tetik
  sessiz, `0x03` atış olayı yok — **bilek kılıfındaki atılabilir de atılamıyor** (aynı ateş kapısı).
- Eski `maps.json` (gameType alanı olmayan) ile sunucu: haritalar Hızlı Savaş sayılıyor, hiçbir
  maç reddedilmiyor.
