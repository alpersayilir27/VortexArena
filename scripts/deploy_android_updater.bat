@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  deploy_android_updater.bat
rem  updater\ (Kotlin/Gradle) projesini derler -> deploy\updater\VortexUpdater.apk
rem  ve kurulum betigini yanina kopyalar (deploy\updater\install_updater.bat).
rem
rem  Android Studio GEREKMEZ: JDK, SDK ve Gradle Unity'nin Android modulunden
rem  alinir. Gradle wrapper repoda YOK (wrapper jar'i binary, commit edilmiyor);
rem  bu yuzden Gradle dogrudan launcher jar'i uzerinden calistirilir.
rem
rem  Kullanim:
rem    deploy_android_updater.bat             cift tiklanabilir; sonda bekler
rem    deploy_android_updater.bat --no-pause  otomasyon; beklemeden cikar
rem    (VORTEX_NO_PAUSE=1 ortam degiskeni de beklemeyi kapatir)
rem
rem  NOT: betik-ici degiskenler VA_ onekli - cocuk sureclere (Gradle -> AGP)
rem  miras kaliyor ve kisa genel adlar derleme zincirini kirabiliyor.
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
set "VA_PROJ=%VA_REPO%\updater"
set "VA_OUT=%VA_REPO%\deploy\updater"

echo === VortexArena : Android OTA updater build ===
echo   Proje : %VA_PROJ%
echo   Hedef : %VA_OUT%\VortexUpdater.apk
echo.

if not exist "%VA_PROJ%\settings.gradle.kts" (
  echo [HATA] Updater projesi bulunamadi: "%VA_PROJ%"
  set "VA_RC=1"
  goto :son
)

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

rem --- 2) Unity Android modulu -----------------------------------------
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
for %%I in ("!VA_UNITY!") do set "VA_UDIR=%%~dpI"
set "VA_ANDROID=!VA_UDIR!Data\PlaybackEngines\AndroidPlayer"
if not exist "!VA_ANDROID!" (
  echo [HATA] Unity Android Build Support kurulu degil:
  echo        "!VA_ANDROID!" yok.
  echo        Unity Hub ^> Installs ^> %VA_UVER% ^> Add modules ile
  echo        "Android Build Support" + SDK/NDK + OpenJDK kurun.
  set "VA_RC=1"
  goto :son
)

rem --- 3) JDK ----------------------------------------------------------
set "JAVA_HOME=!VA_ANDROID!\OpenJDK"
if not exist "!JAVA_HOME!\bin\java.exe" (
  echo [HATA] Unity ile gelen OpenJDK yok:
  echo        "!JAVA_HOME!\bin\java.exe"
  echo        Unity Hub ^> Add modules ile "OpenJDK" bilesenini kurun.
  set "VA_RC=1"
  goto :son
)
echo   JDK   : !JAVA_HOME!

rem --- 4) Gradle -------------------------------------------------------
rem  Once Unity'nin gomulu Gradle'i (launcher jar dogrudan calistirilir),
rem  o yoksa PATH'teki gradle komutu.
set "VA_GRADLE_JAR="
for %%J in ("!VA_ANDROID!\Tools\gradle\lib\gradle-launcher-*.jar") do set "VA_GRADLE_JAR=%%~fJ"
set "VA_GRADLE_PATH="
if not defined VA_GRADLE_JAR (
  where gradle >nul 2>nul
  if not errorlevel 1 set "VA_GRADLE_PATH=1"
)
if not defined VA_GRADLE_JAR if not defined VA_GRADLE_PATH (
  echo [HATA] Gradle bulunamadi. Iki secenekten biri gerekir:
  echo          1^) Unity Hub ^> Installs ^> %VA_UVER% ^> Add modules ile
  echo             "Android Build Support" ic bilesenlerini kurun
  echo             ^(gomulu Gradle: "!VA_ANDROID!\Tools\gradle"^)
  echo          2^) Gradle 8.4+ kurup PATH'e ekleyin ^(gradle --version^)
  set "VA_RC=1"
  goto :son
)
if defined VA_GRADLE_JAR (
  echo   Gradle: !VA_GRADLE_JAR!
) else (
  echo   Gradle: PATH
)

