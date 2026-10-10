# deploy/ — dağıtılabilir çıktılar

Bu klasörün içeriği **`scripts/deploy-*.bat` tarafından üretilir ve git'e girmez**
(`.gitignore`). Elle dosya koymayın; bir sonraki deploy siler (`player/`, `admin/` ve `server/`
silinmez, orada sürümlü çıktılar birikir).

**Sahaya olduğu gibi taşınan paket `deploy\launcher\`'dır**: launcher'ın tek exe'si, yanındaki
klasörler ve `platform-tools\` (adb). Launcher kendi klasörünü kök sayar (`launcher/README.md`).
`deploy-launcher.bat` klasörleri boş açar ve `platform-tools\`'u repo kökünden kopyalar; sunucu ve
admin sürümleri launcher'ın **Sunucu** / **Yönetim** sayfalarından `server_versions\` ve
`admin_versions\` altına indirilir — elle kopyalanmaz.
Betik yeniden koşunca exe ve `platform-tools\` yenilenir, klasörlerin içeriği korunur.

```
deploy\launcher\VortexArena.Launcher.exe
deploy\launcher\server_versions\ (indirilen sunucu sürümleri: server_v<sürüm>\)
deploy\launcher\server\          (sunucunun işletme verisi: config\, logs\, mekan ölçümü)
deploy\launcher\admin_versions\  (indirilen admin sürümleri: admin_v<sürüm>\)
deploy\launcher\game_versions\   (indirilen APK'lar)
deploy\launcher\replays\         (maç kayıtları)
deploy\launcher\platform-tools\  (adb; repo kökündeki platform-tools\'tan)
```

⚠️ **`server\` ile `server_versions\` farklı şeylerdir:** sürüm klasörleri yenilenir ve
değiştirilir, `server\` ise işletmenin kendi verisidir (ayarlar, cihaz adları, günlükler, mekan
ölçümü) ve sürüm değişince yerinde kalır — sunucu onu `--data-dir` ile alır
(`../Server/README.md`).

| Çıktı | Üreten | İçerik | Nasıl çalıştırılır |
|---|---|---|---|
| `admin/` | `scripts/deploy-admin-game.bat` | Unity Windows yönetim build'leri: `admin_v<sürüm>/` (`VortexArena.exe` + `VortexArena_Data/`) + yanında yayınlanan `admin_v<sürüm>.zip` — sürümler yan yana durur, klasör build'de silinmez | **Launcher başlatır** (sürümü sunucudan indirip açar) — elle çalıştırılırsa sunucu adresi olmaz |
| `player/` | `scripts/deploy-player-apk.bat` | Unity Android oyuncu build'leri (`game_v<sürüm>.apk` + `install_game.bat`) — sürümler yan yana durur, klasör build'de silinmez | Gözlüğe kurulur — `install_game.bat` (adb) bulduğu sürümleri listeler, hangisinin kurulacağını sorar |
| `server/` | `scripts/deploy-server.bat` | Self-contained .NET 10 sunucu sürümleri: `server_v<sürüm>/` (`VortexArena.Server.App.exe` + `config/maps.json` + `config/server.json` tohumu + `firewall-kur.cmd`) + yanında yayınlanan `server_v<sürüm>.zip` — sürümler yan yana durur, klasör build'de silinmez | **Launcher başlatır** (sürümü sunucudan indirip açar; veri klasörünü `--data-dir`, mekanı `--venue` ile geçer) ya da elle çift tıkla |
| `launcher/` | `scripts/deploy-launcher.bat` | **Saha paketi**: operatör launcher'ı **tek dosya** `VortexArena.Launcher.exe` (self-contained .NET 10 WPF) + `server_versions/` · `server/` · `admin_versions/` · `game_versions/` · `replays/` (betik boş açar, içeriğe dokunmaz) + `platform-tools/` (adb, repodan her koşuda yenilenir) | Operatör çift tıklar; sunucuyu, yönetimi, APK kurulumunu ve maç kaydını buradan yönetir |
| `updater/` | `scripts/deploy_android_updater.bat` | Quest OTA updater (`VortexUpdater.apk` + `install_updater.bat`) | Gözlüğe **bir kez** kurulur — `install_updater.bat` (adb); sonrası USB'siz: oyun APK'sı IIS'ten indirilip kurulur (`updater/README.md`) |

## İşletmeye kurulum sırası

1. `scripts\deploy-launcher.bat`, `scripts\deploy-server.bat` ve `scripts\deploy-admin-game.bat`
   (ikisi sürüm numarasını sorar ve zip'i sunucuya yayınlar) → `deploy\launcher\` klasörünü
   **tamamıyla** operatör PC'sine taşı. Exe'ler tek başına çalışmaz; launcher exe'si paketteki
   klasörlerin yanında durmalıdır. Launcher sunucuyu kendi makinesinden kontrol eder, ikisi
   **aynı PC'dedir**. Sunucu ve admin build'i paketle taşınmaz: operatör PC'sinde launcher'ın
   **Sunucu** / **Yönetim** sayfalarından indirilir.
2. O PC'de bir kez, sunucu sürümü indirildikten sonra:
   `server_versions\server_v<sürüm>\firewall-kur.cmd` → sağ tık → **yönetici olarak çalıştır**
   (launcher'daki düğme de aynı işi yapar). Betik kurulu **her** sürümün exe'si için kural ekler,
   ama yeni bir sürüm indirildiğinde tekrar çalıştırılmalıdır (`../Server/README.md`).
3. `scripts\deploy-player-apk.bat` (sürüm numarasını sorar) → gözlükleri USB ile bağla (geliştirici
   modu açık) ve `deploy\player\install_game.bat` ile **her gözlüğe aynı sürümü** kur; betik bulduğu
   sürümleri listeler, hangisinin kurulacağını sorar. Rol ve sunucu adresi gömülü değildir; oyuncu
   build'i sunucuyu UDP beacon ile kendi bulur.
   Sonraki oyun güncellemeleri USB'siz yapılabilir: gözlüğe bir kez `install_updater.bat` ile
   **Vortex Updater** kurulur; `deploy-player-apk.bat` build sonunda APK'yı sunucudaki yayın
   ucuna (`updater_uploader/`) kendisi yükler, gözlükteki updater sürümleri listeleyip indirir
   (`updater/README.md`).
4. `VortexArena.Launcher.exe`'ye çift tıkla: **Sunucu** sayfasında sunucu sürümünü indir, listeden
   **mekan** seç ve **Başlat**, sonra **Yönetim** sayfasında admin sürümünü indir ve **Başlat**.
   Exe yolları sorulmaz — launcher kendi klasörünü kök sayar.

Sunucu `--data-dir <veri klasörü> --venue <mekan>` ile açılır: o oturumda yalnız o işletmenin haritaları oynatılabilir ve
açılış sahnesi o mekanın lobisidir. Oyun, IP'yi `--server-ip` argümanıyla alır ve doğrudan bağlı
dashboard'a düşer; oyun içinde IP sorulmaz.

> **Launcher sunucuyu başlatır ve durdurur** — durdurma kontrol ucundan temiz kapanıştır
> (`launcher/README.md`). Sunucu konsol penceresi olmadan koştuğu için operatörün penceresi
> launcher'daki günlük akışıdır (`server\logs\`). Sunucu istenirse elle de çalıştırılabilir;
> o durumda mekan konsolda sorulur ve launcher onu yine görüp durdurabilir.
