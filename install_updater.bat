@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"

rem =====================================================================
rem  install_updater.bat
rem  OTA guncelleyiciyi (com.vortex.updater) bagli gozluge kurar.
rem
rem  APK'yi IKI YERDE arar, ilk buldugunu kurar:
rem    1) bu betigin yani     ->  deploy_android_updater.bat betigi apk'nin
rem                               yanina bu dosyanin bir kopyasini koyar
rem    2) deploy\updater\     ->  repo kokunden calistirildiginda normal yol
rem
rem  Bu kurulum USB ile YALNIZ BIR KEZ yapilir; sonrasinda oyun APK'si
rem  gozlukteki updater uzerinden kablosuz guncellenir.
rem =====================================================================

set "VA_APK="
for %%P in (
  "%~dp0VortexUpdater.apk"
  "%~dp0deploy\updater\VortexUpdater.apk"
) do if not defined VA_APK if exist "%%~fP" set "VA_APK=%%~fP"

if not defined VA_APK (
    echo [HATA] VortexUpdater.apk bulunamadi. Bakilan yerler:
    echo    %~dp0VortexUpdater.apk
    echo    %~dp0deploy\updater\VortexUpdater.apk
    echo.
    echo APK'yi uretmek icin: scripts\deploy_android_updater.bat
    pause
    exit /b 1
)

echo Kurulacak APK:
echo    %VA_APK%
for %%F in ("%VA_APK%") do echo    %%~zF bayt  -  %%~tF
echo.

rem =====================================================================
rem  adb bul: once PATH, sonra Unity'nin Android modulundeki platform-tools.
rem  Bulunan yol VA_ADB'de tutulur ve tum cagrilar onun uzerinden gider -
rem  ciplak "adb" cagrisi PATH'te olmadigi makinede sessizce farkli davranir.
rem =====================================================================
set "VA_ADB="
for /f "delims=" %%A in ('where adb 2^>nul') do if not defined VA_ADB set "VA_ADB=%%A"

if not defined VA_ADB (
    set "VA_UVER="
    for /f "tokens=2" %%v in ('findstr /b "m_EditorVersion:" "%~dp0ProjectSettings\ProjectVersion.txt" 2^>nul') do set "VA_UVER=%%v"
    if defined UNITY_EXE (
        set "VA_UNITY=%UNITY_EXE%"
    ) else (
        set "VA_UNITY=C:\Program Files\Unity\Hub\Editor\!VA_UVER!\Editor\Unity.exe"
    )
    for %%I in ("!VA_UNITY!") do set "VA_UDIR=%%~dpI"
    set "VA_TRY=!VA_UDIR!Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
    if exist "!VA_TRY!" set "VA_ADB=!VA_TRY!"
)

if not defined VA_ADB (
    echo [HATA] adb bulunamadi. Android platform-tools kurulu ve PATH'e ekli olmali.
    echo   - Meta Quest Developer Hub kuruluysa adb onunla birlikte gelir.
    echo   - Unity Android Build Support kuruluysa adb su yolda olur:
    echo       ...\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe
    echo     Unity baska bir dizindeyse UNITY_EXE ortam degiskenini ayarlayin.
    pause
    exit /b 1
)

echo adb:
echo    %VA_ADB%
echo.

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
"%VA_ADB%" devices
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
"%VA_ADB%" kill-server
echo ^> adb start-server
"%VA_ADB%" start-server
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
"%VA_ADB%" devices
echo.

:va_install
echo Gozluge kuruluyor, lutfen bekleyin...
"%VA_ADB%" install -r "%VA_APK%"
if errorlevel 1 (
    echo.
    echo [HATA] Kurulum basarisiz. Kontrol edin:
    echo   - Gozluk USB ile bagli mi? Kablosuz icin: adb connect ^<gozluk-ip^>:5555
    echo   - Gelistirici modu acik mi, USB hata ayiklama izni verildi mi?
    echo   - Birden fazla cihaz bagliysa digerlerini cikarin.
    echo   - Gozlukte baska bir makinenin imzasiyla kurulmus bir updater duruyorsa
    echo     imza uyusmazligi olur; once onu kaldirin:
    echo       adb uninstall com.vortex.updater
    pause
    exit /b 1
)

echo.
echo Kurulum tamamlandi.
echo.
echo   - Uygulama gozlukte "Bilinmeyen Kaynaklar" listesinde "Vortex Updater"
echo     adiyla gorunur.
echo   - Ilk acilista "bilinmeyen uygulamalardan kurulum" iznini onaylamak
echo     gerekir; uygulama sizi ilgili ayar ekranina kendisi goturur.
echo   - Bundan sonra oyun guncellemesi USB'siz yapilir: updater'i acip
echo     "Guncelle" dugmesine basmak yeterli.
pause
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
for /f "tokens=1,2" %%A in ('"%VA_ADB%" devices 2^>nul') do (
    if /i "%%B"=="unauthorized" set "VA_UNAUTH=%%A"
)
exit /b 0
