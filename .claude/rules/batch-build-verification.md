# Kural: Build/doğrulamayı batch'le — her işlemden sonra alma

Unity derleme/build/play doğrulaması tüm task'lar bitene kadar çalıştırılmaz.
- Tüm implementasyonu önce yaz; sonda TEK birleşik doğrulama geçişi yap.
- Ara doğrulamayı yalnız gerçek bir blocker için kullan, rutin teyit için değil.
