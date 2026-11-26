@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title WarpCord - Durum

powershell -NoProfile -ExecutionPolicy Bypass -NoLogo -File "%~dp0status.ps1" %*
echo.
pause
