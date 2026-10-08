# deploy/ — dağıtılabilir çıktılar

Bu klasörün içeriği **`scripts/deploy-*.bat` tarafından üretilir ve git'e girmez**
(`.gitignore`). Elle dosya koymayın; bir sonraki deploy siler (`player/` silinmez, orada sürümlü
APK'lar birikir).

`deploy\` kökü aynı zamanda **sahaya olduğu gibi taşınan yerleşimdir**: launcher exe'si kökte
durur ve yanındaki `server\` · `admin\` klasörlerini kendisi bulur (`launcher/README.md`).
`game_versions\` (indirilen APK'lar) ve `replays\` (maç kayıtları) klasörlerini launcher
gerektiğinde oluşturur.

```
deploy\VortexArena.Launcher.exe
deploy\server\VortexArena.Server.App.exe   (+ config\, logs\)
deploy\admin\VortexArena.exe
deploy\game_versions\
deploy\replays\
```

| Çıktı | Üreten | İçerik | Nasıl çalıştırılır |
|---|---|---|---|
| `admin/` | `scripts/deploy-admin-game.bat` | Unity Windows yönetim build'i (`VortexArena.exe` + `VortexArena_Data/`) | **Launcher başlatır** — elle çalıştırılırsa sunucu adresi olmaz |
| `player/` | `scripts/deploy-player-apk.bat` | Unity Android oyuncu build'leri (`game_v<sürüm>.apk` + `install_game.bat`) — sürümler yan yana durur, klasör build'de silinmez | Gözlüğe kurulur — `install_game.bat` (adb) bulduğu sürümleri listeler, hangisinin kurulacağını sorar |
| `server/` | `scripts/deploy-server.bat` | Self-contained .NET 10 sunucu (`VortexArena.Server.App.exe` + `config/`) | **Launcher başlatır** (mekanı `--venue` ile geçer) ya da elle çift tıkla |
| `VortexArena.Launcher.exe` | `scripts/deploy-launcher.bat` | Operatör launcher'ı — **tek dosya** (self-contained .NET 10 WPF, `adb` gömülü) | Operatör çift tıklar; sunucuyu, yönetimi, APK kurulumunu ve maç kaydını buradan yönetir |
| `updater/` | `scripts/deploy_android_updater.bat` | Quest OTA updater (`VortexUpdater.apk` + `install_updater.bat`) | Gözlüğe **bir kez** kurulur — `install_updater.bat` (adb); sonrası USB'siz: oyun APK'sı IIS'ten indirilip kurulur (`updater/README.md`) |

## İşletmeye kurulum sırası

1. `scripts\deploy-server.bat`, `scripts\deploy-admin-game.bat`, `scripts\deploy-launcher.bat` →
   `deploy\` kökünü (exe + `server\` + `admin\`) operatör PC'sine kopyala. **Klasörlerin tamamı**
   taşınır, exe'ler tek başına çalışmaz; launcher exe'si klasörlerin yanında durmalıdır.
   Launcher sunucuyu kendi makinesinden kontrol eder, ikisi **aynı PC'dedir**.
2. O PC'de bir kez: `server\firewall-kur.cmd` → sağ tık → **yönetici olarak çalıştır**.
3. `scripts\deploy-player-apk.bat` (sürüm numarasını sorar) → gözlükleri USB ile bağla (geliştirici
   modu açık) ve `deploy\player\install_game.bat` ile **her gözlüğe aynı sürümü** kur; betik bulduğu
   sürümleri listeler, hangisinin kurulacağını sorar. Rol ve sunucu adresi gömülü değildir; oyuncu
   build'i sunucuyu UDP beacon ile kendi bulur.
   Sonraki oyun güncellemeleri USB'siz yapılabilir: gözlüğe bir kez `install_updater.bat` ile
   **Vortex Updater** kurulur; `deploy-player-apk.bat` build sonunda APK'yı sunucudaki yayın
   ucuna (`updater_uploader/`) kendisi yükler, gözlükteki updater sürümleri listeleyip indirir
   (`updater/README.md`).
4. `VortexArena.Launcher.exe`'ye çift tıkla: **Sunucu** sayfasında listeden **mekan** seç ve
   **Başlat**, sonra **Yönetim** sayfasında **Başlat**. Exe yolları sorulmaz — launcher kendi
   klasörünü kök sayar.

Sunucu `--venue <mekan>` ile açılır: o oturumda yalnız o işletmenin haritaları oynatılabilir ve
açılış sahnesi o mekanın lobisidir. Oyun, IP'yi `--server-ip` argümanıyla alır ve doğrudan bağlı
dashboard'a düşer; oyun içinde IP sorulmaz.

> **Launcher sunucuyu başlatır ve durdurur** — durdurma kontrol ucundan temiz kapanıştır
> (`launcher/README.md`). Sunucu konsol penceresi olmadan koştuğu için operatörün penceresi
> launcher'daki günlük akışıdır (`server\logs\`). Sunucu istenirse elle de çalıştırılabilir;
> o durumda mekan konsolda sorulur ve launcher onu yine görüp durdurabilir.
