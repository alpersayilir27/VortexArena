@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy-server.bat
rem  VortexArena.Server.App -> dotnet publish -> deploy\server\server_v<N>\
rem  ve yanina deploy\server\server_v<N>.zip paketler.
rem
rem  SURUMLU BUILD: betik her zaman surum numarasi sorar. Numara cikti
rem  klasorunun ve zip'in adina girer; launcher surumleri bu adla indirir ve
rem  yan yana tutar. Surumsuz sunucu build'i YOKTUR.
rem  AYNI commit'ten alinan sunucu, admin ve oyuncu build'ine AYNI numara
rem  verilir: protokol surumu ve maps.json uyusmak zorundadir.
rem
rem  Kendine yeten (self-contained) TEK klasor uretir: isletme PC'sine
rem  .NET kurmak gerekmez.
rem
rem  Surum klasoru ICERIGI: exe + publish ciktisi, config\maps.json
rem  (ZORUNLU), config\server.json (tohum), firewall-kur.cmd.
rem  devices.json KOPYALANMAZ - cihaz adlari kuruluma ozeldir, sunucu uretir.
rem  config\server.json surum klasorunde yalnizca TOHUMDUR: isletmenin
rem  kullandigi kopya veri klasorunde yasar (sunucunun --data-dir argumani).
rem
rem  maps.json Unity'den export edilir
rem  (Tools > VortexArena > Server > Export Server Config) - burada URETILMEZ,
rem  yalnizca kopyalanir. Yoksa betik BASARISIZ olur: maps.json surumle birlikte
rem  dagilir ve ayni numarali oyuncu build'iyle ayni harita tablosunu tasir.
rem  NOT: weapons.json diye bir dosya YOKTUR - sunucuda silah tablosu yok,
rem  hasari istemci hesaplar (Docs/ArenaNet-Protokol.md 10.3).
rem
rem  Surum sorulmadan once sunucuda yayinlanmis sunucu surumleri listelenir
rem  (lib\list-server-versions.ps1 -> GET /server/versions). Sunucuya
rem  ulasilamamasi yalnizca uyaridir, soru yine gelir.
rem
rem  Zip yazildiktan sonra yayin ucuna POST edilir
rem  (updater_uploader\updater_uploader_main.py); uc kapaliysa yalnizca uyari
rem  basilir, build basarili sayilir.
rem
rem  Kullanim:
rem    deploy-server.bat             cift tiklanabilir; surum sorar, sonda bekler
rem    deploy-server.bat --no-pause  beklemeden cikar - AMA bu kipte surum
rem                                  sorulamaz, betik exit 2 ile durur
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
set "VA_AUTO="
set "VA_CL=%cmdcmdline%"
if not "!VA_CL:%~nx0=!"=="!VA_CL!" set "VA_HOLD=1"
if defined VORTEX_NO_PAUSE set "VA_AUTO=1"
if /i "%~1"=="--no-pause" set "VA_AUTO=1"
if defined VA_AUTO set "VA_HOLD="
set "VA_RC=0"

set "VA_REPO=%~dp0.."
for %%I in ("%VA_REPO%") do set "VA_REPO=%%~fI"
set "VA_OUT=%VA_REPO%\deploy\server"
set "VA_PROJ=%VA_REPO%\Server\VortexArena.Server.App\VortexArena.Server.App.csproj"

rem  Yayin sunucusu, tek yer: surum listesi (GET /server/versions) ile sondaki
rem  yukleme (POST /server/upload) asla baska makineyi gostermemeli.
set "VA_SERVER=http://159.100.20.26:8091"

echo === VortexArena : sunucu publish ===
echo   Proje : %VA_PROJ%
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

