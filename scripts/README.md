# scripts/ — dağıtım betikleri

Üç bileşenin her biri kendi betiğiyle `deploy/` altına üretilir. Betikler **idempotent**:
hedef klasörü silip yeniden yazarlar.

| Betik | Kaynak | Çıktı | Ön koşul |
|---|---|---|---|
| `deploy-admin-game.bat` | Unity projesi (`Assets/`) | `deploy\admin\VortexArena.exe` | Unity Editor kapalı (betik zorlamaz) |
| `deploy-server.bat` | `Server/VortexArena.Server.App` | `deploy\server\VortexArena.Server.App.exe` | .NET 10 SDK |
| `deploy-launcher.bat` | `launcher/` (Flutter) | `deploy\launcher\vortex_launcher.exe` | Flutter + VS C++ + **Developer Mode** |

## Çalıştırma ve pencerenin kapanması

Üçü de **çift tıklanabilir**: iş bitince (başarı ya da hata) pencere `pause` ile bekler, çıktı
okunabilir. Hata durumunda en sonda `=== BASARISIZ (cikis kodu N) ===` satırı basılır — uzun
build log'unda hatayı aramaya gerek yok.

Bekleme yalnız betik çift tıklanarak / `cmd /c betik.bat` diye başlatıldığında devreye girer
(`%cmdcmdline%` betiğin adını içeriyor mu diye bakılır); zaten açık bir konsoldan çalıştırıldığında
beklemez. Otomasyonda kapatmak için:

```bat
deploy-launcher.bat --no-pause
set VORTEX_NO_PAUSE=1 && deploy-launcher.bat
```

## Neden bu ön koşullar?

- **Editör kapalı olmalı:** `deploy-admin-game.bat` batch-mode Unity başlatır
  (`-batchmode -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildWindowsAdmin`).
  Aynı proje editörde açıkken **proje kilidine** takılır. Ama betik bunu **kontrol etmez**
  (bilinçli): editör kapatıldıktan sonra bile AI motoru gibi alt süreçlerin `Unity.exe`'si arka
  planda yaşayabiliyor ve `tasklist` kontrolü yanlış alarm veriyordu. Build ilerlemiyorsa Ctrl+C
  ile iptal edip süreçleri kapatın, tekrar deneyin. Log: `deploy\admin-build.log` — script bunu
  her koşuda siler; **silinemezse uyarır**, çünkü o dosyayı hâlâ bir Unity süreci tutuyor demektir
  (kilit için tasklist'ten çok daha güvenilir bir işaret) ve basılan log satırları bayat olabilir.
- **Developer Mode:** Flutter'ın Windows plugin sistemi symlink kullanır.
  Kapalıysa build → *"Building with plugins requires symlink support"*.
  Aç: `start ms-settings:developers`. Betik bunu **build'e girmeden önce** kayıt defterinden
  kontrol eder (`HKLM\...\AppModelUnlock\AllowDevelopmentWithoutDevLicense` = `0x1`), böylece
  dakikalarca süren `pub get` + build boşa gitmez.

## Betik yazarken iki tuzak (kanıtlanmış)

- **`call flutter …` çağıran betiği de öldürür.** `flutter.bat` sonunda
  `… & bin\internal\exit_with_errorlevel.bat` zinciri var; bu zincir `call` ile girilen batch
  bağlamını komple sonlandırıyor → betik hiçbir şey yazmadan ölür, çift tıklanmışsa pencere
  anında kapanır. Flutter **her zaman ayrı bir çocuk süreçte** çağrılır:
  `cmd /c call "%VA_FLUTTER%" build windows --release`.
  Ayrıca `flutter.bat` PATH üzerinden tırnaklı çağrılırsa (`"flutter"`) `FLUTTER_ROOT`'u yanlış
  çözüp *"The Flutter directory is not a clone of the GitHub project"* verir — betik önce
  `where` ile **tam yola** çözer, sonra tam yolla çağırır.
- **Betik-içi değişkenler `VA_` önekli olmalı.** Bu değişkenler çocuk süreçlere miras kalıyor;
  kısa genel adlar derleme zincirini kırıyor. Yaşanmış örnek: `set "RC=0"` → CMake `RC`'yi
  resource compiler sanıp *"Could not find the compiler specified in the environment variable
  RC: 0"* ile üretimi kırdı (aynı risk MSBuild için de var — ortam değişkenlerini global property
  olarak okur, Unity → IL2CPP → MSVC zinciri dahil). Yeni değişken eklerken öneki koru.

## Ortam değişkenleriyle yol geçersiz kılma

| Değişken | Ne için | Varsayılan |
|---|---|---|
| `UNITY_EXE` | Unity editör exe'si | `C:\Program Files\Unity\Hub\Editor\<ProjectVersion>\Editor\Unity.exe` |
| `FLUTTER_EXE` | Flutter komutu | PATH'teki `flutter` |

Unity sürümü `ProjectSettings/ProjectVersion.txt`'ten okunur — sürüm yükseltilince betik
kendiliğinden doğru editörü bulur.

## Sıra önemli mi?

Bağımsızlar, ayrı ayrı çalıştırılabilir. Ama **silah/harita SO'su değiştiyse** önce Unity'de
`Tools > VortexArena > Export Server Config`, sonra `deploy-server.bat` (config'i o kopyalar).

Çıktıların işletmeye taşınma sırası: `deploy/README.md`.
