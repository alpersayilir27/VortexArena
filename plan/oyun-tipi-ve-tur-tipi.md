# Oyun tipi · tur tipi — kalan iş

Taksonomi yazıldı: `GameType` (Hızlı Savaş / Çocuk Oyunları) katalogda, `maps.json`'da ve
`start_match` doğrulamasında; kural zemininde `weaponSource:"none"` ve `scoring:"shared"`.
Kural `Docs/ArenaNet-Protokol.md` §10.5 ve §11, davranış `Docs/Sistem-Ozeti.md`.

Kooperatif skorun **yazan** yolu ilk kooperatif modla birlikte yazıldı
(`MatchDirector.AddSharedScore`, `BurgerClientController`'ın skor satırı). Admin tarafında ayrı bir
dal gerekmedi: panelin yerleşimi `Teams == None` ile karar veriyor (kooperatif modda doğru cevap
zaten bu) ve tek `ScoreKind.Player` karşılaştırması **öldürmeye** bağlı iyimser bir tahmin —
silahsız modda hiç koşmuyor.

## Çocuk Oyunları sunumu — kalan iş

**Ses.** Ortak "hadi hadi" duyuruları çocuk ailesine uymuyor; mekanizma yazıldı
(`ModeAudioRegistry` kuralında oyun tipi filtresi + saniyeye göre klip çalan `Countdown`
tetikleyicisi — `Docs/Sistem-Ozeti.md` `ModeAudioRegistry`). Eksik olan **içeriktir**:

- [ ] Klipler (projede yok, üretken ses servisi de yapılandırılmamış): çocuk sesiyle/yumuşak
      tonla `Üç` · `İki` · `Bir` (her biri 1 sn'den kısa) + maç başı `Oyun!` / `Başla!`.
      `Assets/Audio/Announce/VO_Cocuk_*.wav`.
- [ ] `Resources/ModeAudioRegistry` asset'ine iki satır: oyun tipi **Çocuk Oyunları** ·
      `RoundStart` · maç başı klibi; oyun tipi **Çocuk Oyunları** · `Countdown` · klipler
      `[Bir, İki, Üç]` (indeks 0 = 1 sn kala). Klip gelmeden de satırlar eklenebilir: boş satır
      ortak "hadi hadi"yi çocuk maçında **susturur** (kural eşleşir, klip yok → sessiz).
- [ ] Ortak `MatchEndWarning` ("son 5 saniye") çocuk ailesinde de çalar; yumuşak bir klip
      istenirse aynı biçimde üçüncü satır.

**Karakter.** Çocuk oyunlarında oyuncular hâlâ arena karakteriyle (`Ch18`, Mixamo rig) çiziliyor;
projede başka rigli karakter yok. **Karar bekliyor:** hangi karakter (satın alma / üretim), tek
karakter mi takım rengine göre iki mi. Karar gelince yapılacak mekanizma:

- Katalogdan (`GameCatalog`, açık sahnenin `MapDefinition.GameType`'ı) oyun tipine göre
  **avatar prefabı seçimi**: `RemotePlayerSpawner.avatarPrefab` yerine tip başına prefab
  (`Kids` için ayrı `RemoteAvatar` varyantı) ve `LocalBodyAvatar` için `Resources` altında
  ikinci prefab.
- Yeni karakter Mixamo uyumlu humanoid rig olmalı: `NetworkCharacterRetargeter` config'i,
  `Tools > VortexArena > Avatars > Takım Gövdesini Kur`, `RemoteHitBox` damgaları ve
  `SkeletonStreamGuard` eklem denetimi ikinci prefab için de koşar.

## Doğrulama (kullanıcı koşar)

- Satırın altındaki bütün satırlar (mod, harita, süre, skor limiti, geri sayım, dost ateşi,
  kalibrasyon kipi) bir sıra aşağı kaydı ve **taşmadı** — kalibrasyon düğmeleri panelin içinde.
- Mevcut arenalar hiç dokunulmadan Hızlı Savaş: `Export Server Config` sonrası `maps.json`'da
  `"gameType": "quickbattle"`, sunucu açılış özetinde harita adının yanında tip görünüyor.
- `weapons:"none"` bir modla (geçici test SO'su) lobiden maça: çerçeve yok, grant yok, tetik
  sessiz, `0x03` atış olayı yok — **bilek kılıfındaki atılabilir de atılamıyor** (aynı ateş kapısı).
- Eski `maps.json` (gameType alanı olmayan) ile sunucu: haritalar Hızlı Savaş sayılıyor, hiçbir
  maç reddedilmiyor.