rem --- maps.json surum sorulmadan ONCE kontrol edilir ------------------
rem  Zorunlu dosya: eksikse uzun bir publish'in sonunda patlamak yerine burada
rem  durulur, operator Unity'den export edip tekrar calistirir.
if not exist "%VA_REPO%\Server\config\maps.json" (
  echo [HATA] maps.json yok: "%VA_REPO%\Server\config\maps.json"
  echo        Unity'de uretin: Tools ^> VortexArena ^> Server ^> Export Server Config
  echo        maps.json surumle birlikte dagilir, onsuz surum paketlenemez.
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

rem --- 0) Surum (interaktif) -------------------------------------------
rem  Otomasyon kipinde soracak konsol yok: set /p aninda bos doner ve soru
rem  sonsuz donerdi. Onun yerine basta durulur.
if defined VA_AUTO (
  echo [HATA] Otomasyon kipinde ^(--no-pause / VORTEX_NO_PAUSE^) surum sorulamaz.
  echo        Bu betik surumu yalnizca interaktif olarak sorar; beklemesiz
  echo        kipte calistirmayin.
  set "VA_RC=2"
  goto :son
)

rem  Yayinda ne var, numara korden secilmesin. Buradaki hata asla olumcul
rem  degildir: yardimci her zaman 0 doner ve soru yine gelir.
set "VA_LIST=%~dp0lib\list-server-versions.ps1"
if exist "!VA_LIST!" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "!VA_LIST!" -Url "%VA_SERVER%/server/versions" -Label "sunucu arsivinin"
) else (
  echo   [UYARI] Surum listeleyici yok, sunucudaki surumler basilamayacak:
  echo           "!VA_LIST!"
)
echo.

echo   Girilen numara cikti klasoru ve zip adina girer ^(server_v^<numara^>^).
echo   AYNI commit'in sunucu, admin ve oyuncu build'ine AYNI numara verilir.

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

rem  Basindaki sifirlar atilir ("007" -> "7"): klasor, zip ve yayindaki surum
rem  numarasi tek bir ad olmali.
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
set "VA_DIR=%VA_OUT%\server_v!VA_VER!"
set "VA_ZIP=%VA_OUT%\server_v!VA_VER!.zip"
set "VA_EXE=!VA_DIR!\VortexArena.Server.App.exe"

rem  Ayni numarayi yeniden uretmek mesru (rebuild), bu yuzden yalnizca uyarir.
if exist "!VA_DIR!" (
  echo   [UYARI] Bu surum zaten var, yeniden uretilecek: !VA_DIR!
)
echo   Hedef : !VA_DIR!
echo   Zip   : !VA_ZIP!
echo.

rem --- 1) Cikti klasorunu hazirla --------------------------------------
rem  deploy\server\ ASLA bostan silinmez: eski server_v*\ klasorleri ve zip'leri
rem  kalmali (onceki surum hala yayinlanabilir ya da denenebilir olmali), ustelik
rem  launcher yerlesiminde veri klasoru de burada durur. Yalnizca BU surumun
rem  klasoru ve zip'i silinir.
if not exist "%VA_OUT%" mkdir "%VA_OUT%" 2>nul
if exist "!VA_DIR!" (
  echo   Temizlik: ayni surumun eski ciktisi siliniyor...
  rmdir /s /q "!VA_DIR!"
)
if exist "!VA_DIR!" (
  echo [HATA] Eski cikti silinemedi: "!VA_DIR!"
  echo        Klasor bir programda acik olabilir - kapatip tekrar deneyin.
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

rem --- 2) Publish ------------------------------------------------------
echo   Publish basliyor...
dotnet publish "%VA_PROJ%" -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=false ^
  -o "!VA_DIR!"
if errorlevel 1 (
  echo.
  echo [HATA] dotnet publish basarisiz.
  echo        Kurulu SDK'lari gormek icin: dotnet --list-sdks
  set "VA_RC=1"
  goto :son
)

if not exist "!VA_EXE!" (
  echo [HATA] Publish 0 dondu ama exe yok: "!VA_EXE!"
  set "VA_RC=1"
  goto :son
)

rem --- 3) config\ yanina kopyala ---------------------------------------
rem  Sunucu --data-dir verilmediginde exe yanindan baslayip yukari dogru
rem  config\server.json arar; maps.json'i her zaman exe yanindaki config'ten
rem  okur. Ikisi de surum klasorune girer.
echo.
echo   config\ kopyalaniyor...
mkdir "!VA_DIR!\config" 2>nul
for %%F in (maps.json server.json) do (
  if exist "%VA_REPO%\Server\config\%%F" (
    copy /y "%VA_REPO%\Server\config\%%F" "!VA_DIR!\config\%%F" >nul
    echo     + %%F
  ) else (
    rem  Gecikmeli genisletme "!" karakterini yiyor - uyari isareti "-" yazilir.
    echo     - %%F YOK
  )
)
if not exist "!VA_DIR!\config\maps.json" (
  echo [HATA] maps.json surum klasorune kopyalanamadi.
  set "VA_RC=1"
  goto :son
)
rem devices.json KOPYALANMAZ: cihaz adlari kuruluma ozeldir, sunucu uretir.

