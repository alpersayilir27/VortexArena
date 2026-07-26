@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-launcher.bat
rem  Flutter launcher (Windows desktop) -> deploy\launcher\
rem
rem  Flutter'in Windows build'i tek klasordur: vortex_launcher.exe +
rem  flutter_windows.dll + data\. Klasorun TAMAMI tasinir, exe tek basina
rem  calismaz.
rem
rem  On kosullar:
rem    * Flutter SDK PATH'te (veya FLUTTER_EXE ortam degiskeni)
rem    * Visual Studio + "Desktop development with C++" workload
rem    * Windows Developer Mode ACIK (plugin symlink destegi icin)
rem      -> start ms-settings:developers
rem
rem  Kullanim:
rem    deploy-launcher.bat             cift tiklanabilir; sonda bekler
rem    deploy-launcher.bat --no-pause  otomasyon; beklemeden cikar
rem    (VORTEX_NO_PAUSE=1 ortam degiskeni de beklemeyi kapatir)
rem
rem  NOT: betik-ici degiskenler VA_ onekli. Sebep: bu degiskenler cocuk
rem  sureclere miras kaliyor ve kisa genel adlar derleme zincirini kiriyor
rem  (ornek: "RC" -> CMake onu resource compiler saniyor, MSBuild ortam
rem  degiskenlerini global property olarak okuyor). Yeni degisken eklerken
rem  VA_ onekini koru.
rem =====================================================================

rem --- Cift tiklamada pencere kapanmasin -------------------------------
rem  cmdcmdline, betik cift tiklanarak (veya "cmd /c betik" ile) baslatilinca
rem  betigin adini icerir. Oyleyse sonda bekleriz; yoksa hata mesaji goz
rem  kirpip kaybolur. Zaten acik bir konsoldan calistirilirsa beklemez.
set "VA_HOLD="
set "VA_CL=%cmdcmdline%"
if not "!VA_CL:%~nx0=!"=="!VA_CL!" set "VA_HOLD=1"
if defined VORTEX_NO_PAUSE set "VA_HOLD="
if /i "%~1"=="--no-pause" set "VA_HOLD="
set "VA_RC=0"

set "VA_REPO=%~dp0.."
for %%I in ("%VA_REPO%") do set "VA_REPO=%%~fI"
set "VA_SRC=%VA_REPO%\launcher"
set "VA_OUT=%VA_REPO%\deploy\launcher"

echo === VortexArena : launcher build ===
echo   Proje : %VA_SRC%
echo   Hedef : %VA_OUT%
echo.

if not exist "%VA_SRC%\pubspec.yaml" (
  echo [HATA] Flutter projesi bulunamadi: "%VA_SRC%\pubspec.yaml"
  set "VA_RC=1"
  goto :son
)

rem --- Flutter'i TAM YOLA coz ------------------------------------------
rem  Iki ayri tuzak, ikisi de flutter.bat'tan kaynakli:
rem   1) "call flutter ..." CAGIRAN BETIGI de sonlandirir. flutter.bat sonunda
rem      "... & exit_with_errorlevel.bat" zinciri var ve bu zincir call ile
rem      girilen batch baglamini komple bitiriyor -> betik hicbir sey yazmadan
rem      olurdu (pencere aninda kapaniyordu). Cozum: her cagri ayri bir
rem      "cmd /c call" cocuk surecinde.
rem   2) PATH'ten tirnakli cagrilirsa ("flutter") FLUTTER_ROOT'u yanlis cozup
rem      "The Flutter directory is not a clone of the GitHub project" verir.
rem      Cozum: once tam yola coz, sonra tam yolla cagir.
set "VA_FLUTTER="
set "VA_HINT=flutter.bat"
if defined FLUTTER_EXE set "VA_HINT=%FLUTTER_EXE%"
if exist "%VA_HINT%" (
  for %%I in ("%VA_HINT%") do set "VA_FLUTTER=%%~fI"
) else (
  for /f "delims=" %%I in ('where "%VA_HINT%" 2^>nul') do if not defined VA_FLUTTER set "VA_FLUTTER=%%I"
)
if not defined VA_FLUTTER (
  echo [HATA] Flutter bulunamadi: "%VA_HINT%"
  echo        Flutter SDK'yi PATH'e ekleyin veya FLUTTER_EXE ile tam yolu verin:
  echo          set FLUTTER_EXE=C:\src\flutter\bin\flutter.bat
  set "VA_RC=1"
  goto :son
)
echo   Flutter : !VA_FLUTTER!

