@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-admin-game.bat
rem  Builds the Unity admin (Windows) build -> deploy\admin\VortexArena.exe
rem
rem  The role and the server address are NOT baked into the build: the desktop
rem  build falls to the admin role at runtime and reads the address from the
rem  --server-ip argument passed by the launcher (AppBoot). The launcher starts
rem  this exe.
rem
rem  IMPORTANT: batch-mode Unity can hit the project lock while the editor has
rem  the same project open. This script does NOT check for it (deliberate): even
rem  after the editor is closed, the Unity.exe of sub-processes such as the AI
rem  engine can live on in the background, and the tasklist check kept raising
rem  false alarms. If the build hangs on the lock, cancel and restart it by hand.
rem
rem  Unity path: UNITY_EXE environment variable > project version from Hub.
rem
rem  Usage:
rem    deploy-admin-game.bat                    double-clickable; asks for the tenant
rem                                             (A = every venue, number = one venue),
rem                                             waits at the end
rem    deploy-admin-game.bat --no-pause         automation; no menu (every venue),
rem                                             exits without waiting
rem    deploy-admin-game.bat --tenant <Venue>   only the shared scenes + that
rem                                             venue's scenes, no menu
rem    (the VORTEX_NO_PAUSE=1 environment variable disables the wait too)
rem
rem  THE TENANT MUST MATCH THE APK: start_match looks the scene up in every
rem  player's hello.scenes, so an installation's admin and its headsets have to
rem  be built with the SAME --tenant, otherwise the match is rejected silently.
rem
rem  NOTE: script-local variables are VA_ prefixed. Reason: they are inherited by
rem  child processes (Unity -> IL2CPP -> MSVC) and short generic names break the
rem  build chain (example: "RC" -> CMake/MSVC takes it for the resource
rem  compiler). Keep the VA_ prefix on new variables.
rem =====================================================================

rem --- Keep the window open on double click ----------------------------
rem  cmdcmdline contains the script name when the script is started by double
rem  click (or via "cmd /c script"). Then we wait at the end; otherwise an error
rem  message blinks and disappears. Run from an already open console it does not wait.
set "VA_HOLD="
set "VA_AUTO="
set "VA_CL=%cmdcmdline%"
if not "!VA_CL:%~nx0=!"=="!VA_CL!" set "VA_HOLD=1"
if defined VORTEX_NO_PAUSE (
  set "VA_AUTO=1"
  set "VA_HOLD="
)
set "VA_RC=0"

rem --- Arguments (any order) -------------------------------------------
set "VA_TENANT="
:va_args
if "%~1"=="" goto :va_args_done
if /i "%~1"=="--no-pause" (
  set "VA_AUTO=1"
  set "VA_HOLD="
  shift /1
  goto :va_args
)
if /i "%~1"=="--tenant" (
  if "%~2"=="" (
    echo [HATA] --tenant icin isletme adi verilmedi.
    echo        Kullanim: deploy-admin-game.bat [--no-pause] [--tenant ^<Isletme^>]
    set "VA_RC=2"
    goto :son
  )
  set "VA_TENANT=%~2"
  rem  "/1" matters: a bare shift moves %0 too and %~dp0 below would
  rem  point at an argument instead of the script.
  shift /1
  shift /1
  goto :va_args
)
echo [HATA] Bilinmeyen arguman: %~1
echo        Kullanim: deploy-admin-game.bat [--no-pause] [--tenant ^<Isletme^>]
set "VA_RC=2"
goto :son
:va_args_done

set "VA_REPO=%~dp0.."
for %%I in ("%VA_REPO%") do set "VA_REPO=%%~fI"
set "VA_OUT=%VA_REPO%\deploy\admin"
set "VA_LOG=%VA_REPO%\deploy\admin-build.log"

rem --- Tenant menu -----------------------------------------------------
rem  Asked only when --tenant was not given; automation mode has no console,
rem  so it builds every venue without asking.
if not defined VA_TENANT if not defined VA_AUTO (
  call "%~dp0lib\select-tenant.bat" "%VA_REPO%"
  if errorlevel 1 (
    set "VA_RC=2"
    goto :son
  )
  echo.
)

