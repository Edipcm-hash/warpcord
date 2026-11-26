@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title WarpCord - Register WARP device

echo Tek seferlik WARP cihaz kaydi (Python gerekmez)...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -NoLogo -File "%~dp0tools\warp-register.ps1" %*
echo.
pause
