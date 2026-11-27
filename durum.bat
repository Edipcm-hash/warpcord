@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title WarpCord - Durum

powershell -NoProfile -ExecutionPolicy Bypass -NoLogo -File "%~dp0app\status.ps1" %*
echo.
pause
