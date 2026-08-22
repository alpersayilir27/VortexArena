@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-player-apk.bat
rem  Builds the Unity player (Meta Quest 3/3S) -> deploy\player\game_v<N>.apk
rem  and copies the installer next to it (deploy\player\install_game.bat).
rem
rem  VERSIONED BUILD: the script always asks for a version number. It goes
rem  into the APK name AND into the Android application id
rem  (com.vortex.arenav<N>, no dot - an Android package segment cannot start
rem  with a digit), so several versions stay installed side by side on one
rem  headset. There is no version-less player build.
rem
rem  Only the platform differs from the admin build: scene list, role and
rem  server address resolution are identical. The player build falls to the
rem  player role at runtime and finds the server over a UDP beacon - no IP
rem  is baked in.
rem
rem  IMPORTANT 1: batch-mode Unity can hit the project lock while the editor
rem  has the same project open. This script does NOT check for it (deliberate,
rem  see deploy-admin-game.bat). If the build hangs on the lock, cancel and
rem  restart it by hand.
rem
rem  IMPORTANT 2: THE TARGET PLATFORM IS PINNED HERE: -buildTarget Android.
rem  It is not derived from the active platform - whatever platform the
rem  project was left on, this script produces an APK. The flag is passed at
rem  Unity STARTUP: switching platform from inside -executeMethod triggers a
rem  domain reload and aborts the running method.
rem  If the active platform is not Android the switch happens at startup and
rem  that run means a FULL REIMPORT (textures recompressed to ASTC) - it can
rem  take 20-40 min; later runs are fast. The platform is NOT restored after
rem  the build (restoring would cost a second full reimport) and need not be:
rem  the admin script pins its own target too.
rem
rem  Unity path: UNITY_EXE environment variable > project version from Hub.
rem
rem  After the build the APK is POSTed to the publish endpoint on the server
rem  (updater_uploader\updater_uploader_main.py); if the endpoint is down only
rem  a warning is printed and the build still counts as successful.
rem
rem  Usage:
rem    deploy-player-apk.bat             double-clickable; asks for the
rem                                      version, waits at the end
rem    deploy-player-apk.bat --no-pause  exits without waiting - BUT the
rem                                      version cannot be asked in this mode,
rem                                      so the script fails with exit 2
rem    (the VORTEX_NO_PAUSE environment variable disables the wait too)
rem
rem  NOTE: script-local variables are VA_ prefixed - they are inherited by
rem  child processes (Unity -> IL2CPP -> Gradle) and short generic names break
rem  the build chain. Keep the VA_ prefix on new variables.
rem =====================================================================

rem --- Keep the window open on double click ----------------------------
set "VA_HOLD="
set "VA_AUTO="
set "VA_CL=%cmdcmdline%"
if not "!VA_CL:%~nx0=!"=="!VA_CL!" set "VA_HOLD=1"
if defined VORTEX_NO_PAUSE set "VA_AUTO=1"
if /i "%~1"=="--no-pause" set "VA_AUTO=1"
if defined VA_AUTO set "VA_HOLD="
set "VA_RC=0"

set "VA_REPO=%~dp0.."
for %%I in ("%VA_REPO%") do set "VA_REPO=%%~fI"
set "VA_OUT=%VA_REPO%\deploy\player"
set "VA_LOG=%VA_REPO%\deploy\player-build.log"

echo === VortexArena : oyuncu ^(Meta Quest / Android^) build ===
echo   Proje : %VA_REPO%
echo.

rem --- 0) Version (interactive) ----------------------------------------
rem  Automation mode has no console to ask on: set /p would return empty
rem  immediately and the prompt would loop forever. Fail early instead.
if defined VA_AUTO (
  echo [HATA] Otomasyon kipinde ^(--no-pause / VORTEX_NO_PAUSE^) surum sorulamaz.
  echo        Bu betik surumu yalnizca interaktif olarak sorar; beklemesiz
  echo        kipte calistirmayin.
  set "VA_RC=2"
  goto :son
)

