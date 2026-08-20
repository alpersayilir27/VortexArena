# Bağlantı kopması: kalan iş (doğrulama)

Kod, protokol ve doküman yerinde: `Docs/ArenaNet-Protokol.md` §1 (`HEARTBEAT_TIMEOUT` ·
`RECONNECT_GRACE` · `PROTOCOL_VERSION` 8) · §2 (üç değerli bağlantı durumu) · §5.3
(`connection`/`reconnectSeconds`/`inMatch`) · §5.4 · §8 · §10.2 (maç katılımcısı defteri);
`Docs/Sistem-Ozeti.md` §4 (`PlayerRegistry`, `AdminRoster`, `ConnectionOverlay`) + §7 son madde.

⚠️ **`PROTOCOL_VERSION` 7 → 8:** tüm başlıklara yeni APK, admin'e yeni build, sunucuya yeni publish
gerekir. Karışık sürümde eski admin `connection` alanını bulamaz ve **her satırı bağlı çizer** —
kopan oyuncular hiç fark edilmez.

---

## Doğrulama listesi

**Kopma → geri dönüş**
- [ ] Maç sırasında bir gözlüğün Wi-Fi'ı kesilir → admin satırı `yeniden bağlanıyor · N sn` der ve
      sayaç **kendiliğinden ilerler** (başka bir roster değişikliği beklemeden).
- [ ] Aynı gözlükte ekran: "BAĞLANTI KOPTU · çıkarılmana N sn / maç istatistiklerin korunuyor".
- [ ] Wi-Fi geri gelir → aynı ad, aynı forma numarası, aynı takım, aynı `kills/deaths/score`;
      roster'da **ikinci bir satır açılmaz**.

**Süre dolması**
- [ ] `RECONNECT_GRACE` (45 sn) dolar → admin satırı `ayrıldı`ya döner ve **maç bitene kadar
      tabloda kalır**; satırdaki TAKIM / AT düğmeleri kapalı.
- [ ] Gözlük ekranı "OYUNDAN ÇIKARILDINIZ — yeniden bağlanılıyor" der ama **denemeye devam eder**;
      ağ dönünce elle hiçbir şey yapmadan katılır.
- [ ] Ayrılmış oyuncu maç bitmeden geri döner → **eski satırına oturur**, istatistikleri yerinde.
- [ ] Maç yokken kopan oyuncu: süre dolunca roster'dan **tümden düşer** (playerId serbest kalır).

**Maç sonu**
- [ ] Maç biter (`finished`) → ayrılmış oyuncu **maç sonu tablosunda görünür**.
- [ ] Lobiye dönülür → `ayrıldı` satırları kaybolur, kalanların `inMatch`'i temizlenir.
- [ ] Kazanan **bağlı** oyunculardan seçilir (ayrılmış oyuncunun skoru tabloda durur ama kupayı
      almaz).

**Admin ve atma**
- [ ] Admin penceresi kapatılır → satır **hemen** kaybolur ("yeniden bağlanıyor" yazmaz).
- [ ] Admin bağlantısı koparsa admin ekranı bugünkü "SUNUCUYA BAĞLANILAMIYOR" metnini korur
      (geri sayım gösterilmez).
- [ ] `AT` ile atılan oyuncu: satır anında kalkar ve **maç sonu tablosunda da yer almaz**.

**Genel**
- [ ] Hiçbir ekranda "çevrimdışı" yazmıyor.
- [ ] Maç kapıları eskisi gibi: kopan oyuncu yükleme kapısını bekletmiyor, vurulamıyor,
      canlanmıyor, snapshot'ta çizilmiyor.
