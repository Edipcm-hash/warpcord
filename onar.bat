@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title WarpCord - onarim

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Yonetici haklari isteniyor / requesting administrator rights...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -ArgumentList '%*'"
    exit /b
)

if not exist "%~dp0tools\warpheal.ps1" (
    echo tools\warpheal.ps1 bulunamadi. Tum dosyalari ayni klasore cikartin.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -NoLogo -File "%~dp0tools\warpheal.ps1" %*
set "RC=%errorlevel%"

echo.
if "%RC%"=="0" (
    echo Sorun yok, tunel saglikli.
) else if "%RC%"=="1" (
    echo Sorun bulundu ve onarildi.
) else (
    echo Onarim tamamlanamadi ^(kod %RC%^). WarpCord.bat ile yeniden kurun.
)
echo.
echo Log: C:\ProgramData\DiscordTunnel\heal.log
echo.
pause
exit /b %RC%
