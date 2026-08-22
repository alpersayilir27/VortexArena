@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

rem =====================================================================
rem  install_game.bat
rem  Installs a player APK on the connected headset (adb install -r -g).
rem
rem  VERSIONED APKs: builds are named game_v<N>.apk and each carries its own
rem  application id (com.vortex.arenav<N>), so several versions can stay
rem  installed side by side. This script lists the versions it finds and asks
rem  which one to install; the package name is derived from the choice.
rem
rem  It looks for APKs in THREE places and uses the first folder that
rem  contains a game_v*.apk:
rem    1) next to this script  ->  deploy-player-apk.bat drops a copy of this
rem                                file next to the apk
rem    2) deploy\player\       ->  the normal path when run from the repo root
rem    3) Builds\player\       ->  PlayerBuildTool's default when it runs
rem                                without -buildOutput
rem
rem  So it does not matter which copy is double-clicked; no APK has to be
rem  moved or renamed by hand.
rem
rem  NOTE: delayed expansion is ON - a "!" inside an echo would be eaten, so
rem  the messages below avoid it.
rem =====================================================================

rem  %%~P keeps the trailing backslash (%%~fP would strip it and glue the
rem  folder name to the file name).
set "VA_DIR="
for %%P in (
  "%~dp0"
  "%~dp0deploy\player\"
  "%~dp0Builds\player\"
) do if not defined VA_DIR if exist "%%~Pgame_v*.apk" set "VA_DIR=%%~P"

if not defined VA_DIR (
    echo [HATA] game_v^<numara^>.apk bulunamadi. Bakilan yerler:
    echo    %~dp0
    echo    %~dp0deploy\player\
    echo    %~dp0Builds\player\
    echo.
    echo APK'yi uretmek icin: scripts\deploy-player-apk.bat
    echo ^(Unity editoru kapali olmali; betik surum numarasini sorar.^)
    pause
    exit /b 1
)

