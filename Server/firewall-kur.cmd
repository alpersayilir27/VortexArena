@echo off
rem =====================================================================
rem  VortexArena Sunucu - Windows Firewall kurallari
rem  Sag tik -> "Yonetici olarak calistir" ile BIR KEZ calistirin.
rem
rem  Yaptiklari:
rem   1) Uygulama ilk acildiginda Windows'un otomatik ekledigi ENGELLE
rem      (Block) kurallarini siler (bunlar gozluklerin baglanmasini engeller).
rem   2) TCP 47821 (WebSocket kontrol) + UDP 47820 (beacon) + UDP 47822
rem      (state/poz) icin IZIN kurallari ekler.
rem =====================================================================
net session >nul 2>&1
if errorlevel 1 (
  echo Bu betik YONETICI olarak calistirilmali.
  echo Sag tik ^> "Yonetici olarak calistir" ile tekrar deneyin.
  pause
  exit /b 1
)

rem 1) Uygulamaya ozel otomatik ENGELLE kurallarini temizle.
netsh advfirewall firewall delete rule name="VortexArena.Server.App" >nul 2>&1

rem 2) Eski port kurallarini temizle (idempotent).
netsh advfirewall firewall delete rule name="VortexArena Sunucu (WS 47821)" >nul 2>&1
netsh advfirewall firewall delete rule name="VortexArena Sunucu (Beacon 47820)" >nul 2>&1
netsh advfirewall firewall delete rule name="VortexArena Sunucu (State 47822)" >nul 2>&1

rem 3) IZIN kurallari (ozel + genel profil; loopback Editor testi icin genel de acik).
netsh advfirewall firewall add rule name="VortexArena Sunucu (WS 47821)" dir=in action=allow protocol=TCP localport=47821 profile=private,public
netsh advfirewall firewall add rule name="VortexArena Sunucu (Beacon 47820)" dir=in action=allow protocol=UDP localport=47820 profile=private,public
netsh advfirewall firewall add rule name="VortexArena Sunucu (State 47822)" dir=in action=allow protocol=UDP localport=47822 profile=private,public

echo.
echo Kurallar guncellendi (App engelle kurallari silindi, port izinleri eklendi).
echo Bu pencereyi kapatabilirsiniz.
pause
exit /b 0
