# deploy/ — dağıtılabilir çıktılar

Bu klasörün alt klasörleri **`scripts/deploy-*.bat` tarafından üretilir ve git'e girmez**
(`.gitignore`). Elle dosya koymayın; bir sonraki deploy siler.

| Klasör | Üreten | İçerik | Nasıl çalıştırılır |
|---|---|---|---|
| `admin/` | `scripts/deploy-admin-game.bat` | Unity Windows yönetim build'i (`VortexArena.exe` + `VortexArena_Data/`) | **Launcher başlatır** — elle çalıştırılırsa sunucu adresi olmaz |
| `player/` | `scripts/deploy-player-apk.bat` | Unity Android oyuncu build'i (`game.apk` + `install_game.bat`) | Gözlüğe kurulur — `install_game.bat` (adb) |
| `server/` | `scripts/deploy-server.bat` | Self-contained .NET 10 sunucu (`VortexArena.Server.App.exe` + `config/`) | **Elle** çift tıkla / terminalden |
| `launcher/` | `scripts/deploy-launcher.bat` | Flutter operatör launcher'ı (`vortex_launcher.exe` + `data/`) | Operatör çift tıklar |

## İşletmeye kurulum sırası

1. `scripts\deploy-server.bat` → `deploy\server\` klasörünü sunucu PC'sine kopyala.
2. Sunucu PC'sinde bir kez: `deploy\server\firewall-kur.cmd` → sağ tık → **yönetici olarak çalıştır**.
3. `scripts\deploy-admin-game.bat` ve `scripts\deploy-launcher.bat` → `deploy\admin\` +
   `deploy\launcher\` klasörlerini yönetim PC'sine kopyala (**klasörlerin tamamı** — exe'ler
   tek başına çalışmaz).
4. `scripts\deploy-player-apk.bat` → gözlükleri USB ile bağla (geliştirici modu açık) ve
   `deploy\player\install_game.bat` ile **her gözlüğe aynı APK'yı** kur. Rol ve sunucu adresi
   gömülü değildir; oyuncu build'i sunucuyu UDP beacon ile kendi bulur.
5. Sunucuyu elle başlat: `deploy\server\VortexArena.Server.App.exe`.
6. Launcher'ı aç → **Ayarlar**'dan `deploy\admin\VortexArena.exe`'yi seç → **Sunucu IP**'yi
   yaz → **Yönetimi Başlat**.

Oyun, IP'yi `--server-ip` argümanıyla alır ve doğrudan bağlı dashboard'a düşer;
oyun içinde IP sorulmaz.

> Sunucu launcher'dan **başlatılmaz** — kasıtlı: sunucu maçın tek otoritesidir, ömrü
> operatör uygulamasının ömrüne bağlanmamalıdır.
