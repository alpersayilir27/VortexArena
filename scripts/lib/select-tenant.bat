@echo off
rem =====================================================================
rem  select-tenant.bat <repo root>
rem  Interactive tenant (venue) picker shared by deploy-player-apk.bat and
rem  deploy-admin-game.bat. Lists Assets\Arenas\Venues\* numbered;
rem  "A" = every venue, a number = that venue.
rem
rem  Sets VA_TENANT in the CALLER's environment (empty = every venue), so
rem  there is deliberately no setlocal here. Needs the caller's
rem  EnableDelayedExpansion. Exit code 2 = no valid choice.
rem =====================================================================
set "VA_VENUES=%~1\Assets\Arenas\Venues"
set "VA_TENANT="
set "VA_TN=0"

echo   Hangi tenant icin build alinsin?
echo     A^) Hepsi
for /f "delims=" %%D in ('dir /b /ad /on "%VA_VENUES%" 2^>nul') do (
  set /a VA_TN+=1
  set "VA_TEN_!VA_TN!=%%D"
  echo     !VA_TN!^) %%D
)
if "!VA_TN!"=="0" (
  echo [HATA] Isletme klasoru bulunamadi: "%VA_VENUES%"
  exit /b 2
)

set "VA_TTRY=0"
:va_ask_tenant
set /a VA_TTRY+=1
if !VA_TTRY! GTR 5 (
  echo [HATA] Gecerli tenant secimi alinamadi ^(5 deneme^).
  exit /b 2
)
set "VA_IN="
set /p "VA_IN=  Secim (A veya 1-!VA_TN!): "
if defined VA_IN set "VA_IN=!VA_IN: =!"
if not defined VA_IN (
  echo   [UYARI] Secim bos birakilamaz.
  goto :va_ask_tenant
)
if /i "!VA_IN!"=="A" exit /b 0
rem  Digits only before the lookup: anything else would be pasted into a
rem  variable name.
echo(!VA_IN!|findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 (
  echo   [UYARI] A ya da listedeki bir numara girin.
  goto :va_ask_tenant
)
if not defined VA_TEN_!VA_IN! (
  echo   [UYARI] Listede !VA_IN! numarasi yok.
  goto :va_ask_tenant
)
set "VA_TENANT=!VA_TEN_%VA_IN%!"
exit /b 0