rem --- 5) Android SDK --------------------------------------------------
rem  Unity'nin SDK'si KULLANILMAZ: Program Files altinda yazma korumali ve
rem  AGP'nin istedigi build-tools surumunu tasimiyor - AGP eksik bileseni
rem  oraya kuramayip "licences have not been accepted" hatasiyla duser.
rem  Bunun yerine kullanici profilinde yazilabilir bir SDK koku tutulur;
rem  lisans dosyasi sayesinde AGP eksik bilesenleri (platform + build-tools)
rem  ilk kosuda kendisi indirir.
set "ANDROID_HOME=%LOCALAPPDATA%\VortexUpdaterSdk"
if not exist "%ANDROID_HOME%\licenses" mkdir "%ANDROID_HOME%\licenses"
>"%ANDROID_HOME%\licenses\android-sdk-license" echo 8933bad161af4178b1185d1a37fbf41ea5269c55
>>"%ANDROID_HOME%\licenses\android-sdk-license" echo 24333f8a63b6825ea9c5514f83c2829b004d1fee
if not exist "%ANDROID_HOME%\platforms" (
  echo   [!] SDK bilesenleri ilk kosuda AGP tarafindan indirilecek ^(internet gerekir^).
)
set "ANDROID_SDK_ROOT=%ANDROID_HOME%"
echo   SDK   : %ANDROID_HOME%

rem --- 6) Derleme ------------------------------------------------------
rem  --no-daemon bilincli: arkada yetim bir Gradle daemon'i kalmasin
rem  (repo kurali - yetim surec kaynak ve port tutar).
echo.
echo   Derleme basliyor ^(:app:assembleDebug^)...
echo.
if defined VA_GRADLE_JAR (
  "!JAVA_HOME!\bin\java.exe" -classpath "!VA_GRADLE_JAR!" org.gradle.launcher.GradleMain ^
    -p "%VA_PROJ%" --no-daemon :app:assembleDebug
  set "VA_RC=!ERRORLEVEL!"
) else (
  call gradle -p "%VA_PROJ%" --no-daemon :app:assembleDebug
  set "VA_RC=!ERRORLEVEL!"
)

if not "!VA_RC!"=="0" (
  echo.
  echo [HATA] Gradle derlemesi basarisiz ^(cikis %VA_RC%^).
  echo        Ilk kosuda bagimliliklar internetten iner - ag baglantisini kontrol edin.
  goto :son
)

set "VA_APK=%VA_PROJ%\app\build\outputs\apk\debug\app-debug.apk"
if not exist "%VA_APK%" (
  echo [HATA] Gradle 0 dondu ama apk yok: "%VA_APK%"
  set "VA_RC=1"
  goto :son
)

rem --- 7) Cikti --------------------------------------------------------
if not exist "%VA_OUT%" mkdir "%VA_OUT%" 2>nul
copy /y "%VA_APK%" "%VA_OUT%\VortexUpdater.apk" >nul
if not exist "%VA_OUT%\VortexUpdater.apk" (
  echo [HATA] Cikti kopyalanamadi: "%VA_OUT%\VortexUpdater.apk"
  set "VA_RC=1"
  goto :son
)

rem  Kurulum betigi apk'nin yanina konur ki klasor tek basina tasinabilsin
rem  (repo kokundeki kopya tek dogruluk kaynagi).
if exist "%VA_REPO%\install_updater.bat" (
  copy /y "%VA_REPO%\install_updater.bat" "%VA_OUT%\install_updater.bat" >nul
) else (
  echo   [UYARI] install_updater.bat repo kokunde yok, kopyalanamadi.
)

echo.
echo === TAMAM ===
echo   %VA_OUT%\VortexUpdater.apk
powershell -NoProfile -Command "$s=(Get-Item '%VA_OUT%\VortexUpdater.apk').Length/1MB; Write-Host ('  Boyut: {0:N1} MB' -f $s)"
echo.
echo   Gozluge kurmak icin: gozlugu USB ile bagla, gelistirici modu acik olsun,
echo   sonra "%VA_OUT%\install_updater.bat" dosyasini calistir.
echo   Updater bir kez kurulduktan sonra oyun APK'si USB'siz guncellenir.

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
