@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-launcher.bat
rem  VortexArena.Launcher (WPF, .NET 10) -> single-file exe -> deploy\launcher\
rem
rem  deploy\launcher\ is the package: ONE self-contained exe (no .NET
rem  install on the operator PC) next to server\ server_versions\
rem  admin_versions\ game_versions\ replays\. The folders are created empty and their contents survive
rem  every rerun. platform-tools\ (adb) is mirrored from the repo root
rem  on every run.
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
set "VA_OUT=%VA_ROOT%\launcher"
set "VA_EXE=%VA_OUT%\VortexArena.Launcher.exe"
set "VA_PROJ=%VA_REPO%\launcher\VortexArena.Launcher\VortexArena.Launcher.csproj"
set "VA_PT=%VA_REPO%\platform-tools"

echo === VortexArena : launcher publish (tek dosya) ===
echo   Proje : %VA_PROJ%
echo   Hedef : %VA_EXE%
echo.

if not exist "%VA_PROJ%" (
  echo [HATA] Launcher projesi bulunamadi: "%VA_PROJ%"
  set "VA_RC=1"
  goto :son
)

if not exist "%VA_PT%\adb.exe" (
  echo [HATA] platform-tools bulunamadi: "%VA_PT%\adb.exe"
  set "VA_RC=1"
  goto :son
)

rem  A missing LFS object leaves a ~130-byte pointer that fails only on the
rem  operator PC, when the first headset is plugged in.
for %%F in ("%VA_PT%\adb.exe") do if %%~zF lss 1000000 (
  echo [HATA] "%VA_PT%\adb.exe" Git LFS isaretcisi, gercek dosya degil.
  echo        Once: git lfs pull
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

rem --- Close every running launcher (it locks its own exe) ---------------
rem  Only after a successful publish, so a failed build leaves it running.
rem  Also the adb server started from the package's platform-tools\ (it
rem  locks adb.exe; the next adb call restarts it). A server or admin
rem  started from server\ / admin_versions\ keeps running, it does not depend on
rem  the launcher. VA_OUT reaches PowerShell as an environment variable.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$pt = Join-Path $env:VA_OUT 'platform-tools'; $p = @(Get-Process | Where-Object { $_.ProcessName -eq 'VortexArena.Launcher' -or ($_.Path -and [IO.Path]::GetDirectoryName($_.Path) -in @($env:VA_OUT, $pt)) }); foreach ($x in $p) { Write-Host ('  Kapatiliyor: {0} (PID {1})' -f $x.ProcessName, $x.Id) }; $p | Stop-Process -Force -ErrorAction SilentlyContinue; $p | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue"

rem --- Package folder: the exe + the four layout folders ----------------
rem  server\ server_versions\ admin_versions\ game_versions\ replays\ and their
rem  contents are KEPT (venue data, builds, APKs, recordings); platform-tools\ is mirrored below;
rem  every other top-level entry is stale output and goes. Released
rem  handles lag a little after a kill, hence the retries.
if not exist "%VA_OUT%" mkdir "%VA_OUT%"
set "VA_TRY=0"
:temizle
del /f /q /a "%VA_OUT%\*" >nul 2>&1
for /d %%D in ("%VA_OUT%\*") do (
  set "VA_KEEP="
  for %%K in (server server_versions admin_versions game_versions replays platform-tools) do if /i "%%~nxD"=="%%K" set "VA_KEEP=1"
  if not defined VA_KEEP rmdir /s /q "%%D" 2>nul
)
dir /b /a-d "%VA_OUT%" 2>nul | findstr . >nul
if not errorlevel 1 (
  set /a VA_TRY+=1
  if !VA_TRY! lss 5 (
    ping -n 2 127.0.0.1 >nul
    goto :temizle
  )
  echo [HATA] Cikti klasoru temizlenemedi: "%VA_OUT%"
  echo        Kilitli kalanlar:
  dir /b /a-d "%VA_OUT%"
  set "VA_RC=1"
  goto :son
)

copy /y "%VA_TMP%\VortexArena.Launcher.exe" "%VA_EXE%" >nul
if errorlevel 1 (
  echo [HATA] Exe kopyalanamadi: "%VA_EXE%"
  set "VA_RC=1"
  goto :son
)

rem  /MIR: unchanged files are skipped, removed ones go. Codes 0-7 = ok.
robocopy "%VA_PT%" "%VA_OUT%\platform-tools" /MIR /R:3 /W:1 /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (
  echo [HATA] platform-tools kopyalanamadi: "%VA_OUT%\platform-tools"
  set "VA_RC=1"
  goto :son
)

for %%K in (server server_versions admin_versions game_versions replays) do (
  if not exist "%VA_OUT%\%%K" mkdir "%VA_OUT%\%%K"
)

rmdir /s /q "%VA_TMP%" 2>nul

echo.
echo === TAMAM ===
echo   %VA_EXE%
powershell -NoProfile -Command "$s=(Get-Item '%VA_EXE%').Length/1MB; Write-Host ('  Boyut: {0:N1} MB (tek dosya, self-contained)' -f $s)"
echo.
echo   Paket (kok = exe'nin klasoru; klasorlerin icerigi korunur):
echo     %VA_OUT%\VortexArena.Launcher.exe
echo     %VA_OUT%\server\          (sunucunun isletme verisi: config, log, olcum)
echo     %VA_OUT%\server_versions\ (sunucu surumleri buraya iner)
echo     %VA_OUT%\admin_versions\  (admin surumleri buraya iner)
echo     %VA_OUT%\game_versions\   (APK'lar buraya iner)
echo     %VA_OUT%\replays\         (mac kayitlari)
echo     %VA_OUT%\platform-tools\  (adb; her calistirmada repodan yenilenir)
echo.
echo   Sunucu ve admin surumleri launcher'in Sunucu / Yonetim sayfalarindan
echo   indirilir, elle kopyalanmaz. Yerel denemek icin:
echo     "%VA_REPO%\deploy\admin\admin_v^<N^>"   -^> "%VA_OUT%\admin_versions\admin_v^<N^>"
echo     "%VA_REPO%\deploy\server\server_v^<N^>" -^> "%VA_OUT%\server_versions\server_v^<N^>"
echo.
echo   Operator ayarlari %%APPDATA%%\VortexArena\launcher\settings.json
echo   icinde durur; exe yeniden uretilse de ayarlar korunur.

if exist "%VA_ROOT%\VortexArena.Launcher.exe" (
  echo.
  echo   NOT: "%VA_ROOT%\VortexArena.Launcher.exe" eski konum, silebilirsiniz.
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
