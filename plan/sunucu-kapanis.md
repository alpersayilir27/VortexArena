# Sunucu kapanışı — kalan iş: doğrulama

Kod ve doküman yazıldı (`ServiceShutdown` + dört serviste `StopAsync` + `Program.ShutdownAsync` üç
tetikleyiciyle; `Server/README.md` "Kapanış", `Sistem-Ozeti` §4 sözleşmesi ve §7 tuzağı).
Bu dosya doğrulama bitince silinir.

## Doğrulama (kullanıcı koşar)

- [ ] Ctrl+C: konsolda son satır "Kapandı.", sonrasında log yok; süreç 3 sn içinde çıkar.
- [ ] **Konsol penceresini çarpıyla kapatma** aynı sırayı koşturur (pencere kapanmadan önce
      "Kapatılıyor…" → "Kapandı." görünür).
- [ ] Bağlı başlıklar `close` alır → yeniden bağlanma ekranı (`ConnectionOverlay`).
- [ ] Launcher'dan durdurma aynı sonucu verir.
- [ ] Maç KOŞARKEN kapatma: tik durduktan sonra hiçbir yayın çıkmaz, zaman aşımı satırı basılmaz.
