# VortexUpdater — gözlük üstü OTA güncelleyici

Quest gözlüğüne bir kez kurulan `com.vortex.updater` uygulaması, sunucuda yayınlanmış **tüm oyun
sürümlerini listeler** ve seçileni indirip kurar. **USB kablosu yalnız updater'ın ilk kurulumu
için gerekir**; oyunun her sürümü kablosuz iner. Her sürümün paket adı ayrıdır
(`com.vortex.arenav<sürüm>`, ör. sürüm 132 → `com.vortex.arenav132`), bu yüzden birden çok sürüm
gözlükte yan yana kurulu durabilir.

## Sabitler

Adresler ve paket adı öneki `app/src/main/kotlin/com/vortex/updater/MainActivity.kt` başındaki
`companion object` içindedir:

```
GAME_PACKAGE_PREFIX = "com.vortex.arenav"        // + surum numarasi
VERSIONS_URL        = "http://159.100.20.26:8091/versions"
DOWNLOAD_BASE       = "http://159.100.20.26:8090/game_versions/"
```

Adres değişirse burası düzeltilir ve updater yeniden kurulur — gözlükte ayar ekranı yoktur.

## Sunucu tarafı

İndirme ile yükleme ayrı süreçlerdedir ve ikisi de aynı klasörü kullanır
(ör. `D:\WebHost\player_apk_updater`). Aynı süreç **üç çıktıyı** yayınlar: gözlüğün oyun APK'sı,
operatör PC'sinin admin build'i ve sunucu paketi.

- **İndirme = IIS, 8090.** O klasörü kök olarak yayınlayan site; çıktılar alt klasörlerde durur:
  - `http://159.100.20.26:8090/game_versions/game_v<sürüm>.apk` — gözlükteki updater buradan iner.
    ⚠️ **IIS `.apk` uzantısını varsayılan MIME listesinde tanımaz ve 404 döner.** MIME eşlemesi
    eklenir: `.apk` → `application/vnd.android.package-archive`.
  - `http://159.100.20.26:8090/admin_versions/admin_v<sürüm>.zip` — launcher'ın **Yönetim**
    sayfası buradan iner. `.zip` IIS'in varsayılan MIME listesinde vardır, ek eşleme gerekmez.
  - `http://159.100.20.26:8090/server_versions/server_v<sürüm>.zip` — launcher'ın **Sunucu**
    sayfası buradan iner.
- **Yükleme ve listeleme = `updater_uploader\updater_uploader_main.py`, 8091.** Betik aynı
  klasöre konur ve `python updater_uploader_main.py` ile çalıştırılır. Uçları:
  - `POST /upload?v=<sürüm>` — gelen APK'yı `game_versions/game_v<sürüm>.apk` olarak yazar
    (atomik değiştirme — yarım yükleme mevcut dosyayı bozamaz).
  - `GET /versions` — listeyi veren uç; `{"count": …, "versions": [{"version":
    132, "file": "game_v132.apk", "size": …, "modified": …}, …]}` döner, sürüme göre büyükten
    küçüğe sıralıdır. İki tüketicisi var: gözlükteki updater ekranı ve `deploy-player-apk.bat`'in
    sürüm sormadan önce bastığı liste (`scripts/README.md`).
  - `POST /admin/upload?v=<sürüm>` — gelen zip'i `admin_versions/admin_v<sürüm>.zip` olarak yazar
    (klasör yoksa açılır, aynı atomik değiştirme). APK ile aynı doğrulamalardan geçer
    (`Content-Length`, `PK` imzası, pozitif tam sayı sürüm) **artı**: arşivin kökünde
    `VortexArena.exe` yoksa `400` ile reddedilir — launcher arşivi doğrudan açtığı için
    sarmalayıcı klasörlü bir zip sessizce bozuk kurulum üretirdi.
  - `GET /admin/versions` — `/versions` ile aynı JSON biçimi, dosya adları `admin_v<sürüm>.zip`.
  - `POST /server/upload?v=<sürüm>` — gelen zip'i `server_versions/server_v<sürüm>.zip` olarak
    yazar (klasör yoksa açılır, aynı atomik değiştirme). Aynı doğrulamalardan geçer **artı**:
    arşivin kökünde `VortexArena.Server.App.exe` **ve** `config/maps.json` yoksa `400` ile
    reddedilir — launcher arşivi doğrudan açtığı için sarmalayıcı klasörlü zip bozuk kurulum
    üretirdi, `maps.json` ise sürümle birlikte dağılır ve onsuz sunucu harita doğrulaması kapalı
    açılırdı.
  - `GET /server/versions` — `/versions` ile aynı JSON biçimi, dosya adları `server_v<sürüm>.zip`.
  - `GET /upload`, `GET /admin/upload` ve `GET /server/upload` — sağlık kontrolü.

  Portlar ayrı olduğu için IIS ile çakışmaz. Windows Firewall'da 8091'e gelen bağlantı
  `python.exe` için açık olmalı (IIS'in kuralı http.sys'e aittir, Python'u kapsamaz).
- Yeni sürüm yayınlamak **otomatiktir**: `scripts\deploy-player-apk.bat`,
  `scripts\deploy-admin-game.bat` ve `scripts\deploy-server.bat` build sonunda çıktıyı kendileri
  POST'lar. Elle yayınlamak da mümkün:
  `curl -f -X POST --data-binary "@deploy\player\game_v132.apk" "http://159.100.20.26:8091/upload?v=132"`
  `curl -f -X POST -H "Expect:" -T "deploy\admin\admin_v16.zip" "http://159.100.20.26:8091/admin/upload?v=16"`
  `curl -f -X POST -H "Expect:" -T "deploy\server\server_v16.zip" "http://159.100.20.26:8091/server/upload?v=16"`
  ya da dosyayı ilgili alt klasöre aynı adla kopyalamak.
- Yükleme ucu **anahtarsızdır** — porta erişebilen herkes sürüm yayınlayabilir (bilinçli tercih).
- Trafik düz HTTP'dir (`usesCleartextTraffic="true"`) — TLS yoktur.

## Ekran

Açılışta sunucudan sürüm listesi çekilir ve her sürüm için bir satır çizilir:
`Surum 132  ·  569 MB` (kuruluysa sonuna `·  kurulu` eklenir). Satırın sağındaki tek düğme
duruma göre değişir:

- **`Indir`** — o sürüm gözlükte kurulu değil: APK indirilir ve kurulum onayı açılır.
- **`Ac`** — o sürüm kurulu: paket doğrudan başlatılır.

**`Yenile`** düğmesi listeyi sunucudan yeniden çeker (uygulamadan çıkıp girmeye gerek yoktur).
İndirme/kurulum sürerken bütün düğmeler pasiftir.

İndirilen APK uygulamanın cache klasöründe tutulur ve **bir sonraki indirmeden önce oradaki tüm
`game_v*.apk` dosyaları silinir** — her sürüm ~600 MB, birikirse gözlüğün deposu dolar. Cache'te
tutulmasının sebebi: imza uyuşmazlığında (farklı anahtarla imzalanmış aynı paket) o sürüm
otomatik kaldırılır ve **aynı dosyayla** yeniden kurulur, ikinci kez indirilmez.

⚠️ Bilinen sınır: sunucuda **aynı sürüm numarasıyla** yeni bir APK yayınlanırsa, o sürüm cihazda
kuruluyken satır `Indir` değil `Ac` gösterir — yeniden indirmek için önce o sürüm gözlükten
kaldırılır.

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