echo === VortexArena : admin (Windows) build ===
echo   Proje : %VA_REPO%
echo   Hedef : %VA_OUT%
if defined VA_TENANT (
  echo   Tenant: !VA_TENANT!
) else (
  echo   Tenant: hepsi
)
echo.

rem --- Tenant folder ---------------------------------------------------
rem  Checked up front: a typo must not be paid for with a build that only
rem  aborts inside Unity minutes later.
if defined VA_TENANT (
  if not exist "%VA_REPO%\Assets\Arenas\Venues\!VA_TENANT!\" (
    echo [HATA] Boyle bir isletme klasoru yok:
    echo        "%VA_REPO%\Assets\Arenas\Venues\!VA_TENANT!"
    echo        Kullanilabilir isletmeler:
    dir /b /ad "%VA_REPO%\Assets\Arenas\Venues" 2>nul
    set "VA_RC=1"
    goto :son
  )
)

rem --- 1) Project Unity version ----------------------------------------
set "VA_UVER="
for /f "tokens=2" %%v in ('findstr /b "m_EditorVersion:" "%VA_REPO%\ProjectSettings\ProjectVersion.txt"') do set "VA_UVER=%%v"
if not defined VA_UVER (
  echo [HATA] ProjectVersion.txt okunamadi:
  echo        "%VA_REPO%\ProjectSettings\ProjectVersion.txt"
  set "VA_RC=1"
  goto :son
)
echo   Surum : %VA_UVER%

rem --- 2) Locate Unity.exe ---------------------------------------------
if defined UNITY_EXE (
  set "VA_UNITY=%UNITY_EXE%"
) else (
  set "VA_UNITY=C:\Program Files\Unity\Hub\Editor\%VA_UVER%\Editor\Unity.exe"
)
if not exist "!VA_UNITY!" (
  echo [HATA] Unity bulunamadi: "!VA_UNITY!"
  echo        UNITY_EXE ortam degiskeni ile tam yolu verin:
  echo          set UNITY_EXE=D:\Unity\%VA_UVER%\Editor\Unity.exe
  set "VA_RC=1"
  goto :son
)
echo   Unity : !VA_UNITY!

rem --- 3) Wipe the output folder ---------------------------------------
if exist "%VA_OUT%" (
  echo   Temizlik: eski cikti siliniyor...
  rmdir /s /q "%VA_OUT%"
)
if exist "%VA_OUT%" (
  echo [HATA] Eski cikti silinemedi: "%VA_OUT%"
  echo        Admin oyunu acik olabilir ^(VortexArena.exe^) - kapatip tekrar deneyin.
  set "VA_RC=1"
  goto :son
)
mkdir "%VA_OUT%" 2>nul

