# Zombi modu (`zombie`) — kalan iş

Kooperatif dalga savunması. Protokol sözleşmesi `Docs/ArenaNet-Protokol.md` §10.5 (`zombie` bloğu +
denge tablosu) ve §10.10 ("Sunucu-atanmış sahiplik"); bileşenler `Docs/Sistem-Ozeti.md` §4;
reçete `Docs/Gelistirici/Yemek-Kitabi.md` §11.7.

## Arayüz

- [ ] HUD, Hamburgerci HUD'ının (`Lokanta` paleti) script takasıyla kopyasıdır; zombi moduna ait
      bir görünüm tasarlanacak.

## Karar bekleyenler

- [ ] **Paket tavanı:** eşzamanlı tavan 12 iken `OBJECT_MAX_ENTRIES_PER_PACKET` 16 yetiyor. 48'e
      çıkarma ve **adil sıra** (tavanı aşan poz bölünmez, DÜŞER — hep aynı zombi donar) ancak
      eşzamanlı sayı büyütülecekse gerekir; `PROTOCOL_VERSION` artar.
- [ ] **Aradaki oyuncu:** başka hedefe yürüyen zombi yolundaki oyuncunun bedeninden geçer. Çözüm
      adayı: uzak oyuncuda `NavMeshObstacle` (yalnız sahip makinede anlamlı) — maliyeti ve kalibresiz
      oyuncuda davranışı ölçülmeli.
- [ ] **Doğuş animasyonu:** bugün yok, zombi ilk poz gelene kadar görünmezdir. İşaret gerekirse
      payload'a doğuş anı (`t:<kalan sn>`) eklenir ya da animasyon tamamen yerel tutulur.
- [ ] **Build Readiness satırı:** `supportedModeIds`'i `zombie` içeren ya da boş olan arenada
      NavMesh verisi var mı? Kontrol listesine satır eklenecek mi?
- [ ] **Görüş hattı:** bugün en yakın oyuncu kovalanır, duvar/kolon arkasını da görür. Kolonlu
      arenada sahte takip gibi okunabilir.

## Playtest ile oynanacaklar

- [ ] Hasar/hız/eşzamanlı sayı eğrileri — §10.5'teki tablo başlangıçtır.
- [ ] Ses: doğuş, yürüyüş, saldırı, ölüm kancaları `ZombieView`'da; klipler içerik işi.
- [ ] Gerçek model + animasyon seti (idle · walk · attack · death · rise). Bugünkü sunum
      **prosedüreldir** (Animator yok, hareket akan pozdan türer).
