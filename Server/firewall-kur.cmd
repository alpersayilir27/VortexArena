@echo off
rem =====================================================================
rem  VortexArena - LAN ag kurulumu ve Windows Firewall kurallari
rem  Sag tik -> "Yonetici olarak calistir" ile BIR KEZ calistirin.
rem
rem  NEREDE CALISTIRILIR?
rem   - Sunucu PC'de (zorunlu).
rem   - Admin console calistiran DIGER PC'lerde de calistirin: beacon bir
rem     BROADCAST paketidir, stateful UDP eslesmesine takilmaz; istemcide
rem     inbound izin yoksa Windows onu sessizce dusurur ve sunucu listede
rem     gorunmez. Istemcide dinleyen yoktur, fazladan kurallar zararsizdir.
rem
rem  Yaptiklari:
rem   1) Ag profilini Private yapar (Public profilde Defender gelen
rem      broadcast'i ve cogu inbound'u keser).
rem   2) Windows'un otomatik ekledigi ENGELLE (Block) kurallarini siler.
rem   3) UDP 47820 (beacon) + TCP 47821 (WS kontrol) + UDP 47822 (state)
rem      icin IZIN kurallari ekler (profil: Private + Domain).
rem      Exe bulunursa ayrica programa ozel izin kurali eklenir.
rem   4) Teshis: aktif adaptorler, IPv4 adresleri, dinlenen portlar.
rem =====================================================================
net session >nul 2>&1
if errorlevel 1 (
  echo Bu betik YONETICI olarak calistirilmali.
  echo Sag tik ^> "Yonetici olarak calistir" ile tekrar deneyin.
  pause
  exit /b 1
)

set "PS=powershell -NoProfile -ExecutionPolicy Bypass -Command"

echo.
echo === [1/5] Ag profili -^> Private =====================================
%PS% "Get-NetConnectionProfile | ForEach-Object { if ($_.NetworkCategory -eq 'Public') { Set-NetConnectionProfile -InterfaceIndex $_.InterfaceIndex -NetworkCategory Private; Write-Host ('  [DEGISTI] ' + $_.Name + ' : Public -> Private') } else { Write-Host ('  [ok]      ' + $_.Name + ' : ' + $_.NetworkCategory) } }"

echo.
echo === [2/5] Eski / otomatik ENGELLE kurallarini temizle ===============
rem Windows'un uygulamaya ozel otomatik Block kurallari:
netsh advfirewall firewall delete rule name="VortexArena.Server.App" >nul 2>&1
netsh advfirewall firewall delete rule name="VortexArena.PoseBot" >nul 2>&1
rem Kendi kurallarimiz (idempotent olsun diye once silinir):
netsh advfirewall firewall delete rule name="VortexArena Beacon (UDP 47820)" >nul 2>&1
netsh advfirewall firewall delete rule name="VortexArena Control (TCP 47821)" >nul 2>&1
netsh advfirewall firewall delete rule name="VortexArena State (UDP 47822)" >nul 2>&1
netsh advfirewall firewall delete rule name="VortexArena Server (Program)" >nul 2>&1
rem Eski surumlerden kalan isimler:
netsh advfirewall firewall delete rule name="VortexArena Sunucu (WS 47821)" >nul 2>&1
netsh advfirewall firewall delete rule name="VortexArena Sunucu (Beacon 47820)" >nul 2>&1
netsh advfirewall firewall delete rule name="VortexArena Sunucu (State 47822)" >nul 2>&1
echo   Temizlendi.

echo.
echo === [3/5] IZIN kurallari (Private + Domain) =========================
netsh advfirewall firewall add rule name="VortexArena Beacon (UDP 47820)" dir=in action=allow protocol=UDP localport=47820 profile=private,domain
netsh advfirewall firewall add rule name="VortexArena Control (TCP 47821)" dir=in action=allow protocol=TCP localport=47821 profile=private,domain
netsh advfirewall firewall add rule name="VortexArena State (UDP 47822)" dir=in action=allow protocol=UDP localport=47822 profile=private,domain

