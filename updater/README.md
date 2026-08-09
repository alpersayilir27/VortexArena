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

## Sunucu tarafı (IIS)

- 8090 portunda bir site (ya da sanal dizin) yayınlanır; kök klasörde `game.apk` durur.
- ⚠️ **IIS `.apk` uzantısını varsayılan MIME listesinde tanımaz ve 404 döner.** MIME eşlemesi
  eklenir: `.apk` → `application/vnd.android.package-archive`.
- Yeni sürüm yayınlamak = `deploy\player\game.apk` dosyasını IIS klasörüne **aynı adla**
  kopyalamak. Sürüm numarası dosya adında taşınmaz, updater tek sabit adrese bakar.
- Trafik düz HTTP'dir (`usesCleartextTraffic="true"`) — TLS yoktur; adres bilinçli olarak
  işletme ağının içindeki bir sunucudur.

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
