@echo off
setlocal EnableDelayedExpansion
rem =====================================================================
rem  quest-logcat.bat
rem  Streams the headset's Unity log (adb logcat, Unity tag only) to this
rem  window. "Logcat" in the test cards means THIS output - not the Unity
rem  Editor console and not the server console: the player app runs on the
rem  headset, so its Debug.Log lines exist only here.
rem
rem  Usage:
rem    scripts\quest-logcat.bat            all Unity lines
rem    scripts\quest-logcat.bat Regroup    only lines containing "Regroup"
rem                                        (case-insensitive substring)
rem  Ctrl+C stops it. The headset must be connected over USB (or
rem  "adb connect <headset-ip>:5555" for Wi-Fi) with USB debugging approved -
rem  install_game.bat walks through that approval.
rem
rem  adb lookup: PATH first, then Unity's own Android SDK (the Android Build
rem  Support module ships platform-tools), located via UNITY_EXE or the Hub's
rem  default install folder for the project's editor version.
rem =====================================================================

set "VA_ADB="
where adb >nul 2>nul
if not errorlevel 1 set "VA_ADB=adb"

if not defined VA_ADB (
    for %%P in (
        "%UNITY_EXE%\..\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
        "%ProgramFiles%\Unity\Hub\Editor\6000.3.20f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
    ) do if not defined VA_ADB if exist "%%~fP" set "VA_ADB=%%~fP"
)

if not defined VA_ADB (
    echo [HATA] adb bulunamadi. Android platform-tools PATH'e ekli olmali ya da Unity'nin
    echo        Android Build Support modulu kurulu olmali ^(UNITY_EXE ile editor yolu verilebilir^).
    pause
    exit /b 1
)

echo Bagli cihazlar:
"!VA_ADB!" devices
echo.

set "VA_FILTER=%~1"
if defined VA_FILTER (
    echo Unity logu akiyor, filtre: "!VA_FILTER!"  ^(Ctrl+C ile dur^)
) else (
    echo Unity logu akiyor  ^(Ctrl+C ile dur^)
)
echo.

rem  -c drops the boot backlog so the first lines belong to this session.
"!VA_ADB!" logcat -c >nul 2>nul

if defined VA_FILTER (
    "!VA_ADB!" logcat -v time -s Unity:V | findstr /i /c:"!VA_FILTER!"
) else (
    "!VA_ADB!" logcat -v time -s Unity:V
)
