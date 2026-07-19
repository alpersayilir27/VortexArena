@echo off
setlocal
cd /d "%~dp0"

set APK=game.apk

if not exist "%APK%" (
    echo [HATA] %APK% bulunamadi. APK'yi bu scriptle ayni klasore "%APK%" adiyla koyun.
    pause
    exit /b 1
)

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

echo %APK% gozluge kuruluyor, lutfen bekleyin...
adb install -r -g "%APK%"
if errorlevel 1 (
    echo.
    echo [HATA] Kurulum basarisiz. Kontrol edin:
    echo   - Gozluk USB ile bagli mi? Kablosuz icin: adb connect ^<gozluk-ip^>:5555
    echo   - Gelistirici modu acik mi, USB hata ayiklama izni verildi mi?
    echo   - Birden fazla cihaz bagliysa digerlerini cikarin.
    pause
    exit /b 1
)

echo.
echo Kurulum tamamlandi.
pause
