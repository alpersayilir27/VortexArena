@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  docs-serve.bat
rem  VortexArena dokumantasyonunu tarayicida acar (Quartz statik site).
rem
rem    http://localhost:1111
rem
rem  Icerik BU repodadir: Docs\ klasoru. Site motoru (Quartz + node_modules)
rem  repo DISINDA durur: ..\vortexarena-docs-site. Ikisi bir junction ile
rem  baglidir (site\content -> repo\Docs), yani Docs\ altinda bir .md
rem  dosyasini kaydettigin anda tarayici kendini yeniler.
rem
rem  Ilk kurulum (yeni bilgisayarda bir kez):
rem    scripts\docs-setup.bat
rem
rem  Kullanim:
rem    docs-serve.bat             cift tiklanabilir; Ctrl+C ile durdurulur
rem    docs-serve.bat --port 2222 baska port
rem
rem  NOT: betik-ici degiskenler VA_ onekli (scripts\README.md).
rem =====================================================================

set "VA_SITE=%~dp0..\vortexarena-docs-site"
set "VA_PORT=1111"
set "VA_WSPORT=3131"

rem --- Argumanlar ------------------------------------------------------
:parse
if "%~1"=="" goto parsed
if /I "%~1"=="--port" (
  set "VA_PORT=%~2"
  shift & shift & goto parse
)
if /I "%~1"=="--wsport" (
  set "VA_WSPORT=%~2"
  shift & shift & goto parse
)
echo [UYARI] Bilinmeyen arguman: %~1
shift & goto parse
:parsed

title VortexArena Docs - Quartz (port %VA_PORT%)

echo.
echo ========================================================
echo   VortexArena Dokumantasyon Sunucusu
echo   http://localhost:%VA_PORT%
echo ========================================================
echo.

rem --- Kurulum var mi --------------------------------------------------
if not exist "%VA_SITE%\package.json" (
  echo [HATA] Site motoru bulunamadi:
  echo        %VA_SITE%
  echo.
  echo        Once kurulumu calistir:  scripts\docs-setup.bat
  echo.
  pause
  exit /b 1
)

if not exist "%VA_SITE%\node_modules" (
  echo [HATA] node_modules yok - bagimliliklar kurulmamis.
  echo        Once kurulumu calistir:  scripts\docs-setup.bat
  echo.
  pause
  exit /b 1
)

if not exist "%VA_SITE%\content\index.md" (
  echo [HATA] content junction'i repo Docs klasorune bakmiyor.
  echo        Beklenen: %VA_SITE%\content  ->  %~dp0Docs
  echo        Once kurulumu calistir:  scripts\docs-setup.bat
  echo.
  pause
  exit /b 1
)

echo Icerik : %~dp0Docs
echo Motor  : %VA_SITE%
echo.
echo Durdurmak icin Ctrl+C. Docs\ altinda bir .md kaydedince site
echo kendini yeniler - tarayiciyi elle yenilemen gerekmez.
echo.

pushd "%VA_SITE%"
call npx quartz build --serve --port %VA_PORT% --wsPort %VA_WSPORT%
set "VA_RESULT=%ERRORLEVEL%"
popd

echo.
if not "%VA_RESULT%"=="0" (
  echo [HATA] Quartz %VA_RESULT% koduyla cikti.
) else (
  echo Sunucu durduruldu.
)
pause
exit /b %VA_RESULT%
