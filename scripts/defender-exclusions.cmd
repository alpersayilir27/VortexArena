@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  defender-exclusions.cmd
rem  Windows Defender dislamalarini kurar (gelistirici makinesi).
rem  Sag tik -> "Yonetici olarak calistir" ile BIR KEZ calistirin.
rem
rem  Neden: IL2CPP build'i on binlerce .cpp/.obj uretir, Library/ surekli
rem  yazilip okunur. Defender'in gercek zamanli korumasi her dosya
rem  acilisinda araya girer ve paralel derlemenin onunde kuyruk olusturur.
rem
rem  Butun is defender-exclusions.ps1 icinde; bu dosya yalniz yonetici
rem  kontrolu + cift tiklanabilirlik icindir.
rem
rem  Kullanim:
rem    defender-exclusions.cmd                cift tiklanabilir; sonda bekler
rem    defender-exclusions.cmd -List          yalniz listeler (yazmaz)
rem    defender-exclusions.cmd -Remove        ayni listeyi geri alir
rem    defender-exclusions.cmd -ExtraPath D:\games\digerproje
rem    defender-exclusions.cmd --no-pause     otomasyon; beklemeden cikar
rem    (VORTEX_NO_PAUSE=1 ortam degiskeni de beklemeyi kapatir)
rem
rem  NOT: betik-ici degiskenler VA_ onekli (cocuk sureclere miras kaliyor,
rem  kisa genel adlar derleme zincirini kiriyor).
rem =====================================================================

set "VA_HOLD="
set "VA_CL=%cmdcmdline%"
if not "!VA_CL:%~nx0=!"=="!VA_CL!" set "VA_HOLD=1"
if defined VORTEX_NO_PAUSE set "VA_HOLD="
set "VA_RC=0"
rem  DIKKAT: "shift" %0'i da kaydiriyor - betigin klasorunu dongu BASLAMADAN al.
set "VA_DIR=%~dp0"

rem --- Argumanlari ayikla: --no-pause bize ait, geri kalani .ps1'e gider ---
rem  (dongu sayesinde --no-pause listenin herhangi bir yerinde olabilir)
set "VA_ARGS="
:argdongu
if "%~1"=="" goto :argbitti
if /i "%~1"=="--no-pause" (
  set "VA_HOLD="
) else (
  set "VA_ARGS=!VA_ARGS! %1"
)
shift
goto :argdongu
:argbitti

set "VA_PS1=%VA_DIR%defender-exclusions.ps1"
if not exist "%VA_PS1%" (
  echo [HATA] Bulunamadi: "%VA_PS1%"
  set "VA_RC=1"
  goto :son
)

rem --- Yonetici kontrolu ------------------------------------------------
rem  -List de yonetici ister: Defender, yetkisiz oturumda listeyi vermez,
rem  yerine "N/A: Must be an administrator to view exclusions" dondurur.
net session >nul 2>&1
if errorlevel 1 (
  echo [HATA] Bu betik YONETICI olarak calistirilmali.
  echo        Sag tik ^> "Yonetici olarak calistir" ile tekrar deneyin.
  set "VA_RC=1"
  goto :son
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%VA_PS1%" !VA_ARGS!
set "VA_RC=!errorlevel!"

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