echo   Girilen numara hem APK adina ^(game_v^<numara^>.apk^) hem paket adina
echo   ^(com.vortex.arenav^<numara^>^) girer; gozlukte diger surumlerin yanina
echo   kurulur, onlari silmez.

set "VA_VER="
set "VA_TRY=0"
:va_ask_version
set /a VA_TRY+=1
if !VA_TRY! GTR 5 (
  echo [HATA] Gecerli surum numarasi alinamadi ^(5 deneme^).
  echo        Beklenen: pozitif tam sayi, ornek 132.
  set "VA_RC=2"
  goto :son
)
set "VA_IN="
set /p "VA_IN=  Surum numarasi (or. 132): "
set "VA_IN=!VA_IN: =!"
if not defined VA_IN (
  echo   [UYARI] Surum bos birakilamaz.
  goto :va_ask_version
)
echo(!VA_IN!|findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 (
  echo   [UYARI] Yalnizca rakam girin ^(ornek: 132^).
  goto :va_ask_version
)

rem  Leading zeros are trimmed to the plain integer ("007" -> "7"): Unity parses
rem  -buildVersion as an int and would name the file game_v7.apk, while this
rem  script kept looking for game_v007.apk and called a finished build failed.
:va_trim_zeros
if not "!VA_IN:~0,1!"=="0" goto :va_trimmed
if "!VA_IN:~1!"=="" goto :va_trimmed
set "VA_IN=!VA_IN:~1!"
goto :va_trim_zeros
:va_trimmed
if "!VA_IN!"=="0" (
  echo   [UYARI] Surum sifir olamaz.
  goto :va_ask_version
)
set "VA_VER=!VA_IN!"
set "VA_APK=%VA_OUT%\game_v!VA_VER!.apk"

rem  Overwriting an existing version is legitimate (rebuild of the same
rem  number), so this only warns.
if exist "!VA_APK!" (
  echo   [UYARI] Bu surum zaten var, uzerine yazilacak: !VA_APK!
)
echo   Hedef : !VA_APK!
echo   Paket : com.vortex.arenav!VA_VER!
echo.

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

rem --- 3) Is the Android module installed? ------------------------------
rem  Without the module Unity cannot switch to Android and the build would
rem  silently continue as Windows and emit an .exe. Say so early and clearly.
for %%I in ("!VA_UNITY!") do set "VA_UDIR=%%~dpI"
if not exist "!VA_UDIR!Data\PlaybackEngines\AndroidPlayer" (
  echo [HATA] Unity Android Build Support kurulu degil:
  echo        "!VA_UDIR!Data\PlaybackEngines\AndroidPlayer" yok.
  echo        Unity Hub ^> Installs ^> %VA_UVER% ^> Add modules ile
  echo        "Android Build Support" + SDK/NDK + OpenJDK kurun.
  set "VA_RC=1"
  goto :son
)

rem --- 4) Prepare the output file --------------------------------------
rem  The FOLDER is never wiped: older game_v*.apk files must stay so the
rem  installer can offer them. Only the same-named file is replaced.
if not exist "%VA_OUT%" mkdir "%VA_OUT%" 2>nul
if exist "!VA_APK!" (
  echo   Temizlik: ayni isimli eski apk siliniyor...
  del /q "!VA_APK!" 2>nul
)
if exist "!VA_APK!" (
  echo [HATA] Eski cikti silinemedi: "!VA_APK!"
  set "VA_RC=1"
  goto :son
)

