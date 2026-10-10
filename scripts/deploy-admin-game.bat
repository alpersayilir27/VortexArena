@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-admin-game.bat
rem  Builds the Unity admin (Windows) build -> deploy\admin\admin_v<N>\
rem  and packs it next to it as deploy\admin\admin_v<N>.zip.
rem
rem  VERSIONED BUILD: the script always asks for a version number. It goes
rem  into the output folder name, into the zip name and into bundleVersion
rem  (Application.version, so the admin reports it to the server). There is
rem  no version-less admin build.
rem
rem  The application NAME (productName) deliberately stays version-less:
rem  operator settings live in PlayerPrefs, which Windows keys by
rem  productName - a per-version name would reset them on every version.
rem
rem  Before the prompt the admin versions already published on the server
rem  are listed (lib\list-server-versions.ps1 -> GET /admin/versions).
rem  Server unreachable = warning only, the prompt still comes.
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
rem  After the zip is written it is POSTed to the publish endpoint on the server
rem  (updater_uploader\updater_uploader_main.py); if the endpoint is down only
rem  a warning is printed and the build still counts as successful.
rem
rem  Usage:
rem    deploy-admin-game.bat                    double-clickable; asks for the
rem                                             tenant (A = every venue, number =
rem                                             one venue) and the version, waits
rem                                             at the end
rem    deploy-admin-game.bat --no-pause         exits without waiting - BUT the
rem                                             version cannot be asked in this
rem                                             mode, so the script fails with exit 2
rem    deploy-admin-game.bat --tenant <Venue>   only the shared scenes + that
rem                                             venue's scenes, no tenant menu
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
if defined VORTEX_NO_PAUSE set "VA_AUTO=1"
set "VA_RC=0"

rem --- Arguments (any order) -------------------------------------------
set "VA_TENANT="
:va_args
if "%~1"=="" goto :va_args_done
if /i "%~1"=="--no-pause" (
  set "VA_AUTO=1"
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
if defined VA_AUTO set "VA_HOLD="

set "VA_REPO=%~dp0.."
for %%I in ("%VA_REPO%") do set "VA_REPO=%%~fI"
set "VA_OUT=%VA_REPO%\deploy\admin"
set "VA_LOG=%VA_REPO%\deploy\admin-build.log"

rem  Publish server, single place: the version listing (GET /admin/versions) and
rem  the upload at the end (POST /admin/upload) must never point at different
rem  machines.
set "VA_SERVER=http://159.100.20.26:8091"

rem --- Tenant menu -----------------------------------------------------
rem  Asked only when --tenant was not given. Automation mode skips it; that
rem  mode then fails at the version prompt anyway.
if not defined VA_TENANT if not defined VA_AUTO (
  call "%~dp0lib\select-tenant.bat" "%VA_REPO%"
  if errorlevel 1 (
    set "VA_RC=2"
    goto :son
  )
  echo.
)

echo === VortexArena : admin ^(Windows^) build ===
echo   Proje : %VA_REPO%
if defined VA_TENANT (
  echo   Tenant: !VA_TENANT!
) else (
  echo   Tenant: hepsi
)
echo.

rem --- Tenant folder ---------------------------------------------------
rem  Checked BEFORE the version prompt: a typo must not cost the operator the
rem  whole prompt sequence and a long build that then aborts in Unity.
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

rem  What is already published, so the number is not chosen blind. A failure here
rem  is never fatal: the helper always exits 0 and the prompt follows anyway.
set "VA_LIST=%~dp0lib\list-server-versions.ps1"
if exist "!VA_LIST!" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "!VA_LIST!" -Url "%VA_SERVER%/admin/versions" -Label "admin arsivinin"
) else (
  echo   [UYARI] Surum listeleyici yok, sunucudaki surumler basilamayacak:
  echo           "!VA_LIST!"
)
echo.

echo   Girilen numara hem cikti klasoru ve zip adina ^(admin_v^<numara^>^) hem
echo   bundleVersion'a girer; uygulama adi surumsuz kalir, boylece operator
echo   ayarlari ^(PlayerPrefs^) surum degisince sifirlanmaz.
echo   AYNI commit'in admin ve oyuncu build'ine AYNI numara verilir.

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
rem  -buildVersion as an int, so the published version would be 7 while the
rem  folder and zip were named admin_v007 - two names for one build.
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
set "VA_DIR=%VA_OUT%\admin_v!VA_VER!"
set "VA_ZIP=%VA_OUT%\admin_v!VA_VER!.zip"
set "VA_EXE=!VA_DIR!\VortexArena.exe"

rem  Overwriting an existing version is legitimate (rebuild of the same
rem  number), so this only warns.
if exist "!VA_DIR!" (
  echo   [UYARI] Bu surum zaten var, yeniden uretilecek: !VA_DIR!
)
echo   Hedef : !VA_DIR!
echo   Zip   : !VA_ZIP!
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