rem --- 4) firewall betigini yanina al ----------------------------------
if exist "%VA_REPO%\Server\firewall-kur.cmd" (
  copy /y "%VA_REPO%\Server\firewall-kur.cmd" "!VA_DIR!\firewall-kur.cmd" >nul
  echo   + firewall-kur.cmd
) else (
  echo   [UYARI] firewall-kur.cmd yok: "%VA_REPO%\Server\firewall-kur.cmd"
  echo           Isletme PC'sinde guvenlik duvari kurallari elle eklenmek zorunda kalir.
)

echo.
echo === TAMAM ===
echo   !VA_EXE!
powershell -NoProfile -Command "$s=(Get-ChildItem '!VA_DIR!' -Recurse -File | Measure-Object -Sum Length).Sum/1MB; Write-Host ('  Boyut: {0:N1} MB (self-contained)' -f $s)"

rem --- 5) Zip paketle --------------------------------------------------
rem  Launcher server_v<N>.zip'i indirip aciyor, yani gercek urun arsivdir:
rem  onsuz surum yayinlanamaz ve betik basarisiz olur.
set "VA_PACK=%~dp0lib\zip-build-folder.ps1"
echo.
if not exist "%VA_PACK%" (
  echo [HATA] Zip yardimcisi yok: "%VA_PACK%"
  set "VA_RC=1"
  goto :son
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%VA_PACK%" -SourceDir "!VA_DIR!" -Destination "!VA_ZIP!"
if errorlevel 1 (
  echo [HATA] Zip uretilemedi: "!VA_ZIP!"
  set "VA_RC=1"
  goto :son
)
if not exist "!VA_ZIP!" (
  echo [HATA] Zip yardimcisi 0 dondu ama dosya yok: "!VA_ZIP!"
  set "VA_RC=1"
  goto :son
)

rem --- 6) Sunucuya yukle -----------------------------------------------
rem  Zip, yayin ucuna gonderilir (updater_uploader betigi); launcher'in Sunucu
rem  sayfasi ayni makineden indirir. Yukleme hatasi build'i basarisiz YAPMAZ:
rem  zip yerinde durur, elle yayinlanabilir.
rem  -T dosyayi akitir, -d gibi bellege almaz; "Expect:" temizlenir cunku
rem  yukleyici 100-continue cevabi vermez ve curl bosuna bir saniye bekler.
set "VA_UPLOAD_URL=%VA_SERVER%/server/upload?v=%VA_VER%"
echo.
echo   Sunucuya yukleniyor: %VA_UPLOAD_URL%
curl -f --connect-timeout 10 -X POST -H "Expect:" -T "!VA_ZIP!" "%VA_UPLOAD_URL%"
if errorlevel 1 (
  echo.
  echo   [UYARI] Sunucuya yukleme BASARISIZ - launcher bu surumu goremez.
  echo           Sunucuda updater_uploader_main.py calisiyor mu? 8091 disaridan erisilir mi?
  echo           Elle tekrar denemek icin:
  echo             curl -f -X POST -H "Expect:" -T "!VA_ZIP!" "%VA_UPLOAD_URL%"
) else (
  echo.
  echo   Sunucuya yuklendi - launcher'in Sunucu sayfasi bu surumu gorur.
)
echo.
echo   Yerel denemek icin "!VA_DIR!" klasorunu
echo   "%VA_REPO%\deploy\launcher\server_versions\server_v%VA_VER%" altina kopyalayin;
echo   sahada surum launcher'in Sunucu sayfasindan indirilir.
echo   Isletme PC'sinde bir kez: firewall-kur.cmd -^> yonetici olarak calistir
echo   ^(her yeni surumden sonra tekrar; launcher'daki dugme de ayni isi yapar^).

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
