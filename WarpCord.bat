@echo off
setlocal EnableExtensions
chcp 65001 >nul
cd /d "%~dp0"
title WarpCord - Discord Only Tunnel

rem --- v3: drop the mark-of-the-web first, so RemoteSigned / ASR rules and the
rem     "not digitally signed" wall cannot stop the scripts we ship in this zip.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem -LiteralPath '%~dp0' -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object { try { Unblock-File -LiteralPath $_.FullName -ErrorAction SilentlyContinue; if (Get-Item -LiteralPath $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue) { Remove-Item -LiteralPath $_.FullName -Stream Zone.Identifier -Force -ErrorAction SilentlyContinue } } catch { } }" >nul 2>&1

if not exist "%~dp0app\install.ps1" (
    echo app\install.ps1 bulunamadi. Tum dosyalari ayni klasore cikartin.
    pause
    exit /b 1
)

rem --- v3.1: Smart App Control blocks unsigned binaries with no reputation, so
rem     WarpCord.exe and sing-box.exe cannot start there. Warn with the exact
rem     fix before a 40 second install that is guaranteed to fail. The prompt is
rem     asked once, in the unelevated parent; the elevated re-run stays quiet.
set "SAC_MODE=prompt"
net session >nul 2>&1
if not errorlevel 1 set "SAC_MODE=quiet"
if not defined WARPCORD_SAC_OK (
    call "%~dp0tools\sac-check.bat" %SAC_MODE%
    if errorlevel 4 exit /b 4
    set "WARPCORD_SAC_OK=1"
)

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Yonetici haklari isteniyor / requesting administrator rights...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs -ArgumentList '%*'"
    exit /b
)

rem Double-click = 5 seconds of cat. Command-line runs skip it.
if "%~1"=="" (
    if exist "%~dp0tools\splash.bat" call "%~dp0tools\splash.bat"
)

rem --- v3: a GPO can force AllSigned/Restricted, which refuses to load .ps1 files
rem     even with -ExecutionPolicy Bypass. Then run the same script in memory.
set "GPO="
for /f "delims=" %%p in ('powershell -NoProfile -ExecutionPolicy Bypass -Command "(Get-ExecutionPolicy -Scope MachinePolicy)" 2^>nul') do set "GPO=%%p"
if /i "%GPO%"=="AllSigned" goto :inmemory
if /i "%GPO%"=="Restricted" goto :inmemory

powershell -NoProfile -ExecutionPolicy Bypass -NoLogo -File "%~dp0app\install.ps1" %*
set "RC=%errorlevel%"
goto :done

:inmemory
echo Politika betik dosyalarini engelliyor; bellek ici calistirma kullaniliyor...
powershell -NoProfile -ExecutionPolicy Bypass -NoLogo -Command "$s = Get-Content -Raw '%~dp0app\install.ps1'; & ([scriptblock]::Create($s)) -RepoRoot '%~dp0' %*"
set "RC=%errorlevel%"

:done
echo.
if "%RC%"=="0" (
    echo WarpCord calisiyor. Discord uygulamasini kapatip yeniden acin.
) else (
    echo Kurulum tamamlanmadi ^(kod %RC%^).
    echo Log  : C:\ProgramData\DiscordTunnel\install.log
    echo Tani : tani.bat
)
echo.
pause
exit /b %RC%