rem --- 4) Build --------------------------------------------------------
rem  -nographics IS NOT USED: in the player build shader variant compilation may
rem  need a graphics device and could silently produce a broken build.
rem  THE TARGET PLATFORM IS PINNED HERE: -buildTarget Win64. It is not derived
rem  from the active platform - whatever platform the project was left on, this
rem  script produces a Windows build. The flag is passed at Unity STARTUP:
rem  switching platform from inside -executeMethod triggers a domain reload and
rem  aborts the running method.
rem  If the active platform is already Windows the flag is a no-op; otherwise the
rem  switch happens at startup and that run takes long because of a full reimport
rem  (textures recompressed to DXT) - later runs are fast.
echo.
echo   Build basliyor (hedef: Windows; platform degisiyorsa uzun surebilir)...
echo   Asagidaki durum satiri canli guncellenir; hicbir sey ilerlemiyorsa
echo   izleyici uyari basar (editor/arka plan Unity.exe proje kilidini tutuyor
echo   olabilir - Ctrl+C ile iptal edip surecleri kapattiktan sonra tekrar deneyin).
echo   Log   : %VA_LOG%
rem  Delete the old log: if Unity never starts, the error branch would print a
rem  STALE log and lead to the wrong diagnosis. If it cannot be deleted, a Unity
rem  process still holds the file - we do not block, only warn.
del /q "%VA_LOG%" 2>nul
if exist "%VA_LOG%" (
  echo   [UYARI] Onceki log silinemedi - bir Unity sureci hala tutuyor.
  echo           Build takilirsa o sureci kapatip tekrar deneyin;
  echo           asagida basilan log satirlari da bayat olabilir.
)
rem  Unity is run through the watcher, NOT directly: batch-mode Unity prints
rem  nothing to the console, so "stalled or progressing?" had no other answer.
rem  lib\watch-unity-build.ps1 builds the same command line, tails the log live,
rem  prints phase + percentage + stall warning and returns Unity's exit code
rem  unchanged. Without the watcher we fall back to the old behaviour.
rem  The tenant flag is prebuilt into a variable: an unset tenant must disappear
rem  from the command line entirely - a bare "-tenant" with no value would make
rem  PlayerBuildTool abort the build.
set "VA_TARG_PS="
set "VA_TARG_U="
if defined VA_TENANT (
  set "VA_TARG_PS=-Tenant !VA_TENANT!"
  set "VA_TARG_U=-tenant !VA_TENANT!"
)
set "VA_WATCH=%~dp0lib\watch-unity-build.ps1"
if exist "%VA_WATCH%" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%VA_WATCH%" ^
    -Unity "!VA_UNITY!" -Project "%VA_REPO%" -OutDir "%VA_OUT%" -Log "%VA_LOG%" ^
    -UnityBuildTarget Win64 !VA_TARG_PS!
  set "VA_RC=!ERRORLEVEL!"
) else (
  echo   [UYARI] Izleyici yok, ilerleme basilamayacak: "%VA_WATCH%"
  "!VA_UNITY!" -batchmode -quit ^
    -projectPath "%VA_REPO%" ^
    -buildTarget Win64 ^
    -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildWindowsAdmin ^
    -buildOutput "%VA_OUT%" ^
    !VA_TARG_U! ^
    -logFile "%VA_LOG%"
  set "VA_RC=!ERRORLEVEL!"
)

rem  Failure diagnosis belongs to lib\explain-build-failure.ps1: the last 30 log
rem  lines are always shutdown noise (memory leak JSON, licensing), the real cause
rem  is not visible there; and printing the lock hint unconditionally led to the
rem  wrong diagnosis with no Unity open. The helper extracts the cause and prints
rem  the lock hint only if the log really shows a lock.
set "VA_EXPLAIN=%~dp0lib\explain-build-failure.ps1"
if not "%VA_RC%"=="0" (
  echo.
  echo [HATA] Build basarisiz ^(exit %VA_RC%^).
  if exist "!VA_EXPLAIN!" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "!VA_EXPLAIN!" -Log "%VA_LOG%"
  ) else (
    echo   [UYARI] Teshis yardimcisi yok: "!VA_EXPLAIN!"
    powershell -NoProfile -Command "if (Test-Path '%VA_LOG%') { Get-Content '%VA_LOG%' -Tail 30 }"
  )
  goto :son
)

if not exist "%VA_OUT%\VortexArena.exe" (
  echo [HATA] Build 0 dondu ama exe yok: "%VA_OUT%\VortexArena.exe"
  echo        Log: %VA_LOG%
  set "VA_RC=1"
  goto :son
)

echo.
echo === TAMAM ===
echo   %VA_OUT%\VortexArena.exe
powershell -NoProfile -Command "$s=(Get-ChildItem '%VA_OUT%' -Recurse -File | Measure-Object -Sum Length).Sum/1MB; Write-Host ('  Boyut: {0:N1} MB' -f $s)"
echo.
echo   Launcher'in Ayarlar ekraninda bu exe'yi secin.

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
