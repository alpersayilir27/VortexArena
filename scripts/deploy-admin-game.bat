@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-admin-game.bat
rem  Unity admin (Windows) build'ini alir -> deploy\admin\VortexArena.exe
rem
rem  Rol ve sunucu adresi build'e GOMULMEZ: masaustu build'i calisma aninda
rem  admin rolune duser ve adresi launcher'in gectigi --server-ip
rem  argumanindan okur (AppBoot). Launcher bu exe'yi baslatir.
rem
rem  ONEMLI: batch-mode Unity, editor ayni projeyi acikken proje kilidine
rem  takilabilir. Betik bunu KONTROL ETMEZ (bilincli): editor kapatildiktan
rem  sonra bile AI motoru gibi alt sureclerin Unity.exe'si arka planda
rem  yasayabiliyor ve tasklist kontrolu yanlis alarm veriyordu. Build kilitte
rem  takilirsa elle iptal edip tekrar baslatin.
rem
rem  Unity yolu: UNITY_EXE ortam degiskeni > Hub'daki proje surumu.
rem
rem  Kullanim:
rem    deploy-admin-game.bat             cift tiklanabilir; sonda bekler
rem    deploy-admin-game.bat --no-pause  otomasyon; beklemeden cikar
rem    (VORTEX_NO_PAUSE=1 ortam degiskeni de beklemeyi kapatir)
rem
rem  NOT: betik-ici degiskenler VA_ onekli. Sebep: bu degiskenler cocuk
rem  sureclere (Unity -> IL2CPP -> MSVC) miras kaliyor ve kisa genel adlar
rem  derleme zincirini kiriyor (ornek: "RC" -> CMake/MSVC onu resource
rem  compiler saniyor). Yeni degisken eklerken VA_ onekini koru.
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
set "VA_OUT=%VA_REPO%\deploy\admin"
set "VA_LOG=%VA_REPO%\deploy\admin-build.log"

echo === VortexArena : admin (Windows) build ===
echo   Proje : %VA_REPO%
echo   Hedef : %VA_OUT%
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

rem --- 3) Cikti klasorunu temizle --------------------------------------
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
rem  -nographics KULLANILMIYOR: player build'inde shader varyant derlemesi
rem  grafik cihazi isteyebilir ve sessizce bozuk cikti uretebilir.
rem  HEDEF PLATFORM BU BETIKTE SABITTIR: -buildTarget Win64. Aktif platformdan
rem  turetilmez - projede hangi platform acik kalmis olursa olsun bu betik
rem  Windows build'i alir. Bayrak Unity'ye ACILISTA verilir: platformu
rem  -executeMethod'un icinden cevirmek domain reload tetikler ve calisan
rem  metot yarida kalir.
rem  Aktif platform zaten Windows ise bayrak etkisizdir; degilse gecis
rem  acilista olur ve o kosu tam reimport yuzunden uzun surer (texture'lar
rem  DXT'ye yeniden sikistirilir) - sonrakiler hizlidir.
echo.
echo   Build basliyor (hedef: Windows; platform degisiyorsa uzun surebilir)...
echo   Asagidaki durum satiri canli guncellenir; hicbir sey ilerlemiyorsa
echo   izleyici uyari basar (editor/arka plan Unity.exe proje kilidini tutuyor
echo   olabilir - Ctrl+C ile iptal edip surecleri kapattiktan sonra tekrar deneyin).
echo   Log   : %VA_LOG%
rem  Eski log'u sil: Unity hic baslayamazsa hata dalinda BAYAT log basilir,
rem  yanlis teshise goturur. Silinemiyorsa dosyayi tutan bir Unity sureci
rem  hala yasiyor demektir - engellemiyoruz, yalnizca uyariyoruz.
del /q "%VA_LOG%" 2>nul
if exist "%VA_LOG%" (
  echo   [UYARI] Onceki log silinemedi - bir Unity sureci hala tutuyor.
  echo           Build takilirsa o sureci kapatip tekrar deneyin;
  echo           asagida basilan log satirlari da bayat olabilir.
)
rem  Unity'yi DOGRUDAN degil, izleyici uzerinden calistiriyoruz: batch-mode Unity
rem  konsola hicbir sey yazmadigi icin "takildi mi ilerliyor mu" sorusu baska
rem  turlu cevaplanamiyordu. lib\watch-unity-build.ps1 ayni komut satirini kurar,
rem  log'u canli okur, asama + yuzde + hareketsizlik uyarisi basar ve Unity'nin
rem  cikis kodunu aynen dondurur. Izleyici yoksa eski davranisa duseriz.
set "VA_WATCH=%~dp0lib\watch-unity-build.ps1"
if exist "%VA_WATCH%" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%VA_WATCH%" ^
    -Unity "!VA_UNITY!" -Project "%VA_REPO%" -OutDir "%VA_OUT%" -Log "%VA_LOG%" ^
    -UnityBuildTarget Win64
  set "VA_RC=!ERRORLEVEL!"
) else (
  echo   [UYARI] Izleyici yok, ilerleme basilamayacak: "%VA_WATCH%"
  "!VA_UNITY!" -batchmode -quit ^
    -projectPath "%VA_REPO%" ^
    -buildTarget Win64 ^
    -executeMethod VortexArena.Core.Editor.PlayerBuildTool.BuildWindowsAdmin ^
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
