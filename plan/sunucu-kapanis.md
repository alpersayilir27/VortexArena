# Sunucu kapanışı — kalan iş: doğrulama

Kod ve doküman yazıldı (`ServiceShutdown` + dört serviste `StopAsync` + `Program.ShutdownAsync` üç
tetikleyiciyle; `Server/README.md` "Kapanış", `Sistem-Ozeti` §4 sözleşmesi ve §7 tuzağı).
Bu dosya doğrulama bitince silinir.

## Doğrulama (kullanıcı koşar)

- [ ] Launcher sunucuyu **kapatmaz**: açıkken tekrar *Sunucuyu Başlat* → "zaten çalışıyor … Ctrl+C"
      uyarısı; kapatma yalnız sunucunun kendi konsolundan.
