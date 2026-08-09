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

rem =====================================================================
rem  Cihaz yetkilendirmesi
rem  "unauthorized" = gozluk bu PC'nin RSA anahtarini kabul etmemis.
rem  Gelistirici modu ACIK olsa bile ayri bir onay gerekir; anahtar
rem  yenilendiginde (%USERPROFILE%\.android\adbkey) eski onay olur.
rem  Cozum: adb kill-server + start-server -> anahtar yeniden gonderilir,
rem  onay penceresi gozlukte yeniden cikar. Betik once izin ister,
rem  sonra kullanicinin gozlukte onayladigini adb'ye tekrar sorarak teyit eder.
rem =====================================================================

:va_devcheck
echo Bagli cihazlar:
adb devices
echo.

call :va_find_unauthorized
if not defined VA_UNAUTH goto :va_install

echo [UYARI] Gozluk "unauthorized" durumda  ^(%VA_UNAUTH%^)
echo   Cihaz gorunuyor ama bu bilgisayara guvenmiyor: RSA anahtari onaylanmamis.
echo   Kablo/surucu sorunu degildir.
echo.
echo   Onay penceresini yeniden tetiklemek icin su iki komut calistirilir:
echo       adb kill-server
echo       adb start-server
echo.
choice /c EH /n /m "Bu komutlari simdi calistirayim mi? (E=Evet / H=Hayir): "
if errorlevel 2 goto :va_auth_abort

echo.
echo ^> adb kill-server
adb kill-server
echo ^> adb start-server
adb start-server
echo.
echo   Simdi GOZLUGU TAK ^(masada dururken ekran kapali oldugu icin pencere cizilmez^):
echo     1^) Bir uygulamanin icindeysen Meta tusuyla ana ekrana cik.
echo     2^) Kilit deseni/PIN varsa once kilidi ac.
echo     3^) "USB hata ayiklamaya izin verilsin mi?" penceresinde
echo        "Bu bilgisayardan her zaman izin ver" isaretle, sonra Izin Ver.
echo.
choice /c EH /n /m "Gozlukte izni verdin mi? (E=Evet, kontrol edeyim / H=Hayir, vazgec): "
if errorlevel 2 goto :va_auth_abort

echo.
echo Kontrol ediliyor...
call :va_find_unauthorized
if not defined VA_UNAUTH goto :va_auth_ok

echo.
echo [HATA] Cihaz hala "unauthorized" gorunuyor - onay adb'ye ulasmamis.
echo   Gozlukte pencere hic cikmadiysa:
echo     - Ayarlar ^> Sistem ^> Gelistirici ^> "USB hata ayiklama yetkilendirmelerini
echo       iptal et", ardindan kabloyu cikar-tak.
echo     - Hala cikmiyorsa PC'deki anahtari sifirla ^(gozluk yepyeni bir anahtar
echo       gorur ve pencereyi gostermek zorunda kalir^):
echo         del "%%USERPROFILE%%\.android\adbkey" "%%USERPROFILE%%\.android\adbkey.pub"
echo.
choice /c TV /n /m "(T) Tekrar dene / (V) Vazgec: "
if errorlevel 2 goto :va_auth_abort
echo.
goto :va_devcheck

:va_auth_ok
echo Cihaz yetkilendirildi:
adb devices
echo.

:va_install
set "VA_PKG=com.vortex.arena"
set "VA_INSTALL_LOG=%TEMP%\va_install_log_%RANDOM%.txt"

echo Gozluge kuruluyor, lutfen bekleyin...
call :va_do_install
if "%VA_INSTALL_OK%"=="1" goto :va_install_done

findstr /i "INSTALL_FAILED_UPDATE_INCOMPATIBLE" "%VA_INSTALL_LOG%" >nul
if not errorlevel 1 (
    echo.
    echo [UYARI] Gozlukteki kurulum farkli bir imzayla imzalanmis - once o kaldiriliyor...
    echo ^> adb uninstall %VA_PKG%
    adb uninstall %VA_PKG%
    echo.
    echo Tekrar kuruluyor...
    call :va_do_install
    if "%VA_INSTALL_OK%"=="1" goto :va_install_done
)

echo.
echo [HATA] Kurulum basarisiz. Kontrol edin:
echo   - Gozluk USB ile bagli mi? Kablosuz icin: adb connect ^<gozluk-ip^>:5555
echo   - Gelistirici modu acik mi, USB hata ayiklama izni verildi mi?
echo   - Birden fazla cihaz bagliysa digerlerini cikarin.
echo   - Eski bir kurulum baska bir paket adiyla duruyorsa once onu kaldirin:
echo       adb uninstall com.UnityTechnologies.com.unity.template.urpblank
del "%VA_INSTALL_LOG%" >nul 2>nul
pause
exit /b 1

:va_install_done
del "%VA_INSTALL_LOG%" >nul 2>nul
echo.
echo Kurulum tamamlandi.
pause
exit /b 0

rem ---------------------------------------------------------------------
rem  adb install -r -g calistirir, ciktiyi hem ekrana basar hem log dosyasina
rem  yazar (imza uyusmazligini tespit icin), basari ise VA_INSTALL_OK=1 yapar.
rem ---------------------------------------------------------------------
:va_do_install
set "VA_INSTALL_OK=0"
adb install -r -g "%VA_APK%" > "%VA_INSTALL_LOG%" 2>&1
type "%VA_INSTALL_LOG%"
findstr /i /c:"Success" "%VA_INSTALL_LOG%" >nul
if not errorlevel 1 set "VA_INSTALL_OK=1"
exit /b 0

:va_auth_abort
echo.
echo Kurulum yapilmadi - cihaz yetkilendirilmedi.
pause
exit /b 1

rem ---------------------------------------------------------------------
rem  adb devices ciktisinda "unauthorized" satiri ararsa seri numarasini
rem  VA_UNAUTH'a yazar, yoksa tanimsiz birakir.
rem ---------------------------------------------------------------------
:va_find_unauthorized
set "VA_UNAUTH="
for /f "tokens=1,2" %%A in ('adb devices 2^>nul') do (
    if /i "%%B"=="unauthorized" set "VA_UNAUTH=%%A"
)
exit /b 0
