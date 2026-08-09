# deploy/ — dağıtılabilir çıktılar

Bu klasörün alt klasörleri **`scripts/deploy-*.bat` tarafından üretilir ve git'e girmez**
(`.gitignore`). Elle dosya koymayın; bir sonraki deploy siler.

| Klasör | Üreten | İçerik | Nasıl çalıştırılır |
|---|---|---|---|
| `admin/` | `scripts/deploy-admin-game.bat` | Unity Windows yönetim build'i (`VortexArena.exe` + `VortexArena_Data/`) | **Launcher başlatır** — elle çalıştırılırsa sunucu adresi olmaz |
| `player/` | `scripts/deploy-player-apk.bat` | Unity Android oyuncu build'i (`game.apk` + `install_game.bat`) | Gözlüğe kurulur — `install_game.bat` (adb) |
| `server/` | `scripts/deploy-server.bat` | Self-contained .NET 10 sunucu (`VortexArena.Server.App.exe` + `config/`) | **Launcher başlatır** (mekanı `--venue` ile geçer) ya da elle çift tıkla |
| `launcher/` | `scripts/deploy-launcher.bat` | Self-contained .NET 10 WPF operatör launcher'ı (`VortexArena.Launcher.exe` + runtime) | Operatör çift tıklar |
| `updater/` | `scripts/deploy_android_updater.bat` | Quest OTA updater (`VortexUpdater.apk` + `install_updater.bat`) | Gözlüğe **bir kez** kurulur — `install_updater.bat` (adb); sonrası USB'siz: oyun APK'sı IIS'ten indirilip kurulur (`updater/README.md`) |

## İşletmeye kurulum sırası

1. `scripts\deploy-server.bat` → `deploy\server\` klasörünü sunucu PC'sine kopyala.
2. Sunucu PC'sinde bir kez: `deploy\server\firewall-kur.cmd` → sağ tık → **yönetici olarak çalıştır**.
3. `scripts\deploy-admin-game.bat` ve `scripts\deploy-launcher.bat` → `deploy\admin\` +
   `deploy\launcher\` klasörlerini yönetim PC'sine kopyala (**klasörlerin tamamı** — exe'ler
   tek başına çalışmaz).
4. `scripts\deploy-player-apk.bat` → gözlükleri USB ile bağla (geliştirici modu açık) ve
   `deploy\player\install_game.bat` ile **her gözlüğe aynı APK'yı** kur. Rol ve sunucu adresi
   gömülü değildir; oyuncu build'i sunucuyu UDP beacon ile kendi bulur.
   Sonraki oyun güncellemeleri USB'siz yapılabilir: gözlüğe bir kez `install_updater.bat` ile
   **Vortex Updater** kurulur; yeni `game.apk` IIS klasörüne kopyalanır ve gözlükteki updater
   indirip kurar (`updater/README.md`).
5. Launcher'ı aç ve bir kez doldur: **1 · Sunucu** → `deploy\server\VortexArena.Server.App.exe`
   + listeden **mekan**; **2 · Bağlantı** → sunucunun IP'si; **3 · Yönetim oyunu** →
   `deploy\admin\VortexArena.exe`.
6. **Sunucuyu Başlat** → **Yönetimi Başlat**.

Sunucu `--venue <mekan>` ile açılır: o oturumda yalnız o işletmenin haritaları oynatılabilir ve
açılış sahnesi o mekanın lobisidir. Oyun, IP'yi `--server-ip` argümanıyla alır ve doğrudan bağlı
dashboard'a düşer; oyun içinde IP sorulmaz.

> **Sunucunun ömrü launcher'a bağlı değildir** — launcher yalnız başlatır, kapatmaz. Kapatmak
> için sunucunun kendi penceresinde **Ctrl+C**. Aynı sunucu istenirse eskisi gibi elle de
> çalıştırılabilir; o durumda mekan konsolda sorulur.
