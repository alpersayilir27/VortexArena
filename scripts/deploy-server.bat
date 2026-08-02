@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-server.bat
rem  VortexArena.Server.App -> dotnet publish -> deploy\server\
rem
rem  Kendine yeten (self-contained) TEK klasor uretir: isletme PC'sine
rem  .NET kurmak gerekmez. config\ dosyalari yaninda gider.
rem
rem  maps.json Unity'den export edilir
rem  (Tools > VortexArena > Server > Export Server Config) - burada URETILMEZ,
rem  yalnizca kopyalanir. Yoksa uyarilir.
rem  NOT: weapons.json diye bir dosya YOKTUR - sunucuda silah tablosu yok,
rem  hasari istemci hesaplar (Docs/ArenaNet-Protokol.md 10.3).
rem
rem  Kullanim:
rem    deploy-server.bat             cift tiklanabilir; sonda bekler
rem    deploy-server.bat --no-pause  otomasyon; beklemeden cikar
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
set "VA_OUT=%VA_REPO%\deploy\server"
set "VA_PROJ=%VA_REPO%\Server\VortexArena.Server.App\VortexArena.Server.App.csproj"

echo === VortexArena : sunucu publish ===
echo   Proje : %VA_PROJ%
echo   Hedef : %VA_OUT%
echo.

if not exist "%VA_PROJ%" (
  echo [HATA] Sunucu projesi bulunamadi: "%VA_PROJ%"
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

rem --- Sunucu calisiyorsa exe kilitli olur, publish yarida kalir --------
tasklist /fi "imagename eq VortexArena.Server.App.exe" 2>nul | find /i "VortexArena.Server.App.exe" >nul
if not errorlevel 1 (
  echo [HATA] VortexArena.Server.App.exe calisiyor - cikti klasoru kilitli.
  echo        Sunucuyu kapatip tekrar deneyin.
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
  echo        Klasor bir programda acik olabilir - kapatip tekrar deneyin.
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

if not exist "%VA_OUT%\VortexArena.Server.App.exe" (
  echo [HATA] Publish 0 dondu ama exe yok: "%VA_OUT%\VortexArena.Server.App.exe"
  set "VA_RC=1"
  goto :son
)

rem --- config\ yanina kopyala ------------------------------------------
rem Sunucu, exe yanindan baslayip 6 seviye yukari config\server.json arar
rem (Program.ResolveConfigDir); publish klasoru repo disina tasinabilsin
rem diye config'i exe'nin YANINA koyuyoruz.
echo.
echo   config\ kopyalaniyor...
mkdir "%VA_OUT%\config" 2>nul
for %%F in (server.json maps.json) do (
  if exist "%VA_REPO%\Server\config\%%F" (
    copy /y "%VA_REPO%\Server\config\%%F" "%VA_OUT%\config\%%F" >nul
    echo     + %%F
  ) else (
    echo     ! %%F YOK
    if /i "%%F"=="maps.json" (
      echo       ^-^> Unity'de: Tools ^> VortexArena ^> Server ^> Export Server Config
      echo          Yoksa start_match harita dogrulamasi devre disi kalir.
    )
  )
)
rem devices.json KOPYALANMAZ: cihaz adlari kuruluma ozeldir, sunucu uretir.

rem --- firewall betigini yanina al -------------------------------------
if exist "%VA_REPO%\Server\firewall-kur.cmd" (
  copy /y "%VA_REPO%\Server\firewall-kur.cmd" "%VA_OUT%\firewall-kur.cmd" >nul
  echo   + firewall-kur.cmd
)

echo.
echo === TAMAM ===
echo   %VA_OUT%\VortexArena.Server.App.exe
powershell -NoProfile -Command "$s=(Get-ChildItem '%VA_OUT%' -Recurse -File | Measure-Object -Sum Length).Sum/1MB; Write-Host ('  Boyut: {0:N1} MB (self-contained)' -f $s)"
echo.
echo   Isletme PC'sinde bir kez: firewall-kur.cmd -^> yonetici olarak calistir.

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
