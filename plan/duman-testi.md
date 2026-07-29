# Kalan duman testi (sahada, gerçek başlıkla)

> Bu dosya **yapılmamış doğrulamayı** tutar, tasarımı değil. Tasarımın tamamı dokümana işlendi:
> durum modeli `Docs/ArenaNet-Protokol.md` §10.1, duraklatma §5.2, mekan seçimi §11.1.
> Adımlar geçince bu dosya silinir.

## Neden hâlâ duruyor

Aşağıdakiler kod incelemesiyle ya da tek başına sunucu çalıştırarak doğrulanamaz — gerçek istemci,
sahne yükleme ve fiziksel hareket ister. Bugüne kadar doğrulananlar: sunucu derlemesi (0 hata),
Unity derlemesi, `maps.json` export'u, sunucunun mekan listesi ve her mekanın kendi lobisini
çözmesi (`Outdoor12x12 → Lobby12x12`, `VortexAntep → LobbyVortexAntep`), launcher derlemesi +
testleri ve sunucunun `--venue <ad>` ile açılışı (elle çalıştırılarak doğrulandı).
**Launcher'ın düğmelerinden süreç başlatma yolu henüz tıklanarak denenmedi.**

## ⚠️ Önce: `PROTOCOL_VERSION` 2 → 3

Sahadaki **tüm APK ve admin build'leri yenilenmeden** test edilemez — eski istemci bağlanamaz.
`scripts\deploy-player-apk.bat` + `scripts\deploy-admin-game.bat`.

## Adımlar

- [ ] **Launcher → sunucu:** launcher'da (`deploy\launcher\VortexArena.Launcher.exe`) sunucu
      exe'si seçilince mekan listesi **kendiliğinden** doluyor (`Outdoor12x12`, `VortexAntep`;
      ikisi de "lobi var"); mekan seçmeden **Sunucuyu Başlat** uyarı veriyor; seçince sunucu
      penceresinde `[Venue] '<mekan>' yapılandırmadan seçildi` satırı çıkıyor.
- [ ] **Launcher → yönetim oyunu:** **Yönetimi Başlat** admin build'ini açıyor, oyun IP sormadan
      bağlanıyor; **Durdur** onu kapatıyor. Launcher kapatılınca **sunucu ayakta kalıyor**.
- [ ] **Mekan seçimi:** sunucu elle (launchersız) açıldığında liste **yalnız** `Outdoor12x12` ve
      `VortexAntep` gösteriyor; seçilen mekanın dışındaki harita admin panelinde görünmüyor.
- [ ] **Mekan lobisi:** her iki mekanda da oyuncular açık sahne olarak o mekanın kendi lobisine
      düşüyor; `VortexAntep` lobisi arenayla aynı geometride (aynı fiziksel oda).
- [ ] **Lobide ateş:** silah alınabiliyor, ateş edilebiliyor, **hasar yok** (`fireWhilePaused`).
- [ ] **Maç akışı:** `start_match` → yükleme → 5 sn geri sayım → `playing`'de hasar var →
      süre/skor limitinde `finished` → ~10 sn sonra açık sahneye dönüş.
- [ ] **Duraklatma:** koşan maçta **DURAKLAT** → süre duruyor, hasar kapanıyor, skorlar duruyor →
      **DEVAM ET** → maç kaldığı yerden sürüyor. Geri sayımda ve lobide düğme pasif.
- [ ] **Çoklu admin:** ikinci admin penceresinde de düğme **DEVAM ET** yazıyor ve durum satırında
      kimin duraklattığı görünüyor.
- [ ] **Kabuk lobi:** `Lobby.unity` artık yalnız durum metni + gizli IP paneli; roster/hazır/takım
      düğmeleri kaldırıldı, ekranda boşluk/bozulma yok.