cmd /c call "!VA_FLUTTER!" --version >nul 2>&1
if errorlevel 1 (
  echo [HATA] Flutter calistirilamadi: "!VA_FLUTTER!"
  echo        Tanilama icin elle deneyin: "!VA_FLUTTER!" doctor
  set "VA_RC=1"
  goto :son
)

rem --- Developer Mode acik mi? -----------------------------------------
rem  Flutter'in Windows plugin sistemi symlink kurar; Developer Mode kapaliysa
rem  build "Building with plugins requires symlink support" ile kirilir.
rem  Dakikalarca surecek pub get + build'e girmeden burada duruyoruz.
set "VA_DEVMODE=0"
for /f "tokens=3" %%I in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /v AllowDevelopmentWithoutDevLicense 2^>nul ^| find /i "AllowDevelopmentWithoutDevLicense"') do set "VA_DEVMODE=%%I"
if /i not "!VA_DEVMODE!"=="0x1" (
  echo [HATA] Windows Developer Mode KAPALI - Flutter plugin symlink'leri kurulamaz.
  echo        Acin:  start ms-settings:developers
  echo        Ayarlar ^> Gizlilik ve guvenlik ^> Gelistiriciler icin ^> Gelistirici Modu
  echo        Actiktan sonra bu betigi tekrar calistirin.
  set "VA_RC=1"
  goto :son
)

rem --- Build -----------------------------------------------------------
pushd "%VA_SRC%"

echo.
echo   [1/2] flutter pub get ...
cmd /c call "!VA_FLUTTER!" pub get
if errorlevel 1 (
  echo.
  echo [HATA] flutter pub get basarisiz.
  popd
  set "VA_RC=1"
  goto :son
)

echo.
echo   [2/2] flutter build windows --release ...
cmd /c call "!VA_FLUTTER!" build windows --release
if errorlevel 1 (
  echo.
  echo [HATA] flutter build windows basarisiz.
  echo        Gerekli: Visual Studio + "Desktop development with C++" workload.
  echo        CMake/MSBuild hatasi veriyorsa cache bayat olabilir:
  echo          "!VA_FLUTTER!" clean     ^(launcher\ icinde^)
  popd
  set "VA_RC=1"
  goto :son
)
popd

rem --- Ciktiyi bul -----------------------------------------------------
set "VA_RELEASE=%VA_SRC%\build\windows\x64\runner\Release"
if not exist "%VA_RELEASE%\vortex_launcher.exe" (
  echo [HATA] Build ciktisi bulunamadi: "%VA_RELEASE%\vortex_launcher.exe"
  set "VA_RC=1"
  goto :son
)

rem --- Kopyala ---------------------------------------------------------
if exist "%VA_OUT%" (
  echo.
  echo   Temizlik: eski cikti siliniyor...
  rmdir /s /q "%VA_OUT%"
)
if exist "%VA_OUT%" (
  echo [HATA] Eski cikti silinemedi: "%VA_OUT%"
  echo        Launcher acik olabilir ^(vortex_launcher.exe^) - kapatip tekrar deneyin.
  set "VA_RC=1"
  goto :son
)
mkdir "%VA_OUT%" 2>nul
echo   Kopyalaniyor...
robocopy "%VA_RELEASE%" "%VA_OUT%" /e /njh /njs /ndl /nc /ns /nfl >nul
rem robocopy 0-7 = basarili, 8+ = hata
if errorlevel 8 (
  echo [HATA] Kopyalama basarisiz ^(robocopy %ERRORLEVEL%^).
  set "VA_RC=1"
  goto :son
)

echo.
echo === TAMAM ===
echo   %VA_OUT%\vortex_launcher.exe
powershell -NoProfile -Command "$s=(Get-ChildItem '%VA_OUT%' -Recurse -File | Measure-Object -Sum Length).Sum/1MB; Write-Host ('  Boyut: {0:N1} MB' -f $s)"
echo.
echo   Klasorun TAMAMINI tasiyin - exe tek basina calismaz.

:son
if not "%VA_RC%"=="0" (
  echo.
  echo === BASARISIZ ^(cikis kodu %VA_RC%^) ===
)
if defined VA_HOLD (
  echo.
  pause
)
exit /b %VA_RC%
