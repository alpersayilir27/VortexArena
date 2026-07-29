@echo off
setlocal
cd /d "%~dp0"

rem =====================================================================
rem  install_game.bat
rem  Oyuncu APK'sini bagli gozluge kurar (adb install -r -g).
rem
rem  APK'yi UC YERDE arar, ilk buldugunu kurar:
rem    1) bu betigin yani  ->  deploy-player-apk.bat betigi apk'nin yanina
rem                            bu dosyanin bir kopyasini koyar
rem    2) deploy\player\   ->  repo kokunden calistirildiginda normal yol
rem    3) Builds\player\   ->  PlayerBuildTool'un -buildOutput verilmeden
rem                            calistirildigindaki varsayilani
rem
rem  Boylece hangi kopyayi cift tikladigin fark etmez; APK'yi elle tasimak
rem  ya da yeniden adlandirmak gerekmez.
rem =====================================================================

set "VA_APK="
for %%P in (
  "%~dp0game.apk"
  "%~dp0deploy\player\game.apk"
  "%~dp0Builds\player\game.apk"
) do if not defined VA_APK if exist "%%~fP" set "VA_APK=%%~fP"

if not defined VA_APK (
    echo [HATA] game.apk bulunamadi. Bakilan yerler:
    echo    %~dp0game.apk
    echo    %~dp0deploy\player\game.apk
    echo    %~dp0Builds\player\game.apk
    echo.
    echo APK'yi uretmek icin: scripts\deploy-player-apk.bat
    echo ^(Unity editoru kapali olmali.^)
    pause
    exit /b 1
)

echo Kurulacak APK:
echo    %VA_APK%
for %%F in ("%VA_APK%") do echo    %%~zF bayt  -  %%~tF
echo.

where adb >nul 2>nul
if errorlevel 1 (
    echo [HATA] adb bulunamadi. Android platform-tools kurulu ve PATH'e ekli olmali.
    echo Meta Quest Developer Hub kuruluysa adb onunla birlikte gelir.
    pause
    exit /b 1
)

echo Bagli cihazlar:
adb devices
echo.

echo Gozluge kuruluyor, lutfen bekleyin...
adb install -r -g "%VA_APK%"
if errorlevel 1 (
    echo.
    echo [HATA] Kurulum basarisiz. Kontrol edin:
    echo   - Gozluk USB ile bagli mi? Kablosuz icin: adb connect ^<gozluk-ip^>:5555
    echo   - Gelistirici modu acik mi, USB hata ayiklama izni verildi mi?
    echo   - Birden fazla cihaz bagliysa digerlerini cikarin.
    echo   - Eski bir kurulum baska bir paket adiyla duruyorsa once onu kaldirin:
    echo       adb uninstall com.UnityTechnologies.com.unity.template.urpblank
    pause
    exit /b 1
)

echo.
echo Kurulum tamamlandi.
pause