rem --- 5) Build --------------------------------------------------------
rem  -nographics IS NOT USED: shader variant compilation may need a graphics
rem  device and could silently produce a broken build.
echo.
echo   Build basliyor.
echo   [!] Hedef platform sabit: Android. Aktif platform baska ise acilista
echo       gecilir - o kosu tam reimport yuzunden 20-40 dk surebilir.
echo   Asagidaki durum satiri canli guncellenir.
echo   Log   : %VA_LOG%
del /q "%VA_LOG%" 2>nul
if exist "%VA_LOG%" (
  echo   [UYARI] Onceki log silinemedi - bir Unity sureci hala tutuyor.
)
set "VA_WATCH=%~dp0lib\watch-unity-build.ps1"
if exist "%VA_WATCH%" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%VA_WATCH%" ^
    -Unity "!VA_UNITY!" -Project "%VA_REPO%" -OutDir "%VA_OUT%" -Log "%VA_LOG%" ^
    -Method VortexArena.Core.Editor.PlayerBuildTool.BuildQuestPlayer ^
    -UnityBuildTarget Android -BuildVersion !VA_VER!
  set "VA_RC=!ERRORLEVEL!"
) else (
  echo   [UYARI] Izleyici yok, ilerleme basilamayacak: "%VA_WATCH%"
  "!VA_UNITY!" -batchmode -quit ^
    -projectPath "%VA_REPO%" ^
    -buildTarget Android ^
    -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildQuestPlayer ^
    -buildOutput "%VA_OUT%" ^
    -buildVersion !VA_VER! ^
    -logFile "%VA_LOG%"
  set "VA_RC=!ERRORLEVEL!"
)

rem  Failure diagnosis belongs to lib\explain-build-failure.ps1 (rationale in
rem  deploy-admin-game.bat): the cause is extracted from the log, and the lock
rem  hint is printed only if the log really shows a lock.
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

if not exist "%VA_APK%" (
  echo [HATA] Build 0 dondu ama apk yok: "%VA_APK%"
  echo        Log: %VA_LOG%
  set "VA_RC=1"
  goto :son
)

rem --- 6) Put the installer next to the apk ----------------------------
rem  install_game.bat looks for game_v*.apk in its OWN folder; it is copied so
rem  the two travel together (the copy in the repo root is the single source
rem  of truth).
if exist "%VA_REPO%\install_game.bat" (
  copy /y "%VA_REPO%\install_game.bat" "%VA_OUT%\install_game.bat" >nul
) else (
  echo   [UYARI] install_game.bat repo kokunde yok, kopyalanamadi.
)

echo.
echo === TAMAM ===
echo   %VA_APK%
powershell -NoProfile -Command "$s=(Get-Item '%VA_APK%').Length/1MB; Write-Host ('  Boyut: {0:N1} MB' -f $s)"

rem --- 7) Upload to the server (OTA) -----------------------------------
rem  The APK is sent to the publish endpoint on the server (updater_uploader
rem  script) - the Vortex Updater on the headset downloads it from the same
rem  address. An upload failure does NOT fail the build: the apk exists and
rem  can be published by hand.
set "VA_UPLOAD_URL=http://159.100.20.26:8091/upload?v=%VA_VER%"
echo.
echo   Sunucuya yukleniyor: %VA_UPLOAD_URL%
curl -f --connect-timeout 10 -X POST --data-binary "@%VA_APK%" "%VA_UPLOAD_URL%"
if errorlevel 1 (
  echo.
  echo   [UYARI] Sunucuya yukleme BASARISIZ - gozlukler bu surumu goremez.
  echo           Sunucuda updater_uploader_main.py calisiyor mu? 8091 disaridan erisilir mi?
  echo           Elle tekrar denemek icin:
  echo             curl -f -X POST --data-binary "@%VA_APK%" "%VA_UPLOAD_URL%"
) else (
  echo.
  echo   Sunucuya yuklendi - gozlukteki Vortex Updater artik bu surumu gorur.
)
echo.
echo   Gozluge kurmak icin: gozlugu USB ile bagla, gelistirici modu acik olsun,
echo   sonra "%VA_OUT%\install_game.bat" dosyasini calistir ve surumu sec.
echo   ^(Ayni APK her iki gozluge de kurulur - rol ve sunucu adresi gomulu degildir.^)

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
