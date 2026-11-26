@echo off
rem ---------------------------------------------------------------
rem  WarpCord splash: 5 seconds of ASCII cat, then back to work.
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
call :wait2

cls
echo.
echo.
echo            /\_/\
echo           ( o.o )     miyav!
echo          ~~^> ^^ ^<~~
echo.
call :wait2

cls
echo.
echo.
echo            /\_/\
echo           ( O.O )     WarpCord baslatiliyor...
echo          ~~^> ^^ ^<~~
echo.
call :wait1

cls
exit /b 0

:wait1
rem timeout is exact but refuses redirected input; ping is the fallback.
timeout /t 1 /nobreak >nul 2>&1
if errorlevel 1 ping -n 2 127.0.0.1 >nul 2>&1
exit /b 0

:wait2
timeout /t 2 /nobreak >nul 2>&1
if errorlevel 1 ping -n 3 127.0.0.1 >nul 2>&1
exit /b 0
