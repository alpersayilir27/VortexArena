@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-player-apk.bat
rem  Unity oyuncu (Meta Quest 3/3S) build'ini alir -> deploy\player\game.apk
rem  ve kurulum betigini yanina kopyalar (deploy\player\install_game.bat).
rem
rem  Admin build'inden farki YALNIZ platformdur: sahne listesi, rol ve
rem  sunucu adresi cozumu aynidir. Oyuncu build'i calisma aninda player
rem  rolune duser ve sunucuyu UDP beacon ile kendi bulur - IP gomulmez.
rem
rem  ONEMLI 1: batch-mode Unity, editor ayni projeyi acikken proje kilidine
rem  takilabilir. Betik bunu KONTROL ETMEZ (bilincli, bkz. deploy-admin-game.bat).
rem  Build kilitte takilirsa elle iptal edip tekrar baslatin.
rem
rem  ONEMLI 2: HEDEF PLATFORM BU BETIKTE SABITTIR: -buildTarget Android.
rem  Aktif platformdan turetilmez - projede hangi platform acik kalmis olursa
rem  olsun bu betik APK alir. Bayrak Unity'ye ACILISTA verilir: platformu
rem  -executeMethod'un icinden cevirmek domain reload tetikler ve calisan
rem  metot yarida kalir.
rem  Aktif platform Android degilse gecis acilista olur ve o kosu TAM REIMPORT
rem  demektir (texture'lar ASTC'ye yeniden sikistirilir) - 20-40 dk surebilir;
rem  sonrakiler hizlidir. Platform build sonunda geri ALINMAZ (geri almak
rem  ikinci bir tam reimport daha olurdu) ve gerekmez: admin betigi de kendi
rem  hedefini sabit geciyor.
rem
rem  Unity yolu: UNITY_EXE ortam degiskeni > Hub'daki proje surumu.
rem
rem  Kullanim:
rem    deploy-player-apk.bat             cift tiklanabilir; sonda bekler
rem    deploy-player-apk.bat --no-pause  otomasyon; beklemeden cikar
rem    (VORTEX_NO_PAUSE=1 ortam degiskeni de beklemeyi kapatir)
rem
rem  NOT: betik-ici degiskenler VA_ onekli - cocuk sureclere (Unity ->
rem  IL2CPP -> Gradle) miras kaliyor ve kisa genel adlar derleme zincirini
rem  kiriyor. Yeni degisken eklerken VA_ onekini koru.
rem =====================================================================

rem --- Cift tiklamada pencere kapanmasin -------------------------------
set "VA_HOLD="
set "VA_CL=%cmdcmdline%"
if not "!VA_CL:%~nx0=!"=="!VA_CL!" set "VA_HOLD=1"
if defined VORTEX_NO_PAUSE set "VA_HOLD="
if /i "%~1"=="--no-pause" set "VA_HOLD="
set "VA_RC=0"

set "VA_REPO=%~dp0.."
for %%I in ("%VA_REPO%") do set "VA_REPO=%%~fI"
set "VA_OUT=%VA_REPO%\deploy\player"
set "VA_LOG=%VA_REPO%\deploy\player-build.log"

echo === VortexArena : oyuncu ^(Meta Quest / Android^) build ===
echo   Proje : %VA_REPO%
echo   Hedef : %VA_OUT%\game.apk
echo.

rem --- 1) Proje Unity surumu -------------------------------------------
set "VA_UVER="
for /f "tokens=2" %%v in ('findstr /b "m_EditorVersion:" "%VA_REPO%\ProjectSettings\ProjectVersion.txt"') do set "VA_UVER=%%v"
if not defined VA_UVER (
  echo [HATA] ProjectVersion.txt okunamadi:
  echo        "%VA_REPO%\ProjectSettings\ProjectVersion.txt"
  set "VA_RC=1"
  goto :son
)
echo   Surum : %VA_UVER%

rem --- 2) Unity.exe bul ------------------------------------------------
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

rem --- 3) Android modulu kurulu mu -------------------------------------
rem  Modul yoksa Unity platformu Android'e ceviremez ve build sessizce
rem  Windows olarak devam edip .exe uretirdi. Erken ve acikca soyleyelim.
for %%I in ("!VA_UNITY!") do set "VA_UDIR=%%~dpI"
if not exist "!VA_UDIR!Data\PlaybackEngines\AndroidPlayer" (
  echo [HATA] Unity Android Build Support kurulu degil:
  echo        "!VA_UDIR!Data\PlaybackEngines\AndroidPlayer" yok.
  echo        Unity Hub ^> Installs ^> %VA_UVER% ^> Add modules ile
  echo        "Android Build Support" + SDK/NDK + OpenJDK kurun.
  set "VA_RC=1"
  goto :son
)

rem --- 4) Cikti klasorunu temizle --------------------------------------
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

rem --- 5) Build --------------------------------------------------------
rem  -nographics KULLANILMIYOR: shader varyant derlemesi grafik cihazi
rem  isteyebilir ve sessizce bozuk cikti uretebilir.
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
    -UnityBuildTarget Android
  set "VA_RC=!ERRORLEVEL!"
) else (
  echo   [UYARI] Izleyici yok, ilerleme basilamayacak: "%VA_WATCH%"
  "!VA_UNITY!" -batchmode -quit ^
    -projectPath "%VA_REPO%" ^
    -buildTarget Android ^
    -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildQuestPlayer ^
    -buildOutput "%VA_OUT%" ^
    -logFile "%VA_LOG%"
  set "VA_RC=!ERRORLEVEL!"
)

if not "%VA_RC%"=="0" (
  echo.
  echo [HATA] Build basarisiz ^(exit %VA_RC%^). Log'un son satirlari:
  powershell -NoProfile -Command "if (Test-Path '%VA_LOG%') { Get-Content '%VA_LOG%' -Tail 30 }"
  echo.
  echo        Log'da proje kilidi ^("Multiple Unity instances" / lock^) geciyorsa
  echo        arka planda Unity.exe yasiyor demektir - kapatip tekrar deneyin.
  goto :son
)

if not exist "%VA_OUT%\game.apk" (
  echo [HATA] Build 0 dondu ama apk yok: "%VA_OUT%\game.apk"
  echo        Log: %VA_LOG%
  set "VA_RC=1"
  goto :son
)

rem --- 6) Kurulum betigini apk'nin yanina koy --------------------------
rem  install_game.bat, game.apk'yi KENDI klasorunde arar; ikisi birlikte
rem  tasinsin diye kopyalanir (repo kokundeki kopya tek dogruluk kaynagi).
if exist "%VA_REPO%\install_game.bat" (
  copy /y "%VA_REPO%\install_game.bat" "%VA_OUT%\install_game.bat" >nul
) else (
  echo   [UYARI] install_game.bat repo kokunde yok, kopyalanamadi.
)

echo.
echo === TAMAM ===
echo   %VA_OUT%\game.apk
powershell -NoProfile -Command "$s=(Get-Item '%VA_OUT%\game.apk').Length/1MB; Write-Host ('  Boyut: {0:N1} MB' -f $s)"
echo.
echo   Gozluge kurmak icin: gozlugu USB ile bagla, gelistirici modu acik olsun,
echo   sonra "%VA_OUT%\install_game.bat" dosyasini calistir.
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
