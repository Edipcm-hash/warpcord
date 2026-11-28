@echo off
rem ============================================================================
rem  WarpCord - Smart App Control preflight
rem
rem  Smart App Control blocks unsigned binaries that have no reputation, and it
rem  has no per-app allowlist. On such a machine WarpCord.exe cannot start at
rem  all and sing-box.exe (downloaded unsigned) cannot run either. Print the
rem  exact fix before a 40 second install that is guaranteed to fail.
rem
rem  Usage:  sac-check.bat [prompt|quiet]
rem  Exit :  0 = continue, 4 = the user asked not to continue
rem ============================================================================
setlocal EnableExtensions
chcp 65001 >nul

set "MODE=%~1"
if /i not "%MODE%"=="prompt" set "MODE=quiet"

set "STATE=%WARPCORD_SAC_FORCE%"
if not defined STATE (
    for /f "delims=" %%s in ('powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0sac-state.ps1" 2^>nul') do set "STATE=%%s"
)
if not defined STATE set "STATE=-1"

if "%STATE%"=="2" (
    echo  [!] Akilli Uygulama Denetimi degerlendirme modunda: imzasiz bilesenler engellenebilir.
    echo      Ayrintili: README.tr.md ^-^> "Akilli Uygulama Denetimi"
    exit /b 0
)
if not "%STATE%"=="1" exit /b 0

echo.
echo  ================================================================
echo   [!] Akilli Uygulama Denetimi (Smart App Control) acik
echo.
echo   Bu makinede imzasiz uygulamalar engellenir. WarpCord.exe
echo   baslamaz, sing-box.exe de calismaz; kurulum yarida kalir.
echo.
echo   Kapatmak icin:
echo     Ayarlar ^> Gizlilik ve guvenlik ^> Windows Guvenligi ^>
echo     Uygulama ve tarayici denetimi ^> Akilli Uygulama Denetimi
echo     ^> Kapali
echo.
echo   Not: kapatmak geri alinamaz; Windows sifirlanmadan
echo   yeniden acilamaz. Kalici cozum imzali surumdur.
echo   Ayrintili: README.tr.md ^-^> "Akilli Uygulama Denetimi"
echo  ================================================================
echo.

if /i not "%MODE%"=="prompt" exit /b 0

choice /c EH /n /m "  Yine de devam edilsin mi? [E/H] "
if errorlevel 2 exit /b 4
exit /b 0