rem  Numeric sort is not reliable in pure batch (string compare puts 9 after
rem  118), so PowerShell produces the sorted version list.
set "VA_LIST="
set "VA_LAST="
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "Get-ChildItem -LiteralPath '!VA_DIR!' -Filter 'game_v*.apk' ^| ForEach-Object { if ($_.BaseName -match 'game_v([0-9]+)$') { [int]$Matches[1] } } ^| Sort-Object -Unique"`) do (
    set "VA_LIST=!VA_LIST!  %%V"
    set "VA_LAST=%%V"
)

if not defined VA_LAST (
    echo [HATA] Klasorde game_v^<numara^>.apk desenine uyan dosya yok:
    echo    !VA_DIR!
    echo.
    echo APK'yi uretmek icin: scripts\deploy-player-apk.bat
    pause
    exit /b 1
)

echo Klasor: !VA_DIR!
echo Bulunan surumler:!VA_LIST!
echo.

:va_pick
set "VA_SEL="
set "VA_DEF=0"
set /p "VA_SEL=Kurulacak surum (bos birakirsan en yenisi: !VA_LAST!): "
set "VA_SEL=!VA_SEL: =!"
if not defined VA_SEL (
    set "VA_SEL=!VA_LAST!"
    set "VA_DEF=1"
)

rem  "007" is the same version as "7": the file name carries the plain integer.
:va_trim_zeros
if not "!VA_SEL:~0,1!"=="0" goto :va_trimmed
if "!VA_SEL:~1!"=="" goto :va_trimmed
set "VA_SEL=!VA_SEL:~1!"
goto :va_trim_zeros
:va_trimmed
set "VA_APK=!VA_DIR!game_v!VA_SEL!.apk"
if exist "!VA_APK!" goto :va_picked
echo.
echo [HATA] Bu surum bulunamadi: !VA_APK!
if "!VA_DEF!"=="1" (
    echo        Varsayilan surum de okunamadi - kurulum yapilamiyor.
    pause
    exit /b 1
)
echo        Yukaridaki listeden bir numara girin ^(Enter = !VA_LAST!^).
echo.
goto :va_pick

:va_picked
echo.
echo Kurulacak APK:
echo    !VA_APK!
for %%F in ("!VA_APK!") do echo    %%~zF bayt  -  %%~tF
echo.

where adb >nul 2>nul
if errorlevel 1 (
    echo [HATA] adb bulunamadi. Android platform-tools kurulu ve PATH'e ekli olmali.
    echo Meta Quest Developer Hub kuruluysa adb onunla birlikte gelir.
    pause
    exit /b 1
)

rem =====================================================================
rem  Device authorization
rem  "unauthorized" = the headset has not accepted this PC's RSA key.
rem  A separate approval is needed even with developer mode ON; renewing the
rem  key (%USERPROFILE%\.android\adbkey) invalidates the old approval.
rem  Fix: adb kill-server + start-server -> the key is sent again and the
rem  approval dialog reappears on the headset. The script asks for permission
rem  first, then re-queries adb to confirm the user really approved it.
rem =====================================================================

:va_devcheck
echo Bagli cihazlar:
adb devices
echo.

call :va_find_unauthorized
if not defined VA_UNAUTH goto :va_install

echo [UYARI] Gozluk "unauthorized" durumda  ^(!VA_UNAUTH!^)
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
rem  The package name carries the version, so each build lives next to the
rem  others on the headset instead of replacing them.
set "VA_PKG=com.vortex.arenav!VA_SEL!"
set "VA_INSTALL_LOG=%TEMP%\va_install_log_%RANDOM%.txt"

echo Gozluge kuruluyor ^(paket: !VA_PKG!^), lutfen bekleyin...
call :va_do_install
if "!VA_INSTALL_OK!"=="1" goto :va_install_done

findstr /i "INSTALL_FAILED_UPDATE_INCOMPATIBLE" "!VA_INSTALL_LOG!" >nul
if not errorlevel 1 (
    echo.
    echo [UYARI] Gozlukteki kurulum farkli bir imzayla imzalanmis - once o kaldiriliyor...
    echo ^> adb uninstall !VA_PKG!
    adb uninstall !VA_PKG!
    echo.
    echo Tekrar kuruluyor...
    call :va_do_install
    rem  Delayed expansion is mandatory here: %VA_INSTALL_OK% would expand when
    rem  the block is PARSED, i.e. before :va_do_install ran, and a successful
    rem  second install would still fall through to the failure branch.
    if "!VA_INSTALL_OK!"=="1" goto :va_install_done
)

echo.
echo [HATA] Kurulum basarisiz. Kontrol edin:
echo   - Gozluk USB ile bagli mi? Kablosuz icin: adb connect ^<gozluk-ip^>:5555
echo   - Gelistirici modu acik mi, USB hata ayiklama izni verildi mi?
echo   - Birden fazla cihaz bagliysa digerlerini cikarin.
echo   - Eski bir kurulum baska bir paket adiyla duruyorsa once onu kaldirin:
echo       adb uninstall com.UnityTechnologies.com.unity.template.urpblank
del "!VA_INSTALL_LOG!" >nul 2>nul
pause
exit /b 1

:va_install_done
del "!VA_INSTALL_LOG!" >nul 2>nul
echo.
echo Kurulum tamamlandi ^(paket: !VA_PKG!^).
pause
exit /b 0

rem ---------------------------------------------------------------------
rem  Runs adb install -r -g, prints the output and also writes it to a log
rem  file (to detect the signature mismatch); sets VA_INSTALL_OK=1 on success.
rem ---------------------------------------------------------------------
:va_do_install
set "VA_INSTALL_OK=0"
adb install -r -g "!VA_APK!" > "!VA_INSTALL_LOG!" 2>&1
type "!VA_INSTALL_LOG!"
findstr /i /c:"Success" "!VA_INSTALL_LOG!" >nul
if not errorlevel 1 set "VA_INSTALL_OK=1"
exit /b 0

:va_auth_abort
echo.
echo Kurulum yapilmadi - cihaz yetkilendirilmedi.
pause
exit /b 1

rem ---------------------------------------------------------------------
rem  Looks for an "unauthorized" line in adb devices output and stores the
rem  serial in VA_UNAUTH; leaves it undefined otherwise.
rem ---------------------------------------------------------------------
:va_find_unauthorized
set "VA_UNAUTH="
for /f "tokens=1,2" %%A in ('adb devices 2^>nul') do (
    if /i "%%B"=="unauthorized" set "VA_UNAUTH=%%A"
)
exit /b 0
