@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-launcher.bat
rem  VortexArena.Launcher (WPF, .NET 10) -> dotnet publish -> deploy\launcher\
rem
rem  Kendine yeten (self-contained) TEK klasor uretir: operator PC'sine
rem  .NET kurmak gerekmez. Klasorun TAMAMI tasinir, exe tek basina calismaz.
rem
rem  On kosul: .NET 10 SDK (dotnet PATH'te). Baska hicbir sey gerekmez.
rem
rem  Kullanim:
rem    deploy-launcher.bat             cift tiklanabilir; sonda bekler
rem    deploy-launcher.bat --no-pause  otomasyon; beklemeden cikar
rem    (VORTEX_NO_PAUSE=1 ortam degiskeni de beklemeyi kapatir)
rem
rem  NOT: betik-ici degiskenler VA_ onekli. Sebep: bu degiskenler cocuk
rem  sureclere miras kaliyor ve kisa genel adlar derleme zincirini kiriyor
rem  (MSBuild ortam degiskenlerini global property olarak okuyor). Yeni
rem  degisken eklerken VA_ onekini koru.
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
set "VA_OUT=%VA_REPO%\deploy\launcher"
set "VA_PROJ=%VA_REPO%\launcher\VortexArena.Launcher\VortexArena.Launcher.csproj"

echo === VortexArena : launcher publish ===
echo   Proje : %VA_PROJ%
echo   Hedef : %VA_OUT%
echo.

if not exist "%VA_PROJ%" (
  echo [HATA] Launcher projesi bulunamadi: "%VA_PROJ%"
  set "VA_RC=1"
  goto :son
)

where dotnet >nul 2>&1
if errorlevel 1 (
  echo [HATA] dotnet PATH'te yok. .NET 10 SDK kurun.
  echo        https://dotnet.microsoft.com/download
  set "VA_RC=1"
  goto :son
)

rem --- Launcher acik ise exe kilitli olur, publish yarida kalir ---------
tasklist /fi "imagename eq VortexArena.Launcher.exe" 2>nul | find /i "VortexArena.Launcher.exe" >nul
if not errorlevel 1 (
  echo [HATA] VortexArena.Launcher.exe calisiyor - cikti klasoru kilitli.
  echo        Launcher'i kapatip tekrar deneyin.
  set "VA_RC=1"
  goto :son
)

rem --- Cikti klasorunu temizle -----------------------------------------
if exist "%VA_OUT%" (
  echo   Temizlik: eski cikti siliniyor...
  rmdir /s /q "%VA_OUT%"
)
if exist "%VA_OUT%" (
  echo [HATA] Eski cikti silinemedi: "%VA_OUT%"
  set "VA_RC=1"
  goto :son
)
mkdir "%VA_OUT%" 2>nul

rem --- Publish ---------------------------------------------------------
echo   Publish basliyor...
dotnet publish "%VA_PROJ%" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=false ^
  -o "%VA_OUT%"
if errorlevel 1 (
  echo.
  echo [HATA] dotnet publish basarisiz.
  echo        Kurulu SDK'lari gormek icin: dotnet --list-sdks
  set "VA_RC=1"
  goto :son
)

if not exist "%VA_OUT%\VortexArena.Launcher.exe" (
  echo [HATA] Publish 0 dondu ama exe yok: "%VA_OUT%\VortexArena.Launcher.exe"
  set "VA_RC=1"
  goto :son
)

echo.
echo === TAMAM ===
echo   %VA_OUT%\VortexArena.Launcher.exe
powershell -NoProfile -Command "$s=(Get-ChildItem '%VA_OUT%' -Recurse -File | Measure-Object -Sum Length).Sum/1MB; Write-Host ('  Boyut: {0:N1} MB (self-contained)' -f $s)"
echo.
echo   Klasorun TAMAMINI tasiyin - exe tek basina calismaz.
echo   Operator ayarlari %%APPDATA%%\VortexArena\launcher\settings.json icinde
echo   durur; bu klasor yeniden dagitilsa da ayarlar korunur.

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
