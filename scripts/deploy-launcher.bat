@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-launcher.bat
rem  VortexArena.Launcher (WPF, .NET 10) -> single-file exe -> deploy\
rem
rem  Produces ONE self-contained file: deploy\VortexArena.Launcher.exe.
rem  No .NET install on the operator PC, no folder to carry around; adb
rem  ships inside the exe.
rem
rem  Prerequisite: .NET 10 SDK (dotnet on PATH). Nothing else.
rem
rem  Usage:
rem    deploy-launcher.bat             double-clickable; waits at the end
rem    deploy-launcher.bat --no-pause  automation; exits without waiting
rem    (VORTEX_NO_PAUSE=1 also disables the wait)
rem
rem  NOTE: script variables are VA_ prefixed. They are inherited by child
rem  processes and short generic names break the build chain (MSBuild
rem  reads environment variables as global properties). Keep the prefix.
rem =====================================================================

rem --- Keep the window open on a double click --------------------------
rem  cmdcmdline contains the script name when started by double click
rem  (or via "cmd /c script"). Then we wait at the end; otherwise an
rem  error message would blink and vanish.
set "VA_HOLD="
set "VA_CL=%cmdcmdline%"
if not "!VA_CL:%~nx0=!"=="!VA_CL!" set "VA_HOLD=1"
if defined VORTEX_NO_PAUSE set "VA_HOLD="
if /i "%~1"=="--no-pause" set "VA_HOLD="
set "VA_RC=0"

set "VA_REPO=%~dp0.."
for %%I in ("%VA_REPO%") do set "VA_REPO=%%~fI"
set "VA_ROOT=%VA_REPO%\deploy"
set "VA_TMP=%VA_ROOT%\.launcher-publish"
set "VA_EXE=%VA_ROOT%\VortexArena.Launcher.exe"
set "VA_PROJ=%VA_REPO%\launcher\VortexArena.Launcher\VortexArena.Launcher.csproj"

echo === VortexArena : launcher publish (tek dosya) ===
echo   Proje : %VA_PROJ%
echo   Hedef : %VA_EXE%
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

rem --- A running launcher locks its own exe -----------------------------
tasklist /fi "imagename eq VortexArena.Launcher.exe" 2>nul | find /i "VortexArena.Launcher.exe" >nul
if not errorlevel 1 (
  echo [HATA] VortexArena.Launcher.exe calisiyor - cikti kilitli.
  echo        Launcher'i kapatip tekrar deneyin.
  set "VA_RC=1"
  goto :son
)

if not exist "%VA_ROOT%" mkdir "%VA_ROOT%" 2>nul

rem --- Clean only the temporary publish folder --------------------------
if exist "%VA_TMP%" rmdir /s /q "%VA_TMP%"
if exist "%VA_TMP%" (
  echo [HATA] Gecici klasor silinemedi: "%VA_TMP%"
  set "VA_RC=1"
  goto :son
)

echo   Publish basliyor...
dotnet publish "%VA_PROJ%" -c Release ^
  -p:VortexSingleFile=true ^
  -o "%VA_TMP%"
if errorlevel 1 (
  echo.
  echo [HATA] dotnet publish basarisiz.
  echo        Kurulu SDK'lari gormek icin: dotnet --list-sdks
  set "VA_RC=1"
  goto :son
)

if not exist "%VA_TMP%\VortexArena.Launcher.exe" (
  echo [HATA] Publish 0 dondu ama exe yok: "%VA_TMP%\VortexArena.Launcher.exe"
  set "VA_RC=1"
  goto :son
)

copy /y "%VA_TMP%\VortexArena.Launcher.exe" "%VA_EXE%" >nul
if errorlevel 1 (
  echo [HATA] Exe kopyalanamadi: "%VA_EXE%"
  set "VA_RC=1"
  goto :son
)

rmdir /s /q "%VA_TMP%" 2>nul

echo.
echo === TAMAM ===
echo   %VA_EXE%
powershell -NoProfile -Command "$s=(Get-Item '%VA_EXE%').Length/1MB; Write-Host ('  Boyut: {0:N1} MB (tek dosya, self-contained)' -f $s)"
echo.
echo   Beklenen yerlesim (exe'nin yanindan okunur):
echo     %VA_ROOT%\server\VortexArena.Server.App.exe
echo     %VA_ROOT%\admin\VortexArena.exe
echo     %VA_ROOT%\game_versions\   (APK'lar buraya iner)
echo     %VA_ROOT%\replays\         (mac kayitlari)
echo.
echo   Operator ayarlari %%APPDATA%%\VortexArena\launcher\settings.json
echo   icinde durur; exe yeniden uretilse de ayarlar korunur.

if exist "%VA_ROOT%\launcher" (
  echo.
  echo   NOT: "%VA_ROOT%\launcher" klasoru artik kullanilmiyor, silebilirsiniz.
)

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
