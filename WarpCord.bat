@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title WarpCord - Discord Only Tunnel

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Yonetici haklari isteniyor / requesting administrator rights...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -ArgumentList '%*'"
    exit /b
)

if not exist "%~dp0install.ps1" (
    echo install.ps1 bulunamadi. Tum dosyalari ayni klasore cikartin.
    pause
    exit /b 1
)

rem Double-click = 3 seconds of cat. Command-line runs skip it.
if "%~1"=="" (
    if exist "%~dp0tools\splash.bat" call "%~dp0tools\splash.bat"
)

powershell -NoProfile -ExecutionPolicy Bypass -NoLogo -File "%~dp0install.ps1" %*
set "RC=%errorlevel%"

echo.
if "%RC%"=="0" (
    echo WarpCord calisiyor. Discord uygulamasini kapatip yeniden acin.
) else (
    echo Kurulum tamamlanmadi ^(kod %RC%^).
    echo Log: C:\ProgramData\DiscordTunnel\install.log
)
echo.
pause
exit /b %RC%
