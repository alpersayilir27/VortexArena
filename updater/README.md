# VortexUpdater — gözlük üstü OTA güncelleyici

Quest gözlüğüne bir kez kurulan `com.vortex.updater` uygulaması, oyunun APK'sını sabit bir
URL'den indirip kurar. **USB kablosu yalnız updater'ın ilk kurulumu için gerekir**; oyunun
(`com.vortex.arena`) her yeni sürümü kablosuz iner.

## Sabitler

URL ve hedef paket adı `app/src/main/kotlin/com/vortex/updater/MainActivity.kt` başındaki
`companion object` içindedir:

```
GAME_PACKAGE = "com.vortex.arena"
APK_URL      = "http://159.100.20.26:8090/game.apk"
```

Adres değişirse burası düzeltilir ve updater yeniden kurulur — gözlükte ayar ekranı yoktur.

## Sunucu tarafı (`updater_uploader/`)

- Sunucuda APK klasörüne (ör. `D:\WebHost\player_apk_updater`)
  `updater_uploader\updater_uploader_main.py` konur ve `python updater_uploader_main.py` ile
  çalıştırılır. Betik 8090'ı dinler: `POST /upload` gelen APK'yı kendi klasörüne `game.apk`
  olarak yazar (atomik değiştirme — yarım yükleme mevcut dosyayı bozamaz), `GET /game.apk`
  (ve `GET /upload`) dosyayı indirtir, başka hiçbir yolu yakalamaz.
- Yeni sürüm yayınlamak **otomatiktir**: `scripts\deploy-player-apk.bat` build sonunda APK'yı
  `/upload`'a kendisi POST'lar. Elle yayınlamak da mümkün:
  `curl -f -X POST --data-binary "@deploy\player\game.apk" http://159.100.20.26:8090/upload`
  ya da dosyayı klasöre aynı adla kopyalamak.
- ⚠️ **Aynı portu iki süreç dinleyemez:** bu betik ayaktayken IIS'te 8090'a bağlı site
  **durdurulmuş** olmalı. Betik indirmeyi de yaptığı için IIS'e gerek yoktur (IIS'in `.apk`
  MIME eşlemesi derdi de yoktur). Windows Firewall'da 8090'a gelen bağlantı `python.exe` için
  açık olmalı (IIS'in kuralı http.sys'e aittir, Python'u kapsamaz).
- Yükleme ucu **anahtarsızdır** — porta erişebilen herkes APK yayınlayabilir (bilinçli tercih).
- Trafik düz HTTP'dir (`usesCleartextTraffic="true"`) — TLS yoktur.

## Güncelleme mi, sil-yükle mi

| Durum | Düğme | Sonuç |
|---|---|---|
| Aynı imza anahtarı + artan `versionCode` | **Güncelle** | Yerinde güncelleme, oyunun yerel verisi korunur |
| İmza değişti ya da `versionCode` düştü | **Sil ve Yükle** | Android yerinde güncellemeyi reddettiği için eski paket kaldırılır; oyunun yerel verisi silinir |

"Oyunu Başlat" düğmesi kurulu oyunu açar; oyun kurulu değilse pasiftir. Ekranın üstünde kurulu
sürüm ve `versionCode` yazar.

## Gözlükte ilk çalıştırma

- "Bilinmeyen kaynaklardan kurulum" izni **cihaz başına bir kez** onaylanır; updater izin yoksa
  ilgili ayar ekranını kendisi açar, izin verildikten sonra düğmeye tekrar basılır.
- Her kurulumda sistemin **Kur/Güncelle onay penceresi** çıkar. Bu bir Android kısıtıdır:
  sessiz kurulum device-owner ayrıcalığı ister, bu projede öyle bir kurulum yoktur.

## Build

```
scripts\deploy_android_updater.bat
```

Android Studio gerekmez: JDK ve Gradle Unity'nin Android modülünden alınır; Android SDK'sı ise
`%LOCALAPPDATA%\VortexUpdaterSdk` altındaki yazılabilir köke ilk koşuda AGP tarafından indirilir
(Unity'nin kendi SDK'sı Program Files altında yazma korumalı olduğu için kullanılmaz). İlk koşu
bu yüzden internet ister ve uzun sürer; sonrakiler hızlıdır. Çıktı:
`deploy\updater\VortexUpdater.apk`.

Gözlüğe kurulum: repo kökündeki `install_updater.bat` (ya da çıktı klasörüne kopyalanan aynı
betik).

## İmza notu

APK **debug** anahtarıyla imzalanır ve o anahtar makine başına farklıdır. Updater'ı başka bir
PC'den üretilmiş APK ile kurmak gerekirse önce gözlükten kaldırılır:

```
adb uninstall com.vortex.updater
```

Bu yalnız updater'ın kendisiyle ilgilidir; oyunun APK'sının imzasını etkilemez.
