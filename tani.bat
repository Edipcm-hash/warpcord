@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title WarpCord - Tani

if not exist "%~dp0tools\tani.ps1" (
    echo tools\tani.ps1 bulunamadi.
    pause
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -NoLogo -File "%~dp0tools\tani.ps1" %*
set "RC=%errorlevel%"

echo.
echo Rapor: %%TEMP%%\WarpCord-tani-*.log
echo Paket:  tani.bat -Bundle  (tanilama raporunu zip olarak olusturur)
echo.
pause
exit /b %RC%
