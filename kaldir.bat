@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title WarpCord - Kaldir

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Yonetici haklari isteniyor / requesting administrator rights...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -ArgumentList '%*'"
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -NoLogo -File "%~dp0uninstall.ps1" %*
echo.
pause
