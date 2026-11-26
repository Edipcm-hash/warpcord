@echo off
rem ---------------------------------------------------------------
rem  WarpCord splash: 3 seconds of ASCII cat, then back to work.
rem  Called only for a plain double-click (no command-line arguments).
rem ---------------------------------------------------------------
setlocal

cls
echo.
echo.
echo            /\_/\
echo           ( -.- )     zzz...
echo          ~~^> ^^ ^<~~
echo.
call :pause1

cls
echo.
echo.
echo            /\_/\
echo           ( o.o )     miyav!
echo          ~~^> ^^ ^<~~
echo.
call :pause1

cls
echo.
echo.
echo            /\_/\
echo           ( O.O )     WarpCord baslatiliyor...
echo          ~~^> ^^ ^<~~
echo.
call :pause1

cls
exit /b 0

:pause1
rem timeout is exact but refuses redirected input; ping is the fallback.
timeout /t 1 /nobreak >nul 2>&1
if errorlevel 1 ping -n 2 127.0.0.1 >nul 2>&1
exit /b 0