rem Programa ozel izin: Windows'un exe icin yeniden Block kurali uretmesini onler.
set "EXE=%~dp0VortexArena.Server.App\bin\Release\net10.0\VortexArena.Server.App.exe"
if not exist "%EXE%" set "EXE=%~dp0VortexArena.Server.App\bin\Debug\net10.0\VortexArena.Server.App.exe"
if exist "%EXE%" (
  netsh advfirewall firewall add rule name="VortexArena Server (Program)" dir=in action=allow program="%EXE%" enable=yes profile=private,domain
  echo   Program kurali: "%EXE%"
) else (
  echo   [bilgi] Sunucu exe bulunamadi, programa ozel kural eklenmedi.
  echo           Once derleyin: dotnet build Server/VortexArena.Server.sln -c Release
)
rem Outbound Windows'ta varsayilan olarak zaten serbesttir - kural gerekmez.

echo.
echo === [4/5] Teshis: adaptorler ve adresler ============================
%PS% "$a=@(Get-NetAdapter | Where-Object { $_.Status -eq 'Up' }); Write-Host ('  Aktif adaptor sayisi: ' + $a.Count); $a | Format-Table -AutoSize Name, InterfaceDescription, LinkSpeed; if ($a.Count -gt 1) { Write-Host '  UYARI: birden fazla aktif adaptor var (Ethernet + WiFi, VPN, Hyper-V, VMware, WSL).'; Write-Host '         Beacon yanlis arayuzden yayilabilir ve gozlukler sunucuyu bulamaz.'; Write-Host '         Kullanmadiklarinizi kapatin:  Disable-NetAdapter -Name ""<Ad>""' }"
rem 169.254.* (APIPA) ve 127.0.0.1 elenir - ikisi de gozluklerin baglanamayacagi adreslerdir.
%PS% "$ips=@(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' }); if ($ips.Count -eq 0) { Write-Host '  UYARI: kullanilabilir IPv4 adresi YOK (yalniz 169.254.* APIPA). Ag baglantisi kurulmamis.' } else { $ips | Sort-Object InterfaceAlias | Format-Table -AutoSize InterfaceAlias, IPAddress, PrefixOrigin; if ($ips.Count -gt 1) { Write-Host '  UYARI: birden fazla kullanilabilir IPv4 var - gozluklerin bagli oldugu agdakini secin.' }; if ($ips | Where-Object { $_.PrefixOrigin -eq 'Dhcp' }) { Write-Host '  Not: adres DHCP ile alinmis. Router''da DHCP rezervasyonu yapin, yoksa IP degisebilir.' } }"
echo   Yukaridaki IPv4 adresi, gozluklerin lobide elle girecegi adrestir (ornek 192.168.1.10:47821).
echo   Tum cihazlar AYNI subnet'te olmali (ayni router, ayni 192.168.x blogu).

echo.
echo === [5/5] Dinlenen portlar =========================================
netstat -ano | findstr 4782
if errorlevel 1 (
  echo   [bilgi] Sunucu su an calismiyor - dinlenen port yok. Normaldir.
)
echo.
echo   Sunucuyu baslattiktan sonra bu komutu tekrar calistirin:
echo       netstat -ano ^| findstr 4782
echo   0.0.0.0:47821 GORMELISINIZ. 127.0.0.1:47821 gorurseniz sunucu yalniz
echo   loopback'e bind olmustur ve disaridan hicbir cihaz baglanamaz.

echo.
echo =====================================================================
echo  Kurallar guncellendi.
echo.
echo  ELLE YAPILACAK (betik yapamaz):
echo   * IP'yi sabitleyin - router'da DHCP rezervasyonu (tercih edilen) veya
echo     statik IP. IP degisirse StreamingAssets/arena.json ve gozluklerdeki
echo     kayitli adres bozulur.
echo   * AP ayarlari: 5 GHz, sabit kanal, client/AP isolation KAPALI.
echo   * Admin console calistiran diger PC'lerde de bu betigi calistirin.
echo =====================================================================
pause
exit /b 0
