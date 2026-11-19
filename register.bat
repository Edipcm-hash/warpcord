@echo off
chcp 65001 >nul
setlocal
title WarpCord - Register WARP device
set "PY=py -3"
%PY% --version >nul 2>&1
if errorlevel 1 set "PY=python"
%PY% "%~dp0tools\warp-register.py" --out "%~dp0config.json" --install-dir "C:\ProgramData\DiscordTunnel"
echo.
pause
