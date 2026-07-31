# Kural: Build/doğrulamayı batch'le — her işlemden sonra alma

Unity derleme/build/play doğrulaması tüm task'lar bitene kadar çalıştırılmaz.
- Tüm implementasyonu önce yaz; sonda TEK birleşik doğrulama geçişi yap.
- Ara doğrulamayı yalnız gerçek bir blocker için kullan, rutin teyit için değil.

⚠️ **O tek geçişi ajan KOŞMAZ, kullanıcı koşar** → [[derleme-kullaniciya-aittir]]. Yani bu kural
pratikte şuna iner: ajan hiç doğrulama tetiklemez, işi bitirir ve "şunlar değişti, derleme
gerekiyor" der. Ajan yalnız kullanıcı açıkça istediğinde derler.