rem --- 3) Prepare the output folder ------------------------------------
rem  deploy\admin\ is never wiped: older admin_v*\ folders and zips must stay so
rem  an earlier version can still be published or tested. Only THIS version's
rem  folder and zip are removed.
if not exist "%VA_OUT%" mkdir "%VA_OUT%" 2>nul
if exist "!VA_DIR!" (
  echo   Temizlik: ayni surumun eski ciktisi siliniyor...
  rmdir /s /q "!VA_DIR!"
)
if exist "!VA_DIR!" (
  echo [HATA] Eski cikti silinemedi: "!VA_DIR!"
  echo        Admin oyunu acik olabilir ^(VortexArena.exe^) - kapatip tekrar deneyin.
  set "VA_RC=1"
  goto :son
)
del /q "!VA_ZIP!" 2>nul
if exist "!VA_ZIP!" (
  echo [HATA] Eski zip silinemedi: "!VA_ZIP!"
  set "VA_RC=1"
  goto :son
)
mkdir "!VA_DIR!" 2>nul

rem --- 4) Build --------------------------------------------------------
rem  -nographics IS NOT USED: shader variant compilation may need a graphics
rem  device and could silently produce a broken build.
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
    -Unity "!VA_UNITY!" -Project "%VA_REPO%" -OutDir "!VA_DIR!" -Log "%VA_LOG%" ^
    -Method VortexArena.Core.Editor.PlayerBuildTool.BuildWindowsAdmin ^
    -UnityBuildTarget Win64 -BuildVersion !VA_VER! !VA_TARG_PS!
  set "VA_RC=!ERRORLEVEL!"
) else (
  echo   [UYARI] Izleyici yok, ilerleme basilamayacak: "%VA_WATCH%"
  "!VA_UNITY!" -batchmode -quit ^
    -projectPath "%VA_REPO%" ^
    -buildTarget Win64 ^
    -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildWindowsAdmin ^
    -buildOutput "!VA_DIR!" ^
    -buildVersion !VA_VER! ^
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

if not exist "%VA_EXE%" (
  echo [HATA] Build 0 dondu ama exe yok: "%VA_EXE%"
  echo        Log: %VA_LOG%
  set "VA_RC=1"
  goto :son
)

echo.
echo === TAMAM ===
echo   %VA_EXE%
powershell -NoProfile -Command "$s=(Get-ChildItem '%VA_DIR%' -Recurse -File | Measure-Object -Sum Length).Sum/1MB; Write-Host ('  Boyut: {0:N1} MB' -f $s)"

rem --- 5) Pack the zip -------------------------------------------------
rem  The launcher downloads and extracts admin_v<N>.zip, so the archive is the
rem  real artifact: without it the build is not publishable and the script fails.
set "VA_PACK=%~dp0lib\zip-build-folder.ps1"
echo.
if not exist "%VA_PACK%" (
  echo [HATA] Zip yardimcisi yok: "%VA_PACK%"
  set "VA_RC=1"
  goto :son
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%VA_PACK%" -SourceDir "%VA_DIR%" -Destination "%VA_ZIP%"
if errorlevel 1 (
  echo [HATA] Zip uretilemedi: "%VA_ZIP%"
  set "VA_RC=1"
  goto :son
)
if not exist "%VA_ZIP%" (
  echo [HATA] Zip yardimcisi 0 dondu ama dosya yok: "%VA_ZIP%"
  set "VA_RC=1"
  goto :son
)

rem --- 6) Upload to the server -----------------------------------------
rem  The zip is sent to the publish endpoint on the server (updater_uploader
rem  script); the launcher's Yonetim page downloads it from the same machine.
rem  An upload failure does NOT fail the build: the zip exists and can be
rem  published by hand.
rem  -T streams the file instead of -d loading it into memory (the archive can
rem  pass 2 GB); "Expect:" is cleared because the uploader does not answer
rem  100-continue and curl would wait a second for nothing.
set "VA_UPLOAD_URL=%VA_SERVER%/admin/upload?v=%VA_VER%"
echo.
echo   Sunucuya yukleniyor: %VA_UPLOAD_URL%
curl -f --connect-timeout 10 -X POST -H "Expect:" -T "%VA_ZIP%" "%VA_UPLOAD_URL%"
if errorlevel 1 (
  echo.
  echo   [UYARI] Sunucuya yukleme BASARISIZ - launcher bu surumu goremez.
  echo           Sunucuda updater_uploader_main.py calisiyor mu? 8091 disaridan erisilir mi?
  echo           Elle tekrar denemek icin:
  echo             curl -f -X POST -H "Expect:" -T "%VA_ZIP%" "%VA_UPLOAD_URL%"
) else (
  echo.
  echo   Sunucuya yuklendi - launcher'in Yonetim sayfasi bu surumu gorur.
)
if defined VA_TENANT (
  echo   [UYARI] Sunucudaki surum listesi isletme bilgisi TASIMAZ: bu surumu her
  echo           operator PC'si gorur. Hangi surum hangi isletmeye ait, not edin.
)
echo.
echo   Yerel denemek icin "%VA_DIR%" klasorunu
echo   "%VA_REPO%\deploy\launcher\admin_versions\admin_v%VA_VER%" altina kopyalayin;
echo   sahada surum launcher'in Yonetim sayfasindan indirilir.

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
