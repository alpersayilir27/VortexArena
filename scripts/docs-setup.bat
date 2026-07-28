@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  docs-setup.bat
rem  Dokumantasyon sitesinin motorunu (Quartz) kurar - YENI BILGISAYARDA
rem  BIR KEZ calistirilir. Gunluk kullanim: repo kokundeki docs-serve.bat
rem
rem  Ne yapar:
rem    1. ..\vortexarena-docs-site klasorune Quartz'i klonlar
rem    2. npm bagimliliklarini kurar
rem    3. site\content -> repo\Docs junction'ini kurar
rem    4. VortexArena'ya ozel quartz.config.yaml'i yazar
rem
rem  Gereksinimler: Node 22+, npm, git, internet (yalnizca kurulum icin -
rem  sonrasinda site tamamen offline calisir).
rem
rem  Motor neden repo DISINDA: node_modules ~365 paket. Repoya girseydi
rem  Unity onu import etmeye calisirdi ve git gecmisi sisirdi.
rem
rem  NOT: betik-ici degiskenler VA_ onekli (scripts\README.md).
rem =====================================================================

rem  %%~fi = yolu normalize eder ("...\scripts\.." yerine "D:\games\vortexarena")
for %%i in ("%~dp0..") do set "VA_REPO=%%~fi"
for %%i in ("%~dp0..\..\vortexarena-docs-site") do set "VA_SITE=%%~fi"
set "VA_QUARTZ_URL=https://github.com/jackyzha0/quartz.git"

title VortexArena Docs - Kurulum

echo.
echo ========================================================
echo   VortexArena Dokumantasyon Sitesi - Kurulum
echo ========================================================
echo.

rem --- On kosullar -----------------------------------------------------
where node >nul 2>&1
if errorlevel 1 (
  echo [HATA] node bulunamadi. Node 22+ kur: https://nodejs.org
  pause & exit /b 1
)
where git >nul 2>&1
if errorlevel 1 (
  echo [HATA] git bulunamadi.
  pause & exit /b 1
)

for /f "tokens=*" %%v in ('node --version') do set "VA_NODE=%%v"
echo Node : %VA_NODE%
echo Repo : %VA_REPO%
echo Site : %VA_SITE%
echo.

rem --- 1) Klonla -------------------------------------------------------
if exist "%VA_SITE%\package.json" (
  echo [1/4] Quartz zaten var - klonlama atlandi.
) else (
  echo [1/4] Quartz klonlaniyor...
  git clone --depth 1 "%VA_QUARTZ_URL%" "%VA_SITE%"
  if errorlevel 1 (
    echo [HATA] Klonlama basarisiz.
    pause & exit /b 1
  )
)

rem --- 2) Bagimliliklar ------------------------------------------------
echo [2/4] npm bagimliliklari kuruluyor (ilk seferde ~1 dk)...
pushd "%VA_SITE%"
call npm install --no-audit --no-fund
set "VA_RESULT=%ERRORLEVEL%"
popd
if not "%VA_RESULT%"=="0" (
  echo [HATA] npm install basarisiz ^(%VA_RESULT%^).
  pause & exit /b 1
)

rem --- 3) content junction ---------------------------------------------
echo [3/4] content junction kuruluyor...
if exist "%VA_SITE%\content\index.md" (
  echo       zaten bagli - atlandi.
) else (
  if exist "%VA_SITE%\content" rmdir /S /Q "%VA_SITE%\content"
  mklink /J "%VA_SITE%\content" "%VA_REPO%\Docs" >nul
  if errorlevel 1 (
    echo [HATA] Junction kurulamadi. Yonetici olarak dene ya da elle:
    echo        mklink /J "%VA_SITE%\content" "%VA_REPO%\Docs"
    pause & exit /b 1
  )
)

rem --- 4) config -------------------------------------------------------
echo [4/4] quartz.config.yaml yaziliyor...
if exist "%VA_SITE%\quartz.config.yaml" (
  echo       zaten var - korundu ^(elle duzenlenmis olabilir^).
) else (
  copy /Y "%VA_SITE%\quartz.config.default.yaml" "%VA_SITE%\quartz.config.yaml" >nul
  powershell -NoProfile -Command ^
    "$p='%VA_SITE%\quartz.config.yaml';" ^
    "$c=Get-Content -Raw -Encoding UTF8 $p;" ^
    "$c=$c -replace 'pageTitle: Quartz 5','pageTitle: VortexArena - Gelistirici Dokumani';" ^
    "$c=$c -replace 'locale: en-US','locale: tr-TR';" ^
    "$c=$c -replace 'baseUrl: quartz.jzhao.xyz','baseUrl: localhost:1111';" ^
    "$c=$c -replace 'provider: plausible','provider: null';" ^
    "[IO.File]::WriteAllText($p,$c,(New-Object Text.UTF8Encoding $false))"
)

echo.
echo ========================================================
echo   Kurulum tamam.
echo   Simdi repo kokundeki  docs-serve.bat  dosyasini calistir.
echo ========================================================
echo.
pause
exit /b 0
